using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordFormatterUI.Models.Profile;
using WordFormatterUI.Services;

namespace WordFormatterUI.Views;

/// <summary>
/// Standalone PDF preview window — singleton per app session.
///
/// Opens a WebView2 that displays a PDF generated on the backend
/// from a Word document formatted with the current profile.
///
/// Features:
///   • Continuous-scroll PDF viewing (via Edge WebView2 built-in)
///   • 100% / fit-width / fit-page zoom presets
///   • Prev / next page navigation
///   • Current-page / total-pages display
///   • Async backend generation with cancellation
///   • ProgressRing loading overlay
///   • Only one instance exists globally; re-open refreshes content
/// </summary>
public sealed partial class PreviewWindow : Window
{
    // ── Singleton ─────────────────────────────────────────────────────

    private static PreviewWindow? _instance;
    private static readonly object _instanceLock = new();

    /// <summary>Get or create the singleton PreviewWindow.</summary>
    public static PreviewWindow GetOrCreate()
    {
        lock (_instanceLock)
        {
            if (_instance is null)
            {
                _instance = new PreviewWindow();
                _instance.Closed += (_, _) =>
                {
                    lock (_instanceLock) { _instance = null; }
                };
            }
            return _instance;
        }
    }

    /// <summary>Whether a PreviewWindow is currently open.</summary>
    public static bool IsOpen => _instance is not null;

    /// <summary>Current PreviewWindow instance, or null if none is open (does not create).</summary>
    public static new PreviewWindow? Current => _instance;

    // ── State ──────────────────────────────────────────────────────────

    private CancellationTokenSource? _pollCts;
    private string? _currentPdfPath;
    private bool _viewerLoaded; // true after viewer.html first loads
    private static bool _pdfjsReady; // true after pdfjs assets copied to LOCALAPPDATA
    private readonly DocumentPreviewService _previewService = new();

    // Shared WebView2 environment — created once, reused by all PreviewWindows
    private static Microsoft.Web.WebView2.Core.CoreWebView2Environment? _wv2Env;
    private static Task? _wv2EnvTask;

    /// <summary>
    /// Pre-initialize the WebView2 environment at app startup.
    /// Call once (fire-and-forget) from App.OnLaunched.
    /// </summary>
    public static async Task WarmUpAsync()
    {
        if (_wv2EnvTask is not null) return;
        _wv2EnvTask = Task.Run(async () =>
        {
            try
            {
                // Set user data folder to LOCALAPPDATA so WebView2 works in Program Files
                var userDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WordFormatter", "WebView2");
                System.IO.Directory.CreateDirectory(userDataDir);
                Environment.SetEnvironmentVariable(
                    "WEBVIEW2_USER_DATA_FOLDER", userDataDir);
                _wv2Env = await Microsoft.Web.WebView2.Core
                    .CoreWebView2Environment.CreateAsync();
            }
            catch { /* WebView2 runtime not available — will init on demand */ }
        });
        await Task.CompletedTask;
    }

    // ── Constructor ────────────────────────────────────────────────────

    public PreviewWindow()
    {
        InitializeComponent();

        // Custom title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarGrid);

        // Size and position are set in the Loaded handler so we can
        // read the DPI scale (Content.XamlRoot.RasterizationScale).

        ((FrameworkElement)Content).Loaded += OnContentLoaded;
        Closed += OnWindowClosed;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;

        // Desired size in DIPs: 700 × 800, then multiply by scale
        // to get the physical-pixel size that AppWindow.Resize expects.
        int targetW = (int)(700 * scale);
        int targetH = (int)(800 * scale);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(targetW, targetH));

        // Center on screen (DisplayArea.WorkArea is in physical pixels)
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            int cx = (displayArea.WorkArea.Width - targetW) / 2;
            int cy = (displayArea.WorkArea.Height - targetH) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(cx, cy));
        }
    }

    // ── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Start generating and showing a PDF preview for the given file
    /// with the given profile. Can be called while a preview is already
    /// showing — it will cancel the old task and start fresh.
    /// </summary>
    public async Task ShowPreviewAsync(string filePath, ProfileConfigDto profile)
    {
        // Cancel any existing generation
        CancelCurrentTask();

        // Show loading state
        ShowLoading();

        try
        {
            var api = App.Api!;

            // Step 1: Start PDF generation on backend
            var (startResult, startError) = await api.StartPdfPreviewAsync(filePath, profile);
            if (startResult is null || string.IsNullOrWhiteSpace(startResult.TaskId))
            {
                ShowError(startError ?? "无法连接格式化服务。");
                return;
            }

            // Step 2: Poll until complete
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            while (!token.IsCancellationRequested)
            {
                var status = await api.GetPdfPreviewStatusAsync(startResult.TaskId);
                if (status is null)
                {
                    ShowError("无法获取预览状态。");
                    return;
                }

                switch (status.State)
                {
                    case "completed":
                        if (!string.IsNullOrWhiteSpace(status.PreviewPath))
                        {
                            // Backend returns a formatted .docx — convert to PDF
                            // via WPS/Word COM, then load in PDF.js
                            var docxPath = status.PreviewPath;
                            await LoadPdfFromDocxAsync(docxPath);
                        }
                        else
                        {
                            ShowError("预览生成失败，请检查文档内容。");
                        }
                        return;

                    case "error":
                    case "cancelled":
                        ShowError(status.Error ?? "预览生成失败，请检查文档内容。");
                        return;
                }

                try { await Task.Delay(500, token); }
                catch (TaskCanceledException) { return; }
            }
        }
        catch (HttpRequestException)
        {
            ShowError("无法连接格式化服务。");
        }
        catch (TaskCanceledException)
        {
            // Cancelled — polling already handles this via token
        }
        catch (Exception ex)
        {
            ShowError($"预览生成失败：{ex.Message}");
        }
    }

    // ── Loading / PDF / Error display ──────────────────────────────────

    private void ShowLoading()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;
        LoadingText.Text = "正在生成预览……";
        LoadingSubText.Visibility = Visibility.Visible;
        PdfViewer.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
    }

    // ── DOCX → PDF → PDF.js pipeline ─────────────────────────────────

    /// <summary>
    /// Copy pdfjs assets from the install directory to a user-writable
    /// LOCALAPPDATA location so that the preview PDF can live in the same
    /// virtual-host directory as viewer.html (same-origin, no CORS).
    /// </summary>
    private static void CopyPdfjsAssets(string destDir)
    {
        try
        {
            var srcDir = Path.Combine(AppContext.BaseDirectory, "Assets", "pdfjs");
            if (!Directory.Exists(srcDir)) return;
            if (Directory.Exists(destDir)) return; // already copied

            // Copy all files and subdirectories
            void CopyDir(string src, string dest)
            {
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(src))
                {
                    var relPath = Path.GetRelativePath(srcDir, file);
                    var destFile = Path.Combine(destDir, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    File.Copy(file, destFile, overwrite: false);
                }
                foreach (var dir in Directory.GetDirectories(src))
                {
                    CopyDir(dir, Path.Combine(dest, Path.GetFileName(dir)));
                }
            }
            CopyDir(srcDir, destDir);
        }
        catch { } // Non-critical — fallback: try the install dir directly
    }

    private async Task LoadPdfFromDocxAsync(string docxPath)
    {
        try
        {
            LoadingText.Text = "正在生成 PDF……";

            // Convert .docx → PDF via WPS/Word COM
            var pdfPath = await _previewService.ConvertToPdfAsync(docxPath);
            _currentPdfPath = pdfPath;

            // Verify the PDF was actually created and is non-empty
            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 100)
            {
                ShowError("预览生成失败，请检查文档内容。");
                return;
            }

            // Load PDF.js viewer in WebView2
            await LoadPdfInViewerAsync(pdfPath);
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception)
        {
            ShowError("无法打开预览文件。");
        }
    }

    private async Task LoadPdfInViewerAsync(string pdfPath)
    {
        // Use pre-warmed environment if available
        if (_wv2Env is not null)
            await PdfViewer.EnsureCoreWebView2Async(_wv2Env);
        else
            await PdfViewer.EnsureCoreWebView2Async();

        // Single virtual host: copy pdfjs + PDF to LOCALAPPDATA for same-origin access
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WordFormatter");
        var pdfjsDestDir = Path.Combine(appData, "pdfjs");
        var pdfjsWebDir = Path.Combine(pdfjsDestDir, "web");

        // Copy pdfjs assets once (lazy init)
        if (!_pdfjsReady)
        {
            CopyPdfjsAssets(pdfjsDestDir);
            _pdfjsReady = true;
        }

        // Copy preview PDF into same directory as viewer.html for same-origin access
        var destPath = Path.Combine(pdfjsWebDir, "_preview.pdf");
        File.Copy(pdfPath, destPath, overwrite: true);

        PdfViewer.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "pdfjs.local", pdfjsDestDir,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

        if (!_viewerLoaded)
        {
            // Single host, same origin — no CORS issue
            PdfViewer.Source = new Uri(
                "https://pdfjs.local/web/viewer.html?file=_preview.pdf");
            _viewerLoaded = true;

            PdfViewer.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                await Task.Delay(600);
                await SyncPageInfoAsync();
            };
        }
        else
        {
            await ExecutePdfJsAsync(
                "PDFViewerApplication.open({url:'_preview.pdf'})");
            await Task.Delay(400);
            await SyncPageInfoAsync();
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
        LoadingSubText.Visibility = Visibility.Collapsed;
        PdfViewer.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Visible;
    }

    // ── Toolbar ↔ PDF.js JavaScript interop ──────────────────────────

    private async Task ExecutePdfJsAsync(string script)
    {
        try
        {
            if (PdfViewer.CoreWebView2 is not null)
                await PdfViewer.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch { /* ignore if PDF.js not ready */ }
    }

    private async Task SyncPageInfoAsync()
    {
        try
        {
            var page = await PdfViewer.CoreWebView2.ExecuteScriptAsync(
                "PDFViewerApplication.page");
            var total = await PdfViewer.CoreWebView2.ExecuteScriptAsync(
                "PDFViewerApplication.pagesCount");
            if (int.TryParse(page?.Trim('"'), out var p) &&
                int.TryParse(total?.Trim('"'), out var t))
            {
                PageCounter.Text = $"{p}/{t}";
                PrevPageBtn.IsEnabled = p > 1;
                NextPageBtn.IsEnabled = p < t;
            }
        }
        catch { }
    }

    private async void Zoom100_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePdfJsAsync("PDFViewerApplication.pdfViewer.currentScaleValue='auto';PDFViewerApplication.pdfViewer.currentScale=1;");
        await SyncPageInfoAsync();
    }

    private async void FitWidth_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePdfJsAsync("PDFViewerApplication.pdfViewer.currentScaleValue='page-width';");
        await SyncPageInfoAsync();
    }

    private async void FitPage_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePdfJsAsync("PDFViewerApplication.pdfViewer.currentScaleValue='page-fit';");
        await SyncPageInfoAsync();
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePdfJsAsync("PDFViewerApplication.page--;");
        await SyncPageInfoAsync();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePdfJsAsync("PDFViewerApplication.page++;");
        await SyncPageInfoAsync();
    }

    // ── Error / loading display ───────────────────────────────────────

    private void ShowError(string message)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        LoadingText.Text = message;
        LoadingText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.Colors.Red);
        LoadingText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        LoadingSubText.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
    }

    // ── Title bar buttons ──────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_MINIMIZE = 6;

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_MINIMIZE);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped)
        {
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        CancelCurrentTask();
    }

    private void CancelCurrentTask()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }
}

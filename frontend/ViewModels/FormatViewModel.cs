using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WordFormatterUI.Models.Format;
using WordFormatterUI.Models.Profile;
using WordFormatterUI.Models.Templates;
using WordFormatterUI.Services;

namespace WordFormatterUI.ViewModels;

/// <summary>
/// Format task control ViewModel (design-document §7.2).
///
/// Manages template selection, preview, task lifecycle (start/poll/cancel),
/// progress tracking, and post-task result display including retry of
/// failed files.
/// </summary>
public partial class FormatViewModel : ObservableObject
{
    private readonly ApiService _api;

    public FormatViewModel(ApiService api)
    {
        _api = api;
    }

    // ── Loading ──────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    // ── Status ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isRunning;

    // ── Templates ─────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<TemplateDto> _templates = new();

    [ObservableProperty]
    private string _selectedTemplateId = "";

    [ObservableProperty]
    private string _selectedTemplateName = "默认模板";

    /// <summary>
    /// The TemplateDto whose profile should be applied when the user
    /// explicitly selects a new template. Set by FormatControlView
    /// before updating SelectedTemplateId so MainWindow can read it.
    /// </summary>
    public TemplateDto? PendingTemplate { get; set; }

    // ── Output ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _outputDir = "";

    /// <summary>
    /// Current formatting profile to send with format requests (Step 9.3).
    /// Set by MainWindow before starting a format task. When non-null,
    /// takes precedence over the template ID.
    /// </summary>
    public ProfileConfigDto? CurrentProfile { get; set; }

    // ── Progress ──────────────────────────────────────────────────────

    [ObservableProperty]
    private int _progressCurrent;

    [ObservableProperty]
    private int _progressTotal;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _taskId = "";

    // ── Results (post-task) ───────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<FileResultDto> _results = new();

    /// <summary>
    /// 最近一次排版成功生成的文件路径列表，由 Results 计算得出。
    /// 供「打开结果」按钮使用。
    /// 优先使用 OutputPath（后端返回的全路径），回退到 Output（仅文件名）。
    /// </summary>
    public List<string> OutputFiles =>
        Results?
            .Where(r => r.Status == "success" && !string.IsNullOrWhiteSpace(r.Output))
            .Select(r => !string.IsNullOrWhiteSpace(r.OutputPath) ? r.OutputPath : r.Output)
            .ToList() ?? new List<string>();

    /// <summary>是否有排版结果可打开。</summary>
    public bool HasOutputFiles => OutputFiles.Count > 0;

    [ObservableProperty]
    private int _okCount;

    [ObservableProperty]
    private int _failCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private double _elapsedSeconds;

    [ObservableProperty]
    private bool _hasResults;

    /// <summary>True after task completes with at least one failure — enables retry button.</summary>
    [ObservableProperty]
    private bool _hasFailedFiles;

    /// <summary>Paths of files that failed in the last task (for retry).</summary>
    [ObservableProperty]
    private ObservableCollection<string> _failedFilePaths = new();

    // ── Preview ───────────────────────────────────────────────────────

    [ObservableProperty]
    private string _previewText = "";

    [ObservableProperty]
    private bool _showPreview;

    // ── Internal ──────────────────────────────────────────────────────

    private CancellationTokenSource? _pollCts;

    // ═══════════════════════════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════════════════════════

    [RelayCommand]
    public async Task LoadTemplatesAsync()
    {
        // Wait for the backend to be ready before calling the API
        await App.WaitForBackendAsync();

        string? lastError = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var resp = await _api.GetTemplatesAsync();
                if (resp is null)
                {
                    lastError = "API returned null (connection failed)";
                }
                else if (!resp.Success)
                {
                    lastError = $"API error: code={resp.Code}, message={resp.Message}";
                }
                else if (resp.Data is null)
                {
                    lastError = "API returned success but no data";
                }
                else if (resp.Data.Templates.Count == 0)
                {
                    lastError = "API returned empty template list";
                }
                else
                {
                    Templates = new ObservableCollection<TemplateDto>(resp.Data.Templates);

                    // 保留当前选中项（若模板仍存在），否则回退到默认模板。
                    // 不能每次重载都重置为默认 —— 保存新模板后用户的选择必须保持，
                    // 否则下拉框会在用户眼皮底下跳回"默认模板"。
                    var current = !string.IsNullOrEmpty(SelectedTemplateId)
                        ? resp.Data.Templates.FirstOrDefault(t => t.Id == SelectedTemplateId)
                        : null;
                    var def = current ?? resp.Data.Templates.FirstOrDefault(t => t.IsDefault);
                    if (def is not null)
                    {
                        SelectedTemplateId = def.Id;
                        SelectedTemplateName = def.Name;
                    }
                    return; // success — exit
                }
            }
            catch (Exception ex)
            {
                lastError = $"Exception: {ex.GetType().Name}: {ex.Message}";
            }
            if (attempt == 0) await Task.Delay(1000);
        }

        // Both attempts failed — show the actual reason
        StatusMessage = $"模板加载失败: {lastError}";
    }

    [RelayCommand]
    public async Task PreviewAsync()
    {
        IsLoading = true;
        ShowPreview = false;
        PreviewText = "";

        try
        {
            var profile = SelectedTemplateId.Length > 0 ? SelectedTemplateId : "default";
            var text = await _api.GetPreviewAsync(null, profile);
            if (text is not null)
            {
                PreviewText = text;
                ShowPreview = true;
                StatusMessage = "预览已生成";
            }
            else
            {
                StatusMessage = "预览生成失败";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"预览失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StartFormatAsync(IEnumerable<string> files)
    {
        if (!files.Any())
        {
            StatusMessage = "请先添加文件";
            return;
        }

        IsLoading = true;
        IsRunning = true;
        StatusMessage = "正在启动排版任务…";
        Results.Clear();
        HasResults = false;
        HasFailedFiles = false;
        FailedFilePaths.Clear();
        OkCount = 0;
        FailCount = 0;
        SkippedCount = 0;
        ElapsedSeconds = 0;
        ShowPreview = false;

        try
        {
            var req = new FormatStartRequestDto
            {
                Files = files.ToList(),
                OutputDir = OutputDir,
                // Step 9.3: use the shared profile DTO when available (user edits),
                // otherwise fall back to the selected template ID.
                Profile = CurrentProfile ?? (object)(SelectedTemplateId.Length > 0 ? SelectedTemplateId : "default"),
            };

            var result = await _api.StartFormatAsync(req);
            if (result is null || string.IsNullOrEmpty(result.TaskId))
            {
                StatusMessage = "启动失败: 未返回任务 ID";
                IsRunning = false;
                IsLoading = false;
                return;
            }

            TaskId = result.TaskId;
            StatusMessage = $"任务 {TaskId} 已启动";

            // Poll for progress
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = new CancellationTokenSource();
            await PollProgressAsync(_pollCts.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = $"启动失败: {ex.Message}";
            IsRunning = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PollProgressAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, ct);

                var resp = await _api.GetFormatStatusAsync(TaskId);
                if (resp?.Success != true || resp.Data is null) continue;

                var status = resp.Data;
                ProgressCurrent = status.Current;
                ProgressTotal = status.Total;
                ProgressPercent = status.Total > 0
                    ? (double)status.Current / status.Total * 100
                    : 0;

                StatusMessage = $"排版中… {status.Current}/{status.Total}";

                // Terminal states: completed, failed, cancelled
                if (status.State is "completed" or "failed" or "cancelled")
                {
                    await LoadResultAsync();
                    IsRunning = false;
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Retry on transient errors
            }
        }
    }

    private async Task LoadResultAsync()
    {
        try
        {
            var resp = await _api.GetFormatResultAsync(TaskId);
            if (resp?.Success == true && resp.Data is not null)
            {
                var result = resp.Data;
                Results = new ObservableCollection<FileResultDto>(result.Results);
                OnPropertyChanged(nameof(OutputFiles));
                OnPropertyChanged(nameof(HasOutputFiles));
                OkCount = result.Ok;
                FailCount = result.Fail;
                SkippedCount = result.Skipped;
                ElapsedSeconds = result.Elapsed;
                HasResults = true;

                // Collect failed file paths for retry (优先完整路径，回退文件名)
                var failed = result.FailedFiles?
                    .Where(f => f.Status is "error" or "failed")
                    .Select(f => !string.IsNullOrWhiteSpace(f.Path) ? f.Path : f.File)
                    .ToList() ?? new List<string>();
                FailedFilePaths = new ObservableCollection<string>(failed);
                HasFailedFiles = failed.Count > 0;

                StatusMessage = FailCount == 0
                    ? $"全部完成  共 {OkCount} 个文件"
                    : $"完成: {OkCount} 成功，{FailCount} 失败";
            }
            else
            {
                StatusMessage = $"获取结果失败: {resp?.Message ?? "未知错误"}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"获取结果失败: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CancelFormatAsync()
    {
        _pollCts?.Cancel();

        if (!string.IsNullOrEmpty(TaskId))
        {
            try
            {
                await _api.CancelFormatAsync(TaskId);
                StatusMessage = "已请求取消任务";
            }
            catch
            {
                StatusMessage = "取消请求发送失败";
            }
        }

        IsRunning = false;
    }

    [RelayCommand]
    public async Task RetryFailedAsync()
    {
        if (FailedFilePaths.Count == 0)
        {
            StatusMessage = "没有失败文件需要重试";
            return;
        }

        // Start a new task with only the failed files
        await StartFormatAsync(FailedFilePaths.ToList());
    }
}

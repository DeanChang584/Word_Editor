using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WordFormatterUI.Controls;

/// <summary>
/// Bottom status bar — displays operational state, file count,
/// template name, and version.
///
/// Height 32 px  (design-document §8).
///
/// Usage from MainWindow:
///     StatusBarControl.StatusText   = "处理中... (5/10)";
///     StatusBarControl.FileCount    = "文档：23";
///     StatusBarControl.TemplateName = "日常写作模板";
///
/// Or bind via XAML Binding to ViewModel properties.
/// </summary>
public sealed partial class StatusBar : UserControl
{
    // ── Status types (drive dot colour) ────────────────────────────────

    public enum StatusBarState
    {
        /// <summary>System accent colour (default)</summary>
        Ready,
        /// <summary>Processing / scanning</summary>
        Running,
        /// <summary>Task completed</summary>
        Completed,
        /// <summary>Task failed or error</summary>
        Error,
    }

    // ── Dependency Properties ──────────────────────────────────────────

    /// <summary>Main status text (left side).</summary>
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText), typeof(string), typeof(StatusBar),
            new PropertyMetadata("已就绪", OnStatusTextChanged));

    /// <summary>Controls the colour of the status indicator dot.</summary>
    public static readonly DependencyProperty StateKindProperty =
        DependencyProperty.Register(
            nameof(StateKind), typeof(StatusBarState), typeof(StatusBar),
            new PropertyMetadata(StatusBarState.Ready, OnStateKindChanged));

    /// <summary>File count text, e.g. "文档：23". Empty hides the field.</summary>
    public static readonly DependencyProperty FileCountProperty =
        DependencyProperty.Register(
            nameof(FileCount), typeof(string), typeof(StatusBar),
            new PropertyMetadata(string.Empty, OnFileCountChanged));

    /// <summary>Template name shown after the first divider.</summary>
    public static readonly DependencyProperty TemplateNameProperty =
        DependencyProperty.Register(
            nameof(TemplateName), typeof(string), typeof(StatusBar),
            new PropertyMetadata("默认模板", OnTemplateNameChanged));

    /// <summary>Version string (right side).</summary>
    public static readonly DependencyProperty VersionProperty =
        DependencyProperty.Register(
            nameof(Version), typeof(string), typeof(StatusBar),
            new PropertyMetadata("Word Formatter 2.1", OnVersionChanged));

    /// <summary>Current file being processed (right side), e.g. "当前：5/10".</summary>
    public static readonly DependencyProperty CurrentFileProperty =
        DependencyProperty.Register(
            nameof(CurrentFile), typeof(string), typeof(StatusBar),
            new PropertyMetadata(string.Empty, OnCurrentFileChanged));

    // ── Public Properties ──────────────────────────────────────────────

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public StatusBarState StateKind
    {
        get => (StatusBarState)GetValue(StateKindProperty);
        set => SetValue(StateKindProperty, value);
    }

    public string FileCount
    {
        get => (string)GetValue(FileCountProperty);
        set => SetValue(FileCountProperty, value);
    }

    public string TemplateName
    {
        get => (string)GetValue(TemplateNameProperty);
        set => SetValue(TemplateNameProperty, value);
    }

    public string Version
    {
        get => (string)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public string CurrentFile
    {
        get => (string)GetValue(CurrentFileProperty);
        set => SetValue(CurrentFileProperty, value);
    }

    // ── Cached brushes ─────────────────────────────────────────────────

    private static readonly SolidColorBrush _accentBrush = null!;
    private static readonly SolidColorBrush _readyBrush;
    private static readonly SolidColorBrush _runningBrush;
    private static readonly SolidColorBrush _completedBrush;
    private static readonly SolidColorBrush _errorBrush;

    static StatusBar()
    {
        // System accent — resolved once at load time
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        var accentColor = uiSettings.GetColorValue(
            Windows.UI.ViewManagement.UIColorType.Accent);
        _accentBrush   = new SolidColorBrush(accentColor);
        _readyBrush    = _accentBrush;
        _runningBrush  = new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)); // Blue
        _completedBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x13, 0xA1, 0x0E)); // Green
        _errorBrush    = new SolidColorBrush(Color.FromArgb(0xFF, 0xD1, 0x34, 0x38)); // Red
    }

    // ── Constructor ────────────────────────────────────────────────────

    public StatusBar()
    {
        InitializeComponent();
    }

    // ── DP Changed Handlers ────────────────────────────────────────────

    private static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar bar)
            bar.StatusTextBlock.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnStateKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StatusBar bar) return;

        var type = e.NewValue is StatusBarState t ? t : StatusBarState.Ready;
        bar.StatusDot.Fill = type switch
        {
            StatusBarState.Running   => _runningBrush,
            StatusBarState.Completed => _completedBrush,
            StatusBarState.Error     => _errorBrush,
            _                        => _readyBrush,
        };
    }

    private static void OnFileCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StatusBar bar) return;
        var text = e.NewValue as string ?? string.Empty;
        bar.FileCountBlock.Text = text;
    }

    private static void OnTemplateNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar bar)
            bar.TemplateNameBlock.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnVersionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar bar)
            bar.VersionBlock.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnCurrentFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar bar)
            bar.CurrentFileBlock.Text = e.NewValue as string ?? string.Empty;
    }
}

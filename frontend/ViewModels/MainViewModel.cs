using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WordFormatterUI.Controls;
using WordFormatterUI.Models.Profile;
using WordFormatterUI.Services;

namespace WordFormatterUI.ViewModels;

/// <summary>
/// Main window ViewModel — the single DataContext for MainWindow.
/// Aggregates sub-ViewModels (Files/Profile/Format) and exposes
/// window-level properties for the StatusBar and navigation state.
///
/// Step 9.1: initial creation with StatusBar properties synced from
/// sub-VMs. Additional page-level VMs split out in Step 9.2.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ApiService _api;

    public MainViewModel(ApiService api)
    {
        _api = api;

        // Shared profile config — single source of truth for all section VMs
        SharedProfile = new ProfileConfigDto();

        // Core VMs
        FilesVm = new FilesViewModel(api);
        ProfileVm = new ProfileViewModel(api);
        FormatVm = new FormatViewModel(api);

        // Section-specific config VMs (Step 9.2)
        PageSettingsVm = new PageSettingsViewModel();
        BodyStyleVm = new BodyStyleViewModel();
        HeadingStyleVm = new HeadingStyleViewModel();
        HeaderFooterVm = new HeaderFooterViewModel();
        PictureSettingsVm = new PictureSettingsViewModel();
        TableSettingsVm = new TableSettingsViewModel();

        // Template management
        TemplateManagementVm = new TemplateManagementViewModel(api, this);

        // Utility VMs
        HistoryVm = new HistoryViewModel(api);
        SettingsVm = new SettingsViewModel(api);

        // Wire section VMs to the shared DTO (initial load with values)
        LoadProfileToAllVms();

        WireSubVmSync();
        WireDirtyTracking();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Sub-ViewModels (exposed for x:Bind / code-behind access)
    // ═══════════════════════════════════════════════════════════════

    public FilesViewModel FilesVm { get; }
    public ProfileViewModel ProfileVm { get; }
    public FormatViewModel FormatVm { get; }

    // Section config VMs (Step 9.2)
    public PageSettingsViewModel PageSettingsVm { get; }
    public BodyStyleViewModel BodyStyleVm { get; }
    public HeadingStyleViewModel HeadingStyleVm { get; }
    public HeaderFooterViewModel HeaderFooterVm { get; }
    public PictureSettingsViewModel PictureSettingsVm { get; }
    public TableSettingsViewModel TableSettingsVm { get; }

    // Template management
    public TemplateManagementViewModel TemplateManagementVm { get; }

    // Utility VMs
    public HistoryViewModel HistoryVm { get; }
    public SettingsViewModel SettingsVm { get; }

    // ═══════════════════════════════════════════════════════════════
    //  Shared profile config (Step 9.3)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// The single shared <see cref="ProfileConfigDto"/> instance.
    /// All section VMs read/write this DTO on property changes.
    /// Sent to the backend when a format task starts.
    /// </summary>
    public ProfileConfigDto SharedProfile { get; set; }

    /// <summary>
    /// Trigger ProfileRefreshed so section Views push ViewModel values to their UI controls.
    /// </summary>
    public void RefreshAllViews() => ProfileRefreshed?.Invoke();

    /// <summary>Update all section VMs to point to current SharedProfile without overwriting user values.</summary>
    public void SyncProfileToAllVms()
    {
        PageSettingsVm.SetSharedProfile(SharedProfile);
        BodyStyleVm.SetSharedProfile(SharedProfile);
        HeadingStyleVm.SetSharedProfile(SharedProfile);
        HeaderFooterVm.SetSharedProfile(SharedProfile);
        PictureSettingsVm.SetSharedProfile(SharedProfile);
        TableSettingsVm.SetSharedProfile(SharedProfile);

        ProfileRefreshed?.Invoke();
    }

    /// <summary>Set reference AND reload values from DTO (init + template switch only).</summary>
    public void LoadProfileToAllVms()
    {
        PageSettingsVm.LoadSharedProfile(SharedProfile);
        BodyStyleVm.LoadSharedProfile(SharedProfile);
        HeadingStyleVm.LoadSharedProfile(SharedProfile);
        HeaderFooterVm.LoadSharedProfile(SharedProfile);
        PictureSettingsVm.LoadSharedProfile(SharedProfile);
        TableSettingsVm.LoadSharedProfile(SharedProfile);

        ProfileRefreshed?.Invoke();
    }

    /// <summary>
    /// Raised after <see cref="SyncProfileToAllVms"/> completes.
    /// Views subscribe to push refreshed ViewModel values to their UI controls.
    /// </summary>
    public event Action? ProfileRefreshed;

    // ═══════════════════════════════════════════════════════════════
    //  Navigation
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _currentNavIndex;

    [RelayCommand]
    private void Navigate(int index)
    {
        if (index >= 0 && index <= 7)
            CurrentNavIndex = index;
    }

    // ═══════════════════════════════════════════════════════════════
    //  StatusBar properties (synced from sub-VMs)
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _statusText = "已就绪";

    [ObservableProperty]
    private string _fileCountText = "文档：0";

    [ObservableProperty]
    private string _templateName = "默认模板";

    [ObservableProperty]
    private StatusBar.StatusBarState _statusBarState = StatusBar.StatusBarState.Ready;

    // ═══════════════════════════════════════════════════════════════
    //  Global dirty state (replaces per-section IsDirty dialogs)
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isDirty;

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(DirtyStatusText));
        OnPropertyChanged(nameof(CanSave));
        RefreshTemplateName();
    }

    /// <summary>Status text shown in the save bar at the bottom of the config column.</summary>
    public string DirtyStatusText => IsDirty ? "● 有未保存修改" : "✓ 已保存";

    /// <summary>Whether the Save button should be enabled.</summary>
    public bool CanSave => IsDirty;

    // ═══════════════════════════════════════════════════════════════
    //  Format state (synced from FormatVm)
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isFormatting;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private int _okCount;

    [ObservableProperty]
    private int _failCount;

    // ═══════════════════════════════════════════════════════════════
    //  Sub-VM sync — mirror property changes upward
    // ═══════════════════════════════════════════════════════════════

    private void WireSubVmSync()
    {
        // --- FormatVm → MainViewModel ---
        FormatVm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(FormatVm.StatusMessage):
                    StatusText = FormatVm.StatusMessage;
                    break;
                case nameof(FormatVm.IsRunning):
                    IsFormatting = FormatVm.IsRunning;
                    if (!FormatVm.IsRunning)
                        StatusBarState = StatusBar.StatusBarState.Ready;
                    break;
                case nameof(FormatVm.ProgressPercent):
                    ProgressPercent = FormatVm.ProgressPercent;
                    ProgressText = $"{FormatVm.ProgressCurrent}/{FormatVm.ProgressTotal}";
                    break;
                case nameof(FormatVm.HasResults):
                    if (FormatVm.HasResults)
                    {
                        OkCount = FormatVm.OkCount;
                        FailCount = FormatVm.FailCount;
                        StatusText = $"完成  成功:{FormatVm.OkCount}  失败:{FormatVm.FailCount}";
                        StatusBarState = FormatVm.FailCount > 0
                            ? StatusBar.StatusBarState.Error
                            : StatusBar.StatusBarState.Completed;
                    }
                    break;
                case nameof(FormatVm.SelectedTemplateName):
                    RefreshTemplateName();
                    break;
            }
        };

        // --- FilesVm → MainViewModel ---
        FilesVm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(FilesVm.FileCount):
                    FileCountText = $"文档：{FilesVm.FileCount}";
                    break;
                // 让「添加/移除/清空」的失败信息显示到状态栏，
                // 避免任何文件操作静默无反馈。
                case nameof(FilesVm.StatusMessage):
                    if (!string.IsNullOrEmpty(FilesVm.StatusMessage))
                        StatusText = FilesVm.StatusMessage;
                    break;
            }
        };
    }

    /// <summary>
    /// Subscribe to all 6 section VMs' PropertyChanged so that any
    /// user edit sets the global <see cref="IsDirty"/> flag.
    /// </summary>
    private void WireDirtyTracking()
    {
        var subVms = new ObservableObject[] {
            PageSettingsVm, BodyStyleVm, HeadingStyleVm,
            HeaderFooterVm, PictureSettingsVm, TableSettingsVm
        };
        foreach (var vm in subVms)
        {
            vm.PropertyChanged += (_, args) =>
            {
                // Don't fire IsDirty recursively when we clear sub-VM flags
                if (args.PropertyName != nameof(PageSettingsViewModel.IsDirty))
                    IsDirty = true;
            };
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        try
        {
            var ok = await _api.UpdateProfileAsync(SharedProfile);
            if (ok)
            {
                // Sync back to ProfileVm so it stays consistent
                ProfileVm.Profile = SharedProfile;

                // Clear all section dirty flags
                PageSettingsVm.IsDirty = false;
                BodyStyleVm.IsDirty = false;
                HeadingStyleVm.IsDirty = false;
                HeaderFooterVm.IsDirty = false;
                PictureSettingsVm.IsDirty = false;
                TableSettingsVm.IsDirty = false;
                IsDirty = false;
            }
        }
        catch
        {
            // Save failed — dirty state preserved
        }
    }

    [RelayCommand]
    private void ResetProfile()
    {
        // Reset in-memory to a fresh default profile — no backend call
        // needed. The spec requires IsDirty=true after reset so the user
        // must explicitly click "保存配置" to persist.
        SharedProfile = new ProfileConfigDto();
        LoadProfileToAllVms();
        IsDirty = true;
    }

    /// <summary>
    /// Update the template name display. Appends " *" when the
    /// current profile has unsaved modifications (design-doc §7.2).
    /// </summary>
    public void RefreshTemplateName()
    {
        var name = FormatVm.SelectedTemplateName;
        TemplateName = IsDirty ? $"{name} *" : name;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Initialization (called by MainWindow.Page_Loaded)
    // ═══════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        // Wait for backend health check to pass (up to 15s)
        await App.WaitForBackendAsync();

        // Load persisted settings (theme) from backend and apply
        await SettingsVm.InitializeAsync();

        // Use local defaults for the profile (more reliable than backend).
        // The profile is already set up in the constructor via LoadProfileToAllVms()
        // with a fresh ProfileConfigDto containing all proper defaults.
        // Only sync the template name once templates are loaded below.

        await FilesVm.LoadFilesCommand.ExecuteAsync(null);
        await FormatVm.LoadTemplatesCommand.ExecuteAsync(null);
        RefreshTemplateName();

        // Initial load is clean
        IsDirty = false;
    }
}

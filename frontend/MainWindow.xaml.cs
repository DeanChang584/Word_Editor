using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WordFormatterUI.Behaviors;
using WordFormatterUI.Controls;
using WordFormatterUI.Models.Common;
using WordFormatterUI.Services;
using WordFormatterUI.Models.History;
using WordFormatterUI.ViewModels;
using WordFormatterUI.Views;

namespace WordFormatterUI;

public sealed partial class MainWindow : Window
{
    // ── Main ViewModel (DataContext) ────────────────────────────────

    public MainViewModel ViewModel { get; }

    // ── Backward-compat passthrough (keeps x:Bind working) ──────────

    public FilesViewModel FilesVm => ViewModel.FilesVm;
    public ProfileViewModel ProfileVm => ViewModel.ProfileVm;
    public FormatViewModel FormatVm => ViewModel.FormatVm;
    public PageSettingsViewModel PageSettingsVm => ViewModel.PageSettingsVm;
    public BodyStyleViewModel BodyStyleVm => ViewModel.BodyStyleVm;
    public HeadingStyleViewModel HeadingStyleVm => ViewModel.HeadingStyleVm;
    public HeaderFooterViewModel HeaderFooterVm => ViewModel.HeaderFooterVm;
    public PictureSettingsViewModel PictureSettingsVm => ViewModel.PictureSettingsVm;
    public TableSettingsViewModel TableSettingsVm => ViewModel.TableSettingsVm;
    public HistoryViewModel HistoryVm => ViewModel.HistoryVm;
    public SettingsViewModel SettingsVm => ViewModel.SettingsVm;
    public TemplateManagementViewModel TemplateManagementVm => ViewModel.TemplateManagementVm;

    // ── Section panels for navigation ───────────────────────────────

    private readonly FrameworkElement[] _sectionPanels;
    private readonly FrameworkElement[] _sectionTitles;
    private int _currentNavIndex;
    private TableSettingsView? _tableSettingsView;

    public MainWindow()
    {
        InitializeComponent();

        // Force Light theme on the root element immediately (App.MainWindow is not
        // yet assigned during the constructor, so ThemeService.Apply cannot apply yet).
        ((FrameworkElement)Content).RequestedTheme = ElementTheme.Light;

        // Create the main ViewModel and set as DataContext
        var api = App.Api ?? throw new InvalidOperationException("ApiService not initialized");
        ViewModel = new MainViewModel(api);
        ((FrameworkElement)Content).DataContext = ViewModel;

        // Inject SettingsVm into the advanced settings view (theme persistence)
        AdvancedSettingsView.SettingsVm = ViewModel.SettingsVm;

        // Map section panels in order matching nav items.
        // TableSettingsView is created dynamically after Loaded (fix XAML parsing race).
        _sectionPanels = new FrameworkElement[]
        {
            PageSettingsView,
            BodyStyleView,
            HeadingStyleView,
            HeaderFooterView,
            PictureSettingsView,
            null!,  // TableSettingsView placeholder — filled in Loaded handler
            AdvancedSettingsView,
            AboutView,
        };
        _sectionTitles = new FrameworkElement[]
        {
            null!, null!, null!, null!, null!, null!, null!, null!,
        };

        // Wire VM-level events
        FormatVm.PropertyChanged += OnFormatVmPropertyChanged;
        FormatVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FormatVm.SelectedTemplateId))
                ApplySelectedTemplate();
        };

        // Wire FormatControlView events
        FormatControlView.SaveTemplateRequested += OnSaveTemplateRequested;
        FormatControlView.OpenResultRequested += FormatControlView_OpenResultRequested;

        // When a template is deleted from the management panel, refresh the dropdown
        TemplateManagementVm.TemplatesChanged += (_, _) =>
        {
            FormatControlView.RefreshTemplates();
        };

        // When profile is reset/reloaded, push ViewModel values back to UI controls
        ViewModel.ProfileRefreshed += () =>
        {
            PageSettingsView.RefreshUI();
            BodyStyleView.RefreshUI();
            HeadingStyleView.RefreshUI();
            HeaderFooterView.RefreshUI();
            PictureSettingsView.RefreshUI();
            _tableSettingsView?.RefreshUI();
        };

        // Trigger initial refresh now that subscribers are in place.
        // (LoadProfileToAllVms() was called in the ViewModel constructor,
        //  before this event handler was registered, so the event was missed.)
        ViewModel.RefreshAllViews();

        // Register window close → clean shutdown of backend + tray icon
        Closed += async (_, _) =>
        {
            // Remove tray icon first
            App.TrayIcon?.Dispose();
            // Notify backend to exit
            await App.Api.ShutdownBackendAsync();
            // Fully exit the process
            Environment.Exit(0);
        };

        // Title bar + keyboard + delayed TableSettingsView creation
        ((FrameworkElement)Content).Loaded += (_, _) =>
        {
            // Create TableSettingsView after the window is fully loaded,
            // so theme resources and VisualTree are guaranteed ready.
            _tableSettingsView = new TableSettingsView();
            _tableSettingsView.DataContext = ViewModel.TableSettingsVm;
            _tableSettingsView.RefreshUI();
            TableSettingsPlaceholder.Children.Add(_tableSettingsView);
            _sectionPanels[5] = _tableSettingsView;

            SetTitleBar(TitleBarControl.DragRegion);
            var iconPath = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
            var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                (int)(960 * scale), (int)(800 * scale)));
            Content.KeyDown += OnGlobalKeyDown;
        };
    }

    private async Task<string> ShowUnsavedDialogAsync(string context)
    {
        var dialog = new ContentDialog
        {
            Title = "未保存的修改",
            Content = $"是否保存对【{context}】的修改？",
            PrimaryButtonText = "保存",
            SecondaryButtonText = "放弃",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => "save",
            ContentDialogResult.Secondary => "discard",
            _ => "cancel",
        };
    }

    // ── Left Column: NavBar Navigation ─────────────────────────────

    private void NavBarControl_SelectionChanged(object? sender, NavBarSelectionChangedEventArgs e)
    {
        var newIdx = e.Index;
        if (newIdx < 0 || newIdx >= _sectionPanels.Length) return;
        if (newIdx == _currentNavIndex) return;

        // Navigate directly — no dirty-check dialog. The global save bar
        // handles save/reset; the profile stays in SharedProfile across
        // all section switches.
        NavigateToSection(newIdx);
    }

    /// <summary>Navigate to the given section index and update tracking.</summary>
    private void NavigateToSection(int idx)
    {
        if (idx < 0 || idx >= _sectionPanels.Length) return;

        ViewModel.NavigateCommand.Execute(idx);
        _currentNavIndex = idx;

        for (int i = 0; i < _sectionPanels.Length; i++)
        {
            if (_sectionPanels[i] is null) continue;
            var vis = i == idx ? Visibility.Visible : Visibility.Collapsed;
            _sectionPanels[i].Visibility = vis;
            if (_sectionTitles[i] is not null)
                _sectionTitles[i].Visibility = vis;
        }

        // 同步管理 TableSettingsPlaceholder 父容器的可见性
        // 该 Grid 包裹了动态创建的 TableSettingsView，XAML 中初始为 Collapsed
        if (_tableSettingsView is not null)
        {
            TableSettingsPlaceholder.Visibility = (idx == 5)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // "高级设置"(idx=6) 和 "关于软件"(idx=7) 隐藏 Save Bar（无保存/恢复按钮）
        const int aboutIndex = 7;
        const int advancedIndex = 6;
        bool hideSaveBar = idx == aboutIndex || idx == advancedIndex;
        SaveBar.Visibility = hideSaveBar ? Visibility.Collapsed : Visibility.Visible;

        // ── Right column panel switching ────────────────────────────────
        // Advanced Settings: show template management panel, keep 3-column layout
        // About: hide right column, middle column spans full width
        // Other pages: show file management panel, keep 3-column layout
        if (idx == advancedIndex)
        {
            // Advanced Settings → show advanced functions panel in right column
            MiddleColumnGrid.SetValue(Grid.ColumnSpanProperty, 1);
            RightColumnGrid.Visibility = Visibility.Visible;
            RightColumnDivider.Visibility = Visibility.Visible;
            FileManagementPanel.Visibility = Visibility.Collapsed;
            AdvancedFunctionsPanel.Visibility = Visibility.Visible;
        }
        else if (idx == aboutIndex)
        {
            // About → hide right column, middle column full width
            MiddleColumnGrid.SetValue(Grid.ColumnSpanProperty, 2);
            RightColumnGrid.Visibility = Visibility.Collapsed;
            RightColumnDivider.Visibility = Visibility.Collapsed;
        }
        else
        {
            // All other pages → show file management panel
            MiddleColumnGrid.SetValue(Grid.ColumnSpanProperty, 1);
            RightColumnGrid.Visibility = Visibility.Visible;
            RightColumnDivider.Visibility = Visibility.Visible;
            FileManagementPanel.Visibility = Visibility.Visible;
            AdvancedFunctionsPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 响应式左栏宽度四档切换：
    ///   < 800px    → 64px  （仅图标，NavBar 紧凑模式）
    ///   800-1119px → 160px
    ///   1120-1599px → 240px
    ///   ≥ 1600px   → 360px
    /// 仅修改 ColLeft.Width，不碰 MinWidth/MaxWidth（XAML 已设好边界）。
    /// </summary>
    private void UpdateCompactState(double width)
    {
        double newWidth;
        bool isCompact;
        if (width < 800)
        {
            newWidth = 64;
            isCompact = true;
        }
        else if (width < 1120)
        {
            newWidth = 160;
            isCompact = false;
        }
        else if (width < 1600)
        {
            newWidth = 240;
            isCompact = false;
        }
        else
        {
            newWidth = 360;
            isCompact = false;
        }

        // 只在宽度变化时更新，避免不必要的布局触发
        if (Math.Abs(ColLeft.Width.Value - newWidth) < 0.5) return;

        ColLeft.Width = new GridLength(newWidth);
        NavBarControl.IsCompact = isCompact;
        foreach (var s in NavBarControl.GetSelectors())
            s.IsCompact = isCompact;
    }

    private void NavBarControl_CompactModeChanged(object? sender, EventArgs e)
    {
        // 不再使用 NavBar 自检测，由 LayoutRoot.SizeChanged + Page_Loaded 接管
    }

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && NavBarControl.HandleKeyDown(e.Key, ctrl))
            e.Handled = true;
    }

    // ── Page Loaded ─────────────────────────────────────────────────

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        UpdateFileButtonStates();

        // 初始布局完成后，根据内容宽度设置左栏 compact 状态
        UpdateCompactState(LayoutRoot.ActualWidth);
    }

    // ── Profile refresh → refresh all section view UI controls ────────

    /// <summary>
    /// Called after profile reset / template apply / history reuse.
    /// Calls <see cref="RefreshUI"/> on every section view that has one.
    /// </summary>
    private void RefreshAllSectionViews()
    {
        // PageSettingsView doesn't use RefreshUI — it binds directly
        // via x:Bind to PageSettingsViewModel properties.  The other 5
        // views manipulate UI controls programmatically and need refresh.
        BodyStyleView.RefreshUI();
        HeadingStyleView.RefreshUI();
        HeaderFooterView.RefreshUI();
        PictureSettingsView.RefreshUI();
        _tableSettingsView?.RefreshUI();
    }

    // ── LayoutRoot Size Changed ─────────────────────────────────────

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactState(e.NewSize.Width);
    }

    // ── LayoutRoot Pointer Pressed (clear NumberBox selection highlight) ─

    private void LayoutRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 如果点击的目标不在 NumberBox 内部，则清除 NumberBox 的选中高亮
        if (!NumberBoxBehavior.IsInsideNumberBox(e.OriginalSource as DependencyObject))
        {
            NumberBoxBehavior.ClearFocusedNumberBoxSelection();
        }
    }

    // ── Right Column: File Management ───────────────────────────────

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".docx");
        picker.FileTypeFilter.Add(".doc");

        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0)
        {
            var paths = files.Select(f => f.Path).ToList();
            await FilesVm.AddFilesCommand.ExecuteAsync(paths);
        }
        UpdateFileButtonStates();
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            await FilesVm.AddFolderCommand.ExecuteAsync(folder.Path);
        UpdateFileButtonStates();
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = FilesVm.Files.Where(f => f.IsSelected).Select(f => f.Path).ToList();
        if (selected.Count > 0)
            await FilesVm.RemoveSelectedCommand.ExecuteAsync(selected);
        UpdateFileButtonStates();
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        await FilesVm.ClearAllCommand.ExecuteAsync(null);
        UpdateFileButtonStates();
    }

    private void FileCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        // x:Bind TwoWay 绑定推送值到源属性 (IsSelected) 的时机晚于
        // Checked/Unchecked 事件，导致 UpdateSelectionState 计数时
        // IsSelected 尚未更新，始终少算 1。
        // 此处直接从 CheckBox 的 IsChecked 属性读取当前状态并手动同步。
        if (sender is CheckBox checkBox && checkBox.DataContext is FileItemDto fileItem)
        {
            fileItem.IsSelected = checkBox.IsChecked == true;
        }
        UpdateSelectionState();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var file in FilesVm.Files)
            file.IsSelected = true;
        // 强制 ListView 重新绑定以刷新 UI 上的 CheckBox 状态
        FileListView.ItemsSource = null;
        FileListView.ItemsSource = FilesVm.Files;
        UpdateSelectionState();
    }

    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var file in FilesVm.Files)
            file.IsSelected = !file.IsSelected;
        // 强制 ListView 重新绑定以刷新 UI 上的 CheckBox 状态
        FileListView.ItemsSource = null;
        FileListView.ItemsSource = FilesVm.Files;
        UpdateSelectionState();
    }

    private async void RecentFlyout_Opening(object sender, object e)
    {
        await FilesVm.LoadRecentCommand.ExecuteAsync(null);
        RecentFlyout.Items.Clear();

        if (FilesVm.Recent.Count == 0)
        {
            RecentFlyout.Items.Add(new MenuFlyoutItem { Text = "（无最近记录）", IsEnabled = false });
            return;
        }

        foreach (var rec in FilesVm.Recent)
        {
            var item = new MenuFlyoutItem
            {
                Text = rec.Path,
                Icon = new FontIcon
                {
                    Glyph = rec.Type == "folder" ? "\uE8B7" : "\uE8A5",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                },
            };
            var captured = rec;
            item.Click += async (_, _) =>
            {
                if (captured.Type == "folder")
                    await FilesVm.AddFolderCommand.ExecuteAsync(captured.Path);
                else
                    await FilesVm.AddFilesCommand.ExecuteAsync(new[] { captured.Path });
                UpdateFileButtonStates();
            };
            RecentFlyout.Items.Add(item);
        }
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "添加到文件列表";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var filePaths = new List<string>();

            foreach (var item in items)
            {
                if (item is Windows.Storage.StorageFolder folder)
                    await FilesVm.AddFolderCommand.ExecuteAsync(folder.Path);
                else if (item is Windows.Storage.StorageFile file
                    && System.IO.Path.GetExtension(file.Path).ToLowerInvariant() is ".doc" or ".docx")
                    filePaths.Add(file.Path);
            }

            if (filePaths.Count > 0)
                await FilesVm.AddFilesCommand.ExecuteAsync(filePaths);

            UpdateFileButtonStates();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void UpdateFileButtonStates()
    {
        ClearBtn.IsEnabled = FilesVm.HasFiles;
        SelectAllBtn.IsEnabled = FilesVm.Files.Count > 0;
        InvertBtn.IsEnabled = FilesVm.Files.Count > 0;

        FileListView.ItemsSource = null;
        FileListView.ItemsSource = FilesVm.Files;

        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var count = FilesVm.Files.Count(f => f.IsSelected);
        RemoveBtn.IsEnabled = count > 0;
        SelectedCountText.Text = $"已选 {count} 个文件";
    }

    // ── FormatVm → StatusBar (minimal: only running→Ready transition) ──

    private void OnFormatVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // StatusBar now binds to MainViewModel via x:Bind, so this
        // handler only manages the StateKind.Running transition which
        // is triggered by FormatVm.StatusMessage changes.
    }

    // ── Template selection → Profile sync ──────────────────────────

    private async void ApplySelectedTemplate()
    {
        var tmpl = FormatVm.PendingTemplate;
        if (tmpl?.Profile is null)
        {
            FormatVm.PendingTemplate = null;
            return;
        }

        // Guard: if there are unsaved changes, ask user first
        if (ViewModel.IsDirty)
        {
            var choice = await ShowUnsavedDialogAsync("模板切换");
            if (choice == "save")
                await ViewModel.SaveProfileCommand.ExecuteAsync(null);
            else if (choice == "cancel")
            {
                FormatVm.PendingTemplate = null;
                return;
            }
            // "discard": proceed without saving
        }

        // Update shared DTO and push to all section VMs (Step 9.3)
        ViewModel.SharedProfile = tmpl.Profile;
        ViewModel.LoadProfileToAllVms();
        ProfileVm.ApplyTemplateProfile(tmpl.Profile);
        FormatVm.PendingTemplate = null;
        ViewModel.IsDirty = false;
        ViewModel.RefreshTemplateName();
    }

    // ── Right Column: Fixed Bottom Bar Format Operations ────────────

    private void FormatControlView_PreviewRequested(object? sender, EventArgs e)
    {
        // Delegate to the right-bar preview button handler
        RightPreviewBtn_Click(sender!, new RoutedEventArgs());
    }

    private async void RightPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        // ── File selection rules (spec § 情况一/二) ──────────────────

        // Count files in the list
        var fileItems = FilesVm.Files;
        if (fileItems.Count == 0)
        {
            var dialog = new ContentDialog
            {
                Title = "",
                Content = "请先添加文件。",
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        var selected = FilesVm.Files.Where(f => f.IsSelected).ToList();

        if (selected.Count > 1)
        {
            var dialog = new ContentDialog
            {
                Title = "",
                Content = "一次只能预览一个文件。",
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        string previewFile;
        if (selected.Count == 1)
        {
            previewFile = selected[0].Path;
        }
        else if (fileItems.Count == 1)
        {
            previewFile = fileItems[0].Path;
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title = "",
                Content = "请选择一个文件进行预览。",
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        // ── Open PreviewWindow ──────────────────────────────────────
        var window = Views.PreviewWindow.GetOrCreate();
        window.Activate();
        // Force-sync table settings to SharedProfile before preview.
        // (Belt-and-suspenders: the section VM's _sharedProfile may be stale.)
        await window.ShowPreviewAsync(previewFile, ViewModel.SharedProfile);
    }

    private async void RightStartBtn_Click(object sender, RoutedEventArgs e)
    {
        // Use selected files if any, otherwise fall back to all files
        var fileItems = FilesVm.Files;
        var selected = fileItems.Where(f => f.IsSelected).ToList();
        var files = selected.Count > 0
            ? selected.Select(f => f.Path).ToList()
            : fileItems.Select(f => f.Path).ToList();

        if (files.Count == 0)
        {
            ViewModel.StatusText = "请先添加文件";
            return;
        }

        // Auto-save if the profile has unsaved changes (no dialog)
        if (ViewModel.IsDirty)
            await ViewModel.SaveProfileCommand.ExecuteAsync(null);

        // Update FormatVm to use the shared profile (Step 9.3)
        FormatVm.CurrentProfile = ViewModel.SharedProfile;

        await FormatVm.StartFormatCommand.ExecuteAsync(files);
    }

    // ── Save Template ───────────────────────────────────────────────

    private async void OnSaveTemplateRequested(object? sender, EventArgs e)
    {
        var vm = ViewModel;
        var api = App.Api!;

        // Auto-save the profile first so we have the latest configuration
        if (vm.IsDirty)
            await vm.SaveProfileCommand.ExecuteAsync(null);

        // Ask user for a template name
        var dialog = new ContentDialog
        {
            Title = "保存配置为模板",
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var inputBox = new TextBox
        {
            PlaceholderText = "请输入模板名称",
            AcceptsReturn = false,
        };
        dialog.Content = inputBox;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var name = inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            vm.StatusText = "模板名称不能为空";
            return;
        }

        try
        {
            var saved = await api.SaveTemplateAsync(name, vm.SharedProfile);
            if (saved != null)
            {
                vm.StatusText = $"模板「{name}」保存成功";
                // Reload templates and refresh the combo box dropdown
                await FormatVm.LoadTemplatesCommand.ExecuteAsync(null);
                FormatControlView.RefreshTemplates();

                // Also refresh the template management list so newly saved template appears there
                await TemplateManagementVm.LoadTemplatesCommand.ExecuteAsync(null);
            }
            else
            {
                vm.StatusText = "模板保存失败";
            }
        }
        catch (Exception ex)
        {
            vm.StatusText = $"模板保存失败: {ex.Message}";
        }
    }

    // ── Open Result ─────────────────────────────────────────────────

    private async void FormatControlView_OpenResultRequested(object? sender, EventArgs e)
    {
        var outputFiles = FormatVm.OutputFiles;

        if (outputFiles.Count == 0)
        {
            var dialog = new ContentDialog
            {
                Title = "",
                Content = "暂无排版结果，请先执行排版。",
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
            return;
        }

        if (outputFiles.Count == 1)
        {
            // 单文件：/select 选中该文件
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer",
                Arguments = $"/select,\"{outputFiles[0]}\"",
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
        }
        else
        {
            // 多文件：获取所有文件所在目录
            var dirs = outputFiles
                .Select(f => System.IO.Path.GetDirectoryName(f))
                .Where(d => d != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dirs.Count == 1)
            {
                // 所有文件在同一目录 → 直接打开该目录
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer",
                    Arguments = $"\"{dirs[0]}\"",
                    UseShellExecute = false,
                };
                System.Diagnostics.Process.Start(psi);
            }
            else
            {
                // 不同目录 → 打开第一个文件所在目录
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer",
                    Arguments = $"\"{dirs[0]}\"",
                    UseShellExecute = false,
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
    }

    // ── Right Column: Format Operations ─────────────────────────────

    private async void FormatControlView_PickOutputDirRequested(object? sender, EventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            FormatControlView.SetOutputDir(folder.Path);
    }

    private async void FormatControlView_StartFormatRequested(object? sender, EventArgs e)
    {
        // Use selected files if any, otherwise fall back to all files
        var fileItems = FilesVm.Files;
        var selected = fileItems.Where(f => f.IsSelected).ToList();
        var files = selected.Count > 0
            ? selected.Select(f => f.Path).ToList()
            : fileItems.Select(f => f.Path).ToList();

        if (files.Count == 0)
        {
            ViewModel.StatusText = "请先添加文件";
            return;
        }

        // Auto-save if the profile has unsaved changes (no dialog)
        if (ViewModel.IsDirty)
            await ViewModel.SaveProfileCommand.ExecuteAsync(null);

        // Update FormatVm to use the shared profile (Step 9.3)
        FormatVm.CurrentProfile = ViewModel.SharedProfile;

        await FormatVm.StartFormatCommand.ExecuteAsync(files);
    }

    // ── ResultHistoryView events ────────────────────────────────────

    private async void ResultHistoryView_RetrySingleFileRequested(object? sender, string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
            await FormatVm.StartFormatCommand.ExecuteAsync(new[] { filePath });
    }

    private async void ResultHistoryView_ReuseHistoryRequested(object? sender, string recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId)) return;

        // Guard: if there are unsaved changes, ask user first
        if (ViewModel.IsDirty)
        {
            var choice = await ShowUnsavedDialogAsync("历史配置");
            if (choice == "save")
                await ViewModel.SaveProfileCommand.ExecuteAsync(null);
            else if (choice == "cancel")
                return;
            // "discard": proceed without saving
        }

        try
        {
            var api = App.Api!;
            var resp = await api.GetHistoryDetailAsync(recordId);
            if (resp?.Success != true || resp.Data is null)
            {
                ViewModel.StatusText = "无法加载历史记录详情";
                return;
            }

            var detail = resp.Data;

            if (detail.Profile is not null)
            {
                ViewModel.SharedProfile = detail.Profile;
                ViewModel.LoadProfileToAllVms();
                ProfileVm.ApplyTemplateProfile(detail.Profile);
            }

            if (detail.Files is { Count: > 0 })
            {
                await FilesVm.AddFilesCommand.ExecuteAsync(detail.Files.Select(f => f.Path));
                UpdateFileButtonStates();
            }

            if (detail.Results?.OutputDir is { Length: > 0 } dir)
            {
                FormatVm.OutputDir = dir;
                FormatControlView.SetOutputDir(dir);
            }

            ViewModel.IsDirty = false;
            ViewModel.RefreshTemplateName();
            ViewModel.StatusText = $"已加载历史任务: {detail.Template}";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"加载历史失败: {ex.Message}";
        }
    }
}
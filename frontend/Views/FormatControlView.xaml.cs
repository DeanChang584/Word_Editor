using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordFormatterUI.Models.Templates;
using WordFormatterUI.ViewModels;

namespace WordFormatterUI.Views
{
    /// <summary>
    /// Format task control card (design-document §7.2).
    ///
    /// Self-contained View that handles all format-task UI logic:
    /// template selection, output directory, preview, start/cancel,
    /// progress display, and post-task result summary with retry.
    ///
    /// Binds to <see cref="FormatViewModel"/> via DataContext.
    /// Raises events for operations that need the parent Window handle
    /// (folder picker) or cross-VM coordination (file list access).
    /// </summary>
    public sealed partial class FormatControlView : UserControl
    {
        // ── Events (raised for parent Window to handle) ─────────────────

        /// <summary>Raised when the user clicks "选择" for output directory.</summary>
        public event EventHandler? PickOutputDirRequested;

        /// <summary>Raised when the user clicks "开始排版". Parent should gather
        /// file paths and call <see cref="FormatViewModel.StartFormatAsync"/>.</summary>
        public event EventHandler? StartFormatRequested;

        /// <summary>Raised when the user clicks "保存配置为模板".</summary>
        public event EventHandler? SaveTemplateRequested;

        /// <summary>Raised when the user clicks "预览效果". Parent validates file
        /// selection and opens PreviewWindow with PDF preview.</summary>
        public event EventHandler? PreviewRequested;

        /// <summary>Raised when the user clicks "打开结果". Parent opens the
        /// output file(s) in Windows Explorer.</summary>
        public event EventHandler? OpenResultRequested;

        // ── ShowActionButtons property ──────────────────────────────────

        /// <summary>
        /// Whether to show the action buttons (Preview/Start/Cancel) inside this control.
        /// When false, only the fixed bottom bar in MainWindow shows them.
        /// </summary>
        public bool ShowActionButtons
        {
            get => ActionButtonsPanel.Visibility == Visibility.Visible;
            set => ActionButtonsPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Constructor ──────────────────────────────────────────────────

        public FormatControlView()
        {
            InitializeComponent();
            PreviewBtn.Content = "预览效果";
            Loaded += OnLoaded;
        }

        // ── Load ─────────────────────────────────────────────────────────

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var vm = GetVm();
            if (vm == null) return;

            await App.WaitForBackendAsync();
            await vm.LoadTemplatesCommand.ExecuteAsync(null);
            PopulateTemplateBox(vm);

            // Sync output dir from VM
            if (!string.IsNullOrWhiteSpace(vm.OutputDir))
                OutputDirBox.Text = vm.OutputDir;

            // Subscribe to VM property changes
            vm.PropertyChanged += (_, args) =>
            {
                DispatcherQueue.TryEnqueue(() => OnVmPropertyChanged(args.PropertyName));
            };

            // Initial state
            RefreshButtonStates();
        }

        // ── Template selection ───────────────────────────────────────────

        private bool _isPopulating; // guards against mutating Items inside SelectionChanged

        /// <summary>
        /// Rebuild the ComboBox items from the VM's template list, then sync
        /// the current selection. 仅用于模板集合变化（首次加载/保存/删除）——
        /// 这些时机下拉 Popup 必然已关闭，重建 Items 是安全的。
        /// </summary>
        private void PopulateTemplateBox(FormatViewModel vm)
        {
            if (_isPopulating) return;
            _isPopulating = true;
            try
            {
                TemplateBox.Items.Clear();
                foreach (var t in vm.Templates)
                {
                    TemplateBox.Items.Add(new ComboBoxItem
                    {
                        Content = t.Name,
                        Tag = t.Id,
                    });
                }
            }
            finally
            {
                _isPopulating = false;
            }
            SyncTemplateSelection(vm);
        }

        /// <summary>
        /// 仅同步 ComboBox 的选中项，不触碰 Items 集合。
        /// 用于 SelectedTemplateId 被外部（如模板管理面板 SwitchTemplateAsync）
        /// 程序化切换时的下拉同步；用户点选路径由 SelectionChanged 自行处理。
        ///
        /// 注意：绝不能在这里重建 Items。用户点选模板时，下拉 Popup 的关闭
        /// 动画仍在进行，此时 Items.Clear() 会销毁 Popup 仍在引用的
        /// ItemContainer，抛出 COMException 0x80070490 "找不到元素"，
        /// 经 XAML stowed exception 直接终止进程（实测必崩，startup.log 11:14:57）。
        /// </summary>
        private void SyncTemplateSelection(FormatViewModel vm)
        {
            if (_isPopulating) return;
            _isPopulating = true;
            try
            {
                if (string.IsNullOrEmpty(vm.SelectedTemplateId))
                {
                    TemplateBox.SelectedIndex = -1;
                    return;
                }

                for (int i = 0; i < TemplateBox.Items.Count; i++)
                {
                    if (TemplateBox.Items[i] is ComboBoxItem item
                        && item.Tag is string id
                        && id == vm.SelectedTemplateId)
                    {
                        TemplateBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            finally
            {
                _isPopulating = false;
            }
        }

        private void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVm();
            if (vm == null) return;

            SaveTemplateRequested?.Invoke(this, EventArgs.Empty);
        }

        private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Programmatic population (PopulateTemplateBox) re-selects the current
            // template — treat that as a UI sync, not a user action.
            if (_isPopulating) return;

            var vm = GetVm();
            if (vm == null || TemplateBox.SelectedItem is not ComboBoxItem item || item.Tag is not string id)
                return;

            // Find the selected template DTO
            var template = vm.Templates.FirstOrDefault(t => t.Id == id);
            if (template != null)
            {
                vm.PendingTemplate = template;
                vm.SelectedTemplateId = id;
                vm.SelectedTemplateName = template.Name;
            }
        }

        // ── Output directory ─────────────────────────────────────────────

        private void OutputDirBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var vm = GetVm();
            if (vm != null)
                vm.OutputDir = OutputDirBox.Text;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            PickOutputDirRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Called by MainWindow after folder picker returns a path.</summary>
        public void SetOutputDir(string path)
        {
            OutputDirBox.Text = path;
            var vm = GetVm();
            if (vm != null) vm.OutputDir = path;
        }

        // ── Preview ──────────────────────────────────────────────────────

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            // Forward to parent MainWindow, which validates file selection
            // and opens the standalone PreviewWindow with PDF preview.
            PreviewRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenResult_Click(object sender, RoutedEventArgs e)
        {
            OpenResultRequested?.Invoke(this, EventArgs.Empty);
        }

        // ── Public API (for fixed bottom bar in MainWindow) ──────────────

        /// <summary>Refresh the template dropdown from the ViewModel (used after save/delete).</summary>
        public void RefreshTemplates()
        {
            var vm = GetVm();
            if (vm != null)
                PopulateTemplateBox(vm);
        }

        /// <summary>Trigger preview programmatically (used by fixed bottom bar).</summary>
        public void Preview() => PreviewRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Trigger start format programmatically (used by fixed bottom bar).</summary>
        public void StartFormat() => StartFormatRequested?.Invoke(this, EventArgs.Empty);

        // ── Start / Cancel ───────────────────────────────────────────────

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            StartFormatRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetVm();
            if (vm != null)
                await vm.CancelFormatCommand.ExecuteAsync(null);
        }

        // ── VM property change → UI updates ──────────────────────────────

        private void OnVmPropertyChanged(string? propertyName)
        {
            var vm = GetVm();
            if (vm == null) return;

            switch (propertyName)
            {
                case nameof(vm.IsRunning):
                    RefreshButtonStates();
                    ProgressSection.Visibility = vm.IsRunning ? Visibility.Visible : Visibility.Collapsed;
                    break;

                case nameof(vm.ProgressPercent):
                    ProgressBar.Value = vm.ProgressPercent;
                    ProgressText.Text = $"处理中: {vm.ProgressCurrent}/{vm.ProgressTotal}";
                    break;

                case nameof(vm.Templates):
                    // 模板集合变化（首次加载/保存/删除）→ 重建下拉 Items。
                    // 这些时机 Popup 必然已关闭，重建安全。
                    PopulateTemplateBox(vm);
                    break;

                case nameof(vm.SelectedTemplateId):
                    // 只同步选中项，不重建 Items（见 SyncTemplateSelection 注释：
                    // 下拉关闭动画期间重建会触发 COMException 0x80070490 崩溃）。
                    SyncTemplateSelection(vm);
                    break;
            }
        }

        private void RefreshButtonStates()
        {
            var vm = GetVm();
            if (vm == null) return;

            var running = vm.IsRunning;
            StartBtn.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            StartBtn.IsEnabled = !running;
            PreviewBtn.IsEnabled = !running;

            CancelBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            CancelBtn.IsEnabled = running;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private FormatViewModel? GetVm()
        {
            if (ViewRoot.DataContext is FormatViewModel direct)
                return direct;

            DependencyObject? current = ViewRoot;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is FormatViewModel vm)
                    return vm;
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
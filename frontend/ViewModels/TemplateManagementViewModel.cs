using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WordFormatterUI.Models.Profile;
using WordFormatterUI.Models.Templates;
using WordFormatterUI.Services;

namespace WordFormatterUI.ViewModels;

    /// <summary>
    /// ViewModel for template management panel.
    /// Provides a simple list of templates with delete, import, export operations.
    /// Clicking a template name switches the active template.
    /// </summary>
    public partial class TemplateManagementViewModel : ObservableObject
    {
        /// <summary>
        /// Raised after a template is deleted, so subscribers (e.g. FormatControlView)
        /// can refresh their template dropdown.
        /// </summary>
        public event EventHandler? TemplatesChanged;

        private readonly ApiService _api;
        private readonly MainViewModel _mainVm;

        public TemplateManagementViewModel(ApiService api, MainViewModel mainVm)
    {
        _api = api;
        _mainVm = mainVm;
        _templates = new ObservableCollection<TemplateDto>();
    }

    [ObservableProperty]
    private ObservableCollection<TemplateDto> _templates;

    [ObservableProperty]
    private string _currentTemplateId = "";

    [ObservableProperty]
    private string _currentTemplateName = "默认模板";

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Load the template list from the backend.
    /// </summary>
    [RelayCommand]
    public async Task LoadTemplatesAsync()
    {
        await App.WaitForBackendAsync();
        IsLoading = true;
        try
        {
            var resp = await _api.GetTemplatesAsync();
            if (resp?.Success == true && resp.Data is not null)
            {
                Templates = new ObservableCollection<TemplateDto>(resp.Data.Templates);

                // Maintain current selection if still present
                if (!string.IsNullOrEmpty(CurrentTemplateId))
                {
                    var existing = Templates.FirstOrDefault(t => t.Id == CurrentTemplateId);
                    if (existing is null)
                    {
                        // Fall back to default
                        var def = Templates.FirstOrDefault(t => t.IsDefault) ?? Templates.FirstOrDefault();
                        if (def is not null)
                        {
                            CurrentTemplateId = def.Id;
                            CurrentTemplateName = def.Name;
                        }
                    }
                }
                else
                {
                    var def = Templates.FirstOrDefault(t => t.IsDefault) ?? Templates.FirstOrDefault();
                    if (def is not null)
                    {
                        CurrentTemplateId = def.Id;
                        CurrentTemplateName = def.Name;
                    }
                }
            }
        }
        catch
        {
            // Silently handle — templates list remains as-is
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Switch the active template to the selected one, then reload profile.
    /// </summary>
    [RelayCommand]
    public async Task SwitchTemplateAsync(TemplateDto? template)
    {
        if (template is null) return;

        CurrentTemplateId = template.Id;
        CurrentTemplateName = template.Name;

        // Update FormatVM's template selection and reload profile
        _mainVm.FormatVm.SelectedTemplateId = template.Id;
        _mainVm.FormatVm.SelectedTemplateName = template.Name;
        _mainVm.TemplateName = template.Name;

        // Apply the template's own profile (included in the list since it now
        // carries profile data). Fall back to loading the saved profile.
        if (template.Profile is not null)
        {
            _mainVm.SharedProfile = template.Profile;
            _mainVm.SyncProfileToAllVms();
        }
        else
        {
            await _mainVm.ProfileVm.LoadProfileCommand.ExecuteAsync(null);
            if (_mainVm.ProfileVm.Profile is not null)
            {
                _mainVm.SharedProfile = _mainVm.ProfileVm.Profile;
                _mainVm.SyncProfileToAllVms();
            }
        }

        _mainVm.IsDirty = false;
    }

    /// <summary>
    /// Delete a template after confirmation. Default templates cannot be deleted.
    /// </summary>
    [RelayCommand]
    private async Task DeleteTemplateAsync(TemplateDto? template)
    {
        if (template is null || template.IsDefault) return;

        // Confirm deletion
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "删除模板",
            Content = $"确定要删除模板「{template.Name}」吗？\n此操作不可恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            XamlRoot = GetXamlRoot()
        };

        var result = await dialog.ShowAsync();
        if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;

        var ok = await _api.DeleteTemplateAsync(template.Id);
        if (ok)
        {
            Templates.Remove(template);

            // Refresh FormatVM's template list so the dropdown stays in sync
            await _mainVm.FormatVm.LoadTemplatesCommand.ExecuteAsync(null);
            TemplatesChanged?.Invoke(this, EventArgs.Empty);

            // If the deleted template was active, switch to default
            if (CurrentTemplateId == template.Id)
            {
                var def = Templates.FirstOrDefault(t => t.IsDefault) ?? Templates.FirstOrDefault();
                if (def is not null)
                    await SwitchTemplateAsync(def);
            }
        }
    }

    /// <summary>
    /// Import a template from a .json file.
    /// </summary>
    [RelayCommand]
    private async Task ImportTemplateAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var imported = await _api.ImportTemplateAsync(file.Path);
        if (imported is not null)
        {
            // Refresh the list
            await LoadTemplatesAsync();
        }
    }

    /// <summary>
    /// Export the currently active template to a .json file.
    /// </summary>
    [RelayCommand]
    private async Task ExportTemplateAsync()
    {
        if (string.IsNullOrEmpty(CurrentTemplateId)) return;

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{CurrentTemplateName}.json"
        };
        picker.FileTypeChoices.Add("JSON 模板文件", new List<string> { ".json" });

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await _api.ExportTemplateAsync(CurrentTemplateId, file.Path);
    }

    private Microsoft.UI.Xaml.XamlRoot? GetXamlRoot()
    {
        if (App.MainWindow is Microsoft.UI.Xaml.Window window)
        {
            return window.Content.XamlRoot;
        }
        return null;
    }
}
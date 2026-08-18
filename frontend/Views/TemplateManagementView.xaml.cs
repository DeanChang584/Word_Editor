using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WordFormatterUI.Models.Templates;
using WordFormatterUI.ViewModels;

namespace WordFormatterUI.Views;

/// <summary>
/// Template management panel — the only advanced settings right-column view.
///
/// Layout:
///   "模板管理" title
///   Template list box (Border + ScrollViewer, 240px height)
///     Each row: template name | delete button (× / 🗑)
///   [导入模板] [导出模板] buttons at bottom
/// </summary>
public sealed partial class TemplateManagementView : UserControl
{
    private TemplateManagementViewModel? ViewModel => DataContext as TemplateManagementViewModel;

    public TemplateManagementView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshTemplateList();
    }

    /// <summary>
    /// Refresh the template list from the ViewModel.
    /// </summary>
    public async Task RefreshTemplateList()
    {
        var vm = ViewModel;
        if (vm == null) return;

        await vm.LoadTemplatesCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Called when a template name/row is clicked (Tapped).
    /// Switches the active template.
    /// </summary>
    private async void OnTemplateItemClick(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not TemplateDto template) return;
        if (ViewModel == null) return;

        await ViewModel.SwitchTemplateCommand.ExecuteAsync(template);
    }

    /// <summary>
    /// Called when the delete button (🗑) on a template row is clicked.
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not TemplateDto template) return;
        if (ViewModel == null) return;

        await ViewModel.DeleteTemplateCommand.ExecuteAsync(template);
    }

    /// <summary>
    /// Import template button click handler.
    /// </summary>
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        await ViewModel.ImportTemplateCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Export template button click handler.
    /// </summary>
    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        await ViewModel.ExportTemplateCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Delete button visibility — hidden for default templates.
    /// </summary>
    private Visibility DeleteButtonVisibility(bool isDefault)
    {
        return isDefault ? Visibility.Collapsed : Visibility.Visible;
    }
}

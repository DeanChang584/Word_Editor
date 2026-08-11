using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordFormatterUI.Services;
using WordFormatterUI.ViewModels;

namespace WordFormatterUI.Views;

/// <summary>
/// Advanced application settings (plan §7.7).
/// Language, theme (via <see cref="ThemeService"/>), and auto-check-update.
/// Theme is persisted through <see cref="SettingsViewModel"/>.
/// </summary>
public sealed partial class AdvancedSettingsView : UserControl
{
    // Guard against firing change handlers while pushing initial state
    private bool _isLoading;

    /// <summary>Injected by MainWindow; drives theme persistence.</summary>
    public SettingsViewModel? SettingsVm { get; set; }

    public AdvancedSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // Language (default zh-CN — no persistence yet)
        LanguageBox.SelectedIndex = 0;

        // Theme — select the item matching the current mode
        var mode = ThemeService.CurrentMode;
        for (int i = 0; i < ThemeBox.Items.Count; i++)
        {
            if (ThemeBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == mode)
            {
                ThemeBox.SelectedIndex = i;
                break;
            }
        }

        _isLoading = false;
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        // No-op until backend /api/settings + i18n resource swapping is available.
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            if (SettingsVm is not null)
                SettingsVm.Theme = mode;
            else
                ThemeService.Apply(mode);   // fallback — apply directly
        }
    }
}

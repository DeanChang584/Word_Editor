using CommunityToolkit.Mvvm.ComponentModel;
using WordFormatterUI.Services;

namespace WordFormatterUI.ViewModels;

/// <summary>
/// Global settings ViewModel (design-document §17).
/// Theme changes apply immediately (ThemeService) and persist to the
/// backend settings.json (fire-and-forget PUT).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ApiService _api;

    public SettingsViewModel(ApiService api)
    {
        _api = api;
    }

    [ObservableProperty] private string _language = "zh-CN";
    [ObservableProperty] private bool _autoCheckUpdate = true;

    [ObservableProperty] private string _theme = "light";

    // Set once the user actively changes the theme — startup load must not
    // overwrite a selection the user made while settings were still loading.
    private bool _userChangedTheme;

    partial void OnThemeChanged(string value)
    {
        _userChangedTheme = true;
        // Apply immediately; persist without blocking the UI thread.
        ThemeService.Apply(value);
        _ = PersistThemeAsync(value);
    }

    private async Task PersistThemeAsync(string theme)
    {
        try
        {
            await _api.UpdateSettingsAsync(theme);
        }
        catch
        {
            // Persistence failure is non-fatal — theme stays applied in-session.
        }
    }

    /// <summary>Load settings from the backend once (startup). Failure keeps defaults.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Respect a theme the user already picked while loading.
            if (_userChangedTheme) return;

            var settings = await _api.GetSettingsAsync();
            if (settings is null) return;
            if (settings.Theme is "light" or "dark" or "system")
            {
                _theme = settings.Theme;          // bypass OnThemeChanged: no Apply/PUT on load
                OnPropertyChanged(nameof(Theme));
                ThemeService.Apply(settings.Theme);
            }
        }
        catch
        {
            // Keep default light.
        }
    }
}

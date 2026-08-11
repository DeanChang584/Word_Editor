using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace WordFormatterUI.Services;

/// <summary>
/// Local theme management — light / dark / system.
/// "system" follows the OS theme and reacts to live changes via
/// <see cref="UISettings.ColorValuesChanged"/>.
/// Also keeps <see cref="Application.RequestedTheme"/> in sync so that
/// code-side resource lookups (Application.Current.Resources[...])
/// resolve per the active theme.
/// </summary>
public static class ThemeService
{
    public static string CurrentMode { get; private set; } = "light";

    private static UISettings? _uiSettings;

    public static void Apply(string mode)
    {
        if (mode is not ("light" or "dark" or "system"))
            mode = "light";
        CurrentMode = mode;

        var theme = mode switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        ApplyToWindow(App.MainWindow, theme);
        ApplyToWindow(Views.PreviewWindow.Current, theme);

        SyncApplicationTheme();
        EnsureSystemWatcher();
    }

    /// <summary>
    /// Application-level theme drives code-side lookups such as
    /// <c>Application.Current.Resources["TextFillColorPrimaryBrush"]</c>.
    /// Resolve it from the active mode so those lookups match the window.
    /// </summary>
    private static void SyncApplicationTheme()
    {
        try
        {
            Application.Current.RequestedTheme = CurrentMode switch
            {
                "dark" => ApplicationTheme.Dark,
                "light" => ApplicationTheme.Light,
                _ => ResolveSystemTheme(),
            };
        }
        catch
        {
            // Safe to ignore during shutdown or rapid startup.
        }
    }

    /// <summary>Map the OS theme to an ApplicationTheme via background luminance.</summary>
    private static ApplicationTheme ResolveSystemTheme()
    {
        try
        {
            var background = new UISettings().GetColorValue(UIColorType.Background);
            var luminance = (background.R + background.G + background.B) / 3.0;
            return luminance < 128 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        catch
        {
            return ApplicationTheme.Light;
        }
    }

    private static void ApplyToWindow(Window? window, ElementTheme theme)
    {
        try
        {
            if (window?.Content is FrameworkElement root)
                root.RequestedTheme = theme;
        }
        catch
        {
            // COM exceptions may occur during shutdown or rapid startup; safe to ignore.
        }
    }

    private static void EnsureSystemWatcher()
    {
        if (_uiSettings is not null) return;
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            // ColorValuesChanged fires on a background thread; marshal back to UI.
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (CurrentMode == "system")
                {
                    SyncApplicationTheme();
                    ApplyToWindow(App.MainWindow, ElementTheme.Default);
                    ApplyToWindow(Views.PreviewWindow.Current, ElementTheme.Default);
                }
            });
        };
    }
}

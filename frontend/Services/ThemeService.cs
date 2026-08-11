using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace WordFormatterUI.Services;

/// <summary>
/// Local theme management — light / dark / system.
/// "system" follows the OS theme and reacts to live changes via
/// <see cref="UISettings.ColorValuesChanged"/>.
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

        EnsureSystemWatcher();
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
                    ApplyToWindow(App.MainWindow, ElementTheme.Default);
            });
        };
    }
}

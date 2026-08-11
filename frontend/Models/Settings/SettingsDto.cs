namespace WordFormatterUI.Models.Settings;

/// <summary>
/// Application-wide settings — mirrors shared/schemas.py Settings.
/// Independent of formatting profiles.
/// </summary>
public class SettingsDto
{
    public string Theme { get; set; } = "light";
    public string Language { get; set; } = "zh-CN";
    public string DefaultOutput { get; set; } = "sameFolder";
    public string DefaultTemplate { get; set; } = "Default";
    public int RecentCount { get; set; } = 20;
    public bool AutoCheckUpdate { get; set; } = true;
}

public class SettingsResponseDto
{
    public SettingsDto? Settings { get; set; }
}

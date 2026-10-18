namespace Wiina.Core.Settings;

/// <summary>
/// Shell-level window bounds. Kept in a partial file so feature work can add
/// settings without touching the large serialized settings class.
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>Settings window size in DIPs; default is one step larger than v1.</summary>
    public double SettingsWindowWidth { get; set; } = 760;

    public double SettingsWindowHeight { get; set; } = 600;

    /// <summary>Physical screen position; int.MinValue means "let Windows place it".</summary>
    public int SettingsWindowX { get; set; } = int.MinValue;

    public int SettingsWindowY { get; set; } = int.MinValue;
}

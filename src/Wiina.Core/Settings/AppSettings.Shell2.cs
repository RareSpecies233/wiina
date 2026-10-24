namespace Wiina.Core.Settings;

/// <summary>
/// Bounds for the secondary shell windows added after v1: the welcome launcher
/// and the standalone key-binding editor. Persisted separately from the large
/// settings class so feature work stays additive.
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>Welcome window size in DIPs (default 560x440, minimum 480x360).</summary>
    public double WelcomeWindowWidth { get; set; } = 560;

    public double WelcomeWindowHeight { get; set; } = 440;

    /// <summary>Physical screen position; int.MinValue means "let Windows place it".</summary>
    public int WelcomeWindowX { get; set; } = int.MinValue;

    public int WelcomeWindowY { get; set; } = int.MinValue;

    public double KeyBindingWindowWidth { get; set; } = 880;

    public double KeyBindingWindowHeight { get; set; } = 660;

    public int KeyBindingWindowX { get; set; } = int.MinValue;

    public int KeyBindingWindowY { get; set; } = int.MinValue;

    /// <summary>Snap a dragged player window to a work-area edge (macOS style).</summary>
    public bool EdgeSnapping { get; set; } = true;

    /// <summary>Show raw mpv playback statistics as an on-screen overlay.</summary>
    public bool ShowMpvStats { get; set; }

    /// <summary>Warn with an OSD message whenever dropped video frames increase.</summary>
    public bool FrameDropOsd { get; set; } = true;
}

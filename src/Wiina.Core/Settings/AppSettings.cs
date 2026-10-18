using Wiina.Core.Models;

namespace Wiina.Core.Settings;

/// <summary>
/// User settings persisted to %LOCALAPPDATA%\Wiina\settings.json.
/// Stage 1 uses only the window/OSC/theme subset; later stages add more.
/// </summary>
public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    public WindowLayoutMode Layout { get; set; } = WindowLayoutMode.EdgeToEdge;

    public OscPosition OscPosition { get; set; } = OscPosition.Floating;

    public SidebarSide SidebarSide { get; set; } = SidebarSide.Trailing;

    public double SidebarWidth { get; set; } = 360;

    public bool AlwaysOnTop { get; set; }

    public bool LockAspectRatio { get; set; } = true;

    /// <summary>Floating OSC centre X as a 0..1 fraction of the video area.</summary>
    public double OscX { get; set; } = 0.5;

    /// <summary>Floating OSC centre Y as a 0..1 fraction of the video area.</summary>
    public double OscY { get; set; } = 0.85;

    public int WindowWidth { get; set; } = 1280;

    public int WindowHeight { get; set; } = 720;

    public int WindowX { get; set; } = int.MinValue;

    public int WindowY { get; set; } = int.MinValue;
}

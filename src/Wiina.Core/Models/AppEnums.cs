namespace Wiina.Core.Models;

public enum ThemeMode
{
    System,
    Dark,
    Light,
}

public enum WindowLayoutMode
{
    EdgeToEdge,
    Docked,
}

public enum OscPosition
{
    Floating,
    Bottom,
    Top,
}

public enum SidebarSide
{
    Leading,
    Trailing,
}

/// <summary>Where the Stage 5 OSD sits inside the playback window.</summary>
public enum OsdPosition
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
    TopCenter,
    BottomCenter,
}

public enum SidebarType
{
    Settings,
    Playlist,
    Chapters,
    Plugins,
}

/// <summary>What a single left click on the video area does.</summary>
public enum SingleClickAction
{
    HideOsc,
    TogglePlayPause,
    Fullscreen,
    None,
}

/// <summary>
/// Window presentation modes owned by <see cref="State.ModeStateMachine"/>.
/// Exactly one mode is active at a time (mutual exclusion).
/// </summary>
public enum PlayerMode
{
    Normal,
    Fullscreen,
    PiP,
    Mini,
}

/// <summary>Transport command routed from SMTC / media keys to a window.</summary>
public enum MediaCommand
{
    Play,
    Pause,
    TogglePlayPause,
    Stop,
    Next,
    Previous,
    Seek,
}

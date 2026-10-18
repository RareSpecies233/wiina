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

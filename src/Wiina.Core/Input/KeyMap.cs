namespace Wiina.Core.Input;

/// <summary>Stable action names shared by the key map, menus and tests.</summary>
public static class PlayerActions
{
    public const string PlayPause = "play-pause";
    public const string Stop = "stop";
    public const string SeekBack5 = "seek-back-5";
    public const string SeekForward5 = "seek-forward-5";
    public const string SeekBack1 = "seek-back-1";
    public const string SeekForward1 = "seek-forward-1";
    public const string VolumeUp5 = "volume-up-5";
    public const string VolumeDown5 = "volume-down-5";
    public const string VolumeUp1 = "volume-up-1";
    public const string VolumeDown1 = "volume-down-1";
    public const string Mute = "mute";
    public const string FramePrevious = "frame-previous";
    public const string FrameNext = "frame-next";
    public const string SubtitleSeekPrevious = "subtitle-seek-previous";
    public const string SubtitleSeekNext = "subtitle-seek-next";
    public const string Open = "open";
    public const string OpenUrl = "open-url";
    public const string Settings = "settings";
    public const string Screenshot = "screenshot";
    public const string AbLoop = "ab-loop";
    public const string PlaylistPrevious = "playlist-previous";
    public const string PlaylistNext = "playlist-next";
    public const string SpeedHalf = "speed-half";
    public const string SpeedDouble = "speed-double";
    public const string SpeedReset = "speed-reset";
    public const string ToggleFullscreen = "toggle-fullscreen";
    public const string ExitFullscreen = "exit-fullscreen";
    public const string PictureInPicture = "picture-in-picture";
    public const string Sidebar = "sidebar";
    public const string SidebarSettings = "sidebar-settings";
    public const string Layout = "layout";
    public const string MusicMode = "music-mode";
    public const string Theme = "theme";
    public const string OscPosition = "osc-position";
    public const string LoopMode = "loop-mode";
    public const string ChapterPrevious = "chapter-previous";
    public const string ChapterNext = "chapter-next";
    public const string SubtitleVisibility = "subtitle-visibility";
    public const string SubtitleDelayDecrease = "subtitle-delay-decrease";
    public const string SubtitleDelayIncrease = "subtitle-delay-increase";
    public const string SubtitleDelayReset = "subtitle-delay-reset";
    public const string SubtitleScaleIncrease = "subtitle-scale-increase";
    public const string SubtitleScaleDecrease = "subtitle-scale-decrease";
    public const string SubtitlePositionUp = "subtitle-position-up";
    public const string SubtitlePositionDown = "subtitle-position-down";
    public const string WindowHalf = "window-half";
    public const string WindowNormal = "window-normal";
    public const string WindowDouble = "window-double";
    public const string WindowShrink = "window-shrink";
    public const string WindowGrow = "window-grow";
    public const string NewWindow = "new-window";
    public const string OpenInNewWindow = "open-in-new-window";
    public const string CloseWindow = "close-window";
    public const string Quit = "quit";
}

/// <summary>
/// Chord → action lookup with the documented Windows defaults. Conflicts can
/// be detected by binding the same chord twice (Bind overwrites, LastConflict
/// reports what was replaced).
/// </summary>
public sealed class KeyMap
{
    private readonly Dictionary<KeyChord, string> _bindings = new();

    public IReadOnlyDictionary<KeyChord, string> Bindings => _bindings;

    public KeyChord? LastConflict { get; private set; }

    public string? LastConflictAction { get; private set; }

    public void Bind(KeyChord chord, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        if (chord.IsEmpty)
        {
            throw new ArgumentException("Chord must have a key.", nameof(chord));
        }

        if (_bindings.TryGetValue(chord, out string? previous))
        {
            LastConflict = chord;
            LastConflictAction = previous;
        }
        else
        {
            LastConflict = null;
            LastConflictAction = null;
        }

        _bindings[chord] = action;
    }

    public bool TryBind(string chordText, string action)
    {
        if (!KeyChord.TryParse(chordText, out KeyChord chord))
        {
            return false;
        }

        Bind(chord, action);
        return true;
    }

    public bool BindFirst(string chordText, string action)
    {
        if (!KeyChord.TryParse(chordText, out KeyChord chord) ||
            _bindings.ContainsKey(chord))
        {
            return false;
        }

        Bind(chord, action);
        return true;
    }

    public void Unbind(KeyChord chord) => _bindings.Remove(chord);

    public bool TryGetAction(KeyChord chord, out string? action) =>
        _bindings.TryGetValue(chord, out action);

    public string? FindAction(KeyChord chord) =>
        _bindings.TryGetValue(chord, out string? action) ? action : null;

    /// <summary>Human-readable rows for the acceptance doc / settings list.</summary>
    public IReadOnlyList<(string Chord, string Action)> Describe() =>
        _bindings
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => (pair.Key.ToString(), pair.Value))
            .ToArray();

    public static KeyMap CreateDefault()
    {
        var map = new KeyMap();

        // Playback.
        map.TryBind("Space", PlayerActions.PlayPause);
        map.TryBind("P", PlayerActions.PlayPause);
        map.TryBind("Ctrl+.", PlayerActions.Stop);
        map.TryBind("Left", PlayerActions.SeekBack5);
        map.TryBind("Right", PlayerActions.SeekForward5);
        map.TryBind("Shift+Left", PlayerActions.SeekBack1);
        map.TryBind("Shift+Right", PlayerActions.SeekForward1);
        map.TryBind("Up", PlayerActions.VolumeUp5);
        map.TryBind("Down", PlayerActions.VolumeDown5);
        map.TryBind("Ctrl+Up", PlayerActions.VolumeUp1);
        map.TryBind("Ctrl+Down", PlayerActions.VolumeDown1);
        map.TryBind("Ctrl+M", PlayerActions.Mute);
        map.TryBind("M", PlayerActions.Mute);
        map.TryBind("Alt+Left", PlayerActions.FramePrevious);
        map.TryBind("Alt+Right", PlayerActions.FrameNext);
        map.TryBind("Ctrl+Shift+Left", PlayerActions.SubtitleSeekPrevious);
        map.TryBind("Ctrl+Shift+Right", PlayerActions.SubtitleSeekNext);
        map.TryBind("[", PlayerActions.SpeedHalf);
        map.TryBind("]", PlayerActions.SpeedDouble);
        map.TryBind("Ctrl+Backspace", PlayerActions.SpeedReset);
        map.TryBind("Ctrl+L", PlayerActions.AbLoop);
        map.TryBind("Ctrl+PageUp", PlayerActions.ChapterPrevious);
        map.TryBind("Ctrl+PageDown", PlayerActions.ChapterNext);
        map.TryBind("Ctrl+Shift+R", PlayerActions.LoopMode);

        // Media / files.
        map.TryBind("Ctrl+O", PlayerActions.Open);
        map.TryBind("Ctrl+Shift+O", PlayerActions.OpenUrl);
        map.TryBind("Ctrl+S", PlayerActions.Screenshot);
        map.TryBind("Ctrl+,", PlayerActions.Settings);
        map.TryBind("Ctrl+Left", PlayerActions.PlaylistPrevious);
        map.TryBind("Ctrl+Right", PlayerActions.PlaylistNext);

        // Window / chrome.
        map.TryBind("F11", PlayerActions.ToggleFullscreen);
        map.TryBind("Esc", PlayerActions.ExitFullscreen);
        map.TryBind("Ctrl+Shift+P", PlayerActions.PictureInPicture);
        map.TryBind("Ctrl+P", PlayerActions.PictureInPicture);
        map.TryBind("Ctrl+Shift+B", PlayerActions.Sidebar);
        map.TryBind("Ctrl+Shift+S", PlayerActions.SidebarSettings);
        map.TryBind("Ctrl+Shift+L", PlayerActions.Layout);
        map.TryBind("Ctrl+Shift+M", PlayerActions.MusicMode);
        map.TryBind("Ctrl+Shift+T", PlayerActions.Theme);
        map.TryBind("Ctrl+Alt+O", PlayerActions.OscPosition);
        map.TryBind("Ctrl+0", PlayerActions.WindowHalf);
        map.TryBind("Ctrl+1", PlayerActions.WindowNormal);
        map.TryBind("Ctrl+2", PlayerActions.WindowDouble);
        map.TryBind("Ctrl+-", PlayerActions.WindowShrink);
        map.TryBind("Ctrl+Plus", PlayerActions.WindowGrow);
        map.TryBind("Ctrl+N", PlayerActions.NewWindow);
        map.TryBind("Ctrl+Shift+N", PlayerActions.OpenInNewWindow);
        map.TryBind("Ctrl+W", PlayerActions.CloseWindow);
        map.TryBind("Q", PlayerActions.Quit);
        map.TryBind("Ctrl+Q", PlayerActions.Quit);

        // Subtitles.
        map.TryBind("V", PlayerActions.SubtitleVisibility);
        map.TryBind("Z", PlayerActions.SubtitleDelayDecrease);
        map.TryBind("X", PlayerActions.SubtitleDelayIncrease);
        map.TryBind("C", PlayerActions.SubtitleDelayReset);
        map.TryBind("G", PlayerActions.SubtitleScaleIncrease);
        map.TryBind("F", PlayerActions.SubtitleScaleDecrease);
        map.TryBind("R", PlayerActions.SubtitlePositionUp);
        map.TryBind("T", PlayerActions.SubtitlePositionDown);

        return map;
    }
}

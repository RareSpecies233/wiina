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

    // Actions added for the Wiina default profile (wiina-defaults.conf).
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string EnterFullscreen = "enter-fullscreen";
    public const string ToggleTopmost = "toggle-topmost";
    public const string FitToScreen = "fit-to-screen";
    public const string ShowProgress = "show-progress";
    public const string CycleEdition = "cycle-edition";
    public const string CycleVideo = "cycle-video";
    public const string CycleAudio = "cycle-audio";
    public const string CycleSubtitle = "cycle-subtitle";
    public const string SecondarySubtitleVisibility = "secondary-subtitle-visibility";
    public const string VideoPanel = "video-panel";
    public const string AudioPanel = "audio-panel";
    public const string SubtitlesPanel = "subtitles-panel";
    public const string PlaylistPanel = "playlist-panel";
    public const string ChapterPanel = "chapter-panel";
    public const string AudioDelayIncrease05 = "audio-delay-increase-05";
    public const string AudioDelayDecrease05 = "audio-delay-decrease-05";
    public const string AudioDelayIncrease01 = "audio-delay-increase-01";
    public const string AudioDelayDecrease01 = "audio-delay-decrease-01";
    public const string AudioDelayReset = "audio-delay-reset";
    public const string SubtitleDelayDecrease05 = "subtitle-delay-decrease-05";
    public const string SubtitleDelayIncrease05 = "subtitle-delay-increase-05";
    public const string SpeedSlower = "speed-slower";
    public const string SpeedFaster = "speed-faster";
    public const string VolumeUp2 = "volume-up-2";
    public const string VolumeDown2 = "volume-down-2";
    public const string SeekBack15 = "seek-back-15";
    public const string SeekForward15 = "seek-forward-15";
    public const string SeekBack600 = "seek-back-600";
    public const string SeekForward600 = "seek-forward-600";
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

    public static KeyMap CreateDefault() => KeyBindings.BuildKeyMap(null);
}
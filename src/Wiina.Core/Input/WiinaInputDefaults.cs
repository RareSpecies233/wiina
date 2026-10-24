using System.Globalization;
using System.Reflection;

namespace Wiina.Core.Input;

/// <summary>
/// The built-in "Wiina 默认" keyboard profile. It is parsed from the shipped
/// <c>wiina-defaults.conf</c> (mpv input.conf syntax), which is embedded into
/// this assembly. The file mirrors the author's own mpv key bindings, so the
/// desktop player and mpv behave the same out of the box.
/// </summary>
public static class WiinaInputDefaults
{
    /// <summary>Manifest name of the embedded default profile.</summary>
    public const string EmbeddedResourceName = "Wiina.Core.Input.wiina-defaults.conf";

    private static IReadOnlyList<KeyBindingSetting>? _cached;

    /// <summary>Parsed "Wiina 默认" bindings (cached; read-only).</summary>
    public static IReadOnlyList<KeyBindingSetting> Create() =>
        _cached ??= Parse(ReadConf());

    /// <summary>Reads the embedded conf; exposed for tests and diagnostics.</summary>
    public static string ReadConf()
    {
        Assembly assembly = typeof(WiinaInputDefaults).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded shortcut profile '{EmbeddedResourceName}' is missing.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Parses mpv input.conf text. Lines are <c>chord command</c>; <c>#</c>
    /// starts a comment, blank lines are ignored and the first binding wins when
    /// two lines resolve to the same chord. Commands wiina knows how to execute
    /// become a <see cref="PlayerActions"/> id; anything else is preserved as a
    /// raw <c>mpv:&lt;command&gt;</c> action so the binding keeps working.
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> Parse(string? conf)
    {
        var bindings = new List<KeyBindingSetting>();
        if (string.IsNullOrWhiteSpace(conf))
        {
            return bindings;
        }

        var seen = new HashSet<KeyChord>();
        foreach (string rawLine in conf.Split('\n'))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int split = line.IndexOfAny(new[] { ' ', '\t' });
            if (split <= 0)
            {
                continue;
            }

            string chordText = line[..split].Trim();
            string command = NormalizeWhitespace(line[(split + 1)..]);
            if (chordText.Length == 0 || command.Length == 0)
            {
                continue;
            }

            if (!KeyChord.TryParseMpv(chordText, out KeyChord chord) || chord.IsEmpty)
            {
                continue;
            }

            // A Windows keyboard only has one media play/pause key, so PLAY,
            // PAUSE, PLAYPAUSE... collapse to the same chord. Keep the first.
            if (!seen.Add(chord))
            {
                continue;
            }

            bindings.Add(new KeyBindingSetting(MapCommand(command), chord.ToString()));
        }

        return bindings;
    }

    /// <summary>Maps one mpv command to a Wiina action id (or a raw command).</summary>
    public static string MapCommand(string command)
    {
        string normalized = NormalizeWhitespace(command);
        if (normalized.StartsWith("快速", StringComparison.Ordinal))
        {
            normalized = normalized[2..].Trim();
        }

        switch (normalized)
        {
            case "video-panel":
                return PlayerActions.VideoPanel;
            case "audio-panel":
                return PlayerActions.AudioPanel;
            case "sub-panel":
                return PlayerActions.SubtitlesPanel;
            case "playlist-panel":
                return PlayerActions.PlaylistPanel;
            case "chapter-panel":
                return PlayerActions.ChapterPanel;
            case "cycle ontop":
                return PlayerActions.ToggleTopmost;
            case "cycle mute":
                return PlayerActions.Mute;
            case "cycle video":
                return PlayerActions.CycleVideo;
            case "cycle audio":
                return PlayerActions.CycleAudio;
            case "cycle sub":
                return PlayerActions.CycleSubtitle;
            case "cycle pause":
                return PlayerActions.PlayPause;
            case "stop":
                return PlayerActions.Stop;
            case "screenshot":
                return PlayerActions.Screenshot;
            case "ab-loop":
                return PlayerActions.AbLoop;
            case "playlist-next":
                return PlayerActions.PlaylistNext;
            case "playlist-prev":
                return PlayerActions.PlaylistPrevious;
            case "add chapter 1":
                return PlayerActions.ChapterNext;
            case "add chapter -1":
                return PlayerActions.ChapterPrevious;
            case "frame-step":
                return PlayerActions.FrameNext;
            case "frame-back-step":
                return PlayerActions.FramePrevious;
            case "sub-seek -1":
                return PlayerActions.SubtitleSeekPrevious;
            case "sub-seek 1":
                return PlayerActions.SubtitleSeekNext;
            case "set speed 1.0":
                return PlayerActions.SpeedReset;
            case "set fullscreen no":
                return PlayerActions.ExitFullscreen;
            case "set fullscreen yes":
                return PlayerActions.EnterFullscreen;
            case "set pause no":
                return PlayerActions.Resume;
            case "set pause yes":
                return PlayerActions.Pause;
            case "quit":
                return PlayerActions.Quit;
            case "show-progress":
                return PlayerActions.ShowProgress;
            case "cycle sub-visibility":
                return PlayerActions.SubtitleVisibility;
            case "cycle secondary-sub-visibility":
                return PlayerActions.SecondarySubtitleVisibility;
            case "cycle fullscreen":
                return PlayerActions.ToggleFullscreen;
            case "cycle edition":
                return PlayerActions.CycleEdition;
        }

        if (normalized.StartsWith("cycle-values loop", StringComparison.Ordinal))
        {
            return PlayerActions.LoopMode;
        }

        string[] parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            switch (parts[0])
            {
                case "seek" when parts.Length == 2:
                    switch (CanonicalNumber(parts[1]))
                    {
                        case "5":
                            return PlayerActions.SeekForward5;
                        case "-5":
                            return PlayerActions.SeekBack5;
                        case "15":
                            return PlayerActions.SeekForward15;
                        case "-15":
                            return PlayerActions.SeekBack15;
                        case "600":
                            return PlayerActions.SeekForward600;
                        case "-600":
                            return PlayerActions.SeekBack600;
                    }

                    break;

                case "multiply" when parts.Length == 3 && parts[1] == "speed":
                    switch (CanonicalNumber(parts[2]))
                    {
                        case "0.5":
                            return PlayerActions.SpeedHalf;
                        case "2":
                            return PlayerActions.SpeedDouble;
                        case "1/1.1":
                            return PlayerActions.SpeedSlower;
                        case "1.1":
                            return PlayerActions.SpeedFaster;
                    }

                    break;

                case "set" when parts.Length == 3 && parts[1] == "window-scale":
                    switch (CanonicalNumber(parts[2]))
                    {
                        case "0.5":
                            return PlayerActions.WindowHalf;
                        case "1":
                            return PlayerActions.WindowNormal;
                        case "2":
                            return PlayerActions.WindowDouble;
                    }

                    break;

                case "set" when parts.Length == 3 && parts[1] == "audio-delay" &&
                                      CanonicalNumber(parts[2]) == "0":
                    return PlayerActions.AudioDelayReset;

                case "set" when parts.Length == 3 && parts[1] == "sub-delay" &&
                                      CanonicalNumber(parts[2]) == "0":
                    return PlayerActions.SubtitleDelayReset;

                case "add" when parts.Length == 3:
                    switch (parts[1], CanonicalNumber(parts[2]))
                    {
                        case ("volume", "5"):
                            return PlayerActions.VolumeUp5;
                        case ("volume", "-5"):
                            return PlayerActions.VolumeDown5;
                        case ("volume", "1"):
                            return PlayerActions.VolumeUp1;
                        case ("volume", "-1"):
                            return PlayerActions.VolumeDown1;
                        case ("volume", "2"):
                            return PlayerActions.VolumeUp2;
                        case ("volume", "-2"):
                            return PlayerActions.VolumeDown2;
                        case ("audio-delay", "0.5"):
                            return PlayerActions.AudioDelayIncrease05;
                        case ("audio-delay", "-0.5"):
                            return PlayerActions.AudioDelayDecrease05;
                        case ("audio-delay", "0.1"):
                            return PlayerActions.AudioDelayIncrease01;
                        case ("audio-delay", "-0.1"):
                            return PlayerActions.AudioDelayDecrease01;
                        case ("sub-delay", "0.5"):
                            return PlayerActions.SubtitleDelayIncrease05;
                        case ("sub-delay", "-0.5"):
                            return PlayerActions.SubtitleDelayDecrease05;
                        case ("sub-delay", "0.1"):
                            return PlayerActions.SubtitleDelayIncrease;
                        case ("sub-delay", "-0.1"):
                            return PlayerActions.SubtitleDelayDecrease;
                        case ("sub-scale", "0.1"):
                            return PlayerActions.SubtitleScaleIncrease;
                        case ("sub-scale", "-0.1"):
                            return PlayerActions.SubtitleScaleDecrease;
                        case ("sub-pos", "-1"):
                            return PlayerActions.SubtitlePositionDown;
                        case ("sub-pos", "1"):
                            return PlayerActions.SubtitlePositionUp;
                    }

                    break;
            }
        }

        return "mpv:" + normalized;
    }

    private static string StripComment(string line)
    {
        int index = line.IndexOf('#');
        return index < 0 ? line : line[..index];
    }

    private static string NormalizeWhitespace(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string CanonicalNumber(string token)
    {
        string trimmed = token.TrimStart('+');
        return double.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : trimmed;
    }
}

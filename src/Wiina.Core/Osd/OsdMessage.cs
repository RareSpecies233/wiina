using Wiina.Core.Localization;
using Wiina.Core.Text;

namespace Wiina.Core.Osd;

/// <summary>Semantic OSD message categories. The renderer only uses these to
/// pick an icon, coalescing behaviour and text direction.</summary>
public enum OsdMessageKind
{
    Normal,
    Volume,
    Seek,
    Speed,
    Track,
    Subtitle,
    Pause,
    AbLoop,
    Screenshot,
    Error,
    Buffer,
    Network,
    Persistent,
}

/// <summary>
/// One OSD message. This is the Stage 5 replacement for the implicit
/// "message + secondary line" tuple that Stage 3 passed to the control.
/// </summary>
public sealed record OsdMessage
{
    public required string Text { get; init; }

    public string? Secondary { get; init; }

    /// <summary>Segoe Fluent Icons glyph. The renderer multiplies its size by 0.8.</summary>
    public string IconGlyph { get; init; } = OsdIcons.Info;

    public OsdMessageKind Kind { get; init; } = OsdMessageKind.Normal;

    /// <summary>0..1 progress for the 12 DIP bar; null hides the bar.</summary>
    public double? Progress { get; init; }

    /// <summary>Position/time messages are always laid out left-to-right.</summary>
    public bool LeftToRight { get; init; }

    /// <summary>Error messages survive the "OSD disabled" preference.</summary>
    public bool AlwaysShow { get; init; }

    /// <summary>Overrides the configured hold time for this one message.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>Icon catalogue for the message renderer.</summary>
public static class OsdIcons
{
    public const string Info = "\uE946";
    public const string Play = "\uE768";
    public const string Pause = "\uE769";
    public const string Volume = "\uE767";
    public const string Mute = "\uE74F";
    public const string Track = "\uE8FD";
    public const string Subtitle = "\uE8D8";
    public const string Speed = "\uE916";
    public const string Repeat = "\uE8EE";
    public const string Camera = "\uE722";
    public const string Warning = "\uE7BA";
    public const string Network = "\uE774";
    public const string Seek = "\uE823";
    public const string Buffer = "\uE895";
}

/// <summary>
/// Message constructors used by the player and by the text heuristics. Keeping
/// the wording in Core makes the message catalogue unit-testable.
/// </summary>
public static class OsdMessages
{
    public static OsdMessage Normal(string text, string? secondary = null, string icon = OsdIcons.Info) =>
        new()
        {
            Text = text,
            Secondary = secondary,
            IconGlyph = icon,
        };

    public static OsdMessage Volume(double percent, string? language = null)
    {
        double clamped = Math.Clamp(percent, 0, 500);
        return new OsdMessage
        {
            Text = UiStrings.Format("osd.volume", language, Math.Round(clamped)),
            IconGlyph = clamped < 1 ? OsdIcons.Mute : OsdIcons.Volume,
            Kind = OsdMessageKind.Volume,
            Progress = Math.Clamp(clamped / 100.0, 0, 1),
            LeftToRight = false,
        };
    }

    public static OsdMessage Mute(bool muted, string? language = null) =>
        new()
        {
            Text = UiStrings.Get(muted ? "osd.mute" : "osd.unmute", language),
            IconGlyph = muted ? OsdIcons.Mute : OsdIcons.Volume,
            Kind = OsdMessageKind.Volume,
            Progress = muted ? 0 : null,
        };

    public static OsdMessage Seek(double position, double duration)
    {
        double ratio = duration <= 0 ? 0 : Math.Clamp(position / duration, 0, 1);
        return new OsdMessage
        {
            Text = TimeFormatter.Format(position),
            Secondary = TimeFormatter.Format(duration),
            IconGlyph = OsdIcons.Seek,
            Kind = OsdMessageKind.Seek,
            Progress = duration <= 0 ? null : ratio,
            LeftToRight = true,
        };
    }

    public static OsdMessage Speed(double speed, string? language = null)
    {
        double clamped = Math.Clamp(speed, 0.05, 8);
        return new OsdMessage
        {
            Text = UiStrings.Format("osd.speed", language, clamped),
            IconGlyph = OsdIcons.Speed,
            Kind = OsdMessageKind.Speed,
            Progress = Math.Clamp(clamped / 4.0, 0, 1),
        };
    }

    public static OsdMessage Track(string label, string? name, string? language = null) =>
        new()
        {
            Text = UiStrings.Format("osd.track", language, TrackLabel(label, language)),
            Secondary = name,
            IconGlyph = OsdIcons.Track,
            Kind = OsdMessageKind.Track,
        };

    public static OsdMessage Subtitle(string text, string? secondary = null) =>
        new()
        {
            Text = text,
            Secondary = secondary,
            IconGlyph = OsdIcons.Subtitle,
            Kind = OsdMessageKind.Subtitle,
            LeftToRight = secondary is not null && LooksLikeTime(secondary),
        };

    public static OsdMessage Pause(bool paused, string? language = null) =>
        new()
        {
            Text = UiStrings.Get(paused ? "osd.pause" : "osd.play", language),
            IconGlyph = paused ? OsdIcons.Pause : OsdIcons.Play,
            Kind = OsdMessageKind.Pause,
        };

    public static OsdMessage AbLoop(string text, string? secondary = null) =>
        new()
        {
            Text = text,
            Secondary = secondary,
            IconGlyph = OsdIcons.Repeat,
            Kind = OsdMessageKind.AbLoop,
        };

    public static OsdMessage Screenshot(string text, string? secondary = null) =>
        new()
        {
            Text = text,
            Secondary = secondary,
            IconGlyph = OsdIcons.Camera,
            Kind = OsdMessageKind.Screenshot,
        };

    public static OsdMessage Error(string text, string? secondary = null) =>
        new()
        {
            Text = text,
            Secondary = secondary,
            IconGlyph = OsdIcons.Warning,
            Kind = OsdMessageKind.Error,
            AlwaysShow = true,
            Duration = TimeSpan.FromSeconds(3),
        };

    public static OsdMessage Buffer(string? text = null, double? percent = null, string? secondary = null, string? language = null) =>
        new()
        {
            Text = text ?? UiStrings.Get("osd.buffering", language),
            Secondary = secondary,
            IconGlyph = OsdIcons.Buffer,
            Kind = OsdMessageKind.Buffer,
            Progress = percent is null ? null : Math.Clamp(percent.Value / 100.0, 0, 1),
        };

    public static OsdMessage NetworkOpening(string? target = null, string? language = null) =>
        new()
        {
            Text = UiStrings.Get("osd.opening", language),
            Secondary = target,
            IconGlyph = OsdIcons.Network,
            Kind = OsdMessageKind.Network,
        };

    public static OsdMessage NetworkSeeking(string? language = null) =>
        new()
        {
            Text = UiStrings.Get("osd.seeking", language),
            IconGlyph = OsdIcons.Network,
            Kind = OsdMessageKind.Network,
        };

    public static OsdMessage Persistent(string title, string? subtitle = null) =>
        new()
        {
            Text = title,
            Secondary = subtitle,
            IconGlyph = OsdIcons.Info,
            Kind = OsdMessageKind.Persistent,
        };

    /// <summary>
    /// Stage 3 call sites pass plain strings. Map the common catalogue entries
    /// to semantic messages; unknown text keeps the generic info icon.
    /// </summary>
    public static OsdMessage FromText(string text, string? secondary = null, string? language = null)
    {
        string value = text.Trim();
        if (value.Length == 0)
        {
            return Normal(text, secondary);
        }

        if (value.StartsWith("音量", StringComparison.Ordinal))
        {
            return new OsdMessage
            {
                Text = value,
                Secondary = secondary,
                IconGlyph = value.Contains("静音", StringComparison.Ordinal)
                    ? OsdIcons.Mute
                    : OsdIcons.Volume,
                Kind = OsdMessageKind.Volume,
                Progress = ParseTrailingPercent(value),
            };
        }

        if (value.StartsWith("速度", StringComparison.Ordinal))
        {
            return new OsdMessage
            {
                Text = value,
                Secondary = secondary,
                IconGlyph = OsdIcons.Speed,
                Kind = OsdMessageKind.Speed,
            };
        }

        if (value.StartsWith("已切换", StringComparison.Ordinal))
        {
            return Track(value["已切换".Length..], secondary, language);
        }

        if (value is "播放" or "暂停")
        {
            return Pause(value == "暂停", language);
        }

        if (value is "静音" or "取消静音")
        {
            return Mute(value == "静音", language);
        }

        if (value.StartsWith("A 点", StringComparison.Ordinal) ||
            value.StartsWith("B 点", StringComparison.Ordinal) ||
            value.StartsWith("A-B", StringComparison.Ordinal))
        {
            return AbLoop(value, secondary);
        }

        if (value.StartsWith("截图", StringComparison.Ordinal))
        {
            return value.Contains("失败", StringComparison.Ordinal)
                ? Error(value, secondary)
                : Screenshot(value, secondary);
        }

        if (value.StartsWith("字幕", StringComparison.Ordinal))
        {
            return Subtitle(value, secondary);
        }

        if (LooksLikeTime(value) || LooksLikeTimePair(value))
        {
            double position = ParseFirstTime(value);
            double duration = secondary is not null && TryParseTime(secondary, out double parsed)
                ? parsed
                : ParseSecondTime(value);
            return Seek(position, duration);
        }

        if (IsErrorText(value))
        {
            return Error(value, secondary);
        }

        return Normal(value, secondary);
    }

    public static bool IsErrorText(string text)
    {
        string value = text.Trim();
        return value.Contains("失败", StringComparison.Ordinal) ||
            value.Contains("错误", StringComparison.Ordinal) ||
            value.Contains("无效", StringComparison.Ordinal) ||
            value.Contains("不存在", StringComparison.Ordinal) ||
            value.Contains("无法", StringComparison.Ordinal) ||
            value.Contains("过窄", StringComparison.Ordinal);
    }

    private static double? ParseTrailingPercent(string text)
    {
        int percentIndex = text.LastIndexOf('%');
        if (percentIndex <= 0)
        {
            return null;
        }

        int start = percentIndex - 1;
        while (start >= 0 && (char.IsDigit(text[start]) || text[start] == '.'))
        {
            start--;
        }

        return double.TryParse(
            text.AsSpan(start + 1, percentIndex - start - 1),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double percent)
            ? Math.Clamp(percent / 100.0, 0, 1)
            : null;
    }

    private static string TrackLabel(string label, string? language) => label switch
    {
        "音频" => UiStrings.Get("osd.track.audio", language),
        "字幕" => UiStrings.Get("osd.track.subtitle", language),
        "视频" => UiStrings.Get("osd.track.video", language),
        _ => label,
    };

    private static bool LooksLikeTime(string text) => TryParseTime(text, out _);

    private static bool LooksLikeTimePair(string text)
    {
        int separator = text.IndexOf(" / ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        return TryParseTime(text[..separator], out _) &&
            TryParseTime(text[(separator + 3)..], out _);
    }

    private static double ParseFirstTime(string text)
    {
        int separator = text.IndexOf(" / ", StringComparison.Ordinal);
        string first = separator > 0 ? text[..separator] : text;
        return TryParseTime(first, out double value) ? value : 0;
    }

    private static double ParseSecondTime(string text)
    {
        int separator = text.IndexOf(" / ", StringComparison.Ordinal);
        return separator > 0 && TryParseTime(text[(separator + 3)..], out double value)
            ? value
            : 0;
    }

    /// <summary>Parses <c>m:ss</c> or <c>h:mm:ss</c> without touching culture.</summary>
    public static bool TryParseTime(string text, out double seconds)
    {
        seconds = 0;
        string[] parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        double total = 0;
        foreach (string part in parts)
        {
            if (!int.TryParse(
                    part,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value) ||
                value < 0)
            {
                return false;
            }

            total = (total * 60) + value;
        }

        seconds = total;
        return true;
    }
}



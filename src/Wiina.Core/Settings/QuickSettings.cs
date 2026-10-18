namespace Wiina.Core.Settings;

/// <summary>The four quick-settings pages inside the settings sidebar.</summary>
public enum QuickSettingsPage
{
    Layout,
    Video,
    Audio,
    Subtitles,
}

public sealed record AspectRatioOption(string Label, string Value);

public sealed record CropOption(string Label, double Aspect);

public sealed record SubtitleColorOption(string Label, string Value);

/// <summary>
/// Pure value mapping for the quick-settings cards: option lists, clamps and
/// mpv property strings. Keeping this out of WinUI makes the mapping testable.
/// </summary>
public static class QuickSettings
{
    public static readonly IReadOnlyList<AspectRatioOption> AspectRatios =
        new[]
        {
            new AspectRatioOption("自动", "no"),
            new AspectRatioOption("16:9", "16:9"),
            new AspectRatioOption("4:3", "4:3"),
            new AspectRatioOption("2.35:1", "2.35:1"),
            new AspectRatioOption("1:1", "1:1"),
            new AspectRatioOption("5:4", "5:4"),
        };

    public static readonly IReadOnlyList<CropOption> CropOptions =
        new[]
        {
            new CropOption("无", 0.0),
            new CropOption("16:9", 16.0 / 9.0),
            new CropOption("4:3", 4.0 / 3.0),
            new CropOption("2.35:1", 2.35),
            new CropOption("1:1", 1.0),
            new CropOption("5:4", 1.25),
        };

    public static readonly IReadOnlyList<int> Rotations = new[] { 0, 90, 180, 270 };

    public static readonly IReadOnlyList<SubtitleColorOption> SubtitleColors =
        new[]
        {
            new SubtitleColorOption("白色", "#FFFFFFFF"),
            new SubtitleColorOption("黄色", "#FFFFFF00"),
            new SubtitleColorOption("青色", "#FF00FFFF"),
            new SubtitleColorOption("绿色", "#FF00FF00"),
            new SubtitleColorOption("灰色", "#FFC0C0C0"),
        };

    public const double MinVolume = 0;

    public const double MinMaxVolume = 100;

    public const double MaxMaxVolume = 200;

    public const double MaxAudioDelay = 10;

    public const double MaxSubtitleDelay = 30;

    public const double MinSubtitleScale = 0.5;

    public const double MaxSubtitleScale = 2.0;

    public const double MinSubtitleFontSize = 8;

    public const double MaxSubtitleFontSize = 120;

    public const double MaxSubtitleBorder = 10;

    public static double ClampMaxVolume(double value) =>
        Math.Clamp(value, MinMaxVolume, MaxMaxVolume);

    public static double ClampVolume(double value, double maxVolume) =>
        Math.Clamp(value, MinVolume, ClampMaxVolume(maxVolume));

    public static double ClampAudioDelay(double value) =>
        Math.Clamp(value, -MaxAudioDelay, MaxAudioDelay);

    public static double ClampSubtitleDelay(double value) =>
        Math.Clamp(value, -MaxSubtitleDelay, MaxSubtitleDelay);

    public static double ClampSubtitlePosition(double value) =>
        Math.Clamp(value, 0, 150);

    public static double ClampSubtitleScale(double value) =>
        Math.Clamp(value, MinSubtitleScale, MaxSubtitleScale);

    public static double ClampSubtitleFontSize(double value) =>
        Math.Clamp(value, MinSubtitleFontSize, MaxSubtitleFontSize);

    public static double ClampSubtitleBorder(double value) =>
        Math.Clamp(value, 0, MaxSubtitleBorder);

    public static double ClampZoom(double value) => Math.Clamp(value, -1, 1);

    public static double ClampPan(double value) => Math.Clamp(value, -1, 1);

    /// <summary>Snaps an arbitrary angle to the nearest 90° step in 0..270.</summary>
    public static int NormalizeRotation(int degrees)
    {
        int normalized = ((degrees % 360) + 360) % 360;
        int snapped = (int)Math.Round(normalized / 90.0, MidpointRounding.AwayFromZero) * 90;
        return snapped % 360;
    }

    public static int NextRotation(int degrees) => (NormalizeRotation(degrees) + 90) % 360;

    /// <summary>Maps a video aspect override to a value mpv accepts.</summary>
    public static string NormalizeAspect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return "no";
        }

        return value.Trim();
    }

    /// <summary>
    /// Computes a centred <c>video-crop</c> string (even width/height) for the
    /// requested aspect, or "no" to clear the crop when no video is loaded or
    /// the aspect already matches.
    /// </summary>
    public static string ComputeCrop(double aspect, int videoWidth, int videoHeight)
    {
        if (aspect <= 0 || videoWidth <= 0 || videoHeight <= 0)
        {
            return "no";
        }

        double current = (double)videoWidth / videoHeight;
        if (Math.Abs(current - aspect) < 0.01)
        {
            return "no";
        }

        int width;
        int height;
        if (current > aspect)
        {
            height = videoHeight;
            width = (int)Math.Round(height * aspect, MidpointRounding.AwayFromZero);
        }
        else
        {
            width = videoWidth;
            height = (int)Math.Round(width / aspect, MidpointRounding.AwayFromZero);
        }

        width = Math.Max(2, width - (width % 2));
        height = Math.Max(2, height - (height % 2));
        return $"{width}x{height}";
    }

    /// <summary>mpv <c>vf</c> string for the flip toggles (empty clears it).</summary>
    public static string BuildVideoFilters(bool flipHorizontal, bool flipVertical)
    {
        if (flipHorizontal && flipVertical)
        {
            return "hflip,vflip";
        }

        if (flipHorizontal)
        {
            return "hflip";
        }

        return flipVertical ? "vflip" : string.Empty;
    }

    public static string HwdecValue(bool enabled) => enabled ? "auto-safe" : "no";

    public static string DeinterlaceValue(bool enabled) => enabled ? "yes" : "no";

    public static string BooleanValue(bool enabled) => enabled ? "yes" : "no";

    /// <summary>Ensures mpv receives an #AARRGGBB colour string.</summary>
    public static string NormalizeColor(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "#FFFFFFFF";
        }

        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        string hex = text[1..];
        if (hex.Length == 6 &&
            hex.All(character => Uri.IsHexDigit(character)))
        {
            return "#FF" + hex.ToUpperInvariant();
        }

        if (hex.Length == 8 &&
            hex.All(character => Uri.IsHexDigit(character)))
        {
            return "#" + hex.ToUpperInvariant();
        }

        return "#FFFFFFFF";
    }
}

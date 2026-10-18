using System.Globalization;

namespace Wiina.Core.Diagnostics;

/// <summary>
/// Pure formatting helpers for the technical inspector. Keeping the units and
/// rounding here lets the WinUI window stay a thin renderer and makes the
/// values testable without a live mpv instance.
/// </summary>
public static class InspectorFormatter
{
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    public static string Resolution(int width, int height) =>
        width > 0 && height > 0
            ? $"{width} × {height}"
            : "—";

    public static string FrameRate(double fps) =>
        fps > 0
            ? $"{fps.ToString("0.###", CultureInfo.InvariantCulture)} fps"
            : "—";

    public static string BitRate(double bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "—";
        }

        if (bitsPerSecond >= 1_000_000)
        {
            return $"{(bitsPerSecond / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture)} Mb/s";
        }

        return $"{(bitsPerSecond / 1000).ToString("0.#", CultureInfo.InvariantCulture)} kb/s";
    }

    public static string SampleRate(long hertz) =>
        hertz > 0
            ? $"{(hertz / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} kHz"
            : "—";

    public static string Channels(string? channels) =>
        string.IsNullOrWhiteSpace(channels) ? "—" : channels.Trim();

    /// <summary>One line describing an mpv <c>video-params</c> colour tuple.</summary>
    public static string Colour(
        string? primaries,
        string? gamma,
        string? matrix,
        string? levels,
        double signalPeak,
        double minLuma,
        double maxLuma)
    {
        var parts = new List<string>();
        if (!IsUnknown(primaries))
        {
            parts.Add($"原色 {primaries}");
        }

        if (!IsUnknown(gamma))
        {
            parts.Add($"传递 {gamma}");
        }

        if (!IsUnknown(matrix))
        {
            parts.Add($"矩阵 {matrix}");
        }

        if (!IsUnknown(levels))
        {
            parts.Add($"范围 {levels}");
        }

        if (signalPeak > 0 || maxLuma > 0)
        {
            parts.Add(
                $"亮度 {minLuma.ToString("0.##", CultureInfo.InvariantCulture)}–" +
                $"{maxLuma.ToString("0.##", CultureInfo.InvariantCulture)} " +
                $"(峰值 {signalPeak.ToString("0.##", CultureInfo.InvariantCulture)})");
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    /// <summary>Returns HDR/SDR classification from mpv transfer characteristics.</summary>
    public static string DynamicRange(string? gamma, double maxLuma)
    {
        string value = (gamma ?? string.Empty).ToLowerInvariant();
        if (value.Contains("pq") || value.Contains("hlg") || maxLuma > 100)
        {
            return "HDR";
        }

        if (value.Length == 0 || value == "unknown" || value == "auto")
        {
            return "—";
        }

        return "SDR";
    }

    public static string SubtitleTrack(
        string? codec,
        string? language,
        string? title,
        bool selected)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(codec))
        {
            parts.Add(codec);
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            parts.Add(language);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title);
        }

        if (selected)
        {
            parts.Add("已选中");
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    /// <summary>True for bitmap subtitle codecs whose styles cannot be changed.</summary>
    public static bool IsImageSubtitle(string? codec)
    {
        string value = (codec ?? string.Empty).ToLowerInvariant();
        return value is "pgs" or "dvd_subtitle" or "dvdsub" or "dvb_subtitle" or
            "dvbsub" or "xsub" or "hdmv_pgs_subtitle";
    }

    /// <summary>True for ASS/SSA subtitles, which carry their own styling.</summary>
    public static bool IsAssSubtitle(string? codec)
    {
        string value = (codec ?? string.Empty).ToLowerInvariant();
        return value is "ass" or "ssa";
    }

    private static bool IsUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("auto", StringComparison.OrdinalIgnoreCase);
}

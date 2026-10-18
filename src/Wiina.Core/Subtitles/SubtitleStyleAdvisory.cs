namespace Wiina.Core.Subtitles;

public enum SubtitleStyleIssue
{
    None,
    Ass,
    Image,
}

public sealed record SubtitleStyleAdvice(
    SubtitleStyleIssue Issue,
    string Message,
    bool AllowStyleOverride)
{
    public static SubtitleStyleAdvice None { get; } =
        new(SubtitleStyleIssue.None, string.Empty, true);
}

/// <summary>
/// Pure rules behind the quick-settings subtitle warning. ASS/SSA subtitles
/// carry their own styling; bitmap subtitles (PGS/VobSub) cannot be restyled
/// at all. Keeping this testable avoids relying on a real subtitle file.
/// </summary>
public static class SubtitleStyleAdvisory
{
    public static SubtitleStyleAdvice Analyze(string? codec)
    {
        string value = (codec ?? string.Empty).Trim().ToLowerInvariant();

        if (value is "pgs" or "dvd_subtitle" or "dvdsub" or "dvb_subtitle" or
            "dvbsub" or "xsub" or "hdmv_pgs_subtitle")
        {
            return new SubtitleStyleAdvice(
                SubtitleStyleIssue.Image,
                "图像字幕（PGS/VobSub）由位图构成，无法调整字体、颜色和边框。",
                AllowStyleOverride: false);
        }

        if (value is "ass" or "ssa")
        {
            return new SubtitleStyleAdvice(
                SubtitleStyleIssue.Ass,
                "ASS/SSA 字幕的部分样式由字幕文件决定；字号/颜色/边框仅作为默认样式。",
                AllowStyleOverride: true);
        }

        return SubtitleStyleAdvice.None;
    }
}

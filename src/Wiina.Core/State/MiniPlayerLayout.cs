namespace Wiina.Core.State;

/// <summary>
/// Mini / music-mode constants from spec §10 and §20.1: cover at least 72 DIP,
/// 300-wide playlist panel, 148-wide volume bubble, 0.2 s control cross-fade and
/// a 48 high transport row inside a 72 high control area.
/// </summary>
public static class MiniPlayerLayout
{
    public const double CoverMinSize = 72;

    public const double PlaylistWidth = 300;

    public const double PlaylistHeight = 300;

    public const double VolumeBubbleWidth = 148;

    public const double ControlAnimationMilliseconds = 200;

    public const double ControlRowHeight = 48;

    public const double ProgressRowHeight = 24;

    public const double ControlAreaHeight = 72;

    public const double PlaylistAutoCollapseThreshold = 200;

    public const double DefaultWidth = 400;

    /// <summary>72 DIP control area + a 16:9 cover.</summary>
    public const double DefaultHeight = 297;

    public const double MinWidth = 320;

    public const double MinHeight = 260;

    public const double MaxAutoWindowHeight = 900;

    public static double CoverHeight(double windowWidth) =>
        Math.Round(windowWidth / (16.0 / 9.0));

    /// <summary>Spec: window height = 72 + cover/video height.</summary>
    public static double TotalHeight(double windowWidth) =>
        ControlAreaHeight + CoverHeight(windowWidth);

    /// <summary>Expanding the playlist adds its 300 DIP height.</summary>
    public static double HeightWithPlaylist(double windowWidth, bool expanded) =>
        TotalHeight(windowWidth) + (expanded ? PlaylistHeight : 0);

    /// <summary>
    /// The cover area must never shrink below 72 DIP, even in the smallest
    /// window the shell allows.
    /// </summary>
    public static double ResolveCoverSize(double availableWidth, double availableHeight)
    {
        double cover = Math.Min(availableWidth, availableHeight);
        return Math.Max(CoverMinSize, cover);
    }

    /// <summary>Below this window height the playlist auto-collapses (§10.1).</summary>
    public static bool ShouldAutoCollapsePlaylist(double windowHeight) =>
        windowHeight < TotalHeight(DefaultWidth) + PlaylistAutoCollapseThreshold;
}

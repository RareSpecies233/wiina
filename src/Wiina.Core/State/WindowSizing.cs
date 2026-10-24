namespace Wiina.Core.State;

/// <summary>Window size in device-independent pixels.</summary>
public readonly record struct DipSize(int Width, int Height);

/// <summary>
/// Pure initial-window sizing for the first video load. Video pixel dimensions
/// map 1:1 to physical window pixels, so a 1920x1080 frame at 150% DPI asks for
/// a 1280x720 DIP window. Oversized frames get half their dimensions before the
/// work-area cap, which keeps 4K content usable on smaller displays.
/// </summary>
public static class WindowSizing
{
    public const double MinimumWidthDip = 285;
    public const double MinimumHeightDip = 120;

    /// <summary>Nominal frame used by the window-size menu before any video loads.</summary>
    public const int FallbackVideoWidth = 1280;

    /// <inheritdoc cref="FallbackVideoWidth"/>
    public const int FallbackVideoHeight = 720;

    public static DipSize? InitialVideoWindowSize(
        int videoWidth,
        int videoHeight,
        double rasterizationScale,
        int workAreaWidth,
        int workAreaHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 ||
            rasterizationScale <= 0 || double.IsNaN(rasterizationScale) ||
            double.IsInfinity(rasterizationScale) ||
            workAreaWidth <= 0 || workAreaHeight <= 0)
        {
            return null;
        }

        double workWidthDip = workAreaWidth / rasterizationScale;
        double workHeightDip = workAreaHeight / rasterizationScale;
        double widthDip = videoWidth / rasterizationScale;
        double heightDip = videoHeight / rasterizationScale;

        if (widthDip > workWidthDip || heightDip > workHeightDip)
        {
            widthDip = videoWidth / 2.0 / rasterizationScale;
            heightDip = videoHeight / 2.0 / rasterizationScale;

            // Half of a very large frame can still be too wide/tall; shrink
            // proportionally so the aspect ratio is never distorted.
            double fit = Math.Min(1.0, Math.Min(workWidthDip / widthDip, workHeightDip / heightDip));
            widthDip *= fit;
            heightDip *= fit;
        }

        widthDip = Math.Clamp(widthDip, Math.Min(MinimumWidthDip, workWidthDip), workWidthDip);
        heightDip = Math.Clamp(heightDip, Math.Min(MinimumHeightDip, workHeightDip), workHeightDip);
        return new DipSize((int)Math.Round(widthDip), (int)Math.Round(heightDip));
    }

    /// <summary>
    /// Window size for the 0.5× / 1× / 2× menu entries. Video display pixels map
    /// 1:1 to physical window pixels, so a 1920x1080 frame at 1× asks for a
    /// 1920x1080 physical window (1280x720 DIP at 150%). The result is expressed
    /// in DIP and clamped to the current monitor's work area while keeping the
    /// video's display aspect ratio (<c>video-params/dw|dh</c>), then raised to
    /// the minimum window size (which is also capped by the work area).
    /// When no video is loaded (<paramref name="videoWidth"/> or
    /// <paramref name="videoHeight"/> is not positive) the 1280x720 fallback
    /// frame is used, so the menu still has a predictable effect.
    /// </summary>
    public static DipSize? ScaledVideoWindowSize(
        int videoWidth,
        int videoHeight,
        double factor,
        double rasterizationScale,
        int workAreaWidth,
        int workAreaHeight)
    {
        if (!IsValidScale(factor) ||
            !IsValidScale(rasterizationScale) ||
            workAreaWidth <= 0 ||
            workAreaHeight <= 0)
        {
            return null;
        }

        if (videoWidth <= 0 || videoHeight <= 0)
        {
            videoWidth = FallbackVideoWidth;
            videoHeight = FallbackVideoHeight;
        }

        double workWidthDip = workAreaWidth / rasterizationScale;
        double workHeightDip = workAreaHeight / rasterizationScale;
        double widthDip = videoWidth * factor / rasterizationScale;
        double heightDip = videoHeight * factor / rasterizationScale;
        return ClampToWorkArea(widthDip, heightDip, workWidthDip, workHeightDip);
    }

    /// <summary>
    /// "Fit to screen": the largest window with the video's display aspect ratio
    /// inside the work area (not a hardcoded 16:9 box). Falls back to the
    /// 1280x720 frame when no video is loaded.
    /// </summary>
    public static DipSize? FitVideoWindowSize(
        int videoWidth,
        int videoHeight,
        double rasterizationScale,
        int workAreaWidth,
        int workAreaHeight)
    {
        if (!IsValidScale(rasterizationScale) ||
            workAreaWidth <= 0 ||
            workAreaHeight <= 0)
        {
            return null;
        }

        if (videoWidth <= 0 || videoHeight <= 0)
        {
            videoWidth = FallbackVideoWidth;
            videoHeight = FallbackVideoHeight;
        }

        double workWidthDip = workAreaWidth / rasterizationScale;
        double workHeightDip = workAreaHeight / rasterizationScale;
        double aspect = (double)videoWidth / videoHeight;
        double widthDip = workWidthDip;
        double heightDip = workWidthDip / aspect;
        if (heightDip > workHeightDip)
        {
            heightDip = workHeightDip;
            widthDip = workHeightDip * aspect;
        }

        return ClampToWorkArea(widthDip, heightDip, workWidthDip, workHeightDip);
    }

    private static bool IsValidScale(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>
    /// Shrinks an aspect-correct size into the work area, grows it to the
    /// minimum window size, then caps it again so the minimum never leaves the
    /// screen. Aspect ratio is preserved in every step.
    /// </summary>
    private static DipSize ClampToWorkArea(
        double widthDip,
        double heightDip,
        double workWidthDip,
        double workHeightDip)
    {
        if (!(widthDip > 0) || !(heightDip > 0))
        {
            widthDip = workWidthDip;
            heightDip = workHeightDip;
        }

        double fit = Math.Min(1.0, Math.Min(workWidthDip / widthDip, workHeightDip / heightDip));
        widthDip *= fit;
        heightDip *= fit;

        double grow = Math.Max(1.0, Math.Max(MinimumWidthDip / widthDip, MinimumHeightDip / heightDip));
        widthDip *= grow;
        heightDip *= grow;

        double cap = Math.Min(1.0, Math.Min(workWidthDip / widthDip, workHeightDip / heightDip));
        widthDip *= cap;
        heightDip *= cap;

        return new DipSize(
            Math.Max(1, (int)Math.Round(widthDip)),
            Math.Max(1, (int)Math.Round(heightDip)));
    }

    /// <summary>Converts a DIP size to the physical pixels expected by AppWindow.Resize.</summary>
    public static (int Width, int Height) ToPhysical(DipSize size, double rasterizationScale)
    {
        double scale = rasterizationScale > 0 && !double.IsNaN(rasterizationScale)
            ? rasterizationScale
            : 1.0;
        return (
            Math.Max(1, (int)Math.Round(size.Width * scale)),
            Math.Max(1, (int)Math.Round(size.Height * scale)));
    }
}

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

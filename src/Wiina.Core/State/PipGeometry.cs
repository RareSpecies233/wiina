namespace Wiina.Core.State;

/// <summary>
/// PiP geometry rules (spec §11.2 / Stage 6): a 16:9, aspect-locked window with
/// a 480×270 default and a 320×180 minimum. Pure so the WinUI layer only has to
/// translate DIP ↔ pixels.
/// </summary>
public static class PipGeometry
{
    public const int DefaultWidth = 480;

    public const int DefaultHeight = 270;

    public const int MinWidth = 320;

    public const int MinHeight = 180;

    public const int MaxWidth = 1920;

    public const double AspectRatio = 16.0 / 9.0;

    /// <summary>Height implied by a 16:9 width.</summary>
    public static double HeightForWidth(double width) => width / AspectRatio;

    /// <summary>Width implied by a 16:9 height.</summary>
    public static double WidthForHeight(double height) => height * AspectRatio;

    /// <summary>
    /// Forces 16:9, the 320×180 minimum and a sane maximum. Width wins, then the
    /// height minimum pulls the width back up.
    /// </summary>
    public static (int Width, int Height) Clamp(int width, int height)
    {
        _ = height;
        int clampedWidth = Math.Clamp(width, MinWidth, MaxWidth);
        int clampedHeight = (int)Math.Round(HeightForWidth(clampedWidth));
        if (clampedHeight < MinHeight)
        {
            clampedHeight = MinHeight;
            clampedWidth = (int)Math.Round(WidthForHeight(clampedHeight));
        }

        return (clampedWidth, clampedHeight);
    }

    /// <summary>Largest 16:9 size that fits inside the work-area budget.</summary>
    public static (int Width, int Height) FitInto(int maxWidth, int maxHeight)
    {
        int width = Math.Min(Math.Max(maxWidth, MinWidth), MaxWidth);
        int height = (int)Math.Round(HeightForWidth(width));
        if (height > maxHeight)
        {
            height = Math.Max(maxHeight, MinHeight);
            width = (int)Math.Round(WidthForHeight(height));
        }

        return Clamp(width, height);
    }

    public static bool IsAspectLocked(int width, int height) =>
        width > 0 && height > 0 &&
        Math.Abs((width / (double)height) - AspectRatio) < 0.02;

    /// <summary>
    /// Bottom-right placement inside the current display's work area, offset
    /// from the screen edges and clamped so the window never leaves the monitor.
    /// </summary>
    public static (int X, int Y) PlaceBottomRight(
        int workAreaX,
        int workAreaY,
        int workAreaWidth,
        int workAreaHeight,
        int windowWidth,
        int windowHeight,
        int margin = 16)
    {
        int x = workAreaX + workAreaWidth - windowWidth - margin;
        int y = workAreaY + workAreaHeight - windowHeight - margin;
        x = Math.Clamp(x, workAreaX, Math.Max(workAreaX, workAreaX + workAreaWidth - windowWidth));
        y = Math.Clamp(y, workAreaY, Math.Max(workAreaY, workAreaY + workAreaHeight - windowHeight));
        return (x, y);
    }
}

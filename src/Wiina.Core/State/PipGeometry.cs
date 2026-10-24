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

    /// <summary>
    /// Upper bound for the mpv composition buffer in mini/PiP mode. The XAML
    /// surface always keeps the full window size; only the source that the
    /// compositor upscales is capped (see <see cref="RenderSize"/>).
    /// </summary>
    public const int MaxRenderWidth = 640;

    public const int MaxRenderHeight = 360;

    /// <summary>
    /// mpv render/composition size for a window of
    /// <paramref name="pixelWidth"/>×<paramref name="pixelHeight"/> physical
    /// pixels. Small windows render 1:1; larger ones are capped at 640×360
    /// while keeping the window's aspect ratio, which is the closest safe
    /// equivalent of CanvasSwapChain.SourceSize without replacing mpv's render
    /// path (small-window reference doc §3). This value must never be used as
    /// the XAML layout size — that stays the full window size (issue 3).
    /// </summary>
    public static (int Width, int Height) RenderSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (1, 1);
        }

        if (pixelWidth <= MaxRenderWidth && pixelHeight <= MaxRenderHeight)
        {
            return (pixelWidth, pixelHeight);
        }

        double factor = Math.Min(
            MaxRenderWidth / (double)pixelWidth,
            MaxRenderHeight / (double)pixelHeight);
        return (
            Math.Max(1, (int)Math.Round(pixelWidth * factor)),
            Math.Max(1, (int)Math.Round(pixelHeight * factor)));
    }

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

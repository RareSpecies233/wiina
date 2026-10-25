namespace Wiina.Core.Playback;

/// <summary>
/// A rectangle in DIPs: where the video actually paints inside the video widget.
/// The widget can be letterboxed/pillarboxed, so the frame never fills it while
/// the aspect ratios differ.
/// </summary>
public readonly record struct DisplayRect(double X, double Y, double Width, double Height)
{
    /// <summary>True when the rectangle has no area and must not be used.</summary>
    public bool IsEmpty => !(Width > 0) || !(Height > 0);
}

/// <summary>
/// Works out the rectangle the video really occupies inside the video widget.
///
/// mpv renders into the composition swapchain, whose size equals the widget in
/// physical pixels (<c>d3d11-composition-size</c>), so <c>osd-dimensions/*</c>
/// and its margins are physical pixels too. XAML works in DIPs, hence the
/// <c>RasterizationScale</c> division. When mpv cannot answer (no video yet, a
/// build without the property) the frame is fitted by its aspect ratio and
/// centred, which is what mpv draws by default.
/// </summary>
public static class VideoDisplayRect
{
    /// <summary>
    /// Builds the display rectangle from mpv's <c>osd-dimensions</c> values.
    /// <paramref name="scale"/> converts physical pixels to DIPs.
    /// Returns false when the values are missing or inconsistent with the
    /// widget, so the caller can fall back to <see cref="Fit"/>.
    /// </summary>
    public static bool TryFromOsd(
        double controlWidth,
        double controlHeight,
        double scale,
        double osdWidth,
        double osdHeight,
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom,
        out DisplayRect rect)
    {
        rect = default;
        if (!IsFinite(controlWidth) || !IsFinite(controlHeight) || controlWidth <= 0 || controlHeight <= 0)
        {
            return false;
        }
        if (!IsFinite(scale) || scale <= 0)
        {
            return false;
        }
        if (!IsFinite(osdWidth) || !IsFinite(osdHeight) || osdWidth < 8 || osdHeight < 8)
        {
            return false;
        }
        if (!IsFinite(marginLeft) || !IsFinite(marginTop) || !IsFinite(marginRight) || !IsFinite(marginBottom))
        {
            return false;
        }
        if (marginLeft < -1 || marginTop < -1 || marginRight < -1 || marginBottom < -1)
        {
            return false;
        }

        double physicalWidth = controlWidth * scale;
        double physicalHeight = controlHeight * scale;
        // mpv rounds the rendered rect, so allow a couple of pixels of slack and
        // reject values that clearly describe a different surface (PiP, stale
        // properties after a resize, ...).
        double tolerance = Math.Max(2, physicalWidth * 0.02);
        if (osdWidth > physicalWidth + tolerance || osdHeight > physicalHeight + tolerance)
        {
            return false;
        }
        double sumWidth = marginLeft + osdWidth + marginRight;
        double sumHeight = marginTop + osdHeight + marginBottom;
        double toleranceHeight = Math.Max(2, physicalHeight * 0.02);
        if (Math.Abs(sumWidth - physicalWidth) > tolerance || Math.Abs(sumHeight - physicalHeight) > toleranceHeight)
        {
            return false;
        }

        rect = new DisplayRect(
            Math.Max(0, marginLeft) / scale,
            Math.Max(0, marginTop) / scale,
            Math.Min(osdWidth, physicalWidth) / scale,
            Math.Min(osdHeight, physicalHeight) / scale);
        return !rect.IsEmpty;
    }

    /// <summary>
    /// Aspect-fit rectangle centred in the widget: letterbox for wide widgets,
    /// pillarbox for narrow ones.
    /// </summary>
    public static DisplayRect Fit(double controlWidth, double controlHeight, double aspect)
    {
        if (!IsFinite(controlWidth) || !IsFinite(controlHeight) || controlWidth <= 0 || controlHeight <= 0)
        {
            return default;
        }
        if (!IsFinite(aspect) || aspect <= 0)
        {
            return default;
        }

        double width = controlWidth;
        double height = controlWidth / aspect;
        if (height > controlHeight)
        {
            height = controlHeight;
            width = controlHeight * aspect;
        }
        return new DisplayRect((controlWidth - width) / 2, (controlHeight - height) / 2, width, height);
    }

    /// <summary>Clamps a rectangle so it never paints outside the widget.</summary>
    public static DisplayRect Clamp(DisplayRect rect, double controlWidth, double controlHeight)
    {
        if (rect.IsEmpty || !IsFinite(controlWidth) || !IsFinite(controlHeight) || controlWidth <= 0 || controlHeight <= 0)
        {
            return default;
        }
        double x = Math.Clamp(rect.X, 0, controlWidth);
        double y = Math.Clamp(rect.Y, 0, controlHeight);
        double width = Math.Min(rect.Width, controlWidth - x);
        double height = Math.Min(rect.Height, controlHeight - y);
        return width <= 0 || height <= 0 ? default : new DisplayRect(x, y, width, height);
    }

    /// <summary>
    /// Size of the edge-extended bitmap that covers the whole widget. The width
    /// is the requested bitmap width and the height follows the widget aspect, so
    /// the extended frame can be stretched uniformly over the widget (no
    /// non-uniform scaling, no seam at the letterbox boundary).
    /// </summary>
    public static (int Width, int Height) ExtendedSize(int width, double widgetWidth, double widgetHeight)
    {
        int w = Math.Max(1, width);
        if (!IsFinite(widgetWidth) || !IsFinite(widgetHeight) || widgetWidth <= 0 || widgetHeight <= 0)
        {
            return (w, w);
        }
        int h = Math.Max(1, (int)Math.Round(w * widgetHeight / widgetWidth));
        return (w, h);
    }

    /// <summary>
    /// Maps a pixel of the edge-extended bitmap back onto the source frame.
    /// Pixels inside <paramref name="display"/> keep the exact proportional
    /// mapping; letterbox/pillarbox pixels clamp to the nearest frame edge, which
    /// replicates the border colours outwards (continuous with the centre, so no
    /// visible seam at the boundary).
    /// </summary>
    public static bool TryMapExtended(
        int x,
        int y,
        int destWidth,
        int destHeight,
        DisplayRect display,
        double widgetWidth,
        double widgetHeight,
        int sourceWidth,
        int sourceHeight,
        out int sourceX,
        out int sourceY)
    {
        sourceX = 0;
        sourceY = 0;
        if (display.IsEmpty || destWidth <= 0 || destHeight <= 0 ||
            sourceWidth <= 0 || sourceHeight <= 0 ||
            !IsFinite(widgetWidth) || !IsFinite(widgetHeight) ||
            widgetWidth <= 0 || widgetHeight <= 0)
        {
            return false;
        }
        if (x < 0 || y < 0 || x >= destWidth || y >= destHeight)
        {
            return false;
        }

        double widgetX = (x + 0.5) * widgetWidth / destWidth;
        double widgetY = (y + 0.5) * widgetHeight / destHeight;
        double u = (widgetX - display.X) / display.Width;
        double v = (widgetY - display.Y) / display.Height;
        double sx = u <= 0 ? 0 : u >= 1 ? sourceWidth - 1 : u * (sourceWidth - 1);
        double sy = v <= 0 ? 0 : v >= 1 ? sourceHeight - 1 : v * (sourceHeight - 1);
        sourceX = Math.Clamp((int)Math.Round(sx), 0, sourceWidth - 1);
        sourceY = Math.Clamp((int)Math.Round(sy), 0, sourceHeight - 1);
        return true;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

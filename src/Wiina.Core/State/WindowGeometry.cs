namespace Wiina.Core.State;

/// <summary>Screen-space rectangle used by the Win32 WM_SIZING hook.</summary>
public readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>Which edge or corner the user is dragging (HTLEFT..HTBOTTOMRIGHT).</summary>
public enum WindowResizeEdge
{
    Left = 1,
    Right = 2,
    Top = 3,
    TopLeft = 4,
    TopRight = 5,
    Bottom = 6,
    BottomLeft = 7,
    BottomRight = 8,
}

/// <summary>
/// Pure aspect-ratio resize math shared by the Win32 sizing hook and tests.
/// The helper only changes the proposed rectangle; it never re-centres the
/// window, so repeated WM_SIZING calls are deterministic and cannot oscillate.
/// </summary>
public static class WindowGeometry
{
    public static WindowRect ResizeWithAspect(
        WindowRect proposed,
        WindowResizeEdge edge,
        double aspect,
        int minimumWidth,
        int minimumHeight)
    {
        if (!IsValidAspect(aspect) || proposed.Width <= 0 || proposed.Height <= 0)
        {
            return proposed;
        }

        int minWidth = Math.Max(1, minimumWidth);
        int minHeight = Math.Max(1, minimumHeight);
        int width;
        int height;

        if (edge is WindowResizeEdge.Left or WindowResizeEdge.Right)
        {
            // Horizontal edge drags drive the width; the top edge is the anchor.
            width = Math.Max(proposed.Width, MinimumWidthForHeight(aspect, minWidth, minHeight));
            height = HeightForWidth(width, aspect, minHeight);
        }
        else if (edge is WindowResizeEdge.Top or WindowResizeEdge.Bottom)
        {
            // Vertical edge drags drive the height; the left edge is the anchor.
            height = Math.Max(proposed.Height, MinimumHeightForWidth(aspect, minWidth, minHeight));
            width = WidthForHeight(height, aspect, minWidth);
        }
        else
        {
            // Corners keep the opposite corner fixed and follow the dragged width.
            width = Math.Max(proposed.Width, MinimumWidthForHeight(aspect, minWidth, minHeight));
            height = HeightForWidth(width, aspect, minHeight);
        }

        return edge switch
        {
            WindowResizeEdge.Left => new WindowRect(
                proposed.Right - width, proposed.Top, proposed.Right, proposed.Top + height),
            WindowResizeEdge.Right => new WindowRect(
                proposed.Left, proposed.Top, proposed.Left + width, proposed.Top + height),
            WindowResizeEdge.Top => new WindowRect(
                proposed.Left, proposed.Bottom - height, proposed.Left + width, proposed.Bottom),
            WindowResizeEdge.Bottom => new WindowRect(
                proposed.Left, proposed.Top, proposed.Left + width, proposed.Top + height),
            WindowResizeEdge.TopLeft => new WindowRect(
                proposed.Right - width, proposed.Bottom - height, proposed.Right, proposed.Bottom),
            WindowResizeEdge.TopRight => new WindowRect(
                proposed.Left, proposed.Bottom - height, proposed.Left + width, proposed.Bottom),
            WindowResizeEdge.BottomLeft => new WindowRect(
                proposed.Right - width, proposed.Top, proposed.Right, proposed.Top + height),
            WindowResizeEdge.BottomRight => new WindowRect(
                proposed.Left, proposed.Top, proposed.Left + width, proposed.Top + height),
            _ => proposed,
        };
    }

    private static bool IsValidAspect(double aspect) =>
        aspect > 0 && !double.IsNaN(aspect) && !double.IsInfinity(aspect);

    private static int MinimumWidthForHeight(double aspect, int minWidth, int minHeight) =>
        Math.Max(minWidth, (int)Math.Ceiling(minHeight * aspect));

    private static int MinimumHeightForWidth(double aspect, int minWidth, int minHeight) =>
        Math.Max(minHeight, (int)Math.Ceiling(minWidth / aspect));

    private static int HeightForWidth(int width, double aspect, int minHeight) =>
        Math.Max(minHeight, (int)Math.Round(width / aspect));

    private static int WidthForHeight(int height, double aspect, int minWidth) =>
        Math.Max(minWidth, (int)Math.Round(height * aspect));
}

namespace Wiina.Core.State;

/// <summary>
/// Thickness of the invisible resize border around a top-level window: the
/// distance between the <em>outer</em> window rectangle (<c>GetWindowRect</c> /
/// <c>AppWindow.Position</c> / <c>AppWindow.Size</c> / WM_MOVING) and the
/// <em>visible</em> frame that DWM actually draws
/// (<c>DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)</c>).
///
/// On Windows 10/11 this is roughly 7–8 physical pixels on the left, right and
/// bottom edges at 100% and 0 at the top. Snapping in outer-rect space therefore
/// leaves a 5–10 px gap on three sides while the top edge looks flush — the
/// exact symptom reported in issue 2. Snap the visible rect, then translate the
/// result back to the outer rect before applying it.
///
/// <see cref="Left"/>..<see cref="Bottom"/> are physical pixels and are normally
/// non-negative. The conversion is pure so it can be unit tested without any
/// P/Invoke (see <c>FrameMarginsTests</c>).
/// </summary>
public readonly record struct FrameMargins(int Left, int Top, int Right, int Bottom)
{
    /// <summary>A window without invisible borders (the identity conversion).</summary>
    public static FrameMargins None => default;

    public bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    /// <summary>Total horizontal invisible border (left + right).</summary>
    public int Horizontal => Left + Right;

    /// <summary>Total vertical invisible border (top + bottom).</summary>
    public int Vertical => Top + Bottom;

    /// <summary>
    /// Outer window rect → visible frame. Inset by the margins on every side;
    /// the resulting rectangle is what the user sees on screen.
    /// </summary>
    public WindowRect ToVisible(WindowRect outer) => new(
        outer.Left + Left,
        outer.Top + Top,
        outer.Right - Right,
        outer.Bottom - Bottom);

    /// <summary>
    /// Visible frame → outer window rect. Exact inverse of
    /// <see cref="ToVisible"/>: the margins are added back on every side.
    /// </summary>
    public WindowRect ToOuter(WindowRect visible) => new(
        visible.Left - Left,
        visible.Top - Top,
        visible.Right + Right,
        visible.Bottom + Bottom);

    /// <summary>
    /// Margins implied by a measured outer rect and visible frame. Negative
    /// values (a visible frame larger than the reported window rect, e.g. some
    /// maximized/DWM-rounded states) are clamped to zero so the conversion never
    /// pushes the window inwards.
    /// </summary>
    public static FrameMargins FromRects(WindowRect outer, WindowRect visible) => new(
        Math.Max(0, visible.Left - outer.Left),
        Math.Max(0, visible.Top - outer.Top),
        Math.Max(0, outer.Right - visible.Right),
        Math.Max(0, outer.Bottom - visible.Bottom));

    /// <summary>
    /// Visible size for an outer size. Used where only the size is known (no
    /// absolute position), e.g. to feed the visible rectangle into the snap
    /// session.
    /// </summary>
    public (int Width, int Height) VisibleSize(int outerWidth, int outerHeight) => (
        Math.Max(0, outerWidth - Horizontal),
        Math.Max(0, outerHeight - Vertical));
}

namespace Wiina.Core.State;

/// <summary>Which monitor edges a window is currently flush against.</summary>
[Flags]
public enum SnapEdges
{
    None = 0,

    Left = 1,

    Top = 2,

    Right = 4,

    Bottom = 8,
}

/// <summary>Work area of a monitor in physical pixels (taskbar excluded).</summary>
public readonly record struct WorkArea(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public WindowRect ToRect() => new(X, Y, Right, Bottom);

    public static WorkArea FromRect(WindowRect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);
}

/// <summary>Result of a snap pass: where the window should sit and what is stuck.</summary>
public readonly record struct EdgeSnapResult(WindowRect Rect, SnapEdges Edges)
{
    public bool IsSnapped => Edges != SnapEdges.None;
}

/// <summary>
/// Cursor-independent edge snapping with breakaway hysteresis (issue 4).
///
/// The caller feeds whole window rectangles (outer frame, physical pixels) and
/// the work area under the pointer; the session returns the position to apply.
/// Pass <see cref="FrameMargins"/> in the last argument when the rectangle is
/// the outer window rect (GetWindowRect / AppWindow / WM_MOVING): the session
/// snaps the <em>visible</em> frame inside those invisible borders and converts
/// the result back to the outer rect, so all four edges end up pixel-flush
/// (issue 2). Without margins the behaviour is unchanged.
/// Two call modes are supported:
///
/// <list type="bullet">
///   <item><c>Apply(rect, area, scale)</c> — <c>rect</c> starts from the raw
///   cursor position. Used by the app-driven drags (video surface, mini
///   player), which compute the raw position from the pointer delta.</item>
///   <item><c>Apply(rect, area, scale, fromApplied: true)</c> — <c>rect</c> is
///   the rectangle Windows derived from the <em>previous, already snapped</em>
///   position (WM_MOVING). The session keeps the per-axis offset between the
///   raw position and the applied one, so the proposed rect can be converted
///   back to the raw cursor position. Without this, the OS would keep proposing
///   positions from the snapped edge and a 1 px drag could never pull the
///   window off the magnet.</item>
/// </list>
///
/// Snapping only changes the position; the size (16:9 aspect, minimum sizes)
/// is never touched. Thresholds are DIP values scaled by the caller.
/// </summary>
public sealed class EdgeSnapSession
{
    public const int DefaultSnapDistanceDip = 10;

    /// <summary>
    /// Distance the pointer must travel away from a snapped edge before the
    /// magnet releases. Must exceed the snap distance or a snapped window can
    /// never be freed (the classic "sticky forever" bug).
    /// </summary>
    public const int DefaultBreakawayDip = 22;

    private SnapEdges _edges;
    private int _offsetX;
    private int _offsetY;

    public EdgeSnapSession(
        int snapDistanceDip = DefaultSnapDistanceDip,
        int breakawayDip = DefaultBreakawayDip)
    {
        if (snapDistanceDip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapDistanceDip));
        }

        if (breakawayDip < snapDistanceDip)
        {
            throw new ArgumentOutOfRangeException(
                nameof(breakawayDip),
                "Breakaway must be at least the snap distance, otherwise a snapped window can never be released.");
        }

        SnapDistanceDip = snapDistanceDip;
        BreakawayDip = breakawayDip;
    }

    public int SnapDistanceDip { get; }

    public int BreakawayDip { get; }

    /// <summary>Edges the window is currently stuck to.</summary>
    public SnapEdges Edges => _edges;

    public bool IsSnapped => _edges != SnapEdges.None;

    /// <summary>Forgets the magnet, e.g. when a new drag gesture starts.</summary>
    public void Reset()
    {
        _edges = SnapEdges.None;
        _offsetX = 0;
        _offsetY = 0;
    }

    /// <summary>
    /// Snap decision for one proposed window rectangle. <paramref name="scale"/>
    /// is the display scale (DIP to physical pixels); the thresholds are rounded
    /// to physical pixels so 100% and 150% displays feel the same.
    ///
    /// <paramref name="margins"/> describes the window's invisible resize border
    /// (see <see cref="FrameMargins"/>). <paramref name="proposed"/> is the outer
    /// rect in that case and so is the returned rect; the edge distances,
    /// hysteresis offsets and the resulting position are all computed in the
    /// visible frame.
    /// </summary>
    public EdgeSnapResult Apply(
        WindowRect proposed,
        WorkArea area,
        double scale = 1.0,
        bool fromApplied = false,
        FrameMargins margins = default)
    {
        WindowRect visible = margins.ToVisible(proposed);
        if (visible.Width <= 0 || visible.Height <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return new EdgeSnapResult(proposed, SnapEdges.None);
        }

        double factor = Math.Max(0.1, scale);
        int snapPx = Math.Max(1, (int)Math.Round(SnapDistanceDip * factor));
        int breakawayPx = Math.Max(snapPx, (int)Math.Round(BreakawayDip * factor));

        // Offsets are kept in visible-frame space; the margins are constant so
        // subtracting them from both sides is equivalent to outer-frame space.
        int rawLeft = fromApplied ? visible.Left - _offsetX : visible.Left;
        int rawTop = fromApplied ? visible.Top - _offsetY : visible.Top;
        int width = visible.Width;
        int height = visible.Height;

        int left = ResolveAxis(
            rawLeft,
            width,
            area.X,
            area.Right,
            wasStart: (_edges & SnapEdges.Left) != 0,
            wasEnd: (_edges & SnapEdges.Right) != 0,
            snapPx,
            breakawayPx,
            out bool stuckLeft,
            out bool stuckRight);
        int top = ResolveAxis(
            rawTop,
            height,
            area.Y,
            area.Bottom,
            wasStart: (_edges & SnapEdges.Top) != 0,
            wasEnd: (_edges & SnapEdges.Bottom) != 0,
            snapPx,
            breakawayPx,
            out bool stuckTop,
            out bool stuckBottom);

        SnapEdges edges = SnapEdges.None;
        if (stuckLeft)
        {
            edges |= SnapEdges.Left;
        }

        if (stuckRight)
        {
            edges |= SnapEdges.Right;
        }

        if (stuckTop)
        {
            edges |= SnapEdges.Top;
        }

        if (stuckBottom)
        {
            edges |= SnapEdges.Bottom;
        }

        _offsetX = left - rawLeft;
        _offsetY = top - rawTop;
        _edges = edges;

        // Translate the snapped visible frame back into outer window rect space
        // so callers can apply it directly (WM_MOVING / AppWindow.Move).
        WindowRect snappedVisible = new(left, top, left + width, top + height);
        return new EdgeSnapResult(margins.ToOuter(snappedVisible), edges);
    }

    /// <summary>
    /// One axis of the snap. <paramref name="rawStart"/> is the unsnapped
    /// position; the returned value is the position to apply.
    /// </summary>
    private static int ResolveAxis(
        int rawStart,
        int size,
        int areaStart,
        int areaEnd,
        bool wasStart,
        bool wasEnd,
        int snapPx,
        int breakawayPx,
        out bool stuckStart,
        out bool stuckEnd)
    {
        int rawEnd = rawStart + size;

        // Keep an existing magnet while the pointer has not travelled past the
        // breakaway distance; a negative value means the window is being pushed
        // into the edge, which should keep it stuck.
        stuckStart = wasStart && rawStart - areaStart <= breakawayPx;
        stuckEnd = wasEnd && areaEnd - rawEnd <= breakawayPx;

        if (!stuckStart && !stuckEnd)
        {
            bool nearStart = Math.Abs(rawStart - areaStart) <= snapPx;
            bool nearEnd = Math.Abs(areaEnd - rawEnd) <= snapPx;
            if (nearStart)
            {
                stuckStart = true;
            }
            else if (nearEnd)
            {
                stuckEnd = true;
            }
        }

        // A window at least as large as the work area matches both edges; the
        // leading edge wins so the title bar is never pushed off-screen.
        if (stuckStart)
        {
            stuckEnd = false;
            return areaStart;
        }

        if (stuckEnd)
        {
            return areaEnd - size;
        }

        return rawStart;
    }
}

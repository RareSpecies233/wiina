namespace Wiina.Core.Files;

/// <summary>
/// Pure mapping for mpv's asymmetric <c>playlist-move old new</c> semantics.
/// </summary>
/// <remarks>
/// mpv removes <c>old</c> first and then inserts at <c>new</c> in the shortened
/// list. Moving down therefore needs <c>new + 1</c> to land on the requested
/// final index (verified with libmpv: <c>[a,b,c]</c> + <c>move 0 1</c> is a
/// no-op, while <c>move 0 2</c> yields <c>[b,a,c]</c>). Moving up already uses
/// the final index because the removed row is below the insertion point.
/// </remarks>
public static class PlaylistMove
{
    /// <summary>
    /// Maps a requested final index to the index passed to
    /// <c>playlist-move</c>: <paramref name="to"/> when moving up,
    /// <paramref name="to"/> + 1 when moving down.
    /// </summary>
    public static int ResolveTarget(int from, int to) =>
        to > from ? to + 1 : to;

    /// <summary>
    /// True when both indexes address existing, distinct rows.
    /// The mapped target may legitimately equal <paramref name="count"/>
    /// (moving a row to the end); that is not an input index and is not
    /// validated here.
    /// </summary>
    public static bool IsValid(int from, int to, int count) =>
        count > 0
        && from >= 0
        && from < count
        && to >= 0
        && to < count
        && from != to;

    /// <summary>
    /// Validates a move request and returns the exact argument for
    /// <c>playlist-move</c>. Returns <c>false</c> for out-of-range or
    /// same-row requests; callers should then skip the mpv command instead
    /// of sending something mpv silently ignores.
    /// </summary>
    public static bool TryResolveTarget(int from, int to, int count, out int target)
    {
        if (!IsValid(from, to, count))
        {
            target = -1;
            return false;
        }

        target = ResolveTarget(from, to);
        return true;
    }
}

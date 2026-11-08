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

    /// <summary>
    /// Expands a multi-row move into the exact, ordered <c>playlist-move</c>
    /// commands that shift every selected row one slot in
    /// <paramref name="delta"/>'s direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned <c>To</c> values are already the second argument for
    /// <c>playlist-move</c> (mapped through <see cref="ResolveTarget"/>); the
    /// caller sends them verbatim. mpv executes queued commands in order, so
    /// the sequence below composes into one block move.
    /// </para>
    /// <para>
    /// Up moves are emitted ascending and down moves descending, so a command
    /// never shifts a row that still has to move. Selected indexes are
    /// de-duplicated, sorted, and clamped to the playlist; out-of-range
    /// entries are ignored. Contiguous runs are treated as a single block: a
    /// run already touching the edge in the move direction is skipped
    /// entirely instead of being split. Only the sign of
    /// <paramref name="delta"/> matters (the UI only requests ±1).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int From, int To)> ResolveBatch(
        IReadOnlyList<int> selectedIndexes,
        int count,
        int delta)
    {
        if (selectedIndexes is null
            || selectedIndexes.Count == 0
            || count <= 0
            || delta == 0)
        {
            return Array.Empty<(int From, int To)>();
        }

        int[] selected = selectedIndexes
            .Where(index => index >= 0 && index < count)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        if (selected.Length == 0)
        {
            return Array.Empty<(int From, int To)>();
        }

        var moves = new List<(int From, int To)>(selected.Length);
        if (delta < 0)
        {
            int runStart = 0;
            while (runStart < selected.Length)
            {
                int runEnd = runStart;
                while (runEnd + 1 < selected.Length
                       && selected[runEnd + 1] == selected[runEnd] + 1)
                {
                    runEnd++;
                }

                // A run pinned at index 0 cannot move up as a block: moving
                // only its lower rows would reorder the run.
                if (selected[runStart] > 0)
                {
                    for (int i = runStart; i <= runEnd; i++)
                    {
                        int from = selected[i];
                        moves.Add((from, ResolveTarget(from, from - 1)));
                    }
                }

                runStart = runEnd + 1;
            }
        }
        else
        {
            int runEnd = selected.Length - 1;
            while (runEnd >= 0)
            {
                int runStart = runEnd;
                while (runStart - 1 >= 0
                       && selected[runStart - 1] == selected[runStart] - 1)
                {
                    runStart--;
                }

                // A run pinned at the last index cannot move down as a block.
                if (selected[runEnd] < count - 1)
                {
                    for (int i = runEnd; i >= runStart; i--)
                    {
                        int from = selected[i];
                        moves.Add((from, ResolveTarget(from, from + 1)));
                    }
                }

                runEnd = runStart - 1;
            }
        }

        return moves;
    }

    /// <summary>
    /// True when <see cref="ResolveBatch"/> would send at least one command.
    /// Drives the playlist buttons and context-menu enabled state.
    /// </summary>
    public static bool CanMoveBatch(
        IReadOnlyList<int> selectedIndexes,
        int count,
        int delta) =>
        ResolveBatch(selectedIndexes, count, delta).Count > 0;
}

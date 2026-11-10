using Xunit;
using Wiina.Core.Files;

namespace Wiina.Core.Tests;

public sealed class PlaylistMoveTests
{
    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(1, 0, 0)]
    public void MovingUpUsesTheFinalIndex(int from, int to, int expected)
    {
        Assert.Equal(expected, PlaylistMove.ResolveTarget(from, to));
    }

    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(1, 2, 3)]
    [InlineData(0, 1, 2)]
    public void MovingDownInsertsAfterRemoval(int from, int to, int expected)
    {
        Assert.Equal(expected, PlaylistMove.ResolveTarget(from, to));
    }

    [Fact]
    public void AdjacentMoveDownMapsToTheNextInsertSlot()
    {
        // libmpv: [a,b,c] + "move 0 1" is a no-op; "move 0 2" gives [b,a,c].
        Assert.Equal(2, PlaylistMove.ResolveTarget(0, 1));
        Assert.True(PlaylistMove.TryResolveTarget(0, 1, 3, out int target));
        Assert.Equal(2, target);
    }

    [Fact]
    public void MovingTheLastRowDownTargetsTheListLength()
    {
        const int count = 3;

        Assert.True(PlaylistMove.TryResolveTarget(0, count - 1, count, out int target));
        Assert.Equal(count, target);
    }

    [Theory]
    [InlineData(3, 0, 3)]   // from == count
    [InlineData(0, 3, 3)]   // to == count
    [InlineData(-1, 1, 3)]  // negative from
    [InlineData(1, -1, 3)]  // negative to
    [InlineData(1, 1, 3)]   // same row
    [InlineData(0, 0, 1)]   // same row with a single item
    [InlineData(0, 0, 0)]   // empty playlist
    public void InvalidRequestsAreRejected(int from, int to, int count)
    {
        Assert.False(PlaylistMove.IsValid(from, to, count));
        Assert.False(PlaylistMove.TryResolveTarget(from, to, count, out int target));
        Assert.Equal(-1, target);
    }

    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(2, 0, 3)]
    [InlineData(1, 2, 4)]
    public void ValidRequestsAreAccepted(int from, int to, int count)
    {
        Assert.True(PlaylistMove.IsValid(from, to, count));
        Assert.True(PlaylistMove.TryResolveTarget(from, to, count, out _));
    }

    // ------------------------------------------------------- batch resolution

    [Fact]
    public void ContiguousSelectionMovesDownAsABlock()
    {
        string[] initial = { "A", "B", "C", "D" };

        (int From, int To)[] moves = Batch(new[] { 1, 2 }, initial.Length, 1);

        // Bottom row first: 2 -> 4, then 1 -> 3.
        Assert.Equal(new[] { (2, 4), (1, 3) }, moves);
        Assert.Equal(new[] { "A", "D", "B", "C" }, ApplyMoves(initial, moves));
    }

    [Fact]
    public void ContiguousSelectionMovesUpAsABlock()
    {
        string[] initial = { "A", "B", "C", "D" };

        (int From, int To)[] moves = Batch(new[] { 1, 2 }, initial.Length, -1);

        // Top row first: 1 -> 0, then 2 -> 1.
        Assert.Equal(new[] { (1, 0), (2, 1) }, moves);
        Assert.Equal(new[] { "B", "C", "A", "D" }, ApplyMoves(initial, moves));
    }

    [Fact]
    public void ContiguousRunPinnedAtTopDoesNotMoveUp()
    {
        Assert.Empty(Batch(new[] { 0, 1 }, 4, -1));
        Assert.Empty(Batch(new[] { 0, 1, 2, 3 }, 4, -1));
    }

    [Fact]
    public void ContiguousRunPinnedAtBottomDoesNotMoveDown()
    {
        Assert.Empty(Batch(new[] { 2, 3 }, 4, 1));
        Assert.Empty(Batch(new[] { 0, 1, 2, 3 }, 4, 1));
    }

    [Fact]
    public void SingleMiddleRowMovesExactlyOneSlot()
    {
        string[] initial = { "A", "B", "C", "D", "E" };

        (int From, int To)[] up = Batch(new[] { 2 }, initial.Length, -1);
        Assert.Equal(new[] { (2, 1) }, up);
        Assert.Equal(new[] { "A", "C", "B", "D", "E" }, ApplyMoves(initial, up));

        (int From, int To)[] down = Batch(new[] { 2 }, initial.Length, 1);
        Assert.Equal(new[] { (2, 4) }, down);
        Assert.Equal(new[] { "A", "B", "D", "C", "E" }, ApplyMoves(initial, down));
    }

    [Fact]
    public void FirstRowCannotMoveUpAndLastRowCannotMoveDown()
    {
        Assert.Empty(Batch(new[] { 0 }, 3, -1));
        Assert.Empty(Batch(new[] { 2 }, 3, 1));
    }

    [Fact]
    public void AllSelectedRowsCannotMoveInEitherDirection()
    {
        Assert.Empty(Batch(new[] { 0, 1, 2 }, 3, -1));
        Assert.Empty(Batch(new[] { 0, 1, 2 }, 3, 1));
    }

    [Fact]
    public void NonContiguousSelectionMovesEachRowOneSlot()
    {
        string[] initial = { "A", "B", "C", "D", "E" };

        // B (1) and D (3) down: D first, then B.
        (int From, int To)[] down = Batch(new[] { 1, 3 }, initial.Length, 1);
        Assert.Equal(new[] { (3, 5), (1, 3) }, down);
        Assert.Equal(new[] { "A", "C", "B", "E", "D" }, ApplyMoves(initial, down));

        // B (1) and D (3) up: B first, then D.
        (int From, int To)[] up = Batch(new[] { 1, 3 }, initial.Length, -1);
        Assert.Equal(new[] { (1, 0), (3, 2) }, up);
        Assert.Equal(new[] { "B", "A", "D", "C", "E" }, ApplyMoves(initial, up));
    }

    [Fact]
    public void FiveRowsWithThreeNonContiguousSelectedKeepRelativeOrder()
    {
        string[] initial = { "A", "B", "C", "D", "E" };

        // A (0), C (2), E (4); E is pinned at the bottom and stays put.
        (int From, int To)[] moves = Batch(new[] { 0, 2, 4 }, initial.Length, 1);
        Assert.Equal(new[] { (2, 4), (0, 2) }, moves);

        List<string> final = ApplyMoves(initial, moves);
        Assert.Equal(new[] { "B", "A", "D", "C", "E" }, final);
        Assert.True(final.IndexOf("A") < final.IndexOf("C"));
        Assert.True(final.IndexOf("C") < final.IndexOf("E"));
    }

    [Fact]
    public void MixedRunsSkipOnlyThePinnedRun()
    {
        string[] initial = { "A", "B", "C", "D", "E", "F" };

        // Runs: [0], [2,3], [5]; only the run touching the bottom is skipped.
        (int From, int To)[] moves = Batch(new[] { 0, 2, 3, 5 }, initial.Length, 1);
        Assert.Equal(new[] { (3, 5), (2, 4), (0, 2) }, moves);
        Assert.Equal(new[] { "B", "A", "E", "C", "D", "F" }, ApplyMoves(initial, moves));
    }

    [Fact]
    public void DuplicateAndOutOfRangeIndexesAreIgnored()
    {
        Assert.Equal(
            new[] { (2, 1) },
            Batch(new[] { 2, 2, -1, 4, 2 }, 4, -1));

        Assert.Equal(
            new[] { (2, 4), (1, 3) },
            Batch(new[] { 1, 2, 1, 99 }, 5, 1));
    }

    [Fact]
    public void EmptySelectionAndEmptyPlaylistProduceNoMoves()
    {
        Assert.Empty(Batch(Array.Empty<int>(), 4, 1));
        Assert.Empty(Batch(new[] { 1 }, 0, 1));
        Assert.Empty(PlaylistMove.ResolveBatch(null!, 4, 1));
    }

    [Fact]
    public void ZeroDeltaProducesNoMoves()
    {
        Assert.Empty(Batch(new[] { 1, 2 }, 4, 0));
    }

    [Fact]
    public void ResolveBatchSortsAndDeduplicates()
    {
        Assert.Equal(
            new[] { (1, 0), (2, 1) },
            Batch(new[] { 2, 1, 2 }, 4, -1));
    }

    [Fact]
    public void EveryCommandTargetStaysInsideTheMpvRange()
    {
        int[][] selections =
        {
            new[] { 0, 2, 4 },
            new[] { 1, 2, 3 },
            new[] { 0, 1 },
            new[] { 2 },
            new[] { 0, 3 },
        };

        foreach (int[] selected in selections)
        {
            foreach (int delta in new[] { -1, 1 })
            {
                foreach ((int from, int to) in Batch(selected, 5, delta))
                {
                    Assert.InRange(from, 0, 4);
                    if (delta < 0)
                    {
                        Assert.InRange(to, 0, from - 1);
                    }
                    else
                    {
                        Assert.InRange(to, from + 1, 5);
                    }
                }
            }
        }
    }

    [Fact]
    public void CanMoveBatchMatchesTheButtonEdgeStates()
    {
        Assert.True(PlaylistMove.CanMoveBatch(new[] { 1 }, 3, -1));
        Assert.True(PlaylistMove.CanMoveBatch(new[] { 1 }, 3, 1));
        Assert.False(PlaylistMove.CanMoveBatch(new[] { 0 }, 3, -1));
        Assert.False(PlaylistMove.CanMoveBatch(new[] { 2 }, 3, 1));
        Assert.False(PlaylistMove.CanMoveBatch(new[] { 0, 1 }, 3, -1));
        Assert.False(PlaylistMove.CanMoveBatch(new[] { 1, 2 }, 3, 1));
        Assert.True(PlaylistMove.CanMoveBatch(new[] { 0, 2 }, 3, -1));
        Assert.False(PlaylistMove.CanMoveBatch(Array.Empty<int>(), 3, 1));
        Assert.False(PlaylistMove.CanMoveBatch(new[] { 1 }, 1, 1));
    }

    // ------------------------------------------------------------------ helpers

    private static (int, int)[] Batch(int[] selected, int count, int delta) =>
        PlaylistMove.ResolveBatch(selected, count, delta)
            .Select(move => (move.From, move.To))
            .ToArray();

    /// <summary>
    /// Replays the commands with libmpv's remove-then-insert semantics: the
    /// second argument addresses the list before removal, so after removing a
    /// row the insert position is one lower when the row moves down.
    /// </summary>
    private static List<string> ApplyMoves(
        IReadOnlyList<string> initial,
        IReadOnlyList<(int From, int To)> moves)
    {
        var items = new List<string>(initial);
        foreach ((int from, int to) in moves)
        {
            string entry = items[from];
            items.RemoveAt(from);
            items.Insert(to > from ? to - 1 : to, entry);
        }

        return items;
    }
}

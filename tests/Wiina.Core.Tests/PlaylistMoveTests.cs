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
}

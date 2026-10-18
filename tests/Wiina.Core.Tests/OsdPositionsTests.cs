using Wiina.Core.Models;
using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public class OsdPositionsTests
{
    [Fact]
    public void ExistingSerializedValuesAreUnchanged()
    {
        Assert.Equal(0, (int)OsdPosition.BottomRight);
        Assert.Equal(1, (int)OsdPosition.BottomLeft);
        Assert.Equal(2, (int)OsdPosition.TopRight);
        Assert.Equal(3, (int)OsdPosition.TopLeft);
        Assert.Equal(4, (int)OsdPosition.TopCenter);
        Assert.Equal(5, (int)OsdPosition.BottomCenter);
    }

    [Fact]
    public void AllListsEveryPositionInChoiceOrder()
    {
        Assert.Equal(
            new[]
            {
                OsdPosition.BottomRight,
                OsdPosition.BottomLeft,
                OsdPosition.TopRight,
                OsdPosition.TopLeft,
                OsdPosition.TopCenter,
                OsdPosition.BottomCenter,
            },
            OsdPositions.All);
    }

    [Theory]
    [InlineData(OsdPosition.BottomRight, 0)]
    [InlineData(OsdPosition.BottomLeft, 1)]
    [InlineData(OsdPosition.TopRight, 2)]
    [InlineData(OsdPosition.TopLeft, 3)]
    [InlineData(OsdPosition.TopCenter, 4)]
    [InlineData(OsdPosition.BottomCenter, 5)]
    public void IndexAndPositionRoundTrip(OsdPosition position, int index)
    {
        Assert.Equal(index, OsdPositions.ToIndex(position));
        Assert.Equal(position, OsdPositions.FromIndex(index));
    }

    [Theory]
    [InlineData(-1, OsdPosition.BottomRight)]
    [InlineData(6, OsdPosition.BottomCenter)]
    [InlineData(99, OsdPosition.BottomCenter)]
    public void FromIndexClampsOutOfRangeValues(int index, OsdPosition expected)
    {
        Assert.Equal(expected, OsdPositions.FromIndex(index));
    }

    [Theory]
    [InlineData(OsdPosition.TopLeft, true, true, false)]
    [InlineData(OsdPosition.TopRight, true, false, false)]
    [InlineData(OsdPosition.TopCenter, true, false, true)]
    [InlineData(OsdPosition.BottomLeft, false, true, false)]
    [InlineData(OsdPosition.BottomRight, false, false, false)]
    [InlineData(OsdPosition.BottomCenter, false, false, true)]
    public void AlignmentFlagsMatchTheSixPlacements(
        OsdPosition position,
        bool top,
        bool left,
        bool center)
    {
        Assert.Equal(top, OsdPositions.IsTop(position));
        Assert.Equal(left, OsdPositions.IsLeft(position));
        Assert.Equal(center, OsdPositions.IsCenter(position));
    }
}

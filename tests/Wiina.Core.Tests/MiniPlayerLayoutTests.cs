using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class MiniPlayerLayoutTests
{
    [Fact]
    public void SpecConstantsArePinned()
    {
        Assert.Equal(72, MiniPlayerLayout.CoverMinSize);
        Assert.Equal(300, MiniPlayerLayout.PlaylistWidth);
        Assert.Equal(148, MiniPlayerLayout.VolumeBubbleWidth);
        Assert.Equal(200, MiniPlayerLayout.ControlAnimationMilliseconds);
        Assert.Equal(48, MiniPlayerLayout.ControlRowHeight);
    }

    [Fact]
    public void TotalHeightIsControlAreaPlusCover()
    {
        Assert.Equal(
            MiniPlayerLayout.ControlAreaHeight + MiniPlayerLayout.CoverHeight(400),
            MiniPlayerLayout.TotalHeight(400),
            3);
    }

    [Fact]
    public void PlaylistAddsThreeHundred()
    {
        Assert.Equal(
            MiniPlayerLayout.TotalHeight(400) + 300,
            MiniPlayerLayout.HeightWithPlaylist(400, expanded: true),
            3);
        Assert.Equal(
            MiniPlayerLayout.TotalHeight(400),
            MiniPlayerLayout.HeightWithPlaylist(400, expanded: false),
            3);
    }

    [Fact]
    public void CoverNeverShrinksBelowSeventyTwo()
    {
        Assert.Equal(72, MiniPlayerLayout.ResolveCoverSize(40, 30));
        Assert.Equal(220, MiniPlayerLayout.ResolveCoverSize(220, 400));
    }

    [Fact]
    public void PlaylistAutoCollapsesForShortWindows()
    {
        Assert.True(MiniPlayerLayout.ShouldAutoCollapsePlaylist(300));
        Assert.False(MiniPlayerLayout.ShouldAutoCollapsePlaylist(1200));
    }

    [Fact]
    public void DefaultWindowFitsTheMinimums()
    {
        Assert.True(MiniPlayerLayout.DefaultWidth >= MiniPlayerLayout.MinWidth);
        Assert.True(MiniPlayerLayout.DefaultHeight >= MiniPlayerLayout.MinHeight);
        Assert.True(MiniPlayerLayout.CoverHeight(MiniPlayerLayout.DefaultWidth) >=
            MiniPlayerLayout.CoverMinSize);
    }
}

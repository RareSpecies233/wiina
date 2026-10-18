using Wiina.Core.Models;
using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class SidebarStateTests
{
    [Fact]
    public void ToggleShowsOnRequestedSide()
    {
        var state = new SidebarState();

        Assert.Equal(SidebarToggleResult.Shown, state.Toggle(SidebarSide.Trailing, SidebarType.Settings));
        Assert.True(state.IsAnyVisible);
        Assert.True(state.IsVisible(SidebarSide.Trailing));
        Assert.Equal(SidebarType.Settings, state.TypeFor(SidebarSide.Trailing));
        Assert.Equal(SidebarSide.Trailing, state.SideFor(SidebarType.Settings));
    }

    [Fact]
    public void ClickingTheSameTabClosesIt()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Leading, SidebarType.Settings);

        Assert.Equal(SidebarToggleResult.Hidden, state.Toggle(SidebarSide.Leading, SidebarType.Settings));
        Assert.False(state.IsAnyVisible);
        Assert.Null(state.TypeFor(SidebarSide.Leading));
    }

    [Fact]
    public void DifferentTabReplacesInPlace()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Trailing, SidebarType.Settings);

        Assert.Equal(SidebarToggleResult.Replaced, state.Toggle(SidebarSide.Trailing, SidebarType.Playlist));
        Assert.True(state.IsVisible(SidebarSide.Trailing));
        Assert.Equal(SidebarType.Playlist, state.TypeFor(SidebarSide.Trailing));
        Assert.DoesNotContain(SidebarSide.Leading, state.Shown.Keys);
    }

    [Fact]
    public void DifferentTypeOnOtherSideIsAllowed()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Leading, SidebarType.Settings);
        state.Toggle(SidebarSide.Trailing, SidebarType.Playlist);

        Assert.True(state.IsVisible(SidebarSide.Leading));
        Assert.True(state.IsVisible(SidebarSide.Trailing));
        Assert.Equal(SidebarType.Settings, state.TypeFor(SidebarSide.Leading));
        Assert.Equal(SidebarType.Playlist, state.TypeFor(SidebarSide.Trailing));
    }

    [Fact]
    public void OneTypeMovesInsteadOfDuplicating()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Leading, SidebarType.Chapters);

        Assert.Equal(SidebarToggleResult.Moved, state.Toggle(SidebarSide.Trailing, SidebarType.Chapters));
        Assert.False(state.IsVisible(SidebarSide.Leading));
        Assert.Equal(SidebarType.Chapters, state.TypeFor(SidebarSide.Trailing));
        Assert.Single(state.Shown);
    }

    [Fact]
    public void ShowDoesNotCloseAnAlreadyVisibleTab()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Trailing, SidebarType.Settings);

        Assert.Equal(SidebarToggleResult.Unchanged, state.Show(SidebarSide.Trailing, SidebarType.Settings));
        Assert.True(state.IsVisible(SidebarSide.Trailing));
    }

    [Fact]
    public void ShowReplacesAnotherTypeWithoutClosing()
    {
        var state = new SidebarState();
        state.Show(SidebarSide.Trailing, SidebarType.Settings);

        Assert.Equal(SidebarToggleResult.Replaced, state.Show(SidebarSide.Trailing, SidebarType.Playlist));
        Assert.Equal(SidebarType.Playlist, state.TypeFor(SidebarSide.Trailing));
    }

    [Fact]
    public void HideByTypeRemovesWhicheverSideOwnsIt()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Leading, SidebarType.Plugins);

        Assert.True(state.Hide(SidebarType.Plugins));
        Assert.False(state.IsAnyVisible);
        Assert.False(state.Hide(SidebarType.Plugins));
    }

    [Fact]
    public void HideAllClearsBothSlots()
    {
        var state = new SidebarState();
        state.Toggle(SidebarSide.Leading, SidebarType.Settings);
        state.Toggle(SidebarSide.Trailing, SidebarType.Playlist);

        state.HideAll();

        Assert.False(state.IsAnyVisible);
        Assert.Empty(state.Shown);
    }

    [Theory]
    [InlineData(360, 800, 360)]
    [InlineData(250, 800, 300)]
    [InlineData(600, 800, 500)]
    [InlineData(460, 500, 400)]
    [InlineData(360, 400, 320)]
    [InlineData(double.NaN, 1280, 360)]
    public void WidthIsClampedToSpec(double requested, double windowWidth, double expected)
    {
        Assert.Equal(expected, SidebarState.ClampWidth(requested, windowWidth));
    }

    [Fact]
    public void WidthNeverDropsBelowMinimumForTinyWindows()
    {
        // 80% of 300 would be 240; the 300 DIP lower bound wins and the shell
        // hides sidebars below its compact threshold anyway.
        Assert.Equal(SidebarState.MinWidth, SidebarState.ClampWidth(320, 300));
    }

    [Fact]
    public void SetWidthStoresClampedValue()
    {
        var state = new SidebarState();
        state.SetWidth(900, 1280);

        Assert.Equal(SidebarState.MaxWidth, state.Width);
    }

    [Theory]
    [InlineData(true, true, SidebarAnimationMode.Slide)]
    [InlineData(true, false, SidebarAnimationMode.Fade)]
    [InlineData(false, true, SidebarAnimationMode.None)]
    [InlineData(false, false, SidebarAnimationMode.None)]
    public void AnimationModeRespectsBothSwitches(
        bool appEnabled,
        bool systemEnabled,
        SidebarAnimationMode expected)
    {
        Assert.Equal(
            expected,
            SidebarState.ResolveAnimation(appEnabled, systemEnabled));
    }

    [Fact]
    public void DockedInsetOnlyAppliesToDockedLayout()
    {
        Assert.Equal(360, SidebarState.DockedInset(WindowLayoutMode.Docked, true, 360));
        Assert.Equal(0, SidebarState.DockedInset(WindowLayoutMode.Docked, false, 360));
        Assert.Equal(0, SidebarState.DockedInset(WindowLayoutMode.EdgeToEdge, true, 360));
    }

    [Fact]
    public void SidebarIsSuppressedBelowCompactWidth()
    {
        Assert.False(SidebarState.AllowedAtWindowWidth(400));
        Assert.True(SidebarState.AllowedAtWindowWidth(420));
        Assert.True(SidebarState.AllowedAtWindowWidth(1280));
    }
}

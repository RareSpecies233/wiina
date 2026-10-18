using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class WindowCoordinatorTests
{
    [Fact]
    public void FirstRegisteredWindowBecomesActive()
    {
        var coordinator = new WindowCoordinator();

        coordinator.Register("a", "Wiina A");

        Assert.Equal("a", coordinator.ActiveWindowId);
        Assert.Equal(1, coordinator.Count);
        Assert.False(coordinator.ShouldExit);
    }

    [Fact]
    public void RegisteringASecondWindowDoesNotStealActivation()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");
        string? raised = null;
        coordinator.ActiveWindowChanged += id => raised = id;

        coordinator.Register("b");

        Assert.Equal("a", coordinator.ActiveWindowId);
        Assert.Null(raised);
        Assert.Equal(2, coordinator.Count);
    }

    [Fact]
    public void ActivateMovesFocusAndMediaTarget()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");
        coordinator.Register("b");

        Assert.True(coordinator.Activate("b"));

        Assert.Equal("b", coordinator.ActiveWindowId);
        Assert.Equal("b", coordinator.ResolveMediaTargetId());
    }

    [Fact]
    public void ClosingTheActiveWindowFallsBackToTheMostRecentSurvivor()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");
        coordinator.Register("b");
        coordinator.Activate("b");

        Assert.True(coordinator.Close("b"));

        Assert.Equal("a", coordinator.ActiveWindowId);
        Assert.Equal("a", coordinator.ResolveMediaTargetId());
        Assert.Equal(1, coordinator.Count);
    }

    [Fact]
    public void ClosingTheLastWindowRequiresExit()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");

        Assert.True(coordinator.Close("a"));

        Assert.True(coordinator.ShouldExit);
        Assert.Null(coordinator.ActiveWindowId);
    }

    [Fact]
    public void ClosingAnInactiveWindowKeepsTheActiveOne()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");
        coordinator.Register("b");
        coordinator.Activate("b");

        coordinator.Close("a");

        Assert.Equal("b", coordinator.ActiveWindowId);
        Assert.Equal(1, coordinator.Count);
    }

    [Fact]
    public void UpdateTracksTitleAndMediaFlags()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");

        Assert.True(coordinator.Update("a", "Movie", hasMedia: true));

        PlayerWindowInfo? info = coordinator.Find("a");
        Assert.NotNull(info);
        Assert.Equal("Movie", info!.Title);
        Assert.True(info.HasMedia);
        Assert.Equal("a", coordinator.ResolveMediaWindowId());
    }

    [Fact]
    public void UnknownIdsAreIgnored()
    {
        var coordinator = new WindowCoordinator();

        Assert.False(coordinator.Activate("missing"));
        Assert.False(coordinator.Close("missing"));
        Assert.False(coordinator.Update("missing", "x"));

        coordinator.Register("missing");
        coordinator.Register("missing");
        Assert.Equal(1, coordinator.Count);
    }

    [Fact]
    public void ClearEmptiesTheRegistry()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");
        coordinator.Register("b");

        coordinator.Clear();

        Assert.True(coordinator.ShouldExit);
        Assert.Empty(coordinator.Windows);
        Assert.Null(coordinator.ResolveMediaTargetId());
    }

    [Fact]
    public void ActiveWindowChangeRaisedWhenFocusMoves()
    {
        var coordinator = new WindowCoordinator();
        coordinator.Register("a");
        coordinator.Register("b");
        var changes = new List<string?>();
        coordinator.ActiveWindowChanged += changes.Add;

        coordinator.Activate("b");
        coordinator.Close("b");

        Assert.Equal(new[] { "b", "a" }, changes);
    }
}

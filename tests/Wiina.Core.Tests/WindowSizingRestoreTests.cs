using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Dragging the video content of a maximized window must restore it the way a
/// native title-bar drag does: the restored frame keeps the size it had before
/// maximizing and the cursor keeps the same relative grab point, so the window
/// does not jump out from under the pointer.
/// </summary>
public class WindowSizingRestoreTests
{
    private static readonly WindowRect Maximized = new(0, 0, 2000, 1000);

    private static readonly WindowRect Restore = new(0, 0, 1000, 500);

    [Fact]
    public void MiddleGrabKeepsTheCursorAtTheSameRelativePoint()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: 1000, grabY: 500, cursorX: 1500, cursorY: 800);

        Assert.Equal(new WindowRect(1000, 550, 2000, 1050), rect);
    }

    [Fact]
    public void TopLeftGrabPutsTheCornerUnderTheCursor()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: 0, grabY: 0, cursorX: 100, cursorY: 200);

        Assert.Equal(new WindowRect(100, 200, 1100, 700), rect);
    }

    [Fact]
    public void BottomRightGrabKeepsTheOppositeCornerUnderTheCursor()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: 2000, grabY: 1000, cursorX: 1900, cursorY: 1100);

        Assert.Equal(new WindowRect(900, 600, 1900, 1100), rect);
    }

    [Fact]
    public void RestoreSizeIsUsedVerbatim()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: 1000, grabY: 500, cursorX: 1500, cursorY: 800);

        Assert.NotNull(rect);
        Assert.Equal(1000, rect!.Value.Width);
        Assert.Equal(500, rect.Value.Height);
    }

    [Fact]
    public void FractionalGrabRatioUsesTheExactPoint()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: 500, grabY: 750, cursorX: 1000, cursorY: 1000);

        // 25% / 75% of a 1000x500 frame: 250 / 375.
        Assert.Equal(new WindowRect(750, 625, 1750, 1125), rect);
    }

    [Fact]
    public void RatiosRoundAwayFromZeroAtHalfPixels()
    {
        // 50% of 501 px is 250.5: banker's rounding would pick 250, the user
        // expects the nearest half to grow to 251.
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            new WindowRect(0, 0, 1002, 1000),
            new WindowRect(0, 0, 501, 500),
            grabX: 501,
            grabY: 500,
            cursorX: 1000,
            cursorY: 800);

        Assert.Equal(new WindowRect(749, 550, 1250, 1050), rect);
    }

    [Fact]
    public void GrabOutsideTheMaximizedFrameIsClamped()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: -100, grabY: -100, cursorX: 50, cursorY: 60);

        Assert.Equal(new WindowRect(50, 60, 1050, 560), rect);
    }

    [Fact]
    public void GrabBeyondTheMaximizedFrameIsClamped()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            Maximized, Restore, grabX: 5000, grabY: 5000, cursorX: 500, cursorY: 600);

        Assert.Equal(new WindowRect(-500, 100, 500, 600), rect);
    }

    [Fact]
    public void UnusableMaximizedBoundsCenterTheWindowUnderTheCursor()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            new WindowRect(0, 0, 0, 0), Restore, grabX: 10, grabY: 20, cursorX: 500, cursorY: 500);

        Assert.Equal(new WindowRect(0, 250, 1000, 750), rect);
    }

    [Fact]
    public void UnusableRestoreSizeReturnsNull()
    {
        Assert.Null(WindowSizing.RestoreUnderCursor(
            Maximized, new WindowRect(0, 0, 0, 0), grabX: 10, grabY: 10, cursorX: 10, cursorY: 10));
        Assert.Null(WindowSizing.RestoreUnderCursor(
            Maximized, new WindowRect(0, 0, 800, -5), grabX: 10, grabY: 10, cursorX: 10, cursorY: 10));
    }

    [Fact]
    public void SecondaryMonitorOriginIsHonoured()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            new WindowRect(1920, 0, 3920, 1000), Restore, grabX: 2920, grabY: 500, cursorX: 2920, cursorY: 800);

        Assert.Equal(new WindowRect(2420, 550, 3420, 1050), rect);
    }

    [Fact]
    public void RestoreFrameLargerThanTheMaximizedOneStillKeepsTheRatio()
    {
        WindowRect? rect = WindowSizing.RestoreUnderCursor(
            new WindowRect(0, 0, 800, 600),
            new WindowRect(0, 0, 1600, 900),
            grabX: 400,
            grabY: 300,
            cursorX: 1000,
            cursorY: 700);

        Assert.Equal(new WindowRect(200, 250, 1800, 1150), rect);
    }
}

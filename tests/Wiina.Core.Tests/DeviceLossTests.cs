using Wiina.Core.Playback;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class DeviceLossTests
{
    [Theory]
    [InlineData(unchecked((int)0x887A0005), DeviceLossSignal.Removed)]
    [InlineData(unchecked((int)0x887A0006), DeviceLossSignal.Hung)]
    [InlineData(unchecked((int)0x887A0007), DeviceLossSignal.Reset)]
    [InlineData(unchecked((int)0x887A0020), DeviceLossSignal.DriverError)]
    public void ClassifiesEachDxgiDeviceLossCode(int hr, DeviceLossSignal expected)
    {
        Assert.True(DeviceLoss.IsDeviceLoss(hr));
        Assert.Equal(expected, DeviceLoss.Classify(hr));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(unchecked((int)0x887A0001))] // DXGI_ERROR_INVALID_CALL
    [InlineData(unchecked((int)0x80004005))] // E_FAIL
    [InlineData(1)]
    public void TreatsUnrelatedHresultsAsHealthy(int hr)
    {
        Assert.False(DeviceLoss.IsDeviceLoss(hr));
        Assert.Equal(DeviceLossSignal.None, DeviceLoss.Classify(hr));
    }

    [Theory]
    [InlineData("vo/gpu-next/d3d11: Failed to create swapchain: 0x887a0005")]
    [InlineData("[ffmpeg] dxgi: device removed")]
    [InlineData("libplacebo: Device lost")]
    [InlineData("cplayer: GPU device hung, restarting video output")]
    [InlineData("D3D11: DXGI_ERROR_DEVICE_RESET")]
    [InlineData("d3d11: 0x887A0020")]
    public void DetectsDeviceLossInMpvLogLines(string line)
    {
        Assert.True(DeviceLoss.MentionsDeviceLoss(line));
        Assert.NotEqual(DeviceLossSignal.None, DeviceLoss.ClassifyLogLine(line));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cplayer: Playing: video.mp4")]
    [InlineData("vo/gpu-next/d3d11: Failed to create swapchain: 0x887a0001")]
    [InlineData("[ffmpeg] h264: device pixel format yuv420p")]
    public void IgnoresHealthyOrUnrelatedLines(string? line)
    {
        Assert.False(DeviceLoss.MentionsDeviceLoss(line));
        Assert.Equal(DeviceLossSignal.None, DeviceLoss.ClassifyLogLine(line));
    }

    [Fact]
    public void PrefersSpecificCodeOverGenericPhrase()
    {
        Assert.Equal(DeviceLossSignal.Hung, DeviceLoss.ClassifyLogLine("device removed? no: 0x887a0006 device hung"));
        Assert.Equal(DeviceLossSignal.Reset, DeviceLoss.ClassifyLogLine("0x887a0007 (device reset)"));
        Assert.Equal(DeviceLossSignal.Removed, DeviceLoss.ClassifyLogLine("device removed"));
    }

    [Fact]
    public void BudgetGrantsFirstAttemptAndIgnoresBurst()
    {
        var budget = new DeviceLossRecoveryBudget();
        var t0 = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        Assert.True(budget.TryBegin(t0));
        Assert.Equal(1, budget.Attempts);

        // A burst of failures right after the reinit must not stack attempts.
        Assert.False(budget.TryBegin(t0.AddMilliseconds(200)));
        Assert.False(budget.TryBegin(t0.AddSeconds(4)));
        Assert.Equal(1, budget.Attempts);

        Assert.True(budget.TryBegin(t0.AddSeconds(6)));
        Assert.Equal(2, budget.Attempts);
    }

    [Fact]
    public void BudgetStopsAfterThreeAttemptsWithinTheWindow()
    {
        var budget = new DeviceLossRecoveryBudget();
        var t0 = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        Assert.True(budget.TryBegin(t0));
        Assert.True(budget.TryBegin(t0.AddSeconds(10)));
        Assert.True(budget.TryBegin(t0.AddSeconds(20)));
        Assert.False(budget.CanAttempt);

        // Exhausted: no more reinit loops even long after the cooldown.
        Assert.False(budget.TryBegin(t0.AddSeconds(90)));
    }

    [Fact]
    public void BudgetRecoversAfterTheWindowElapses()
    {
        var budget = new DeviceLossRecoveryBudget();
        var t0 = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(budget.TryBegin(t0.AddSeconds(i * 10)));
        }

        Assert.False(budget.CanAttempt);
        Assert.True(budget.TryBegin(t0.Add(DeviceLossRecoveryBudget.DefaultWindow).AddSeconds(1)));
        Assert.Equal(1, budget.Attempts);
    }

    [Fact]
    public void ResetClearsTheBudget()
    {
        var budget = new DeviceLossRecoveryBudget();
        var t0 = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        Assert.True(budget.TryBegin(t0));
        budget.Reset();

        Assert.Equal(0, budget.Attempts);
        Assert.Null(budget.WindowStarted);
        Assert.Null(budget.LastAttempt);
        Assert.True(budget.TryBegin(t0));
    }
}

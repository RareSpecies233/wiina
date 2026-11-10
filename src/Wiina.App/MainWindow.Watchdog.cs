using Microsoft.UI.Dispatching;
using Wiina.App.Localization;
using Wiina.Core.Playback;
using Wiina.Mpv;

namespace Wiina.App;

/// <summary>
/// Long-play watchdog: samples mpv every two seconds and rebuilds the video
/// output when <c>time-pos</c> keeps advancing while
/// <c>estimated-frame-number</c> stands still. That is the "audio plays, screen
/// is black" state the device-loss path cannot see (no DXGI error is logged).
///
/// The decision logic lives in <see cref="RenderWatchdog"/>; this partial only
/// samples mpv, filters the states that are not evidence of a stall (pause,
/// seek, buffering, file switch, recovery in progress), traces, and calls the
/// existing recovery path.
///
/// Also hosts the sleep-resume / display-change handlers: both re-apply the
/// video layout, and ask for a one-shot video reload only when the frame
/// counter has actually gone stale.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan RenderWatchdogInterval = TimeSpan.FromSeconds(2);

    // A frame counter that has not moved for this long is treated as stale by
    // the power-resume / display-change paths. Two sampling intervals plus a
    // margin, so a normal 2 s tick never looks stale.
    private static readonly TimeSpan RenderProgressStaleAfter = TimeSpan.FromSeconds(5);

    private RenderWatchdog? _renderWatchdog;
    private DispatcherQueueTimer? _renderWatchdogTimer;
    private bool _renderWatchdogGaveUp;
    private string? _renderWatchdogPath;
    private long _renderWatchdogLastFrame = -1;
    private DateTimeOffset? _renderWatchdogLastProgressAt;
    private long _renderWatchdogSamples;

    private void InitializeRenderWatchdog()
    {
        if (_renderWatchdogTimer is not null)
        {
            return;
        }

        _renderWatchdog = new RenderWatchdog();
        _renderWatchdogTimer = DispatcherQueue.CreateTimer();
        _renderWatchdogTimer.Interval = RenderWatchdogInterval;
        _renderWatchdogTimer.IsRepeating = true;
        _renderWatchdogTimer.Tick += (_, _) => OnRenderWatchdogTick();
        _renderWatchdogTimer.Start();
        Trace(
            "[watchdog] started: interval 2 s, advance >= 3 s, 2 stalled samples, " +
            "2 recoveries / 10 min");
    }

    private void StopRenderWatchdog()
    {
        _renderWatchdogTimer?.Stop();
    }

    /// <summary>
    /// Fresh playback state: clears the sample baseline, the gave-up latch and
    /// the recovery budget, then makes sure the timer runs again. Called from
    /// the new-file path and after the user reloads the video output.
    /// </summary>
    private void ResetRenderWatchdog()
    {
        _renderWatchdog?.Reset();
        _renderWatchdogPath = null;
        _renderWatchdogLastFrame = -1;
        _renderWatchdogLastProgressAt = null;
        _renderWatchdogSamples = 0;
        _renderWatchdogGaveUp = false;
        _renderWatchdogTimer?.Start();
    }

    private void OnRenderWatchdogTick()
    {
        if (_closed || _closing)
        {
            _renderWatchdogTimer?.Stop();
            return;
        }

        MpvPlayer? player = _player;
        if (player is not { IsRunning: true })
        {
            _renderWatchdog?.ResetSamples();
            _renderWatchdogPath = null;
            _renderWatchdogLastFrame = -1;
            _renderWatchdogLastProgressAt = null;
            return;
        }

        double timePos;
        long frame;
        bool paused;
        bool seeking;
        bool buffering;
        string? path;
        try
        {
            timePos = player.GetPropertyDouble("time-pos");
            frame = player.GetPropertyInt64("estimated-frame-number");
            paused = player.GetPropertyFlag("pause");
            seeking = _seeking ||
                _seekingProperty ||
                player.GetPropertyFlag("seeking");
            buffering = _cacheBuffering ||
                _openingStream ||
                player.GetPropertyFlag("paused-for-cache") ||
                player.GetPropertyFlag("core-idle");
            path = player.GetPropertyString("path");
        }
        catch (Exception ex)
        {
            // A disposed handle during teardown must never take the UI thread
            // down; the next tick will see IsRunning false.
            Trace("[watchdog] sample failed: " + ex.Message);
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool switched = !string.Equals(path, _renderWatchdogPath, StringComparison.Ordinal);
        if (switched)
        {
            _renderWatchdogPath = path;
            _renderWatchdog?.ResetSamples();
        }

        // A presented frame is the only positive proof that video output is
        // alive; the resume/display paths use this timestamp as "has new
        // frames" evidence.
        if (!paused &&
            !seeking &&
            !buffering &&
            !switched &&
            frame > 0 &&
            frame > _renderWatchdogLastFrame)
        {
            _renderWatchdogLastFrame = frame;
            _renderWatchdogLastProgressAt = now;
        }

        var sample = new RenderSample(
            now,
            timePos,
            frame,
            paused,
            seeking,
            buffering,
            switched,
            _deviceRecoveryPending);
        RenderWatchdogDecision decision =
            _renderWatchdog?.Observe(sample) ?? RenderWatchdogDecision.Ignored;
        _renderWatchdogSamples++;

        if (decision.Action == RenderWatchdogAction.None)
        {
            // Keep the default trace light: one diagnostic line a minute at the
            // 2 s cadence, plus every action below.
            if (_renderWatchdogSamples % 30 == 0)
            {
                Trace(
                    $"[watchdog] pos={timePos:0.00} frame={frame} " +
                    $"paused={paused} seeking={seeking} buffering={buffering} " +
                    $"recoveries={_renderWatchdog?.RecoveriesInWindow ?? 0}");
            }
            return;
        }

        Trace(
            $"[watchdog] {decision.Action} pos={timePos:0.00} " +
            $"base={decision.BaselineTimePos:0.00} advance={decision.AdvanceSeconds:0.00}s " +
            $"frame={frame} stalled={decision.StalledSamples} " +
            $"recoveries={decision.RecoveriesInWindow}");

        if (decision.Action == RenderWatchdogAction.BudgetExhausted ||
            _deviceRecoveryFailed)
        {
            GiveUpRenderWatchdog();
            return;
        }

        _renderWatchdogSamples = 0;
        Trace("[watchdog] requesting video output rebuild (no frames presented)");
        RecoverFromDeviceLoss("no frames presented");
    }

    /// <summary>
    /// The automatic budget is spent (or the device-loss path already gave up):
    /// tell the user and stop retrying until a new file is opened.
    /// </summary>
    private void GiveUpRenderWatchdog()
    {
        if (_renderWatchdogGaveUp)
        {
            return;
        }

        _renderWatchdogGaveUp = true;
        _renderWatchdogTimer?.Stop();
        Trace("[watchdog] giving up; automatic recovery disabled until the next file");
        // Reuses the device-loss failure string (the catalog is outside this
        // task's write scope): both mean "no video output, playback continues".
        ShowOsd(Loc.T("osd.deviceLostFailed"));
    }

    /// <summary>
    /// True when playback is running but the frame counter has not moved
    /// recently. Used by the sleep-resume / display-change handlers so a
    /// harmless layout refresh does not trigger a video rebuild.
    /// </summary>
    private bool VideoOutputLooksStale()
    {
        if (_player is not { IsRunning: true } || !HasMedia())
        {
            return false;
        }

        if (_player.GetPropertyFlag("pause"))
        {
            return false;
        }

        return _renderWatchdogLastProgressAt is not { } at ||
            DateTimeOffset.UtcNow - at > RenderProgressStaleAfter;
    }

    /// <summary>Native WndProc callback: enqueue, never touch XAML here.</summary>
    private void OnPowerResumed()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _closing)
            {
                return;
            }

            Trace("power resume");
            ApplyVideoLayout();
            RequestVideoReload("power resume");
        });
    }

    /// <summary>Native WndProc callback: enqueue, never touch XAML here.</summary>
    private void OnDisplayChanged()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _closing)
            {
                return;
            }

            Trace("display change");
            ApplyVideoLayout();
            RequestVideoReload("display change");
        });
    }

    /// <summary>
    /// One-shot video rebuild after a power/display event, reusing the
    /// device-loss path (it already detaches the panel, issues
    /// <c>video-reload</c> and bounds the attempts). Only fires when the frame
    /// counter is actually stale; otherwise the layout refresh is enough.
    /// </summary>
    private void RequestVideoReload(string reason)
    {
        if (_deviceRecoveryPending || _deviceRecoveryFailed || _renderWatchdogGaveUp)
        {
            return;
        }

        if (!VideoOutputLooksStale())
        {
            Trace($"{reason}: frames still advancing, layout only");
            return;
        }

        Trace($"{reason}: video-reload requested");
        RecoverFromDeviceLoss(reason, quiet: true);
    }
}

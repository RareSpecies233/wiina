using Microsoft.UI.Dispatching;
using Wiina.App.Interop;

namespace Wiina.App;

/// <summary>
/// "Prevent sleep while playing": one power request per window, re-evaluated
/// from a 1 s timer so pause, stop, file switches and setting changes release
/// it without every playback call site having to remember. The request belongs
/// to this window, so PiP/mini keep it while they keep playing and closing one
/// window never releases another window's request.
/// </summary>
public sealed partial class MainWindow
{
    private readonly PowerKeepAwake _powerKeepAwake = new();
    private DispatcherQueueTimer? _powerKeepAwakeTimer;
    private bool _powerKeepAwakeReleased;

    private void InitializePowerKeepAwake()
    {
        if (_powerKeepAwakeTimer is not null)
        {
            return;
        }

        _powerKeepAwakeTimer = DispatcherQueue.CreateTimer();
        _powerKeepAwakeTimer.Interval = TimeSpan.FromSeconds(1);
        _powerKeepAwakeTimer.IsRepeating = true;
        _powerKeepAwakeTimer.Tick += (_, _) => UpdatePowerKeepAwake();
        _powerKeepAwakeTimer.Start();

        // Releasing on the way out matters as much as holding it: a leftover
        // request would keep the display awake after Wiina disappeared.
        Closed += (_, _) => ReleasePowerKeepAwake();
        try
        {
            AppWindow.Closing += (_, _) => ReleasePowerKeepAwake();
        }
        catch
        {
            // Some shells do not expose AppWindow events; Closed still covers
            // the normal teardown path.
        }

        UpdatePowerKeepAwake();
    }

    /// <summary>
    /// Holds the request only while media is actually playing. A paused,
    /// stopped or between-files window must let the display sleep like any
    /// other idle app. Called from the timer, the settings row and the shared
    /// settings broadcast.
    /// </summary>
    private void UpdatePowerKeepAwake()
    {
        bool shouldHold = !_closed &&
            _settings.PreventSleepWhilePlaying &&
            HasMedia() &&
            _player is { IsRunning: true } &&
            !_player.GetPropertyFlag("pause");

        _powerKeepAwake.Set(shouldHold);
    }

    private void ReleasePowerKeepAwake()
    {
        if (_powerKeepAwakeReleased)
        {
            return;
        }

        _powerKeepAwakeReleased = true;
        _powerKeepAwakeTimer?.Stop();
        _powerKeepAwake.Dispose();
    }
}

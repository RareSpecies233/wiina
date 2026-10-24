namespace Wiina.Core.State;

public enum OscVisibilityState
{
    Hidden,
    WillShow,
    Shown,
    WillHide,
}

/// <summary>
/// Pure state machine for the OSC/titlebar visibility plus the idle rules that
/// decide when the chrome may fade out. Animations, timers and hit testing stay
/// in the UI layer; time and pointer positions are passed in so every rule is
/// testable.
/// </summary>
public sealed class OscVisibilityController
{
    /// <summary>
    /// Real pointer movement must beat this many DIP on either axis. A hand
    /// resting on the mouse still emits sub-pixel moves, and those must not
    /// keep the OSC on screen forever.
    /// </summary>
    public const double MovementThreshold = 5;

    public const double DefaultAutoHideSeconds = 2.5;

    public const double MinAutoHideSeconds = 0.5;

    public const double MaxAutoHideSeconds = 30;

    private double _autoHideSeconds = DefaultAutoHideSeconds;
    private double? _anchorX;
    private double? _anchorY;

    public OscVisibilityState State { get; private set; } = OscVisibilityState.Shown;

    public bool AutoHideEnabled { get; set; } = true;

    public bool IsDragging { get; set; }

    /// <summary>PiP, interactive mode, live text, etc.</summary>
    public bool IsSuppressed { get; set; }

    /// <summary>Idle delay before the chrome fades out; clamped to 0.5–30 s.</summary>
    public double AutoHideSeconds
    {
        get => _autoHideSeconds;
        set => _autoHideSeconds = Math.Clamp(value, MinAutoHideSeconds, MaxAutoHideSeconds);
    }

    /// <summary>Timestamp of the last pointer activity the UI counted.</summary>
    public TimeSpan LastActivity { get; private set; }

    public bool CanHide => AutoHideEnabled && !IsDragging && !IsSuppressed;

    public OscVisibilityState Show()
    {
        if (State is OscVisibilityState.Shown or OscVisibilityState.WillShow)
        {
            return State;
        }

        State = OscVisibilityState.WillShow;
        return State;
    }

    public OscVisibilityState CompleteShow()
    {
        if (State == OscVisibilityState.WillShow)
        {
            State = OscVisibilityState.Shown;
        }

        return State;
    }

    public OscVisibilityState Hide(bool force = false)
    {
        if (!force && !CanHide)
        {
            return State;
        }

        if (State is OscVisibilityState.Hidden or OscVisibilityState.WillHide)
        {
            return State;
        }

        State = OscVisibilityState.WillHide;
        return State;
    }

    public OscVisibilityState CompleteHide()
    {
        if (State == OscVisibilityState.WillHide)
        {
            State = OscVisibilityState.Hidden;
        }

        return State;
    }

    public OscVisibilityState TimerElapsed() => Hide();

    public OscVisibilityState PointerEntered() => Show();

    public OscVisibilityState PointerExited() => Hide(force: true);

    // ------------------------------------------------------- idle tracking

    /// <summary>Marks pointer activity; entering the window and real moves count.</summary>
    public void MarkActivity(TimeSpan now) => LastActivity = now;

    /// <summary>
    /// Starts a fresh pointer anchor when the pointer enters the window.
    /// Entering is activity, so the OSC stays visible until the idle delay
    /// elapses again.
    /// </summary>
    public void EnterWindow(double x, double y, TimeSpan now)
    {
        _anchorX = x;
        _anchorY = y;
        MarkActivity(now);
    }

    /// <summary>
    /// Records the latest pointer position. Returns true only when the pointer
    /// actually moved by more than <see cref="MovementThreshold"/> DIP on an
    /// axis; sub-threshold jitter keeps the previous anchor, so it neither
    /// re-anchors nor resets the idle clock.
    /// </summary>
    public bool PointerMoved(double x, double y, TimeSpan now)
    {
        if (_anchorX is null || _anchorY is null)
        {
            _anchorX = x;
            _anchorY = y;
            MarkActivity(now);
            return true;
        }

        double anchorX = _anchorX.Value;
        double anchorY = _anchorY.Value;
        if (Math.Abs(x - anchorX) <= MovementThreshold &&
            Math.Abs(y - anchorY) <= MovementThreshold)
        {
            return false;
        }

        _anchorX = x;
        _anchorY = y;
        MarkActivity(now);
        return true;
    }

    /// <summary>Forgets the movement anchor, e.g. after the pointer left the window.</summary>
    public void ResetPointerAnchor()
    {
        _anchorX = null;
        _anchorY = null;
    }

    public bool IsIdle(TimeSpan now) =>
        now - LastActivity >= TimeSpan.FromSeconds(AutoHideSeconds);

    /// <summary>
    /// Timer tick: hide only after a full idle delay elapsed with no qualifying
    /// movement. The pointer may rest over the OSC itself; a resting pointer
    /// must not keep the chrome on screen forever.
    /// </summary>
    public bool ShouldHideOnIdle(TimeSpan now) => CanHide && IsIdle(now);

    /// <summary>
    /// Left click on the video: hides visible chrome even when auto-hide is
    /// disabled. A drag, a click on the chrome itself, or a suppressed layout
    /// never hides.
    /// </summary>
    public bool ShouldHideOnClick(bool chromeVisible, bool overChrome, bool isDrag) =>
        chromeVisible && !overChrome && !isDrag && !IsSuppressed && !IsDragging;

    /// <summary>
    /// Losing focus hides immediately, except in the docked layout (the chrome
    /// is part of the window frame there) and while a sidebar is open (the user
    /// is reading content, not watching the video).
    /// </summary>
    public static bool ShouldHideOnDeactivate(bool docked, bool sidebarVisible) =>
        !docked && !sidebarVisible;
}

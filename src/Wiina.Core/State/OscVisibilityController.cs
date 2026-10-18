namespace Wiina.Core.State;

public enum OscVisibilityState
{
    Hidden,
    WillShow,
    Shown,
    WillHide,
}

/// <summary>
/// Pure state machine for the OSC/titlebar visibility. UI code owns the
/// 2.5 s timer and the opacity animation; this class only decides transitions.
/// </summary>
public sealed class OscVisibilityController
{
    public OscVisibilityState State { get; private set; } = OscVisibilityState.Shown;

    public bool AutoHideEnabled { get; set; } = true;

    public bool IsDragging { get; set; }

    /// <summary>PiP, interactive mode, live text, etc.</summary>
    public bool IsSuppressed { get; set; }

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
}

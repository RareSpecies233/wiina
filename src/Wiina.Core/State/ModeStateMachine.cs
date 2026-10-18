using Wiina.Core.Models;

namespace Wiina.Core.State;

/// <summary>Why a mode transition did or did not happen.</summary>
public enum ModeTransitionReason
{
    None,
    SameMode,
    Reentrant,
    ApplyFailed,
    RolledBack,
}

/// <summary>Outcome of one <see cref="ModeStateMachine.Request"/> call.</summary>
public readonly record struct ModeTransitionResult(
    bool Success,
    PlayerMode From,
    PlayerMode To,
    ModeTransitionReason Reason,
    bool ExitPrevious)
{
    public static ModeTransitionResult Ok(
        PlayerMode from,
        PlayerMode to,
        bool exitPrevious) =>
        new(true, from, to, ModeTransitionReason.None, exitPrevious);

    public static ModeTransitionResult Fail(
        PlayerMode from,
        PlayerMode to,
        ModeTransitionReason reason) =>
        new(false, from, to, reason, false);
}

/// <summary>
/// Per-mode policy: always-on-top, whether the main video is handed to a
/// separate window, whether chrome is visible and whether the shell window is
/// compact.
/// </summary>
public readonly record struct ModePlan(
    PlayerMode Mode,
    bool IsAlwaysOnTop,
    bool HidesMainVideo,
    bool ShowsChrome,
    bool UsesCompactWindow);

/// <summary>Pure rules shared by the state machine and the tests.</summary>
public static class ModePolicy
{
    public static ModePlan For(PlayerMode mode) => mode switch
    {
        // Fullscreen drops always-on-top (spec §11.1) and hides the titlebar;
        // OSC still shows, so chrome is not "suppressed" in the docked sense.
        PlayerMode.Fullscreen => new ModePlan(
            mode,
            IsAlwaysOnTop: false,
            HidesMainVideo: false,
            ShowsChrome: true,
            UsesCompactWindow: false),

        // PiP keeps the main window alive but moves the video surface out.
        PlayerMode.PiP => new ModePlan(
            mode,
            IsAlwaysOnTop: false,
            HidesMainVideo: true,
            ShowsChrome: true,
            UsesCompactWindow: false),

        // Mini/music mode is a compact, always-on-top main-window mode.
        PlayerMode.Mini => new ModePlan(
            mode,
            IsAlwaysOnTop: true,
            HidesMainVideo: false,
            ShowsChrome: false,
            UsesCompactWindow: true),

        _ => new ModePlan(
            PlayerMode.Normal,
            IsAlwaysOnTop: false,
            HidesMainVideo: false,
            ShowsChrome: true,
            UsesCompactWindow: false),
    };

    /// <summary>Mutual exclusion: any different mode is a legal target.</summary>
    public static bool CanTransition(PlayerMode from, PlayerMode to) => from != to;

    /// <summary>
    /// Leaving Fullscreen/PiP/Mini for another non-Normal mode first tears the
    /// previous mode down (presenter, topmost, PiP surface) before entering the
    /// target. The caller sees this flag in the transition result.
    /// </summary>
    public static bool RequiresExitFirst(PlayerMode from, PlayerMode to) =>
        from != PlayerMode.Normal && from != to;
}

/// <summary>
/// Explicit Normal / Fullscreen / PiP / Mini state machine. The machine owns
/// the state and the rollback contract; the WinUI shell passes an
/// <c>apply</c> callback that performs the actual presenter/window work.
///
/// Contract:
/// - at most one non-Normal mode is active;
/// - switching between two non-Normal modes reports <c>ExitPrevious = true</c>;
/// - if <c>apply</c> returns false or throws, the state is left at the previous
///   mode and <c>apply(previous)</c> is invoked once, best effort, so partial
///   presenter/topmost changes are undone.
/// </summary>
public sealed class ModeStateMachine
{
    public PlayerMode Current { get; private set; } = PlayerMode.Normal;

    public PlayerMode Previous { get; private set; } = PlayerMode.Normal;

    public bool IsTransitioning { get; private set; }

    public event EventHandler<ModeTransitionResult>? Changed;

    public ModePlan CurrentPlan => ModePolicy.For(Current);

    public ModeTransitionResult Request(PlayerMode target, Func<PlayerMode, bool>? apply = null)
    {
        if (IsTransitioning)
        {
            return ModeTransitionResult.Fail(Current, target, ModeTransitionReason.Reentrant);
        }

        if (target == Current)
        {
            return ModeTransitionResult.Fail(Current, target, ModeTransitionReason.SameMode);
        }

        PlayerMode from = Current;
        bool exitPrevious = ModePolicy.RequiresExitFirst(from, target);
        ModeTransitionResult result;

        IsTransitioning = true;
        try
        {
            bool applied;
            try
            {
                applied = apply?.Invoke(target) ?? true;
            }
            catch
            {
                applied = false;
            }

            if (applied)
            {
                Previous = from;
                Current = target;
                result = ModeTransitionResult.Ok(from, target, exitPrevious);
            }
            else
            {
                result = ModeTransitionResult.Fail(from, target, ModeTransitionReason.ApplyFailed);
            }
        }
        finally
        {
            IsTransitioning = false;
        }

        if (!result.Success)
        {
            try
            {
                apply?.Invoke(from);
            }
            catch
            {
                // Rollback is best effort; the state never claims the target.
            }

            result = result with { Reason = ModeTransitionReason.RolledBack };
        }

        Changed?.Invoke(this, result);
        return result;
    }

    /// <summary>Test/diagnostic helper: drops back to Normal without callbacks.</summary>
    public void Reset()
    {
        Current = PlayerMode.Normal;
        Previous = PlayerMode.Normal;
    }
}

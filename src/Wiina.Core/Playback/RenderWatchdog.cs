namespace Wiina.Core.Playback;

/// <summary>
/// One render-health observation taken from mpv. The UI samples
/// <c>time-pos</c> and <c>estimated-frame-number</c> and fills the flags that
/// make a sample meaningless for stall detection (paused, seeking, buffering,
/// file switch, recovery in progress).
/// </summary>
public readonly record struct RenderSample(
    DateTimeOffset Timestamp,
    double TimePos,
    long FrameNumber,
    bool Paused = false,
    bool Seeking = false,
    bool Buffering = false,
    bool FileSwitching = false,
    bool RecoveryInProgress = false);

/// <summary>What the watchdog wants the host to do after a sample.</summary>
public enum RenderWatchdogAction
{
    /// <summary>Nothing to do; the sample was normal or not usable.</summary>
    None = 0,

    /// <summary>The video output stopped presenting; rebuild it once.</summary>
    Stalled = 1,

    /// <summary>The recovery budget for this playback is spent; stop retrying.</summary>
    BudgetExhausted = 2,
}

/// <summary>
/// Outcome of <see cref="RenderWatchdog.Observe"/>. The diagnostics fields are
/// what the host traces when it acts, so a support log explains the decision.
/// </summary>
public readonly record struct RenderWatchdogDecision(
    RenderWatchdogAction Action,
    double BaselineTimePos,
    double AdvanceSeconds,
    long FrameNumber,
    int StalledSamples,
    int RecoveriesInWindow)
{
    /// <summary>A decision that requires no host action.</summary>
    public static RenderWatchdogDecision Ignored => default;
}

/// <summary>
/// Detects "audio keeps playing but the video output stopped presenting".
///
/// The rule is deliberately narrow: while playback is running, a wall-clock
/// window in which <c>time-pos</c> advanced by at least
/// <see cref="DefaultMinAdvanceSeconds"/> but <c>estimated-frame-number</c> did
/// not move is a stalled renderer. Two consecutive such samples are required,
/// which tolerates a single slow tick. Paused, seeking, buffering and
/// file-switch samples reset the baseline, so a seek or a new file can never
/// look like a stall.
///
/// Recovery is budgeted separately from the device-loss budget: at most
/// <see cref="DefaultMaxRecoveries"/> automatic recoveries per
/// <see cref="DefaultWindow"/>. The host shows a message and stops retrying
/// once the budget is spent, rather than looping forever on broken hardware.
/// </summary>
public sealed class RenderWatchdog
{
    /// <summary>How far <c>time-pos</c> must advance before a sample can count.</summary>
    public const double DefaultMinAdvanceSeconds = 3.0;

    /// <summary>Consecutive stalled samples required to trigger a recovery.</summary>
    public const int DefaultRequiredSamples = 2;

    /// <summary>Automatic recoveries allowed inside <see cref="DefaultWindow"/>.</summary>
    public const int DefaultMaxRecoveries = 2;

    /// <summary>Rolling window that bounds automatic recoveries.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(10);

    private readonly double _minAdvanceSeconds;
    private readonly int _requiredSamples;
    private readonly int _maxRecoveries;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _recoveries = new();

    private bool _hasBaseline;
    private double _baselineTimePos;
    private long _baselineFrame;
    private int _stalledSamples;

    public RenderWatchdog(
        double minAdvanceSeconds = DefaultMinAdvanceSeconds,
        int requiredSamples = DefaultRequiredSamples,
        int maxRecoveries = DefaultMaxRecoveries,
        TimeSpan? window = null)
    {
        _minAdvanceSeconds = minAdvanceSeconds > 0
            ? minAdvanceSeconds
            : DefaultMinAdvanceSeconds;
        _requiredSamples = Math.Max(1, requiredSamples);
        _maxRecoveries = Math.Max(1, maxRecoveries);
        _window = window ?? DefaultWindow;
    }

    /// <summary>Recoveries recorded inside the rolling window.</summary>
    public int RecoveriesInWindow => _recoveries.Count;

    /// <summary>True once the rolling budget has no recovery left.</summary>
    public bool BudgetExhausted => RecoveriesInWindow >= _maxRecoveries;

    /// <summary>True while a baseline sample is being tracked.</summary>
    public bool HasBaseline => _hasBaseline;

    /// <summary>Consecutive samples that satisfied the stall condition.</summary>
    public int StalledSamples => _stalledSamples;

    /// <summary>
    /// Feeds one sample and returns the required action. The method is pure with
    /// respect to <see cref="RenderSample.Timestamp"/>: the caller owns the
    /// clock, so tests can drive any timeline without sleeping.
    /// </summary>
    public RenderWatchdogDecision Observe(RenderSample sample)
    {
        Prune(sample.Timestamp);

        if (sample.Paused ||
            sample.Seeking ||
            sample.Buffering ||
            sample.FileSwitching ||
            sample.RecoveryInProgress ||
            !double.IsFinite(sample.TimePos) ||
            sample.TimePos < 0 ||
            sample.FrameNumber <= 0)
        {
            // Not evidence of a stall: drop the baseline so the 3 s window can
            // never span a pause, seek, buffer, file switch or recovery.
            ResetSamples();
            return RenderWatchdogDecision.Ignored;
        }

        if (!_hasBaseline)
        {
            SetBaseline(sample);
            return RenderWatchdogDecision.Ignored;
        }

        if (sample.FrameNumber > _baselineFrame)
        {
            // Frames are being presented again.
            SetBaseline(sample);
            return RenderWatchdogDecision.Ignored;
        }

        double advance = sample.TimePos - _baselineTimePos;
        if (advance < 0)
        {
            // The position moved backwards (seek, loop or reload): rebaseline.
            SetBaseline(sample);
            return RenderWatchdogDecision.Ignored;
        }

        if (advance < _minAdvanceSeconds)
        {
            // Keep the original baseline so the window accumulates across
            // samples instead of restarting on every tick.
            return RenderWatchdogDecision.Ignored;
        }

        _stalledSamples++;
        double baseline = _baselineTimePos;
        if (_stalledSamples < _requiredSamples)
        {
            return new RenderWatchdogDecision(
                RenderWatchdogAction.None,
                baseline,
                advance,
                sample.FrameNumber,
                _stalledSamples,
                RecoveriesInWindow);
        }

        int stalled = _stalledSamples;
        ResetSamples();

        if (RecoveriesInWindow >= _maxRecoveries)
        {
            return new RenderWatchdogDecision(
                RenderWatchdogAction.BudgetExhausted,
                baseline,
                advance,
                sample.FrameNumber,
                stalled,
                RecoveriesInWindow);
        }

        _recoveries.Enqueue(sample.Timestamp);
        return new RenderWatchdogDecision(
            RenderWatchdogAction.Stalled,
            baseline,
            advance,
            sample.FrameNumber,
            stalled,
            RecoveriesInWindow);
    }

    /// <summary>
    /// Clears the baseline and the consecutive-stall counter, keeping the
    /// recovery budget. The host calls this after a seek or a file switch.
    /// </summary>
    public void ResetSamples()
    {
        _hasBaseline = false;
        _baselineTimePos = 0;
        _baselineFrame = 0;
        _stalledSamples = 0;
    }

    /// <summary>
    /// Clears the recovery budget as well as the sample state. Called when a new
    /// file is opened: a budget spent on the previous file must not disable
    /// automatic recovery for the rest of the process.
    /// </summary>
    public void Reset()
    {
        ResetSamples();
        _recoveries.Clear();
    }

    private void SetBaseline(RenderSample sample)
    {
        _hasBaseline = true;
        _baselineTimePos = sample.TimePos;
        _baselineFrame = sample.FrameNumber;
        _stalledSamples = 0;
    }

    private void Prune(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - _window;
        while (_recoveries.Count > 0 && _recoveries.Peek() < cutoff)
        {
            _recoveries.Dequeue();
        }
    }
}

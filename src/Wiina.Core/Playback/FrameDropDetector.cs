namespace Wiina.Core.Playback;

/// <summary>
/// One read of the frame counters mpv exposes. All values are cumulative since
/// the file was opened and are reset by mpv on seek/file change.
/// </summary>
/// <param name="Dropped">
/// <c>frame-drop-count</c> (old alias <c>vo-drop-frame-count</c>): frames the
/// video output refused to present.
/// </param>
/// <param name="Mistimed">
/// <c>mistimed-frame-count</c>: frames presented but late enough to be a visible
/// stutter. Composition outputs can report jank here and nowhere else.
/// </param>
/// <param name="Delayed">
/// <c>vo-delayed-frame-count</c>: frames the video output delayed (usually
/// recovered, so it is reported but not counted as a drop).
/// </param>
/// <param name="DecoderDropped">
/// <c>decoder-dropped</c>: frames thrown away by the decoder before rendering.
/// </param>
public readonly record struct FrameDropCounters(
    long Dropped,
    long Mistimed,
    long Delayed,
    long DecoderDropped)
{
    /// <summary>
    /// Drops that actually hurt playback. Delayed frames are excluded because
    /// the VO recovered them; they are still reported separately.
    /// </summary>
    public long Losses => Math.Max(0, Dropped) + Math.Max(0, Mistimed) + Math.Max(0, DecoderDropped);

    /// <summary>True when every counter read was zero.</summary>
    public bool IsZero => Dropped <= 0 && Mistimed <= 0 && Delayed <= 0 && DecoderDropped <= 0;
}

/// <summary>Why a measurement window cannot be used.</summary>
public enum FrameDropDiscontinuity
{
    None = 0,
    NotPlaying = 1,
    CounterReset = 2,
}

/// <summary>
/// A rate that crossed the reporting threshold, ready to be shown to the user.
/// </summary>
/// <param name="Drops">Drops accumulated inside the measurement window.</param>
/// <param name="Seconds">Length of the measurement window.</param>
/// <param name="PerMinute">Extrapolated drops per minute.</param>
/// <param name="TotalDrops">Drops accumulated since the last reset.</param>
public readonly record struct FrameDropNotice(
    long Drops,
    double Seconds,
    double PerMinute,
    long TotalDrops);

/// <summary>
/// Sliding-window frame-drop detector for libmpv playback.
///
/// <para>The old implementation compared a single counter
/// (<c>vo-drop-frame-count</c>) once per second and reported every increase
/// immediately. Under <c>vo=gpu-next</c> + <c>d3d11</c> composition mpv often
/// reports drops through other counters (or not at all), so it usually showed
/// nothing; when it did fire, the message was a raw delta with no notion of
/// rate and could repeat every second.</para>
///
/// <para>This detector reads all counters, keeps a window of the last few
/// seconds, and reports a rate. It is deliberately conservative:</para>
/// <list type="bullet">
///   <item>a counter that goes backwards is a seek/file change and starts a new
///     baseline instead of producing a negative delta;</item>
///   <item>samples taken while paused are ignored (the video output is not
///     presenting frames, so a jump there is not playback jank);</item>
///   <item>notices are rate-limited by a cooldown and a minimum rate, so one
///     hiccup cannot produce a stream of OSD messages.</item>
/// </list>
/// The class has no WinUI or mpv dependency so the logic is unit-testable.
/// </summary>
public sealed class FrameDropDetector
{
    public const double DefaultWindowSeconds = 3.0;
    public const double DefaultCooldownSeconds = 12.0;
    public const double DefaultMinimumPerMinute = 6.0;
    public const long DefaultMinimumDrops = 3;

    private readonly double _windowSeconds;
    private readonly double _cooldownSeconds;
    private readonly double _minimumPerMinute;
    private readonly long _minimumDrops;
    private readonly List<(double Time, double Cumulative)> _samples = new(16);

    private FrameDropCounters _lastCounters;
    private bool _hasCounters;
    private double _cumulative;
    private double _lastNoticeTime = double.NegativeInfinity;

    public FrameDropDetector(
        double windowSeconds = DefaultWindowSeconds,
        double cooldownSeconds = DefaultCooldownSeconds,
        double minimumPerMinute = DefaultMinimumPerMinute,
        long minimumDrops = DefaultMinimumDrops)
    {
        if (!double.IsFinite(windowSeconds) || windowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        }

        _windowSeconds = windowSeconds;
        _cooldownSeconds = Math.Max(0, cooldownSeconds);
        _minimumPerMinute = Math.Max(0, minimumPerMinute);
        _minimumDrops = Math.Max(1, minimumDrops);
    }

    /// <summary>Drops accumulated inside the current window.</summary>
    public long WindowDrops { get; private set; }

    /// <summary>Window rate in drops per minute (0 when the window is empty).</summary>
    public double PerMinute { get; private set; }

    /// <summary>Drops accumulated since the detector was reset.</summary>
    public long TotalDrops { get; private set; }

    /// <summary>Whether the current window holds enough time to trust a rate.</summary>
    public bool HasWindow => _samples.Count >= 2 && WindowSeconds > 0;

    /// <summary>Duration covered by the current window, in seconds.</summary>
    public double WindowSeconds => _samples.Count < 2 ? 0 : _samples[^1].Time - _samples[0].Time;

    /// <summary>Last counters read, for the statistics panel.</summary>
    public FrameDropCounters LastCounters => _lastCounters;

    /// <summary>Last discontinuity seen, for diagnostics.</summary>
    public FrameDropDiscontinuity LastDiscontinuity { get; private set; }

    /// <summary>
    /// Starts a new measurement baseline. Call on seek, file change, or when
    /// playback restarts; the detector also rebases on its own when it sees a
    /// counter going backwards.
    /// </summary>
    public void Reset()
    {
        _samples.Clear();
        _hasCounters = false;
        _cumulative = 0;
        TotalDrops = 0;
        WindowDrops = 0;
        PerMinute = 0;
        LastDiscontinuity = FrameDropDiscontinuity.None;
    }

    /// <summary>
    /// Feeds one counter read. <paramref name="nowSeconds"/> is a monotonic
    /// clock (not the media position: seeks jump that). When
    /// <paramref name="playing"/> is false the read only rebases the counters;
    /// it can never produce a rate.
    /// </summary>
    public void Sample(double nowSeconds, FrameDropCounters counters, bool playing)
    {
        if (!double.IsFinite(nowSeconds))
        {
            return;
        }

        if (!playing)
        {
            // Paused: the VO is not presenting, so deltas here are meaningless.
            Rebase(nowSeconds, counters, FrameDropDiscontinuity.NotPlaying);
            return;
        }

        if (!_hasCounters)
        {
            Rebase(nowSeconds, counters, FrameDropDiscontinuity.None);
            return;
        }

        if (counters.Dropped < _lastCounters.Dropped ||
            counters.Mistimed < _lastCounters.Mistimed ||
            counters.Delayed < _lastCounters.Delayed ||
            counters.DecoderDropped < _lastCounters.DecoderDropped)
        {
            // mpv resets the counters on seek; a jump backwards is not jank.
            _lastCounters = counters;
            Rebase(nowSeconds, counters, FrameDropDiscontinuity.CounterReset);
            return;
        }

        long losses = counters.Losses - _lastCounters.Losses;
        _lastCounters = counters;
        LastDiscontinuity = FrameDropDiscontinuity.None;
        if (losses > 0)
        {
            _cumulative += losses;
            TotalDrops += losses;
        }

        _samples.Add((nowSeconds, _cumulative));

        // Drop samples that fell out of the window, but keep one older sample so
        // the window boundary is interpolated instead of rounded down.
        while (_samples.Count > 2 && nowSeconds - _samples[1].Time > _windowSeconds)
        {
            _samples.RemoveAt(0);
        }

        double span = WindowSeconds;
        double drops = _samples.Count < 2
            ? 0
            : _samples[^1].Cumulative - _samples[0].Cumulative;
        WindowDrops = (long)Math.Round(Math.Max(0, drops));
        PerMinute = span > 0.05 ? Math.Max(0, drops) / span * 60.0 : 0;
    }

    /// <summary>
    /// Returns a notice when the current rate is worth interrupting the user
    /// with, at most once per cooldown. The caller formats the message.
    /// </summary>
    public bool TryTakeNotice(double nowSeconds, out FrameDropNotice notice)
    {
        notice = default;
        if (!HasWindow ||
            WindowDrops < _minimumDrops ||
            PerMinute < _minimumPerMinute)
        {
            return false;
        }

        if (nowSeconds - _lastNoticeTime < _cooldownSeconds)
        {
            return false;
        }

        _lastNoticeTime = nowSeconds;
        notice = new FrameDropNotice(
            WindowDrops,
            WindowSeconds,
            PerMinute,
            TotalDrops);
        return true;
    }

    private void Rebase(
        double nowSeconds,
        FrameDropCounters counters,
        FrameDropDiscontinuity reason)
    {
        if (reason != FrameDropDiscontinuity.None)
        {
            LastDiscontinuity = reason;
            _samples.Clear();
            WindowDrops = 0;
            PerMinute = 0;
        }

        _lastCounters = counters;
        _hasCounters = true;
        _cumulative = 0;
        _samples.Add((nowSeconds, _cumulative));
    }
}

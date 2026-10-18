namespace Wiina.Core.Osd;

/// <summary>
/// One-message-at-a-time OSD queue. Every enqueue replaces the visible message;
/// rapid continuous messages (volume/seek/speed/cache updates) additionally
/// coalesce so the UI updates the current card instead of restacking it.
/// </summary>
public sealed class OsdQueue
{
    private static readonly HashSet<OsdMessageKind> ContinuousKinds = new()
    {
        OsdMessageKind.Volume,
        OsdMessageKind.Seek,
        OsdMessageKind.Speed,
        OsdMessageKind.Buffer,
    };

    public OsdMessage? Current { get; private set; }

    /// <summary>Increments on every accepted message.</summary>
    public long Version { get; private set; }

    public OsdQueueResult Enqueue(OsdMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        bool coalesced = ShouldCoalesce(Current, message);
        Current = message;
        Version++;
        return new OsdQueueResult(message, coalesced, Version);
    }

    public void Clear()
    {
        Current = null;
        Version++;
    }

    public static bool ShouldCoalesce(OsdMessage? previous, OsdMessage next) =>
        previous is not null &&
        previous.Kind == next.Kind &&
        ContinuousKinds.Contains(next.Kind);

    public static bool IsContinuous(OsdMessageKind kind) => ContinuousKinds.Contains(kind);
}

public readonly record struct OsdQueueResult(
    OsdMessage Message,
    bool Coalesced,
    long Version);

/// <summary>
/// Clamping shared by settings, the quick-settings UI and the renderer.
/// </summary>
public static class OsdSettings
{
    public const double MinSizePercent = 1;
    public const double MaxSizePercent = 500;
    public const double DefaultSizePercent = 100;
    public const double MinDurationSeconds = 0.1;
    public const double MaxDurationSeconds = 30;
    public const double DefaultDurationSeconds = 1;
    public const double DefaultBaseFontSize = 20;
    public const double FadeOutSeconds = 0.5;

    public static double ClampSize(double percent) =>
        !double.IsFinite(percent)
            ? DefaultSizePercent
            : Math.Clamp(percent, MinSizePercent, MaxSizePercent);

    public static double ClampDuration(double seconds) =>
        !double.IsFinite(seconds)
            ? DefaultDurationSeconds
            : Math.Clamp(seconds, MinDurationSeconds, MaxDurationSeconds);

    public static double MainFontSize(double percent) =>
        DefaultBaseFontSize * ClampSize(percent) / 100.0;

    /// <summary>Secondary text is half the main size, clamped to 10–25 DIP.</summary>
    public static double SecondaryFontSize(double percent) =>
        Math.Clamp(MainFontSize(percent) * 0.5, 10, 25);

    public static double IconFontSize(double percent) =>
        MainFontSize(percent) * 0.8;

    public static TimeSpan Duration(double seconds) =>
        TimeSpan.FromSeconds(ClampDuration(seconds));
}

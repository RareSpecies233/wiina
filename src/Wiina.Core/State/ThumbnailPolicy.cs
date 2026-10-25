using Wiina.Core.Settings;

namespace Wiina.Core.State;

/// <summary>
/// Thumbnail work policy shared by the settings rows and the extraction service
/// (spec §10.3). Pre-generation walks the whole timeline (one seek + decode per
/// bucket, so a two hour file is ~1440 frames) on a second mpv instance, which
/// is what stutters on weak machines; slow mode opts out of that pass and keeps
/// a single frame-at-a-time worker instead, and instant mode never walks the
/// timeline at all.
/// </summary>
public static class ThumbnailPolicy
{
    /// <summary>Pause between two extractions while slow mode is on (milliseconds).</summary>
    public const int SlowModeCooldownMilliseconds = 250;

    /// <summary>Upper bound for the instant-mode hover delay (milliseconds).</summary>
    public const int MaxHoverDelayMilliseconds = 2_000;

    /// <summary>Default instant-mode hover delay (milliseconds).</summary>
    public const int DefaultHoverDelayMilliseconds = 250;

    /// <summary>
    /// True when the background pass over the whole file may start. Slow mode,
    /// instant mode, a disabled preview, or a disabled disk cache (0 MiB) all
    /// say no; cached frames are still served in every case.
    /// </summary>
    public static bool AllowsPreGeneration(
        bool thumbnailPreviewEnabled,
        bool slowMode,
        int diskCacheMegabytes,
        ThumbnailMode mode = ThumbnailMode.PreGenerate) =>
        thumbnailPreviewEnabled
        && !slowMode
        && mode != ThumbnailMode.Instant
        && diskCacheMegabytes > 0;

    /// <summary>
    /// Instant mode extracts on demand and must not touch the disk cache:
    /// writing one PNG per hovered position would grow the cache without a
    /// known bound. Pre-generation mode keeps the existing disk behaviour.
    /// </summary>
    public static bool AllowsDiskCache(ThumbnailMode mode) =>
        mode != ThumbnailMode.Instant;

    /// <summary>Clamps a hover delay from settings or the UI to 0–2000 ms.</summary>
    public static int ClampHoverDelayMilliseconds(int milliseconds) =>
        Math.Clamp(milliseconds, 0, MaxHoverDelayMilliseconds);

    /// <summary>
    /// True when a hover request should wait before extracting. Instant mode
    /// always waits (the delay is what prevents scrubbing from queueing a
    /// decode per pixel); pre-generation answers from its warm cache instead.
    /// </summary>
    public static bool RequiresHoverDelay(ThumbnailMode mode) =>
        mode == ThumbnailMode.Instant;
}

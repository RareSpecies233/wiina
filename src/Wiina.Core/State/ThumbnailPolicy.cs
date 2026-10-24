namespace Wiina.Core.State;

/// <summary>
/// Thumbnail work policy shared by the settings row and the extraction service
/// (spec §10.3). Pre-generation walks the whole timeline (one seek + decode per
/// bucket, so a two hour file is ~1440 frames) on a second mpv instance, which
/// is what stutters on weak machines; slow mode opts out of that pass and keeps
/// a single frame-at-a-time worker instead.
/// </summary>
public static class ThumbnailPolicy
{
    /// <summary>Pause between two extractions while slow mode is on (milliseconds).</summary>
    public const int SlowModeCooldownMilliseconds = 250;

    /// <summary>
    /// True when the background pass over the whole file may start. Slow mode,
    /// a disabled preview, or a disabled disk cache (0 MiB) all say no; cached
    /// frames are still served in every case.
    /// </summary>
    public static bool AllowsPreGeneration(
        bool thumbnailPreviewEnabled,
        bool slowMode,
        int diskCacheMegabytes) =>
        thumbnailPreviewEnabled && !slowMode && diskCacheMegabytes > 0;
}

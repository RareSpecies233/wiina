namespace Wiina.Core.Caching;

/// <summary>
/// Sizing rules for the in-memory timeline thumbnail cache. The cache has to be
/// able to hold every time bucket of the current video, so a hover anywhere on
/// the timeline is a memory hit once the file has been pre-generated. The
/// user-configured "cache count" is only a floor for short clips.
/// </summary>
public static class ThumbnailCacheSizing
{
    /// <summary>
    /// Buckets kept on top of the source length, so a seek slightly past the
    /// reported duration (or a rounded-up bucket) still lands on a cached frame.
    /// </summary>
    public const int BucketPadding = 2;

    /// <summary>Bytes per pixel of a <c>ThumbnailFrame</c> (BGRA8).</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// Number of buckets needed to cover <paramref name="durationSeconds"/>,
    /// never below <paramref name="minimumFrames"/>. Unknown or non-positive
    /// durations (a live stream, a failed probe) fall back to the floor so the
    /// cache stays bounded.
    /// </summary>
    public static int FramesForDuration(
        double durationSeconds,
        double bucketSeconds,
        int minimumFrames = 1)
    {
        int floor = Math.Max(1, minimumFrames);
        if (!double.IsFinite(bucketSeconds) || bucketSeconds <= 0)
        {
            bucketSeconds = 5;
        }

        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return floor;
        }

        double frames = Math.Ceiling(durationSeconds / bucketSeconds) + BucketPadding;
        return frames >= int.MaxValue ? int.MaxValue : Math.Max(floor, (int)frames);
    }

    /// <summary>Bytes taken by one decoded BGRA8 frame of the given size.</summary>
    public static long FrameBytes(int width, int height) =>
        (long)Math.Max(1, width) * Math.Max(1, height) * BytesPerPixel;

    /// <summary>Bytes taken by <paramref name="frames"/> frames of equal size.</summary>
    public static long EstimateBytes(int frames, long bytesPerFrame) =>
        Math.Max(0, frames) * Math.Max(0, bytesPerFrame);

    /// <summary>Megabytes for logging, with one decimal.</summary>
    public static double Megabyte(long bytes) => bytes / (1024.0 * 1024.0);
}

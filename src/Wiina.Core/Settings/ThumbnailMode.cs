namespace Wiina.Core.Settings;

/// <summary>
/// How timeline thumbnails are produced. <see cref="PreGenerate"/> warms every
/// bucket of the file in the background (disk cache + memory cache), giving the
/// fastest hover at the cost of a whole-file decode pass. <see cref="Instant"/>
/// skips that pass entirely: the frame is extracted only for the time the user
/// is actually hovering, after a short adjustable delay, and nothing is written
/// to the disk cache.
/// </summary>
public enum ThumbnailMode
{
    /// <summary>Warm the whole timeline in the background (default).</summary>
    PreGenerate = 0,

    /// <summary>Extract only the hovered time point, after the hover delay.</summary>
    Instant = 1,
}

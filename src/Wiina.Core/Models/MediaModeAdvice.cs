namespace Wiina.Core.Models;

/// <summary>
/// Pure media-shape advice for the Stage 6 mode switch. mpv reports embedded
/// cover art as a video track with the <c>image</c>/<c>albumart</c> flag, so a
/// file with only such a track is still audio-only for the mini player.
/// </summary>
public static class MediaModeAdvice
{
    public static bool IsAudioOnly(IReadOnlyList<TrackInfo> tracks)
    {
        if (tracks.Count == 0)
        {
            return false;
        }

        return !tracks.Any(track => track.Type == TrackType.Video && !track.IsAlbumArt);
    }

    public static bool HasAlbumArt(IReadOnlyList<TrackInfo> tracks) =>
        tracks.Any(track => track.Type == TrackType.Video && track.IsAlbumArt);

    public static bool HasRealVideo(IReadOnlyList<TrackInfo> tracks) =>
        tracks.Any(track => track.Type == TrackType.Video && !track.IsAlbumArt);
}

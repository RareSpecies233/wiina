namespace Wiina.Core.Models;

public enum TrackType
{
    Video,
    Audio,
    Subtitle,
}

/// <summary>
/// One entry of mpv's <c>track-list</c>, mapped into a WinUI-independent model.
/// </summary>
public sealed record TrackInfo(
    int Id,
    TrackType Type,
    string? Title = null,
    string? Language = null,
    string? Codec = null,
    bool IsDefault = false,
    bool IsSelected = false,
    bool IsExternal = false)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title;
            }

            string type = Type switch
            {
                TrackType.Video => "视频",
                TrackType.Audio => "音频",
                _ => "字幕",
            };

            if (!string.IsNullOrWhiteSpace(Language))
            {
                return $"{type} · {Language}";
            }

            return $"{type} #{Id}";
        }
    }
}

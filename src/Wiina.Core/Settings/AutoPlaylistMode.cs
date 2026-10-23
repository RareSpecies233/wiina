namespace Wiina.Core.Settings;

/// <summary>
/// Controls how opening a single media file fills the playlist.
/// </summary>
public enum AutoPlaylistMode
{
    /// <summary>Only media files next to the opened file.</summary>
    SameFolder = 0,

    /// <summary>Open exactly what the user picked; never scan siblings.</summary>
    Manual = 1,

    /// <summary>Media files in the opened file's folder and all subfolders.</summary>
    IncludeSubfolders = 2,
}

namespace Wiina.Core.Files;

/// <summary>
/// Per-user file association metadata (issue 11). Wiina is unpackaged, so it
/// cannot use the MSIX <c>uap:FileTypeAssociation</c> extension: registration
/// goes into <c>HKCU\Software\Classes</c> plus the Default Apps capability
/// keys, and Windows still asks the user to confirm the default handler.
/// The lists and the registry shape live in Core so they stay testable; the
/// WinUI layer only performs the actual registry writes.
/// </summary>
public static class FileAssociationCatalog
{
    public const string VideoProgId = "Wiina.Video";
    public const string AudioProgId = "Wiina.Audio";
    public const string ApplicationName = "Wiina";

    /// <summary>Capabilities key referenced by <c>RegisteredApplications</c>.</summary>
    public const string CapabilitiesPath = @"Software\Wiina\Capabilities";

    public static IReadOnlyList<string> VideoExtensions { get; } = new[]
    {
        "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v",
        "ts", "m2ts", "mpg", "mpeg", "3gp", "ogv", "rmvb",
    };

    public static IReadOnlyList<string> AudioExtensions { get; } = new[]
    {
        "mp3", "flac", "aac", "wav", "m4a", "ogg", "opus", "wma", "ape",
    };

    /// <summary>Command line stored under <c>shell\open\command</c>.</summary>
    public static string OpenCommand(string executablePath) =>
        $"\"{executablePath}\" \"%1\"";

    public static string IconCommand(string executablePath) =>
        $"\"{executablePath}\",0";

    public static string ExtensionsText(bool video, bool audio)
    {
        var parts = new List<string>();
        if (video)
        {
            parts.Add($"{VideoExtensions.Count} 种视频格式（{string.Join("、", VideoExtensions)}）");
        }

        if (audio)
        {
            parts.Add($"{AudioExtensions.Count} 种音频格式（{string.Join("、", AudioExtensions)}）");
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// One registry value that registration must write. <see cref="ValueName"/>
    /// null means the key's default value; <see cref="Binary"/> true writes an
    /// empty REG_NONE-style marker (used by <c>OpenWithProgids</c>).
    /// </summary>
    public sealed record Entry(string KeyPath, string? ValueName, string Value, bool Binary = false);

    /// <summary>
    /// Full set of values for the requested handler kinds, with key paths
    /// relative to <c>HKEY_CURRENT_USER</c>. Deterministic so tests can assert
    /// exact coverage without touching the registry.
    /// </summary>
    public static IReadOnlyList<Entry> BuildPlan(
        string executablePath,
        bool video,
        bool audio)
    {
        var entries = new List<Entry>();
        string exe = executablePath;

        void AddProgId(string progId, string friendlyName, IReadOnlyList<string> extensions)
        {
            string classes = $@"Software\Classes\{progId}";
            entries.Add(new Entry(classes, null, friendlyName));
            entries.Add(new Entry($@"{classes}\DefaultIcon", null, IconCommand(exe)));
            entries.Add(new Entry($@"{classes}\shell\open\command", null, OpenCommand(exe)));
            entries.Add(new Entry($@"{classes}\Application", "ApplicationName", ApplicationName));

            foreach (string extension in extensions)
            {
                entries.Add(new Entry(
                    $@"Software\Classes\.{extension}\OpenWithProgids",
                    progId,
                    string.Empty,
                    Binary: true));
            }
        }

        if (video)
        {
            AddProgId(VideoProgId, "Wiina 视频", VideoExtensions);
        }

        if (audio)
        {
            AddProgId(AudioProgId, "Wiina 音频", AudioExtensions);
        }

        // Default Apps (Windows 10/11 settings) reads this shape.
        entries.Add(new Entry(CapabilitiesPath, "ApplicationName", ApplicationName));
        entries.Add(new Entry(
            CapabilitiesPath,
            "ApplicationDescription",
            "Wiina 媒体播放器"));
        entries.Add(new Entry(CapabilitiesPath, "ApplicationIcon", IconCommand(exe)));
        foreach (string extension in video ? VideoExtensions : Array.Empty<string>())
        {
            entries.Add(new Entry(
                $@"{CapabilitiesPath}\FileAssociations",
                $".{extension}",
                VideoProgId));
        }

        foreach (string extension in audio ? AudioExtensions : Array.Empty<string>())
        {
            entries.Add(new Entry(
                $@"{CapabilitiesPath}\FileAssociations",
                $".{extension}",
                AudioProgId));
        }

        entries.Add(new Entry(
            @"Software\RegisteredApplications",
            ApplicationName,
            CapabilitiesPath));

        return entries;
    }

    /// <summary>ProgIds that the uninstall path is allowed to delete.</summary>
    public static IReadOnlyList<string> OwnedProgIds { get; } = new[]
    {
        VideoProgId,
        AudioProgId,
    };
}

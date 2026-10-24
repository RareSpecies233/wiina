namespace Wiina.Core.Release;

/// <summary>
/// The single source of truth for the v1 portable package layout. It is kept
/// in Wiina.Core (which has no UI or platform dependencies) so the packaging
/// script and the test suite can verify the same list.
/// </summary>
public static class PackageManifest
{
    /// <summary>Product version stamped into the archive name and SOURCE.txt.</summary>
    public const string Version = "1.2.3";

    /// <summary>Runtime identifier of the self-contained payload.</summary>
    public const string RuntimeIdentifier = "win-x64";

    /// <summary>Name of the archive written under artifacts/.</summary>
    public const string ArchiveFileName = "wiina-1.2.3-win-x64.zip";

    /// <summary>Executable entry point of the packaged player.</summary>
    public const string ExecutableFileName = "Wiina.exe";

    /// <summary>The dynamically linked libmpv binaries; never committed.</summary>
    public const string LibMpvFileName = "libmpv-2.dll";

    /// <summary>
    /// Files that must be present next to Wiina.exe in the staged payload.
    /// "libmpv-2.dll" is copied from third_party/libmpv; the remaining files
    /// are copied from the repository root.
    /// </summary>
    public static IReadOnlyList<string> RequiredPayloadFiles { get; } = new[]
    {
        ExecutableFileName,
        LibMpvFileName,
        "LICENSE",
        "NOTICE",
        "README.md",
        "SOURCE.txt",
    };

    /// <summary>Legal/source files copied verbatim from the repository root.</summary>
    public static IReadOnlyList<string> RequiredRootFiles { get; } = new[]
    {
        "LICENSE",
        "NOTICE",
        "README.md",
    };

    /// <summary>
    /// Validates the payload file names, returning the missing ones. The
    /// comparison is case-insensitive because Windows archives are extracted
    /// on case-insensitive file systems.
    /// </summary>
    public static IReadOnlyList<string> FindMissing(IEnumerable<string> presentFiles)
    {
        ArgumentNullException.ThrowIfNull(presentFiles);
        var present = new HashSet<string>(
            presentFiles.Select(static file => file.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        return RequiredPayloadFiles
            .Where(required => !present.Contains(required))
            .ToArray();
    }
}

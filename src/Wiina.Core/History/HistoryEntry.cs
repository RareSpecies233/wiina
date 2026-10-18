namespace Wiina.Core.History;

/// <summary>One playback-history entry (local path or URL).</summary>
public sealed class HistoryEntry
{
    public string Path { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public double Duration { get; set; }

    public double Position { get; set; }

    public DateTimeOffset LastPlayedUtc { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUrl =>
        Uri.TryCreate(Path, UriKind.Absolute, out Uri? uri) && !uri.IsFile;

    [System.Text.Json.Serialization.JsonIgnore]
    public double ProgressRatio =>
        Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;
}

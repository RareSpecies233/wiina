using System.Text.Json;

namespace Wiina.Core.History;

/// <summary>
/// Playback history and resume points, stored atomically in
/// %LOCALAPPDATA%\Wiina\history.json. Reads are tolerant (a corrupt file
/// yields an empty history); writes use a temp file + replace.
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _gate = new();

    public HistoryStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wiina",
            "history.json");
    }

    public string Path { get; }

    /// <summary>How many entries are retained on disk.</summary>
    public int MaxEntries { get; init; } = 200;

    public IReadOnlyList<HistoryEntry> Load()
    {
        lock (_gate)
        {
            return LoadUnsafe();
        }
    }

    public HistoryEntry? Find(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        lock (_gate)
        {
            return LoadUnsafe().FirstOrDefault(
                entry => SamePath(entry.Path, path));
        }
    }

    public IReadOnlyList<HistoryEntry> Recent(int count)
    {
        lock (_gate)
        {
            return LoadUnsafe()
                .OrderByDescending(entry => entry.LastPlayedUtc)
                .Take(Math.Max(0, count))
                .ToArray();
        }
    }

    /// <summary>Inserts or updates (keyed by path) and saves.</summary>
    public void Record(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Path))
        {
            return;
        }

        lock (_gate)
        {
            List<HistoryEntry> entries = LoadUnsafe();
            entries.RemoveAll(existing => SamePath(existing.Path, entry.Path));
            if (entry.LastPlayedUtc == default)
            {
                entry.LastPlayedUtc = DateTimeOffset.UtcNow;
            }

            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            }

            SaveUnsafe(entries);
        }
    }

    /// <summary>Records a position without changing the title/duration defaults.</summary>
    public void RecordPosition(string path, double position, double duration)
    {
        HistoryEntry? existing = Find(path);
        Record(new HistoryEntry
        {
            Path = path,
            Title = existing?.Title ?? System.IO.Path.GetFileName(path),
            Duration = duration > 0 ? duration : existing?.Duration ?? 0,
            Position = Math.Max(0, position),
            LastPlayedUtc = DateTimeOffset.UtcNow,
        });
    }

    public bool Remove(string path)
    {
        lock (_gate)
        {
            List<HistoryEntry> entries = LoadUnsafe();
            int removed = entries.RemoveAll(
                entry => SamePath(entry.Path, path));
            if (removed == 0)
            {
                return false;
            }

            SaveUnsafe(entries);
            return true;
        }
    }

    public void Save(IEnumerable<HistoryEntry> entries)
    {
        lock (_gate)
        {
            SaveUnsafe(entries.ToList());
        }
    }

    private List<HistoryEntry> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new List<HistoryEntry>();
            }

            string json = File.ReadAllText(Path);
            HistoryFile? file = JsonSerializer.Deserialize<HistoryFile>(
                json,
                JsonOptions);
            return file?.Entries?.Where(
                entry => !string.IsNullOrWhiteSpace(entry.Path)).ToList()
                ?? new List<HistoryEntry>();
        }
        catch
        {
            // History must never prevent startup or playback.
            return new List<HistoryEntry>();
        }
    }

    private void SaveUnsafe(List<HistoryEntry> entries)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            new HistoryFile { Entries = entries },
            JsonOptions);
        string temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }
    }

    private static bool SamePath(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed class HistoryFile
    {
        public int Version { get; set; } = 1;

        public List<HistoryEntry> Entries { get; set; } = new();
    }
}

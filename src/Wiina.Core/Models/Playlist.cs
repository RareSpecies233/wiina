using System.Text;

namespace Wiina.Core.Models;

public enum PlaylistRepeatMode
{
    Off,
    All,
    One,
}

/// <summary>One saved playlist entry. The title is optional (m3u EXTINF).</summary>
public sealed record PlaylistItem(string Path, string? Title = null)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Title) ? Path : Title;
}

/// <summary>
/// Ordered, editable playlist used for .m3u persistence and as a lightweight
/// mirror of mpv's playlist. UI list virtualization is Stage 4.
/// </summary>
public sealed class Playlist
{
    private readonly List<PlaylistItem> _items = new();

    public IReadOnlyList<PlaylistItem> Items => _items;

    public int Count => _items.Count;

    /// <summary>Index of the current entry, or -1 when empty.</summary>
    public int CurrentIndex { get; private set; } = -1;

    public PlaylistRepeatMode RepeatMode { get; set; }

    public bool Shuffle { get; set; }

    public PlaylistItem? Current =>
        CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;

    public int Add(string path, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _items.Add(new PlaylistItem(path, title));
        int index = _items.Count - 1;
        if (CurrentIndex < 0)
        {
            CurrentIndex = index;
        }

        return index;
    }

    public void AddRange(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (string path in paths)
        {
            Add(path);
        }
    }

    public bool Insert(int index, string path, string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (index < 0 || index > _items.Count)
        {
            return false;
        }

        _items.Insert(index, new PlaylistItem(path, title));
        if (CurrentIndex < 0)
        {
            CurrentIndex = index;
        }
        else if (index <= CurrentIndex)
        {
            CurrentIndex++;
        }

        return true;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (index == CurrentIndex && CurrentIndex >= _items.Count)
        {
            CurrentIndex = _items.Count - 1;
        }

        return true;
    }

    public bool Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count ||
            from == to)
        {
            return false;
        }

        PlaylistItem item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);

        // Keep the current *item* selected across the reorder.
        if (from == CurrentIndex)
        {
            CurrentIndex = to;
        }
        else if (from < CurrentIndex && to >= CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (from > CurrentIndex && to <= CurrentIndex)
        {
            CurrentIndex++;
        }

        return true;
    }

    public bool SetCurrent(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        CurrentIndex = index;
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        CurrentIndex = -1;
    }

    public static Playlist FromPaths(IEnumerable<string> paths, string? title = null)
    {
        var playlist = new Playlist();
        foreach (string path in paths)
        {
            playlist.Add(path, title);
        }

        return playlist;
    }

    /// <summary>Writes a plain #EXTM3U playlist with optional EXTINF titles.</summary>
    public string ToM3u()
    {
        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        foreach (PlaylistItem item in _items)
        {
            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                builder.Append("#EXTINF:-1,").AppendLine(item.Title);
            }

            builder.AppendLine(item.Path);
        }

        return builder.ToString();
    }

    /// <summary>Reads a local .m3u/.m3u8 playlist. HLS tags are skipped.</summary>
    public static Playlist ParseM3u(string? text)
    {
        var playlist = new Playlist();
        if (string.IsNullOrWhiteSpace(text))
        {
            return playlist;
        }

        string? pendingTitle = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().Trim('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = line.IndexOf(',');
                pendingTitle = comma >= 0 && comma + 1 < line.Length
                    ? line[(comma + 1)..].Trim()
                    : null;
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            playlist.Add(line, pendingTitle);
            pendingTitle = null;
        }

        return playlist;
    }

    public static Playlist LoadM3u(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ParseM3u(File.ReadAllText(path));
    }

    public void SaveM3u(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, ToM3u());
        File.Move(temp, path, overwrite: true);
    }
}

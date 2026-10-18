using Wiina.Core.Models;

namespace Wiina.Core.State;

/// <summary>One top-level player window as seen by the shared coordinator.</summary>
public sealed record PlayerWindowInfo(string Id, string Title, bool HasMedia);

/// <summary>
/// App-level registry for multi-window playback. Keeps registration order and
/// activation order separate so closing the active window falls back to the
/// most recently active survivor, and routes shared media commands (SMTC,
/// media keys) to exactly one window.
/// </summary>
public sealed class WindowCoordinator
{
    private readonly List<PlayerWindowInfo> _windows = new();
    private readonly List<string> _activationOrder = new();

    /// <summary>Raised with the new active id, or null when no window remains.</summary>
    public event Action<string?>? ActiveWindowChanged;

    public IReadOnlyList<PlayerWindowInfo> Windows => _windows;

    public int Count => _windows.Count;

    /// <summary>True once the last window has been unregistered.</summary>
    public bool ShouldExit => _windows.Count == 0;

    public string? ActiveWindowId { get; private set; }

    public PlayerWindowInfo? Find(string id) =>
        _windows.Find(window => string.Equals(window.Id, id, StringComparison.Ordinal));

    public bool Contains(string id) => Find(id) is not null;

    /// <summary>
    /// Adds a window. The first registered window becomes active; later windows
    /// become active only when <see cref="Activate"/> is called (i.e. focused),
    /// so constructing a second window does not steal SMTC routing.
    /// </summary>
    public void Register(string id, string title = "Wiina", bool hasMedia = false)
    {
        if (string.IsNullOrWhiteSpace(id) || Contains(id))
        {
            return;
        }

        _windows.Add(new PlayerWindowInfo(id, title, hasMedia));
        _activationOrder.Add(id);
        if (ActiveWindowId is null)
        {
            SetActive(id);
        }
    }

    /// <summary>Updates title / media flags without changing activation.</summary>
    public bool Update(string id, string? title = null, bool? hasMedia = null)
    {
        int index = IndexOf(id);
        if (index < 0)
        {
            return false;
        }

        PlayerWindowInfo current = _windows[index];
        _windows[index] = current with
        {
            Title = title ?? current.Title,
            HasMedia = hasMedia ?? current.HasMedia,
        };
        return true;
    }

    /// <summary>Marks a window as the focused one; it wins media commands.</summary>
    public bool Activate(string id)
    {
        if (!Contains(id))
        {
            return false;
        }

        _activationOrder.Remove(id);
        _activationOrder.Add(id);
        SetActive(id);
        return true;
    }

    /// <summary>
    /// Removes a window. When it was active the most recently active remaining
    /// window takes over (null when the app is about to exit).
    /// </summary>
    public bool Close(string id)
    {
        int index = IndexOf(id);
        if (index < 0)
        {
            return false;
        }

        _windows.RemoveAt(index);
        _activationOrder.Remove(id);
        if (string.Equals(ActiveWindowId, id, StringComparison.Ordinal))
        {
            SetActive(_activationOrder.LastOrDefault());
        }

        return true;
    }

    /// <summary>
    /// The window that owns SMTC / media-key commands: the focused window, or
    /// the most recently active one if focus was lost to a non-player window.
    /// </summary>
    public string? ResolveMediaTargetId()
    {
        if (ActiveWindowId is not null && Contains(ActiveWindowId))
        {
            return ActiveWindowId;
        }

        return _activationOrder.LastOrDefault(Contains);
    }

    /// <summary>Last window that reported playable media (diagnostics/menu use).</summary>
    public string? ResolveMediaWindowId() =>
        _activationOrder.LastOrDefault(
            id => Find(id)?.HasMedia == true);

    private int IndexOf(string id) =>
        _windows.FindIndex(window => string.Equals(window.Id, id, StringComparison.Ordinal));

    private void SetActive(string? id)
    {
        if (string.Equals(ActiveWindowId, id, StringComparison.Ordinal))
        {
            return;
        }

        ActiveWindowId = id;
        ActiveWindowChanged?.Invoke(id);
    }

    /// <summary>Clears the registry (app shutdown / tests).</summary>
    public void Clear()
    {
        _windows.Clear();
        _activationOrder.Clear();
        SetActive(null);
    }
}

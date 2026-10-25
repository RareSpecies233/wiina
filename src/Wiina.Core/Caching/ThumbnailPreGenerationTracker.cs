namespace Wiina.Core.Caching;

/// <summary>
/// Tracks how much of the current file's timeline the background pre-generation
/// pass has covered. The timeline peek uses it to decide whether previews are
/// ready: with <c>ThumbnailPreviewWhileGenerating</c> off, hovering stays silent
/// until every bucket is either extracted or already cached, which is the IINA
/// behaviour (no thumbnails before <c>thumbnailsReady</c>).
/// </summary>
/// <remarks>
/// Only one source is tracked at a time. <see cref="Begin"/> starts a run for a
/// new file (dropping the previous progress), <see cref="MarkCompleted"/> counts
/// one finished bucket, and <see cref="MarkComplete"/> force-finishes a run that
/// will never walk the timeline (slow mode, no disk cache, empty duration). Keys
/// are ordinal-ignoring-case file paths so a late callback from the previous file
/// cannot complete the new one.
/// </remarks>
public sealed class ThumbnailPreGenerationTracker
{
    private readonly object _gate = new();
    private string? _key;
    private int _total;
    private int _completed;
    private bool _complete;

    /// <summary>File currently being tracked, or <c>null</c> when idle.</summary>
    public string? SourceKey
    {
        get
        {
            lock (_gate)
            {
                return _key;
            }
        }
    }

    /// <summary>Number of buckets the current run has to cover (0 when idle).</summary>
    public int TotalBuckets
    {
        get
        {
            lock (_gate)
            {
                return _total;
            }
        }
    }

    /// <summary>Buckets already handled by the current run.</summary>
    public int CompletedBuckets
    {
        get
        {
            lock (_gate)
            {
                return _completed;
            }
        }
    }

    /// <summary>
    /// True once every bucket of the tracked source is available. A tracker that
    /// never began a run is incomplete, so callers must not treat "idle" as ready.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            lock (_gate)
            {
                return _complete;
            }
        }
    }

    /// <summary>True when <paramref name="mediaPath"/> is the finished source.</summary>
    public bool IsCompleteFor(string? mediaPath)
    {
        lock (_gate)
        {
            return _complete && KeyEquals(_key, mediaPath);
        }
    }

    /// <summary>
    /// True while a pass over exactly <paramref name="mediaPath"/> is still
    /// collecting buckets. A source that was never tracked is not pending: there
    /// is no pass to wait for, so hover previews must not be blocked.
    /// </summary>
    public bool IsPendingFor(string? mediaPath)
    {
        lock (_gate)
        {
            return KeyEquals(_key, mediaPath) && _total > 0 && !_complete;
        }
    }

    /// <summary>
    /// Starts tracking <paramref name="mediaPath"/>. A missing key or a file with
    /// no buckets is complete immediately: there is nothing to wait for.
    /// </summary>
    public void Begin(string? mediaPath, int totalBuckets)
    {
        lock (_gate)
        {
            _key = mediaPath;
            _total = Math.Max(0, totalBuckets);
            _completed = 0;
            _complete = string.IsNullOrWhiteSpace(mediaPath) || _total == 0;
        }
    }

    /// <summary>
    /// Marks one bucket of <paramref name="mediaPath"/> as available. Ignored when
    /// another file is tracked; completion flips exactly when the last bucket
    /// arrives.
    /// </summary>
    public void MarkCompleted(string? mediaPath)
    {
        lock (_gate)
        {
            if (!KeyEquals(_key, mediaPath))
            {
                return;
            }

            _completed = Math.Min(_total, _completed + 1);
            if (_total > 0 && _completed >= _total)
            {
                _complete = true;
            }
        }
    }

    /// <summary>
    /// Force-finishes the run for <paramref name="mediaPath"/> (for example when
    /// the pass is skipped by design or aborted after an extraction error).
    /// </summary>
    public void MarkComplete(string? mediaPath)
    {
        lock (_gate)
        {
            if (!KeyEquals(_key, mediaPath))
            {
                return;
            }

            _completed = _total;
            _complete = true;
        }
    }

    /// <summary>Drops the tracked source and its progress.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _key = null;
            _total = 0;
            _completed = 0;
            _complete = false;
        }
    }

    private static bool KeyEquals(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

namespace Wiina.Core.Caching;

/// <summary>
/// Bucketed LRU cache used by the timeline thumbnail peek. Requests are keyed
/// by a fixed time bucket so small pointer movements reuse the same frame.
/// Values are evicted through an optional callback so callers can release
/// decoded images.
/// </summary>
public sealed class ThumbnailCache<T> where T : class
{
    private int _capacity;
    private readonly double _bucketSeconds;
    private readonly Action<T>? _onEvict;
    private readonly Dictionary<long, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly object _gate = new();

    public ThumbnailCache(int capacity = 60, double bucketSeconds = 5, Action<T>? onEvict = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        if (!double.IsFinite(bucketSeconds) || bucketSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSeconds), "Bucket size must be positive.");
        }

        _capacity = capacity;
        _bucketSeconds = bucketSeconds;
        _onEvict = onEvict;
    }

    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _capacity;
            }
        }
    }

    public double BucketSeconds => _bucketSeconds;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public static long BucketFor(double seconds, double bucketSeconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        if (!double.IsFinite(bucketSeconds) || bucketSeconds <= 0)
        {
            bucketSeconds = 5;
        }

        return (long)Math.Floor(seconds / bucketSeconds);
    }

    public long KeyFor(double seconds) => BucketFor(seconds, _bucketSeconds);

    public bool TryGet(double seconds, out T value)
    {
        long key = KeyFor(seconds);
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                value = null!;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    /// <summary>Stores or replaces an entry. Returns the evicted values in LRU order.</summary>
    public IReadOnlyList<T> Put(double seconds, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        long key = KeyFor(seconds);
        var evicted = new List<T>();

        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                if (!ReferenceEquals(existing.Value.Value, value))
                {
                    evicted.Add(existing.Value.Value);
                }

                existing.Value = new Entry(key, value);
                _order.Remove(existing);
                _order.AddFirst(existing);
            }
            else
            {
                var node = new LinkedListNode<Entry>(new Entry(key, value));
                _order.AddFirst(node);
                _map[key] = node;
            }

            while (_map.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                evicted.Add(last.Value.Value);
            }
        }

        foreach (T item in evicted)
        {
            _onEvict?.Invoke(item);
        }

        return evicted;
    }

    /// <summary>
    /// Changes the capacity in place. The most recently used entries survive;
    /// anything above the new size is evicted from the LRU end and returned in
    /// eviction order (the same callback fires as for <see cref="Put"/>).
    /// </summary>
    public IReadOnlyList<T> Resize(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        var evicted = new List<T>();
        lock (_gate)
        {
            _capacity = capacity;
            while (_map.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                evicted.Add(last.Value.Value);
            }
        }

        foreach (T item in evicted)
        {
            _onEvict?.Invoke(item);
        }

        return evicted;
    }

    public IReadOnlyList<T> Clear()
    {
        var removed = new List<T>();
        lock (_gate)
        {
            foreach (Entry entry in _order)
            {
                removed.Add(entry.Value);
            }

            _order.Clear();
            _map.Clear();
        }

        foreach (T item in removed)
        {
            _onEvict?.Invoke(item);
        }

        return removed;
    }

    public IReadOnlyList<long> KeysInLruOrder()
    {
        lock (_gate)
        {
            var keys = new List<long>(_order.Count);
            foreach (Entry entry in _order)
            {
                keys.Add(entry.Key);
            }

            return keys;
        }
    }

    private readonly record struct Entry(long Key, T Value);
}


namespace F1App.Telemetry.Media;

/// <summary>
/// A bounded, recency-ordered cache for image bytes, held for the session only.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately in-memory and deliberately small. F1's CDN artwork is used as published — hotlinked, credited
/// and never written to disk — so there is no file cache to fall out of step with that rule, and no bytes of
/// it survive the app closing. See ADR-019.
/// </para>
/// <para>
/// Recency matters more than hit rate here: the pictures a person navigates back to are the ones they just
/// looked at, so reading an entry promotes it and eviction takes the least recently used.
/// </para>
/// </remarks>
public sealed class ImageMemoryCache
{
    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, byte[] Bytes)> _entries = [];

    /// <summary>Creates a cache holding at most <paramref name="capacity"/> images.</summary>
    public ImageMemoryCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>How many images are held right now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Reads an image and promotes it to most recently used.</summary>
    public bool TryGet(string key, out byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                bytes = [];
                return false;
            }

            _order.Remove(entry.Node);
            _order.AddFirst(entry.Node);
            bytes = entry.Bytes;
            return true;
        }
    }

    /// <summary>Stores an image, evicting the least recently used when full.</summary>
    public void Set(string key, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(bytes);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _order.Remove(existing.Node);
                _entries.Remove(key);
            }

            var node = _order.AddFirst(key);
            _entries[key] = (node, bytes);

            while (_entries.Count > _capacity)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _entries.Remove(oldest.Value);
            }
        }
    }
}

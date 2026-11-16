namespace F1App.Telemetry;

/// <summary>
/// Fixed-capacity FIFO that overwrites the oldest item once full.
/// </summary>
/// <remarks>
/// Used for the per-driver telemetry history behind the comparison charts: the chart only ever
/// needs a bounded window, so a ring buffer replaces both an unbounded list and its regrowth
/// copies.
/// </remarks>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _next;
    private int _count;

    /// <summary>Creates a ring that holds <paramref name="capacity"/> items before overwriting.</summary>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    /// <summary>Capacity of the ring.</summary>
    public int Capacity => _items.Length;

    /// <summary>Number of items currently held.</summary>
    public int Count => _count;

    /// <summary>Appends <paramref name="item"/>, discarding the oldest when full.</summary>
    public void Add(T item)
    {
        _items[_next] = item;
        _next = (_next + 1) % _items.Length;

        if (_count < _items.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// Copies the held items oldest-first into <paramref name="destination"/>.
    /// </summary>
    /// <returns>The number of items copied (at most <paramref name="destination"/>'s length).</returns>
    public int CopyTo(Span<T> destination)
    {
        var take = Math.Min(_count, destination.Length);
        var oldest = (_next - _count + _items.Length) % _items.Length;
        var skip = _count - take;

        for (var i = 0; i < take; i++)
        {
            destination[i] = _items[(oldest + skip + i) % _items.Length];
        }

        return take;
    }

    /// <summary>Drops every item.</summary>
    public void Clear()
    {
        Array.Clear(_items);
        _next = 0;
        _count = 0;
    }
}

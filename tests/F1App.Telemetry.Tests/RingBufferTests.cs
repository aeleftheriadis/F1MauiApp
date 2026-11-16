namespace F1App.Telemetry.Tests;

public sealed class RingBufferTests
{
    [Fact]
    public void Count_GrowsToCapacityAndStaysThereWhenItemsAreOverwritten()
    {
        var ring = new RingBuffer<int>(3);

        Assert.Equal(0, ring.Count);
        ring.Add(1);
        ring.Add(2);
        Assert.Equal(2, ring.Count);
        ring.Add(3);
        ring.Add(4);

        Assert.Equal(3, ring.Count);
        Assert.Equal(3, ring.Capacity);
    }

    [Fact]
    public void CopyTo_ReturnsItemsOldestFirstUntilFull()
    {
        var ring = new RingBuffer<int>(4);
        ring.Add(1);
        ring.Add(2);
        ring.Add(3);

        Span<int> buffer = stackalloc int[4];
        var count = ring.CopyTo(buffer);

        Assert.Equal(3, count);
        Assert.Equal([1, 2, 3], buffer[..count].ToArray());
    }

    [Fact]
    public void CopyTo_AfterWrapKeepsChronologicalOrder()
    {
        var ring = new RingBuffer<int>(4);
        ring.Add(1);
        ring.Add(2);
        ring.Add(3);
        ring.Add(4);
        ring.Add(5);
        ring.Add(6);

        Span<int> buffer = stackalloc int[4];
        var count = ring.CopyTo(buffer);

        Assert.Equal(4, count);
        Assert.Equal([3, 4, 5, 6], buffer[..count].ToArray());
    }

    [Fact]
    public void CopyTo_TruncatesToDestinationWhenDestinationIsSmaller()
    {
        var ring = new RingBuffer<int>(4);
        for (var i = 1; i <= 6; i++)
        {
            ring.Add(i);
        }

        Span<int> buffer = stackalloc int[2];
        var count = ring.CopyTo(buffer);

        Assert.Equal(2, count);
        Assert.Equal([5, 6], buffer.ToArray());
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(-1));
    }

    [Fact]
    public void Clear_DropsEveryItem()
    {
        var ring = new RingBuffer<int>(2);
        ring.Add(1);
        ring.Add(2);

        ring.Clear();

        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.CopyTo(stackalloc int[2]));
    }
}

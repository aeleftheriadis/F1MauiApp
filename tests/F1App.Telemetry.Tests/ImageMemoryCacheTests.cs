using F1App.Telemetry.Media;

namespace F1App.Telemetry.Tests;

/// <summary>
/// The app may only hold F1's artwork in memory, so the cache's eviction and its refusal to touch disk are
/// behaviour worth pinning, not implementation detail.
/// </summary>
public sealed class ImageMemoryCacheTests
{
    [Fact]
    public void Set_ThenTryGet_ReturnsTheBytes()
    {
        var cache = new ImageMemoryCache(2);
        cache.Set("a", [1, 2, 3]);

        Assert.True(cache.TryGet("a", out var bytes));
        Assert.Equal([1, 2, 3], bytes);
    }

    [Fact]
    public void Set_BeyondCapacity_EvictsTheOldestEntry()
    {
        var cache = new ImageMemoryCache(2);
        cache.Set("a", [1]);
        cache.Set("b", [2]);
        cache.Set("c", [3]);

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        Assert.False(new ImageMemoryCache(4).TryGet("nope", out _));
    }

    [Fact]
    public void Use_RefreshesRecency_SoTheOldestUnusedEntryIsEvicted()
    {
        var cache = new ImageMemoryCache(2);
        cache.Set("a", [1]);
        cache.Set("b", [2]);
        _ = cache.TryGet("a", out _);   // "a" is now the more recent one
        cache.Set("c", [3]);

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageMemoryCache(0));
    }
}

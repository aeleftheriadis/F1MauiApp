using F1App.Domain;
using F1App.Telemetry.Facts;

namespace F1App.Telemetry.Tests;

public sealed class SqliteFactsCacheTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"f1app-facts-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Champions_SurviveAWriteAndRead()
    {
        var cache = new SqliteFactsCache(_path);

        await cache.WriteChampionsAsync(
            [new SeasonChampion(2024, "Max Verstappen", "Red Bull", 437, 9)],
            Now,
            CancellationToken.None);

        var restored = await cache.ReadChampionsAsync(Now, TimeSpan.FromHours(24), CancellationToken.None);

        var champion = Assert.Single(restored);
        Assert.Equal(2024, champion.Season);
        Assert.Equal("Max Verstappen", champion.DriverName);
        Assert.Equal(437, champion.Points);
        Assert.Equal(9, champion.Wins);
    }

    [Fact]
    public async Task Champions_AreTreatedAsAbsentWhenStale()
    {
        var cache = new SqliteFactsCache(_path);

        await cache.WriteChampionsAsync(
            [new SeasonChampion(2024, "Max Verstappen", "Red Bull", 437, 9)],
            Now,
            CancellationToken.None);

        var stale = await cache.ReadChampionsAsync(
            Now.AddHours(25),
            TimeSpan.FromHours(24),
            CancellationToken.None);

        Assert.Empty(stale);
    }

    [Fact]
    public async Task Champions_AreReplacedRatherThanDuplicated()
    {
        var cache = new SqliteFactsCache(_path);

        await cache.WriteChampionsAsync(
            [new SeasonChampion(2024, "Max Verstappen", "Red Bull", 437, 9)],
            Now,
            CancellationToken.None);

        await cache.WriteChampionsAsync(
            [new SeasonChampion(2025, "Lando Norris", "McLaren", 423, 7)],
            Now,
            CancellationToken.None);

        var restored = await cache.ReadChampionsAsync(Now, TimeSpan.FromHours(24), CancellationToken.None);

        var champion = Assert.Single(restored);
        Assert.Equal(2025, champion.Season);
    }

    [Fact]
    public async Task Winners_AreScopedPerCircuit()
    {
        var cache = new SqliteFactsCache(_path);

        await cache.WriteWinnersAsync(
            "interlagos",
            [new CircuitWinner("Michael Schumacher", 4, 2002)],
            Now,
            CancellationToken.None);

        await cache.WriteWinnersAsync(
            "monza",
            [new CircuitWinner("Michael Schumacher", 5, 2006)],
            Now,
            CancellationToken.None);

        var interlagos = await cache.ReadWinnersAsync("interlagos", Now, TimeSpan.FromHours(24), CancellationToken.None);
        var monza = await cache.ReadWinnersAsync("monza", Now, TimeSpan.FromHours(24), CancellationToken.None);

        Assert.Equal(4, Assert.Single(interlagos).Wins);
        Assert.Equal(5, Assert.Single(monza).Wins);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

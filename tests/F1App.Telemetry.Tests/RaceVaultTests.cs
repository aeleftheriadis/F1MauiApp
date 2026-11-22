using System.Collections.Immutable;
using System.Net;
using F1App.Domain;
using F1App.Telemetry.Facts;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1App.Telemetry.Tests;

public sealed class JolpicaRaceVaultClientTests
{
    /// <summary>Shape verified against the live endpoint for 1950 and 2023.</summary>
    private static string SeasonPayload(int season, string raceName, string winner, string constructor) => $$"""
        {
          "MRData": {
            "RaceTable": {
              "Races": [
                {
                  "season": "{{season}}",
                  "round": "1",
                  "raceName": "{{raceName}}",
                  "Circuit": {
                    "circuitId": "silverstone",
                    "circuitName": "Silverstone Circuit",
                    "Location": { "country": "UK" }
                  },
                  "date": "{{season}}-05-13",
                  "Results": [
                    {
                      "position": "1",
                      "points": "9",
                      "laps": "70",
                      "Driver": { "driverId": "farina", "givenName": "Nino", "familyName": "Farina" },
                      "Constructor": { "constructorId": "alfa", "name": "{{constructor}}" },
                      "Time": { "millis": "7943600", "time": "2:13:23.600" }
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

    private static (JolpicaRaceVaultClient Client, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var client = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (client, handler);
    }

    [Fact]
    public async Task GetSeasonRacesAsync_ParsesEveryFieldOfTheWinner()
    {
        var handler = new StubHttpHandler()
            .Map("2023/results/1", HttpStatusCode.OK, SeasonPayload(2023, "Bahrain Grand Prix", "Max Verstappen", "Red Bull"));

        var vault = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var races = UnionAssert.Success(await vault.GetSeasonRacesAsync(2023, CancellationToken.None));

        var race = Assert.Single(races);
        Assert.Equal(2023, race.Season);
        Assert.Equal(1, race.Round);
        Assert.Equal("Bahrain Grand Prix", race.RaceName);
        Assert.Equal("silverstone", race.CircuitId);
        Assert.Equal("Silverstone Circuit", race.CircuitName);
        Assert.Equal("UK", race.Country);
        Assert.Equal(new DateOnly(2023, 5, 13), race.Date);
        Assert.Equal("Nino Farina", race.Winner);
        Assert.Equal("Red Bull", race.Constructor);
        Assert.Equal(70, race.Laps);
        Assert.Equal("2:13:23.600", race.WinningTime);
    }

    [Fact]
    public async Task GetSeasonRacesAsync_RequestsExactlyOneSeasonPerCall()
    {
        var handler = new StubHttpHandler()
            .Map("1950/results/1", HttpStatusCode.OK, SeasonPayload(1950, "British Grand Prix", "Nino Farina", "Alfa Romeo"));

        var vault = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        await vault.GetSeasonRacesAsync(1950, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/1950/results/1.json", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSeasonRacesAsync_DropsTheIndianapolis500ButKeepsTheUsGrandPrix()
    {
        const string payload = """
            {
              "MRData": {
                "RaceTable": {
                  "Races": [
                    {
                      "season": "1950", "round": "3", "raceName": "Indianapolis 500",
                      "Circuit": { "circuitId": "indianapolis", "circuitName": "Indianapolis Motor Speedway", "Location": { "country": "US" } },
                      "date": "1950-05-30",
                      "Results": [ { "laps": "138", "Driver": { "givenName": "Johnnie", "familyName": "Parsons" }, "Constructor": { "name": "Kurtis Kraft" }, "Time": { "time": "2:46:55.97" } } ]
                    },
                    {
                      "season": "1950", "round": "1", "raceName": "British Grand Prix",
                      "Circuit": { "circuitId": "silverstone", "circuitName": "Silverstone Circuit", "Location": { "country": "UK" } },
                      "date": "1950-05-13",
                      "Results": [ { "laps": "70", "Driver": { "givenName": "Nino", "familyName": "Farina" }, "Constructor": { "name": "Alfa Romeo" }, "Time": { "time": "2:13:23.6" } } ]
                    }
                  ]
                }
              }
            }
            """;

        var handler = new StubHttpHandler().Map("1950/results/1", HttpStatusCode.OK, payload);
        var vault = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var races = UnionAssert.Success(await vault.GetSeasonRacesAsync(1950, CancellationToken.None));

        var race = Assert.Single(races);
        Assert.Equal("British Grand Prix", race.RaceName);
    }

    [Fact]
    public async Task GetSeasonRacesAsync_KeepsTheUsGrandPrixHeldAtIndianapolis()
    {
        var handler = new StubHttpHandler().Map(
            "2006/results/1",
            HttpStatusCode.OK,
            """
            {
              "MRData": {
                "RaceTable": {
                  "Races": [
                    {
                      "season": "2006", "round": "10", "raceName": "United States Grand Prix",
                      "Circuit": { "circuitId": "indianapolis", "circuitName": "Indianapolis Motor Speedway", "Location": { "country": "US" } },
                      "date": "2006-07-02",
                      "Results": [ { "laps": "73", "Driver": { "givenName": "Michael", "familyName": "Schumacher" }, "Constructor": { "name": "Ferrari" }, "Time": { "time": "1:34:35.199" } } ]
                    }
                  ]
                }
              }
            }
            """);

        var vault = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var races = UnionAssert.Success(await vault.GetSeasonRacesAsync(2006, CancellationToken.None));

        Assert.Equal("United States Grand Prix", Assert.Single(races).RaceName);
    }

    [Fact]
    public async Task GetSeasonRacesAsync_ReportsAFailingSeason()
    {
        var handler = new StubHttpHandler()
            .Map("1950/results/1", HttpStatusCode.ServiceUnavailable, "down");

        var vault = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        UnionAssert.Failure(await vault.GetSeasonRacesAsync(1950, CancellationToken.None));
    }

    [Fact]
    public async Task GetSeasonRacesAsync_ReportsRateLimiting()
    {
        var handler = new StubHttpHandler()
            .Map("1950/results/1", HttpStatusCode.TooManyRequests, "{}");

        var vault = new JolpicaRaceVaultClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        UnionAssert.Limited(await vault.GetSeasonRacesAsync(1950, CancellationToken.None));
    }
}

public sealed class RaceVaultServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"f1app-vault-{Guid.NewGuid():N}.db");

    /// <summary>The vault spans 1950..now, so the tests pin the clock rather than paying for 77 stubbed seasons.</summary>
    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Utc;

        public FixedTime(int year) => Utc = new DateTimeOffset(year, 6, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private static JolpicaRaceVaultClient Client(StubHttpHandler handler) =>
        new(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

    private static string SeasonPayload(int season, string raceName, string winner) => $$"""
        {
          "MRData": {
            "RaceTable": {
              "Races": [
                {
                  "season": "{{season}}",
                  "round": "1",
                  "raceName": "{{raceName}}",
                  "Circuit": {
                    "circuitId": "silverstone",
                    "circuitName": "Silverstone Circuit",
                    "Location": { "country": "UK" }
                  },
                  "date": "{{season}}-05-13",
                  "Results": [
                    {
                      "laps": "70",
                      "Driver": { "driverId": "x", "givenName": "A", "familyName": "{{winner}}" },
                      "Constructor": { "constructorId": "c", "name": "Constructor {{winner}}" },
                      "Time": { "time": "1:00:00.000" }
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

    [Fact]
    public async Task ReadRacesAsync_PurgesAnIndy500RowFromAVersion2Database()
    {
        // A device already synced by the previous schema: race_vault exists, holds the Indy 500, version 2.
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();

            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE race_vault (
                    season INTEGER NOT NULL, round INTEGER NOT NULL, race_name TEXT NOT NULL,
                    circuit_id TEXT NOT NULL, circuit_name TEXT NOT NULL, country TEXT NOT NULL,
                    date TEXT NOT NULL, winner TEXT NOT NULL, constructor TEXT NOT NULL,
                    laps INTEGER NOT NULL, winning_time TEXT NOT NULL, fetched_at TEXT NOT NULL,
                    PRIMARY KEY (season, round));
                INSERT INTO race_vault VALUES
                    (1950, 3, 'Indianapolis 500', 'indianapolis', 'Indianapolis Motor Speedway', 'US',
                     '1950-05-30', 'Johnnie Parsons', 'Kurtis Kraft', 138, '2:46:55.97', '1950-06-01T00:00:00.0000000+00:00'),
                    (1950, 1, 'British Grand Prix', 'silverstone', 'Silverstone Circuit', 'UK',
                     '1950-05-13', 'Nino Farina', 'Alfa Romeo', 70, '2:13:23.6', '1950-06-01T00:00:00.0000000+00:00');
                PRAGMA user_version = 2;
                """;
            await create.ExecuteNonQueryAsync();
        }

        var races = await new SqliteFactsCache(_path).ReadRacesAsync(CancellationToken.None);

        Assert.Equal("British Grand Prix", Assert.Single(races).RaceName);
    }

    [Fact]
    public async Task SyncAsync_FillsTheVaultSoLoadAndNeedsSyncWorkOffline()
    {
        var time = new FixedTime(1951);
        var handler = new StubHttpHandler()
            .Map("1950/results/1", HttpStatusCode.OK, SeasonPayload(1950, "British Grand Prix", "Farina"))
            .Map("1951/results/1", HttpStatusCode.OK, SeasonPayload(1951, "Swiss Grand Prix", "Farina"));
        var cache = new SqliteFactsCache(_path);
        using var service = new RaceVaultService(
            Client(handler),
            cache,
            time,
            NullLogger<RaceVaultService>.Instance);

        var problem = await service.SyncAsync(null, CancellationToken.None);

        Assert.Null(problem);
        Assert.False(await service.NeedsSyncAsync(CancellationToken.None));

        var snapshot = await service.LoadAsync(CancellationToken.None);

        Assert.Null(snapshot.Problem);
        Assert.Equal(2, snapshot.Records.Seasons);
        Assert.Equal(2, snapshot.Races.Length);
    }

    [Fact]
    public async Task SyncAsync_ResumesFromMissingSeasonsOnly()
    {
        var time = new FixedTime(1950);
        var handler = new StubHttpHandler()
            .Map("1950/results/1", HttpStatusCode.OK, SeasonPayload(1950, "British Grand Prix", "Farina"));
        var cache = new SqliteFactsCache(_path);
        using var service = new RaceVaultService(
            Client(handler),
            cache,
            time,
            NullLogger<RaceVaultService>.Instance);

        await service.SyncAsync(null, CancellationToken.None);
        Assert.Single(handler.Requests);

        // A year passes; only 1951 can be missing now.
        time.Utc = new DateTimeOffset(1951, 6, 1, 0, 0, 0, TimeSpan.Zero);
        handler.Map("1951/results/1", HttpStatusCode.OK, SeasonPayload(1951, "Swiss Grand Prix", "Farina"));

        await service.SyncAsync(null, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);

        // 1950 was written by the first sync; the second must not fetch it again.
        Assert.Equal(1, handler.Requests.Count(uri => uri.ToString().Contains("1950/results", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SyncAsync_ReportsFailedSeasonsAndKeepsTheRest()
    {
        var handler = new StubHttpHandler()
            .Map("1950/results/1", HttpStatusCode.ServiceUnavailable, "down");
        var cache = new SqliteFactsCache(_path);
        using var service = new RaceVaultService(
            Client(handler),
            cache,
            new FixedTime(1950),
            NullLogger<RaceVaultService>.Instance);

        var problem = await service.SyncAsync(null, CancellationToken.None);

        Assert.NotNull(problem);
        Assert.Contains("failed", problem, StringComparison.Ordinal);

        var snapshot = await service.LoadAsync(CancellationToken.None);

        Assert.Equal("archive not synced yet", snapshot.Problem);
    }

    [Fact]
    public void ComputeRecords_RanksDriversAndConstructorsByWins()
    {
        var races = ImmutableArray.Create(
            new ArchivedRace(1950, 1, "A", "a", "A", "UK", new DateOnly(1950, 5, 13), "Nino Farina", "Alfa Romeo", 70, "1:00:00"),
            new ArchivedRace(1950, 2, "B", "b", "B", "MC", new DateOnly(1950, 6, 4), "Nino Farina", "Alfa Romeo", 70, "1:00:00"),
            new ArchivedRace(1951, 1, "C", "c", "C", "CH", new DateOnly(1951, 5, 27), "Juan Fangio", "Alfa Romeo", 70, "1:00:00"));

        var records = RaceVaultService.ComputeRecords(races);

        Assert.Equal(3, records.Races);
        Assert.Equal(2, records.Seasons);

        Assert.Equal("Nino Farina", records.DriverWins[0].Name);
        Assert.Equal(2, records.DriverWins[0].Wins);
        Assert.Equal(1950, records.DriverWins[0].FirstSeason);
        Assert.Equal(1950, records.DriverWins[0].LastSeason);

        Assert.Equal("Alfa Romeo", records.ConstructorWins[0].Name);
        Assert.Equal(3, records.ConstructorWins[0].Wins);
        Assert.Equal(1951, records.ConstructorWins[0].LastSeason);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Pooled sqlite handles can outlive the test on some platforms.
        }
    }
}

public sealed class OnThisDayTests
{
    private static ArchivedRace Race(int season, int month, int day, string name) =>
        new(season, 1, name, "c", "Circuit", "UK", new DateOnly(season, month, day), "Winner", "Constructor", 70, "1:00:00");

    [Fact]
    public void OnThisDay_ReturnsEarlierSeasonsOnTheSameCalendarDay()
    {
        var races = ImmutableArray.Create(
            Race(1988, 9, 11, "Italian Grand Prix"),
            Race(1994, 9, 11, "Italian Grand Prix"),
            Race(1994, 9, 25, "Portuguese Grand Prix"));

        var found = RaceVaultService.OnThisDay(races, new DateOnly(2026, 9, 11));

        Assert.Equal(2, found.Length);
        Assert.Equal(1994, found[0].Season);
        Assert.Equal(1988, found[1].Season);
    }

    [Fact]
    public void OnThisDay_IsEmptyOnADayWithNoRace()
    {
        var races = ImmutableArray.Create(Race(1988, 9, 11, "Italian Grand Prix"));

        Assert.Empty(RaceVaultService.OnThisDay(races, new DateOnly(2026, 3, 1)));
        Assert.Empty(RaceVaultService.OnThisDay(ImmutableArray<ArchivedRace>.Empty, new DateOnly(2026, 9, 11)));
    }

    [Fact]
    public void OnThisDay_IgnoresTheCurrentSeasonItself()
    {
        var races = ImmutableArray.Create(
            Race(2026, 9, 11, "Italian Grand Prix"),
            Race(2025, 9, 11, "Italian Grand Prix"));

        var found = RaceVaultService.OnThisDay(races, new DateOnly(2026, 9, 11));

        Assert.Equal(2025, Assert.Single(found).Season);
    }
}

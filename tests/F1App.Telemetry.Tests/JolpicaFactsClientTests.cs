using System.Net;
using F1App.Telemetry.Facts;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1App.Telemetry.Tests;

public sealed class JolpicaFactsClientTests
{
    private static string SeasonPayload(int season, int points, int wins) => $$"""
        {
          "MRData": {
            "StandingsTable": {
              "StandingsLists": [
                {
                  "season": "{{season}}",
                  "round": "24",
                  "DriverStandings": [
                    {
                      "position": "1",
                      "points": "{{points}}",
                      "wins": "{{wins}}",
                      "Driver": { "driverId": "max_verstappen", "givenName": "Max", "familyName": "Verstappen" },
                      "Constructors": [ { "constructorId": "red_bull", "name": "Red Bull" } ]
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

    private static (JolpicaFactsClient Client, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var client = new JolpicaFactsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (client, handler);
    }

    [Fact]
    public async Task GetChampionsAsync_RequestsOneSeasonPerCallAndMergesThem()
    {
        var (client, handler) = Create();
        handler
            .Map("2023/driverStandings/1", HttpStatusCode.OK, SeasonPayload(2023, 575, 19))
            .Map("2024/driverStandings/1", HttpStatusCode.OK, SeasonPayload(2024, 437, 9));

        var champions = UnionAssert.Success(await client.GetChampionsAsync(2023, 2024, CancellationToken.None));

        Assert.Equal(2, champions.Length);
        Assert.Equal(2023, champions[0].Season);
        Assert.Equal(575, champions[0].Points);
        Assert.Equal(19, champions[0].Wins);
        Assert.Equal("Max Verstappen", champions[1].DriverName);
        Assert.Equal("Red Bull", champions[1].ConstructorName);

        // One request per season: Jolpica rejects the cross-season filter with 400.
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            uri => Assert.Contains("/driverStandings/1.json", uri.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetChampionsAsync_SkipsSeasonsThatFail()
    {
        var (client, handler) = Create();
        handler
            .Map("2023/driverStandings/1", HttpStatusCode.ServiceUnavailable, "down")
            .Map("2024/driverStandings/1", HttpStatusCode.OK, SeasonPayload(2024, 437, 9));

        var champions = UnionAssert.Success(await client.GetChampionsAsync(2023, 2024, CancellationToken.None));

        Assert.Single(champions);
        Assert.Equal(2024, champions[0].Season);
    }

    [Fact]
    public async Task GetChampionsAsync_ReportsRateLimitingWhenEverySeasonFails()
    {
        var (client, handler) = Create();
        handler.Map("driverStandings/1", HttpStatusCode.TooManyRequests, "{}");

        UnionAssert.Failure(await client.GetChampionsAsync(2024, 2024, CancellationToken.None));
    }

    [Fact]
    public async Task GetChampionsAsync_RejectsAnImpossibleRange()
    {
        var (client, _) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetChampionsAsync(2020, 2019, CancellationToken.None));
    }
}

public sealed class ChampionsServiceTests
{
    private static (ChampionsService Service, StubHttpHandler Handler) Create(string body)
    {
        var handler = new StubHttpHandler().Map("driverStandings/1", HttpStatusCode.OK, body);

        var client = new JolpicaFactsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (new ChampionsService(client, null, TimeProvider.System, NullLogger<ChampionsService>.Instance), handler);
    }

    private const string OneSeason = """
        {
          "MRData": {
            "StandingsTable": {
              "StandingsLists": [
                {
                  "season": "2024",
                  "DriverStandings": [
                    {
                      "position": "1",
                      "points": "437",
                      "wins": "9",
                      "Driver": { "givenName": "Max", "familyName": "Verstappen" },
                      "Constructors": [ { "name": "Red Bull" } ]
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

    [Fact]
    public async Task GetAsync_CachesTheRangeAndReportsFullCoverage()
    {
        var (service, handler) = Create(OneSeason);

        var first = await service.GetAsync(force: false, CancellationToken.None);
        var requestsAfterFirst = handler.Requests.Count;
        var second = await service.GetAsync(force: false, CancellationToken.None);

        // Twenty seasons in the range, twenty answers, twenty rows.
        Assert.Equal(20, first.Expected);
        Assert.Equal(20, first.Champions.Length);
        Assert.Null(first.Problem);

        Assert.Equal(requestsAfterFirst, handler.Requests.Count);
        Assert.Equal(20, second.Champions.Length);
    }

    [Fact]
    public async Task GetAsync_ReportsAMissingSeasonRatherThanHidingIt()
    {
        var handler = new StubHttpHandler()
            .Map("2010/driverStandings/1", HttpStatusCode.ServiceUnavailable, "down")
            .Map("driverStandings/1", HttpStatusCode.OK, OneSeason);

        var client = new JolpicaFactsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        using var service = new ChampionsService(client, null, TimeProvider.System, NullLogger<ChampionsService>.Instance);

        var snapshot = await service.GetAsync(force: false, CancellationToken.None);

        Assert.Equal(19, snapshot.Champions.Length);
        Assert.Equal(20, snapshot.Expected);
        Assert.Contains("19 of 20", snapshot.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_ReportsAFailureWhenNothingIsCached()
    {
        var handler = new StubHttpHandler().Map("driverStandings/1", HttpStatusCode.ServiceUnavailable, "down");

        var client = new JolpicaFactsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        using var service = new ChampionsService(client, null, TimeProvider.System, NullLogger<ChampionsService>.Instance);

        var snapshot = await service.GetAsync(force: false, CancellationToken.None);

        Assert.Empty(snapshot.Champions);
        Assert.Contains("unavailable", snapshot.Problem, StringComparison.Ordinal);
    }
}

using System.Collections.Immutable;
using System.Net;
using F1App.Domain;
using F1App.Telemetry.Facts;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1App.Telemetry.Tests;

public sealed class JolpicaSeasonResultsClientTests
{
    /// <summary>One page of Jolpica's season-results shape, with the fields the parser reads.</summary>
    private static string Page(
        int total,
        int offset,
        int limit,
        params (string Round, string Constructor, string Driver, int Position)[] rows)
    {
        var races = rows
            .GroupBy(row => row.Round)
            .Select(group =>
            {
                var results = group.Select(row =>
                    "{\"position\":\"" + row.Position + "\",\"positionText\":\"" + row.Position +
                    "\",\"grid\":\"" + row.Position + "\",\"status\":\"Finished\"," +
                    "\"Driver\":{\"driverId\":\"" + row.Driver + "\",\"givenName\":\"A\"," +
                    "\"familyName\":\"B\",\"code\":\"" + row.Driver.ToUpperInvariant() + "\"}," +
                    "\"Constructor\":{\"constructorId\":\"" + row.Constructor + "\"}}");

                return "{\"round\":\"" + group.Key + "\",\"raceName\":\"R" + group.Key +
                       "\",\"Results\":[" + string.Join(",", results) + "]}";
            });

        return "{\"MRData\":{\"total\":\"" + total + "\",\"limit\":\"" + limit + "\"," +
               "\"offset\":\"" + offset + "\",\"RaceTable\":{\"Races\":[" +
               string.Join(",", races) + "]}}}";
    }

    [Fact]
    public async Task GetSeasonResultsAsync_FollowsEveryPageUntilTheTotalIsReached()
    {
        var handler = new StubHttpHandler()
            .Map("offset=0", HttpStatusCode.OK, Page(4, 0, 2, ("1", "red_bull", "verstappen", 1), ("1", "red_bull", "perez", 2)))
            .Map("offset=100", HttpStatusCode.OK, Page(4, 100, 2, ("2", "ferrari", "leclerc", 1), ("2", "ferrari", "hamilton", 2)));

        var client = new JolpicaSeasonResultsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var rows = UnionAssert.Success(await client.GetSeasonResultsAsync(2024, CancellationToken.None));

        Assert.Equal(4, rows.Length);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(rows, row => row.DriverCode == "VERSTAPPEN");
    }

    [Fact]
    public async Task GetSeasonResultsAsync_StopsWhenAPageIsEmpty()
    {
        var handler = new StubHttpHandler()
            .Map("results.json", HttpStatusCode.OK, Page(200, 0, 100));

        var client = new JolpicaSeasonResultsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var rows = UnionAssert.Success(await client.GetSeasonResultsAsync(2024, CancellationToken.None));

        Assert.Empty(rows);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSeasonResultsAsync_ReportsAFailingPageInsteadOfAPartialSeason()
    {
        var handler = new StubHttpHandler()
            .Map("offset=0", HttpStatusCode.OK, Page(4, 0, 2, ("1", "red_bull", "verstappen", 1), ("1", "red_bull", "perez", 2)))
            .Map("offset=100", HttpStatusCode.ServiceUnavailable, "down");

        var client = new JolpicaSeasonResultsClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        // Half a season would make a duel a wrong number, so a failed page fails the season.
        UnionAssert.Failure(await client.GetSeasonResultsAsync(2024, CancellationToken.None));
    }
}

public sealed class TeammateDuelServiceTests
{
    private static RaceResultRow Row(int round, string constructor, string driver, int? position, int? grid = null) =>
        new(2024, round, driver, driver.ToUpperInvariant(), driver, constructor, position, position?.ToString() ?? "R", grid ?? position, "Finished");

    [Fact]
    public void Compute_CountsOnlyClassifiedRacesAndBothDirections()
    {
        var rows = ImmutableArray.Create(
            Row(1, "red_bull", "verstappen", 1), Row(1, "red_bull", "perez", 2),
            Row(2, "red_bull", "verstappen", 3), Row(2, "red_bull", "perez", 1),
            // A retirement: perez has no numeric position, so race 3 is not a defeat for him.
            Row(3, "red_bull", "verstappen", 1), Row(3, "red_bull", "perez", null));

        var duels = TeammateDuelService.Compute(rows);

        var verstappen = duels["verstappen"];
        Assert.Equal(2, verstappen.Compared);
        Assert.Equal(1, verstappen.RaceWins);
        Assert.Equal(1, verstappen.TeammateWins);
        Assert.Equal(-0.5, verstappen.AveragePositionDelta, 3);

        var perez = duels["perez"];
        Assert.Equal(1, perez.RaceWins);
        Assert.Equal(1, perez.TeammateWins);
        Assert.Equal("verstappen", perez.TeammateId);
    }

    [Fact]
    public void Compute_AveragesPositionDeltaFromTheDriversPointOfView()
    {
        var rows = ImmutableArray.Create(
            Row(1, "ferrari", "leclerc", 1), Row(1, "ferrari", "hamilton", 3),
            Row(2, "ferrari", "leclerc", 5), Row(2, "ferrari", "hamilton", 2));

        var duels = TeammateDuelService.Compute(rows);

        // Race 1: +2 for Leclerc; race 2: -3. Mean = -0.5.
        Assert.Equal(-0.5, duels["leclerc"].AveragePositionDelta, 3);
        Assert.Equal(0.5, duels["hamilton"].AveragePositionDelta, 3);
    }

    [Fact]
    public void Compute_CountsQualifyingFromTheGrid()
    {
        var rows = ImmutableArray.Create(
            Row(1, "mclaren", "norris", 2, grid: 1), Row(1, "mclaren", "piastri", 1, grid: 3),
            Row(2, "mclaren", "norris", 1, grid: 2), Row(2, "mclaren", "piastri", 4, grid: 1));

        var duels = TeammateDuelService.Compute(rows);

        Assert.Equal(1, duels["norris"].QualifyingWins);
        Assert.Equal(1, duels["norris"].QualifyingLosses);
        Assert.Equal(1, duels["piastri"].QualifyingWins);
    }

    [Fact]
    public void Compute_IgnoresRacesWithMoreThanTwoCarsInOneTeam()
    {
        // A third driver in the same constructor (a substitute) is not a teammate duel.
        var rows = ImmutableArray.Create(
            Row(1, "williams", "albon", 10),
            Row(1, "williams", "sargeant", 15),
            Row(1, "williams", "colapinto", 12));

        Assert.True(TeammateDuelService.Compute(rows).IsEmpty);
    }

    [Fact]
    public async Task GetAsync_CachesTheSeasonThenReadsItBack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"f1app-duels-{Guid.NewGuid():N}.db");
        var handler = new StubHttpHandler().Map(
            "results.json",
            HttpStatusCode.OK,
            """
            {
              "MRData": {
                "total": "2", "limit": "100", "offset": "0",
                "RaceTable": {
                  "Races": [
                    { "round": "1", "raceName": "R1", "Results": [
                      { "position": "1", "positionText": "1", "grid": "1", "status": "Finished",
                        "Driver": { "driverId": "verstappen", "givenName": "Max", "familyName": "Verstappen", "code": "VER" },
                        "Constructor": { "constructorId": "red_bull", "name": "Red Bull" } },
                      { "position": "2", "positionText": "2", "grid": "2", "status": "Finished",
                        "Driver": { "driverId": "perez", "givenName": "Sergio", "familyName": "Perez", "code": "PER" },
                        "Constructor": { "constructorId": "red_bull", "name": "Red Bull" } } ] }
                  ]
                }
              }
            }
            """);

        try
        {
            var cache = new SqliteFactsCache(path);
            var service = new TeammateDuelService(
                new JolpicaSeasonResultsClient(
                    new HttpClient(handler),
                    new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System)),
                cache,
                TimeProvider.System,
                NullLogger<TeammateDuelService>.Instance);

            var first = await service.GetAsync(2024, force: false, CancellationToken.None);
            Assert.Null(first.Problem);
            Assert.Equal(1, first.Duels["verstappen"].RaceWins);

            var requestsAfterFirst = handler.Requests.Count;
            var second = await service.GetAsync(2024, force: false, CancellationToken.None);

            Assert.Equal(requestsAfterFirst, handler.Requests.Count);
            Assert.Equal(1, second.Duels["perez"].TeammateWins);
            Assert.Equal("Max Verstappen", second.Duels["perez"].TeammateName);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Pooled handles can outlive the test.
            }
        }
    }
}

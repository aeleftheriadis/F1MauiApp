using System.Net;
using F1App.Telemetry.Facts;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

public sealed class JolpicaCircuitWinnersTests
{
    private const string Calendar = """
        {
          "MRData": {
            "RaceTable": {
              "season": "2026",
              "Races": [
                {
                  "round": "1",
                  "raceName": "Australian Grand Prix",
                  "date": "2026-03-08",
                  "time": "04:00:00Z",
                  "Circuit": { "circuitId": "albert_park", "circuitName": "Albert Park", "Location": { "country": "Australia" } }
                },
                {
                  "round": "21",
                  "raceName": "Sao Paulo Grand Prix",
                  "date": "2026-11-08",
                  "time": "17:00:00Z",
                  "Circuit": { "circuitId": "interlagos", "circuitName": "Interlagos", "Location": { "country": "Brazil" } }
                }
              ]
            }
          }
        }
        """;

    private const string Winners = """
        {
          "MRData": {
            "RaceTable": {
              "Races": [
                { "season": "2024", "Results": [ { "position": "1", "Driver": { "givenName": "Max", "familyName": "Verstappen" } } ] },
                { "season": "2023", "Results": [ { "position": "1", "Driver": { "givenName": "Max", "familyName": "Verstappen" } } ] },
                { "season": "2022", "Results": [ { "position": "1", "Driver": { "givenName": "George", "familyName": "Russell" } } ] }
              ]
            }
          }
        }
        """;

    private static (JolpicaCircuitWinners Winners, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var http = new HttpClient(handler);
        var gate = new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System);
        var calendar = new JolpicaClient(http, gate);

        return (new JolpicaCircuitWinners(http, gate, calendar, null, TimeProvider.System), handler);
    }

    [Fact]
    public async Task GetForRaceDateAsync_JoinsTheCalendarOnDateThenCountsWinners()
    {
        var (lookup, handler) = Create();
        handler
            .Map("2026.json", HttpStatusCode.OK, Calendar)
            .Map("circuits/interlagos/results/1.json", HttpStatusCode.OK, Winners);

        var history = UnionAssert.Success(await lookup.GetForRaceDateAsync(
            2026,
            new DateTimeOffset(2026, 11, 8, 17, 0, 0, TimeSpan.Zero),
            CancellationToken.None));

        Assert.Equal("Interlagos", history.CircuitName);
        Assert.Equal(3, history.Races);
        Assert.Equal("Max Verstappen", history.Winners[0].DriverName);
        Assert.Equal(2, history.Winners[0].Wins);
        Assert.Equal(2024, history.Winners[0].LastSeason);
        Assert.Equal("George Russell", history.Winners[1].DriverName);
    }

    [Fact]
    public async Task GetForRaceDateAsync_ReportsADateWithNoCalendarEntry()
    {
        var (lookup, handler) = Create();
        handler.Map("2026.json", HttpStatusCode.OK, Calendar);

        var failure = UnionAssert.Failure(await lookup.GetForRaceDateAsync(
            2026,
            new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None));

        Assert.Equal(204, failure.StatusCode);
    }

    [Fact]
    public async Task GetAsync_CachesPerCircuit()
    {
        var (lookup, handler) = Create();
        handler.Map("circuits/interlagos/results/1.json", HttpStatusCode.OK, Winners);

        await lookup.GetAsync("interlagos", "Interlagos", CancellationToken.None);
        var requests = handler.Requests.Count;
        await lookup.GetAsync("interlagos", "Interlagos", CancellationToken.None);

        Assert.Equal(requests, handler.Requests.Count);
    }
}

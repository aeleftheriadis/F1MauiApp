using System.Net;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

public sealed class JolpicaClientTests
{
    private const string TwoRacePayload = """
        {
          "MRData": {
            "RaceTable": {
              "season": "2024",
              "Races": [
                {
                  "round": "1",
                  "raceName": "Bahrain Grand Prix",
                  "date": "2024-03-02",
                  "time": "15:00:00Z",
                  "Circuit": {
                    "circuitId": "bahrain",
                    "circuitName": "Bahrain International Circuit",
                    "Location": { "locality": "Sakhir", "country": "Bahrain" }
                  }
                },
                {
                  "round": "2",
                  "raceName": "Saudi Arabian Grand Prix",
                  "date": "2024-03-09",
                  "Circuit": {
                    "circuitId": "jeddah",
                    "circuitName": "Jeddah Corniche Circuit",
                    "Location": { "locality": "Jeddah", "country": "Saudi Arabia" }
                  }
                }
              ]
            }
          }
        }
        """;

    private static (JolpicaClient Client, StubHttpHandler Handler) Create(RequestGate? gate = null)
    {
        var handler = new StubHttpHandler();
        var http = new HttpClient(handler);
        var client = new JolpicaClient(
            http,
            gate ?? new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (client, handler);
    }

    [Fact]
    public async Task GetSeasonAsync_MapsTheCalendar()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, TwoRacePayload);

        var result = await client.GetSeasonAsync(2024, CancellationToken.None);

        var races = UnionAssert.Success(result);
        Assert.Equal(2, races.Length);

        Assert.Equal(1, races[0].Round);
        Assert.Equal("Bahrain Grand Prix", races[0].Name);
        Assert.Equal("bahrain", races[0].Circuit.Id);
        Assert.Equal("Bahrain", races[0].Circuit.Country);
        Assert.Equal(new DateTimeOffset(2024, 3, 2, 15, 0, 0, TimeSpan.Zero), races[0].RaceStart);

        Assert.EndsWith("/2024.json?limit=30", handler.Requests[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSeasonAsync_ReportsRateLimitingAndAppliesBackoff()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(42));

        var result = await client.GetSeasonAsync(2024, CancellationToken.None);

        var limited = UnionAssert.Limited(result);
        Assert.Equal(42, limited.RetryAfterSeconds);
    }

    [Fact]
    public async Task GetSeasonAsync_ReportsTransportFailures()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");

        var result = await client.GetSeasonAsync(2024, CancellationToken.None);

        var failure = UnionAssert.Failure(result);
        Assert.Equal(500, failure.StatusCode);
    }

    [Fact]
    public async Task GetSeasonAsync_RejectsImplausibleSeason()
    {
        var (client, _) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetSeasonAsync(1900, CancellationToken.None));
    }
}

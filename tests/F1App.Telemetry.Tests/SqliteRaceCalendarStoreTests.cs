using System.Net;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

public sealed class SqliteRaceCalendarStoreTests : IDisposable
{
    private const string OneRacePayload = """
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
                }
              ]
            }
          }
        }
        """;

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"f1app-calendar-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task GetSeasonAsync_ServesTheSecondReadFromTheCache()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, OneRacePayload);
        var provider = new JolpicaClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var store = new SqliteRaceCalendarStore(_databasePath, provider);

        var first = await store.GetSeasonAsync(2024, refresh: true, CancellationToken.None);
        var second = await store.GetSeasonAsync(2024, refresh: false, CancellationToken.None);

        Assert.Single(UnionAssert.Success(first));
        Assert.Single(UnionAssert.Success(second));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSeasonAsync_FallsBackToTheStaleCacheWhenTheProviderFails()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, OneRacePayload);
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "down");

        var provider = new JolpicaClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var store = new SqliteRaceCalendarStore(_databasePath, provider);

        await store.GetSeasonAsync(2024, refresh: true, CancellationToken.None);
        var offline = await store.GetSeasonAsync(2024, refresh: true, CancellationToken.None);

        Assert.Single(UnionAssert.Success(offline));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSeasonAsync_ReturnsTheFailureWhenNothingIsCached()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "down");

        var provider = new JolpicaClient(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var store = new SqliteRaceCalendarStore(_databasePath, provider);
        var result = await store.GetSeasonAsync(2024, refresh: false, CancellationToken.None);

        UnionAssert.Failure(result);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}

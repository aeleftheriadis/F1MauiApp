using System.Net;
using F1App.Telemetry.Providers;
using F1App.Telemetry.Strategy;

namespace F1App.Telemetry.Tests;

public sealed class OpenF1StrategyLoaderTests
{
    private const string Stints = """
        [
          {"driver_number":44,"stint_number":1,"lap_start":1,"lap_end":14,"compound":"SOFT","tyre_age_at_start":0},
          {"driver_number":44,"stint_number":2,"lap_start":15,"lap_end":40,"compound":"HARD","tyre_age_at_start":2},
          {"driver_number":63,"stint_number":1,"lap_start":1,"lap_end":40,"compound":"MEDIUM","tyre_age_at_start":0}
        ]
        """;

    private const string Drivers = """
        [
          {"driver_number":44,"name_acronym":"HAM"},
          {"driver_number":63,"name_acronym":"RUS"}
        ]
        """;

    private static (OpenF1StrategyLoader Loader, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (new OpenF1StrategyLoader(client), handler);
    }

    [Fact]
    public async Task LoadAsync_BuildsPerDriverStintsInCodeOrder()
    {
        var (loader, handler) = Create();
        handler
            .Enqueue(HttpStatusCode.OK, Stints)
            .Enqueue(HttpStatusCode.OK, Drivers);

        var timeline = UnionAssert.Success(await loader.LoadAsync(9472, "Race", CancellationToken.None));

        Assert.Equal(40, timeline.LastLap);
        Assert.Equal(2, timeline.Drivers.Length);

        Assert.Equal("HAM", timeline.Drivers[0].Code);
        Assert.Equal(2, timeline.Drivers[0].Stints.Length);
        Assert.Equal("SOFT", timeline.Drivers[0].Stints[0].Compound);
        Assert.Equal(14, timeline.Drivers[0].Stints[0].LapEnd);
        Assert.Equal(2, timeline.Drivers[0].Stints[1].TyreAgeAtStart);

        Assert.Equal("RUS", timeline.Drivers[1].Code);
        Assert.Single(timeline.Drivers[1].Stints);
    }

    [Fact]
    public async Task LoadAsync_ClosesAnOpenStintAtTheLastLap()
    {
        var (loader, handler) = Create();
        handler
            .Enqueue(HttpStatusCode.OK, """
                [{"driver_number":44,"stint_number":1,"lap_start":30,"compound":"HARD","tyre_age_at_start":0}]
                """)
            .Enqueue(HttpStatusCode.OK, Drivers);

        var timeline = UnionAssert.Success(await loader.LoadAsync(9472, "Race", CancellationToken.None));

        Assert.Equal(30, timeline.LastLap);
        Assert.Equal(30, timeline.Drivers[0].Stints[0].LapEnd);
    }

    [Fact]
    public async Task LoadAsync_ReportsASessionWithoutStints()
    {
        var (loader, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        Assert.Equal(
            204,
            UnionAssert.Failure(await loader.LoadAsync(9472, "Race", CancellationToken.None)).StatusCode);
    }
}

using System.Net;
using F1App.Telemetry.Providers;
using F1App.Telemetry.Timing;

namespace F1App.Telemetry.Tests;

public sealed class OpenF1SessionTimingLoaderTests
{
    private const int SessionKey = 9472;
    private static readonly DateTimeOffset Start = new(2024, 3, 2, 15, 0, 0, TimeSpan.Zero);

    // Lap 1 of car 44 is a pit-out lap and deliberately the fastest, so it must be ignored.
    private const string Laps = """
        [
          {"driver_number":44,"lap_number":1,"lap_duration":80.0,"duration_sector_1":20.0,"duration_sector_2":35.0,"duration_sector_3":25.0,"is_pit_out_lap":true},
          {"driver_number":44,"lap_number":2,"lap_duration":94.0,"duration_sector_1":29.5,"duration_sector_2":39.9,"duration_sector_3":24.6,"is_pit_out_lap":false},
          {"driver_number":63,"lap_number":1,"lap_duration":94.5,"duration_sector_1":29.8,"duration_sector_2":40.1,"duration_sector_3":24.6,"is_pit_out_lap":false}
        ]
        """;

    private const string Positions = """
        [
          {"driver_number":44,"position":2,"date":"2024-03-02T15:00:00.000000+00:00"},
          {"driver_number":44,"position":1,"date":"2024-03-02T16:00:00.000000+00:00"},
          {"driver_number":63,"position":2,"date":"2024-03-02T16:00:00.000000+00:00"}
        ]
        """;

    private const string Intervals = """
        [
          {"driver_number":44,"gap_to_leader":0.0,"interval":0.0,"date":"2024-03-02T16:00:00.000000+00:00"},
          {"driver_number":63,"gap_to_leader":4.512,"interval":4.512,"date":"2024-03-02T16:00:00.000000+00:00"}
        ]
        """;

    private const string Stints = """
        [
          {"driver_number":44,"stint_number":1,"lap_start":1,"lap_end":29,"compound":"SOFT","tyre_age_at_start":0},
          {"driver_number":44,"stint_number":2,"lap_start":30,"lap_end":40,"compound":"MEDIUM","tyre_age_at_start":2},
          {"driver_number":63,"stint_number":1,"lap_start":1,"lap_end":40,"compound":"HARD","tyre_age_at_start":0}
        ]
        """;

    private const string Pits = """
        [
          {"driver_number":44,"lap_number":29,"pit_duration":24.1},
          {"driver_number":63,"lap_number":18,"pit_duration":23.4},
          {"driver_number":63,"lap_number":39,"pit_duration":22.9}
        ]
        """;

    private const string Drivers = """
        [
          {"driver_number":44,"name_acronym":"HAM","full_name":"Lewis Hamilton","team_name":"Ferrari","team_colour":"E8002D"},
          {"driver_number":63,"name_acronym":"RUS","full_name":"George Russell","team_name":"Mercedes","team_colour":"00D2BE"}
        ]
        """;

    private static (OpenF1SessionTimingLoader Loader, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (new OpenF1SessionTimingLoader(client), handler);
    }

    [Fact]
    public async Task LoadAsync_DerivesTheClassificationFromPublishedData()
    {
        var (loader, handler) = Create();
        handler
            .Enqueue(HttpStatusCode.OK, Laps)
            .Enqueue(HttpStatusCode.OK, Positions)
            .Enqueue(HttpStatusCode.OK, Intervals)
            .Enqueue(HttpStatusCode.OK, Stints)
            .Enqueue(HttpStatusCode.OK, Pits)
            .Enqueue(HttpStatusCode.OK, Drivers);

        var result = await loader.LoadAsync(SessionKey, "Race", Start, CancellationToken.None);
        var timing = UnionAssert.Success(result);

        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(2, timing.Entries.Length);

        var leader = timing.Entries[0];
        Assert.Equal(44, leader.DriverNumber);
        Assert.Equal("HAM", leader.Code);
        Assert.Equal("#E8002D", leader.TeamColourHex);
        Assert.Equal(1, leader.Position);

        // The pit-out lap was faster but must not count, and neither may its sectors.
        Assert.Equal(TimeSpan.FromSeconds(94.0), leader.BestLap);
        Assert.Equal(TimeSpan.FromSeconds(29.5), leader.BestSector1);
        Assert.Equal(0d, leader.GapToLeaderSeconds);

        // Tyre age is the stint's start age plus the laps run in it: 2 + (40 - 30 + 1).
        Assert.Equal("MEDIUM", leader.Compound);
        Assert.Equal(13, leader.TyreAgeLaps);
        Assert.Equal(1, leader.PitStops);

        var second = timing.Entries[1];
        Assert.Equal(63, second.DriverNumber);
        Assert.Equal(2, second.Position);
        Assert.Equal(4.512, second.GapToLeaderSeconds);
        Assert.Equal("HARD", second.Compound);
        Assert.Equal(40, second.TyreAgeLaps);
        Assert.Equal(2, second.PitStops);
    }

    [Fact]
    public async Task LoadAsync_TreatsALappedGapStringAsUnknownSeconds()
    {
        var (loader, handler) = Create();
        handler
            .Enqueue(HttpStatusCode.OK, Laps)
            .Enqueue(HttpStatusCode.OK, Positions)
            .Enqueue(HttpStatusCode.OK, """
                [
                  {"driver_number":44,"gap_to_leader":0.0,"interval":0.0,"date":"2024-03-02T16:00:00.000000+00:00"},
                  {"driver_number":63,"gap_to_leader":"+1 LAP","interval":"+1 LAP","date":"2024-03-02T16:00:00.000000+00:00"}
                ]
                """)
            .Enqueue(HttpStatusCode.OK, Stints)
            .Enqueue(HttpStatusCode.OK, Pits)
            .Enqueue(HttpStatusCode.OK, Drivers);

        var timing = UnionAssert.Success(await loader.LoadAsync(SessionKey, "Race", Start, CancellationToken.None));

        Assert.Null(timing.Entries[1].GapToLeaderSeconds);
    }

    [Fact]
    public async Task LoadAsync_StopsAtTheFirstEmptyEndpoint()
    {
        var (loader, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        var result = await loader.LoadAsync(SessionKey, "Race", Start, CancellationToken.None);

        var failure = UnionAssert.Failure(result);
        Assert.Equal(204, failure.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_PropagatesRateLimiting()
    {
        var (loader, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(17));

        var result = await loader.LoadAsync(SessionKey, "Race", Start, CancellationToken.None);

        Assert.Equal(17, UnionAssert.Limited(result).RetryAfterSeconds);
    }
}

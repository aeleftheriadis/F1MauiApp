using System.Net;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

public sealed class TrackConditionsTests
{
    [Fact]
    public void Describe_FormatsEveryPublishedField()
    {
        var conditions = new TrackConditions(27.8, 52.5, 58, Raining: false, WindMetresPerSecond: 2.4);

        Assert.Equal("air 27.8 °C · track 52.5 °C · 58% humidity · wind 2.4 m/s · dry", conditions.Describe());
    }

    [Fact]
    public void Describe_LeavesOutFieldsTheProviderDidNotPublish()
    {
        var conditions = new TrackConditions(null, 41.0, null, Raining: true, WindMetresPerSecond: null);

        Assert.Equal("track 41.0 °C · rain", conditions.Describe());
    }

    [Fact]
    public void Describe_WithNothingPublishedStillStatesTheRainFlag()
    {
        Assert.Equal("dry", new TrackConditions(null, null, null, false, null).Describe());
        Assert.Equal("rain", new TrackConditions(null, null, null, true, null).Describe());
    }
}

public sealed class OpenF1WeatherTests
{
    /// <summary>Shape taken from the provider's documented example.</summary>
    private const string Payload = """
        [
          {
            "air_temperature": 27.8,
            "date": "2023-05-07T18:42:25.233000+00:00",
            "humidity": 58,
            "meeting_key": 1208,
            "pressure": 1018.7,
            "rainfall": 0,
            "session_key": 9078,
            "track_temperature": 52.5,
            "wind_direction": 136,
            "wind_speed": 2.4
          },
          {
            "air_temperature": 26.1,
            "date": "2023-05-07T18:43:25.233000+00:00",
            "humidity": 62,
            "rainfall": 1,
            "session_key": 9078,
            "track_temperature": 49.0,
            "wind_direction": 140,
            "wind_speed": 3.1
          }
        ]
        """;

    [Fact]
    public async Task GetWeatherAsync_ParsesEveryFieldTheConditionsLineNeeds()
    {
        var handler = new StubHttpHandler().Map("weather?session_key=9078", HttpStatusCode.OK, Payload);
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var samples = UnionAssert.Success(await client.GetWeatherAsync(9078, CancellationToken.None));

        Assert.Equal(2, samples.Length);
        Assert.Equal(27.8, samples[0].AirTemperature);
        Assert.Equal(52.5, samples[0].TrackTemperature);
        Assert.Equal(58, samples[0].Humidity);
        Assert.Equal(0, samples[0].Rainfall);
        Assert.Equal(2.4, samples[0].WindSpeed);

        // The screen shows the latest sample, so ordering by date has to work.
        var latest = samples.MaxBy(static sample => sample.Date)!;
        Assert.Equal(1, latest.Rainfall);
        Assert.Equal(
            "air 26.1 °C · track 49.0 °C · 62% humidity · wind 3.1 m/s · rain",
            new TrackConditions(
                latest.AirTemperature,
                latest.TrackTemperature,
                latest.Humidity,
                latest.Rainfall is not null and not 0,
                latest.WindSpeed).Describe());
    }

    [Fact]
    public async Task GetWeatherAsync_ReportsAFailingSession()
    {
        var handler = new StubHttpHandler().Map("weather", HttpStatusCode.NotFound, "{}");
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        UnionAssert.Failure(await client.GetWeatherAsync(9078, CancellationToken.None));
    }
}

public sealed class OpenF1SessionResultTests
{
    /// <summary>Shape taken from the provider's documented example, plus the points field it returns.</summary>
    private const string Payload = """
        [
          { "dnf": false, "dns": false, "dsq": false, "driver_number": 1, "duration": 77.565,
            "gap_to_leader": 0, "number_of_laps": 24, "position": 1, "points": 25.0, "session_key": 7782 },
          { "dnf": true, "dns": false, "dsq": false, "driver_number": 14, "duration": null,
            "gap_to_leader": null, "number_of_laps": 12, "position": 18, "points": 0.0, "session_key": 7782 },
          { "dnf": false, "dns": false, "dsq": true, "driver_number": 31, "duration": null,
            "gap_to_leader": null, "number_of_laps": 30, "position": null, "points": 0.0, "session_key": 7782 }
        ]
        """;

    [Fact]
    public async Task GetSessionResultAsync_ParsesPointsAndTheClassifiedFlags()
    {
        var handler = new StubHttpHandler().Map("session_result?session_key=7782", HttpStatusCode.OK, Payload);
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var rows = UnionAssert.Success(await client.GetSessionResultAsync(7782, CancellationToken.None));

        Assert.Equal(3, rows.Length);
        Assert.Equal(25.0, rows[0].Points);
        Assert.False(rows[0].DidNotFinish);
        Assert.True(rows[1].DidNotFinish);
        Assert.True(rows[2].Disqualified);
        Assert.Null(rows[2].Position);
    }

    [Fact]
    public async Task GetSessionResultAsync_ReportsAPublishedNothing()
    {
        // A session a few minutes old answers 200 with an empty array; the screen must say so, not crash.
        var handler = new StubHttpHandler().Map("session_result", HttpStatusCode.OK, "[]");
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        Assert.Empty(UnionAssert.Success(await client.GetSessionResultAsync(7782, CancellationToken.None)));
    }
}

public sealed class RaceControlTests
{
    private const string Payload = """
        [
          { "category": "Flag", "date": "2023-06-04T14:21:01+00:00", "driver_number": 1, "flag": "BLACK AND WHITE",
            "lap_number": 59, "message": "BLACK AND WHITE FLAG FOR CAR 1 (VER) - TRACK LIMITS", "scope": "Driver",
            "sector": null, "qualifying_phase": null },
          { "category": "SafetyCar", "date": "2023-06-04T14:25:01+00:00", "driver_number": null, "flag": null,
            "lap_number": 61, "message": "SAFETY CAR DEPLOYED", "scope": "Track", "sector": null,
            "qualifying_phase": null }
        ]
        """;

    [Fact]
    public async Task GetRaceControlAsync_ParsesTheFieldsTheStripShows()
    {
        var handler = new StubHttpHandler().Map("race_control?session_key=9102", HttpStatusCode.OK, Payload);
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var events = UnionAssert.Success(await client.GetRaceControlAsync(9102, CancellationToken.None));

        Assert.Equal(2, events.Length);
        Assert.Equal("BLACK AND WHITE", events[0].Flag);
        Assert.Equal(59, events[0].LapNumber);
        Assert.Equal("SafetyCar", events[1].Category);
        Assert.Null(events[1].Flag);
    }

    [Fact]
    public void Describe_PrefersTheFlagOverTheCategory()
    {
        var flagged = new RaceControlEvent("Flag", "YELLOW", "YELLOW IN SECTOR 2", 41);

        Assert.Equal("lap 41 · YELLOW · YELLOW IN SECTOR 2", flagged.Describe());
    }

    [Fact]
    public void Describe_FallsBackToTheCategoryWhenThereIsNoFlag()
    {
        var safetyCar = new RaceControlEvent("SafetyCar", string.Empty, "SAFETY CAR DEPLOYED", 61);

        Assert.Equal("lap 61 · SafetyCar · SAFETY CAR DEPLOYED", safetyCar.Describe());
    }

    [Fact]
    public void Describe_TrimsALongMessageWithoutLosingTheFlag()
    {
        var longMessage = new RaceControlEvent("Flag", "YELLOW", new string('X', 200), 12);

        var described = longMessage.Describe(maxMessageLength: 20);

        Assert.StartsWith("lap 12 · YELLOW · ", described, StringComparison.Ordinal);
        Assert.EndsWith("…", described, StringComparison.Ordinal);
        Assert.True(described.Length < 40);
    }

    [Fact]
    public void Describe_WithNoLapAndNoFlagStillReadsAsOneSentence()
    {
        var early = new RaceControlEvent("SessionStatus", string.Empty, "SESSION STARTED", null);

        Assert.Equal("SessionStatus · SESSION STARTED", early.Describe());
    }
}

public sealed class StartingGridTests
{
    private const string Payload = """
        [
          { "position": 1, "driver_number": 1, "lap_duration": 76.732, "meeting_key": 1143, "session_key": 7783 },
          { "position": 2, "driver_number": 63, "lap_duration": 76.968, "meeting_key": 1143, "session_key": 7783 }
        ]
        """;

    [Fact]
    public async Task GetStartingGridAsync_AsksTheQualifyingSessionAndParsesTheGrid()
    {
        var handler = new StubHttpHandler().Map("starting_grid?session_key=7783", HttpStatusCode.OK, Payload);
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var grid = UnionAssert.Success(await client.GetStartingGridAsync(7783, CancellationToken.None));

        Assert.Equal(2, grid.Length);
        Assert.Equal(1, grid[0].Position);
        Assert.Equal(63, grid[1].DriverNumber);

        // The measured trap: the race key answers an empty list, not an error.
        handler.Map("starting_grid?session_key=7782", HttpStatusCode.OK, "[]");
        var raceKey = UnionAssert.Success(await client.GetStartingGridAsync(7782, CancellationToken.None));
        Assert.Empty(raceKey);
    }
}

public sealed class PlacesGainedTests
{
    [Fact]
    public void Describe_CountsAGainAsPositive()
    {
        // Started 5th, finished 2nd: three places gained.
        Assert.Equal("+3", new PlacesGained(5, 2).Describe());
    }

    [Fact]
    public void Describe_CountsALossAsNegative()
    {
        Assert.Equal("-4", new PlacesGained(2, 6).Describe());
    }

    [Fact]
    public void Describe_ReportsHoldingStation()
    {
        Assert.Equal("0", new PlacesGained(7, 7).Describe());
    }
}

public sealed class OvertakesTests
{
    private const string Payload = """
        [
          { "date": "2024-11-03T15:50:07.565000+00:00", "meeting_key": 1249, "overtaken_driver_number": 4,
            "overtaking_driver_number": 63, "position": 1, "session_key": 9636 },
          { "date": "2024-11-03T15:52:07.565000+00:00", "meeting_key": 1249, "overtaken_driver_number": 63,
            "overtaking_driver_number": 4, "position": 1, "session_key": 9636 }
        ]
        """;

    [Fact]
    public async Task GetOvertakesAsync_ParsesWhoPassedWhom()
    {
        var handler = new StubHttpHandler().Map("overtakes?session_key=9636", HttpStatusCode.OK, Payload);
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var passes = UnionAssert.Success(await client.GetOvertakesAsync(9636, CancellationToken.None));

        Assert.Equal(2, passes.Length);
        Assert.Equal(63, passes[0].OvertakingDriverNumber);
        Assert.Equal(4, passes[0].OvertakenDriverNumber);
        Assert.Equal(1, passes[0].Position);
    }

    [Fact]
    public void Tally_DescribesBothDirections()
    {
        // One pass made, one suffered - the same fight seen from each car.
        Assert.Equal("1 made, 1 lost", new OvertakeTally(1, 1).Describe());
        Assert.Equal("4 made, 0 lost", new OvertakeTally(4, 0).Describe());
    }

    [Fact]
    public void Tally_IsEmptyWhenTheDriverWasNotInvolved()
    {
        Assert.Equal(string.Empty, new OvertakeTally(0, 0).Describe());
    }
}

public sealed class ChampionshipSwingTests
{
    private const string Payload = """
        [
          { "driver_number": 4, "meeting_key": 1276, "points_current": 423, "points_start": 408,
            "position_current": 1, "position_start": 1, "session_key": 9839 },
          { "driver_number": 81, "meeting_key": 1276, "points_current": 410, "points_start": 392,
            "position_current": 3, "position_start": 5, "session_key": 9839 }
        ]
        """;

    [Fact]
    public async Task GetChampionshipDriversAsync_ParsesTheBeforeAndAfterFigures()
    {
        var handler = new StubHttpHandler().Map("championship_drivers?session_key=9839", HttpStatusCode.OK, Payload);
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        var rows = UnionAssert.Success(await client.GetChampionshipDriversAsync(9839, CancellationToken.None));

        Assert.Equal(2, rows.Length);
        Assert.Equal(408, rows[0].PointsStart);
        Assert.Equal(423, rows[0].PointsCurrent);
        Assert.Equal(5, rows[1].PositionStart);
        Assert.Equal(3, rows[1].PositionCurrent);
    }

    [Fact]
    public void Describe_ReportsPointsGainedAndPlacesMoved()
    {
        var swing = new ChampionshipSwing(PositionStart: 5, PositionNow: 3, PointsStart: 392, PointsNow: 410);

        // 5th to 3rd is a two-place gain, not three.
        Assert.Equal("2", swing.PositionsMoved?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("title P3 · 410 pts · +18 this race · 2 places gained", swing.Describe());
    }

    [Fact]
    public void Describe_ReportsPlacesLost()
    {
        var swing = new ChampionshipSwing(PositionStart: 2, PositionNow: 4, PointsStart: 300, PointsNow: 302);

        Assert.Equal("title P4 · 302 pts · +2 this race · 2 places lost", swing.Describe());
    }

    [Fact]
    public void Describe_SaysNothingWhenNothingWasPublished()
    {
        Assert.Equal(string.Empty, new ChampionshipSwing(null, null, null, null).Describe());
    }

    [Fact]
    public void Describe_OmitsTheSwingWhenTheDriverDidNotMove()
    {
        var swing = new ChampionshipSwing(PositionStart: 1, PositionNow: 1, PointsStart: 400, PointsNow: 400);

        Assert.Equal("title P1 · 400 pts", swing.Describe());
    }
}

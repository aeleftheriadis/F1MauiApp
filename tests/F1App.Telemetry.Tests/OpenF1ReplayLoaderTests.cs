using System.Globalization;
using System.Net;
using System.Text;
using F1App.Domain;
using F1App.Telemetry.Providers;
using F1App.Telemetry.Replay;

namespace F1App.Telemetry.Tests;

public sealed class OpenF1ReplayLoaderTests
{
    private const int SessionKey = 9472;
    private static readonly DateTimeOffset From = new(2024, 3, 2, 15, 29, 0, TimeSpan.Zero);

    /// <summary>JSON for one lap of GPS: a closed circle, one sample per second.</summary>
    private static string OutlineJson(int driver, int segments = 60, float radius = 5000)
    {
        var builder = new StringBuilder("[");

        for (var i = 0; i <= segments; i++)
        {
            var angle = i * Math.Tau / segments;
            var x = (int)(radius * Math.Cos(angle));
            var y = (int)(radius * Math.Sin(angle));
            var time = From.UtcDateTime.AddSeconds(i).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+00:00'", CultureInfo.InvariantCulture);

            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(CultureInfo.InvariantCulture,
                $$"""{"driver_number":{{driver}},"date":"{{time}}","x":{{x}},"y":{{y}},"z":0,"session_key":{{SessionKey}},"meeting_key":1229}""");
        }

        return builder.Append(']').ToString();
    }

    private static string LocationsJson()
    {
        var builder = new StringBuilder("[");

        for (var driver = 0; driver < 3; driver++)
        {
            for (var second = 0; second < 2; second++)
            {
                if (driver > 0 || second > 0)
                {
                    builder.Append(',');
                }

                var number = driver == 0 ? 44 : driver == 1 ? 63 : 99;
                var time = From.UtcDateTime.AddSeconds(second).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+00:00'", CultureInfo.InvariantCulture);

                builder.Append(CultureInfo.InvariantCulture,
                    $$"""{"driver_number":{{number}},"date":"{{time}}","x":{{1000 * driver}},"y":{{500 * second}},"z":0,"session_key":{{SessionKey}},"meeting_key":1229}""");
            }
        }

        return builder.Append(']').ToString();
    }

    private static string CarDataJson()
    {
        var builder = new StringBuilder("[");

        // Drivers 44 and 63 have telemetry; 99 (the third location stream) deliberately does not.
        var drivers = new[] { 44, 63 };

        for (var index = 0; index < drivers.Length; index++)
        {
            for (var second = 0; second < 2; second++)
            {
                if (index > 0 || second > 0)
                {
                    builder.Append(',');
                }

                var time = From.UtcDateTime.AddSeconds(second).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+00:00'", CultureInfo.InvariantCulture);
                var drs = second == 0 ? 10 : 8;
                var brake = second == 0 ? 0 : 100;

                builder.Append(CultureInfo.InvariantCulture,
                    $$"""{"driver_number":{{drivers[index]}},"date":"{{time}}","speed":{{300 + second}},"throttle":{{(second == 0 ? 100 : 0)}},"brake":{{brake}},"n_gear":8,"rpm":12000,"drs":{{drs}},"session_key":{{SessionKey}}}""");
            }
        }

        return builder.Append(']').ToString();
    }

    private const string Drivers = """
        [
          {"driver_number":44,"name_acronym":"HAM","full_name":"Lewis Hamilton","team_name":"Ferrari","team_colour":"E8002D"},
          {"driver_number":63,"name_acronym":"RUS","full_name":"George Russell","team_name":"Mercedes","team_colour":"00D2BE"},
          {"driver_number":99,"name_acronym":"TST","full_name":"Test Driver","team_name":"Test","team_colour":"123456"}
        ]
        """;

    private static (OpenF1ReplayLoader Loader, StubHttpHandler Handler) Create()
    {
        var handler = new StubHttpHandler();
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (new OpenF1ReplayLoader(client, new OpenF1TrackOutlineLoader(client)), handler);
    }

    [Fact]
    public async Task LoadAsync_BuildsTheOutlineAndPairsPositionsWithTelemetry()
    {
        var (loader, handler) = Create();
        handler
            .Enqueue(HttpStatusCode.OK, OutlineJson(44))
            .Enqueue(HttpStatusCode.OK, LocationsJson())
            .Enqueue(HttpStatusCode.OK, CarDataJson())
            .Enqueue(HttpStatusCode.OK, Drivers);

        var result = await loader.LoadAsync(
            SessionKey,
            "Bahrain 2024 · Race",
            From,
            TimeSpan.FromSeconds(2),
            outlineDriver: 44,
            CancellationToken.None);

        var session = UnionAssert.Success(result);

        Assert.Equal(61, session.Outline.Points.Length);
        Assert.Equal("Bahrain 2024 · Race", session.Label);

        // Real identities, not "CAR 44": the tower and the map colour by these.
        Assert.Equal(3, session.Drivers.Length);
        Assert.Equal("HAM", session.DriverFor(44).Code);
        Assert.Equal("#E8002D", session.DriverFor(44).TeamColorHex);
        Assert.Equal("Mercedes", session.DriverFor(63).TeamName);

        // Three drivers sent positions, two have telemetry: the unpaired driver must be dropped
        // rather than rendered with invented speed.
        Assert.Equal(4, session.Samples.Length);
        Assert.DoesNotContain(session.Samples, sample => sample.DriverNumber == 99);

        var first = session.Samples[0];
        Assert.Equal(44, first.DriverNumber);
        Assert.Equal(300, first.SpeedKph);
        Assert.False(first.Braking);
        Assert.Equal(DrsState.Active, first.Drs);

        // Samples are ordered by time, so the two cars interleave within each timestamp.
        Assert.Equal(63, session.Samples[1].DriverNumber);
        Assert.Equal(300, session.Samples[1].SpeedKph);

        var braking = session.Samples[2];
        Assert.Equal(44, braking.DriverNumber);
        Assert.Equal(301, braking.SpeedKph);
        Assert.True(braking.Braking);
        Assert.Equal(DrsState.Available, braking.Drs);

        Assert.Equal(63, session.Samples[3].DriverNumber);
        Assert.Equal(301, session.Samples[3].SpeedKph);
    }

    [Fact]
    public async Task LoadAsync_RequestsAllDriversForTheReplayWindow()
    {
        var (loader, handler) = Create();
        handler
            .Enqueue(HttpStatusCode.OK, OutlineJson(44))
            .Enqueue(HttpStatusCode.OK, LocationsJson())
            .Enqueue(HttpStatusCode.OK, CarDataJson())
            .Enqueue(HttpStatusCode.OK, Drivers);

        await loader.LoadAsync(SessionKey, "label", From, TimeSpan.FromSeconds(2), 44, CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);

        var outlineRequest = handler.Requests[0];
        Assert.Contains("driver_number=44", outlineRequest.ToString(), StringComparison.Ordinal);
        Assert.Contains("date", outlineRequest.ToString(), StringComparison.Ordinal);

        // The replay window covers every car: no driver filter.
        var replayRequest = handler.Requests[1];
        Assert.DoesNotContain("driver_number", replayRequest.ToString(), StringComparison.Ordinal);
        Assert.Contains("location", replayRequest.ToString(), StringComparison.Ordinal);

        Assert.Contains("car_data", handler.Requests[2].ToString(), StringComparison.Ordinal);
        Assert.Contains("drivers", handler.Requests[3].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_ReportsWhenNoLapCanBeTraced()
    {
        var (loader, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, OutlineJson(44, segments: 6));

        var result = await loader.LoadAsync(SessionKey, "label", From, TimeSpan.FromSeconds(2), 44, CancellationToken.None);

        var failure = UnionAssert.Failure(result);
        Assert.Equal(204, failure.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_PropagatesTransportFailures()
    {
        var (loader, handler) = Create();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "down");

        var result = await loader.LoadAsync(SessionKey, "label", From, TimeSpan.FromSeconds(2), 44, CancellationToken.None);

        Assert.Equal(503, UnionAssert.Failure(result).StatusCode);
    }
}

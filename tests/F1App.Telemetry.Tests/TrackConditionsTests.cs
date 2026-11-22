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

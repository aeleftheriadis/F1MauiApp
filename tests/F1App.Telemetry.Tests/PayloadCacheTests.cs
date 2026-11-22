using System.Net;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

public sealed class PayloadCacheTests
{
    private static string DbPath() =>
        Path.Combine(Path.GetTempPath(), $"f1app-payloads-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ReadAsync_ReturnsTheBodyWithinItsLifetimeAndNullAfter()
    {
        var path = DbPath();
        try
        {
            var cache = new SqlitePayloadCache(path);
            var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

            await cache.WriteAsync("weather?session_key=1", "[{\"a\":1}]", now, CancellationToken.None);

            Assert.Equal(
                "[{\"a\":1}]",
                await cache.ReadAsync("weather?session_key=1", now.AddMinutes(5), TimeSpan.FromHours(1), CancellationToken.None));

            Assert.Null(
                await cache.ReadAsync("weather?session_key=1", now.AddHours(2), TimeSpan.FromHours(1), CancellationToken.None));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task WriteAsync_ReplacesRatherThanDuplicating()
    {
        var path = DbPath();
        try
        {
            var cache = new SqlitePayloadCache(path);
            var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

            await cache.WriteAsync("laps?session_key=1", "[1]", now, CancellationToken.None);
            await cache.WriteAsync("laps?session_key=1", "[2]", now, CancellationToken.None);

            Assert.Equal(1, await cache.CountAsync(CancellationToken.None));
            Assert.Equal("[2]", await cache.ReadAsync("laps?session_key=1", now, TimeSpan.FromDays(1), CancellationToken.None));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void LifetimeFor_KeepsFinishedSessionDataLongerThanTheCalendar()
    {
        // A finished session cannot change; a calendar can.
        Assert.Equal(TimeSpan.FromDays(7), SqlitePayloadCache.LifetimeFor("weather?session_key=1"));
        Assert.Equal(TimeSpan.FromDays(7), SqlitePayloadCache.LifetimeFor("laps?session_key=1"));
        Assert.Equal(TimeSpan.FromHours(6), SqlitePayloadCache.LifetimeFor("sessions?year=2026"));
        Assert.Equal(TimeSpan.FromHours(6), SqlitePayloadCache.LifetimeFor("meetings?year=2026"));
    }

    [Fact]
    public async Task OpenF1Client_ServesTheSecondCallFromCacheWithoutAskingTheProvider()
    {
        var path = DbPath();
        try
        {
            var handler = new StubHttpHandler().Map("weather?session_key=7", HttpStatusCode.OK, """
                [ { "air_temperature": 21.5, "track_temperature": 30.0, "rainfall": 0 } ]
                """);

            var client = new OpenF1Client(
                new HttpClient(handler),
                new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System),
                resilience: null,
                cache: new SqlitePayloadCache(path),
                timeProvider: TimeProvider.System);

            var first = UnionAssert.Success(await client.GetWeatherAsync(7, CancellationToken.None));
            var requestsAfterFirst = handler.Requests.Count;
            var second = UnionAssert.Success(await client.GetWeatherAsync(7, CancellationToken.None));

            Assert.Equal(1, requestsAfterFirst);
            Assert.Equal(requestsAfterFirst, handler.Requests.Count);
            Assert.Equal(21.5, first[0].AirTemperature);
            Assert.Equal(21.5, second[0].AirTemperature);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task OpenF1Client_DropsACachedBodyThatNoLongerParses()
    {
        var path = DbPath();
        try
        {
            var cache = new SqlitePayloadCache(path);
            await cache.WriteAsync("weather?session_key=9", "not json at all", TimeProvider.System.GetUtcNow(), CancellationToken.None);

            var handler = new StubHttpHandler().Map("weather?session_key=9", HttpStatusCode.OK, "[ { \"air_temperature\": 19.0 } ]");
            var client = new OpenF1Client(
                new HttpClient(handler),
                new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System),
                cache: cache);

            var samples = UnionAssert.Success(await client.GetWeatherAsync(9, CancellationToken.None));

            Assert.Equal(19.0, Assert.Single(samples).AirTemperature);
            Assert.Single(handler.Requests);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
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

public sealed class OpenF1ResilienceTests
{
    [Fact]
    public void IsTransient_HandlesTransportAndServerFailuresOnly()
    {
        Assert.True(OpenF1Resilience.IsTransient(OutcomeOf(new HttpRequestException())));
        Assert.True(OpenF1Resilience.IsTransient(OutcomeOf(status: HttpStatusCode.ServiceUnavailable)));
        Assert.True(OpenF1Resilience.IsTransient(OutcomeOf(status: HttpStatusCode.InternalServerError)));

        // A throttle is not a fault: retrying into it turns a minute's penalty into a longer ban.
        Assert.False(OpenF1Resilience.IsTransient(OutcomeOf(status: HttpStatusCode.TooManyRequests)));
        Assert.False(OpenF1Resilience.IsTransient(OutcomeOf(status: HttpStatusCode.OK)));
        Assert.False(OpenF1Resilience.IsTransient(OutcomeOf(status: HttpStatusCode.NotFound)));
    }

    [Fact]
    public void Create_BuildsAPipelineWithoutThrowing()
    {
        Assert.NotNull(OpenF1Resilience.Create());
    }

    private static Polly.Outcome<HttpResponseMessage> OutcomeOf(Exception? exception = null, HttpStatusCode status = HttpStatusCode.OK) =>
        exception is not null
            ? Polly.Outcome.FromException<HttpResponseMessage>(exception)
            : Polly.Outcome.FromResult(new HttpResponseMessage(status));
}

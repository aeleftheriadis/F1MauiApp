using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using F1App.Domain;
using Polly;

namespace F1App.Telemetry.Providers;

/// <summary>
/// Read-only client for the OpenF1 REST API.
/// </summary>
/// <remarks>
/// Anonymous access, no key. The free tier serves historical data only (2023 onwards) at 30
/// requests per minute, which is why every call passes through a <see cref="RequestGate"/>. Live
/// sessions are not reachable from this client: no keyless endpoint publishes them.
/// </remarks>
public sealed class OpenF1Client
{
    private const string BaseAddress = "https://api.openf1.org/v1/";
    private readonly HttpClient _http;
    private readonly RequestGate _gate;
    private readonly ResiliencePipeline<HttpResponseMessage>? _resilience;
    private readonly SqlitePayloadCache? _cache;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a client over a shared <paramref name="http"/>; <paramref name="gate"/> holds the provider budget.</summary>
    /// <param name="http">Shared HTTP client.</param>
    /// <param name="gate">Pacing gate: the free tier is 30 requests per minute.</param>
    /// <param name="resilience">
    /// Retry and circuit-breaker pipeline. Optional so a unit test can leave it out and see the raw failure;
    /// the app always supplies one.
    /// </param>
    /// <param name="cache">Optional payload cache; the app supplies one so a restart costs nothing.</param>
    /// <param name="timeProvider">Clock used to age cached payloads.</param>
    public OpenF1Client(
        HttpClient http,
        RequestGate gate,
        ResiliencePipeline<HttpResponseMessage>? resilience = null,
        SqlitePayloadCache? cache = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
        _resilience = resilience;
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Sessions belonging to a meeting key.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Session>>> GetSessionsAsync(
        int meetingKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync(
            $"sessions?meeting_key={meetingKey}",
            OpenF1JsonContext.Default.ListOpenF1Session,
            cancellationToken);

    /// <summary>Every session published for a season, across all meetings.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Session>>> GetSeasonSessionsAsync(
        int year,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2023);
        return GetArrayAsync($"sessions?year={year}", OpenF1JsonContext.Default.ListOpenF1Session, cancellationToken);
    }

    /// <summary>Official classification of a session, when the FIA results have been published.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1SessionResult>>> GetSessionResultAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync(
            $"session_result?session_key={sessionKey}",
            OpenF1JsonContext.Default.ListOpenF1SessionResult,
            cancellationToken);

    /// <summary>Championship state per driver during a race (beta: race sessions only).</summary>
    public Task<FetchResult<ImmutableArray<OpenF1ChampionshipDriver>>> GetChampionshipDriversAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync(
            $"championship_drivers?session_key={sessionKey}",
            OpenF1JsonContext.Default.ListOpenF1ChampionshipDriver,
            cancellationToken);

    /// <summary>Passes recorded during a session. The provider warns the list may be incomplete.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Overtake>>> GetOvertakesAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync(
            $"overtakes?session_key={sessionKey}",
            OpenF1JsonContext.Default.ListOpenF1Overtake,
            cancellationToken);

    /// <summary>
    /// The starting grid, which the provider publishes against the <b>qualifying</b> session key.
    /// </summary>
    public Task<FetchResult<ImmutableArray<OpenF1StartingGrid>>> GetStartingGridAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync(
            $"starting_grid?session_key={sessionKey}",
            OpenF1JsonContext.Default.ListOpenF1StartingGrid,
            cancellationToken);

    /// <summary>Race-control messages for a session, oldest first.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1RaceControl>>> GetRaceControlAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync(
            $"race_control?session_key={sessionKey}",
            OpenF1JsonContext.Default.ListOpenF1RaceControl,
            cancellationToken);

    /// <summary>Track conditions for a session, one sample per minute, oldest first.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Weather>>> GetWeatherAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"weather?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1Weather, cancellationToken);

    /// <summary>Every meeting of a season, with the provider's circuit icon and country flag.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Meeting>>> GetMeetingsAsync(
        int year,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2023);
        return GetArrayAsync($"meetings?year={year}", OpenF1JsonContext.Default.ListOpenF1Meeting, cancellationToken);
    }

    /// <summary>Recorded GPS positions for a session window, optionally limited to one driver.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Location>>> GetLocationsAsync(
        int sessionKey,
        DateTimeOffset from,
        DateTimeOffset to,
        int? driverNumber,
        CancellationToken cancellationToken)
    {
        var url = new System.Text.StringBuilder("location?session_key=")
            .Append(sessionKey)
            .Append("&date>=").Append(Format(from))
            .Append("&date<").Append(Format(to));

        if (driverNumber is { } driver)
        {
            url.Append("&driver_number=").Append(driver);
        }

        return GetArrayAsync(url.ToString(), OpenF1JsonContext.Default.ListOpenF1Location, cancellationToken);
    }

    /// <summary>Recorded car telemetry (speed, gear, throttle, brake, DRS) for a session window.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1CarData>>> GetCarDataAsync(
        int sessionKey,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var url = new System.Text.StringBuilder("car_data?session_key=")
            .Append(sessionKey)
            .Append("&date>=").Append(Format(from))
            .Append("&date<").Append(Format(to))
            .ToString();

        return GetArrayAsync(url, OpenF1JsonContext.Default.ListOpenF1CarData, cancellationToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every lap of a session, with sector durations.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Lap>>> GetLapsAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"laps?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1Lap, cancellationToken);

    /// <summary>Classified positions over the session.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Position>>> GetPositionsAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"position?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1Position, cancellationToken);

    /// <summary>Gaps to the leader and to the car ahead.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Interval>>> GetIntervalsAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"intervals?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1Interval, cancellationToken);

    /// <summary>Tyre stints.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Stint>>> GetStintsAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"stints?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1Stint, cancellationToken);

    /// <summary>Pit stops.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1PitStop>>> GetPitStopsAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"pit?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1PitStop, cancellationToken);

    /// <summary>Driver numbers, acronyms and team colours for a session.</summary>
    public Task<FetchResult<ImmutableArray<OpenF1Driver>>> GetDriversAsync(
        int sessionKey,
        CancellationToken cancellationToken) =>
        GetArrayAsync($"drivers?session_key={sessionKey}", OpenF1JsonContext.Default.ListOpenF1Driver, cancellationToken);

    private async Task<FetchResult<ImmutableArray<TDto>>> GetArrayAsync<TDto>(
        string relativeUrl,
        JsonTypeInfo<List<TDto>> typeInfo,
        CancellationToken cancellationToken)
    {
        // Cache first: the free tier is thirty requests a minute, and a finished session's payloads cannot change.
        if (_cache is not null)
        {
            var cached = await _cache
                .ReadAsync(relativeUrl, _timeProvider.GetUtcNow(), SqlitePayloadCache.LifetimeFor(relativeUrl), cancellationToken)
                .ConfigureAwait(false);

            if (cached is not null)
            {
                var restored = Deserialize(cached, typeInfo);

                if (restored is not null)
                {
                    return ImmutableArray.CreateRange(restored);
                }

                // A body that no longer parses is dropped rather than served: the schema changed under it.
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(BaseAddress), relativeUrl));
        using var response = _resilience is null
            ? await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false)
            : await _resilience
                .ExecuteAsync(
                    token => new ValueTask<HttpResponseMessage>(
                        _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)),
                    cancellationToken)
                .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var backoff = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
            _gate.Penalize(backoff);
            return new RateLimited((int)backoff.TotalSeconds);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new HttpFailure((int)response.StatusCode, response.ReasonPhrase ?? "request failed");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            return ImmutableArray<TDto>.Empty;
        }

        var items = Deserialize(body, typeInfo);

        if (items is null)
        {
            return new HttpFailure(502, "provider returned a body that could not be parsed");
        }

        if (_cache is not null)
        {
            await _cache
                .WriteAsync(relativeUrl, body, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }

        return ImmutableArray.CreateRange(items);
    }

    private static List<TDto>? Deserialize<TDto>(string body, JsonTypeInfo<List<TDto>> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException)
        {
            // Caller decides: a cached body is dropped, a live one is reported as a provider failure.
            return null;
        }
    }
}

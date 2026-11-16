using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using F1App.Domain;

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

    /// <summary>Creates a client over a shared <paramref name="http"/>; <paramref name="gate"/> holds the provider budget.</summary>
    public OpenF1Client(HttpClient http, RequestGate gate)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(BaseAddress), relativeUrl));
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await JsonSerializer
            .DeserializeAsync(stream, typeInfo, cancellationToken)
            .ConfigureAwait(false);

        Debug.Assert(items is not null || response.Content.Headers.ContentLength == 0);
        return items is null ? ImmutableArray<TDto>.Empty : ImmutableArray.CreateRange(items);
    }
}

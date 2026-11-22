using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Facts;

/// <summary>
/// Reads one race in detail from Jolpica: classification, qualifying and pit stops.
/// </summary>
/// <remarks>
/// <para>
/// Three requests per race, which is why this is on demand rather than a bulk sync: the same three endpoints
/// for every race since 1950 would be roughly 7 400 requests. A race a user opens is kept by
/// <see cref="SqlitePayloadCache"/>, so opening it again costs nothing.
/// </para>
/// <para>
/// Qualifying is only published from 1994 and pit stops from 2012, so an empty section is a normal answer.
/// The caller must present that as "not published", never as "none happened".
/// </para>
/// </remarks>
public sealed class JolpicaRaceDetailClient
{
    private readonly HttpClient _http;
    private readonly RequestGate _gate;
    private readonly SqlitePayloadCache? _cache;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the client.</summary>
    public JolpicaRaceDetailClient(
        HttpClient http,
        RequestGate gate,
        SqlitePayloadCache? cache = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Loads a race's classification, qualifying and pit stops together.</summary>
    public async Task<FetchResult<RaceDetail>> GetAsync(
        int season,
        int round,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(season, 1950);
        ArgumentOutOfRangeException.ThrowIfLessThan(round, 1);

        var classification = await FetchAsync(
            season,
            round,
            "results",
            FactsJsonContext.Default.JolpicaRaceDetailResultsPayload,
            cancellationToken).ConfigureAwait(false);

        if (classification is not JolpicaRaceDetailResultsPayload payload
            || payload.MrData?.RaceTable?.Races is not { Count: > 0 } races)
        {
            return classification switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, $"no classification published for {season} round {round}"),
            };
        }

        var race = races[0];

        return new RaceDetail(
            Season: season,
            Round: round,
            RaceName: race.RaceName ?? "unknown",
            CircuitName: race.Circuit?.CircuitName ?? "unknown",
            Date: DateOnly.TryParse(race.Date, CultureInfo.InvariantCulture, out var date) ? date : default,
            Classification: (race.Results ?? []).Select(ToClassification).ToImmutableArray(),
            Qualifying: await GetQualifyingAsync(season, round, cancellationToken).ConfigureAwait(false),
            PitStops: await GetPitStopsAsync(season, round, cancellationToken).ConfigureAwait(false));
    }

    private async Task<ImmutableArray<RaceQualifyingRow>> GetQualifyingAsync(
        int season,
        int round,
        CancellationToken cancellationToken)
    {
        var payload = await FetchAsync(
            season,
            round,
            "qualifying",
            FactsJsonContext.Default.JolpicaRaceDetailQualifyingPayload,
            cancellationToken).ConfigureAwait(false);

        if (payload is not JolpicaRaceDetailQualifyingPayload qualifying
            || qualifying.MrData?.RaceTable?.Races is not { Count: > 0 } races
            || races[0].QualifyingResults is not { Count: > 0 } results)
        {
            return [];
        }

        return results
            .Select(result => new RaceQualifyingRow(
                Position: result.Position ?? "?",
                DriverName: Name(result.Driver),
                Q1: result.Q1 ?? string.Empty,
                Q2: result.Q2 ?? string.Empty,
                Q3: result.Q3 ?? string.Empty))
            .ToImmutableArray();
    }

    private async Task<ImmutableArray<RacePitStopRow>> GetPitStopsAsync(
        int season,
        int round,
        CancellationToken cancellationToken)
    {
        var payload = await FetchAsync(
            season,
            round,
            "pitstops",
            FactsJsonContext.Default.JolpicaRaceDetailPitStopsPayload,
            cancellationToken).ConfigureAwait(false);

        if (payload is not JolpicaRaceDetailPitStopsPayload stops
            || stops.MrData?.RaceTable?.Races is not { Count: > 0 } races
            || races[0].PitStops is not { Count: > 0 } rows)
        {
            return [];
        }

        return rows
            .Select(row => new RacePitStopRow(
                DriverName: row.DriverId ?? "unknown",
                Lap: ReadInt(row.Lap),
                StopNumber: ReadInt(row.Stop),
                DurationSeconds: double.TryParse(
                    row.Duration,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var seconds)
                    ? seconds
                    : null))
            .ToImmutableArray();
    }

    private static RaceClassificationRow ToClassification(JolpicaSeasonResult result) =>
        new(
            Position: result.PositionText ?? result.Position ?? "?",
            DriverName: Name(result.Driver),
            Constructor: result.Constructor?.Name ?? "unknown",
            Grid: ReadInt(result.Grid),
            Laps: ReadInt(result.Laps),
            Points: double.TryParse(result.Points, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
                ? points
                : 0d,
            Status: result.Status ?? string.Empty);

    private static string Name(JolpicaDriverRef? driver)
    {
        if (driver is null)
        {
            return "unknown";
        }

        var name = string.Join(
            ' ',
            new[] { driver.GivenName, driver.FamilyName }.Where(static part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(name) ? driver.DriverId ?? "unknown" : name;
    }

    private async Task<FetchResult<T>> FetchAsync<T>(
        int season,
        int round,
        string endpoint,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var relative = string.Create(CultureInfo.InvariantCulture, $"{season}/{round}/{endpoint}.json");
        var url = "https://api.jolpi.ca/ergast/f1/" + relative;

        if (_cache is not null)
        {
            var cached = await _cache
                .ReadAsync(
                    url,
                    _timeProvider.GetUtcNow(),
                    SqlitePayloadCache.LifetimeFor("races/past"),
                    cancellationToken)
                .ConfigureAwait(false);

            if (cached is not null && TryParse(cached, typeInfo) is { } restored)
            {
                return restored;
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

        if (string.IsNullOrWhiteSpace(body) || TryParse(body, typeInfo) is not { } parsed)
        {
            return new HttpFailure(502, "provider returned a body that could not be parsed");
        }

        if (_cache is not null)
        {
            await _cache.WriteAsync(url, body, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }

        return parsed;
    }

    private static T? TryParse<T>(string body, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}

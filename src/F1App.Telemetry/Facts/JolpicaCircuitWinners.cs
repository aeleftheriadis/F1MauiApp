using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Facts;

/// <summary>
/// Winner history for a circuit, joined from Jolpica.
/// </summary>
/// <remarks>
/// The two sources do not share circuit identifiers: OpenF1 calls Bahrain "Sakhir", Jolpica calls it
/// "bahrain". So the join is on the race date, which both publish - the season calendar maps a date to
/// Jolpica's circuit id, and from there one request returns every winner at that circuit.
/// </remarks>
public sealed class JolpicaCircuitWinners
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly RequestGate _gate;
    private readonly JolpicaClient _calendar;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _cacheGate = new();
    private readonly Dictionary<string, (CircuitWinners Winners, DateTimeOffset At)> _cache = [];
    private readonly SqliteFactsCache? _persisted;

    /// <summary>Creates the lookup.</summary>
    public JolpicaCircuitWinners(
        HttpClient http,
        RequestGate gate,
        JolpicaClient calendar,
        SqliteFactsCache? cache,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _http = http;
        _gate = gate;
        _calendar = calendar;
        _persisted = cache;
        _timeProvider = timeProvider;
    }

    /// <summary>Returns the winner history of the circuit that hosted the race on <paramref name="raceDate"/>.</summary>
    public async Task<FetchResult<CircuitWinners>> GetForRaceDateAsync(
        int season,
        DateTimeOffset raceDate,
        CancellationToken cancellationToken)
    {
        var calendar = await _calendar.GetSeasonAsync(season, cancellationToken).ConfigureAwait(false);

        if (calendar is not ImmutableArray<RaceWeekend> races || races.Length == 0)
        {
            return calendar switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, "no calendar for that season"),
            };
        }

        var race = races.FirstOrDefault(candidate => candidate.RaceStart.UtcDateTime.Date == raceDate.UtcDateTime.Date);

        if (race is null)
        {
            return new HttpFailure(204, "no calendar entry matches that race date");
        }

        return await GetAsync(race.Circuit.Id, race.Circuit.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the winner history of one Jolpica circuit id.</summary>
    public async Task<FetchResult<CircuitWinners>> GetAsync(
        string circuitId,
        string circuitName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        var now = _timeProvider.GetUtcNow();

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(circuitId, out var cached) && now - cached.At < CacheTtl)
            {
                return cached.Winners;
            }
        }

        if (_persisted is not null)
        {
            var persisted = await _persisted
                .ReadWinnersAsync(circuitId, now, CacheTtl, cancellationToken)
                .ConfigureAwait(false);

            if (!persisted.IsDefaultOrEmpty)
            {
                var restored = new CircuitWinners(circuitName, persisted.Length, persisted);
                lock (_cacheGate)
                {
                    _cache[circuitId] = (restored, now);
                }

                return restored;
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.jolpi.ca/ergast/f1/circuits/{circuitId}/results/1.json?limit=100");

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

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var payload = await JsonSerializer
            .DeserializeAsync(stream, CircuitWinnersJsonContext.Default.JolpicaCircuitResults, cancellationToken)
            .ConfigureAwait(false);

        var raceList = payload?.MrData?.RaceTable?.Races;
        if (raceList is null || raceList.Count == 0)
        {
            return new HttpFailure(204, "no winners published for that circuit");
        }

        var wins = new Dictionary<string, (int Count, int LastSeason)>(StringComparer.Ordinal);


        foreach (var race in raceList)
        {
            var winner = race.Results is { Count: > 0 } results ? results[0].Driver : null;
            if (winner is null)
            {
                continue;
            }

            var name = string.Join(
                ' ',
                new[] { winner.GivenName, winner.FamilyName }.Where(static part => !string.IsNullOrWhiteSpace(part)));

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var season = int.TryParse(race.Season, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

            var current = wins.GetValueOrDefault(name, (Count: 0, LastSeason: 0));
            wins[name] = (Count: current.Count + 1, LastSeason: Math.Max(current.LastSeason, season));
        }

        var winners = new CircuitWinners(
            circuitName,
            raceList.Count,
            [.. wins
                .OrderByDescending(static pair => pair.Value.Count)
                .ThenByDescending(static pair => pair.Value.LastSeason)
                .Select(static pair => new CircuitWinner(pair.Key, pair.Value.Count, pair.Value.LastSeason))]);

        lock (_cacheGate)
        {
            _cache[circuitId] = (winners, now);
        }

        if (_persisted is not null)
        {
            await _persisted
                .WriteWinnersAsync(circuitId, winners.Winners, now, cancellationToken)
                .ConfigureAwait(false);
        }

        return winners;
    }
}

internal sealed record JolpicaCircuitResults(
    [property: JsonPropertyName("MRData")] JolpicaCircuitResultsData? MrData);

internal sealed record JolpicaCircuitResultsData(
    [property: JsonPropertyName("RaceTable")] JolpicaCircuitRaceTable? RaceTable);

internal sealed record JolpicaCircuitRaceTable(
    [property: JsonPropertyName("Races")] List<JolpicaCircuitRace>? Races);

internal sealed record JolpicaCircuitRace(
    [property: JsonPropertyName("season")] string? Season,
    [property: JsonPropertyName("raceName")] string? RaceName,
    [property: JsonPropertyName("Results")] List<JolpicaRaceResult>? Results);

internal sealed record JolpicaRaceResult(
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("Driver")] JolpicaWinner? Driver);

internal sealed record JolpicaWinner(
    [property: JsonPropertyName("driverId")] string? DriverId,
    [property: JsonPropertyName("givenName")] string? GivenName,
    [property: JsonPropertyName("familyName")] string? FamilyName);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JolpicaCircuitResults))]
internal sealed partial class CircuitWinnersJsonContext : JsonSerializerContext;

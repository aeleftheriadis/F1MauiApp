using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using F1App.Domain;

namespace F1App.Telemetry.Facts;

/// <summary>
/// Reads every race of one season, with its winner, from Jolpica.
/// </summary>
/// <remarks>
/// <para>
/// One request per season: <c>/{season}/results/1.json</c> answers with the whole season's races and their
/// first-place results, so a full archive since 1950 costs about eighty requests rather than one per race.
/// That was measured against the live endpoint for 1950 and 2023, not assumed.
/// </para>
/// <para>
/// Like the champions client, a season that fails is reported per season: one flaky year must not blank
/// the vault. Calls go through the shared Jolpica gate; how many seasons run at once is the caller's choice.
/// </para>
/// </remarks>
public sealed class JolpicaRaceVaultClient
{
    private readonly HttpClient _http;
    private readonly RequestGate _gate;

    /// <summary>Creates the client.</summary>
    public JolpicaRaceVaultClient(HttpClient http, RequestGate gate)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
    }

    /// <summary>Every race of one season, oldest round first. A season with no races yet is a 204, not an error.</summary>
    public async Task<FetchResult<ImmutableArray<ArchivedRace>>> GetSeasonRacesAsync(
        int season,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(season, 1950);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var url = string.Create(CultureInfo.InvariantCulture, $"https://api.jolpi.ca/ergast/f1/{season}/results/1.json");

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
            .DeserializeAsync(stream, FactsJsonContext.Default.JolpicaRaceVaultPayload, cancellationToken)
            .ConfigureAwait(false);

        var races = payload?.MrData?.RaceTable?.Races;
        var builder = ImmutableArray.CreateBuilder<ArchivedRace>(races?.Count ?? 0);

        if (races is not null)
        {
            foreach (var race in races)
            {
                if (Parse(season, race) is { } archived)
                {
                    builder.Add(archived);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static ArchivedRace? Parse(int fallbackSeason, JolpicaVaultRace race)
    {
        var result = race.Results is { Count: > 0 } results ? results[0] : null;

        if (result is null)
        {
            // A published race always carries its winner; one without is calendar placeholder data.
            return null;
        }

        var season = ReadInt(race.Season) ?? fallbackSeason;

        // The Indianapolis 500 was championship-scoring 1950-1960, so Jolpica lists it among the season's
        // races, but it was not a Grand Prix; it is excluded here. 2000-2007 US GP stays: same circuit id.
        if (race.Circuit?.CircuitId == "indianapolis" && season is >= 1950 and <= 1960)
        {
            return null;
        }

        var date = DateOnly.TryParse(
            race.Date,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : default;

        var winner = string.Join(
            ' ',
            new[] { result.Driver?.GivenName, result.Driver?.FamilyName }
                .Where(static part => !string.IsNullOrWhiteSpace(part)));

        return new ArchivedRace(
            Season: season,
            Round: ReadInt(race.Round) ?? 0,
            RaceName: race.RaceName ?? "unknown",
            CircuitId: race.Circuit?.CircuitId ?? "unknown",
            CircuitName: race.Circuit?.CircuitName ?? "unknown",
            Country: race.Circuit?.Location?.Country ?? "unknown",
            Date: date,
            Winner: string.IsNullOrWhiteSpace(winner) ? result.Driver?.DriverId ?? "unknown" : winner,
            Constructor: result.Constructor?.Name ?? "unknown",
            Laps: ReadInt(result.Laps) ?? 0,
            WinningTime: result.Time?.Time ?? string.Empty);
    }

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}

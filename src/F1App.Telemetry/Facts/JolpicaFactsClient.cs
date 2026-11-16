using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using F1App.Domain;

namespace F1App.Telemetry.Facts;

/// <summary>
/// Reads season champions from Jolpica, one request per season.
/// </summary>
/// <remarks>
/// <para>
/// Jolpica mirrors Ergast, and Ergast does not support the cross-season <c>driverStandings/1</c> filter:
/// <c>/driverStandings/1.json</c> answers 400, while <c>/{season}/driverStandings/1.json</c> answers 200.
/// That was measured, not assumed - so a range costs one request per season, issued four at a time
/// through the shared gate.
/// </para>
/// <para>
/// A season that fails is skipped and reported by its absence: one flaky year must not blank a screen of
/// history.
/// </para>
/// </remarks>
public sealed class JolpicaFactsClient
{
    private const int MaxParallelSeasons = 4;

    private readonly HttpClient _http;
    private readonly RequestGate _gate;

    /// <summary>Creates the client.</summary>
    public JolpicaFactsClient(HttpClient http, RequestGate gate)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
    }

    /// <summary>Returns the champions of <paramref name="fromSeason"/>..<paramref name="toSeason"/>, oldest first.</summary>
    public async Task<FetchResult<ImmutableArray<SeasonChampion>>> GetChampionsAsync(
        int fromSeason,
        int toSeason,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fromSeason, 1950);
        ArgumentOutOfRangeException.ThrowIfLessThan(toSeason, fromSeason);

        var found = new System.Collections.Concurrent.ConcurrentBag<SeasonChampion>();
        string? firstFailure = null;

        await Parallel.ForEachAsync(
            Enumerable.Range(fromSeason, toSeason - fromSeason + 1),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelSeasons,
                CancellationToken = cancellationToken,
            },
            async (season, token) =>
            {
                var result = await GetSeasonAsync(season, token).ConfigureAwait(false);

                switch (result)
                {
                    case SeasonChampion champion:
                        found.Add(champion);
                        break;

                    case RateLimited limited:
                        // Backing off is the gate's job; remember the reason and let the rest finish.
                        Interlocked.CompareExchange(ref firstFailure, $"rate limited ({limited.RetryAfterSeconds}s)", null);
                        break;

                    case HttpFailure failure:
                        Interlocked.CompareExchange(ref firstFailure, $"season {season}: {failure.StatusCode}", null);
                        break;

                }
            }).ConfigureAwait(false);

        if (found.IsEmpty)
        {
            return new HttpFailure(204, firstFailure ?? "no champions found in that range");
        }

        return found.OrderBy(static champion => champion.Season).ToImmutableArray();
    }

    private async Task<FetchResult<SeasonChampion>> GetSeasonAsync(int season, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.jolpi.ca/ergast/f1/{season}/driverStandings/1.json");

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
            .DeserializeAsync(stream, FactsJsonContext.Default.JolpicaChampionPayload, cancellationToken)
            .ConfigureAwait(false);

        var list = payload?.MrData?.StandingsTable?.StandingsLists is { Count: > 0 } lists ? lists[0] : null;

        if (list?.DriverStandings is not { Count: > 0 } standings)
        {
            // A season with no champion yet (a future calendar) is not an error, just nothing to show.
            return new HttpFailure(204, $"no champion published for {season}");
        }

        var champion = standings[0];
        var driver = champion.Driver;
        var name = string.Join(
            ' ',
            new[] { driver?.GivenName, driver?.FamilyName }.Where(static part => !string.IsNullOrWhiteSpace(part)));

        return new SeasonChampion(
            Season: season,
            DriverName: string.IsNullOrWhiteSpace(name) ? driver?.DriverId ?? "unknown" : name,
            ConstructorName: champion.Constructors is { Count: > 0 } constructors
                ? constructors[0].Name ?? "unknown"
                : "unknown",
            Points: ReadDouble(champion.Points),
            Wins: (int)ReadDouble(champion.Wins));
    }

    private static double ReadDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
}

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using F1App.Domain;

namespace F1App.Telemetry.Facts;

/// <summary>
/// Reads a season's full race classifications from Jolpica, one page at a time.
/// </summary>
/// <remarks>
/// <para>
/// A season is one row per driver per race (~480 for a 24-race year). Jolpica caps <c>limit</c> at 100 -
/// asking for 500 or 1000 still returns 100, measured - so a season costs five paged requests. That is the
/// cheapest shape available: per-race results would cost 24, and the whole-season filter with
/// <c>/results/1.json</c> carries only the winner, which is what the archive uses.
/// </para>
/// <para>
/// A page that fails aborts the season with that failure rather than returning a partial classification:
/// a teammate duel counted over half a season would be a wrong number, not a smaller one.
/// </para>
/// </remarks>
public sealed class JolpicaSeasonResultsClient
{
    private const int PageSize = 100;
    private const int MaxPages = 40;

    private readonly HttpClient _http;
    private readonly RequestGate _gate;

    /// <summary>Creates the client.</summary>
    public JolpicaSeasonResultsClient(HttpClient http, RequestGate gate)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
    }

    /// <summary>Every classified result of <paramref name="season"/>, in round order.</summary>
    public async Task<FetchResult<ImmutableArray<RaceResultRow>>> GetSeasonResultsAsync(
        int season,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(season, 1950);

        var rows = ImmutableArray.CreateBuilder<RaceResultRow>(512);
        var seen = 0;
        var total = int.MaxValue;

        for (var page = 0; page < MaxPages && seen < total; page++)
        {
            var result = await GetPageAsync(season, page * PageSize, cancellationToken).ConfigureAwait(false);

            if (result is not PageResult payload)
            {
                return result switch
                {
                    HttpFailure failure => failure,
                    RateLimited limited => limited,
                    _ => new HttpFailure(204, $"no results published for {season}"),
                };
            }

            total = payload.Total;

            if (payload.Rows.Length == 0)
            {
                break;
            }

            rows.AddRange(payload.Rows);
            seen += payload.Rows.Length;
        }

        return rows.ToImmutable();
    }

    private async Task<FetchResult<PageResult>> GetPageAsync(
        int season,
        int offset,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"https://api.jolpi.ca/ergast/f1/{season}/results.json?limit={PageSize}&offset={offset}");

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
            .DeserializeAsync(stream, FactsJsonContext.Default.JolpicaSeasonResultsPayload, cancellationToken)
            .ConfigureAwait(false);

        var data = payload?.MrData;
        var races = data?.RaceTable?.Races ?? [];
        var rows = ImmutableArray.CreateBuilder<RaceResultRow>(PageSize);

        foreach (var race in races)
        {
            var round = ReadInt(race.Round);

            foreach (var result in race.Results ?? [])
            {
                if (result.Driver?.DriverId is not { Length: > 0 } driverId)
                {
                    continue;
                }

                rows.Add(new RaceResultRow(
                    Season: season,
                    Round: round ?? 0,
                    DriverId: driverId,
                    DriverCode: result.Driver.Code is { Length: > 0 } code
                        ? code
                        : driverId[..Math.Min(3, driverId.Length)].ToUpperInvariant(),
                    DriverName: string.Join(
                        ' ',
                        new[] { result.Driver.GivenName, result.Driver.FamilyName }
                            .Where(static part => !string.IsNullOrWhiteSpace(part))),
                    ConstructorId: result.Constructor?.ConstructorId ?? "unknown",
                    Position: ReadInt(result.Position),
                    PositionText: result.PositionText ?? string.Empty,
                    Grid: ReadInt(result.Grid),
                    Status: result.Status ?? string.Empty));
            }
        }

        return new PageResult(ReadInt(data?.Total) ?? 0, rows.ToImmutable());
    }

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private sealed record PageResult(int Total, ImmutableArray<RaceResultRow> Rows);
}

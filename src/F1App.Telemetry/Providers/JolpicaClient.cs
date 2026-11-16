using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using F1App.Domain;

namespace F1App.Telemetry.Providers;

/// <summary>
/// Client for the Jolpica-F1 API (the maintained Ergast successor), used for the season calendar.
/// </summary>
/// <remarks>
/// Historical only, no credentials, no live data. The whole season is one request, which keeps the
/// calendar well inside the free tier's budget.
/// </remarks>
public sealed class JolpicaClient
{
    private const string BaseAddress = "https://api.jolpi.ca/ergast/f1/";
    private readonly HttpClient _http;
    private readonly RequestGate _gate;

    /// <summary>Creates a client over a shared <paramref name="http"/>; <paramref name="gate"/> holds the provider budget.</summary>
    public JolpicaClient(HttpClient http, RequestGate gate)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);

        _http = http;
        _gate = gate;
    }

    /// <summary>Fetches and maps the race calendar for <paramref name="season"/>.</summary>
    public async Task<FetchResult<ImmutableArray<RaceWeekend>>> GetSeasonAsync(
        int season,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(season, 1950);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(BaseAddress), $"{season}.json?limit=30"));

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

        var payload = await JsonSerializer
            .DeserializeAsync(stream, JolpicaJsonContext.Default.JolpicaResponse, cancellationToken)
            .ConfigureAwait(false);

        var races = payload?.MrData?.RaceTable?.Races;
        if (races is null)
        {
            return new HttpFailure(204, "calendar payload contained no races");
        }

        var builder = ImmutableArray.CreateBuilder<RaceWeekend>(races.Count);
        foreach (var race in races)
        {
            var weekend = Map(season, race);
            if (weekend is not null)
            {
                builder.Add(weekend);
            }
        }

        return builder.ToImmutable();
    }

    private static RaceWeekend? Map(int season, JolpicaRace race)
    {
        if (!int.TryParse(race.Round, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round)
            || race.Circuit?.CircuitId is not { Length: > 0 } circuitId
            || race.Date is not { Length: > 0 } date)
        {
            return null;
        }

        var start = ParseStart(date, race.Time);
        if (start is null)
        {
            return null;
        }

        var circuit = new Circuit(
            circuitId,
            circuitId.ToUpperInvariant(),
            race.Circuit.CircuitName ?? circuitId,
            race.Circuit.Location?.Country ?? "Unknown");

        return new RaceWeekend(
            MeetingKey: (season * 1000) + round,
            Season: season,
            Round: round,
            Circuit: circuit,
            Name: race.RaceName ?? circuitId,
            RaceStart: start.Value);
    }

    private static DateTimeOffset? ParseStart(string date, string? time)
    {
        var iso = string.IsNullOrEmpty(time) ? $"{date}T00:00:00Z" : $"{date}T{time}";

        return DateTimeOffset.TryParse(
            iso,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}

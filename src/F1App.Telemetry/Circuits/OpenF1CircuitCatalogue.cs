using System.Collections.Immutable;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Circuits;

/// <summary>One race meeting of a season, with the session that can trace its circuit.</summary>
public sealed record CircuitEntry(
    Circuit Circuit,
    int MeetingKey,
    int RaceSessionKey,
    DateTimeOffset RaceStart,
    int SessionCount,
    bool HasRaceSession);

/// <summary>
/// Builds the season's circuit list from the published timetable.
/// </summary>
/// <remarks>
/// One request for the whole season. Circuits come from the race session of each meeting; a meeting
/// with no race session yet (a future calendar placeholder) still appears, pointing at its first
/// session. Pre-season testing duplicates its circuit, so entries are keyed by circuit and the race
/// meeting wins.
/// </remarks>
public sealed class OpenF1CircuitCatalogue
{
    private readonly OpenF1Client _client;

    /// <summary>Creates the catalogue.</summary>
    public OpenF1CircuitCatalogue(OpenF1Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>Returns every circuit with a published session in <paramref name="year"/>.</summary>
    public async Task<FetchResult<ImmutableArray<CircuitEntry>>> LoadSeasonAsync(
        int year,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2023);

        var rows = await _client.GetSeasonSessionsAsync(year, cancellationToken).ConfigureAwait(false);

        if (rows is not ImmutableArray<OpenF1Session> sessions || sessions.Length == 0)
        {
            // An empty season is a legitimate answer: the next calendar is published months ahead, and
            // the app must say "not announced" rather than look broken.
            return rows switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, $"no sessions published for {year}"),
            };
        }

        var builder = ImmutableArray.CreateBuilder<CircuitEntry>();

        foreach (var meeting in sessions
                     .Where(static session => session.DateStart is not null)
                     .GroupBy(static session => session.MeetingKey)
                     .OrderBy(static group => group.Min(static session => session.DateStart)))
        {
            var ordered = meeting.OrderBy(static session => session.DateStart).ToArray();
            var race = ordered.FirstOrDefault(static session => session.SessionName is "Race") ?? ordered[0];

            var circuit = Circuit.FromProvider(
                race.CircuitShortName ?? race.Location ?? "Unknown circuit",
                race.Location,
                race.CountryName);

            builder.Add(new CircuitEntry(
                circuit,
                meeting.Key,
                race.SessionKey,
                race.DateStart!.Value,
                ordered.Length,
                HasRaceSession: race.SessionName is "Race"));
        }

        // A testing meeting sits at the same circuit as its race, so key by circuit and keep the
        // meeting that actually ran the race, earliest first.
        return builder
            .GroupBy(static entry => entry.Circuit.Id)
            .Select(static group => group
                .OrderByDescending(static entry => entry.HasRaceSession)
                .ThenBy(static entry => entry.RaceStart)
                .First())
            .OrderBy(static entry => entry.RaceStart)
            .ToImmutableArray();
    }
}

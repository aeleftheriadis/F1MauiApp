using System.Collections.Immutable;
using System.Globalization;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Weekend;

/// <summary>
/// Picks the current-or-next meeting from the season timetable and turns it into a
/// <see cref="WeekendSchedule"/>.
/// </summary>
/// <remarks>
/// The meeting is chosen by comparing published session times with the clock, so a delayed schedule
/// shows the real next session instead of assuming a Sunday afternoon race. If the season has not
/// started, the meeting containing <c>now</c> wins; if the season is over, the last one does.
/// </remarks>
public sealed class OpenF1WeekendLoader
{
    private readonly OpenF1Client _client;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a loader.</summary>
    /// <param name="client">Anonymous OpenF1 client.</param>
    /// <param name="timeProvider">Clock used to choose the meeting.</param>
    public OpenF1WeekendLoader(OpenF1Client client, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _client = client;
        _timeProvider = timeProvider;
    }

    /// <summary>Loads the timetable of the meeting that is running now, or the next one.</summary>
    public async Task<FetchResult<WeekendSchedule>> LoadAsync(int year, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2023);

        var rows = await _client.GetSeasonSessionsAsync(year, cancellationToken).ConfigureAwait(false);

        if (rows is not ImmutableArray<OpenF1Session> sessions || sessions.Length == 0)
        {
            return rows switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, "no sessions published for that season"),
            };
        }

        var now = _timeProvider.GetUtcNow();

        var meetings = sessions
            .Where(static session => session.DateStart is not null && session.DateEnd is not null)
            .GroupBy(static session => session.MeetingKey)
            .Select(static group => group.OrderBy(static session => session.DateStart).ToArray())
            .OrderBy(static group => group[0].DateStart)
            .ToArray();

        if (meetings.Length == 0)
        {
            return new HttpFailure(204, "no meeting in that season has a published timetable");
        }

        var meeting = meetings.FirstOrDefault(group => group[0].DateStart <= now && group[^1].DateEnd >= now)
                      ?? meetings.FirstOrDefault(group => group[0].DateStart > now)
                      ?? meetings[^1];

        var head = meeting[0];
        var shortName = head.CircuitShortName ?? head.Location ?? "Unknown circuit";

        var circuit = Circuit.FromProvider(shortName, head.Location, head.CountryName);

        var slots = meeting
            .Select(static session => new SessionSlot(
                session.SessionKey,
                session.SessionName ?? session.SessionType ?? "Session",
                session.SessionType ?? session.SessionName ?? "Session",
                session.DateStart!.Value,
                session.DateEnd!.Value))
            .ToImmutableArray();

        return new WeekendSchedule(
            head.MeetingKey,
            string.Create(CultureInfo.InvariantCulture, $"{circuit.ShortName} {head.Year}"),
            circuit,
            slots);
    }

}

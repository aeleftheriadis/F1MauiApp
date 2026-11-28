using System.Collections.Immutable;
using F1App.Domain;
using F1App.Telemetry.Circuits;
using F1App.Telemetry.Weekend;

namespace F1App.Services;

/// <summary>
/// Chooses the session the app replays when the user has not picked one.
/// </summary>
/// <remarks>
/// The current weekend's most recently finished session, so the Timing tower and the Track markers show
/// the meeting the user is actually looking at. Falls back to the recorded Bahrain 2024 window when the
/// calendar cannot be read, which keeps the app demonstrable offline without inventing anything.
/// </remarks>
public sealed class DefaultSessionResolver
{
    private readonly OpenF1WeekendLoader _weekendLoader;
    private readonly OpenF1CircuitCatalogue _catalogue;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the resolver.</summary>
    public DefaultSessionResolver(
        OpenF1WeekendLoader weekendLoader,
        OpenF1CircuitCatalogue catalogue,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(weekendLoader);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _weekendLoader = weekendLoader;
        _catalogue = catalogue;
        _timeProvider = timeProvider;
    }

    /// <summary>Returns the session to replay, or <see langword="null"/> when nothing can be resolved.</summary>
    public async Task<SessionRequest?> ResolveAsync(CancellationToken cancellationToken)
    {
        var year = _timeProvider.GetUtcNow().Year;
        var loaded = await _weekendLoader.LoadAsync(year, cancellationToken).ConfigureAwait(false);

        if (loaded is not WeekendSchedule schedule || schedule.Sessions.IsEmpty)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var finished = schedule.Sessions.Where(session => session.End < now).ToArray();

        if (finished.Length > 0)
        {
            var latest = finished[^1];

            return new SessionRequest(
                latest.SessionKey,
                $"{schedule.Circuit.ShortName} {latest.Name}",
                latest.Start.AddMinutes(10),
                schedule.Circuit.Id,
                schedule.MeetingName);
        }

        // Race weekend has not started: replay the most recent completed race of the season instead of
        // showing an empty tower for three days.
        var season = await _catalogue.LoadSeasonAsync(year, cancellationToken).ConfigureAwait(false);

        if (season is not ImmutableArray<CircuitEntry> entries)
        {
            return null;
        }

        var completed = entries
            .Where(entry => entry.HasRaceSession && entry.RaceStart < now)
            .OrderBy(entry => entry.RaceStart)
            .ToArray();

        if (completed.Length == 0)
        {
            return null;
        }

        var mostRecent = completed[^1];

        return new SessionRequest(
            mostRecent.RaceSessionKey,
            $"{mostRecent.Circuit.ShortName} {mostRecent.RaceStart.Year} · Race",
            mostRecent.RaceStart.AddMinutes(10),
            mostRecent.Circuit.Id,
            mostRecent.Circuit.Name);
    }
}

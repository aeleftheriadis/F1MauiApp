using System.Collections.Immutable;
using F1App.Domain;

namespace F1App.Telemetry.Circuits;

/// <summary>A circuit on the season list, and whether the second provider agrees it exists.</summary>
public sealed record CalendarMatch(
    int MeetingKey,
    DateTimeOffset RaceDate,
    string CircuitShortName,
    bool MatchedToJolpica,
    int Round);

/// <summary>
/// Matches the two calendars on race date.
/// </summary>
/// <remarks>
/// The sources disagree, and the disagreement is measured rather than assumed: for 2026 OpenF1 lists 25
/// race sessions and Jolpica 23, with the two Jolpica lacks being 2026-04-12 and 2026-04-19. Jolpica is the
/// incomplete one here, so OpenF1 owns the list and Jolpica supplies the round number where it agrees.
/// Names cannot be used to join: OpenF1 says "Sakhir" where Jolpica says "bahrain".
/// </remarks>
public static class CalendarReconciler
{
    /// <summary>Joins the circuits on race date, keeping every OpenF1 meeting.</summary>
    public static ImmutableArray<CalendarMatch> Reconcile(
        ImmutableArray<CircuitEntry> openF1,
        ImmutableArray<RaceWeekend> jolpica)
    {
        var byDate = new Dictionary<DateOnly, RaceWeekend>();

        foreach (var race in jolpica)
        {
            byDate.TryAdd(DateOnly.FromDateTime(race.RaceStart.UtcDateTime), race);
        }

        var builder = ImmutableArray.CreateBuilder<CalendarMatch>(openF1.Length);

        foreach (var entry in openF1)
        {
            var date = DateOnly.FromDateTime(entry.RaceStart.UtcDateTime);

            builder.Add(byDate.TryGetValue(date, out var race)
                ? new CalendarMatch(entry.MeetingKey, entry.RaceStart, entry.Circuit.ShortName, true, race.Round)
                : new CalendarMatch(entry.MeetingKey, entry.RaceStart, entry.Circuit.ShortName, false, 0));
        }

        return builder.ToImmutable();
    }
}

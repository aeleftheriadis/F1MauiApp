using System.Collections.Immutable;
using F1App.Domain;
using F1App.Telemetry.Circuits;

namespace F1App.Telemetry.Tests;

public sealed class CalendarReconcilerTests
{
    private static CircuitEntry Entry(int meetingKey, string circuit, DateTimeOffset date) => new(
        Circuit.FromProvider(circuit, circuit, "Nowhere"),
        meetingKey,
        meetingKey * 10,
        date,
        5,
        HasRaceSession: true);

    private static RaceWeekend Race(int round, string circuitId, DateTimeOffset date) => new(
        round * 100,
        2026,
        round,
        new Circuit(circuitId, circuitId, circuitId, "Nowhere"),
        $"{circuitId} Grand Prix",
        date);

    [Fact]
    public void Reconcile_MarksMeetingsTheOtherCalendarAgreesOn()
    {
        var openF1 = ImmutableArray.Create(
            Entry(1, "Albert Park", new DateTimeOffset(2026, 3, 8, 4, 0, 0, TimeSpan.Zero)),
            Entry(2, "Sakhir", new DateTimeOffset(2026, 4, 12, 15, 0, 0, TimeSpan.Zero)));

        var jolpica = ImmutableArray.Create(
            Race(1, "albert_park", new DateTimeOffset(2026, 3, 8, 4, 0, 0, TimeSpan.Zero)));

        var matches = CalendarReconciler.Reconcile(openF1, jolpica);

        Assert.Equal(2, matches.Length);
        Assert.True(matches[0].MatchedToJolpica);
        Assert.Equal(1, matches[0].Round);

        // Measured fact: OpenF1 lists 2026-04-12 and Jolpica does not.
        Assert.False(matches[1].MatchedToJolpica);
        Assert.Equal(0, matches[1].Round);
    }

    [Fact]
    public void Reconcile_MatchesOnDateNotOnName()
    {
        var openF1 = ImmutableArray.Create(
            Entry(1, "Sakhir", new DateTimeOffset(2026, 4, 12, 15, 0, 0, TimeSpan.Zero)));

        // Jolpica calls the same circuit "bahrain"; only the date joins them.
        var jolpica = ImmutableArray.Create(
            Race(4, "bahrain", new DateTimeOffset(2026, 4, 12, 15, 0, 0, TimeSpan.Zero)));

        var match = Assert.Single(CalendarReconciler.Reconcile(openF1, jolpica));

        Assert.True(match.MatchedToJolpica);
        Assert.Equal(4, match.Round);
    }

    [Fact]
    public void Reconcile_KeepsEveryOpenF1MeetingWhenTheOtherCalendarIsEmpty()
    {
        var openF1 = ImmutableArray.Create(
            Entry(1, "Sakhir", new DateTimeOffset(2026, 4, 12, 15, 0, 0, TimeSpan.Zero)));

        var matches = CalendarReconciler.Reconcile(openF1, []);

        Assert.Single(matches);
        Assert.False(matches[0].MatchedToJolpica);
    }
}

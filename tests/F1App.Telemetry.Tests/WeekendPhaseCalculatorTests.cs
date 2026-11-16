using F1App.Domain;

namespace F1App.Telemetry.Tests;

public sealed class WeekendPhaseCalculatorTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 3, 6, 9, 0, 0, TimeSpan.Zero);

    private static WeekendSchedule Schedule() => new(
        MeetingKey: 1300,
        MeetingName: "Test Circuit 2026",
        Circuit: new Circuit("test", "Test", "Test", "Nowhere"),
        Sessions:
        [
            new SessionSlot(1, "Practice 1", "Practice", Day1, Day1.AddHours(1)),
            new SessionSlot(2, "Qualifying", "Qualifying", Day1.AddDays(1), Day1.AddDays(1).AddHours(1)),
            new SessionSlot(3, "Race", "Race", Day1.AddDays(2), Day1.AddDays(2).AddHours(2)),
        ]);

    [Fact]
    public void Evaluate_LongBeforeTheWeekend_IsPreWeekend()
    {
        var phase = WeekendPhaseCalculator.Evaluate(Schedule(), Day1.AddDays(-3));

        var pre = Assert.IsType<PreWeekend>(phase);
        Assert.Equal(TimeSpan.FromDays(3), pre.UntilFirstSession);
    }

    [Fact]
    public void Evaluate_OnSessionDay_IsSessionDayWithTheNextSlot()
    {
        var phase = WeekendPhaseCalculator.Evaluate(Schedule(), Day1.AddHours(1.5));

        var day = Assert.IsType<SessionDay>(phase);
        Assert.Equal("Qualifying", day.Next.Name);
        Assert.Equal(TimeSpan.FromHours(22.5), day.UntilNext);
    }

    [Fact]
    public void Evaluate_DuringASession_IsInSession()
    {
        var phase = WeekendPhaseCalculator.Evaluate(Schedule(), Day1.AddDays(2).AddMinutes(20));

        var running = Assert.IsType<InSession>(phase);
        Assert.Equal("Race", running.Session.Name);
    }

    [Fact]
    public void Evaluate_JustAfterTheLastSession_IsResultsPending()
    {
        var phase = WeekendPhaseCalculator.Evaluate(Schedule(), Day1.AddDays(2).AddHours(2).AddMinutes(5));

        var pending = Assert.IsType<ResultsPending>(phase);
        Assert.Equal("Race", pending.Session.Name);
        Assert.Equal(Day1.AddDays(2).AddHours(2).AddMinutes(30), pending.ExpectedBy);
    }

    [Fact]
    public void Evaluate_WellAfterTheLastSession_IsPostWeekend()
    {
        var phase = WeekendPhaseCalculator.Evaluate(Schedule(), Day1.AddDays(4));

        Assert.IsType<PostWeekend>(phase);
    }

    [Fact]
    public void Evaluate_ExactBoundaries_AreInclusiveOfStartAndExclusiveOfEnd()
    {
        Assert.IsType<InSession>(WeekendPhaseCalculator.Evaluate(Schedule(), Day1));
        Assert.IsType<SessionDay>(WeekendPhaseCalculator.Evaluate(Schedule(), Day1.AddHours(1)));
    }

    [Fact]
    public void Evaluate_AcceptsAnExplicitPublicationDelay()
    {
        var phase = WeekendPhaseCalculator.Evaluate(
            Schedule(),
            Day1.AddDays(2).AddHours(2).AddMinutes(10),
            resultsDelay: TimeSpan.FromMinutes(5));

        Assert.IsType<PostWeekend>(phase);
    }

    [Fact]
    public void Evaluate_MeetingWithoutSessions_DoesNotThrow()
    {
        var empty = new WeekendSchedule(1, "Empty", new Circuit("x", "X", "X", "X"), []);

        Assert.IsType<PostWeekend>(WeekendPhaseCalculator.Evaluate(empty, Day1));
    }
}

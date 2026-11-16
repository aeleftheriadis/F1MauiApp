using F1App.Domain;

namespace F1App.Telemetry.Tests;

public sealed class SessionPhaseCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2024, 3, 2, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(2);

    [Fact]
    public void Evaluate_BeforeStart_IsNotStarted()
    {
        Assert.IsType<NotStarted>(SessionPhaseCalculator.Evaluate(Start.AddMinutes(-1), Start, End));
    }

    [Fact]
    public void Evaluate_InsideWindow_IsLiveWithScheduledStart()
    {
        var phase = SessionPhaseCalculator.Evaluate(Start.AddMinutes(30), Start, End);

        var live = Assert.IsType<Live>(phase);
        Assert.Equal(Start, live.StartedAt);
    }

    [Fact]
    public void Evaluate_AfterEnd_IsFinishedWithScheduledEnd()
    {
        var phase = SessionPhaseCalculator.Evaluate(End.AddSeconds(1), Start, End);

        var finished = Assert.IsType<Finished>(phase);
        Assert.Equal(End, finished.EndedAt);
    }

    [Fact]
    public void Evaluate_AtExactStart_IsLiveAndAtExactEnd_IsFinished()
    {
        Assert.IsType<Live>(SessionPhaseCalculator.Evaluate(Start, Start, End));
        Assert.IsType<Finished>(SessionPhaseCalculator.Evaluate(End, Start, End));
    }

    [Fact]
    public void Evaluate_RejectsInvertedWindow()
    {
        Assert.Throws<ArgumentException>(() => SessionPhaseCalculator.Evaluate(Start, End, Start));
    }

    [Fact]
    public void SessionPhase_IsExhaustiveWithoutADiscardArm()
    {
        // Compiles only because SessionPhase is a closed hierarchy: adding a phase breaks here,
        // which is exactly the point.
        static string Describe(SessionPhase phase) => phase switch
        {
            NotStarted => "pre",
            Live => "live",
            Finished => "post",
        };

        Assert.Equal("pre", Describe(new NotStarted()));
        Assert.Equal("live", Describe(new Live(Start)));
        Assert.Equal("post", Describe(new Finished(End)));
    }
}

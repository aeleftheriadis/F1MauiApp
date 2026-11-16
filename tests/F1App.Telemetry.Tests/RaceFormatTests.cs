using F1App.Telemetry.Timing;

namespace F1App.Telemetry.Tests;

public sealed class RaceFormatTests
{
    [Fact]
    public void Lap_UsesMinutesAndMillisecondsAboveAMinute()
    {
        Assert.Equal("1:34.567", RaceFormat.Lap(TimeSpan.FromMilliseconds(94_567)));
        Assert.Equal("2:00.000", RaceFormat.Lap(TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Lap_UsesSecondsBelowAMinute()
    {
        Assert.Equal("35.450", RaceFormat.Lap(TimeSpan.FromMilliseconds(35_450)));
    }

    [Fact]
    public void Lap_UsesAnEmDashForMissingData()
    {
        Assert.Equal("—", RaceFormat.Lap(null));
        Assert.Equal("—", RaceFormat.Sector(null));
        Assert.Equal("—", RaceFormat.Speed(null));
        Assert.Equal("—", RaceFormat.Gap(null));
    }

    [Fact]
    public void Sector_AlwaysUsesThreeDecimals()
    {
        Assert.Equal("29.741", RaceFormat.Sector(TimeSpan.FromMilliseconds(29_741)));
        Assert.Equal("9.500", RaceFormat.Sector(TimeSpan.FromMilliseconds(9_500)));
    }

    [Fact]
    public void Gap_DistinguishesTheLeaderFromEveryoneElse()
    {
        Assert.Equal("LEADER", RaceFormat.Gap(0));
        // Anything at or below half a millisecond is the leader, not a gap.
        Assert.Equal("LEADER", RaceFormat.Gap(0.0004));
        Assert.Equal("LEADER", RaceFormat.Gap(0.0005));
        Assert.Equal("+0.001", RaceFormat.Gap(0.001));
        Assert.Equal("+22.457", RaceFormat.Gap(22.457));
    }

    [Fact]
    public void Tyre_JoinsCompoundAndAge()
    {
        Assert.Equal("SOFT 20", RaceFormat.Tyre("SOFT", 20));
    }
}

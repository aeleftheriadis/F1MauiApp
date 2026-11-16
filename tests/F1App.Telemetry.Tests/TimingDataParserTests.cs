using F1App.Telemetry.LiveFeed;

namespace F1App.Telemetry.Tests;

public sealed class TimingDataParserTests
{
    // Shape per the community-documented feed; deliberately exercised with mixed value kinds because
    // the hub has never been observed sending these frames from this project.
    private const string Frame = """
        {
          "TimingData": {
            "SessionStatus": "Started",
            "Lines": {
              "44": { "Position": "1", "NumberOfLaps": 12, "LastLapTime": { "Value": "1:34.567" } },
              "63": { "Position": 2, "NumberOfLaps": "11", "LastLapTime": { "Value": "1:35.001" } },
              "16": { "Position": "3" },
              "99": { "NumberOfLaps": 3 }
            }
          }
        }
        """;

    [Fact]
    public void Parse_ReadsStandingsInPositionOrder()
    {
        var standings = TimingDataParser.Parse(Frame);

        // Car 99 has no position and is skipped; the rest are ordered by position.
        Assert.Equal(3, standings.Length);
        Assert.Equal(44, standings[0].DriverNumber);
        Assert.Equal(1, standings[0].Position);
        Assert.Equal(12, standings[0].Laps);
        Assert.Equal("1:34.567", standings[0].LastLap);

        // Numbers may arrive as strings and vice versa.
        Assert.Equal(2, standings[1].Position);
        Assert.Equal(11, standings[1].Laps);
        Assert.Equal(16, standings[2].DriverNumber);
        Assert.Equal(0, standings[2].Laps);
        Assert.Null(standings[2].LastLap);
    }

    [Fact]
    public void Parse_ReturnsEmptyForFramesWithoutTimingData()
    {
        Assert.Empty(TimingDataParser.Parse("""{"SessionStatus":"Started"}"""));
        Assert.Empty(TimingDataParser.Parse("""{"TimingData":{"Lines":[]}}"""));
        Assert.Empty(TimingDataParser.Parse("{}"));
    }

    [Fact]
    public void Parse_ReturnsEmptyForMalformedJson()
    {
        Assert.Empty(TimingDataParser.Parse("{not json"));
        Assert.Empty(TimingDataParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_IgnoresNonNumericLineKeys()
    {
        const string Drs = """
            {"TimingData":{"Lines":{"44":{"Position":"1"},"SessionStatus":"Started"}}}
            """;

        var standings = TimingDataParser.Parse(Drs);

        Assert.Single(standings);
        Assert.Equal(44, standings[0].DriverNumber);
    }
}

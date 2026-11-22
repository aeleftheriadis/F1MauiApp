using System.Collections.Immutable;

namespace F1App.Domain;

/// <summary>A driver who won a season, with the numbers behind it.</summary>
public sealed record SeasonChampion(
    int Season,
    string DriverName,
    string ConstructorName,
    double Points,
    int Wins);

/// <summary>How often a driver has won at one circuit, and when they last did.</summary>
public sealed record CircuitWinner(string DriverName, int Wins, int LastSeason);

/// <summary>Winners at one circuit, most successful first.</summary>
public sealed record CircuitWinners(string CircuitName, int Races, ImmutableArray<CircuitWinner> Winners);

/// <summary>One world-championship race in the archive, with its winner.</summary>
public sealed record ArchivedRace(
    int Season,
    int Round,
    string RaceName,
    string CircuitId,
    string CircuitName,
    string Country,
    DateOnly Date,
    string Winner,
    string Constructor,
    int Laps,
    string WinningTime);

/// <summary>All-time wins for one driver or constructor, and when they happened.</summary>
public sealed record WinRecord(string Name, int Wins, int FirstSeason, int LastSeason);

/// <summary>Record book computed from the archived races.</summary>
public sealed record VaultRecords(
    int Races,
    int Seasons,
    ImmutableArray<WinRecord> DriverWins,
    ImmutableArray<WinRecord> ConstructorWins);

/// <summary>One driver's classification in one race, as Jolpica publishes it.</summary>
public sealed record RaceResultRow(
    int Season,
    int Round,
    string DriverId,
    string DriverCode,
    string DriverName,
    string ConstructorId,
    int? Position,
    string PositionText,
    int? Grid,
    string Status);

/// <summary>How two teammates compared over one season, race by race.</summary>
/// <param name="DriverId">The driver the duel is told from.</param>
/// <param name="DriverName">That driver's name, for display.</param>
/// <param name="TeammateId">The other car in the same team.</param>
/// <param name="TeammateName">The teammate's name, for display.</param>
/// <param name="DriverCode">Three-letter code, the identifier both sources agree on.</param>
/// <param name="TeammateCode">The teammate's three-letter code.</param>
/// <param name="RaceWins">Races where <paramref name="DriverName"/> was classified ahead of the teammate.</param>
/// <param name="TeammateWins">Races where the teammate was classified ahead.</param>
/// <param name="AveragePositionDelta">
/// Mean of (teammate position - driver position). Positive means the driver finished ahead on average.
/// </param>
/// <param name="QualifyingWins">Races where the driver started ahead of the teammate on the grid.</param>
/// <param name="QualifyingLosses">Races where the teammate started ahead.</param>
/// <param name="Compared">Races where both drivers took part and were classified.</param>
public sealed record TeammateDuel(
    string DriverId,
    string DriverName,
    string TeammateId,
    string TeammateName,
    string DriverCode,
    string TeammateCode,
    int RaceWins,
    int TeammateWins,
    double AveragePositionDelta,
    int QualifyingWins,
    int QualifyingLosses,
    int Compared);

/// <summary>Track conditions reduced to what a timing screen shows.</summary>
/// <param name="AirCelsius">Air temperature, when published.</param>
/// <param name="TrackCelsius">Track temperature, when published.</param>
/// <param name="Humidity">Relative humidity percentage, when published.</param>
/// <param name="Raining">True when the sample reports rainfall.</param>
/// <param name="WindMetresPerSecond">Wind speed, when published.</param>
public sealed record TrackConditions(
    double? AirCelsius,
    double? TrackCelsius,
    double? Humidity,
    bool Raining,
    double? WindMetresPerSecond)
{
    /// <summary>"air 27.8 °C · track 52.5 °C · 58% humidity · dry" - unknown fields are left out, never faked.</summary>
    public string Describe(System.Globalization.CultureInfo? culture = null)
    {
        var parts = new List<string>(5);

        if (AirCelsius is { } air)
        {
            parts.Add($"air {air.ToString("0.0", culture ?? System.Globalization.CultureInfo.InvariantCulture)} °C");
        }

        if (TrackCelsius is { } track)
        {
            parts.Add($"track {track.ToString("0.0", culture ?? System.Globalization.CultureInfo.InvariantCulture)} °C");
        }

        if (Humidity is { } humidity)
        {
            parts.Add($"{humidity.ToString("0", culture ?? System.Globalization.CultureInfo.InvariantCulture)}% humidity");
        }

        if (WindMetresPerSecond is { } wind)
        {
            parts.Add($"wind {wind.ToString("0.0", culture ?? System.Globalization.CultureInfo.InvariantCulture)} m/s");
        }

        parts.Add(Raining ? "rain" : "dry");
        return string.Join(" · ", parts);
    }
}

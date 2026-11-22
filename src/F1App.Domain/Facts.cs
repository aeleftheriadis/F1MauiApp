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

/// <summary>A race-control message reduced to what a pit wall shows.</summary>
/// <param name="Category">SessionStatus, Flag, SafetyCar, CarEvent, Drs...</param>
/// <param name="Flag">GREEN, YELLOW, DOUBLE YELLOW, CHEQUERED, BLACK AND WHITE...</param>
/// <param name="Message">The provider's own wording.</param>
/// <param name="LapNumber">Lap the event belongs to, when the session has laps.</param>
public sealed record RaceControlEvent(string Category, string Flag, string Message, int? LapNumber)
{
    /// <summary>
    /// "lap 34 · SAFETY CAR · SAFETY CAR DEPLOYED" - the message is kept verbatim, never reworded.
    /// </summary>
    /// <param name="maxMessageLength">Trims long messages so one event cannot fill the strip.</param>
    public string Describe(int maxMessageLength = 48)
    {
        var text = Message.Trim();

        if (text.Length > maxMessageLength)
        {
            text = string.Concat(text.AsSpan(0, maxMessageLength - 1), "…");
        }

        var kind = string.IsNullOrWhiteSpace(Flag) ? Category : Flag;
        var lap = LapNumber is { } number
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"lap {number} · ")
            : string.Empty;

        return string.IsNullOrWhiteSpace(kind) ? lap + text : $"{lap}{kind} · {text}";
    }
}

/// <summary>
/// Places gained or lost between the grid and the flag.
/// </summary>
/// <param name="GridPosition">Where the car started.</param>
/// <param name="FinishPosition">Where the car was classified.</param>
public readonly record struct PlacesGained(int GridPosition, int FinishPosition)
{
    /// <summary>Positive means places gained, negative means lost, 0 means held station.</summary>
    public int Delta => GridPosition - FinishPosition;

    /// <summary>"+2" for a gain, "-3" for a loss, "0" for no change.</summary>
    public string Describe() =>
        Delta switch
        {
            > 0 => $"+{Delta}",
            < 0 => Delta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "0",
        };
}

/// <summary>How many passes a driver made and suffered in one session.</summary>
/// <param name="Made">Passes this driver completed.</param>
/// <param name="Suffered">Times this driver was passed.</param>
public readonly record struct OvertakeTally(int Made, int Suffered)
{
    /// <summary>"4 made, 2 lost" - or empty when the driver was not involved at all.</summary>
    public string Describe() =>
        Made == 0 && Suffered == 0
            ? string.Empty
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{Made} made, {Suffered} lost");
}

/// <summary>A driver's championship standing before and after one race.</summary>
/// <param name="PositionStart">Position in the title before the race.</param>
/// <param name="PositionNow">Position after it.</param>
/// <param name="PointsStart">Points before the race.</param>
/// <param name="PointsNow">Points after it.</param>
public readonly record struct ChampionshipSwing(
    int? PositionStart,
    int? PositionNow,
    double? PointsStart,
    double? PointsNow)
{
    /// <summary>Points scored in this race, when both figures are published.</summary>
    public double? PointsGained => PointsNow is { } now && PointsStart is { } before ? now - before : null;

    /// <summary>Places gained in the title (positive) or lost (negative), when both are published.</summary>
    public int? PositionsMoved =>
        PositionNow is { } now && PositionStart is { } before ? before - now : null;

    /// <summary>
    /// "title P1 · 402 pts · +25 this race · 1 place gained" - each clause appears only when the provider
    /// published the two figures it compares.
    /// </summary>
    public string Describe()
    {
        if (PointsNow is null && PositionNow is null)
        {
            return string.Empty;
        }

        var parts = new System.Collections.Generic.List<string>(4);

        if (PositionNow is { } position)
        {
            parts.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"title P{position}"));
        }

        if (PointsNow is { } points)
        {
            parts.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{points:0.#} pts"));
        }

        if (PointsGained is { } gained and not 0)
        {
            parts.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{gained:+0.#;-0.#} this race"));
        }

        if (PositionsMoved is { } moved and not 0)
        {
            parts.Add(moved > 0
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{moved} place{(moved == 1 ? string.Empty : "s")} gained")
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{-moved} place{(moved == -1 ? string.Empty : "s")} lost"));
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>One car in a race's final classification.</summary>
/// <param name="Position">Classified position text ("1", "R", "DSQ").</param>
/// <param name="DriverName">Driver, as published.</param>
/// <param name="Constructor">Team.</param>
/// <param name="Grid">Starting slot, when published.</param>
/// <param name="Laps">Laps completed.</param>
/// <param name="Points">Championship points.</param>
/// <param name="Status">"Finished", "+1 Lap", "Collision", ...</param>
public sealed record RaceClassificationRow(
    string Position,
    string DriverName,
    string Constructor,
    int? Grid,
    int? Laps,
    double Points,
    string Status);

/// <summary>One car's qualifying session.</summary>
/// <param name="Position">Grid slot earned.</param>
/// <param name="DriverName">Driver, as published.</param>
/// <param name="Q1">Best time in Q1, empty when the driver did not take part.</param>
/// <param name="Q2">Best time in Q2.</param>
/// <param name="Q3">Best time in Q3.</param>
public sealed record RaceQualifyingRow(
    string Position,
    string DriverName,
    string Q1,
    string Q2,
    string Q3);

/// <summary>One pit stop in a race.</summary>
/// <param name="DriverName">Driver, as published.</param>
/// <param name="Lap">Lap the stop happened on.</param>
/// <param name="StopNumber">Which stop of that driver's race.</param>
/// <param name="DurationSeconds">Stationary time, when published.</param>
public sealed record RacePitStopRow(string DriverName, int? Lap, int? StopNumber, double? DurationSeconds);

/// <summary>Everything the archive can show about one race: classification, qualifying and pit stops.</summary>
public sealed record RaceDetail(
    int Season,
    int Round,
    string RaceName,
    string CircuitName,
    DateOnly Date,
    ImmutableArray<RaceClassificationRow> Classification,
    ImmutableArray<RaceQualifyingRow> Qualifying,
    ImmutableArray<RacePitStopRow> PitStops);

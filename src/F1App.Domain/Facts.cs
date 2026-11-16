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

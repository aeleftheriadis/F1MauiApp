using System.Collections.Immutable;

namespace F1App.Domain;

/// <summary>One run on one set of tyres.</summary>
public sealed record DriverStint(int LapStart, int LapEnd, string Compound, int TyreAgeAtStart);

/// <summary>A driver's tyre strategy for a session.</summary>
public sealed record DriverStrategy(int DriverNumber, string Code, ImmutableArray<DriverStint> Stints);

/// <summary>Every driver's strategy for one session.</summary>
public sealed record StrategyTimeline(
    int SessionKey,
    string SessionName,
    int LastLap,
    ImmutableArray<DriverStrategy> Drivers);

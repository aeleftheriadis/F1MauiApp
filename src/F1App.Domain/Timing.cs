using System.Collections.Immutable;

namespace F1App.Domain;

/// <summary>One driver's summary for a completed session.</summary>
public sealed record DriverTiming(
    int DriverNumber,
    string Code,
    string TeamColourHex,
    int Position,
    TimeSpan? BestLap,
    TimeSpan? BestSector1,
    TimeSpan? BestSector2,
    TimeSpan? BestSector3,
    double? GapToLeaderSeconds,
    string Compound,
    int TyreAgeLaps,
    int PitStops);

/// <summary>A session's timing table, ordered by classification.</summary>
public sealed record SessionTiming(
    int SessionKey,
    string SessionName,
    DateTimeOffset Start,
    ImmutableArray<DriverTiming> Entries);

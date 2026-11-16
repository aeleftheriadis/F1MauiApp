using System.Collections.Immutable;
using F1App.Domain;

namespace F1App.Telemetry.Replay;

/// <summary>One recorded sample for one car: where it was, and what it was doing.</summary>
public readonly record struct ReplaySample(
    DateTimeOffset Time,
    int DriverNumber,
    float X,
    float Y,
    short SpeedKph,
    byte Gear,
    byte ThrottlePercent,
    bool Braking,
    DrsState Drs);

/// <summary>A recorded session window, ready to replay.</summary>
public sealed record ReplaySession(
    int SessionKey,
    string Label,
    DateTimeOffset From,
    DateTimeOffset To,
    TrackOutline Outline,
    ImmutableArray<ReplaySample> Samples,
    ImmutableArray<Driver> Drivers)
{
    /// <summary>Identity for a car number, or a synthetic one when the session published no driver list.</summary>
    public Driver DriverFor(int driverNumber) =>
        Drivers.FirstOrDefault(driver => driver.Number == driverNumber)
        ?? new Driver(driverNumber, $"CAR {driverNumber}", $"Car {driverNumber}", "Unknown", "#94A3B8");
}

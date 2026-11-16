namespace F1App.Domain;

/// <summary>DRS actuator state as published by the timing feed.</summary>
public enum DrsState : byte
{
    /// <summary>No DRS information for this sample.</summary>
    Unknown = 0,

    /// <summary>DRS not available at this point of the lap.</summary>
    Unavailable = 1,

    /// <summary>DRS available but not deployed.</summary>
    Available = 8,

    /// <summary>DRS deployed.</summary>
    Active = 10,
}

/// <summary>
/// One resolved telemetry sample for one car.
/// </summary>
/// <remarks>
/// A <see langword="readonly record struct"/> on purpose: the ingestion path creates these at
/// telemetry rate, so it must not allocate. 11 fields, no reference-type members.
/// </remarks>
public readonly record struct TelemetryFrame(
    int DriverNumber,
    long Sequence,
    DateTimeOffset Timestamp,
    short SpeedKph,
    byte Gear,
    byte ThrottlePercent,
    bool Braking,
    ushort Rpm,
    DrsState Drs,
    float X,
    float Y);

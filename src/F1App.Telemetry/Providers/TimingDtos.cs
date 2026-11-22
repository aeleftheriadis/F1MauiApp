using System.Text.Json;
using System.Text.Json.Serialization;

namespace F1App.Telemetry.Providers;

/// <summary>One lap, with its three sector durations.</summary>
public sealed record OpenF1Lap(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("lap_number")] int LapNumber,
    [property: JsonPropertyName("lap_duration")] double? LapDuration,
    [property: JsonPropertyName("duration_sector_1")] double? Sector1,
    [property: JsonPropertyName("duration_sector_2")] double? Sector2,
    [property: JsonPropertyName("duration_sector_3")] double? Sector3,
    [property: JsonPropertyName("is_pit_out_lap")] bool? IsPitOutLap);

/// <summary>A classified position at a point in time.</summary>
public sealed record OpenF1Position(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("date")] DateTimeOffset Date);

/// <summary>
/// Gaps at a point in time.
/// </summary>
/// <remarks>
/// <c>gap_to_leader</c> is numeric for cars on the lead lap and a string such as <c>"+1 LAP"</c> for
/// lapped cars, so it is kept as a <see cref="JsonElement"/> and interpreted explicitly.
/// </remarks>
public sealed record OpenF1Interval(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("gap_to_leader")] JsonElement? GapToLeader,
    [property: JsonPropertyName("interval")] JsonElement? Interval,
    [property: JsonPropertyName("date")] DateTimeOffset Date);

/// <summary>A run on one set of tyres.</summary>
public sealed record OpenF1Stint(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("stint_number")] int StintNumber,
    [property: JsonPropertyName("lap_start")] int LapStart,
    [property: JsonPropertyName("lap_end")] int? LapEnd,
    [property: JsonPropertyName("compound")] string? Compound,
    [property: JsonPropertyName("tyre_age_at_start")] int? TyreAgeAtStart);

/// <summary>A pit stop.</summary>
public sealed record OpenF1PitStop(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("lap_number")] int LapNumber,
    [property: JsonPropertyName("pit_duration")] double? PitDuration);

/// <summary>Driver identity and team colour for a session.</summary>
public sealed record OpenF1Driver(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("name_acronym")] string? Acronym,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("team_name")] string? TeamName,
    [property: JsonPropertyName("team_colour")] string? TeamColour,
    [property: JsonPropertyName("headshot_url")] string? HeadshotUrl);

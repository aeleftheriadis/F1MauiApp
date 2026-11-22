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

/// <summary>
/// Official classification of a session from <c>/session_result</c>.
/// </summary>
/// <remarks>
/// Published a few minutes after the FIA results, so a live session has none yet. Carries what the lap data
/// cannot: championship points and whether the car was classified DNF, DNS or DSQ.
/// </remarks>
public sealed record OpenF1SessionResult(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("points")] double? Points,
    [property: JsonPropertyName("number_of_laps")] int? NumberOfLaps,
    [property: JsonPropertyName("dnf")] bool? DidNotFinish,
    [property: JsonPropertyName("dns")] bool? DidNotStart,
    [property: JsonPropertyName("dsq")] bool? Disqualified);

/// <summary>Championship position and points before and after a race (beta endpoint).</summary>
/// <remarks>
/// Verified against Jolpica: <c>position_start</c>/<c>points_start</c> matched the standings on all 22 shared
/// drivers of the 2026 Las Vegas race, so the difference between the two fields is the race's effect on the
/// title rather than a competing source of truth.
/// </remarks>
public sealed record OpenF1ChampionshipDriver(
    [property: JsonPropertyName("driver_number")] int? DriverNumber,
    [property: JsonPropertyName("position_start")] int? PositionStart,
    [property: JsonPropertyName("position_current")] int? PositionCurrent,
    [property: JsonPropertyName("points_start")] double? PointsStart,
    [property: JsonPropertyName("points_current")] double? PointsCurrent);

/// <summary>One pass: who overtook whom, and the position it produced.</summary>
/// <remarks>
/// The provider says this may be incomplete and that it counts position changes from pit stops and penalties
/// as well as on-track passes, so the app calls these "overtakes" and never "on-track passes".
/// </remarks>
public sealed record OpenF1Overtake(
    [property: JsonPropertyName("overtaking_driver_number")] int? OvertakingDriverNumber,
    [property: JsonPropertyName("overtaken_driver_number")] int? OvertakenDriverNumber,
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("date")] DateTimeOffset? Date);

/// <summary>One car's slot on the starting grid.</summary>
/// <remarks>
/// Measured: this endpoint answers for the <b>qualifying</b> session key, not the race's - asking with the race
/// key returns an empty list rather than an error, which is why the caller must resolve the sibling session.
/// </remarks>
public sealed record OpenF1StartingGrid(
    [property: JsonPropertyName("position")] int? Position,
    [property: JsonPropertyName("driver_number")] int? DriverNumber,
    [property: JsonPropertyName("lap_duration")] double? LapDuration);

/// <summary>An event from race control: session status, flags, safety car, incidents.</summary>
public sealed record OpenF1RaceControl(
    [property: JsonPropertyName("date")] DateTimeOffset? Date,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("flag")] string? Flag,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("lap_number")] int? LapNumber,
    [property: JsonPropertyName("driver_number")] int? DriverNumber,
    [property: JsonPropertyName("sector")] int? Sector);

/// <summary>Driver identity and team colour for a session.</summary>
public sealed record OpenF1Driver(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("name_acronym")] string? Acronym,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("team_name")] string? TeamName,
    [property: JsonPropertyName("team_colour")] string? TeamColour,
    [property: JsonPropertyName("headshot_url")] string? HeadshotUrl);

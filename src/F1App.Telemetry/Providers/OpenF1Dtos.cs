using System.Text.Json.Serialization;

namespace F1App.Telemetry.Providers;

/// <summary>A session (practice, qualifying, race, sprint) from the OpenF1 <c>/sessions</c> endpoint.</summary>
public sealed record OpenF1Session(
    [property: JsonPropertyName("session_key")] int SessionKey,
    [property: JsonPropertyName("meeting_key")] int MeetingKey,
    [property: JsonPropertyName("session_name")] string? SessionName,
    [property: JsonPropertyName("session_type")] string? SessionType,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("circuit_short_name")] string? CircuitShortName,
    [property: JsonPropertyName("country_name")] string? CountryName,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("date_start")] DateTimeOffset? DateStart,
    [property: JsonPropertyName("date_end")] DateTimeOffset? DateEnd);

/// <summary>A car telemetry message from the OpenF1 <c>/car_data</c> endpoint.</summary>
public sealed record OpenF1CarData(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("speed")] short? Speed,
    [property: JsonPropertyName("throttle")] byte? Throttle,
    [property: JsonPropertyName("brake")] byte? Brake,
    [property: JsonPropertyName("n_gear")] byte? Gear,
    [property: JsonPropertyName("rpm")] ushort? Rpm,
    [property: JsonPropertyName("drs")] byte? Drs,
    [property: JsonPropertyName("_id")] long? Id);

/// <summary>A GPS position message from the OpenF1 <c>/location</c> endpoint.</summary>
public sealed record OpenF1Location(
    [property: JsonPropertyName("driver_number")] int DriverNumber,
    [property: JsonPropertyName("date")] DateTimeOffset Date,
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("_id")] long? Id);

/// <summary>Source-generated serialisation metadata; no runtime reflection, trim and AOT safe.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<OpenF1Session>))]
[JsonSerializable(typeof(List<OpenF1CarData>))]
[JsonSerializable(typeof(List<OpenF1Location>))]
[JsonSerializable(typeof(List<OpenF1Lap>))]
[JsonSerializable(typeof(List<OpenF1Position>))]
[JsonSerializable(typeof(List<OpenF1Interval>))]
[JsonSerializable(typeof(List<OpenF1Stint>))]
[JsonSerializable(typeof(List<OpenF1PitStop>))]
[JsonSerializable(typeof(List<OpenF1Driver>))]
public sealed partial class OpenF1JsonContext : JsonSerializerContext;

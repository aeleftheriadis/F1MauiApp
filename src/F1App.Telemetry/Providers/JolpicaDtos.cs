using System.Text.Json.Serialization;

namespace F1App.Telemetry.Providers;

internal sealed record JolpicaResponse(
    [property: JsonPropertyName("MRData")] JolpicaMrData? MrData);

internal sealed record JolpicaMrData(
    [property: JsonPropertyName("RaceTable")] JolpicaRaceTable? RaceTable);

internal sealed record JolpicaRaceTable(
    [property: JsonPropertyName("season")] string? Season,
    [property: JsonPropertyName("Races")] List<JolpicaRace>? Races);

internal sealed record JolpicaRace(
    [property: JsonPropertyName("round")] string? Round,
    [property: JsonPropertyName("raceName")] string? RaceName,
    [property: JsonPropertyName("Circuit")] JolpicaCircuit? Circuit,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("time")] string? Time);

internal sealed record JolpicaCircuit(
    [property: JsonPropertyName("circuitId")] string? CircuitId,
    [property: JsonPropertyName("circuitName")] string? CircuitName,
    [property: JsonPropertyName("Location")] JolpicaLocation? Location);

internal sealed record JolpicaLocation(
    [property: JsonPropertyName("locality")] string? Locality,
    [property: JsonPropertyName("country")] string? Country);

/// <summary>Source-generated serialisation metadata for the Jolpica (Ergast-compatible) schema.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JolpicaResponse))]
internal sealed partial class JolpicaJsonContext : JsonSerializerContext;

using System.Text.Json.Serialization;

namespace F1App.Telemetry.Facts;

/// <summary>
/// Jolpica's standings payload for <c>/driverStandings/1.json</c>: the champion of every season.
/// </summary>
internal sealed record JolpicaChampionPayload(
    [property: JsonPropertyName("MRData")] JolpicaChampionData? MrData);

internal sealed record JolpicaChampionData(
    [property: JsonPropertyName("StandingsTable")] JolpicaStandingsTable? StandingsTable);

internal sealed record JolpicaStandingsTable(
    [property: JsonPropertyName("StandingsLists")] List<JolpicaStandingsList>? StandingsLists);

internal sealed record JolpicaStandingsList(
    [property: JsonPropertyName("season")] string? Season,
    [property: JsonPropertyName("DriverStandings")] List<JolpicaDriverStanding>? DriverStandings);

internal sealed record JolpicaDriverStanding(
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("points")] string? Points,
    [property: JsonPropertyName("wins")] string? Wins,
    [property: JsonPropertyName("Driver")] JolpicaDriverRef? Driver,
    [property: JsonPropertyName("Constructors")] List<JolpicaConstructorRef>? Constructors);

internal sealed record JolpicaDriverRef(
    [property: JsonPropertyName("driverId")] string? DriverId,
    [property: JsonPropertyName("givenName")] string? GivenName,
    [property: JsonPropertyName("familyName")] string? FamilyName);

internal sealed record JolpicaConstructorRef(
    [property: JsonPropertyName("constructorId")] string? ConstructorId,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>Source-generated metadata; the app is trimmed and AOT-analyzed.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JolpicaChampionPayload))]
internal sealed partial class FactsJsonContext : JsonSerializerContext;

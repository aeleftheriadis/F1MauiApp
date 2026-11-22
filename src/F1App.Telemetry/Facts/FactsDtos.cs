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
    [property: JsonPropertyName("familyName")] string? FamilyName,
    [property: JsonPropertyName("code")] string? Code);

internal sealed record JolpicaConstructorRef(
    [property: JsonPropertyName("constructorId")] string? ConstructorId,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>
/// Jolpica's payload for a page of <c>/{season}/results.json</c>, the season's full classifications.
/// </summary>
internal sealed record JolpicaSeasonResultsPayload(
    [property: JsonPropertyName("MRData")] JolpicaSeasonResultsData? MrData);

internal sealed record JolpicaSeasonResultsData(
    [property: JsonPropertyName("total")] string? Total,
    [property: JsonPropertyName("limit")] string? Limit,
    [property: JsonPropertyName("offset")] string? Offset,
    [property: JsonPropertyName("RaceTable")] JolpicaSeasonResultsTable? RaceTable);

internal sealed record JolpicaSeasonResultsTable(
    [property: JsonPropertyName("Races")] List<JolpicaSeasonRace>? Races);

internal sealed record JolpicaSeasonRace(
    [property: JsonPropertyName("round")] string? Round,
    [property: JsonPropertyName("raceName")] string? RaceName,
    [property: JsonPropertyName("Results")] List<JolpicaSeasonResult>? Results);

/// <summary>One race in detail: <c>/{season}/{round}/results.json</c>.</summary>
internal sealed record JolpicaRaceDetailResultsPayload(
    [property: JsonPropertyName("MRData")] JolpicaRaceDetailResultsData? MrData);

internal sealed record JolpicaRaceDetailResultsData(
    [property: JsonPropertyName("RaceTable")] JolpicaRaceDetailTable? RaceTable);

internal sealed record JolpicaRaceDetailTable(
    [property: JsonPropertyName("Races")] List<JolpicaRaceDetailRace>? Races);

internal sealed record JolpicaRaceDetailRace(
    [property: JsonPropertyName("raceName")] string? RaceName,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("Circuit")] JolpicaVaultCircuit? Circuit,
    [property: JsonPropertyName("Results")] List<JolpicaSeasonResult>? Results);

/// <summary>Qualifying for one race: <c>/{season}/{round}/qualifying.json</c>.</summary>
internal sealed record JolpicaRaceDetailQualifyingPayload(
    [property: JsonPropertyName("MRData")] JolpicaRaceDetailQualifyingData? MrData);

internal sealed record JolpicaRaceDetailQualifyingData(
    [property: JsonPropertyName("RaceTable")] JolpicaQualifyingTable? RaceTable);

internal sealed record JolpicaQualifyingTable(
    [property: JsonPropertyName("Races")] List<JolpicaQualifyingRace>? Races);

internal sealed record JolpicaQualifyingRace(
    [property: JsonPropertyName("QualifyingResults")] List<JolpicaQualifyingResult>? QualifyingResults);

internal sealed record JolpicaQualifyingResult(
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("Driver")] JolpicaDriverRef? Driver,
    [property: JsonPropertyName("Q1")] string? Q1,
    [property: JsonPropertyName("Q2")] string? Q2,
    [property: JsonPropertyName("Q3")] string? Q3);

/// <summary>Pit stops for one race: <c>/{season}/{round}/pitstops.json</c>.</summary>
internal sealed record JolpicaRaceDetailPitStopsPayload(
    [property: JsonPropertyName("MRData")] JolpicaRaceDetailPitStopsData? MrData);

internal sealed record JolpicaRaceDetailPitStopsData(
    [property: JsonPropertyName("RaceTable")] JolpicaPitStopsTable? RaceTable);

internal sealed record JolpicaPitStopsTable(
    [property: JsonPropertyName("Races")] List<JolpicaPitStopsRace>? Races);

internal sealed record JolpicaPitStopsRace(
    [property: JsonPropertyName("PitStops")] List<JolpicaPitStop>? PitStops);

internal sealed record JolpicaPitStop(
    [property: JsonPropertyName("driverId")] string? DriverId,
    [property: JsonPropertyName("lap")] string? Lap,
    [property: JsonPropertyName("stop")] string? Stop,
    [property: JsonPropertyName("duration")] string? Duration);

internal sealed record JolpicaSeasonResult(
    [property: JsonPropertyName("position")] string? Position,
    [property: JsonPropertyName("positionText")] string? PositionText,
    [property: JsonPropertyName("points")] string? Points,
    [property: JsonPropertyName("laps")] string? Laps,
    [property: JsonPropertyName("grid")] string? Grid,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("Driver")] JolpicaDriverRef? Driver,
    [property: JsonPropertyName("Constructor")] JolpicaConstructorRef? Constructor);

/// <summary>
/// Jolpica's payload for <c>/{season}/results/1.json</c>: every race of a season with its winner.
/// One request per season, verified against the live endpoint for 1950 and 2023.
/// </summary>
internal sealed record JolpicaRaceVaultPayload(
    [property: JsonPropertyName("MRData")] JolpicaRaceVaultData? MrData);

internal sealed record JolpicaRaceVaultData(
    [property: JsonPropertyName("RaceTable")] JolpicaRaceTable? RaceTable);

internal sealed record JolpicaRaceTable(
    [property: JsonPropertyName("Races")] List<JolpicaVaultRace>? Races);

internal sealed record JolpicaVaultRace(
    [property: JsonPropertyName("season")] string? Season,
    [property: JsonPropertyName("round")] string? Round,
    [property: JsonPropertyName("raceName")] string? RaceName,
    [property: JsonPropertyName("Circuit")] JolpicaVaultCircuit? Circuit,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("Results")] List<JolpicaVaultResult>? Results);

internal sealed record JolpicaVaultCircuit(
    [property: JsonPropertyName("circuitId")] string? CircuitId,
    [property: JsonPropertyName("circuitName")] string? CircuitName,
    [property: JsonPropertyName("Location")] JolpicaVaultLocation? Location);

internal sealed record JolpicaVaultLocation(
    [property: JsonPropertyName("country")] string? Country);

internal sealed record JolpicaVaultResult(
    [property: JsonPropertyName("laps")] string? Laps,
    [property: JsonPropertyName("Driver")] JolpicaDriverRef? Driver,
    [property: JsonPropertyName("Constructor")] JolpicaConstructorRef? Constructor,
    [property: JsonPropertyName("Time")] JolpicaVaultTime? Time);

internal sealed record JolpicaVaultTime(
    [property: JsonPropertyName("time")] string? Time);

/// <summary>Source-generated metadata; the app is trimmed and AOT-analyzed.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JolpicaChampionPayload))]
[JsonSerializable(typeof(JolpicaRaceVaultPayload))]
[JsonSerializable(typeof(JolpicaSeasonResultsPayload))]
[JsonSerializable(typeof(JolpicaRaceDetailResultsPayload))]
[JsonSerializable(typeof(JolpicaRaceDetailQualifyingPayload))]
[JsonSerializable(typeof(JolpicaRaceDetailPitStopsPayload))]
internal sealed partial class FactsJsonContext : JsonSerializerContext;

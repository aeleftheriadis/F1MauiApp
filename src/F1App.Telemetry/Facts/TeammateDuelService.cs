using System.Collections.Immutable;
using F1App.Domain;
using Microsoft.Extensions.Logging;

namespace F1App.Telemetry.Facts;

/// <summary>One season's teammate duels, keyed by driver id, with where they came from.</summary>
public sealed record TeammateDuelsSnapshot(
    int Season,
    ImmutableDictionary<string, TeammateDuel> Duels,
    string? Problem);

/// <summary>
/// Computes teammate head-to-heads for a season from cached classifications.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is only ever made between two cars of the same constructor in the same race, and only
/// where both were classified - a retirement is not a defeat, so it is excluded rather than counted. That
/// means the totals can be smaller than the number of races, which the snapshot reports.
/// </para>
/// <para>
/// A season is cached for a day: the current season gains a round every other week, and nothing older
/// changes at all.
/// </para>
/// </remarks>
public sealed partial class TeammateDuelService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly JolpicaSeasonResultsClient _client;
    private readonly SqliteFactsCache? _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TeammateDuelService> _logger;

    /// <summary>Creates the service.</summary>
    public TeammateDuelService(
        JolpicaSeasonResultsClient client,
        SqliteFactsCache? cache,
        TimeProvider timeProvider,
        ILogger<TeammateDuelService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Returns the season's duels, fetching the classifications when the cache is empty or stale.</summary>
    public async Task<TeammateDuelsSnapshot> GetAsync(
        int season,
        bool force,
        CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(season, force, cancellationToken).ConfigureAwait(false);

        return rows switch
        {
            ImmutableArray<RaceResultRow> results when results.Length > 0 =>
                new TeammateDuelsSnapshot(season, Compute(results), null),

            ImmutableArray<RaceResultRow> =>
                new TeammateDuelsSnapshot(season, ImmutableDictionary<string, TeammateDuel>.Empty,
                    $"no classifications published for {season}"),

            HttpFailure failure => new TeammateDuelsSnapshot(
                season,
                ImmutableDictionary<string, TeammateDuel>.Empty,
                $"head-to-head unavailable ({failure.StatusCode})"),

            RateLimited limited => new TeammateDuelsSnapshot(
                season,
                ImmutableDictionary<string, TeammateDuel>.Empty,
                $"rate limited, retry in {limited.RetryAfterSeconds}s"),

            _ => new TeammateDuelsSnapshot(
                season,
                ImmutableDictionary<string, TeammateDuel>.Empty,
                "unexpected provider result"),
        };
    }

    /// <summary>
    /// Counts each pair of teammates in each race, both ways round, so either driver can be shown.
    /// </summary>
    public static ImmutableDictionary<string, TeammateDuel> Compute(ImmutableArray<RaceResultRow> rows)
    {
        var duels = ImmutableDictionary.CreateBuilder<string, TeammateDuel>();

        // One group per race and constructor, which is exactly one pair of cars.
        var pairs = rows
            .GroupBy(static row => (row.Round, row.ConstructorId))
            .Select(static group => group.ToImmutableArray())
            .Where(static pair => pair.Length == 2 && pair[0].DriverId != pair[1].DriverId);

        var accumulator = new Dictionary<(string Driver, string Teammate), Tally>();

        foreach (var pair in pairs)
        {
            var first = pair[0];
            var second = pair[1];

            if (first.Position is { } firstPosition && second.Position is { } secondPosition)
            {
                Accumulate(accumulator, first, second, firstPosition, secondPosition);
                Accumulate(accumulator, second, first, secondPosition, firstPosition);
            }
        }

        foreach (var ((driver, teammate), tally) in accumulator)
        {
            duels[driver] = new TeammateDuel(
                DriverId: driver,
                DriverName: tally.DriverName,
                TeammateId: teammate,
                TeammateName: tally.TeammateName,
                DriverCode: tally.DriverCode,
                TeammateCode: tally.TeammateCode,
                RaceWins: tally.Wins,
                TeammateWins: tally.Losses,
                // Double division: integer arithmetic would truncate -0.5 to 0, which reads as "dead level".
                AveragePositionDelta: tally.Compared == 0 ? 0d : (double)tally.PositionSum / tally.Compared,
                QualifyingWins: tally.QualifyingWins,
                QualifyingLosses: tally.QualifyingLosses,
                Compared: tally.Compared);
        }

        return duels.ToImmutable();
    }

    private static void Accumulate(
        Dictionary<(string, string), Tally> accumulator,
        RaceResultRow driver,
        RaceResultRow teammate,
        int driverPosition,
        int teammatePosition)
    {
        if (!accumulator.TryGetValue((driver.DriverId, teammate.DriverId), out var tally))
        {
            tally = new Tally
            {
                DriverName = driver.DriverName,
                TeammateName = teammate.DriverName,
                DriverCode = driver.DriverCode,
                TeammateCode = teammate.DriverCode,
            };
            accumulator[(driver.DriverId, teammate.DriverId)] = tally;
        }

        tally.Compared++;

        // Positive means this driver finished ahead.
        tally.PositionSum += teammatePosition - driverPosition;

        if (driverPosition < teammatePosition)
        {
            tally.Wins++;
        }
        else if (teammatePosition < driverPosition)
        {
            tally.Losses++;
        }

        if (driver.Grid is { } driverGrid && teammate.Grid is { } teammateGrid && driverGrid > 0 && teammateGrid > 0)
        {
            if (driverGrid < teammateGrid)
            {
                tally.QualifyingWins++;
            }
            else if (teammateGrid < driverGrid)
            {
                tally.QualifyingLosses++;
            }
        }
    }

    private async Task<FetchResult<ImmutableArray<RaceResultRow>>> LoadRowsAsync(
        int season,
        bool force,
        CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return await _client.GetSeasonResultsAsync(season, cancellationToken).ConfigureAwait(false);
        }

        if (!force)
        {
            var fetchedAt = await _cache
                .ReadSeasonResultsFetchedAtAsync(season, cancellationToken)
                .ConfigureAwait(false);

            if (fetchedAt is { } stamp && _timeProvider.GetUtcNow() - stamp < CacheTtl)
            {
                return await _cache.ReadSeasonResultsAsync(season, cancellationToken).ConfigureAwait(false);
            }
        }

        var fetched = await _client.GetSeasonResultsAsync(season, cancellationToken).ConfigureAwait(false);

        if (fetched is not ImmutableArray<RaceResultRow> rows || rows.IsDefaultOrEmpty)
        {
            return fetched;
        }

        await _cache.WriteSeasonResultsAsync(season, rows, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        LogCached(_logger, rows.Length, season);
        return rows;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {Count} classifications for {Season}.")]
    private static partial void LogCached(ILogger logger, int count, int season);

    private sealed class Tally
    {
        public string DriverName = string.Empty;
        public string TeammateName = string.Empty;
        public string DriverCode = string.Empty;
        public string TeammateCode = string.Empty;
        public int Wins;
        public int Losses;
        public int Compared;
        public int PositionSum;
        public int QualifyingWins;
        public int QualifyingLosses;
    }
}

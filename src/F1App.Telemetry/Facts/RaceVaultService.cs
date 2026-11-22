using System.Collections.Immutable;
using F1App.Domain;
using Microsoft.Extensions.Logging;

namespace F1App.Telemetry.Facts;

/// <summary>The vault the Archive screen shows: every race since 1950, with its record book.</summary>
public sealed record RaceVaultSnapshot(
    ImmutableArray<ArchivedRace> Races,
    VaultRecords Records,
    string? Problem);

/// <summary>Progress of the one-off archive sync, reported per completed season.</summary>
public sealed record VaultSyncProgress(int SeasonsDone, int SeasonsTotal, int Races);

/// <summary>
/// Keeps every world-championship race since 1950 in SQLite and computes the record book from it.
/// </summary>
/// <remarks>
/// <para>
/// One request per season through <see cref="JolpicaRaceVaultClient"/>: about eighty for the whole
/// history, written season by season so a sync interrupted at 1970 resumes at 1970. Past seasons never
/// change and are fetched once; the current season refreshes daily, when its rows are a day old.
/// </para>
/// <para>
/// <see cref="LoadAsync"/> never touches the network: it reads the cache and computes the record book
/// from it. <see cref="SyncAsync"/> is the only method that fetches, and the screen decides when to
/// call it, so the archive opens instantly even while a sync is due.
/// </para>
/// </remarks>
public sealed class RaceVaultService : IDisposable
{
    private const int FirstSeason = 1950;
    private const int MaxParallelSeasons = 4;
    private static readonly TimeSpan CurrentSeasonTtl = TimeSpan.FromHours(24);

    private readonly JolpicaRaceVaultClient _client;
    private readonly SqliteFactsCache? _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RaceVaultService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>Creates the service; a null <paramref name="cache"/> means no persistence and no syncing.</summary>
    public RaceVaultService(
        JolpicaRaceVaultClient client,
        SqliteFactsCache? cache,
        TimeProvider timeProvider,
        ILogger<RaceVaultService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Reads the vault from the cache and computes its record book. Never fetches.</summary>
    public async Task<RaceVaultSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var races = _cache is null ? [] : await _cache.ReadRacesAsync(cancellationToken).ConfigureAwait(false);

        return new RaceVaultSnapshot(
            races,
            ComputeRecords(races),
            races.IsEmpty ? "archive not synced yet" : null);
    }

    /// <summary>True when a season is missing from the cache or the current season is a day old.</summary>
    public async Task<bool> NeedsSyncAsync(CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return false;
        }

        var seasons = await _cache.ReadSeasonsAsync(cancellationToken).ConfigureAwait(false);
        var bySeason = seasons.ToDictionary(static row => row.Season, static row => row.FetchedAt);
        var current = _timeProvider.GetUtcNow().Year;

        for (var season = FirstSeason; season <= current; season++)
        {
            // Past seasons are history once fetched; only the current one can go stale.
            if (!bySeason.TryGetValue(season, out var fetched)
                || (season == current && _timeProvider.GetUtcNow() - fetched > CurrentSeasonTtl))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Fetches the missing or stale seasons, four at a time, and reports each one written.</summary>
    /// <returns>What went wrong, if anything; <c>null</c> when every season landed.</returns>
    public async Task<string?> SyncAsync(
        IProgress<VaultSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var seasonsToFetch = await MissingSeasonsAsync(cancellationToken).ConfigureAwait(false);

            if (seasonsToFetch.Length == 0)
            {
                return null;
            }

            var done = 0;
            var races = 0;
            var failures = 0;
            string? firstFailure = null;

            await Parallel.ForEachAsync(
                seasonsToFetch,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelSeasons,
                    CancellationToken = cancellationToken,
                },
                async (season, token) =>
                {
                    var result = await _client.GetSeasonRacesAsync(season, token).ConfigureAwait(false);

                    if (result is ImmutableArray<ArchivedRace> found && !found.IsEmpty && _cache is not null)
                    {
                        // Written per season: an interrupted sync resumes where it stopped.
                        await _cache.WriteRacesAsync(season, found, _timeProvider.GetUtcNow(), token)
                            .ConfigureAwait(false);

                        var written = Interlocked.Add(ref races, found.Length);
                        var writtenSeasons = Interlocked.Increment(ref done);
                        progress?.Report(new VaultSyncProgress(writtenSeasons, seasonsToFetch.Length, written));
                        return;
                    }

                    if (result is ImmutableArray<ArchivedRace>)
                    {
                        // An empty season: a future calendar placeholder. Not a failure, not written.
                        Interlocked.Increment(ref done);
                        return;
                    }

                    Interlocked.Increment(ref failures);
                    Interlocked.CompareExchange(
                        ref firstFailure,
                        result switch
                        {
                            RateLimited limited => $"rate limited, retry in {limited.RetryAfterSeconds}s",
                            HttpFailure failure => $"season {season}: {failure.StatusCode}",
                            _ => "unexpected provider result",
                        },
                        null);
                }).ConfigureAwait(false);

            return failures == 0 ? null : $"{firstFailure}; {failures} of {seasonsToFetch.Length} seasons failed";
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _syncLock.Dispose();

    /// <summary>
    /// Races held on the same calendar day in earlier seasons, newest first.
    /// </summary>
    /// <remarks>
    /// Pure lookup over cached rows - no request, no invented anniversary: a day with no race in the vault
    /// returns an empty list and the screen says nothing rather than manufacturing an occasion.
    /// </remarks>
    public static ImmutableArray<ArchivedRace> OnThisDay(ImmutableArray<ArchivedRace> races, DateOnly today) =>
        races
            .Where(race => race.Date.Month == today.Month && race.Date.Day == today.Day && race.Date.Year < today.Year)
            .OrderByDescending(static race => race.Season)
            .ToImmutableArray();

    /// <summary>Computes the all-time win tables from the races the vault holds.</summary>
    public static VaultRecords ComputeRecords(ImmutableArray<ArchivedRace> races)
    {
        var seasons = races.Select(static race => race.Season).Distinct().Count();
        var driverWins = CountWins(races, static race => race.Winner);
        var constructorWins = CountWins(races, static race => race.Constructor);

        return new VaultRecords(races.Length, seasons, driverWins, constructorWins);
    }

    private static ImmutableArray<WinRecord> CountWins(
        ImmutableArray<ArchivedRace> races,
        Func<ArchivedRace, string> name)
    {
        var byName = new Dictionary<string, List<int>>();

        foreach (var race in races)
        {
            var key = name(race);

            if (!byName.TryGetValue(key, out var seasons))
            {
                seasons = [];
                byName[key] = seasons;
            }

            seasons.Add(race.Season);
        }

        return byName
            .Select(static pair => new WinRecord(
                pair.Key,
                pair.Value.Count,
                pair.Value.Min(),
                pair.Value.Max()))
            .OrderByDescending(static record => record.Wins)
            .ThenByDescending(static record => record.LastSeason)
            .ThenBy(static record => record.Name, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private async Task<ImmutableArray<int>> MissingSeasonsAsync(CancellationToken cancellationToken)
    {
        var current = _timeProvider.GetUtcNow().Year;
        var missing = new List<int>();

        if (_cache is null)
        {
            return [.. Enumerable.Range(FirstSeason, current - FirstSeason + 1)];
        }

        var present = await _cache.ReadSeasonsAsync(cancellationToken).ConfigureAwait(false);
        var bySeason = present.ToDictionary(static row => row.Season, static row => row.FetchedAt);

        for (var season = FirstSeason; season <= current; season++)
        {
            if (!bySeason.TryGetValue(season, out var fetched))
            {
                missing.Add(season);
            }
            else if (season == current && _timeProvider.GetUtcNow() - fetched > CurrentSeasonTtl)
            {
                missing.Add(season);
            }
        }

        return [.. missing];
    }
}

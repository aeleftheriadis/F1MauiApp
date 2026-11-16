using System.Collections.Immutable;
using F1App.Domain;
using Microsoft.Extensions.Logging;

namespace F1App.Telemetry.Facts;

/// <summary>What the facts screen shows: the champions, newest first, and where they came from.</summary>
public sealed record ChampionsSnapshot(
    ImmutableArray<SeasonChampion> Champions,
    int Expected,
    DateTimeOffset FetchedAt,
    string? Problem);

/// <summary>
/// Keeps the champions list in memory so opening the screen twice does not cost two requests.
/// </summary>
/// <remarks>
/// The 500-per-hour budget on Jolpica is generous, but a "historical" screen should not need the network
/// every time it is opened. Twenty-four hours is long enough that the list is stable and short enough that
/// a new season appears the day after the finale.
/// Persisting this to SQLite is the next step; until then, "offline" means the process is still running.
/// </remarks>
public sealed partial class ChampionsService : IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>Seasons fetched, newest first. Twenty covers a generation of the sport in twenty requests.</summary>
    private const int SeasonsCovered = 20;

    private readonly JolpicaFactsClient _client;
    private readonly SqliteFactsCache? _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChampionsService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private ImmutableArray<SeasonChampion> _cached = [];
    private int _expected;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    /// <summary>Creates the service.</summary>
    public ChampionsService(
        JolpicaFactsClient client,
        SqliteFactsCache? cache,
        TimeProvider timeProvider,
        ILogger<ChampionsService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Returns the champions, refreshing when the cache is stale or <paramref name="force"/> is set.</summary>
    public async Task<ChampionsSnapshot> GetAsync(bool force, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (!force && !_cached.IsDefaultOrEmpty && now - _cachedAt < CacheTtl)
        {
            return new ChampionsSnapshot(_cached, _expected, _cachedAt, Describe(_cached.Length, _expected));
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Persisted copy first: it is what makes "historical" work with no network and no process.
            if (!force && _cache is not null)
            {
                var persisted = await _cache
                    .ReadChampionsAsync(now, CacheTtl, cancellationToken)
                    .ConfigureAwait(false);

                if (!persisted.IsDefaultOrEmpty)
                {
                    _cached = persisted;
                    _cachedAt = now;
                    _expected = persisted.Length;
                    return new ChampionsSnapshot(persisted, _expected, now, null);
                }
            }

            var to = now.Year;
            var from = to - SeasonsCovered + 1;
            var expected = to - from + 1;

            var result = await _client.GetChampionsAsync(from, to, cancellationToken).ConfigureAwait(false);

            switch (result)
            {
                case ImmutableArray<SeasonChampion> champions:
                    _cached = champions;
                    _expected = expected;
                    _cachedAt = _timeProvider.GetUtcNow();

                    if (_cache is not null)
                    {
                        await _cache
                            .WriteChampionsAsync(champions, _cachedAt, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return new ChampionsSnapshot(champions, expected, _cachedAt, Describe(champions.Length, expected));

                case HttpFailure failure:
                    LogChampionsFailed(_logger, failure.StatusCode, failure.Reason);
                    // A stale list beats an empty screen; say so rather than pretending it is current.
                    return _cached.IsDefaultOrEmpty
                        ? new ChampionsSnapshot([], expected, now, $"champions unavailable ({failure.StatusCode})")
                        : new ChampionsSnapshot(_cached, _expected, _cachedAt, $"showing cached champions ({failure.StatusCode})");

                case RateLimited limited:
                    return _cached.IsDefaultOrEmpty
                        ? new ChampionsSnapshot([], expected, now, $"rate limited, retry in {limited.RetryAfterSeconds}s")
                        : new ChampionsSnapshot(_cached, _expected, _cachedAt, "showing cached champions (rate limited)");

                default:
                    return new ChampionsSnapshot([], expected, now, "unexpected provider result");
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _loadLock.Dispose();

    private static string? Describe(int found, int expected) =>
        found >= expected ? null : $"showing {found} of {expected} seasons";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Champions request failed: {Status} {Reason}")]
    private static partial void LogChampionsFailed(ILogger logger, int status, string? reason);
}

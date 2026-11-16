using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace F1App.Telemetry.News;

/// <summary>A refresh result: what came back, and which feeds did not answer.</summary>
public sealed record NewsSnapshot(
    ImmutableArray<NewsItem> Items,
    ImmutableArray<string> Failures,
    DateTimeOffset FetchedAt);

/// <summary>
/// Fetches every configured feed and merges the headlines.
/// </summary>
/// <remarks>
/// Bounded parallelism (four at a time) through one shared <see cref="RequestGate"/>, so a refresh does
/// not turn into ten simultaneous connections. One failing feed degrades the list; it never fails the
/// screen. Results are cached for five minutes because feed freshness is minutes, not seconds.
/// </remarks>
public sealed partial class NewsService : IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _fetchGate = new(4, 4);

    private readonly HttpClient _http;
    private readonly RequestGate _gate;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NewsService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private ImmutableArray<NewsItem> _cached = [];
    private ImmutableArray<string> _failures = [];
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    /// <summary>Creates the service.</summary>
    public NewsService(
        HttpClient http,
        RequestGate gate,
        TimeProvider timeProvider,
        ILogger<NewsService> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _gate = gate;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Returns the merged headlines, refreshing when the cache is older than five minutes.</summary>
    /// <param name="max">Maximum items to return.</param>
    /// <param name="force">Ignore the cache.</param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    public async Task<NewsSnapshot> GetLatestAsync(int max, bool force, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (!force && now - _cachedAt < CacheTtl && !_cached.IsDefaultOrEmpty)
        {
            return new NewsSnapshot(_cached.Take(max).ToImmutableArray(), _failures, _cachedAt);
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = ImmutableArray.CreateBuilder<NewsItem>();
            var failures = ImmutableArray.CreateBuilder<string>();

            await Parallel.ForEachAsync(
                NewsSources.All,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = cancellationToken,
                },
                async (source, token) =>
                {
                    try
                    {
                        var parsed = await FetchAsync(source, token).ConfigureAwait(false);

                        lock (items)
                        {
                            items.AddRange(parsed);
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        lock (failures)
                        {
                            failures.Add(source.Name);
                        }

                        LogFeedFailed(_logger, source.Name, exception);
                    }
                }).ConfigureAwait(false);

            _cached = items
                .OrderByDescending(static item => item.Published ?? DateTimeOffset.MinValue)
                .ToImmutableArray();

            _failures = failures.ToImmutable();
            _cachedAt = _timeProvider.GetUtcNow();

            return new NewsSnapshot(_cached.Take(max).ToImmutableArray(), _failures, _cachedAt);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loadLock.Dispose();
        _fetchGate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feed {Source} could not be read.")]
    private static partial void LogFeedFailed(ILogger logger, string source, Exception exception);

    private async Task<ImmutableArray<NewsItem>> FetchAsync(NewsSource source, CancellationToken cancellationToken)
    {
        await _fetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            using var response = await _http
                .GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            return NewsFeedReader.Read(stream, source);
        }
        finally
        {
            _fetchGate.Release();
        }
    }
}

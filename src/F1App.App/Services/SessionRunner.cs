using F1App.Domain;
using F1App.Telemetry;
using F1App.Telemetry.Replay;
using Microsoft.Extensions.Logging;

namespace F1App.Services;

/// <summary>A session the app can replay.</summary>
/// <param name="SessionKey">OpenF1 session key.</param>
/// <param name="Label">Operator-facing identity, shown next to the data.</param>
/// <param name="From">Start of the replay window.</param>
/// <param name="CircuitId">Circuit slug, so the map can tell whether it belongs to this replay.</param>
/// <param name="MeetingName">Race name as published, e.g. <c>Qatar Grand Prix</c>, or null when unknown.</param>
public sealed record SessionRequest(
    int SessionKey,
    string Label,
    DateTimeOffset From,
    string CircuitId,
    string? MeetingName = null);

/// <summary>
/// Loads the selected session from OpenF1 and streams it into the snapshot store.
/// </summary>
/// <remarks>
/// <para>
/// One runner owns one replay at a time. Switching sessions cancels the current load or stream, clears
/// the store and starts again, serialised by a semaphore so two quick taps cannot interleave two loads.
/// </para>
/// <para>
/// The loop is started without awaiting, so its failure path is explicit: anything thrown is logged and
/// surfaces as <see cref="FailureReason"/>. There is no fallback to invented data - if the load fails, the
/// screens say so.
/// </para>
/// </remarks>
public sealed class SessionRunner : IAsyncDisposable
{
    /// <summary>Car used to trace the circuit outline: any car that completed a lap works.</summary>
    public const int DefaultOutlineDriver = 44;

    /// <summary>Window replayed. Thirty seconds of real running is roughly 2 400 samples.</summary>
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromSeconds(30);

    private const double ReplaySpeedFactor = 2d;

    private readonly OpenF1ReplayLoader _loader;
    private readonly DefaultSessionResolver _resolver;
    private readonly TelemetrySnapshotStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IClock _clock;
    private readonly ILogger<SessionRunner> _logger;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private SessionRequest? _request;

    public SessionRunner(
        OpenF1ReplayLoader loader,
        DefaultSessionResolver resolver,
        TelemetrySnapshotStore store,
        TimeProvider timeProvider,
        IClock clock,
        ILogger<SessionRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _loader = loader;
        _resolver = resolver;
        _store = store;
        _timeProvider = timeProvider;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Circuit geometry for the session being replayed.</summary>
    public TrackOutline? Outline { get; private set; }

    /// <summary>Identity of the session being replayed.</summary>
    public string SessionLabel { get; private set; } = "resolving session";

    /// <summary>Circuit slug of the session being replayed.</summary>
    public string CircuitId { get; private set; } = string.Empty;

    /// <summary>Race name as the feeds publish it, or empty when the request did not carry one.</summary>
    public string MeetingName { get; private set; } = string.Empty;

    /// <summary>OpenF1 key of the session being replayed.</summary>
    public int SessionKey { get; private set; }

    /// <summary>Samples of the loaded replay window, for charts.</summary>
    public ReplaySession? Loaded { get; private set; }

    /// <summary>Why there is no data, when there is none.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Starts the replay of the current session. Safe to call more than once.</summary>
    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
    }

    /// <summary>Switches to another session: cancels the current replay, loads, and starts again.</summary>
    public async Task SwitchToAsync(SessionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loop is not null)
            {
                await StopAsync().ConfigureAwait(false);
            }

            _request = request;
            _store.Reset();
            Outline = null;
            Loaded = null;
            FailureReason = null;
            SessionLabel = request.Label;
            CircuitId = request.CircuitId;
            MeetingName = request.MeetingName ?? string.Empty;
            SessionKey = request.SessionKey;
            Start();
        }
        finally
        {
            _switchLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _switchLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
            _switchLock.Dispose();
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private async Task StopAsync()
    {
        if (_cancellation is null || _loop is null)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the user switches sessions or the app shuts down.
        }

        _cancellation.Dispose();
        _cancellation = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // No pick yet: resolve the current weekend's latest finished session. There is no hardcoded
            // fallback: if nothing resolves, the replay does not start and the screens say so, rather than
            // presenting a two-year-old session as if it were current.
            var request = _request
                          ?? await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);

            if (request is null)
            {
                FailureReason = "no finished session available to replay";
                SessionLabel = "no session";
                return;
            }

            _request = request;
            SessionLabel = request.Label;
            CircuitId = request.CircuitId;
            MeetingName = request.MeetingName ?? string.Empty;
            SessionKey = request.SessionKey;

            var loaded = await _loader
                .LoadAsync(
                    request.SessionKey,
                    request.Label,
                    request.From,
                    ReplayWindow,
                    DefaultOutlineDriver,
                    cancellationToken)
                .ConfigureAwait(false);

            switch (loaded)
            {
                case ReplaySession session:
                    Outline = session.Outline;
                    Loaded = session;
                    SessionLabel = session.Label;
                    CircuitId = request.CircuitId;
            MeetingName = request.MeetingName ?? string.Empty;
                    SessionKey = request.SessionKey;
                    FailureReason = null;
                    _store.Reset();

                    _logger.LogInformation(
                        "Loaded {Label}: {Points} outline points, {Samples} samples.",
                        session.Label,
                        session.Outline.Points.Length,
                        session.Samples.Length);

                    var source = new ReplayTelemetrySource(session, ReplaySpeedFactor, _timeProvider);

                    await foreach (var frame in source.StreamAsync(cancellationToken).ConfigureAwait(false))
                    {
                        _store.TryApply(frame);
                    }

                    _logger.LogInformation(
                        "Replay of {Label} finished at {Time}: {Applied} applied, {Rejected} rejected.",
                        session.Label,
                        _clock.UtcNow,
                        _store.AppliedCount,
                        _store.RejectedCount);
                    break;

                case HttpFailure failure:
                    FailureReason = $"OpenF1 returned {failure.StatusCode}: {failure.Reason}";
                    _logger.LogWarning("Session {Key} failed to load: {Reason}", request.SessionKey, FailureReason);
                    break;

                case RateLimited limited:
                    FailureReason = $"OpenF1 rate limit hit; retry in {limited.RetryAfterSeconds}s";
                    _logger.LogWarning("Session {Key} was rate limited.", request.SessionKey);
                    break;

                case null:
                    FailureReason = "OpenF1 returned no value";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Replay cancelled after {Applied} frames.", _store.AppliedCount);
        }
        catch (Exception exception)
        {
            FailureReason = "session load failed; see logs";
            _logger.LogError(exception, "Session load failed after {Applied} frames.", _store.AppliedCount);
        }
    }
}

namespace F1App.Telemetry;

/// <summary>
/// Client-side request pacer for the free tiers of the public data providers.
/// </summary>
/// <remarks>
/// A token bucket: a small burst may go out immediately, then the sustained rate is enforced. Pure
/// spacing (no burst) would make the first screen wait seconds before it can draw anything, while
/// the total request count over a session is unchanged.
/// The free OpenF1 tier allows 30 requests per minute; without a shared gate, two tabs or a
/// navigation re-entry trip the limit and the app shows nothing for the rest of the window. One gate
/// instance is registered per provider and injected into everything that calls it.
/// </remarks>
public sealed class RequestGate
{
    private readonly TimeSpan _minInterval;
    private readonly int _burst;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private double _tokens;
    private DateTimeOffset _refilledAt;
    private DateTimeOffset _blockedUntil;

    /// <param name="minInterval">Minimum spacing between sustained requests.</param>
    /// <param name="timeProvider">Clock used for pacing, so tests can substitute one.</param>
    /// <param name="burst">How many requests may be issued back to back before pacing applies.</param>
    public RequestGate(TimeSpan minInterval, TimeProvider timeProvider, int burst = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minInterval.TotalMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _minInterval = minInterval;
        _timeProvider = timeProvider;
        _burst = burst;
        _tokens = burst;
        _refilledAt = timeProvider.GetUtcNow();
    }

    /// <summary>Creates a gate sized for a requests-per-minute budget.</summary>
    /// <param name="requestsPerMinute">Sustained budget the provider allows.</param>
    /// <param name="timeProvider">Clock used for pacing.</param>
    /// <param name="burst">Requests allowed immediately.</param>
    public static RequestGate ForRequestsPerMinute(
        int requestsPerMinute,
        TimeProvider timeProvider,
        int burst = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestsPerMinute);
        return new RequestGate(TimeSpan.FromMinutes(1d / requestsPerMinute), timeProvider, burst);
    }

    /// <summary>Completes when the caller may issue a request.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = _timeProvider.GetUtcNow();

                if (now < _blockedUntil)
                {
                    wait = _blockedUntil - now;
                }
                else
                {
                    Refill(now);

                    if (_tokens >= 1d)
                    {
                        _tokens -= 1d;
                        return;
                    }

                    wait = _minInterval * (1d - _tokens);
                }
            }

            await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a server-supplied backoff (typically a <c>Retry-After</c> header) to every
    /// subsequent caller, and empties the bucket.
    /// </summary>
    public void Penalize(TimeSpan backoff)
    {
        if (backoff <= TimeSpan.Zero)
        {
            return;
        }

        lock (_gate)
        {
            var until = _timeProvider.GetUtcNow() + backoff;
            if (until > _blockedUntil)
            {
                _blockedUntil = until;
            }

            _tokens = 0d;
        }
    }

    private void Refill(DateTimeOffset now)
    {
        var elapsed = now - _refilledAt;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _tokens = Math.Min(_burst, _tokens + (elapsed.TotalMilliseconds / _minInterval.TotalMilliseconds));
        _refilledAt = now;
    }
}

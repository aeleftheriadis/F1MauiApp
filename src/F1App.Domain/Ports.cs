using System.Collections.Immutable;

namespace F1App.Domain;

/// <summary>The request reached the host and was rejected (or the transport failed).</summary>
public readonly record struct HttpFailure(int StatusCode, string Reason);

/// <summary>The client-side gate refused the request; nothing was sent.</summary>
public readonly record struct RateLimited(int RetryAfterSeconds);

/// <summary>
/// Outcome of a data-provider call.
/// </summary>
/// <remarks>
/// A C# 15 union: the success case and the two expected failure cases are one closed set, so
/// every <c>switch</c> over it is exhaustive and no caller can accidentally ignore a failure.
/// Used at the adapter boundary only - the domain itself does not model transport outcomes.
/// </remarks>
public union FetchResult<T>(T, HttpFailure, RateLimited);

/// <summary>Source of live telemetry samples for the current session.</summary>
public interface ILiveTelemetrySource
{
    /// <summary>
    /// Streams samples until cancelled. Ordering per driver is not guaranteed; the consumer
    /// resolves conflicts using <see cref="TelemetryFrame.Sequence"/>.
    /// </summary>
    IAsyncEnumerable<TelemetryFrame> StreamAsync(CancellationToken cancellationToken);
}

/// <summary>Season calendar, served from the local cache when possible.</summary>
public interface IRaceCalendarStore
{
    /// <summary>
    /// Returns the calendar for <paramref name="season"/>. When the cache is empty or
    /// <paramref name="refresh"/> is <see langword="true"/>, the provider is consulted first.
    /// </summary>
    Task<FetchResult<ImmutableArray<RaceWeekend>>> GetSeasonAsync(
        int season,
        bool refresh,
        CancellationToken cancellationToken);
}

/// <summary>Ambient time, abstracted so phase logic is testable.</summary>
public interface IClock
{
    /// <summary>Current instant in UTC. Never local time: phase logic compares against the calendar.</summary>
    DateTimeOffset UtcNow { get; }
}

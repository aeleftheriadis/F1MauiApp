using System.Runtime.CompilerServices;
using F1App.Domain;

namespace F1App.Telemetry.Replay;

/// <summary>
/// Replays a recorded window, paced by the samples' own timestamps.
/// </summary>
/// <remarks>
/// The port is called "live" because the consumer cannot tell the difference: same frame type, same
/// ordering rules, same store. That is what makes this a real replay rather than a mock - every
/// coordinate and every speed here came off the wire.
/// </remarks>
public sealed class ReplayTelemetrySource : ILiveTelemetrySource
{
    private readonly ReplaySession _session;
    private readonly double _speedFactor;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a source over a loaded session window.</summary>
    /// <param name="session">Recorded window to play.</param>
    /// <param name="speedFactor">Playback rate; 2 plays a thirty-second window in fifteen seconds.</param>
    /// <param name="timeProvider">Clock used for pacing, so tests can substitute one.</param>
    public ReplayTelemetrySource(ReplaySession session, double speedFactor, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speedFactor);

        if (session.Samples.IsEmpty)
        {
            throw new ArgumentException("Replay contains no samples.", nameof(session));
        }

        _session = session;
        _speedFactor = speedFactor;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TelemetryFrame> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var samples = _session.Samples;
        var origin = samples[0].Time;
        var wallStart = _timeProvider.GetUtcNow();
        long sequence = 0;

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            var due = wallStart + (sample.Time - origin) / _speedFactor;
            var wait = due - _timeProvider.GetUtcNow();

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            yield return new TelemetryFrame(
                DriverNumber: sample.DriverNumber,
                Sequence: ++sequence,
                Timestamp: sample.Time,
                SpeedKph: sample.SpeedKph,
                Gear: sample.Gear,
                ThrottlePercent: sample.ThrottlePercent,
                Braking: sample.Braking,
                Rpm: 0,
                Drs: sample.Drs,
                X: sample.X,
                Y: sample.Y);
        }
    }
}

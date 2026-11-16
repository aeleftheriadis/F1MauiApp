using F1App.Domain;

namespace F1App.Telemetry;

/// <summary>
/// Latest-value store for live telemetry: one slot per driver, newest sample wins.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately is <b>not</b> a queue. A queue drained at a fixed render cadence accumulates
/// latency without bound whenever the producer outruns the consumer - which, for a feed that
/// emits faster than 60 Hz across 20 cars, is always. A caller that renders at 30 Hz reads the
/// store, gets the newest value per driver, and is by construction at most one frame behind.
/// </para>
/// <para>
/// Concurrency contract: many writers (network loop) and many readers (UI thread) are allowed.
/// All access is serialised by a single monitor. A read copies at most
/// <see cref="MaxDriverNumber"/> structs, which is measured in single-digit microseconds - three
/// orders of magnitude below a 16 ms frame budget - so contention is not a concern and the
/// locking is obviously correct rather than clever.
/// </para>
/// </remarks>
public sealed class TelemetrySnapshotStore
{
    /// <summary>Exclusive upper bound on driver numbers accepted by the store.</summary>
    public const int MaxDriverNumber = 100;

    private readonly TelemetryFrame[] _frames = new TelemetryFrame[MaxDriverNumber];
    private readonly long[] _lastSequence = new long[MaxDriverNumber];
    private readonly Lock _gate = new();
    private long _applied;
    private long _rejected;

    /// <summary>Samples accepted into the store.</summary>
    public long AppliedCount
    {
        get
        {
            lock (_gate)
            {
                return _applied;
            }
        }
    }

    /// <summary>
    /// Samples refused because they were older than (or equal to) the value already stored for
    /// that driver, or because the driver number was out of range.
    /// </summary>
    public long RejectedCount
    {
        get
        {
            lock (_gate)
            {
                return _rejected;
            }
        }
    }

    /// <summary>
    /// Stores <paramref name="frame"/> when it is newer than the sample already held for that car.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the store changed; <see langword="false"/> for a stale,
    /// duplicate or out-of-range sample.
    /// </returns>
    public bool TryApply(in TelemetryFrame frame)
    {
        // Driver numbers are 1-based; casting the decrement to uint also rejects 0.
        if ((uint)(frame.DriverNumber - 1) >= MaxDriverNumber - 1)
        {
            lock (_gate)
            {
                _rejected++;
            }

            return false;
        }

        lock (_gate)
        {
            // Sequence is the provider's monotonic message id, so this also discards the
            // out-of-order arrivals that a last-value topic is allowed to produce.
            if (frame.Sequence <= _lastSequence[frame.DriverNumber])
            {
                _rejected++;
                return false;
            }

            _lastSequence[frame.DriverNumber] = frame.Sequence;
            _frames[frame.DriverNumber] = frame;
            _applied++;
            return true;
        }
    }

    /// <summary>Copies the current sample per driver, ordered by driver number.</summary>
    /// <returns>The number of samples written to <paramref name="destination"/>.</returns>
    public int ReadInto(Span<TelemetryFrame> destination)
    {
        lock (_gate)
        {
            var count = 0;
            for (var driver = 0; driver < MaxDriverNumber && count < destination.Length; driver++)
            {
                if (_lastSequence[driver] == 0)
                {
                    continue;
                }

                destination[count] = _frames[driver];
                count++;
            }

            return count;
        }
    }

    /// <summary>Forgets every stored sample. Used when a session changes.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_lastSequence);
            Array.Clear(_frames);
        }
    }
}

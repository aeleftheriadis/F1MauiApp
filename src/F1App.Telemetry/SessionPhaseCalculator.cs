using F1App.Domain;

namespace F1App.Telemetry;

/// <summary>Maps a clock reading onto the closed <see cref="SessionPhase"/> hierarchy.</summary>
public static class SessionPhaseCalculator
{
    /// <summary>
    /// Returns the phase for <paramref name="now"/> given the scheduled session window.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="endsAt"/> precedes <paramref name="startsAt"/>.</exception>
    public static SessionPhase Evaluate(DateTimeOffset now, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (endsAt < startsAt)
        {
            throw new ArgumentException("Session end precedes its start.", nameof(endsAt));
        }

        return now < startsAt
            ? new Domain.NotStarted()
            : now < endsAt
                ? new Domain.Live(startsAt)
                : new Domain.Finished(endsAt);
    }
}

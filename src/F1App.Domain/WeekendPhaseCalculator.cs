namespace F1App.Domain;

/// <summary>Derives the weekend phase from a published timetable.</summary>
public static class WeekendPhaseCalculator
{
    /// <summary>
    /// How long OpenF1 keeps a session's data out of the anonymous tier after it ends.
    /// </summary>
    /// <remarks>
    /// Documented as a live window around the session rather than a fixed clock; thirty minutes is the
    /// working assumption until it is confirmed on a real race day.
    /// </remarks>
    public static readonly TimeSpan PublicationDelay = TimeSpan.FromMinutes(30);

    /// <summary>Phase is "session day" rather than "pre-weekend" inside this window.</summary>
    public static readonly TimeSpan SessionDayWindow = TimeSpan.FromHours(24);

    /// <summary>Computes the phase for <paramref name="now"/>.</summary>
    public static WeekendPhase Evaluate(
        WeekendSchedule schedule,
        DateTimeOffset now,
        TimeSpan? resultsDelay = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (schedule.Sessions.IsEmpty)
        {
            // A meeting without sessions is a data problem, not a UI problem: report it as over.
            return new PostWeekend(schedule);
        }

        var sessions = schedule.Sessions;

        foreach (var session in sessions)
        {
            if (now >= session.Start && now < session.End)
            {
                return new InSession(schedule, session);
            }
        }

        var delay = resultsDelay ?? PublicationDelay;
        var last = sessions[^1];

        if (now >= last.End)
        {
            var expectedBy = last.End + delay;
            return now < expectedBy
                ? new ResultsPending(schedule, last, expectedBy)
                : new PostWeekend(schedule);
        }

        var next = sessions[0];
        foreach (var session in sessions)
        {
            if (session.Start > now)
            {
                next = session;
                break;
            }
        }

        var untilNext = next.Start - now;

        return untilNext <= SessionDayWindow
            ? new SessionDay(schedule, next, untilNext)
            : new PreWeekend(schedule, sessions[0].Start - now);
    }
}

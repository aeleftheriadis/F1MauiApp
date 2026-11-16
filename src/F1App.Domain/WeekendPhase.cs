using System.Collections.Immutable;

namespace F1App.Domain;

/// <summary>One session on a weekend timetable.</summary>
public sealed record SessionSlot(int SessionKey, string Name, string Type, DateTimeOffset Start, DateTimeOffset End);

/// <summary>A meeting and every session it contains, in running order.</summary>
public sealed record WeekendSchedule(
    int MeetingKey,
    string MeetingName,
    Circuit Circuit,
    ImmutableArray<SessionSlot> Sessions);

/// <summary>
/// Where a race weekend is right now.
/// </summary>
/// <remarks>
/// Another closed hierarchy: the home screen switches on it, so a new state cannot fall through to a
/// blank page. Every value here is derived from the published timetable - nothing is inferred from
/// "it is usually a Sunday".
/// </remarks>
public closed record class WeekendPhase;

/// <summary>More than a day before the first session.</summary>
public sealed record class PreWeekend(WeekendSchedule Schedule, TimeSpan UntilFirstSession) : WeekendPhase;

/// <summary>Race weekend, between or before sessions.</summary>
public sealed record class SessionDay(WeekendSchedule Schedule, SessionSlot Next, TimeSpan UntilNext) : WeekendPhase;

/// <summary>A session is running.</summary>
public sealed record class InSession(WeekendSchedule Schedule, SessionSlot Session) : WeekendPhase;

/// <summary>The session has ended and its data is not published yet.</summary>
public sealed record class ResultsPending(WeekendSchedule Schedule, SessionSlot Session, DateTimeOffset ExpectedBy) : WeekendPhase;

/// <summary>Everything finished.</summary>
public sealed record class PostWeekend(WeekendSchedule Schedule) : WeekendPhase;

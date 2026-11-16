namespace F1App.Domain;

/// <summary>
/// Lifecycle state of a timing session.
/// </summary>
/// <remarks>
/// Declared as a closed hierarchy (C# 15): every direct descendant lives in this assembly, so a
/// <c>switch</c> expression covering all of them is exhaustive without a discard arm. Adding a
/// new phase therefore becomes a compile error at every consumer instead of a silent fallthrough.
/// </remarks>
public closed record class SessionPhase;

/// <summary>The session has not started yet.</summary>
public sealed record class NotStarted : SessionPhase;

/// <summary>The session is running; <paramref name="StartedAt"/> is the scheduled start time.</summary>
public sealed record class Live(DateTimeOffset StartedAt) : SessionPhase;

/// <summary>The session has ended; <paramref name="EndedAt"/> is the scheduled end time.</summary>
public sealed record class Finished(DateTimeOffset EndedAt) : SessionPhase;

namespace F1App.Services;

/// <summary>The real clock; the only place in the app that reads ambient time.</summary>
public sealed class SystemClock : Domain.IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

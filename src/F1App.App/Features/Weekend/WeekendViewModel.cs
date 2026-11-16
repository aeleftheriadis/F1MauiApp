using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Telemetry.Weekend;

namespace F1App.Features.Weekend;

/// <summary>One row of the weekend timetable.</summary>
public sealed partial class SessionRow : ObservableObject
{
    public SessionRow(string name, string type, DateTimeOffset start, DateTimeOffset end)
    {
        Name = name;
        Type = type;
        Start = start;
        End = end;
        When = start.ToLocalTime().ToString("ddd dd MMM · HH:mm", CultureInfo.CurrentCulture) +
               $"–{end.ToLocalTime():HH:mm}";
    }

    public string Name { get; }

    public string Type { get; }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    /// <summary>Pre-formatted so the cell needs no converter.</summary>
    public string When { get; }

    [ObservableProperty]
    public partial bool IsNext { get; set; }

    [ObservableProperty]
    public partial string Marker { get; set; } = string.Empty;
}

/// <summary>Drives the weekend hub: which meeting, which session, how long until it.</summary>
public sealed partial class WeekendViewModel : ObservableObject
{
    private readonly OpenF1WeekendLoader _loader;
    private readonly TimeProvider _timeProvider;
    private WeekendSchedule? _schedule;

    public WeekendViewModel(OpenF1WeekendLoader loader, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _loader = loader;
        _timeProvider = timeProvider;

        Headline = "race weekend";
        PhaseLabel = "loading timetable";
        Countdown = string.Empty;
        Status = string.Empty;
    }

    /// <summary>Timetable rows in running order.</summary>
    public ObservableCollection<SessionRow> Sessions { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string PhaseLabel { get; set; }

    [ObservableProperty]
    public partial string Countdown { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Loads the timetable when needed, then recomputes the phase against the clock.</summary>
    public async Task RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        if (_schedule is null || force)
        {
            IsBusy = true;
            Status = "loading timetable from OpenF1";

            var year = _timeProvider.GetUtcNow().Year;
            var loaded = await _loader.LoadAsync(year, cancellationToken).ConfigureAwait(true);

            switch (loaded)
            {
                case WeekendSchedule schedule:
                    _schedule = schedule;
                    Headline = schedule.MeetingName;
                    Sessions.Clear();

                    foreach (var session in schedule.Sessions)
                    {
                        Sessions.Add(new SessionRow(session.Name, session.Type, session.Start, session.End));
                    }

                    Status = $"{schedule.Circuit.Name}, {schedule.Circuit.Country} · times shown in your timezone";
                    break;

                case HttpFailure failure:
                    Status = $"no timetable: OpenF1 returned {failure.StatusCode}: {failure.Reason}";
                    IsBusy = false;
                    return;

                case RateLimited limited:
                    Status = $"rate limited; retry in {limited.RetryAfterSeconds}s";
                    IsBusy = false;
                    return;
            }

            IsBusy = false;
        }

        Recompute();
    }

    /// <summary>Recomputes the phase without touching the network. Safe to call on a timer.</summary>
    public void Recompute()
    {
        if (_schedule is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var phase = WeekendPhaseCalculator.Evaluate(_schedule, now);

        foreach (var row in Sessions)
        {
            row.IsNext = false;
            row.Marker = string.Empty;
        }

        switch (phase)
        {
            case PreWeekend pre:
                PhaseLabel = "before the weekend";
                Countdown = $"first session in {Describe(pre.UntilFirstSession)}";
                break;

            case SessionDay day:
                PhaseLabel = $"next: {day.Next.Name}";
                Countdown = $"in {Describe(day.UntilNext)}";
                Mark(day.Next.Start);
                break;

            case InSession running:
                PhaseLabel = $"{running.Session.Name} in progress";
                Countdown = "live timing is not available in this build";
                Mark(running.Session.Start);
                break;

            case ResultsPending pending:
                PhaseLabel = $"{pending.Session.Name} finished";
                Countdown = "timing data publishes shortly after the session";
                Mark(pending.Session.Start);
                break;

            case PostWeekend:
                PhaseLabel = "weekend complete";
                Countdown = string.Empty;
                break;
        }
    }

    private void Mark(DateTimeOffset start)
    {
        foreach (var row in Sessions)
        {
            if (row.Start == start)
            {
                row.IsNext = true;
                row.Marker = "NEXT";
            }
        }
    }

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1
            ? $"{(int)span.TotalDays}d {span.Hours}h"
            : $"{span.Hours}h {Math.Max(0, span.Minutes)}m";
}

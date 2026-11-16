using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry.Timing;
using F1App.Telemetry.Weekend;

namespace F1App.Features.Results;

/// <summary>A timing row, pre-formatted for the list.</summary>
public sealed record TimingDisplayRow(
    string Position,
    string Code,
    string BestLap,
    string Sector1,
    string Sector2,
    string Sector3,
    Color Sector1Color,
    Color Sector2Color,
    Color Sector3Color,
    string Gap,
    string Tyre,
    string Pits,
    Color TeamColor,
    string AccessibleText);

/// <summary>Picks a session from the current weekend and shows its published timing table.</summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly OpenF1WeekendLoader _weekendLoader;
    private readonly OpenF1SessionTimingLoader _timingLoader;
    private readonly SessionRunner _runner;
    private readonly TimeProvider _timeProvider;
    private WeekendSchedule? _schedule;
    private SessionSlot? _selected;

    public ResultsViewModel(
        OpenF1WeekendLoader weekendLoader,
        OpenF1SessionTimingLoader timingLoader,
        SessionRunner runner,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(weekendLoader);
        ArgumentNullException.ThrowIfNull(timingLoader);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _weekendLoader = weekendLoader;
        _timingLoader = timingLoader;
        _runner = runner;
        _timeProvider = timeProvider;

        Headline = "results";
        Status = "select a session";
        SelectedSession = -1;
    }

    /// <summary>Session labels for the picker.</summary>
    public ObservableCollection<string> SessionNames { get; } = [];

    /// <summary>Classified drivers, in order.</summary>
    public ObservableCollection<TimingDisplayRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int SelectedSession { get; set; }

    /// <summary>Loads the weekend once and fills the picker.</summary>
    /// <returns>The index that should be selected by default, or -1.</returns>
    public async Task<int> EnsureScheduleAsync(CancellationToken cancellationToken)
    {
        if (_schedule is not null)
        {
            return SelectedSession;
        }

        IsBusy = true;
        Status = "loading the weekend timetable";

        var year = _timeProvider.GetUtcNow().Year;
        var loaded = await _weekendLoader.LoadAsync(year, cancellationToken).ConfigureAwait(true);

        switch (loaded)
        {
            case WeekendSchedule schedule:
                _schedule = schedule;
                Headline = schedule.MeetingName;

                SessionNames.Clear();
                foreach (var slot in schedule.Sessions)
                {
                    SessionNames.Add($"{slot.Name} · {slot.Start.ToLocalTime():ddd HH:mm}");
                }

                Status = $"{schedule.Circuit.Name}, {schedule.Circuit.Country}";
                IsBusy = false;
                return DefaultIndex(schedule);
            case HttpFailure failure:
                Status = $"timetable unavailable: OpenF1 returned {failure.StatusCode}";
                IsBusy = false;
                return -1;
            case RateLimited limited:
                Status = $"rate limited; retry in {limited.RetryAfterSeconds}s";
                IsBusy = false;
                return -1;
            default:
                IsBusy = false;
                return -1;
        }
    }

    /// <summary>Loads the timing table for the session at <paramref name="index"/>.</summary>
    public async Task SelectSessionAsync(int index, CancellationToken cancellationToken)
    {
        if (_schedule is null || index < 0 || index >= _schedule.Sessions.Length)
        {
            return;
        }

        var slot = _schedule.Sessions[index];
        _selected = slot;
        IsBusy = true;
        Status = $"loading {slot.Name}";

        var loaded = await _timingLoader
            .LoadAsync(slot.SessionKey, slot.Name, slot.Start, cancellationToken)
            .ConfigureAwait(true);

        switch (loaded)
        {
            case SessionTiming timing:
                Rows.Clear();

                // Session bests are what the purple sector in a timing tower means.
                var best1 = timing.Entries.Min(static entry => entry.BestSector1 ?? TimeSpan.MaxValue);
                var best2 = timing.Entries.Min(static entry => entry.BestSector2 ?? TimeSpan.MaxValue);
                var best3 = timing.Entries.Min(static entry => entry.BestSector3 ?? TimeSpan.MaxValue);

                foreach (var entry in timing.Entries)
                {
                    Rows.Add(ToRow(entry, best1, best2, best3));
                }

                Status = timing.Entries.Length == 0
                    ? "no classified drivers"
                    : $"{timing.Entries.Length} drivers · {timing.Start.ToLocalTime():ddd dd MMM HH:mm} · published data";
                break;

            case HttpFailure failure:
                Rows.Clear();
                Status = $"timing unavailable: {failure.StatusCode}: {failure.Reason}";
                break;

            case RateLimited limited:
                Rows.Clear();
                Status = $"rate limited; retry in {limited.RetryAfterSeconds}s";
                break;
        }

        IsBusy = false;
    }

    /// <summary>Switches the app's replay to the session on screen.</summary>
    public async Task ReplaySelectedAsync(CancellationToken cancellationToken)
    {
        if (_selected is not { } slot || _schedule is null)
        {
            return;
        }

        Status = $"switching replay to {slot.Name}";
        await _runner
            .SwitchToAsync(
                new SessionRequest(slot.SessionKey, slot.Name, slot.Start.AddMinutes(10), _schedule.Circuit.Id),
                cancellationToken)
            .ConfigureAwait(true);

        Status = $"replaying {slot.Name}; the Timing and Track screens now follow this session";
    }

    private int DefaultIndex(WeekendSchedule schedule)
    {
        var now = _timeProvider.GetUtcNow();
        var index = -1;

        for (var i = 0; i < schedule.Sessions.Length; i++)
        {
            if (schedule.Sessions[i].End < now)
            {
                index = i;
            }
        }

        return index >= 0 ? index : 0;
    }

    private static TimingDisplayRow ToRow(
        DriverTiming entry,
        TimeSpan best1,
        TimeSpan best2,
        TimeSpan best3)
    {
        var position = entry.Position == 0 ? "—" : entry.Position.ToString(CultureInfo.InvariantCulture);
        var gap = RaceFormat.Gap(entry.GapToLeaderSeconds);

        var accessible =
            $"{entry.Code}, position {position}, best lap {RaceFormat.Lap(entry.BestLap)}, gap {gap}, " +
            $"{entry.Compound} tyres {entry.TyreAgeLaps} laps old, {entry.PitStops} pit stops";

        return new TimingDisplayRow(
            position,
            entry.Code,
            RaceFormat.Lap(entry.BestLap),
            RaceFormat.Sector(entry.BestSector1),
            RaceFormat.Sector(entry.BestSector2),
            RaceFormat.Sector(entry.BestSector3),
            SectorColour(entry.BestSector1, best1),
            SectorColour(entry.BestSector2, best2),
            SectorColour(entry.BestSector3, best3),
            gap,
            RaceFormat.Tyre(entry.Compound, entry.TyreAgeLaps),
            entry.PitStops.ToString(CultureInfo.InvariantCulture),
            ParseColour(entry.TeamColourHex),
            accessible);
    }

    private static Color SectorColour(TimeSpan? value, TimeSpan best) =>
        value is not null && value == best ? Color.FromArgb("#A855F7") : Color.FromArgb("#94A3B8");

    private static Color ParseColour(string hex)
    {
        try
        {
            return Color.FromArgb(hex);
        }
        catch (ArgumentException)
        {
            return Theme.Themes.Current.TextSecondary;
        }
    }
}

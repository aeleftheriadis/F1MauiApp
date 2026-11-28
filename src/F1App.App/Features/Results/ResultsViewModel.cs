using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry.Providers;
using F1App.Telemetry.Timing;
using F1App.Telemetry.Weekend;
using F1App.Theme;

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
    string AccessibleText,
    // Official points and classified status, empty when the FIA result is not published yet.
    string Official);

/// <summary>Picks a session from the current weekend and shows its published timing table.</summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly OpenF1WeekendLoader _weekendLoader;
    private readonly OpenF1SessionTimingLoader _timingLoader;
    private readonly SessionRunner _runner;
    private readonly OpenF1Client _client;
    private readonly TimeProvider _timeProvider;
    private WeekendSchedule? _schedule;
    private SessionSlot? _selected;

    public ResultsViewModel(
        OpenF1WeekendLoader weekendLoader,
        OpenF1SessionTimingLoader timingLoader,
        SessionRunner runner,
        OpenF1Client client,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(weekendLoader);
        ArgumentNullException.ThrowIfNull(timingLoader);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _weekendLoader = weekendLoader;
        _timingLoader = timingLoader;
        _runner = runner;
        _client = client;
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
                    SessionNames.Add($"{slot.Name} · {Dates.DayTime(slot.Start)}");
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

                var official = await LoadOfficialAsync(slot.SessionKey, cancellationToken).ConfigureAwait(true);
                var grid = await LoadGridAsync(slot, cancellationToken).ConfigureAwait(true);
                var passes = await LoadOvertakesAsync(slot.SessionKey, cancellationToken).ConfigureAwait(true);

                foreach (var entry in timing.Entries)
                {
                    Rows.Add(ToRow(entry, best1, best2, best3, official, grid, passes));
                }

                Status = timing.Entries.Length == 0
                    ? "no classified drivers"
                    : official.Count == 0
                        ? $"{timing.Entries.Length} drivers · {timing.Start.ToLocalTime():ddd dd MMM HH:mm} · official points not published yet"
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
                new SessionRequest(slot.SessionKey, slot.Name, slot.Start.AddMinutes(10), _schedule.Circuit.Id, _schedule.MeetingName),
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

    /// <summary>
    /// The official FIA classification, keyed by driver number. Empty when it has not been published, which is
    /// the normal answer for a few minutes after a session and the permanent answer for a live one.
    /// </summary>
    private async Task<Dictionary<int, OpenF1SessionResult>> LoadOfficialAsync(
        int sessionKey,
        CancellationToken cancellationToken)
    {
        var loaded = await _client.GetSessionResultAsync(sessionKey, cancellationToken).ConfigureAwait(true);

        return loaded is System.Collections.Immutable.ImmutableArray<OpenF1SessionResult> results
            ? results.GroupBy(static row => row.DriverNumber).ToDictionary(static g => g.Key, static g => g.First())
            : [];
    }

    /// <summary>
    /// Grid positions for a race, keyed by driver number.
    /// </summary>
    /// <remarks>
    /// The grid is published against the qualifying session, not the race (measured: the race key answers an
    /// empty list), so the sibling session is resolved from the same weekend before asking. No qualifying slot
    /// means no grid and no delta - never an invented one.
    /// </remarks>
    private async Task<Dictionary<int, int>> LoadGridAsync(
        SessionSlot slot,
        CancellationToken cancellationToken)
    {
        if (_schedule is null || !slot.Type.Contains("Race", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var qualifying = _schedule.Sessions.FirstOrDefault(
            session => session.Type.Contains("Qualifying", StringComparison.OrdinalIgnoreCase)
                       || session.Name.Contains("Qualifying", StringComparison.OrdinalIgnoreCase));

        if (qualifying is null)
        {
            return [];
        }

        var loaded = await _client
            .GetStartingGridAsync(qualifying.SessionKey, cancellationToken)
            .ConfigureAwait(true);

        if (loaded is not System.Collections.Immutable.ImmutableArray<OpenF1StartingGrid> rows)
        {
            return [];
        }

        return rows
            .Where(static row => row.DriverNumber is not null && row.Position is not null)
            .GroupBy(static row => row.DriverNumber!.Value)
            .ToDictionary(static group => group.Key, static group => group.First().Position!.Value);
    }

    /// <summary>
    /// Passes per driver, keyed by driver number, made and suffered.
    /// </summary>
    /// <remarks>
    /// The provider warns the list may be incomplete, so this is reported as "overtakes", never as "on-track
    /// passes" - a position change from a pit stop or a penalty is in the same list.
    /// </remarks>
    private async Task<Dictionary<int, OvertakeTally>> LoadOvertakesAsync(
        int sessionKey,
        CancellationToken cancellationToken)
    {
        var loaded = await _client.GetOvertakesAsync(sessionKey, cancellationToken).ConfigureAwait(true);

        if (loaded is not System.Collections.Immutable.ImmutableArray<OpenF1Overtake> passes)
        {
            return [];
        }

        var made = new Dictionary<int, int>();
        var lost = new Dictionary<int, int>();

        foreach (var pass in passes)
        {
            if (pass.OvertakingDriverNumber is { } attacker)
            {
                made[attacker] = made.GetValueOrDefault(attacker) + 1;
            }

            if (pass.OvertakenDriverNumber is { } victim)
            {
                lost[victim] = lost.GetValueOrDefault(victim) + 1;
            }
        }

        return made.Keys
            .Union(lost.Keys)
            .ToDictionary(
                static driver => driver,
                driver => new OvertakeTally(made.GetValueOrDefault(driver), lost.GetValueOrDefault(driver)));
    }

    private static string DescribeOfficial(OpenF1SessionResult? official)
    {
        if (official is null)
        {
            return string.Empty;
        }

        var status = official switch
        {
            { Disqualified: true } => "DSQ",
            { DidNotStart: true } => "DNS",
            { DidNotFinish: true } => "DNF",
            _ => string.Empty,
        };

        var points = official.Points is { } value and > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0.#} pts")
            : string.Empty;

        return (points, status) switch
        {
            ("", "") => string.Empty,
            ("", _) => status,
            (_, "") => points,
            _ => $"{points} · {status}",
        };
    }

    private static TimingDisplayRow ToRow(
        DriverTiming entry,
        TimeSpan best1,
        TimeSpan best2,
        TimeSpan best3,
        Dictionary<int, OpenF1SessionResult> official,
        Dictionary<int, int> grid,
        Dictionary<int, OvertakeTally> passes)
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
            accessible,
            WithOvertakes(
                WithPlaces(
                    DescribeOfficial(official.GetValueOrDefault(entry.DriverNumber)),
                    grid.GetValueOrDefault(entry.DriverNumber),
                    entry.Position),
                passes.GetValueOrDefault(entry.DriverNumber)));
    }

    /// <summary>Adds "+2 places" to the official text when both the grid slot and the finish are known.</summary>
    private static string WithPlaces(string official, int gridPosition, int finishPosition)
    {
        if (gridPosition <= 0 || finishPosition <= 0)
        {
            return official;
        }

        var places = new PlacesGained(gridPosition, finishPosition).Describe();
        return official.Length == 0 ? $"places {places}" : $"{official} · places {places}";
    }

    /// <summary>Appends the pass tally when the provider published any.</summary>
    private static string WithOvertakes(string official, OvertakeTally passes)
    {
        var described = passes.Describe();

        if (described.Length == 0)
        {
            return official;
        }

        return official.Length == 0 ? described : $"{official} · {described}";
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

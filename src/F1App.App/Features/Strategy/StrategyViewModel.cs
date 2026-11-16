using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Telemetry.Strategy;
using F1App.Telemetry.Weekend;

namespace F1App.Features.Strategy;

/// <summary>One stint, sized and coloured for the strip.</summary>
public sealed record StintBlock(string Compound, double Width, Color Colour, string Label, string Description);

/// <summary>One driver's row on the strategy board.</summary>
public sealed record StrategyRow(string Code, IReadOnlyList<StintBlock> Blocks, string Summary, string AccessibleText);

/// <summary>Tyre strategy for a chosen session.</summary>
public sealed partial class StrategyViewModel : ObservableObject
{
    private const double PixelsPerLap = 5d;

    private readonly OpenF1WeekendLoader _weekendLoader;
    private readonly OpenF1StrategyLoader _strategyLoader;
    private WeekendSchedule? _schedule;

    public StrategyViewModel(OpenF1WeekendLoader weekendLoader, OpenF1StrategyLoader strategyLoader)
    {
        ArgumentNullException.ThrowIfNull(weekendLoader);
        ArgumentNullException.ThrowIfNull(strategyLoader);

        _weekendLoader = weekendLoader;
        _strategyLoader = strategyLoader;

        Headline = "strategy";
        Status = "select a session";
    }

    /// <summary>Session labels for the picker.</summary>
    public ObservableCollection<string> SessionNames { get; } = [];

    /// <summary>One row per driver, in code order.</summary>
    public ObservableCollection<StrategyRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Loads the weekend once and returns the index to select.</summary>
    public async Task<int> EnsureScheduleAsync(CancellationToken cancellationToken)
    {
        if (_schedule is not null)
        {
            return 0;
        }

        Status = "loading the weekend timetable";
        var year = DateTimeOffset.UtcNow.Year;
        var loaded = await _weekendLoader.LoadAsync(year, cancellationToken).ConfigureAwait(true);

        if (loaded is not WeekendSchedule schedule)
        {
            Status = "timetable unavailable";
            return -1;
        }

        _schedule = schedule;
        Headline = schedule.MeetingName;
        SessionNames.Clear();

        foreach (var slot in schedule.Sessions)
        {
            SessionNames.Add(slot.Name);
        }

        Status = "pick a session";
        return 0;
    }

    /// <summary>Loads the strategy of the session at <paramref name="index"/>.</summary>
    public async Task SelectAsync(int index, CancellationToken cancellationToken)
    {
        if (_schedule is null || index < 0 || index >= _schedule.Sessions.Length)
        {
            return;
        }

        var slot = _schedule.Sessions[index];
        IsBusy = true;
        Status = $"loading {slot.Name}";

        var loaded = await _strategyLoader
            .LoadAsync(slot.SessionKey, slot.Name, cancellationToken)
            .ConfigureAwait(true);

        switch (loaded)
        {
            case StrategyTimeline timeline:
                Rows.Clear();

                foreach (var driver in timeline.Drivers)
                {
                    Rows.Add(ToRow(driver));
                }

                Status = $"{timeline.Drivers.Length} drivers · {timeline.LastLap} laps";
                break;

            case HttpFailure failure:
                Rows.Clear();
                Status = $"strategy unavailable: {failure.StatusCode}: {failure.Reason}";
                break;

            case RateLimited limited:
                Rows.Clear();
                Status = $"rate limited; retry in {limited.RetryAfterSeconds}s";
                break;
        }

        IsBusy = false;
    }

    /// <summary>Pirelli's compound colours, so the strip reads the way a fan expects.</summary>
    private static Color ColourOf(string compound) => compound.ToUpperInvariant() switch
    {
        "SOFT" => Color.FromArgb("#EF4444"),
        "MEDIUM" => Color.FromArgb("#FBBF24"),
        "HARD" => Color.FromArgb("#E5E7EB"),
        "INTERMEDIATE" => Color.FromArgb("#22C55E"),
        "WET" => Color.FromArgb("#3B82F6"),
        _ => Color.FromArgb("#6B7280"),
    };

    private static StrategyRow ToRow(DriverStrategy driver)
    {
        var blocks = new List<StintBlock>(driver.Stints.Length);
        var summary = new List<string>(driver.Stints.Length);

        foreach (var stint in driver.Stints)
        {
            var laps = Math.Max(1, stint.LapEnd - stint.LapStart + 1);
            blocks.Add(new StintBlock(
                stint.Compound,
                laps * PixelsPerLap,
                ColourOf(stint.Compound),
                stint.Compound.Length > 0 ? stint.Compound[..1] : "?",
                $"{stint.Compound}, laps {stint.LapStart} to {stint.LapEnd}"));
            summary.Add($"{stint.Compound} {stint.LapStart}-{stint.LapEnd}");
        }

        return new StrategyRow(
            driver.Code,
            blocks,
            string.Join(" · ", summary),
            $"{driver.Code}: {string.Join(", then ", summary)}");
    }
}

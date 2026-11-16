using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry;
using F1App.Telemetry.Timing;
using F1App.Theme;

namespace F1App.Features.PitWall;

/// <summary>One car on the pit wall.</summary>
/// <remarks>
/// Speed and gear are observable rather than positional: they change at 15 Hz while the classification
/// does not, so replacing the row would rebuild its view twenty times a second for two changed characters.
/// </remarks>
public sealed partial class PitWallRow : ObservableObject
{
    public PitWallRow(
        int driverNumber,
        string position,
        string driver,
        string gap,
        string interval,
        string tyre,
        string tyreAge,
        Color tyreColor,
        Color sector1,
        Color sector2,
        Color sector3,
        Color teamColor,
        string accessibleText)
    {
        DriverNumber = driverNumber;
        Position = position;
        Driver = driver;
        Gap = gap;
        Interval = interval;
        Tyre = tyre;
        TyreAge = tyreAge;
        TyreColor = tyreColor;
        Sector1 = sector1;
        Sector2 = sector2;
        Sector3 = sector3;
        TeamColor = teamColor;
        AccessibleText = accessibleText;
        Speed = "—";
        Gear = "—";
    }

    public int DriverNumber { get; }

    public string Position { get; }

    public string Driver { get; }

    public string Gap { get; }

    public string Interval { get; }

    public string Tyre { get; }

    public string TyreAge { get; }

    public Color TyreColor { get; }

    public Color Sector1 { get; }

    public Color Sector2 { get; }

    public Color Sector3 { get; }

    public Color TeamColor { get; }

    public string AccessibleText { get; }

    [ObservableProperty]
    public partial string Speed { get; set; }

    [ObservableProperty]
    public partial string Gear { get; set; }
}

/// <summary>
/// The pit-wall screen: classification from the published session timing, live speed and gear from the
/// replay that is currently running.
/// </summary>
/// <remarks>
/// Two sources, one row: the timing loader gives positions, gaps, sectors and tyres for the session, and
/// the snapshot store gives what each car is doing right now. Nothing is interpolated between them - a
/// row shows the published classification and the newest telemetry sample, and the two are labelled as
/// such in the status line.
/// </remarks>
public sealed partial class PitWallViewModel : ObservableObject
{
    private const double SectorGreenThresholdSeconds = 0.3;

    private readonly SessionRunner _runner;
    private readonly OpenF1SessionTimingLoader _timing;
    private readonly TelemetrySnapshotStore _store;
    private readonly IClock _clock;
    private readonly TelemetryFrame[] _frames = new TelemetryFrame[TelemetrySnapshotStore.MaxDriverNumber];
    private SessionTiming? _timingData;

    public PitWallViewModel(
        SessionRunner runner,
        OpenF1SessionTimingLoader timing,
        TelemetrySnapshotStore store,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _runner = runner;
        _timing = timing;
        _store = store;
        _clock = clock;

        Headline = "pit wall";
        Subhead = "waiting for the replay";
        Status = "no session loaded";
        FocusDriver = "—";
        FocusDetail = string.Empty;
        LiveSpeed = "—";
        LiveGear = "—";
        LiveDrs = "—";
        LiveThrottle = "—";
        Cars = "—";
        Fastest = "—";
        DrsOpen = "—";
        Window = "—";
    }

    /// <summary>Every car, in published classification order.</summary>
    public ObservableCollection<PitWallRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Subhead { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial string Cars { get; set; }

    [ObservableProperty]
    public partial string Fastest { get; set; }

    [ObservableProperty]
    public partial string DrsOpen { get; set; }

    [ObservableProperty]
    public partial string Window { get; set; }

    [ObservableProperty]
    public partial string FocusDriver { get; set; }

    [ObservableProperty]
    public partial string FocusDetail { get; set; }

    [ObservableProperty]
    public partial Color FocusColor { get; set; } = Colors.Transparent;

    /// <summary>Car number of the focused row, so the focus pane can trace it.</summary>
    [ObservableProperty]
    public partial int FocusDriverNumber { get; set; }

    [ObservableProperty]
    public partial string LiveSpeed { get; set; }

    [ObservableProperty]
    public partial string LiveGear { get; set; }

    [ObservableProperty]
    public partial string LiveDrs { get; set; }

    [ObservableProperty]
    public partial string LiveThrottle { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    /// <summary>Loads the classification for the session the runner is replaying.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoaded || _runner.SessionKey == 0)
        {
            return;
        }

        IsBusy = true;
        Headline = _runner.SessionLabel;
        Status = "loading classification";

        var loaded = await _timing
            .LoadAsync(_runner.SessionKey, _runner.SessionLabel, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(true);

        switch (loaded)
        {
            case SessionTiming timing:
                _timingData = timing;
                BuildRows(timing);
                IsLoaded = true;
                Status = $"{timing.Entries.Length} cars · classification from OpenF1 · speeds live from the replay";
                break;

            case HttpFailure failure:
                Status = $"classification unavailable ({failure.StatusCode}); showing live order only";
                BuildRowsFromStore();
                IsLoaded = true;
                break;

            case RateLimited limited:
                Status = $"rate limited, retry in {limited.RetryAfterSeconds}s";
                break;
        }

        IsBusy = false;
        Subhead = _runner.SessionLabel;
    }

    /// <summary>Refreshes the live columns. Called from the page tick.</summary>
    public void Tick()
    {
        var count = _store.ReadInto(_frames);

        if (count == 0)
        {
            return;
        }

        var fastest = 0;
        var drsOpen = 0;
        var speedByDriver = new Dictionary<int, (short Speed, byte Gear, string Drs)>();

        for (var i = 0; i < count; i++)
        {
            ref readonly var frame = ref _frames[i];
            fastest = Math.Max(fastest, frame.SpeedKph);

            if (frame.Drs == DrsState.Active)
            {
                drsOpen++;
            }

            speedByDriver[frame.DriverNumber] = (frame.SpeedKph, frame.Gear, frame.Drs == DrsState.Active ? "DRS" : "—");
        }

        Cars = count.ToString(CultureInfo.InvariantCulture);
        Fastest = string.Create(CultureInfo.InvariantCulture, $"{fastest} kph");
        DrsOpen = drsOpen.ToString(CultureInfo.InvariantCulture);
        Window = _runner.Loaded is { } session
            ? string.Create(CultureInfo.InvariantCulture, $"{(session.To - session.From).TotalSeconds:F0}s")
            : "—";

        foreach (var row in Rows)
        {
            if (speedByDriver.TryGetValue(row.DriverNumber, out var live))
            {
                row.Speed = live.Speed.ToString(CultureInfo.InvariantCulture);
                row.Gear = live.Gear.ToString(CultureInfo.InvariantCulture);
            }
        }

        UpdateFocus(speedByDriver);
    }

    private void BuildRows(SessionTiming timing)
    {
        Rows.Clear();

        var best1 = Min(timing.Entries, static entry => entry.BestSector1);
        var best2 = Min(timing.Entries, static entry => entry.BestSector2);
        var best3 = Min(timing.Entries, static entry => entry.BestSector3);

        double? previousGap = null;

        foreach (var entry in timing.Entries)
        {
            var interval = entry.GapToLeaderSeconds is { } gap && previousGap is { } previous
                ? RaceFormat.Gap(gap - previous)
                : "—";

            if (entry.GapToLeaderSeconds is { } current)
            {
                previousGap = current;
            }

            Rows.Add(new PitWallRow(
                driverNumber: entry.DriverNumber,
                position: entry.Position == 0 ? "—" : entry.Position.ToString(CultureInfo.InvariantCulture),
                driver: entry.Code,
                gap: RaceFormat.Gap(entry.GapToLeaderSeconds),
                interval: interval,
                tyre: entry.Compound.Length > 0 ? entry.Compound[..1] : "?",
                tyreAge: string.Create(CultureInfo.InvariantCulture, $"{entry.TyreAgeLaps}L"),
                tyreColor: TyreColour(entry.Compound),
                sector1: SectorColour(entry.BestSector1, best1),
                sector2: SectorColour(entry.BestSector2, best2),
                sector3: SectorColour(entry.BestSector3, best3),
                teamColor: Parse(entry.TeamColourHex),
                accessibleText:
                    $"{entry.Code}, position {entry.Position}, gap {RaceFormat.Gap(entry.GapToLeaderSeconds)}, " +
                    $"{entry.Compound} tyres {entry.TyreAgeLaps} laps old"));
        }
    }

    private void BuildRowsFromStore()
    {
        Rows.Clear();
        var count = _store.ReadInto(_frames);

        for (var i = 0; i < count; i++)
        {
            ref readonly var frame = ref _frames[i];
            var identity = _runner.Loaded?.DriverFor(frame.DriverNumber);

            Rows.Add(new PitWallRow(
                driverNumber: frame.DriverNumber,
                position: "—",
                driver: identity?.Code ?? $"CAR {frame.DriverNumber}",
                gap: "—",
                interval: "—",
                tyre: "?",
                tyreAge: "—",
                tyreColor: Themes.Current.SurfaceMuted,
                sector1: Themes.Current.SurfaceMuted,
                sector2: Themes.Current.SurfaceMuted,
                sector3: Themes.Current.SurfaceMuted,
                teamColor: identity is null ? Themes.Current.TextSecondary : Parse(identity.TeamColorHex),
                accessibleText: $"{identity?.Code ?? frame.DriverNumber.ToString()}, live only"));
        }
    }

    private void UpdateFocus(Dictionary<int, (short Speed, byte Gear, string Drs)> live)
    {
        var focused = Rows.FirstOrDefault();

        if (focused is null)
        {
            return;
        }

        FocusDriver = focused.Driver;
        FocusDriverNumber = focused.DriverNumber;
        FocusColor = focused.TeamColor;
        FocusDetail = $"{focused.Position} · {focused.Gap}";

        if (live.TryGetValue(focused.DriverNumber, out var sample))
        {
            LiveSpeed = sample.Speed.ToString(CultureInfo.InvariantCulture);
            LiveGear = sample.Gear.ToString(CultureInfo.InvariantCulture);
            LiveDrs = sample.Drs;
        }
    }


    private static TimeSpan Min(
        System.Collections.Immutable.ImmutableArray<DriverTiming> entries,
        Func<DriverTiming, TimeSpan?> selector)
    {
        var best = TimeSpan.MaxValue;

        foreach (var entry in entries)
        {
            if (selector(entry) is { } value && value < best)
            {
                best = value;
            }
        }

        return best;
    }

    private static Color SectorColour(TimeSpan? value, TimeSpan best) => value switch
    {
        null => Themes.Current.SurfaceMuted,
        { } time when time == best => Themes.Current.TelePurple,
        { } time when (time - best).TotalSeconds <= SectorGreenThresholdSeconds => Themes.Current.TeleLime,
        _ => Themes.Current.TeleYellow,
    };

    private static Color TyreColour(string compound) => compound.ToUpperInvariant() switch
    {
        "SOFT" => Themes.Current.Live,
        "MEDIUM" => Themes.Current.TeleYellow,
        "HARD" => Themes.Current.TextPrimary,
        "INTERMEDIATE" => Themes.Current.TeleLime,
        "WET" => Themes.Current.TeleCyan,
        _ => Themes.Current.SurfaceMuted,
    };

    private static Color Parse(string hex)
    {
        try
        {
            return Color.FromArgb(hex);
        }
        catch (ArgumentException)
        {
            return Themes.Current.TextSecondary;
        }
    }
}

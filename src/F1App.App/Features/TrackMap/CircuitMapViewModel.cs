using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry.Circuits;
using F1App.Telemetry.Facts;
using F1App.Telemetry.Providers;
using F1App.Telemetry.Replay;

namespace F1App.Features.TrackMap;

/// <summary>Browses the season's circuits and draws each one from its own recorded GPS lap.</summary>
public sealed partial class CircuitMapViewModel : ObservableObject
{
    /// <summary>Cars tried in order when tracing a circuit; the second covers a retired first pick.</summary>
    private static readonly int[] OutlineDrivers = [44, 1];

    private readonly OpenF1CircuitCatalogue _catalogue;
    private readonly OpenF1TrackOutlineLoader _outlines;
    private readonly JolpicaCircuitWinners _winners;
    private readonly JolpicaClient _calendar;
    private readonly SessionRunner _runner;
    private readonly TimeProvider _timeProvider;
    private ImmutableArray<CircuitEntry> _entries = [];

    public CircuitMapViewModel(
        OpenF1CircuitCatalogue catalogue,
        OpenF1TrackOutlineLoader outlines,
        JolpicaCircuitWinners winners,
        JolpicaClient calendar,
        SessionRunner runner,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(outlines);
        ArgumentNullException.ThrowIfNull(winners);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _catalogue = catalogue;
        _outlines = outlines;
        _winners = winners;
        _calendar = calendar;
        _runner = runner;
        _timeProvider = timeProvider;

        Headline = "circuits";
        Facts = string.Empty;
        Winners = string.Empty;
        Status = "loading the season";
        SelectedIndex = -1;
    }

    /// <summary>Circuit labels for the picker.</summary>
    public ObservableCollection<string> Circuits { get; } = [];

    /// <summary>Geometry of the selected circuit, or <see langword="null"/> while none is loaded.</summary>
    [ObservableProperty]
    public partial TrackOutline? Outline { get; set; }

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Facts { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    /// <summary>Most successful drivers at this circuit, newest season in brackets.</summary>
    [ObservableProperty]
    public partial string Winners { get; set; }

    /// <summary>True when the map on screen is the session the app is replaying.</summary>
    [ObservableProperty]
    public partial bool IsReplayCircuit { get; set; }

    /// <summary>Identity of a car in the replayed session, or <see langword="null"/> when it is not loaded.</summary>
    public Driver? LoadedDriver(int driverNumber) =>
        IsReplayCircuit ? _runner.Loaded?.DriverFor(driverNumber) : null;

    /// <summary>Loads the season list and notes whether the next season is published yet.</summary>
    /// <returns>The index to select by default.</returns>
    public async Task<int> LoadSeasonAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        var year = _timeProvider.GetUtcNow().Year;

        var loaded = await _catalogue.LoadSeasonAsync(year, cancellationToken).ConfigureAwait(true);

        switch (loaded)
        {
            case ImmutableArray<CircuitEntry> entries:
                _entries = entries;
                Circuits.Clear();

                foreach (var entry in entries)
                {
                    Circuits.Add($"{entry.Circuit.ShortName} · {entry.Circuit.Country}");
                }

                Headline = string.Create(CultureInfo.InvariantCulture, $"{year} season · {entries.Length} circuits");
                await NoteCalendarAgreementAsync(year, entries, cancellationToken).ConfigureAwait(true);
                Status = "pick a circuit";
                IsBusy = false;

                await NoteNextSeasonAsync(year + 1, cancellationToken).ConfigureAwait(true);
                return 0;

            case HttpFailure failure:
                Status = $"season unavailable: OpenF1 returned {failure.StatusCode}";
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

    /// <summary>Loads and draws the circuit at <paramref name="index"/>.</summary>
    public async Task SelectAsync(int index, CancellationToken cancellationToken)
    {
        if (index < 0 || index >= _entries.Length)
        {
            return;
        }

        var entry = _entries[index];
        IsBusy = true;
        Outline = null;
        Facts = string.Empty;
        Status = $"tracing {entry.Circuit.ShortName} from its recorded lap";

        foreach (var driver in OutlineDrivers)
        {
            var loaded = await _outlines
                .LoadAsync(entry.RaceSessionKey, entry.RaceStart.AddMinutes(10), driver, cancellationToken)
                .ConfigureAwait(true);

            switch (loaded)
            {
                case TrackOutline outline:
                    Outline = outline;
                    IsReplayCircuit = entry.RaceSessionKey == _runner.SessionKey;
                    Facts =
                        $"{entry.Circuit.Name}, {entry.Circuit.Country} · " +
                        $"{outline.LapLengthMeters / 1000d:F2} km measured · " +
                        $"{outline.Points.Length} GPS points · " +
                        $"{entry.SessionCount} sessions · " +
                        $"race {entry.RaceStart.ToLocalTime():ddd dd MMM HH:mm}";
                    Status = IsReplayCircuit
                        ? "geometry from recorded GPS · markers follow the replay"
                        : "geometry from recorded GPS; no hand-drawn map";
                    IsBusy = false;

                    await LoadWinnersAsync(entry, cancellationToken).ConfigureAwait(true);
                    return;

                case RateLimited limited:
                    Status = $"rate limited; retry in {limited.RetryAfterSeconds}s";
                    IsBusy = false;
                    return;

                case HttpFailure failure when failure.StatusCode == 204:
                    // This car did not complete a lap in the window; try the next one.
                    continue;

                case HttpFailure failure:
                    Status = $"circuit unavailable: {failure.StatusCode}: {failure.Reason}";
                    IsBusy = false;
                    return;

                default:
                    Status = "unexpected provider result";
                    IsBusy = false;
                    return;
            }
        }

        Status = "no car completed a lap in that window, so there is no map to draw";
        IsBusy = false;
    }

    private async Task NoteCalendarAgreementAsync(
        int year,
        ImmutableArray<CircuitEntry> entries,
        CancellationToken cancellationToken)
    {
        var jolpica = await _calendar.GetSeasonAsync(year, cancellationToken).ConfigureAwait(true);

        if (jolpica is not ImmutableArray<RaceWeekend> races || races.Length == 0)
        {
            return;
        }

        var matches = CalendarReconciler.Reconcile(entries, races);
        var unmatched = matches.Count(static match => !match.MatchedToJolpica);

        Headline = string.Create(
            CultureInfo.InvariantCulture,
            $"{year} season · {entries.Length} circuits · {unmatched} not in the Jolpica calendar");
    }

    private async Task LoadWinnersAsync(CircuitEntry entry, CancellationToken cancellationToken)
    {
        Winners = string.Empty;
        var loaded = await _winners
            .GetForRaceDateAsync(entry.RaceStart.Year, entry.RaceStart, cancellationToken)
            .ConfigureAwait(true);

        if (loaded is not CircuitWinners history || history.Winners.IsEmpty)
        {
            return;
        }

        var top = history.Winners.Take(5).Select(static winner =>
            $"{winner.DriverName} {winner.Wins} ({winner.LastSeason})");

        Winners = $"winners here: {string.Join(" · ", top)}";
    }

    private async Task<bool> NoteNextSeasonAsync(int year, CancellationToken cancellationToken)
    {
        var next = await _catalogue.LoadSeasonAsync(year, cancellationToken).ConfigureAwait(true);

        switch (next)
        {
            case ImmutableArray<CircuitEntry> { Length: > 0 } entries:
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{year} calendar published: {entries.Length} circuits");
                return true;

            case ImmutableArray<CircuitEntry>:
                Status = string.Create(CultureInfo.InvariantCulture, $"{year} calendar not announced yet");
                return false;

            default:
                Status = string.Create(CultureInfo.InvariantCulture, $"{year} calendar not announced yet");
                return false;
        }
    }
}

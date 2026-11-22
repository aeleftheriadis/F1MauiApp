using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Telemetry.Facts;

namespace F1App.Features.Archive;

/// <summary>One race of the vault, pre-formatted for the list.</summary>
public sealed record ArchiveRaceRow(
    string Season,
    string Round,
    string RaceName,
    string Circuit,
    string Date,
    string Winner,
    string Detail,
    string AccessibleText);

/// <summary>One line of the record book. Section titles carry <see cref="IsSection"/>.</summary>
public sealed record RecordRow(string Rank, string Name, string Numbers, string AccessibleText, bool IsSection);

/// <summary>
/// The archive: every world-championship race since 1950, searchable, with the all-time record book
/// computed from the same cached rows.
/// </summary>
/// <remarks>
/// Opening the screen reads SQLite and renders instantly, even before the one-off sync: past seasons are
/// history the moment they are written. When seasons are missing the sync runs in the background and the
/// status line reports its progress, so the first open is the only one that ever waits on the network.
/// </remarks>
public sealed partial class ArchiveViewModel : ObservableObject
{
    private readonly RaceVaultService _service;
    private readonly TimeProvider _timeProvider;
    private IReadOnlyList<ArchivedRace> _all = [];
    private Progress<VaultSyncProgress>? _progress;

    public ArchiveViewModel(RaceVaultService service, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _service = service;
        _timeProvider = timeProvider;
        Status = "not loaded";
        ShowRaces = true;
        Decades.Add("ALL");
        DecadeIndex = 0;
    }

    /// <summary>Races that pass the current search and decade filter, newest first.</summary>
    public ObservableCollection<ArchiveRaceRow> Races { get; } = [];

    /// <summary>The record book: section titles and win tables, one scroll.</summary>
    public ObservableCollection<RecordRow> Records { get; } = [];

    /// <summary>Decade filter: "ALL" plus one entry per decade the vault holds.</summary>
    public ObservableCollection<string> Decades { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int DecadeIndex { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool ShowRaces { get; set; }

    /// <summary>"On this day" line, empty when no race in the vault shares today's date.</summary>
    [ObservableProperty]
    public partial string OnThisDay { get; set; } = string.Empty;

    /// <summary>Loads the vault from the cache, then syncs in the background when seasons are missing.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            _progress = new Progress<VaultSyncProgress>(p =>
                Status = $"syncing archive: {p.SeasonsDone}/{p.SeasonsTotal} seasons, {p.Races} races");

            var snapshot = await _service.LoadAsync(cancellationToken).ConfigureAwait(true);
            Render(snapshot);

            if (await _service.NeedsSyncAsync(cancellationToken).ConfigureAwait(true))
            {
                IsBusy = true;
                var problem = await _service.SyncAsync(_progress, cancellationToken).ConfigureAwait(true);

                var refreshed = await _service.LoadAsync(cancellationToken).ConfigureAwait(true);
                Render(refreshed);
                Status = problem ?? $"{Races.Count} races · records computed from the archive";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Status = $"archive unreachable: {exception.GetType().Name}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value) => Filter();

    partial void OnDecadeIndexChanged(int value) => Filter();

    private void Render(RaceVaultSnapshot snapshot)
    {
        _all = snapshot.Races;
        RebuildDecades();
        Filter();

        Records.Clear();
        Records.Add(new RecordRow(string.Empty, "ALL-TIME WINS · DRIVERS", string.Empty, "driver wins", IsSection: true));
        AddRecords(Records, snapshot.Records.DriverWins);
        Records.Add(new RecordRow(string.Empty, "ALL-TIME WINS · CONSTRUCTORS", string.Empty, "constructor wins", IsSection: true));
        AddRecords(Records, snapshot.Records.ConstructorWins);

        Status = snapshot.Problem
            ?? string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.Records.Races} races · {snapshot.Records.Seasons} seasons · source Jolpica");

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var anniversaries = RaceVaultService.OnThisDay(snapshot.Races, today);

        OnThisDay = anniversaries.Length == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"ON THIS DAY · {anniversaries[0].Season} {anniversaries[0].RaceName} — {anniversaries[0].Winner}");
    }

    private void RebuildDecades()
    {
        var selected = DecadeIndex >= 0 && DecadeIndex < Decades.Count ? Decades[DecadeIndex] : "ALL";

        Decades.Clear();
        Decades.Add("ALL");

        foreach (var decade in _all.Select(static race => race.Season / 10 * 10).Distinct().OrderBy(static d => d))
        {
            Decades.Add(string.Create(CultureInfo.InvariantCulture, $"{decade}s"));
        }

        DecadeIndex = Math.Max(0, Decades.IndexOf(selected));
    }

    private void Filter()
    {
        var decade = DecadeIndex > 0 && DecadeIndex < Decades.Count
            && int.TryParse(Decades[DecadeIndex].TrimEnd('s'), CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;

        var search = SearchText?.Trim() ?? string.Empty;

        Races.Clear();

        foreach (var race in _all.OrderByDescending(static race => race.Date))
        {
            if (decade >= 0 && race.Season / 10 * 10 != decade)
            {
                continue;
            }

            if (search.Length > 0
                && !race.RaceName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !race.CircuitName.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !race.Country.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !race.Winner.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !race.Constructor.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var date = race.Date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

            var detail = string.IsNullOrWhiteSpace(race.WinningTime)
                ? string.Create(CultureInfo.InvariantCulture, $"{race.Constructor} · {race.Laps} laps")
                : string.Create(CultureInfo.InvariantCulture, $"{race.Constructor} · {race.WinningTime}");

            Races.Add(new ArchiveRaceRow(
                string.Create(CultureInfo.InvariantCulture, $"{race.Season}"),
                string.Create(CultureInfo.InvariantCulture, $"R{race.Round:00}"),
                race.RaceName,
                string.Create(CultureInfo.InvariantCulture, $"{race.CircuitName} · {race.Country}"),
                date,
                race.Winner,
                detail,
                $"{race.Season} {race.RaceName} at {race.CircuitName}: won by {race.Winner} for {race.Constructor}"));
        }

        Status = Races.Count == _all.Count
            ? string.Create(CultureInfo.InvariantCulture, $"{Races.Count} races")
            : $"{Races.Count} of {_all.Count} races";
    }

    private static void AddRecords(ObservableCollection<RecordRow> rows, System.Collections.Immutable.ImmutableArray<WinRecord> records)
    {
        var rank = 1;

        foreach (var record in records.Take(20))
        {
            var numbers = string.Create(
                CultureInfo.InvariantCulture,
                $"{record.Wins} wins · {record.FirstSeason}-{record.LastSeason}");

            rows.Add(new RecordRow(
                string.Create(CultureInfo.InvariantCulture, $"{rank++}"),
                record.Name,
                numbers,
                $"{record.Name}: {numbers}",
                IsSection: false));
        }
    }
}

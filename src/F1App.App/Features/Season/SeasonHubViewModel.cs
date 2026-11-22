using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry.Circuits;

namespace F1App.Features.Season;

/// <summary>
/// One round on the season list. <c>SessionKey</c> is the OpenF1 session that can be replayed, or 0 when
/// none is published yet; <c>CircuitId</c> lets the map tell whether it belongs to the replay.
/// </summary>
public sealed record SeasonRound(
    string Round,
    string Name,
    string Circuit,
    string When,
    string State,
    bool IsNext,
    bool IsDone,
    int SessionKey,
    string CircuitId,
    DateTimeOffset RaceStart)
{
    /// <summary>True when this round has published telemetry to replay.</summary>
    public bool CanReplay => SessionKey != 0;
}

/// <summary>
/// The season hub: every round of a season in local time, with the next race called out.
/// </summary>
/// <remarks>
/// The calendar comes from the SQLite-backed store, so a second launch never waits on the network. Times
/// are converted once, here, so no view has to know about time zones.
/// </remarks>
public sealed partial class SeasonHubViewModel : ObservableObject
{
    private readonly IRaceCalendarStore _calendar;
    private readonly OpenF1CircuitCatalogue _catalogue;
    private readonly SessionRunner _runner;
    private readonly TimeProvider _timeProvider;

    public SeasonHubViewModel(
        IRaceCalendarStore calendar,
        OpenF1CircuitCatalogue catalogue,
        SessionRunner runner,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _calendar = calendar;
        _catalogue = catalogue;
        _runner = runner;
        _timeProvider = timeProvider;

        var year = timeProvider.GetUtcNow().Year;
        // OpenF1 publishes sessions from 2023 onwards; a year outside that range has no calendar and
        // nothing to replay, so the picker starts there rather than at 1950.
        Years = Enumerable.Range(2023, year + 1 - 2023 + 1).ToList();
        YearIndex = Years.IndexOf(year);
        Headline = "season";
        Subhead = "loading calendar";
        HeroTitle = "—";
        HeroDetail = string.Empty;
        HeroCountdown = string.Empty;
        Status = string.Empty;
    }

    /// <summary>Selectable seasons: last, current, next.</summary>
    public IList<int> Years { get; }

    /// <summary>Rounds of the selected season.</summary>
    public ObservableCollection<SeasonRound> Rounds { get; } = [];

    [ObservableProperty]
    public partial int YearIndex { get; set; }

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Subhead { get; set; }

    [ObservableProperty]
    public partial string HeroTitle { get; set; }

    [ObservableProperty]
    public partial string HeroDetail { get; set; }

    [ObservableProperty]
    public partial string HeroCountdown { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Loads the selected season, refreshing from the provider when the cache is empty.</summary>
    public async Task LoadAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (YearIndex < 0 || YearIndex >= Years.Count)
        {
            return;
        }

        var year = Years[YearIndex];
        IsBusy = true;
        Headline = string.Create(CultureInfo.InvariantCulture, $"{year} season");
        Status = "loading calendar";

        var loaded = await _calendar.GetSeasonAsync(year, refresh, cancellationToken).ConfigureAwait(true);

        switch (loaded)
        {
            case System.Collections.Immutable.ImmutableArray<RaceWeekend> races:
                // The calendar decides the rounds; OpenF1 decides which of them have telemetry to replay.
                var sessions = await _catalogue.LoadSeasonAsync(year, cancellationToken).ConfigureAwait(true);
                Build(races, sessions is System.Collections.Immutable.ImmutableArray<CircuitEntry> entries ? entries : []);
                break;

            case HttpFailure failure:
                Rounds.Clear();
                HeroTitle = "no calendar";
                HeroDetail = $"OpenF1 returned {failure.StatusCode}";
                HeroCountdown = string.Empty;
                Status = failure.StatusCode == 204
                    ? string.Create(CultureInfo.InvariantCulture, $"{year} calendar not published yet")
                    : $"calendar unavailable ({failure.StatusCode})";
                break;

            case RateLimited limited:
                Status = $"rate limited, retry in {limited.RetryAfterSeconds}s";
                break;
        }

        IsBusy = false;
    }

    /// <summary>Switches the app's replay to this round's race session, the same path Results uses.</summary>
    public async Task ReplayAsync(SeasonRound round, CancellationToken cancellationToken)
    {
        if (!round.CanReplay)
        {
            Status = $"{round.Name} has no published telemetry to replay";
            return;
        }

        var year = Years[YearIndex];
        Status = $"switching replay to {round.Name} {year}";
        await _runner
            .SwitchToAsync(
                new SessionRequest(
                    round.SessionKey,
                    $"{round.Name} {year} · Race",
                    // Ten minutes in, the same offset Results uses: past the start and into running order.
                    round.RaceStart.AddMinutes(10),
                    round.CircuitId),
                cancellationToken)
            .ConfigureAwait(true);

        Status = $"replaying {round.Name} {year}; the Timing and Track screens now follow this session";
    }

    private void Build(
        System.Collections.Immutable.ImmutableArray<RaceWeekend> races,
        System.Collections.Immutable.ImmutableArray<CircuitEntry> sessions)
    {
        var now = _timeProvider.GetUtcNow();
        Rounds.Clear();

        // Join on the race date: the two sources do not share circuit identifiers (ADR-004 in calendar terms).
        var byDate = new Dictionary<DateOnly, CircuitEntry>();

        foreach (var entry in sessions)
        {
            byDate.TryAdd(DateOnly.FromDateTime(entry.RaceStart.UtcDateTime), entry);
        }

        var next = races.FirstOrDefault(race => race.RaceStart > now);
        var completed = races.Count(race => race.RaceStart <= now);
        var circuit = races.Select(race => race.Circuit.Id).Distinct().Count();

        foreach (var race in races)
        {
            var isNext = next is not null && race.Round == next.Round;
            var isDone = race.RaceStart <= now;

            Rounds.Add(new SeasonRound(
                Round: string.Create(CultureInfo.InvariantCulture, $"R{race.Round:00}"),
                Name: race.Name,
                Circuit: string.Create(CultureInfo.InvariantCulture, $"{race.Circuit.ShortName} · {race.Circuit.Country}"),
                When: race.RaceStart.ToLocalTime().ToString("ddd dd MMM · HH:mm", CultureInfo.CurrentCulture),
                State: isNext ? "NEXT" : isDone ? "DONE" : $"IN {(int)(race.RaceStart - now).TotalDays}d",
                IsNext: isNext,
                IsDone: isDone,
                SessionKey: byDate.TryGetValue(DateOnly.FromDateTime(race.RaceStart.UtcDateTime), out var session)
                    ? session.RaceSessionKey
                    : 0,
                CircuitId: race.Circuit.Id,
                RaceStart: race.RaceStart));
        }

        Subhead = string.Create(
            CultureInfo.InvariantCulture,
            $"{races.Length} rounds · {circuit} circuits · {completed} completed");

        if (next is null)
        {
            HeroTitle = "season complete";
            HeroDetail = Rounds.Count > 0 ? Rounds[^1].Name : string.Empty;
            HeroCountdown = string.Empty;
            Status = "no further rounds scheduled";
            return;
        }

        var until = next.RaceStart - now;

        HeroTitle = next.Name;
        HeroDetail = string.Create(
            CultureInfo.InvariantCulture,
            $"{next.Circuit.ShortName}, {next.Circuit.Country} · {next.RaceStart.ToLocalTime():ddd dd MMM HH:mm} local");

        HeroCountdown = until.TotalDays >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"T-{(int)until.TotalDays}d {until.Hours}h")
            : string.Create(CultureInfo.InvariantCulture, $"T-{until.Hours}h {until.Minutes}m");

        Status = "times shown in your timezone";
    }
}

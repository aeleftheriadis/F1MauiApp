using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;

namespace F1App.Features.Season;

/// <summary>One round on the season list.</summary>
public sealed record SeasonRound(
    string Round,
    string Name,
    string Circuit,
    string When,
    string State,
    bool IsNext,
    bool IsDone);

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
    private readonly TimeProvider _timeProvider;

    public SeasonHubViewModel(IRaceCalendarStore calendar, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _calendar = calendar;
        _timeProvider = timeProvider;

        var year = timeProvider.GetUtcNow().Year;
        Years = new List<int> { year - 1, year, year + 1 };
        YearIndex = 1;
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
                Build(races);
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

    private void Build(System.Collections.Immutable.ImmutableArray<RaceWeekend> races)
    {
        var now = _timeProvider.GetUtcNow();
        Rounds.Clear();

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
                IsDone: isDone));
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

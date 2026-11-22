using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry.Facts;
using F1App.Telemetry.Providers;
using F1App.Telemetry.Media;
using F1App.Theme;

namespace F1App.Features.Paddock;

/// <summary>A driver in the paddock roster.</summary>
public sealed record PaddockDriver(
    int Number,
    string Code,
    string Name,
    string Team,
    Color Colour,
    string? HeadshotUrl = null)
{
    public string Label => $"#{Number} · {Code}";
}

/// <summary>
/// The paddock: this session's drivers, with a dossier that carries a freely licensed photo.
/// </summary>
/// <remarks>
/// The roster is real - it comes from the driver list of the session being replayed. The photo comes from
/// Wikimedia Commons, one request per driver, cached in SQLite with its licence and author because
/// attribution is a condition of the licence, not a nicety. When Commons has nothing, the panel says so.
/// </remarks>
public sealed partial class PaddockViewModel : ObservableObject
{
    private readonly SessionRunner _runner;
    private readonly WikimediaImageClient _images;
    private readonly SqliteMediaCache _cache;
    private readonly TeammateDuelService _duels;
    private readonly OpenF1Client _client;
    private readonly TimeProvider _timeProvider;
    private ImmutableDictionary<string, TeammateDuel> _duelsByDriver = ImmutableDictionary<string, TeammateDuel>.Empty;
    private ImmutableDictionary<int, ChampionshipSwing> _swings = ImmutableDictionary<int, ChampionshipSwing>.Empty;

    public PaddockViewModel(
        SessionRunner runner,
        WikimediaImageClient images,
        SqliteMediaCache cache,
        TeammateDuelService duels,
        OpenF1Client client,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(duels);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _runner = runner;
        _images = images;
        _cache = cache;
        _duels = duels;
        _client = client;
        _timeProvider = timeProvider;

        Headline = "paddock";
        Status = "waiting for a session";
        DossierName = "—";
        DossierDetail = string.Empty;
        PortraitCaption = string.Empty;
        PortraitUrl = null;
        DuelLine = string.Empty;
        ChampionshipLine = string.Empty;
    }

    /// <summary>Drivers of the current session.</summary>
    public ObservableCollection<PaddockDriver> Drivers { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial string DossierName { get; set; }

    [ObservableProperty]
    public partial string DossierDetail { get; set; }

    [ObservableProperty]
    public partial Color DossierColour { get; set; } = Colors.Transparent;

    /// <summary>Preview URL for the dossier photo, or <see langword="null"/> when there is none.</summary>
    [ObservableProperty]
    public partial string? PortraitUrl { get; set; }

    /// <summary>Author and licence, shown under any photo. Empty when there is no photo.</summary>
    [ObservableProperty]
    public partial string PortraitCaption { get; set; }

    /// <summary>Title standing and this race's swing for this driver, or empty when not published.</summary>
    [ObservableProperty]
    public partial string ChampionshipLine { get; set; }

    /// <summary>Teammate head-to-head for this driver, or empty when the season has none.</summary>
    [ObservableProperty]
    public partial string DuelLine { get; set; }

    /// <summary>True when the subject was looked up and has no freely licensed picture.</summary>
    [ObservableProperty]
    public partial bool HasNoPortrait { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Loads this season's teammate duels once and caches the map for every selection.</summary>
    public async Task LoadDuelsAsync(CancellationToken cancellationToken)
    {
        var season = _timeProvider.GetUtcNow().Year;
        var snapshot = await _duels.GetAsync(season, force: false, cancellationToken).ConfigureAwait(true);
        _duelsByDriver = snapshot.Duels;
        ApplyDuel();

        await LoadChampionshipAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the title state for the session being replayed. The beta endpoint answers for race sessions only,
    /// so anything else leaves the line empty rather than guessing a standing.
    /// </summary>
    private async Task LoadChampionshipAsync(CancellationToken cancellationToken)
    {
        if (_runner.SessionKey == 0)
        {
            return;
        }

        var loaded = await _client
            .GetChampionshipDriversAsync(_runner.SessionKey, cancellationToken)
            .ConfigureAwait(true);

        if (loaded is not ImmutableArray<OpenF1ChampionshipDriver> rows || rows.IsEmpty)
        {
            _swings = ImmutableDictionary<int, ChampionshipSwing>.Empty;
            ChampionshipLine = string.Empty;
            return;
        }

        _swings = rows
            .Where(static row => row.DriverNumber is not null)
            .GroupBy(static row => row.DriverNumber!.Value)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => new ChampionshipSwing(
                    group.First().PositionStart,
                    group.First().PositionCurrent,
                    group.First().PointsStart,
                    group.First().PointsCurrent));

        ApplyChampionship();
    }

    private void ApplyChampionship()
    {
        ChampionshipLine = SelectedIndex >= 0 && SelectedIndex < Drivers.Count
            ? _swings.GetValueOrDefault(Drivers[SelectedIndex].Number).Describe()
            : string.Empty;
    }

    private void ApplyDuel()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Drivers.Count)
        {
            DuelLine = string.Empty;
            return;
        }

        // The roster carries the OpenF1 driver, whose name is the full name; the duel is keyed by driver
        // code, which both sources agree on.
        var code = Drivers[SelectedIndex].Code;

        DuelLine = _duelsByDriver.Values
            .FirstOrDefault(duel => string.Equals(duel.DriverCode, code, StringComparison.OrdinalIgnoreCase)) is { } match
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"vs {match.TeammateName}: races {match.RaceWins}-{match.TeammateWins}, "
                + $"average {match.AveragePositionDelta:+0.0;-0.0;0.0} places, "
                + $"qualifying {match.QualifyingWins}-{match.QualifyingLosses} over {match.Compared} races")
            : string.Empty;
    }

    /// <summary>Fills the roster from the loaded replay.</summary>
    public void Refresh()
    {
        var session = _runner.Loaded;

        if (session is null || session.Drivers.IsDefaultOrEmpty)
        {
            Status = _runner.FailureReason ?? "no session loaded yet";
            return;
        }

        Drivers.Clear();

        foreach (var driver in session.Drivers.OrderBy(static driver => driver.Number))
        {
            Drivers.Add(new PaddockDriver(
                driver.Number,
                driver.Code,
                driver.FullName,
                driver.TeamName,
                Parse(driver.TeamColorHex),
                driver.HeadshotUrl));
        }

        Headline = string.Create(CultureInfo.InvariantCulture, $"paddock · {Drivers.Count} drivers");
        Status = session.Label;
    }

    /// <summary>Loads the dossier and the photo for <paramref name="index"/>.</summary>
    public async Task SelectAsync(int index, CancellationToken cancellationToken)
    {
        if (index < 0 || index >= Drivers.Count)
        {
            return;
        }

        SelectedIndex = index;
        var driver = Drivers[index];

        DossierName = driver.Name;
        DossierColour = driver.Colour;
        DossierDetail = string.Create(
            CultureInfo.InvariantCulture,
            $"#{driver.Number} · {driver.Code} · {driver.Team}");
        PortraitUrl = null;
        PortraitCaption = string.Empty;
        HasNoPortrait = false;
        ApplyDuel();
        ApplyChampionship();

        // The provider's own headshot first: it is the current season's picture of the right driver, and it
        // needs no search. Commons is the fallback for the one driver it omits, and carries the licence text.
        if (!string.IsNullOrWhiteSpace(driver.HeadshotUrl))
        {
            PortraitUrl = driver.HeadshotUrl;
            PortraitCaption = "Portrait: Formula 1 media, via OpenF1";
            return;
        }

        var cached = await _cache.ReadAsync(driver.Name, cancellationToken).ConfigureAwait(true);

        if (cached is not null)
        {
            Apply(driver, cached);
            return;
        }

        IsBusy = true;
        Status = $"searching Commons for {driver.Name}";

        var found = await _images.FindAsync(driver.Name, cancellationToken).ConfigureAwait(true);

        switch (found)
        {
            case OpenImage image:
                await _cache.WriteAsync(driver.Name, image, _timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(true);
                Apply(driver, image);
                Status = "photo from Wikimedia Commons";
                break;

            case HttpFailure:
                await _cache.WriteAsync(driver.Name, null, _timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(true);
                HasNoPortrait = true;
                Status = "no freely licensed photo found for this driver";
                break;

            case RateLimited limited:
                Status = $"Commons rate limited; retry in {limited.RetryAfterSeconds}s";
                break;
        }

        IsBusy = false;
    }

    private void Apply(PaddockDriver driver, OpenImage image)
    {
        if (image == SqliteMediaCache.Empty || string.IsNullOrEmpty(image.PreviewUrl))
        {
            HasNoPortrait = true;
            PortraitUrl = null;
            PortraitCaption = string.Empty;
            return;
        }

        HasNoPortrait = false;
        PortraitUrl = image.PreviewUrl;

        // Licence terms: author and licence travel with the picture.
        PortraitCaption = string.Create(
            CultureInfo.InvariantCulture,
            $"{image.Title} · {image.Artist} · {image.Licence} · Wikimedia Commons");
    }

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

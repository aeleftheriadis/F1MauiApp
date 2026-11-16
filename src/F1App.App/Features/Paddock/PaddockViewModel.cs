using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry.Media;
using F1App.Theme;

namespace F1App.Features.Paddock;

/// <summary>A driver in the paddock roster.</summary>
public sealed record PaddockDriver(int Number, string Code, string Name, string Team, Color Colour)
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
    private readonly TimeProvider _timeProvider;

    public PaddockViewModel(
        SessionRunner runner,
        WikimediaImageClient images,
        SqliteMediaCache cache,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _runner = runner;
        _images = images;
        _cache = cache;
        _timeProvider = timeProvider;

        Headline = "paddock";
        Status = "waiting for a session";
        DossierName = "—";
        DossierDetail = string.Empty;
        PortraitCaption = string.Empty;
        PortraitUrl = null;
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

    /// <summary>True when the subject was looked up and has no freely licensed picture.</summary>
    [ObservableProperty]
    public partial bool HasNoPortrait { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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
                Parse(driver.TeamColorHex)));
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

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Telemetry.Facts;

namespace F1App.Features.Races;

/// <summary>One line of a race's classification, qualifying or pit-stop list.</summary>
public sealed record RaceDetailRow(string Lead, string Name, string Detail, string AccessibleText);

/// <summary>
/// One race in full: classification, qualifying and pit stops, loaded on demand.
/// </summary>
/// <remarks>
/// This is the archive's second level. Bulk-syncing every race's three endpoints would be thousands of
/// requests, so a race is fetched when it is opened and then kept in SQLite.
/// </remarks>
public sealed partial class RaceDetailViewModel : ObservableObject
{
    private readonly JolpicaRaceDetailClient _client;
    private readonly TimeProvider _timeProvider;

    public RaceDetailViewModel(JolpicaRaceDetailClient client, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _client = client;
        _timeProvider = timeProvider;
        Headline = "race";
        Subhead = string.Empty;
        Status = "loading";
    }

    /// <summary>Selection, set by whoever navigates here: season and round.</summary>
    public int Season { get; private set; }

    /// <summary>Round within the season.</summary>
    public int Round { get; private set; }

    public ObservableCollection<RaceDetailRow> Classification { get; } = [];

    public ObservableCollection<RaceDetailRow> Qualifying { get; } = [];

    public ObservableCollection<RaceDetailRow> PitStops { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Subhead { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Sets which race to show; the page calls this before loading.</summary>
    public void Select(int season, int round)
    {
        Season = season;
        Round = round;
    }

    /// <summary>Loads the race once. Safe to call again after a failure.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Season == 0 || Round == 0)
        {
            Status = "no race selected";
            return;
        }

        IsBusy = true;
        Status = string.Create(CultureInfo.InvariantCulture, $"loading {Season} round {Round}");

        var loaded = await _client.GetAsync(Season, Round, cancellationToken).ConfigureAwait(true);

        switch (loaded)
        {
            case RaceDetail detail:
                Headline = detail.RaceName;
                Subhead = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{detail.Season} · round {detail.Round} · {detail.CircuitName} · {detail.Date:dd MMM yyyy}");

                Fill(Classification, detail.Classification.Select(ToRow));
                Fill(Qualifying, detail.Qualifying.Select(ToQualifyingRow));
                Fill(PitStops, detail.PitStops.Select(ToPitStopRow));

                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{detail.Classification.Length} classified · {Describe(detail.Qualifying.Length, "qualifying")} · {Describe(detail.PitStops.Length, "pit stops")}");
                break;

            case HttpFailure failure:
                Status = $"race unavailable ({failure.StatusCode}): {failure.Reason}";
                break;

            case RateLimited limited:
                Status = $"rate limited, retry in {limited.RetryAfterSeconds}s";
                break;
        }

        IsBusy = false;
    }

    /// <summary>"12 rows" or "not published" - an empty section is a gap in the source, not an empty race.</summary>
    private static string Describe(int count, string what) =>
        count == 0 ? $"{what} not published" : string.Create(CultureInfo.InvariantCulture, $"{count} {what}");

    private static void Fill(ObservableCollection<RaceDetailRow> target, IEnumerable<RaceDetailRow> rows)
    {
        target.Clear();

        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private static RaceDetailRow ToRow(RaceClassificationRow row)
    {
        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{row.Constructor} · {row.Status} · {row.Laps} laps");

        if (row.Points > 0)
        {
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"{detail} · {row.Points:0.#} pts");
        }

        return new RaceDetailRow(
            row.Position,
            row.DriverName,
            detail,
            $"{row.Position}. {row.DriverName}, {detail}");
    }

    private static RaceDetailRow ToQualifyingRow(RaceQualifyingRow row)
    {
        var times = string.Join(
            " · ",
            new[] { ("Q1", row.Q1), ("Q2", row.Q2), ("Q3", row.Q3) }
                .Where(static part => !string.IsNullOrWhiteSpace(part.Item2))
                .Select(static part => $"{part.Item1} {part.Item2}"));

        return new RaceDetailRow(
            row.Position,
            row.DriverName,
            times.Length == 0 ? "no time set" : times,
            $"{row.Position}. {row.DriverName}, {times}");
    }

    private static RaceDetailRow ToPitStopRow(RacePitStopRow row)
    {
        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"lap {row.Lap} · stop {row.StopNumber}");

        if (row.DurationSeconds is { } seconds)
        {
            detail = string.Create(CultureInfo.InvariantCulture, $"{detail} · {seconds:F1}s");
        }

        return new RaceDetailRow(
            "PIT",
            row.DriverName,
            detail,
            $"{row.DriverName}, {detail}");
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Telemetry.Facts;

namespace F1App.Features.Facts;

/// <summary>One champions row, pre-formatted.</summary>
public sealed record ChampionRow(string Season, string Driver, string Team, string Numbers, string AccessibleText);

/// <summary>The champions wall.</summary>
public sealed partial class FactsViewModel : ObservableObject
{
    private readonly ChampionsService _service;

    public FactsViewModel(ChampionsService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        Status = "not loaded";
    }

    /// <summary>Champions, newest season first.</summary>
    public ObservableCollection<ChampionRow> Champions { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Loads the champions list, using the service cache unless <paramref name="force"/> is set.</summary>
    public async Task LoadAsync(bool force, CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var snapshot = await _service.GetAsync(force, cancellationToken).ConfigureAwait(true);

            Champions.Clear();

            foreach (var champion in snapshot.Champions.OrderByDescending(static entry => entry.Season))
            {
                var numbers = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{champion.Points:F0} pts · {champion.Wins} wins");

                Champions.Add(new ChampionRow(
                    string.Create(CultureInfo.InvariantCulture, $"{champion.Season}"),
                    champion.DriverName,
                    champion.ConstructorName,
                    numbers,
                    $"{champion.Season} world champion: {champion.DriverName} for {champion.ConstructorName}, " +
                    $"{numbers}"));
            }

            var fetched = snapshot.FetchedAt.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture);

            Status = snapshot.Problem is { Length: > 0 } problem
                ? problem
                : $"{Champions.Count} seasons · source Jolpica · cached {fetched}";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Status = $"facts unreachable: {exception.GetType().Name}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

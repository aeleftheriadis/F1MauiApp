using System.Collections.Immutable;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Strategy;

/// <summary>
/// Builds a stint timeline for a session from two anonymous OpenF1 endpoints.
/// </summary>
/// <remarks>
/// Strategy is tyre runs, not pit stops: the stop count is already a Results column, and a stint list with
/// its compounds says more about how a race unfolded than the number of stops does. Lap bounds come from
/// the data; a stint still open at the end is closed at the session's last completed lap.
/// </remarks>
public sealed class OpenF1StrategyLoader
{
    private readonly OpenF1Client _client;

    /// <summary>Creates the loader.</summary>
    public OpenF1StrategyLoader(OpenF1Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>Loads the strategy timeline for a session.</summary>
    public async Task<FetchResult<StrategyTimeline>> LoadAsync(
        int sessionKey,
        string sessionName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        var stintsResult = await _client.GetStintsAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (stintsResult is not ImmutableArray<OpenF1Stint> stints || stints.Length == 0)
        {
            return stintsResult switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, "no stint data published for that session"),
            };
        }

        var driversResult = await _client.GetDriversAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (driversResult is not ImmutableArray<OpenF1Driver> drivers)
        {
            return driversResult switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, "no driver list published for that session"),
            };
        }

        var codes = drivers
            .GroupBy(static driver => driver.DriverNumber)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Acronym ?? group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var lastLap = stints.Max(static stint => stint.LapEnd ?? stint.LapStart);

        var builder = ImmutableArray.CreateBuilder<DriverStrategy>(codes.Count);

        foreach (var byDriver in stints.GroupBy(static stint => stint.DriverNumber))
        {
            var runs = byDriver
                .OrderBy(static stint => stint.StintNumber)
                .Select(stint => new DriverStint(
                    LapStart: stint.LapStart,
                    LapEnd: stint.LapEnd ?? lastLap,
                    Compound: stint.Compound ?? "UNKNOWN",
                    TyreAgeAtStart: stint.TyreAgeAtStart ?? 0))
                .ToImmutableArray();

            builder.Add(new DriverStrategy(byDriver.Key, codes.GetValueOrDefault(byDriver.Key, "???"), runs));
        }

        return new StrategyTimeline(
            sessionKey,
            sessionName,
            lastLap,
            builder.OrderBy(static strategy => strategy.Code, StringComparer.Ordinal).ToImmutableArray());
    }
}

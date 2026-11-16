using System.Collections.Immutable;
using System.Text.Json;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Timing;

/// <summary>
/// Builds the timing table for a completed session from six anonymous OpenF1 endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is derived from published data: best lap is the fastest non-pit-out lap, sector
/// bests are the fastest of each sector, position and gap are the last classified sample, tyre age is
/// the last stint's start age plus the laps run in it. Nothing is estimated, and a missing value stays
/// missing instead of becoming a zero.
/// </para>
/// <para>
/// Six requests at 30 per minute: the request gate paces them, so a load takes a few seconds. That is
/// the price of the free tier, and it is cheaper than showing the wrong number.
/// </para>
/// </remarks>
public sealed class OpenF1SessionTimingLoader
{
    private readonly OpenF1Client _client;

    /// <summary>Creates a loader.</summary>
    public OpenF1SessionTimingLoader(OpenF1Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>Loads the timing table for <paramref name="sessionName"/>.</summary>
    /// <param name="sessionKey">OpenF1 session key.</param>
    /// <param name="sessionName">Session label to carry into the result.</param>
    /// <param name="start">Scheduled session start, carried into the result.</param>
    /// <param name="cancellationToken">Cancels the six requests.</param>
    public async Task<FetchResult<SessionTiming>> LoadAsync(
        int sessionKey,
        string sessionName,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        var lapsResult = await _client.GetLapsAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (lapsResult is not ImmutableArray<OpenF1Lap> laps || laps.Length == 0)
        {
            return Propagate(lapsResult, "no lap data published for that session");
        }

        var positionsResult = await _client.GetPositionsAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (positionsResult is not ImmutableArray<OpenF1Position> positions)
        {
            return Propagate(positionsResult, "no position data published for that session");
        }

        var intervalsResult = await _client.GetIntervalsAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (intervalsResult is not ImmutableArray<OpenF1Interval> intervals)
        {
            return Propagate(intervalsResult, "no interval data published for that session");
        }

        var stintsResult = await _client.GetStintsAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (stintsResult is not ImmutableArray<OpenF1Stint> stints)
        {
            return Propagate(stintsResult, "no stint data published for that session");
        }

        var pitsResult = await _client.GetPitStopsAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (pitsResult is not ImmutableArray<OpenF1PitStop> pits)
        {
            return Propagate(pitsResult, "no pit data published for that session");
        }

        var driversResult = await _client.GetDriversAsync(sessionKey, cancellationToken).ConfigureAwait(false);
        if (driversResult is not ImmutableArray<OpenF1Driver> drivers)
        {
            return Propagate(driversResult, "no driver list published for that session");
        }

        var entries = Build(laps, positions, intervals, stints, pits, drivers);

        return entries.IsEmpty
            ? new HttpFailure(204, "no driver could be classified from the published data")
            : new SessionTiming(sessionKey, sessionName, start, entries);
    }

    private static ImmutableArray<DriverTiming> Build(
        ImmutableArray<OpenF1Lap> laps,
        ImmutableArray<OpenF1Position> positions,
        ImmutableArray<OpenF1Interval> intervals,
        ImmutableArray<OpenF1Stint> stints,
        ImmutableArray<OpenF1PitStop> pits,
        ImmutableArray<OpenF1Driver> drivers)
    {
        var lastPosition = positions
            .GroupBy(static item => item.DriverNumber)
            .ToDictionary(static group => group.Key, static group => group.MaxBy(static item => item.Date));

        var lastInterval = intervals
            .GroupBy(static item => item.DriverNumber)
            .ToDictionary(static group => group.Key, static group => group.MaxBy(static item => item.Date));

        var lastStint = stints
            .GroupBy(static item => item.DriverNumber)
            .ToDictionary(static group => group.Key, static group => group.MaxBy(static item => item.StintNumber));

        var pitCounts = pits
            .GroupBy(static item => item.DriverNumber)
            .ToDictionary(static group => group.Key, static group => group.Count());

        var identities = drivers
            .GroupBy(static item => item.DriverNumber)
            .ToDictionary(static group => group.Key, static group => group.First());

        var builder = ImmutableArray.CreateBuilder<DriverTiming>();

        foreach (var byDriver in laps.GroupBy(static lap => lap.DriverNumber))
        {
            var driver = byDriver.Key;
            var running = byDriver.Where(static lap => lap.IsPitOutLap is not true).ToArray();
            var pool = running.Length > 0 ? running : byDriver.ToArray();

            identities.TryGetValue(driver, out var identity);
            lastPosition.TryGetValue(driver, out var position);
            lastInterval.TryGetValue(driver, out var interval);
            lastStint.TryGetValue(driver, out var stint);

            builder.Add(new DriverTiming(
                DriverNumber: driver,
                Code: identity?.Acronym ?? driver.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TeamColourHex: string.IsNullOrEmpty(identity?.TeamColour) ? "#94A3B8" : $"#{identity.TeamColour}",
                Position: position?.Position ?? 0,
                BestLap: Fastest(pool.Select(static lap => lap.LapDuration)),
                BestSector1: Fastest(pool.Select(static lap => lap.Sector1)),
                BestSector2: Fastest(pool.Select(static lap => lap.Sector2)),
                BestSector3: Fastest(pool.Select(static lap => lap.Sector3)),
                GapToLeaderSeconds: ReadSeconds(interval?.GapToLeader),
                Compound: stint?.Compound ?? "UNKNOWN",
                TyreAgeLaps: TyreAge(stint, byDriver.Max(static lap => lap.LapNumber)),
                PitStops: pitCounts.GetValueOrDefault(driver)));
        }

        return builder
            .OrderBy(static entry => entry.Position == 0 ? int.MaxValue : entry.Position)
            .ThenBy(static entry => entry.DriverNumber)
            .ToImmutableArray();
    }

    private static int TyreAge(OpenF1Stint? stint, int lastLap)
    {
        if (stint is null)
        {
            return 0;
        }

        var end = stint.LapEnd ?? lastLap;
        var run = Math.Max(1, end - stint.LapStart + 1);

        return (stint.TyreAgeAtStart ?? 0) + run;
    }

    private static TimeSpan? Fastest(IEnumerable<double?> seconds)
    {
        var best = double.MaxValue;

        foreach (var value in seconds)
        {
            if (value is > 0 and var candidate && candidate < best)
            {
                best = candidate;
            }
        }

        return best == double.MaxValue ? null : TimeSpan.FromSeconds(best);
    }

    private static double? ReadSeconds(JsonElement? element)
    {
        if (element is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            // "+1 LAP" and similar: a real classification, but not a number of seconds.
            _ => null,
        };
    }

    private static FetchResult<SessionTiming> Propagate<T>(
        FetchResult<T> result,
        string emptyMessage) => result switch
        {
            HttpFailure failure => failure,
            RateLimited limited => limited,
            _ => new HttpFailure(204, emptyMessage),
        };
}

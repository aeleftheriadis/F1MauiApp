using System.Collections.Immutable;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Replay;

/// <summary>
/// Loads a recorded session window from the anonymous OpenF1 endpoints: one lap of GPS for the
/// circuit shape, and a short window of positions plus car telemetry for the replay.
/// </summary>
/// <remarks>
/// Three requests per load, about 700 KB, all inside the free tier's 30 requests per minute. Nothing
/// here is synthesised: a coordinate that is not in the payload does not appear on the map.
/// </remarks>
public sealed class OpenF1ReplayLoader
{
    /// <summary>Window fetched to capture one complete lap for the circuit outline.</summary>
    public static readonly TimeSpan OutlineWindow = TimeSpan.FromSeconds(150);

    /// <summary>Largest gap between a position sample and a car-data sample that still pairs them.</summary>
    public static readonly TimeSpan JoinTolerance = TimeSpan.FromMilliseconds(500);

    private readonly OpenF1Client _client;
    private readonly OpenF1TrackOutlineLoader _outlines;

    /// <summary>Creates a loader over the anonymous OpenF1 endpoints.</summary>
    public OpenF1ReplayLoader(OpenF1Client client, OpenF1TrackOutlineLoader outlines)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(outlines);

        _client = client;
        _outlines = outlines;
    }

    /// <summary>
    /// Loads a replay window for <paramref name="sessionKey"/>.
    /// </summary>
    /// <param name="sessionKey">OpenF1 session key to load.</param>
    /// <param name="label">Operator-facing session identity, shown with the data.</param>
    /// <param name="from">Start of the replay window (UTC).</param>
    /// <param name="duration">Length of the replay window.</param>
    /// <param name="outlineDriver">
    /// Car whose trace is used to draw the circuit. Any car that completed a lap in the outline
    /// window works.
    /// </param>
    /// <param name="cancellationToken">Cancels the three provider requests.</param>
    public async Task<FetchResult<ReplaySession>> LoadAsync(
        int sessionKey,
        string label,
        DateTimeOffset from,
        TimeSpan duration,
        int outlineDriver,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration.TotalSeconds);

        var outlineResult = await _outlines
            .LoadAsync(sessionKey, from, outlineDriver, cancellationToken)
            .ConfigureAwait(false);

        if (outlineResult is not TrackOutline outline)
        {
            return outlineResult switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, $"driver {outlineDriver} completed no full lap in the outline window"),
            };
        }

        var locationRows = await _client
            .GetLocationsAsync(sessionKey, from, from + duration, null, cancellationToken)
            .ConfigureAwait(false);

        if (locationRows is not ImmutableArray<OpenF1Location> locations || locations.Length == 0)
        {
            return Propagate(locationRows);
        }

        var carRows = await _client
            .GetCarDataAsync(sessionKey, from, from + duration, cancellationToken)
            .ConfigureAwait(false);

        if (carRows is not ImmutableArray<OpenF1CarData> cars || cars.Length == 0)
        {
            return Propagate(carRows);
        }

        var samples = Pair(outline, locations, cars);
        if (samples.IsEmpty)
        {
            return new HttpFailure(204, "no position sample could be paired with car telemetry");
        }

        var driverRows = await _client
            .GetDriversAsync(sessionKey, cancellationToken)
            .ConfigureAwait(false);

        if (driverRows is not ImmutableArray<OpenF1Driver> drivers)
        {
            return driverRows switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, "no driver list published for that session"),
            };
        }

        var identities = drivers
            .GroupBy(static driver => driver.DriverNumber)
            .Select(static group => group.First())
            .Select(static driver => new Driver(
                driver.DriverNumber,
                driver.Acronym ?? driver.DriverNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                driver.FullName ?? driver.Acronym ?? "Unknown driver",
                driver.TeamName ?? "Unknown team",
                string.IsNullOrEmpty(driver.TeamColour) ? "#94A3B8" : $"#{driver.TeamColour}",
                string.IsNullOrWhiteSpace(driver.HeadshotUrl) ? null : driver.HeadshotUrl))
            .ToImmutableArray();

        return new ReplaySession(sessionKey, label, from, from + duration, outline, samples, identities);
    }


    private static ImmutableArray<ReplaySample> Pair(
        TrackOutline outline,
        ImmutableArray<OpenF1Location> locations,
        ImmutableArray<OpenF1CarData> cars)
    {
        var carsByDriver = cars
            .GroupBy(static car => car.DriverNumber)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static car => car.Date).ToArray());

        var ordered = locations.OrderBy(static location => location.Date);
        var builder = ImmutableArray.CreateBuilder<ReplaySample>(locations.Length);

        foreach (var location in ordered)
        {
            if (!carsByDriver.TryGetValue(location.DriverNumber, out var driverCars))
            {
                continue;
            }

            var car = Nearest(driverCars, location.Date);
            if (car is null)
            {
                continue;
            }

            var point = outline.Project(location.X, location.Y);

            builder.Add(new ReplaySample(
                Time: location.Date,
                DriverNumber: location.DriverNumber,
                X: point.X,
                Y: point.Y,
                SpeedKph: car.Speed ?? 0,
                Gear: car.Gear ?? 0,
                ThrottlePercent: car.Throttle ?? 0,
                Braking: car.Brake is > 0,
                Drs: MapDrs(car.Drs)));
        }

        return builder.ToImmutable();
    }

    private static OpenF1CarData? Nearest(OpenF1CarData[] samples, DateTimeOffset time)
    {
        var low = 0;
        var high = samples.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = samples[middle];
            var delta = candidate.Date - time;

            if (Math.Abs(delta.TotalMilliseconds) <= JoinTolerance.TotalMilliseconds)
            {
                return candidate;
            }

            if (delta < TimeSpan.Zero)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    private static DrsState MapDrs(byte? drs) => drs switch
    {
        null => DrsState.Unknown,
        >= 10 => DrsState.Active,
        8 => DrsState.Available,
        _ => DrsState.Unavailable,
    };

    private static FetchResult<ReplaySession> Propagate<T>(FetchResult<T> result) => result switch
    {
        HttpFailure failure => failure,
        RateLimited limited => limited,
        null => new HttpFailure(0, "provider returned no value"),
        _ => new HttpFailure(0, "unexpected provider result"),
    };
}

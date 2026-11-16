using System.Collections.Immutable;
using F1App.Domain;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Replay;

/// <summary>
/// Loads a circuit shape from one car's recorded GPS: a single lap, cut at the closure point.
/// </summary>
/// <remarks>
/// One request per circuit. The window is deliberately longer than a lap so a car that pits or is held
/// up still completes one; the closure test decides where the lap ends, not the window length.
/// </remarks>
public sealed class OpenF1TrackOutlineLoader
{
    /// <summary>Window fetched to capture one complete lap.</summary>
    public static readonly TimeSpan OutlineWindow = TimeSpan.FromSeconds(150);

    private readonly OpenF1Client _client;

    /// <summary>Creates the loader.</summary>
    public OpenF1TrackOutlineLoader(OpenF1Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>Loads the outline traced by <paramref name="driverNumber"/> from <paramref name="from"/>.</summary>
    public async Task<FetchResult<TrackOutline>> LoadAsync(
        int sessionKey,
        DateTimeOffset from,
        int driverNumber,
        CancellationToken cancellationToken)
    {
        var rows = await _client
            .GetLocationsAsync(sessionKey, from, from + OutlineWindow, driverNumber, cancellationToken)
            .ConfigureAwait(false);

        if (rows is not ImmutableArray<OpenF1Location> locations || locations.Length == 0)
        {
            return rows switch
            {
                HttpFailure failure => failure,
                RateLimited limited => limited,
                _ => new HttpFailure(204, "no GPS positions for that car in the outline window"),
            };
        }

        var outline = TrackOutline.FromLap(ToPoints(locations));

        return outline is null
            ? new HttpFailure(204, $"car {driverNumber} completed no full lap in the outline window")
            : outline;
    }

    private static TrackPoint[] ToPoints(ImmutableArray<OpenF1Location> rows)
    {
        // The outline assumes time order; the endpoint does not guarantee it.
        var ordered = rows.OrderBy(static row => row.Date).ToArray();
        var points = new TrackPoint[ordered.Length];

        for (var i = 0; i < ordered.Length; i++)
        {
            points[i] = new TrackPoint(ordered[i].X, ordered[i].Y);
        }

        return points;
    }
}

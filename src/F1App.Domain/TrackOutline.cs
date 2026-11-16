using System.Collections.Immutable;

namespace F1App.Domain;

/// <summary>A point on a circuit map, in the provider's own (unnormalised) units.</summary>
public readonly record struct TrackPoint(float X, float Y);

/// <summary>
/// The box that the recorded lap occupied, plus the mapping into the unit square.
/// </summary>
/// <remarks>
/// Kept separate from the outline for one reason: the car positions replayed on top of the map must
/// use the same transform, or the markers drift off the track.
/// </remarks>
public readonly record struct TrackBounds(float MinX, float MinY, float SpanX, float SpanY)
{
    /// <summary>Computes the bounding box of <paramref name="points"/>.</summary>
    public static TrackBounds Of(ReadOnlySpan<TrackPoint> points)
    {
        if (points.IsEmpty)
        {
            return default;
        }

        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        return new TrackBounds(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Maps a provider coordinate into the unit square, preserving aspect ratio.</summary>
    public TrackPoint ToUnitSpace(float x, float y)
    {
        var extent = Math.Max(SpanX, SpanY);
        if (extent <= 0)
        {
            return new TrackPoint(0.5f, 0.5f);
        }

        var scale = 1f / extent;
        var offsetX = (1f - (SpanX * scale)) / 2f;
        var offsetY = (1f - (SpanY * scale)) / 2f;

        return new TrackPoint(
            offsetX + ((x - MinX) * scale),
            // Inverted inside the box: the provider's Y axis grows the opposite way to a canvas.
            offsetY + ((SpanY - (y - MinY)) * scale));
    }
}

/// <summary>
/// One closed lap of a circuit, normalised into the unit square.
/// </summary>
/// <remarks>
/// Built from recorded GPS positions, never from a hand-drawn curve: the shape is whatever the cars
/// actually drove. The lap is cut at the first return to the start point after a plausible distance,
/// so the caller does not need to know the circuit's length or the session's lap time.
/// </remarks>
public sealed record TrackOutline(ImmutableArray<TrackPoint> Points, TrackBounds Bounds)
{
    /// <summary>
    /// Length of the measured lap in metres, from the raw coordinates rather than from the drawing.
    /// </summary>
    public double LapLengthMeters { get; init; }

    /// <summary>Shortest path, in tenths of a metre, before a closure can be believed.</summary>
    public const double MinimumLapLength = 30_000;

    /// <summary>How close to the start point counts as a lap closure, in tenths of a metre (30 m).</summary>
    public const double ClosureRadius = 300;

    /// <summary>Fewer points than this cannot describe a circuit.</summary>
    public const int MinimumPoints = 32;

    /// <summary>
    /// Cuts <paramref name="raw"/> to a single closed lap and normalises it into the unit square.
    /// </summary>
    /// <returns><see langword="null"/> when the trace contains no complete lap.</returns>
    public static TrackOutline? FromLap(ReadOnlySpan<TrackPoint> raw)
    {
        if (raw.Length < MinimumPoints)
        {
            return null;
        }

        var travelled = 0d;
        var end = -1;

        for (var i = 1; i < raw.Length; i++)
        {
            travelled += Distance(raw[i - 1], raw[i]);

            if (travelled >= MinimumLapLength && Distance(raw[i], raw[0]) <= ClosureRadius)
            {
                end = i;
                break;
            }
        }

        if (end < MinimumPoints)
        {
            return null;
        }

        var lap = raw[..(end + 1)];
        var bounds = TrackBounds.Of(lap);
        var builder = ImmutableArray.CreateBuilder<TrackPoint>(lap.Length);

        foreach (var point in lap)
        {
            builder.Add(bounds.ToUnitSpace(point.X, point.Y));
        }

        return new TrackOutline(builder.MoveToImmutable(), bounds)
        {
            // Tenths of a metre on the wire.
            LapLengthMeters = travelled / 10d,
        };
    }

    /// <summary>Projects a provider coordinate onto the same map the outline uses.</summary>
    public TrackPoint Project(float x, float y) => Bounds.ToUnitSpace(x, y);

    private static double Distance(TrackPoint a, TrackPoint b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

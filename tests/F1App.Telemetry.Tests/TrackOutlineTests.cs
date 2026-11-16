using F1App.Domain;

namespace F1App.Telemetry.Tests;

public sealed class TrackOutlineTests
{
    /// <summary>A closed circle of <paramref name="radius"/> tenths of a metre, sampled to include the closing point.</summary>
    private static TrackPoint[] Circle(float radius, int segments)
    {
        var points = new TrackPoint[segments + 1];

        for (var i = 0; i <= segments; i++)
        {
            var angle = i * Math.Tau / segments;
            points[i] = new TrackPoint(
                (float)(radius * Math.Cos(angle)),
                (float)(radius * Math.Sin(angle)));
        }

        return points;
    }

    [Fact]
    public void FromLap_CutsAtTheLapClosure()
    {
        // Circumference is 2*pi*5000 = 31 416 tenths, above the 30 000 minimum, so the closure at the
        // duplicate final point is the lap boundary.
        var outline = TrackOutline.FromLap(Circle(radius: 5000, segments: 60));

        Assert.NotNull(outline);
        Assert.Equal(61, outline!.Points.Length);
    }

    [Fact]
    public void FromLap_NormalisesIntoTheUnitSquare()
    {
        var outline = TrackOutline.FromLap(Circle(radius: 5000, segments: 60));

        Assert.NotNull(outline);

        foreach (var point in outline!.Points)
        {
            Assert.InRange(point.X, 0f, 1f);
            Assert.InRange(point.Y, 0f, 1f);
        }
    }

    [Fact]
    public void FromLap_PreservesAspectRatioAndCentresTheShape()
    {
        // A 200 x 100 box maps to a half-height band centred vertically.
        var bounds = TrackBounds.Of([new TrackPoint(0, 0), new TrackPoint(200, 100)]);

        var bottomLeft = bounds.ToUnitSpace(0, 0);
        var topRight = bounds.ToUnitSpace(200, 100);

        Assert.Equal(0f, bottomLeft.X, 3);
        Assert.Equal(1f, topRight.X, 3);
        Assert.Equal(0.75f, bottomLeft.Y, 3);
        Assert.Equal(0.25f, topRight.Y, 3);
    }

    [Fact]
    public void FromLap_MeasuresTheLapLengthFromRawCoordinates()
    {
        // The 60-gon's perimeter is 60 * 2 * 5000 * sin(pi/60) tenths = 3140.157 m. The polygon is
        // slightly shorter than its circumscribing circle, which is why the measured value is used
        // rather than 2*pi*r.
        var outline = TrackOutline.FromLap(Circle(radius: 5000, segments: 60));

        Assert.NotNull(outline);
        Assert.InRange(outline!.LapLengthMeters, 3140.1, 3140.2);
    }

    [Fact]
    public void FromLap_ReturnsNullWhenTheTraceNeverCloses()
    {
        var straight = new TrackPoint[200];

        for (var i = 0; i < straight.Length; i++)
        {
            straight[i] = new TrackPoint(i * 200, 0);
        }

        Assert.Null(TrackOutline.FromLap(straight));
    }

    [Fact]
    public void FromLap_ReturnsNullForTooFewPoints()
    {
        Assert.Null(TrackOutline.FromLap(Circle(radius: 5000, segments: 8)));
        Assert.Null(TrackOutline.FromLap([]));
    }

    [Fact]
    public void Project_UsesTheOutlineTransform()
    {
        var outline = TrackOutline.FromLap(Circle(radius: 5000, segments: 60));

        Assert.NotNull(outline);

        // A point at the same place as the first sample must project onto the first outline point.
        var first = outline!.Points[0];
        var projected = outline.Project(5000f, 0f);

        Assert.Equal(first.X, projected.X, 3);
        Assert.Equal(first.Y, projected.Y, 3);
    }
}

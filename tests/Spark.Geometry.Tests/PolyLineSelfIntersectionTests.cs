using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="PolyLine.SelfIntersections"/> — `E2-T72`'s last small row.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the closing segment's adjacency.</b> On a closed polyline the last segment
/// meets the first at the start vertex, by construction — so an implementation that skips only the
/// <c>i, i + 1</c> pairs reports a plain rectangle as crossing itself at its own first corner. That
/// is the failure this member exists to avoid, and it is the assertion that catches it: a simple
/// closed polyline returns nothing.
/// </para>
/// </remarks>
public sealed class PolyLineSelfIntersectionTests
{
    private const double Loose = 1e-9;

    /// <summary><b>The branch.</b> A simple closed rectangle does not cross itself.</summary>
    [Fact]
    public void ASimpleClosedPolyLineHasNoSelfIntersection()
    {
        PolyLine rectangle = PolyLine.FromClosedPoints(
        [
            new Point3d(0, 0, 0),
            new Point3d(4, 0, 0),
            new Point3d(4, 3, 0),
            new Point3d(0, 3, 0),
        ]);

        Assert.True(rectangle.IsClosed);
        Assert.True(rectangle.SelfIntersections().IsEmpty, "a rectangle does not cross itself.");
    }

    /// <summary>Nor does a closed regular polygon, at any number of sides.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(12)]
    public void ARegularPolygonHasNoSelfIntersection(int sides)
    {
        Assert.True(PolyLine.FromRegularPolygon(Plane.WorldXY, 2.0, sides).SelfIntersections().IsEmpty);
    }

    /// <summary>
    /// <b>The genuine crossing.</b> A closed figure of eight crosses itself exactly once, at the
    /// origin — computed by hand rather than read off the implementation.
    /// </summary>
    [Fact]
    public void AFigureOfEightCrossesItselfOnce()
    {
        // Two triangles meeting at the origin, traced as one closed loop that crosses there.
        PolyLine eight = PolyLine.FromClosedPoints(
        [
            new Point3d(-2, -2, 0),
            new Point3d(2, 2, 0),
            new Point3d(2, -2, 0),
            new Point3d(-2, 2, 0),
        ]);

        CurveIntersections crossings = eight.SelfIntersections();

        CurveIntersectionPoint only = Assert.Single(crossings.Points);

        Assert.True(only.Point.DistanceTo(Point3d.Origin) < Loose, $"the crossing is at {only.Point}, not the origin.");
    }

    /// <summary>An open polyline that doubles back across itself, which is what a user draws.</summary>
    [Fact]
    public void AnOpenPolyLineThatDoublesBackIsFound()
    {
        PolyLine doubled = PolyLine.FromPoints(
        [
            new Point3d(0, 0, 0),
            new Point3d(4, 0, 0),
            new Point3d(4, 4, 0),
            new Point3d(2, -2, 0),
        ]);

        CurveIntersectionPoint only = Assert.Single(doubled.SelfIntersections().Points);

        // The last segment runs (4, 4) to (2, -2), so a point on it is (4 - 2t, 4 - 6t). It meets
        // the first segment, which is y = 0 from (0, 0) to (4, 0), at t = 2/3 - and x is then
        // 4 - 4/3 = 8/3.
        Assert.Equal(8.0 / 3.0, only.Point.X, 1e-9);
        Assert.Equal(0.0, only.Point.Y, 1e-9);
    }

    /// <summary>
    /// The parameters point back into the polyline: evaluating it at each one gives the crossing
    /// point, which is what makes the answer usable for splitting.
    /// </summary>
    [Fact]
    public void TheParametersLandOnTheCrossing()
    {
        PolyLine doubled = PolyLine.FromPoints(
        [
            new Point3d(0, 0, 0),
            new Point3d(4, 0, 0),
            new Point3d(4, 4, 0),
            new Point3d(2, -2, 0),
        ]);

        CurveIntersectionPoint only = Assert.Single(doubled.SelfIntersections().Points);

        Assert.True(doubled.PointAt(only.ParameterA).DistanceTo(only.Point) < 1e-7);
        Assert.True(doubled.PointAt(only.ParameterB).DistanceTo(only.Point) < 1e-7);
        Assert.True(only.ParameterA < only.ParameterB, "the first parameter should be the earlier one.");
    }

    /// <summary>A shared vertex between consecutive segments is not a crossing.</summary>
    [Fact]
    public void ASharedVertexIsNotACrossing()
    {
        PolyLine zigzag = PolyLine.FromPoints(
        [
            new Point3d(0, 0, 0),
            new Point3d(1, 2, 0),
            new Point3d(2, 0, 0),
            new Point3d(3, 2, 0),
            new Point3d(4, 0, 0),
        ]);

        Assert.True(zigzag.SelfIntersections().IsEmpty, "a zigzag meets itself only at its own vertices.");
    }

    /// <summary>Too few segments to cross: two segments can only meet where they already join.</summary>
    [Fact]
    public void TooFewSegmentsToCross()
    {
        PolyLine two = PolyLine.FromPoints([new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0)]);

        Assert.True(two.SelfIntersections().IsEmpty);
        Assert.Same(CurveIntersections.None, two.SelfIntersections());
    }

    /// <summary>A crossing in three dimensions is not one: the segments must actually meet.</summary>
    [Fact]
    public void SegmentsThatOnlyCrossInPlanViewDoNotCount()
    {
        PolyLine overpass = PolyLine.FromPoints(
        [
            new Point3d(0, 0, 0),
            new Point3d(4, 0, 0),
            new Point3d(4, 4, 0),
            new Point3d(2, -2, 5),
        ]);

        Assert.True(
            overpass.SelfIntersections().IsEmpty,
            "the last segment passes above the first rather than through it.");
    }

    /// <summary>Several crossings come back as several.</summary>
    [Fact]
    public void SeveralCrossingsAreAllFound()
    {
        // A closed star-like loop that crosses itself more than once.
        PolyLine star = PolyLine.FromClosedPoints(
        [
            new Point3d(0, 3, 0),
            new Point3d(2, -2, 0),
            new Point3d(-3, 1, 0),
            new Point3d(3, 1, 0),
            new Point3d(-2, -2, 0),
        ]);

        Assert.True(star.SelfIntersections().Points.Count >= 3, "a five-pointed star crosses itself repeatedly.");
    }
}

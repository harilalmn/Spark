using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Curve/curve intersection in closed form: lines, circles and arcs (`E2-T11`, step A).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every answer is checked against the curves themselves.</b> Beyond each case's own numbers, every
/// point reported must lie on both curves at the parameters reported for it, and every overlap's ends
/// likewise - measured with <see cref="Curve.PointAt(double)"/>, which the intersector does not use to
/// find them. An answer can be wrong in many ways; it cannot be wrong that way and still pass.
/// </para>
/// <para>
/// These are the oracle the general intersector will be checked against, so the cases are chosen for
/// the ways arithmetic goes wrong: skew lines, crossings just past an end, tangency, circles in
/// crossing planes, and arcs that do not reach where their circle would.
/// </para>
/// </remarks>
public sealed class AnalyticCurveIntersectionTests
{
    private const double Close = 1e-9;

    private static CurveIntersections Intersect(Curve a, Curve b)
    {
        CurveIntersections? result = AnalyticCurveIntersection.TryIntersect(a, b, Tolerance.Default);

        Assert.NotNull(result);

        foreach (CurveIntersectionPoint point in result.Points)
        {
            Assert.True(a.PointAt(point.ParameterA).DistanceTo(point.Point) <= 1e-6, $"{point} is not on the first curve");
            Assert.True(b.PointAt(point.ParameterB).DistanceTo(point.Point) <= 1e-6, $"{point} is not on the second curve");
        }

        foreach (CurveOverlap overlap in result.Overlaps)
        {
            Assert.True(a.PointAt(overlap.OnA.Min).DistanceTo(b.PointAt(overlap.OnB.Min)) <= 1e-6, $"{overlap} starts apart");
            Assert.True(a.PointAt(overlap.OnA.Max).DistanceTo(b.PointAt(overlap.OnB.Max)) <= 1e-6, $"{overlap} ends apart");
        }

        return result;
    }

    private static Line L(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(new Point3d(x0, y0, z0), new Point3d(x1, y1, z1));

    /// <summary><b>The row's first case.</b> Two lines crossing meet where they cross, halfway along each.</summary>
    [Fact]
    public void TwoLinesCrossingMeetAtTheirCrossing()
    {
        CurveIntersectionPoint point = Assert.Single(Intersect(L(0, 0, 0, 2, 2, 0), L(0, 2, 0, 2, 0, 0)).Points);

        Assert.True(point.Point.DistanceTo(new Point3d(1, 1, 0)) < Close);
        Assert.Equal(0.5, point.ParameterA, 9);
        Assert.Equal(0.5, point.ParameterB, 9);
    }

    /// <summary>Skew lines - one over the other - do not meet, however close their shadows cross.</summary>
    [Fact]
    public void SkewLinesDoNotMeet()
    {
        CurveIntersections result = Intersect(L(-1, 0, 0, 1, 0, 0), L(0, -1, 1, 0, 1, 1));

        Assert.Empty(result.Points);
        Assert.Empty(result.Overlaps);
    }

    /// <summary>Lines whose infinite extensions cross beyond an end do not meet.</summary>
    [Fact]
    public void LinesThatWouldCrossBeyondAnEndDoNotMeet() =>
        Assert.Empty(Intersect(L(0, 0, 0, 1, 0, 0), L(2, -1, 0, 2, 1, 0)).Points);

    /// <summary>Parallel lines apart share nothing.</summary>
    [Fact]
    public void ParallelLinesApartShareNothing()
    {
        CurveIntersections result = Intersect(L(0, 0, 0, 2, 0, 0), L(0, 1, 0, 2, 1, 0));

        Assert.Empty(result.Points);
        Assert.Empty(result.Overlaps);
    }

    /// <summary>Lines on one line share the stretch they both cover, in each one's own parameters.</summary>
    [Fact]
    public void LinesOnOneLineShareTheirCommonStretch()
    {
        CurveOverlap overlap = Assert.Single(Intersect(L(0, 0, 0, 2, 0, 0), L(1, 0, 0, 3, 0, 0)).Overlaps);

        Assert.Equal(0.5, overlap.OnA.Min, 9);
        Assert.Equal(1.0, overlap.OnA.Max, 9);
        Assert.Equal(0.0, overlap.OnB.Min, 9);
        Assert.Equal(0.5, overlap.OnB.Max, 9);
    }

    /// <summary>Lines meeting end to end touch at one point, not along a stretch of no length.</summary>
    [Fact]
    public void LinesMeetingEndToEndTouchAtOnePoint()
    {
        CurveIntersections result = Intersect(L(0, 0, 0, 1, 0, 0), L(1, 0, 0, 2, 0, 0));

        CurveIntersectionPoint point = Assert.Single(result.Points);
        Assert.Empty(result.Overlaps);
        Assert.Equal(1.0, point.ParameterA, 9);
        Assert.Equal(0.0, point.ParameterB, 9);
    }

    /// <summary>A line through a circle crosses it twice, at the angles the circle measures.</summary>
    [Fact]
    public void ALineThroughACircleCrossesItTwice()
    {
        CurveIntersectionPoint[] points = [.. Intersect(L(-2, 0, 0, 2, 0, 0), Circle.FromCenterRadius(Point3d.Origin, 1.0)).Points];

        Assert.Equal(2, points.Length);
        Assert.True(points[0].Point.DistanceTo(new Point3d(-1, 0, 0)) < Close);
        Assert.Equal(0.25, points[0].ParameterA, 9);
        Assert.Equal(Math.PI, points[0].ParameterB, 9);
        Assert.True(points[1].Point.DistanceTo(new Point3d(1, 0, 0)) < Close);
        Assert.Equal(0.75, points[1].ParameterA, 9);
    }

    /// <summary><b>A grazing line meets a circle once</b>, not twice a rounding error apart.</summary>
    [Fact]
    public void ALineTouchingACircleMeetsItOnce()
    {
        CurveIntersectionPoint point = Assert.Single(
            Intersect(L(-2, 1, 0, 2, 1, 0), Circle.FromCenterRadius(Point3d.Origin, 1.0)).Points);

        Assert.True(point.Point.DistanceTo(new Point3d(0, 1, 0)) < 1e-6);
        Assert.Equal(Math.PI / 2, point.ParameterB, 6);
    }

    /// <summary>A line piercing a circle's plane exactly on the circle meets it there, in 3D.</summary>
    [Fact]
    public void ALinePiercingTheCirclesPlaneOnTheCircleMeetsItOnce()
    {
        CurveIntersectionPoint point = Assert.Single(
            Intersect(L(1, 0, -1, 1, 0, 1), Circle.FromCenterRadius(Point3d.Origin, 1.0)).Points);

        Assert.True(point.Point.DistanceTo(new Point3d(1, 0, 0)) < Close);
        Assert.Equal(0.5, point.ParameterA, 9);
    }

    /// <summary>
    /// <b>An arc is not its circle.</b> A line crossing a quarter arc's circle at both ends of a
    /// diameter meets the arc only where the arc reaches - at its start.
    /// </summary>
    [Fact]
    public void AnArcMeetsALineOnlyWhereTheArcReaches()
    {
        Arc quarter = new(Plane.WorldXY, 1.0, Angle.FromDegrees(0), Angle.FromDegrees(90));

        CurveIntersectionPoint point = Assert.Single(Intersect(L(-2, 0, 0, 2, 0, 0), quarter).Points);

        Assert.True(point.Point.DistanceTo(new Point3d(1, 0, 0)) < Close);
        Assert.Equal(0.0, point.ParameterB, 9);
    }

    /// <summary>
    /// <b>A crossing a hair past an arc's end, but inside the tolerance, is that end</b> — and one
    /// outside the tolerance is nothing. The two halves are one test's worth of claim, because
    /// either on its own is satisfiable by an implementation that answers the same way every time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found by mutation while closing `E2-T32`, not by the harvest.</b> The arc's sweep test in
    /// <c>AnalyticCurveIntersection</c> admits an angle up to <c>tolerance / radius</c> past either
    /// end and snaps it to that end; removing that slack — in either direction — left the whole
    /// suite green. It is the second thing found that day of exactly this shape, after
    /// <c>CircularArcs.Includes</c> ([N175](NOTES.md)), and the shape is <b>a wrapped comparison
    /// deciding whether an angle is on a sweep</b>, which is the only place Spark's start-plus-
    /// signed-sweep representation still has to do the arithmetic the seed library got wrong
    /// everywhere.
    /// </para>
    /// <para>
    /// <b>The slack here is load-bearing where the other one was not</b>, and that is why this is a
    /// test and that one is a corrected comment: nothing else contributes the end of an arc to an
    /// intersection, so an end dropped for being 1e-16 outside its own sweep is an intersection the
    /// caller never hears about.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACrossingJustPastAnArcsEndIsTheEndWhenItIsInsideTheTolerance()
    {
        // The arc ends at 90 degrees. A radial line out through 90 degrees + delta meets the
        // circle once, at an angle the arc does not quite reach.
        Arc quarter = new(Plane.WorldXY, 10.0, Angle.FromDegrees(0), Angle.FromDegrees(90));

        // tolerance / radius is 1e-7 radians here, so this is half a slack.
        CurveIntersectionPoint inside = Assert.Single(Intersect(quarter, Radial(5e-8)).Points);

        Assert.Equal(quarter.Domain.Max, inside.ParameterA, 9);
        Assert.True(inside.Point.DistanceTo(new Point3d(0, 10, 0)) < 1e-5);

        // A hundred times the slack is a miss, and it is what makes the assertion above about the
        // tolerance rather than about the intersector answering yes to everything.
        Assert.Empty(Intersect(quarter, Radial(1e-5)).Points);

        // THE OTHER END, AND IT IS A SEPARATE BRANCH RATHER THAN A SYMMETRY. Past the end, the
        // offset from the start is a shade more than the sweep; before the start it has wrapped and
        // is a shade less than a full turn, which is a different comparison against a different
        // bound. Removing either one left the other's test green.
        CurveIntersectionPoint before = Assert.Single(Intersect(quarter, Radial(-5e-8 - (Math.PI / 2.0))).Points);

        Assert.Equal(quarter.Domain.Min, before.ParameterA, 9);
        Assert.True(before.Point.DistanceTo(new Point3d(10, 0, 0)) < 1e-5);

        Assert.Empty(Intersect(quarter, Radial(-1e-5 - (Math.PI / 2.0))).Points);
    }

    /// <summary>A line from the centre out past the circle, at an angle from the arc's end.</summary>
    /// <param name="past">How far past 90 degrees to aim, in radians.</param>
    /// <returns>The line.</returns>
    private static Line Radial(double past)
    {
        double angle = (Math.PI / 2.0) + past;

        return new Line(
            Point3d.Origin,
            new Point3d(20.0 * Math.Cos(angle), 20.0 * Math.Sin(angle), 0.0));
    }

    /// <summary>Two circles in one plane, overlapping, cross twice.</summary>
    [Fact]
    public void TwoCirclesInOnePlaneCrossTwice()
    {
        CurveIntersectionPoint[] points = [.. Intersect(
            Circle.FromCenterRadius(Point3d.Origin, 1.0),
            Circle.FromCenterRadius(new Point3d(1, 0, 0), 1.0)).Points];

        Assert.Equal(2, points.Length);
        Assert.Contains(points, p => p.Point.DistanceTo(new Point3d(0.5, Math.Sqrt(3) / 2, 0)) < Close);
        Assert.Contains(points, p => p.Point.DistanceTo(new Point3d(0.5, -Math.Sqrt(3) / 2, 0)) < Close);
    }

    /// <summary>Circles touching from outside meet once, on the line of centres.</summary>
    [Fact]
    public void CirclesTouchingFromOutsideMeetOnce()
    {
        CurveIntersectionPoint point = Assert.Single(Intersect(
            Circle.FromCenterRadius(Point3d.Origin, 1.0),
            Circle.FromCenterRadius(new Point3d(2, 0, 0), 1.0)).Points);

        Assert.True(point.Point.DistanceTo(new Point3d(1, 0, 0)) < Close);
    }

    /// <summary>
    /// A circle touching the inside of a larger one meets it once, on the far side - asked both ways
    /// round, because which circle comes first decides which side of the first centre the point is on.
    /// </summary>
    [Fact]
    public void CirclesTouchingFromInsideMeetOnce()
    {
        Circle large = Circle.FromCenterRadius(Point3d.Origin, 2.0);
        Circle small = Circle.FromCenterRadius(new Point3d(1, 0, 0), 1.0);

        CurveIntersectionPoint largeFirst = Assert.Single(Intersect(large, small).Points);
        CurveIntersectionPoint smallFirst = Assert.Single(Intersect(small, large).Points);

        Assert.True(largeFirst.Point.DistanceTo(new Point3d(2, 0, 0)) < Close);
        Assert.True(smallFirst.Point.DistanceTo(new Point3d(2, 0, 0)) < Close);
    }

    /// <summary>Concentric circles of different sizes never meet.</summary>
    [Fact]
    public void ConcentricCirclesOfDifferentSizesDoNotMeet() =>
        Assert.Empty(Intersect(
            Circle.FromCenterRadius(Point3d.Origin, 1.0),
            Circle.FromCenterRadius(Point3d.Origin, 2.0)).Points);

    /// <summary>
    /// <b>Circles in crossing planes</b> meet only where the line their planes share crosses both: a
    /// circle in XY and one in XZ, both at the origin, meet at the two ends of the x axis.
    /// </summary>
    [Fact]
    public void CirclesInCrossingPlanesMeetWhereBothAre()
    {
        CurveIntersectionPoint[] points = [.. Intersect(
            Circle.FromCenterRadius(Point3d.Origin, 1.0),
            new Circle(Point3d.Origin, Vector3d.YAxis, 1.0)).Points];

        Assert.Equal(2, points.Length);
        Assert.Contains(points, p => p.Point.DistanceTo(new Point3d(1, 0, 0)) < Close);
        Assert.Contains(points, p => p.Point.DistanceTo(new Point3d(-1, 0, 0)) < Close);
    }

    /// <summary>A circle and its copy are one overlap, the whole of each.</summary>
    [Fact]
    public void ACircleAndItsCopyAreOneOverlap()
    {
        CurveOverlap overlap = Assert.Single(Intersect(
            Circle.FromCenterRadius(Point3d.Origin, 1.0),
            Circle.FromCenterRadius(Point3d.Origin, 1.0)).Overlaps);

        Assert.Equal(2 * Math.PI, overlap.OnA.Length, 9);
    }

    /// <summary>Two arcs of one circle share the stretch both cover.</summary>
    [Fact]
    public void ArcsOfOneCircleShareTheirCommonStretch()
    {
        Arc first = new(Plane.WorldXY, 1.0, Angle.FromDegrees(0), Angle.FromDegrees(90));
        Arc second = new(Plane.WorldXY, 1.0, Angle.FromDegrees(45), Angle.FromDegrees(90));

        CurveOverlap overlap = Assert.Single(Intersect(first, second).Overlaps);

        Assert.Equal(Math.PI / 4, overlap.OnA.Min, 9);
        Assert.Equal(Math.PI / 2, overlap.OnA.Max, 9);
        Assert.Equal(0.0, overlap.OnB.Min, 9);
        Assert.Equal(Math.PI / 4, overlap.OnB.Max, 9);
    }

    /// <summary>Swapping the curves swaps the parameters and changes nothing else.</summary>
    [Fact]
    public void SwappingTheCurvesSwapsTheParameters()
    {
        Curve line = L(-2, 0.3, 0, 2, 0.3, 0);
        Curve circle = Circle.FromCenterRadius(Point3d.Origin, 1.0);

        CurveIntersectionPoint[] forward = [.. Intersect(line, circle).Points];
        CurveIntersectionPoint[] backward = [.. Intersect(circle, line).Points.OrderBy(p => p.ParameterB)];

        Assert.Equal(2, forward.Length);
        Assert.Equal(forward.Length, backward.Length);

        for (int i = 0; i < forward.Length; i++)
        {
            Assert.Equal(forward[i].ParameterA, backward[i].ParameterB, 12);
            Assert.Equal(forward[i].ParameterB, backward[i].ParameterA, 12);
        }
    }

    /// <summary>A pair with no closed form here is answered as not answered, for the general case.</summary>
    [Fact]
    public void APairWithNoClosedFormIsLeftToTheGeneralCase() =>
        Assert.Null(AnalyticCurveIntersection.TryIntersect(
            L(0, 0, 0, 1, 0, 0),
            new EllipseCurve(Plane.WorldXY, 2.0, 1.0),
            Tolerance.Default));
}

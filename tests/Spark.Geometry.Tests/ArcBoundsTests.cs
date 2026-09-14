using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The bounding box of a <b>partial</b> arc or ellipse — the half of
/// <c>CircularArcs.Bounds</c> that decides which of the four axis extrema actually lie on the
/// sweep (<c>E2-T32</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Harvested from DoodleSharp, which had the assertions and this repository did not.</b> Its
/// <c>SweepAndOrientationTests</c> pins arc and ellipse bounds to the swept portion rather than to
/// the whole circle. Spark's implementation is already the better one — it finds each world axis's
/// extremum analytically, for any orientation, where DoodleSharp works in the plane — but
/// <b>nothing here tested the sweep half of it</b>. The only bounding-box test on this family was
/// <c>ACirclesBoundingBoxIsExactOnATiltedPlane</c>, and a full circle includes every extremum, so
/// the inclusion test it is asserting through is vacuous.
/// </para>
/// <para>
/// <b>Proved to be a gap before it was filled</b>, which is the rule for anything harvested from
/// another repository: forcing <c>CircularArcs.Includes</c> to return <see langword="true"/> — so
/// that every arc reports the bounding box of its entire circle — left all 3,947 tests green.
/// A wrong box is not a visible failure either: it makes selection, culling and BVH queries
/// conservative rather than incorrect, so it is exactly the kind of fault that survives until
/// somebody measures something.
/// </para>
/// <para>
/// <b>Containment alone does not catch it and tightness does.</b> The box of the whole circle
/// contains every point of the arc, so a test that only samples and checks containment passes
/// against the broken implementation. <see cref="AssertBoundsAreTightAround"/> therefore asserts
/// both directions: the box holds every sample, and no face of it stands off the samples by more
/// than the sampling error.
/// </para>
/// </remarks>
public class ArcBoundsTests
{
    /// <summary>
    /// How far a face of the box may stand off the densest sample, in model units.
    /// </summary>
    /// <remarks>
    /// The box is exact and the samples are not: an extremum falls between two of them, and the
    /// coordinate there is short of the true extreme by about <c>r · (Δθ)² / 2</c>. At 4,000
    /// samples over a full turn and a radius of 10 that is about 2e-5, so 1e-3 is loose enough to
    /// never flake and five orders of magnitude tighter than the fault it is written against,
    /// which stands a face off by the best part of a radius.
    /// </remarks>
    private const double Slack = 1e-3;

    private const int Samples = 4000;

    /// <summary>
    /// <b>A quarter arc is bounded by the quarter, not by the circle.</b> The plainest statement of
    /// the rule, with every number exact: an arc from 0 to 90 degrees on a circle of radius ten
    /// about the origin occupies the first quadrant and nothing else.
    /// </summary>
    [Fact]
    public void AQuarterArcIsBoundedByTheQuarterAndNotByItsCircle()
    {
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 10.0, Angle.FromDegrees(0.0), Angle.FromDegrees(90.0));

        BoundingBox box = arc.BoundingBox;

        Assert.Equal(0.0, box.Min.X, 1e-9);
        Assert.Equal(0.0, box.Min.Y, 1e-9);
        Assert.Equal(10.0, box.Max.X, 1e-9);
        Assert.Equal(10.0, box.Max.Y, 1e-9);
    }

    /// <summary>
    /// <b>A full turn still bounds the whole circle</b>, which is the half that was already covered
    /// and is kept because it is the boundary case of the rule above: an implementation that
    /// tightened arcs by simply taking the box of the two end points would pass the quarter and
    /// fail here, where the end points coincide.
    /// </summary>
    [Fact]
    public void AFullTurnStillBoundsTheWholeCircle()
    {
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.FromOriginXAxisYAxis(new Point3d(1.0, 2.0, 0.0), Vector3d.XAxis, Vector3d.YAxis),
            10.0,
            Angle.FromDegrees(0.0),
            Angle.FromDegrees(360.0));

        BoundingBox box = arc.BoundingBox;

        Assert.Equal(-9.0, box.Min.X, 1e-9);
        Assert.Equal(-8.0, box.Min.Y, 1e-9);
        Assert.Equal(11.0, box.Max.X, 1e-9);
        Assert.Equal(12.0, box.Max.Y, 1e-9);
    }

    /// <summary>
    /// <b>A clockwise arc is bounded the same as the counter-clockwise arc over the same span.</b>
    /// Spark normalises a negative sweep by flipping the plane's y axis rather than by carrying a
    /// sign, so this is really a test that the flip and the bounds agree — the two ends and the
    /// extrema between them are the same set of points either way round, and a box that depended
    /// on the direction of travel would be reporting the route rather than the shape.
    /// </summary>
    [Fact]
    public void ADirectionOfTravelDoesNotChangeTheBox()
    {
        BoundingBox forward = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 10.0, Angle.FromDegrees(0.0), Angle.FromDegrees(90.0)).BoundingBox;

        BoundingBox backward = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 10.0, Angle.FromDegrees(90.0), Angle.FromDegrees(-90.0)).BoundingBox;

        Assert.Equal(forward.Min.X, backward.Min.X, 1e-9);
        Assert.Equal(forward.Min.Y, backward.Min.Y, 1e-9);
        Assert.Equal(forward.Max.X, backward.Max.X, 1e-9);
        Assert.Equal(forward.Max.Y, backward.Max.Y, 1e-9);
    }

    /// <summary>
    /// <b>Every sweep, including the ones that cross the start of the turn.</b> The inclusion test
    /// works in an offset from the start angle, wrapped into a full turn, so a sweep that straddles
    /// zero — or one whose start angle is negative, or greater than a full turn — is where an
    /// arithmetic slip shows.
    /// </summary>
    /// <param name="start">The start angle, in degrees.</param>
    /// <param name="sweep">The sweep, in degrees.</param>
    [Theory]
    [InlineData(0.0, 90.0)]
    [InlineData(350.0, 20.0)]      // crosses zero
    [InlineData(-45.0, 245.0)]     // starts before zero
    [InlineData(10.0, 340.0)]      // nearly the whole circle, one corner missing
    [InlineData(0.0, 360.0)]       // the whole circle
    [InlineData(89.999, 0.002)]    // a sliver straddling an extremum
    [InlineData(400.0, 100.0)]     // a start angle past a full turn
    public void AnArcsBoxIsTightAroundTheSweptPortion(double start, double sweep)
    {
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 7.0, Angle.FromDegrees(start), Angle.FromDegrees(sweep));

        AssertBoundsAreTightAround(arc);
    }

    /// <summary>
    /// <b>And on a tilted plane, which is the case the seed library could not express at all.</b>
    /// DoodleSharp's arcs are two-dimensional, so its tests pin the extrema to the plane's own
    /// axes; Spark's arcs carry a frame, and the extremum of a world axis then falls at an angle
    /// that has nothing to do with the arc's own. That is the arithmetic worth testing, and it is
    /// only reachable here.
    /// </summary>
    /// <param name="start">The start angle, in degrees.</param>
    /// <param name="sweep">The sweep, in degrees.</param>
    [Theory]
    [InlineData(0.0, 90.0)]
    [InlineData(300.0, 150.0)]
    [InlineData(-120.0, 200.0)]
    [InlineData(0.0, 360.0)]
    public void ATiltedArcsBoxIsTightInWorldAxes(double start, double sweep)
    {
        Plane plane = Plane.FromOriginNormal(
            new Point3d(-2.0, 5.0, 1.0), new Vector3d(1.0, 2.0, 3.0));

        Arc arc = Arc.FromPlaneRadiusAngles(
            plane, 4.0, Angle.FromDegrees(start), Angle.FromDegrees(sweep));

        AssertBoundsAreTightAround(arc);
    }

    /// <summary>
    /// <b>An extremum that lands exactly on the end of the sweep is still bounded.</b> An arc from
    /// 30 degrees sweeping 60 ends exactly on the +y extremum, and in doubles the offset from the
    /// start overshoots the sweep by 2.2e-16 — so a strict comparison drops it.
    /// </summary>
    /// <remarks>
    /// <b>This test cannot be made to fail by removing the slack in <c>CircularArcs.Includes</c>,
    /// and that is the finding rather than a weakness.</b> The comment there claimed the slack was
    /// load-bearing; removing it left every one of these tests green, because <c>Bounds</c> seeds
    /// its box with both end points and this extremum <i>is</i> the end point. The case is pinned
    /// here anyway so that the next reader meets the boundary with a number attached instead of
    /// deriving 30-and-60 again. See [N175].
    /// </remarks>
    [Fact]
    public void AnExtremumExactlyAtTheEndOfTheSweepIsStillBounded()
    {
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 10.0, Angle.FromDegrees(30.0), Angle.FromDegrees(60.0));

        BoundingBox box = arc.BoundingBox;

        Assert.Equal(10.0, box.Max.Y, 1e-12);
        Assert.Equal(10.0 * Math.Cos(Math.PI / 6.0), box.Max.X, 1e-12);
        Assert.Equal(0.0, box.Min.X, 1e-12);
        Assert.Equal(10.0 * Math.Sin(Math.PI / 6.0), box.Min.Y, 1e-12);
    }

    /// <summary>
    /// <b>Half an ellipse is bounded by the half.</b> The exact statement for the elliptical case:
    /// the upper half of a 10 by 5 ellipse spans the whole width and only the upper half of the
    /// height.
    /// </summary>
    [Fact]
    public void AHalfEllipseIsBoundedByTheHalf()
    {
        EllipseCurve ellipse = new(
            Plane.WorldXY, 10.0, 5.0, Angle.FromDegrees(0.0), Angle.FromDegrees(180.0));

        BoundingBox box = ellipse.BoundingBox;

        Assert.Equal(-10.0, box.Min.X, 1e-9);
        Assert.Equal(0.0, box.Min.Y, 1e-9);
        Assert.Equal(10.0, box.Max.X, 1e-9);
        Assert.Equal(5.0, box.Max.Y, 1e-9);
    }

    /// <summary>
    /// <b>A turned ellipse's box follows its frame rather than its radii.</b> The same ellipse with
    /// its x axis laid along world Y is ten tall and five wide, and an implementation that read the
    /// radii off as world extents would answer the other way round.
    /// </summary>
    [Fact]
    public void ATurnedEllipsesBoxFollowsItsFrame()
    {
        EllipseCurve ellipse = new(
            Plane.FromOriginXAxisYAxis(Point3d.Origin, Vector3d.YAxis, -Vector3d.XAxis), 10.0, 5.0);

        BoundingBox box = ellipse.BoundingBox;

        Assert.Equal(-5.0, box.Min.X, 1e-9);
        Assert.Equal(-10.0, box.Min.Y, 1e-9);
        Assert.Equal(5.0, box.Max.X, 1e-9);
        Assert.Equal(10.0, box.Max.Y, 1e-9);
    }

    /// <summary>
    /// <b>Every sweep of an ellipse, on a frame that is turned to no particular angle.</b> An
    /// ellipse's extremum in a world axis is not at a quarter turn of its own parameter — it is at
    /// <c>atan2</c> of the two radius vectors' components — so an off-axis frame is what separates
    /// an implementation that solves for it from one that samples the four cardinal angles.
    /// </summary>
    /// <param name="start">The start angle, in degrees.</param>
    /// <param name="sweep">The sweep, in degrees.</param>
    [Theory]
    [InlineData(0.0, 360.0)]
    [InlineData(0.0, 180.0)]
    [InlineData(200.0, 200.0)]
    [InlineData(350.0, 20.0)]
    [InlineData(-30.0, 75.0)]
    public void AnEllipsesBoxIsTightAroundTheSweptPortion(double start, double sweep)
    {
        Plane frame = Plane.FromOriginXAxisYAxis(
            new Point3d(3.0, -1.0, 0.0),
            new Vector3d(Math.Cos(0.4), Math.Sin(0.4), 0.0),
            new Vector3d(-Math.Sin(0.4), Math.Cos(0.4), 0.0));

        EllipseCurve ellipse = new(
            frame, 10.0, 4.0, Angle.FromDegrees(start), Angle.FromDegrees(sweep));

        AssertBoundsAreTightAround(ellipse);
    }

    /// <summary>
    /// Asserts that a curve's box contains every sampled point and stands off none of them by more
    /// than <see cref="Slack"/> — which together say the box is the box of the swept portion.
    /// </summary>
    /// <param name="curve">The curve to sample.</param>
    private static void AssertBoundsAreTightAround(Curve curve)
    {
        BoundingBox box = curve.BoundingBox;
        Interval domain = curve.Domain;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (int i = 0; i <= Samples; i++)
        {
            Point3d point = curve.PointAt(domain.Min + ((domain.Max - domain.Min) * i / Samples));

            Assert.True(box.Contains(point), $"The box {box} does not contain the sample {point}.");

            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }

        AssertFaceIsOnTheSamples("min x", box.Min.X, minX);
        AssertFaceIsOnTheSamples("min y", box.Min.Y, minY);
        AssertFaceIsOnTheSamples("min z", box.Min.Z, minZ);
        AssertFaceIsOnTheSamples("max x", box.Max.X, maxX);
        AssertFaceIsOnTheSamples("max y", box.Max.Y, maxY);
        AssertFaceIsOnTheSamples("max z", box.Max.Z, maxZ);
    }

    /// <summary>Asserts one face of the box sits on the samples rather than out beyond them.</summary>
    /// <param name="face">Which face, for the message.</param>
    /// <param name="reported">Where the box puts it.</param>
    /// <param name="sampled">The extreme the samples reached.</param>
    private static void AssertFaceIsOnTheSamples(string face, double reported, double sampled) =>
        Assert.True(
            Math.Abs(reported - sampled) <= Slack,
            $"The {face} face is at {reported} and the curve only reaches {sampled}, "
            + $"which is {Math.Abs(reported - sampled)} of slack — the box is not the box of the "
            + "swept portion.");
}

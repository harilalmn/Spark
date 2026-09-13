using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Curve/surface intersection (<c>E2-T70</c>, step B).
/// </summary>
/// <remarks>
/// <para>
/// <b>The oracle is a closed form the intersector does not use.</b> A line against a plane has an
/// exact answer in one division, and <see cref="LineAgainstPlaneAgreesWithTheClosedForm"/> computes
/// it here and requires the sampled-and-refined path to match it to a nanometre, over a hundred
/// pseudo-random lines. That is the same discipline <c>E2-T11</c> step B was held to: a general
/// intersector that agrees with an exact one on every case the exact one can answer is not
/// self-consistent, it is right.
/// </para>
/// <para>
/// <b>Every reported point is then checked against the geometry itself</b> - it must lie on the
/// curve at the parameter reported and on the surface at the <see cref="UV"/> reported, measured
/// with <see cref="Curve.PointAt(double)"/> and <see cref="Surface.PointAt(double, double)"/>, which
/// is how a result that is plausible but not a solution fails.
/// </para>
/// </remarks>
public sealed class CurveSurfaceIntersectionTests
{
    private const double Close = 1e-6;

    /// <summary>A plane surface big enough that its patch edges are not the thing under test.</summary>
    private static PlaneSurface WidePlane(in Plane plane) => PlaneSurface.FromPlaneSize(plane, 1000.0, 1000.0);

    private static void AssertOnBoth(Curve curve, Surface surface, CurveSurfaceIntersectionPoint hit)
    {
        Assert.True(
            curve.PointAt(hit.Parameter).DistanceTo(hit.Point) <= Close,
            $"the point is not on the curve at parameter {hit.Parameter}.");

        Assert.True(
            surface.PointAt(hit.Uv.U, hit.Uv.V).DistanceTo(hit.Point) <= Close,
            $"the point is not on the surface at {hit.Uv}.");

        Assert.True(curve.Domain.Min - Close <= hit.Parameter && hit.Parameter <= curve.Domain.Max + Close);
        Assert.True(surface.DomainU.Min - Close <= hit.Uv.U && hit.Uv.U <= surface.DomainU.Max + Close);
        Assert.True(surface.DomainV.Min - Close <= hit.Uv.V && hit.Uv.V <= surface.DomainV.Max + Close);
    }

    /// <summary>
    /// The anchor. A line meets a plane where <c>(O − P) · N + s (D · N) = 0</c>, which is one
    /// division; the intersector finds the same point by sampling a signed distance and refining.
    /// Remove the Newton refinement from <c>CurveSurfaceIntersection.Add</c> and this goes red, which
    /// is what makes it the test that matters.
    /// </summary>
    [Fact]
    public void LineAgainstPlaneAgreesWithTheClosedForm()
    {
        Random random = new(20260913);

        for (int i = 0; i < 100; i++)
        {
            Point3d origin = new(random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, -5.0);
            Point3d end = new(random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, 5.0);
            Line line = Line.FromStartPointEndPoint(origin, end);

            Vector3d normal = new Vector3d(random.NextDouble() - 0.5, random.NextDouble() - 0.5, 1.0).Normalised();
            Plane plane = Plane.FromOriginNormal(new Point3d(0.0, 0.0, random.NextDouble() - 0.5), normal);

            // The closed form, computed here and nowhere in the kernel.
            Vector3d direction = end - origin;
            double denominator = direction.Dot(normal);
            Assert.True(Math.Abs(denominator) > 1e-6, "the generated line is parallel to the generated plane.");
            double s = (plane.Origin - origin).Dot(normal) / denominator;
            Point3d expected = origin + (direction * s);

            CurveSurfaceIntersections hits = line.IntersectWith(WidePlane(plane));

            Assert.Single(hits.Points);
            Assert.True(
                hits.Points[0].Point.DistanceTo(expected) <= 1e-9,
                $"case {i}: got {hits.Points[0].Point}, the closed form says {expected}.");

            AssertOnBoth(line, WidePlane(plane), hits.Points[0]);
        }
    }

    /// <summary>A line through the middle of a sphere leaves by the other side: two points, and the diameter apart.</summary>
    [Fact]
    public void LineThroughASphereGivesTwoPointsADiameterApart()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        Line line = Line.FromStartPointEndPoint(new Point3d(-10.0, 0.0, 0.0), new Point3d(10.0, 0.0, 0.0));

        CurveSurfaceIntersections hits = line.IntersectWith(sphere);

        Assert.Equal(2, hits.Points.Count);
        Assert.False(hits.IsEmpty);
        Assert.False(hits.LiesOnSurface);

        foreach (CurveSurfaceIntersectionPoint hit in hits.Points)
        {
            AssertOnBoth(line, sphere, hit);
            Assert.Equal(2.0, hit.Point.DistanceTo(Point3d.Origin), 6);
        }

        Assert.Equal(4.0, hits.Points[0].Point.DistanceTo(hits.Points[1].Point), 6);
        Assert.True(hits.Points[0].Parameter < hits.Points[1].Parameter, "the points are not in order along the curve.");
    }

    /// <summary>A line that passes the sphere by finds nothing, and says so rather than inventing a nearest point.</summary>
    [Fact]
    public void LineMissingASphereFindsNothing()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        Line line = Line.FromStartPointEndPoint(new Point3d(-10.0, 5.0, 0.0), new Point3d(10.0, 5.0, 0.0));

        CurveSurfaceIntersections hits = line.IntersectWith(sphere);

        Assert.True(hits.IsEmpty);
        Assert.Empty(hits.Points);
    }

    /// <summary>
    /// A crossing beyond the surface's patch is not a crossing. The plane is infinite as a
    /// <see cref="Plane"/> and finite as a <see cref="PlaneSurface"/>, and it is the surface that
    /// was asked.
    /// </summary>
    [Fact]
    public void ACrossingOutsideTheSurfacePatchIsRejected()
    {
        Plane plane = Plane.WorldXY;
        PlaneSurface patch = PlaneSurface.FromPlaneSize(plane, 2.0, 2.0);

        // Crosses z = 0 at x = 40, far outside a patch two units across.
        Line line = Line.FromStartPointEndPoint(new Point3d(40.0, 0.0, -1.0), new Point3d(40.0, 0.0, 1.0));

        Assert.True(line.IntersectWith(patch).IsEmpty);

        // The same crossing, moved onto the patch, is found.
        Line onPatch = Line.FromStartPointEndPoint(new Point3d(0.25, 0.0, -1.0), new Point3d(0.25, 0.0, 1.0));
        CurveSurfaceIntersections hits = onPatch.IntersectWith(patch);

        Assert.Single(hits.Points);
        AssertOnBoth(onPatch, patch, hits.Points[0]);
        Assert.Equal(0.0, hits.Points[0].Point.Z, 9);
    }

    /// <summary>
    /// A circle standing across a cylinder cuts it <b>four</b> times, and the arithmetic says why: a
    /// circle of radius five in the plane <c>y = 0</c> is <c>(5 cos t, 0, 5 sin t)</c>, its distance
    /// from the cylinder's axis is <c>5 |cos t|</c>, and <c>5 |cos t| = 3</c> has four roots in a
    /// turn. The first draft of this test expected two and was wrong; the intersector was not.
    /// </summary>
    [Fact]
    public void ACircleAcrossACylinderCrossesItFourTimes()
    {
        CylindricalSurface cylinder = new(Plane.WorldXY, 3.0, new Interval(-5.0, 5.0));
        Circle circle = Circle.FromCenterNormalRadius(Point3d.Origin, Vector3d.YAxis, 5.0);

        CurveSurfaceIntersections hits = circle.IntersectWith(cylinder);

        Assert.Equal(4, hits.Points.Count);

        for (int i = 1; i < hits.Points.Count; i++)
        {
            Assert.True(hits.Points[i - 1].Parameter < hits.Points[i].Parameter, "the points are not in order along the curve.");
        }

        foreach (CurveSurfaceIntersectionPoint hit in hits.Points)
        {
            AssertOnBoth(circle, cylinder, hit);

            // On the cylinder means three units from its axis, which is the world Z here.
            Assert.Equal(3.0, Math.Sqrt((hit.Point.X * hit.Point.X) + (hit.Point.Y * hit.Point.Y)), 5);
        }
    }

    /// <summary>
    /// A NURBS surface is not analytic and has no closed form anywhere, which is the case the general
    /// path exists for. The line is vertical through a saddle, so the answer is checked by the
    /// surface itself rather than by a formula.
    /// </summary>
    [Fact]
    public void ALineCrossingANurbsSurfaceIsFoundAndLandsOnIt()
    {
        Point3d[,] net = new Point3d[4, 4];

        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                double x = i - 1.5;
                double y = j - 1.5;
                net[i, j] = new Point3d(x, y, 0.35 * ((x * x) - (y * y)));
            }
        }

        NurbsSurface surface = new(new KnotVector(3, 4), new KnotVector(3, 4), net);
        Line line = Line.FromStartPointEndPoint(new Point3d(0.4, -0.3, -6.0), new Point3d(0.4, -0.3, 6.0));

        CurveSurfaceIntersections hits = line.IntersectWith(surface);

        Assert.Single(hits.Points);
        AssertOnBoth(line, surface, hits.Points[0]);
        Assert.Equal(0.4, hits.Points[0].Point.X, 5);
        Assert.Equal(-0.3, hits.Points[0].Point.Y, 5);
    }

    /// <summary>
    /// A curve lying in the surface is a shared curve rather than a list of points, and comes back
    /// saying so. Returning the samples would be reporting the sampling.
    /// </summary>
    [Fact]
    public void ACurveLyingInTheSurfaceSaysSoAndReportsNoPoints()
    {
        PlaneSurface patch = PlaneSurface.FromPlaneSize(Plane.WorldXY, 20.0, 20.0);
        Line inPlane = Line.FromStartPointEndPoint(new Point3d(-4.0, -1.0, 0.0), new Point3d(4.0, 3.0, 0.0));

        CurveSurfaceIntersections hits = inPlane.IntersectWith(patch);

        Assert.True(hits.LiesOnSurface);
        Assert.Empty(hits.Points);
        Assert.False(hits.IsEmpty);
    }

    /// <summary>
    /// A polycurve is several curves and must still give one answer per crossing, in order along the
    /// whole chain rather than per segment.
    /// </summary>
    [Fact]
    public void APolyCurveCrossingAPlaneTwiceGivesTwoOrderedPoints()
    {
        PolyCurve zigzag = PolyCurve.FromJoinedCurves(
        [
            Line.FromStartPointEndPoint(new Point3d(0.0, 0.0, -1.0), new Point3d(1.0, 0.0, 1.0)),
            Line.FromStartPointEndPoint(new Point3d(1.0, 0.0, 1.0), new Point3d(2.0, 0.0, -1.0)),
        ]);

        PlaneSurface patch = PlaneSurface.FromPlaneSize(Plane.WorldXY, 20.0, 20.0);
        CurveSurfaceIntersections hits = zigzag.IntersectWith(patch);

        Assert.Equal(2, hits.Points.Count);
        Assert.True(hits.Points[0].Parameter < hits.Points[1].Parameter);

        foreach (CurveSurfaceIntersectionPoint hit in hits.Points)
        {
            AssertOnBoth(zigzag, patch, hit);
            Assert.Equal(0.0, hit.Point.Z, 6);
        }

        Assert.Equal(0.5, hits.Points[0].Point.X, 5);
        Assert.Equal(1.5, hits.Points[1].Point.X, 5);
    }

    /// <summary>A curve nowhere near the surface is rejected by the boxes, and the answer is still right.</summary>
    [Fact]
    public void AFarAwayCurveIsRejected()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 1.0);
        Line far = Line.FromStartPointEndPoint(new Point3d(100.0, 100.0, 100.0), new Point3d(200.0, 100.0, 100.0));

        Assert.True(far.IntersectWith(sphere).IsEmpty);
    }

    /// <summary>
    /// The worked example in <c>docs/help/concepts/curves.md</c> §7, run. The topic says every
    /// example on it was run against the assembly, and this is how that stays true: the numbers in
    /// the topic's comments are asserted here, so editing one without editing the other goes red.
    /// </summary>
    [Fact]
    public void TheHelpTopicsWorkedExampleIsTrue()
    {
        Plane wall = Plane.FromOriginNormal(new Point3d(1.0, 0.0, 0.0), Vector3d.XAxis);
        PlaneSurface panel = PlaneSurface.FromPlaneSize(wall, 2.0, 2.0);
        Line sight = Line.FromStartPointEndPoint(new Point3d(0.0, 0.0, 0.3), new Point3d(3.0, 0.0, 0.3));

        CurveSurfaceIntersections hits = sight.IntersectWith(panel);

        Assert.Single(hits.Points);
        Assert.Equal(1.0, hits.Points[0].Point.X, 9);
        Assert.Equal(0.0, hits.Points[0].Point.Y, 9);
        Assert.Equal(0.3, hits.Points[0].Point.Z, 9);

        // And the topic's claim that the parameter is usable for trimming.
        Curve beforeTheWall = sight.Trimmed(new Interval(sight.Domain.Min, hits.Points[0].Parameter));
        Assert.Equal(1.0, beforeTheWall.Length, 6);

        // The topic's first "will not do": the same crossing past the end of the panel is empty.
        Line pastTheEnd = Line.FromStartPointEndPoint(new Point3d(0.0, 5.0, 0.3), new Point3d(3.0, 5.0, 0.3));
        Assert.True(pastTheEnd.IntersectWith(panel).IsEmpty);
    }

    /// <summary>The member refuses a null surface rather than returning nothing found.</summary>
    [Fact]
    public void ANullSurfaceIsRefused()
    {
        Line line = Line.FromStartPointEndPoint(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Assert.Throws<ArgumentNullException>(() => line.IntersectWith((Surface)null!));
    }

    /// <summary>
    /// The tolerance is a parameter and is used, which ADR-0010 requires of every member that
    /// compares. A tolerance coarser than the gap makes a near-miss a hit, and that is the only way
    /// to see the argument is not being ignored.
    /// </summary>
    [Fact]
    public void TheToleranceIsUsedRatherThanIgnored()
    {
        PlaneSurface patch = PlaneSurface.FromPlaneSize(Plane.WorldXY, 20.0, 20.0);

        // A line a hundredth of a unit above the plane, running parallel to it: no crossing.
        Line above = Line.FromStartPointEndPoint(new Point3d(-2.0, 0.0, 0.01), new Point3d(2.0, 0.0, 0.01));

        Assert.True(above.IntersectWith(patch).IsEmpty);
        Assert.True(above.IntersectWith(patch, Tolerance.Default.Scaled(100000.0)).LiesOnSurface);
    }
}

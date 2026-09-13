using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Surface.Project(in Point3d, in Vector3d, in Tolerance)"/> and its curve overload —
/// `E2-T66`'s fifth item.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is that this is a projection and not a nearest point.</b> On a surface the
/// direction happens to meet perpendicularly the two answers coincide, so a test built on one
/// would pass for an implementation that simply called
/// <see cref="Surface.ClosestPoint(in Point3d, out double, out double)"/>. Every claim here is
/// therefore made on a <b>tilted</b> plane, where the two are provably different points and the
/// difference is computed by hand.
/// </para>
/// <para>
/// <b>The second claim is multiplicity.</b> A line through a cylinder hits it twice, and an
/// implementation returning only the nearer would satisfy every test that expected one point.
/// </para>
/// </remarks>
public sealed class SurfaceProjectionTests
{
    private const double Loose = 1e-6;

    /// <summary>
    /// <b>The defining difference.</b> Projecting straight down onto a plane tilted at 45° lands
    /// at a point <see cref="Surface.ClosestPoint(in Point3d, out double, out double)"/> does not
    /// return — computed here rather than taken from the implementation.
    /// </summary>
    [Fact]
    public void AProjectionIsNotTheClosestPoint()
    {
        // z = x, a plane tilted 45° about the y axis, through the origin.
        PlaneSurface tilted = new(
            Plane.FromOriginXAxisYAxis(Point3d.Origin, new Vector3d(1, 0, 1).Normalised(), Vector3d.YAxis),
            new Interval(-20, 20),
            new Interval(-20, 20));

        Point3d source = new(2.0, 0.0, 6.0);

        IReadOnlyList<Point3d> projected = tilted.Project(source, -Vector3d.ZAxis);

        // Travelling straight down from (2, 0, 6) meets z = x at (2, 0, 2).
        Assert.Single(projected);
        Assert.True(
            projected[0].DistanceTo(new Point3d(2.0, 0.0, 2.0)) < Loose,
            $"the projection landed at {projected[0]}, not (2, 0, 2).");

        // The nearest point is the foot of the perpendicular, which is (4, 0, 4). A member that
        // answered that would pass on an untilted plane and fails here.
        Point3d nearest = tilted.ClosestPoint(source, out _, out _);

        Assert.True(nearest.DistanceTo(new Point3d(4.0, 0.0, 4.0)) < Loose, $"the nearest point is {nearest}.");
        Assert.True(nearest.DistanceTo(projected[0]) > 1.0, "the two answers should differ on a tilted plane.");
    }

    /// <summary>
    /// <b>A line through a cylinder hits it twice</b>, and both come back, ordered along the
    /// direction of travel.
    /// </summary>
    [Fact]
    public void ProjectingThroughACylinderFindsBothSides()
    {
        CylindricalSurface pipe = new(Plane.WorldXY, 2.0, new Interval(-3.0, 3.0));

        IReadOnlyList<Point3d> hits = pipe.Project(new Point3d(-10.0, 0.0, 1.0), Vector3d.XAxis);

        Assert.Equal(2, hits.Count);
        Assert.True(hits[0].DistanceTo(new Point3d(-2.0, 0.0, 1.0)) < Loose, $"the near hit is {hits[0]}.");
        Assert.True(hits[1].DistanceTo(new Point3d(2.0, 0.0, 1.0)) < Loose, $"the far hit is {hits[1]}.");
    }

    /// <summary>
    /// The ordering is along the direction from the point, so a point <i>inside</i> the cylinder
    /// gets the hit behind it first and the hit ahead second — negative before positive, rather
    /// than nearest first.
    /// </summary>
    [Fact]
    public void TheHitsAreOrderedAlongTheDirectionAndNotByDistance()
    {
        CylindricalSurface pipe = new(Plane.WorldXY, 2.0, new Interval(-3.0, 3.0));

        IReadOnlyList<Point3d> hits = pipe.Project(new Point3d(1.5, 0.0, 0.0), Vector3d.XAxis);

        Assert.Equal(2, hits.Count);
        Assert.True(hits[0].X < hits[1].X, "the hits should be ordered along the direction.");
        Assert.True(hits[0].DistanceTo(new Point3d(-2.0, 0.0, 0.0)) < Loose, $"the first hit is {hits[0]}.");
    }

    /// <summary>A miss is an empty answer, not an exception.</summary>
    [Fact]
    public void AMissReturnsNothing()
    {
        SphericalSurface globe = new(Plane.WorldXY, 1.0);

        Assert.Empty(globe.Project(new Point3d(50.0, 50.0, 0.0), Vector3d.ZAxis));
    }

    /// <summary>Every projected point is on the surface, over a range of surfaces and directions.</summary>
    [Theory]
    [MemberData(nameof(AnalyticSurfaceTests.EverySurface), MemberType = typeof(AnalyticSurfaceTests))]
    public void EveryProjectedPointIsOnTheSurface(Surface surface)
    {
        Vector3d direction = new Vector3d(0.3, -0.6, 1.0).Normalised();
        BoundingBox box = surface.BoundingBox;
        Point3d source = box.Center - (direction * (box.Diagonal.Length + 2.0));

        foreach (Point3d hit in surface.Project(source, direction))
        {
            Point3d nearest = surface.ClosestPoint(hit, out _, out _);

            Assert.True(hit.DistanceTo(nearest) < 1e-5, $"{surface.GetType().Name}: {hit} is not on the surface.");
        }
    }

    [Fact]
    public void AZeroDirectionIsRefused()
    {
        SphericalSurface globe = new(Plane.WorldXY, 1.0);

        Assert.Throws<ArgumentException>(() => globe.Project(Point3d.Origin, Vector3d.Zero));
        Assert.Throws<ArgumentException>(
            () => globe.Project(new Line(Point3d.Origin, new Point3d(1, 0, 0)), Vector3d.Zero));
        Assert.Throws<ArgumentNullException>(() => globe.Project(null!, Vector3d.ZAxis));
    }

    /// <summary>
    /// A curve projects to a curve on the surface: every point of the result is on the surface, and
    /// the shadow is followed rather than the straight line between its ends.
    /// </summary>
    [Fact]
    public void ACurveProjectsOntoTheSurface()
    {
        // A cylinder's axis is its base plane's normal, so a pipe lying along y needs a frame whose
        // normal is y. A vertical pipe would be missed entirely by a vertical ray, which is correct
        // and was this test's first premise.
        CylindricalSurface pipe = new(
            Plane.FromOriginNormal(Point3d.Origin, Vector3d.YAxis), 3.0, new Interval(-5.0, 5.0));
        Line overhead = new(new Point3d(-2.0, 0.0, 8.0), new Point3d(2.0, 0.0, 8.0));

        IReadOnlyList<Curve> projected = pipe.Project(overhead, -Vector3d.ZAxis);

        Curve shadow = Assert.Single(projected);

        for (int index = 0; index <= 20; index++)
        {
            Point3d point = shadow.PointAt(shadow.Domain.Denormalise(index / 20.0));

            Assert.Equal(3.0, Math.Sqrt((point.X * point.X) + (point.Z * point.Z)), 1e-2);
        }

        // The shadow curves over the pipe, so it is longer than the line it came from.
        Assert.True(shadow.Length > overhead.Length, "a shadow on a curved surface is longer than its curve.");
    }

    /// <summary>
    /// <b>The shadow breaks where it leaves the surface.</b> A line whose middle passes over a gap
    /// in the surface projects to two pieces, not one that bridges the hole — bridging would invent
    /// surface that is not there.
    /// </summary>
    [Fact]
    public void AShadowThatLeavesTheSurfaceComesBackInPieces()
    {
        // A torus seen from above has a hole in the middle. A line crossing the whole of it hits
        // the tube, misses over the hole, and hits again - which is two projections and not one.
        ToroidalSurface ring = new(Plane.WorldXY, 5.0, 1.5);
        Line across = new(new Point3d(-8.0, 0.0, 10.0), new Point3d(8.0, 0.0, 10.0));

        IReadOnlyList<Curve> projected = ring.Project(across, -Vector3d.ZAxis);

        Assert.Equal(2, projected.Count);

        foreach (Curve piece in projected)
        {
            for (int index = 0; index <= 10; index++)
            {
                Point3d point = piece.PointAt(piece.Domain.Denormalise(index / 10.0));
                Point3d nearest = ring.ClosestPoint(point, out _, out _);

                Assert.True(point.DistanceTo(nearest) < 1e-2, $"{point} is not on the torus.");
            }
        }

        // The two pieces are on opposite sides of the hole, which is what makes them two.
        Assert.True(
            projected[0].PointAt(projected[0].Domain.Mid).X * projected[1].PointAt(projected[1].Domain.Mid).X < 0.0,
            "the two pieces should be on opposite sides of the hole.");
    }

    /// <summary>A curve whose shadow misses entirely projects to nothing.</summary>
    [Fact]
    public void ACurveWhoseShadowMissesProjectsToNothing()
    {
        SphericalSurface globe = new(Plane.WorldXY, 1.0);
        Line elsewhere = new(new Point3d(50.0, 50.0, 5.0), new Point3d(60.0, 50.0, 5.0));

        Assert.Empty(globe.Project(elsewhere, -Vector3d.ZAxis));
    }

    /// <summary>
    /// Projecting a curve that already lies on the surface gives it back, within the sampling
    /// tolerance — the identity case, which a projection that quietly moved things would fail.
    /// </summary>
    [Fact]
    public void ProjectingACurveThatIsAlreadyThereChangesLittle()
    {
        PlaneSurface ground = new(Plane.WorldXY, new Interval(-10, 10), new Interval(-10, 10));
        Line onIt = new(new Point3d(-2.0, -1.0, 0.0), new Point3d(3.0, 2.0, 0.0));

        Curve projected = Assert.Single(ground.Project(onIt, -Vector3d.ZAxis));

        Assert.True(projected.StartPoint.DistanceTo(onIt.StartPoint) < 1e-6);
        Assert.True(projected.EndPoint.DistanceTo(onIt.EndPoint) < 1e-6);
        Assert.Equal(onIt.Length, projected.Length, 1e-6);
    }
}

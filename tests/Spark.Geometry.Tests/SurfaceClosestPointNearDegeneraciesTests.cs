using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Surface.ClosestPoint"/> beside a pole and an apex — `E2-T33`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by a property test's scouting, and a uniform grid of sample points misses it
/// entirely.</b> That is the whole reason this file is written the way it is. Asking a sphere for
/// the closest point to each node of a 21×21 grid gives 441 correct answers; walking *towards* a
/// pole — 0.5, 0.9, 0.99, 0.999 of the way — finds a point already on the surface coming back as
/// the pole itself, out by a sixtieth of the radius. The defect lives in the gap between the seed
/// grid's nodes, so a test whose points are on a grid cannot see it.
/// </para>
/// <para>
/// <b>What was wrong.</b> <see cref="Surface.ClosestPoint"/> seeds from a coarse grid and refines
/// with Newton on the two orthogonality conditions. For any query within half a cell of a pole,
/// the grid's nearest node <i>is</i> the pole — and at a pole one derivative vanishes, so the
/// Jacobian is singular, the first step is refused, and the search returns the point it started
/// from. It was not converging badly; it was not moving at all.
/// </para>
/// <para>
/// <b>These are written as bounds relative to the radius</b>, not as exact equalities. The claim
/// being made is that the answer is *close*, and the numbers here are far outside floating-point
/// noise: the old behaviour was out by 1.6e−2 on a unit sphere, and the tolerance below is 1e−6.
/// </para>
/// </remarks>
public sealed class SurfaceClosestPointNearDegeneraciesTests
{
    /// <summary>
    /// <b>The test the fix exists for.</b> A point on the sphere, arbitrarily close to a pole,
    /// must come back as itself.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(0.99)]
    [InlineData(0.999)]
    public void APointBesideASpheresPoleIsItsOwnClosestPoint(double towardsThePole)
    {
        SphericalSurface sphere = new(Plane.WorldXY, 1.0);

        foreach (double u in new[] { 0.0, 1.0, 2.5, 4.0, 6.0 })
        {
            double v = sphere.DomainV.Max * towardsThePole;
            Point3d on = sphere.PointAt(u, v);

            Assert.True(
                sphere.ClosestPoint(on, out _, out _).DistanceTo(on) <= 1e-6,
                $"a point at u={u}, v={v} on the sphere is not its own closest point");
        }
    }

    /// <summary>And the pole itself, which is the case that always worked.</summary>
    [Fact]
    public void APoleIsItsOwnClosestPoint()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 1.0);

        foreach (double v in new[] { sphere.DomainV.Min, sphere.DomainV.Max })
        {
            Point3d pole = sphere.PointAt(0.0, v);

            Assert.True(sphere.ClosestPoint(pole, out _, out _).DistanceTo(pole) <= 1e-9);
        }
    }

    /// <summary>
    /// A cone's apex is the other degeneracy in the library, and it is degenerate in the same
    /// direction, so it is the same test.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.001)]
    [InlineData(0.01)]
    [InlineData(0.1)]
    public void APointBesideAConesApexIsItsOwnClosestPoint(double fromTheApex)
    {
        // A cone whose apex is inside its own height range, so the tip really is on the surface.
        ConicalSurface cone = new(
            Plane.WorldXY, 1.0, Angle.FromDegrees(30.0), new Interval(-3.0, 2.0));

        double v = cone.DomainV.Denormalise(fromTheApex);

        foreach (double u in new[] { 0.0, 1.0, 2.5, 4.0, 6.0 })
        {
            Point3d on = cone.PointAt(u, v);

            Assert.True(
                cone.ClosestPoint(on, out _, out _).DistanceTo(on) <= 1e-6 * Math.Max(1.0, on.DistanceTo(Point3d.Origin)),
                $"a point at u={u}, v={v} on the cone is not its own closest point");
        }
    }

    /// <summary>
    /// <b>The answer is never worse than a dense sampling of the surface</b>, which is the only
    /// promise a closest-point routine can honestly make about an arbitrary point in space.
    /// </summary>
    /// <remarks>
    /// The surfaces that had a degeneracy to fall into are the ones asserted, and the probe points
    /// are deliberately off-centre and irrational-ish so that none of them lands on a seed node.
    /// </remarks>
    [Fact]
    public void NoSampledPointIsEverCloserThanTheAnswer()
    {
        Surface[] surfaces =
        [
            new SphericalSurface(Plane.WorldXY, 1.0),
            new ConicalSurface(Plane.WorldXY, 1.0, Angle.FromDegrees(30.0), new Interval(0.0, 2.0)),
            new CylindricalSurface(Plane.WorldXY, 1.0, new Interval(0.0, 2.0)),
            new ToroidalSurface(Plane.WorldXY, 1.0, 0.3),
        ];

        Point3d[] probes =
        [
            new(0.37, -0.11, 0.93),
            new(1.73, 0.29, -0.41),
            new(-0.07, 1.19, 2.31),
            new(0.0, 0.0, 3.7),
        ];

        foreach (Surface surface in surfaces)
        {
            foreach (Point3d probe in probes)
            {
                double answered = surface.ClosestPoint(probe, out _, out _).DistanceTo(probe);
                double sampled = double.MaxValue;

                for (int i = 0; i <= 80; i++)
                {
                    for (int j = 0; j <= 80; j++)
                    {
                        Point3d sample = surface.PointAt(
                            surface.DomainU.Denormalise(i / 80.0),
                            surface.DomainV.Denormalise(j / 80.0));

                        sampled = Math.Min(sampled, sample.DistanceTo(probe));
                    }
                }

                Assert.True(
                    answered <= sampled + 1e-9,
                    $"{surface.GetType().Name}: answered {answered}, but a sample was {sampled} from {probe}");
            }
        }
    }
}

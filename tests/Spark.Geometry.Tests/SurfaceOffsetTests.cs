using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Surface.Offset(double)"/> — `E2-T66`'s third item.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the direction, not the distance.</b> A sphere offset by <c>d</c> has radius
/// <c>r + d</c>, and so does a sphere offset the wrong way by <c>-d</c> when the test only measures
/// a radius. So the claim asserted throughout is that the offset point is the original point
/// <i>plus <c>d</c> times the original normal</i> — checked at parameters where the normals are not
/// all parallel.
/// </para>
/// <para>
/// <b>The second branch is each analytic shortcut agreeing with the general answer.</b> Every
/// special-cased type is compared against <see cref="OffsetSurface"/> over the same surface, so a
/// shortcut that returns a plausible surface of the right kind and the wrong size or place fails.
/// That is the comparison a <see cref="ConicalSurface"/> shortcut would fail, which is why there
/// is not one.
/// </para>
/// </remarks>
public sealed class SurfaceOffsetTests
{
    private const double Tight = 1e-9;
    private const double Loose = 1e-6;

    /// <summary>
    /// <b>The defining property, on every surface type.</b> The offset point is the original point
    /// moved along the original normal by the distance asked for.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyticSurfaceTests.EverySurface), MemberType = typeof(AnalyticSurfaceTests))]
    public void TheOffsetFollowsTheOriginalNormal(Surface surface)
    {
        const double distance = 0.2;
        Surface offset = surface.Offset(distance);

        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                double u = surface.DomainU.Denormalise((i + 0.5) / 6.0);
                double v = surface.DomainV.Denormalise((j + 0.5) / 6.0);

                Point3d expected = surface.PointAt(u, v) + (surface.NormalAt(u, v) * distance);
                Point3d actual = offset.PointAt(u, v);

                Assert.True(
                    expected.DistanceTo(actual) < Loose,
                    $"{surface.GetType().Name} at ({u}, {v}): expected {expected}, got {actual}.");
            }
        }
    }

    /// <summary>A negative distance goes the other way, and is not the same surface.</summary>
    [Fact]
    public void ANegativeDistanceOffsetsTheOtherWay()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 3.0);

        Surface outward = sphere.Offset(1.0);
        Surface inward = sphere.Offset(-1.0);

        Assert.Equal(4.0, Assert.IsType<SphericalSurface>(outward).Radius, Tight);
        Assert.Equal(2.0, Assert.IsType<SphericalSurface>(inward).Radius, Tight);
    }

    /// <summary>Zero is allowed, and gives a surface that evaluates to the original.</summary>
    [Fact]
    public void ZeroIsAllowedAndChangesNothing()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.0);

        Surface offset = torus.Offset(0.0);

        Assert.Equal(1.0, Assert.IsType<ToroidalSurface>(offset).MinorRadius, Tight);
        Assert.True(torus.PointAt(0.7, 1.1).DistanceTo(offset.PointAt(0.7, 1.1)) < Tight);
    }

    /// <summary>
    /// <b>The analytic shortcuts return their own kind</b>, which is the whole reason they exist:
    /// a caller who put a sphere in gets a sphere back rather than a wrapper around one.
    /// </summary>
    [Fact]
    public void TheAnalyticTypesKeepTheirTypes()
    {
        Assert.IsType<PlaneSurface>(new PlaneSurface(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3)).Offset(1.0));
        Assert.IsType<SphericalSurface>(new SphericalSurface(Plane.WorldXY, 2.0).Offset(1.0));
        Assert.IsType<CylindricalSurface>(new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)).Offset(1.0));
        Assert.IsType<ToroidalSurface>(new ToroidalSurface(Plane.WorldXY, 5.0, 2.0).Offset(1.0));
    }

    /// <summary>
    /// <b>The shortcut and the general answer are the same surface.</b> Each special-cased type is
    /// checked point for point against <see cref="OffsetSurface"/> over the same surface, which is
    /// the assertion that a plausible-but-wrong shortcut fails.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShortcutSurfaces))]
    public void EachShortcutAgreesWithTheGeneralOffset(Surface surface, double distance)
    {
        Surface shortcut = surface.Offset(distance);
        OffsetSurface general = new(surface, distance);

        Assert.NotSame(shortcut.GetType(), typeof(OffsetSurface));

        for (int i = 0; i < 7; i++)
        {
            for (int j = 0; j < 7; j++)
            {
                double u = surface.DomainU.Denormalise((i + 0.5) / 7.0);
                double v = surface.DomainV.Denormalise((j + 0.5) / 7.0);

                Assert.True(
                    shortcut.PointAt(u, v).DistanceTo(general.PointAt(u, v)) < Loose,
                    $"{surface.GetType().Name} at ({u}, {v}): the shortcut and the general offset disagree.");
            }
        }
    }

    /// <summary>The special-cased types, each with a distance that does not invert them.</summary>
    public static TheoryData<Surface, double> ShortcutSurfaces() =>
        new()
        {
            { new PlaneSurface(Plane.FromOriginNormal(new Point3d(1, 2, 3), new Vector3d(1, 1, 1)), new Interval(0, 2), new Interval(0, 3)), 0.75 },
            { new SphericalSurface(Plane.WorldXY, 2.0), 0.5 },
            { new SphericalSurface(Plane.WorldXY, 2.0), -0.5 },
            { new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)), 0.5 },
            { new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)), -0.5 },
            { new ToroidalSurface(Plane.WorldXY, 5.0, 2.0), 0.5 },
            { new ToroidalSurface(Plane.WorldXY, 5.0, 2.0), -0.5 },
        };

    /// <summary>
    /// <b>A cone is deliberately not special-cased, and this is the reason.</b> Its true offset is
    /// the same cone trimmed at a different height — the ends move along the axis as well as
    /// outwards — so the general wrapper is used and it is exactly right. The test that would
    /// catch a careless shortcut is the one above; this one pins the decision, by showing that the
    /// offset of a cone is at the offset distance from it and is <i>not</i> a cone with the same
    /// height range.
    /// </summary>
    [Fact]
    public void AConeUsesTheGeneralOffsetAndStaysAtTheRightDistance()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));
        const double distance = 0.4;

        Surface offset = cone.Offset(distance);

        Assert.IsType<OffsetSurface>(offset);

        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                double u = cone.DomainU.Denormalise((i + 0.5) / 6.0);
                double v = cone.DomainV.Denormalise((j + 0.5) / 6.0);

                Point3d point = offset.PointAt(u, v);
                Point3d nearest = cone.ClosestPoint(point, out _, out _);

                Assert.Equal(distance, point.DistanceTo(nearest), 1e-6);
            }
        }
    }

    /// <summary>
    /// <b>The inversions are refusals.</b> A sphere, a cylinder or a torus offset inwards by its
    /// own radius has nothing left to be, and a surface turned inside out is worse than an error.
    /// </summary>
    [Fact]
    public void AnOffsetThatWouldInvertTheSurfaceIsRefused()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.0);

        Assert.Throws<ArgumentException>(() => sphere.Offset(-2.0));
        Assert.Throws<ArgumentException>(() => sphere.Offset(-3.0));
        Assert.Throws<ArgumentException>(() => cylinder.Offset(-2.0));
        Assert.Throws<ArgumentException>(() => torus.Offset(-1.0));

        // A torus offset by less than its minor radius is fine, however big the major radius is.
        Assert.IsType<ToroidalSurface>(torus.Offset(-0.9));
    }

    [Fact]
    public void ANonFiniteDistanceIsRefused()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => sphere.Offset(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => sphere.Offset(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => cone.Offset(double.NaN));
        Assert.Throws<ArgumentNullException>(() => new OffsetSurface(null!, 1.0));
    }

    /// <summary>Offsetting an offset adds the distances rather than nesting the wrappers.</summary>
    [Fact]
    public void OffsettingAnOffsetAddsTheDistances()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));

        OffsetSurface twice = Assert.IsType<OffsetSurface>(cone.Offset(0.3).Offset(0.2));

        Assert.Equal(0.5, twice.Distance, Tight);
        Assert.Same(cone, twice.Basis);
    }

    /// <summary>
    /// An offset surface has no NURBS form and says so, rather than quietly fitting one. This is
    /// the honest half of `E2-T66`'s second item: the claim is refused, not faked.
    /// </summary>
    [Fact]
    public void AnOffsetSurfaceRefusesToBecomeANurbsSurface()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => cone.Offset(0.3).ToNurbsSurface());

        Assert.Contains("ApproximateWithTolerance", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rigid transform carries the offset with it: the distance is unchanged, and the offset of
    /// the moved surface is the moved offset.
    /// </summary>
    [Fact]
    public void ARigidTransformKeepsTheDistance()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));
        Transform move = Transform.Translation(new Vector3d(3, -2, 1));

        OffsetSurface moved = Assert.IsType<OffsetSurface>(cone.Offset(0.4).TransformedBy(move));

        Assert.Equal(0.4, moved.Distance, Loose);
        Assert.True(
            moved.PointAt(0.5, 2.0).DistanceTo(move.OfPoint(cone.Offset(0.4).PointAt(0.5, 2.0))) < Loose);
    }

    /// <summary>A uniform scale scales the distance with the surface.</summary>
    [Fact]
    public void AUniformScaleScalesTheDistance()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));

        OffsetSurface scaled =
            Assert.IsType<OffsetSurface>(cone.Offset(0.4).TransformedBy(Transform.Scale(Point3d.Origin, 3.0)));

        Assert.Equal(1.2, scaled.Distance, Loose);
    }

    /// <summary>The wrapper reports its domains and closedness from the surface it wraps.</summary>
    [Fact]
    public void TheWrapperInheritsTheDomainsAndTheClosedness()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0));

        Surface offset = cone.Offset(0.3);

        Assert.Equal(cone.DomainU, offset.DomainU);
        Assert.Equal(cone.DomainV, offset.DomainV);
        Assert.Equal(cone.IsClosedU, offset.IsClosedU);
        Assert.Equal(cone.IsClosedV, offset.IsClosedV);
        Assert.Contains("ConicalSurface", offset.ToString(), StringComparison.Ordinal);
    }
}

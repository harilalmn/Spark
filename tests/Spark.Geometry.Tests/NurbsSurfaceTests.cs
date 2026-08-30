using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="NurbsSurface"/> and the exact conversions onto it — `E2-T19`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The conversions are exact in <i>shape</i> and not in parameterisation</b>, and that
/// distinction is what these tests are built around. A rational quadratic traces a circular arc to
/// the last bit, but its parameter is a projective function of the angle — so the right assertion
/// is that every point of the converted surface satisfies the original's implicit equation, not
/// that the two agree point for point at the same parameter. The first version of this file
/// asserted the latter, and six tests failed for a reason that was not a bug.
/// </para>
/// <para>
/// <b>The implicit checks are the strong ones.</b> *Is every point one radius from the centre* is a
/// statement about the whole sheet that a plausible-but-wrong construction cannot satisfy, and it
/// does not care how the surface is parameterised. Exactness is asserted at 1e-9 rather than at a
/// modelling tolerance, because the point of a rational quadric is that there is no approximation
/// error at all — a test that allowed one would pass a fitted surface and leave the row unfulfilled.
/// </para>
/// </remarks>
public sealed class NurbsSurfaceTests
{
    private const double Exact = 1e-9;

    /// <summary>A bilinear surface interpolates its four corners and everything between them.</summary>
    [Fact]
    public void ABilinearSurfaceInterpolatesItsCorners()
    {
        NurbsSurface surface = NurbsSurface.ByCorners(
        [
            new Point3d(0, 0, 0),
            new Point3d(0, 4, 0),
            new Point3d(3, 0, 0),
            new Point3d(3, 4, 2),
        ]);

        Assert.Equal(new Point3d(0, 0, 0), surface.PointAt(0, 0));
        Assert.Equal(new Point3d(3, 4, 2), surface.PointAt(1, 1));
        Assert.Equal(0.5, surface.PointAt(0.5, 0.5).Z, Exact);
    }

    /// <summary>
    /// <b>The control net is <c>[u, v]</c>.</b> A transposed net evaluates without complaint and is
    /// the wrong shape, so the order is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void TheControlNetIsIndexedUThenV()
    {
        Point3d[,] net = new Point3d[2, 3];

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                net[i, j] = new Point3d(i, j, 0);
            }
        }

        NurbsSurface surface = new(KnotVector.CreateClamped(1, 2), KnotVector.CreateClamped(2, 3), net);

        Assert.Equal(2, surface.ControlPointCountU);
        Assert.Equal(3, surface.ControlPointCountV);
        Assert.Equal(new Point3d(1, 2, 0), surface.ControlPoint(1, 2));
        Assert.Equal(new Point3d(1, 2, 0), surface.PointAt(surface.DomainU.Max, surface.DomainV.Max));
    }

    /// <summary>A net whose size does not match the knot vectors is refused, with the arithmetic.</summary>
    [Fact]
    public void AMismatchedNetIsRefused()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new NurbsSurface(
                KnotVector.CreateClamped(1, 2), KnotVector.CreateClamped(2, 3), new Point3d[2, 2]));

        Assert.Contains("2x3", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A weight of zero is refused: the surface would be undefined where it vanishes.</summary>
    [Fact]
    public void AZeroWeightIsRefused() =>
        Assert.Throws<ArgumentException>(
            () => new NurbsSurface(
                KnotVector.CreateClamped(1, 2),
                KnotVector.CreateClamped(1, 2),
                new Point3d[2, 2],
                new double[2, 2]));

    /// <summary>
    /// <b>The bounding box is the control net's, and it contains the surface.</b> That is the
    /// convex-hull property, and it is why this type overrides the sampled default.
    /// </summary>
    [Fact]
    public void TheBoundingBoxComesFromTheControlNet()
    {
        NurbsSurface surface = new SphericalSurface(Plane.WorldXY, 2.0).ToNurbsSurface();
        BoundingBox box = surface.BoundingBox;

        foreach (Point3d point in Grid(surface))
        {
            Assert.True(box.Contains(point), $"the net's box does not contain {point}");
        }
    }

    /// <summary>Transforming the control net transforms the surface exactly.</summary>
    [Fact]
    public void TransformingTheNetTransformsTheSurface()
    {
        NurbsSurface surface = NurbsSurface.ByCorners(
        [
            new Point3d(0, 0, 0), new Point3d(0, 4, 0), new Point3d(3, 0, 0), new Point3d(3, 4, 0),
        ]);

        // A non-uniform scale, which every analytic surface with a radius refuses and this one
        // takes exactly, because the basis functions do not depend on where the points are.
        Transform stretch = Transform.Scale(2.0, 0.5, 1.0);

        Surface moved = surface.TransformedBy(stretch);

        Assert.Equal(stretch.OfPoint(surface.PointAt(0.5, 0.5)).X, moved.PointAt(0.5, 0.5).X, Exact);
        Assert.Equal(stretch.OfPoint(surface.PointAt(0.5, 0.5)).Y, moved.PointAt(0.5, 0.5).Y, Exact);
    }

    // -- The exact conversions -------------------------------------------------------------------

    /// <summary>
    /// <b>A plane rectangle converts point for point</b>, because a bilinear surface's
    /// parameterisation <i>is</i> the plane's — no rational term, so nothing is reparameterised.
    /// </summary>
    [Fact]
    public void APlaneConvertsPointForPoint()
    {
        PlaneSurface plane = new(Plane.WorldXY, new Interval(-1, 2), new Interval(0.5, 4));
        NurbsSurface converted = plane.ToNurbsSurface();

        for (int i = 0; i <= 7; i++)
        {
            for (int j = 0; j <= 7; j++)
            {
                double u = plane.DomainU.Denormalise(i / 7.0);
                double v = plane.DomainV.Denormalise(j / 7.0);

                Assert.Equal(0.0, plane.PointAt(u, v).DistanceTo(converted.PointAt(u, v)), Exact);
            }
        }
    }

    /// <summary>
    /// <b>Every point of a converted cylinder is exactly one radius from the axis</b>, at a height
    /// on the cylinder. That is the cylinder's implicit equation, and satisfying it everywhere is
    /// what <i>exactly the same sheet</i> means.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cylinders))]
    public void AConvertedCylinderSatisfiesTheCylindersEquation(CylindricalSurface cylinder)
    {
        foreach (Point3d point in Grid(cylinder.ToNurbsSurface()))
        {
            Vector3d offset = point - cylinder.Frame.Origin;
            double height = offset.Dot(cylinder.Axis);
            double radial = (offset - (cylinder.Axis * height)).Length;

            Assert.Equal(cylinder.Radius, radial, Exact);
            Assert.True(
                cylinder.DomainV.Includes(height),
                $"the height {height} is outside the cylinder's own extent");
        }
    }

    /// <summary>
    /// Every point of a converted cone is at exactly the radius its height calls for, which is the
    /// cone's implicit equation and the thing a taper is easiest to get wrong.
    /// </summary>
    [Fact]
    public void AConvertedConeSatisfiesTheConesEquation()
    {
        ConicalSurface cone = new(Plane.WorldXY, 1.0, Angle.FromRadians(0.4), new Interval(0.0, 4.0));

        foreach (Point3d point in Grid(cone.ToNurbsSurface()))
        {
            Vector3d offset = point - cone.Frame.Origin;
            double height = offset.Dot(cone.Frame.Normal);
            double radial = (offset - (cone.Frame.Normal * height)).Length;

            Assert.Equal(cone.RadiusAt(height), radial, Exact);
        }
    }

    /// <summary>Every point of a converted sphere is exactly one radius from the centre.</summary>
    [Theory]
    [MemberData(nameof(Spheres))]
    public void AConvertedSphereSatisfiesTheSpheresEquation(SphericalSurface sphere)
    {
        foreach (Point3d point in Grid(sphere.ToNurbsSurface()))
        {
            Assert.Equal(sphere.Radius, point.DistanceTo(sphere.Centre), Exact);
        }
    }

    /// <summary>
    /// Every point of a converted torus satisfies its implicit equation, which is the sharpest of
    /// the five: it comes out wrong if either direction's weights are wrong.
    /// </summary>
    [Fact]
    public void AConvertedTorusSatisfiesTheTorussEquation()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.5);

        foreach (Point3d point in Grid(torus.ToNurbsSurface()))
        {
            Vector3d offset = point - torus.Frame.Origin;
            double height = offset.Dot(torus.Frame.Normal);
            double radial = (offset - (torus.Frame.Normal * height)).Length;
            double fromTube = Math.Sqrt(
                ((radial - torus.MajorRadius) * (radial - torus.MajorRadius)) + (height * height));

            Assert.Equal(torus.MinorRadius, fromTube, Exact);
        }
    }

    /// <summary>
    /// <b>The four corners line up, which is what makes a patch convert to a patch.</b> The
    /// parameterisation inside is not preserved, but the domains and therefore the extent are —
    /// and that is what trimming and a BRep face rely on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Spheres))]
    public void TheCornersAndDomainsLineUp(SphericalSurface sphere)
    {
        NurbsSurface converted = sphere.ToNurbsSurface();

        Assert.Equal(sphere.DomainU.Min, converted.DomainU.Min, Exact);
        Assert.Equal(sphere.DomainU.Max, converted.DomainU.Max, Exact);
        Assert.Equal(sphere.DomainV.Min, converted.DomainV.Min, Exact);
        Assert.Equal(sphere.DomainV.Max, converted.DomainV.Max, Exact);

        (double, double)[] corners =
        [
            (sphere.DomainU.Min, sphere.DomainV.Min),
            (sphere.DomainU.Min, sphere.DomainV.Max),
            (sphere.DomainU.Max, sphere.DomainV.Min),
            (sphere.DomainU.Max, sphere.DomainV.Max),
        ];

        foreach ((double u, double v) in corners)
        {
            Assert.Equal(0.0, sphere.PointAt(u, v).DistanceTo(converted.PointAt(u, v)), Exact);
        }
    }

    /// <summary>
    /// <b>The parameterisation is deliberately not preserved, and the test says so out loud.</b> A
    /// reader who assumed otherwise would write code that is subtly wrong everywhere except at the
    /// corners, so the difference is pinned by an assertion rather than left as a remark somebody
    /// might not read.
    /// </summary>
    [Fact]
    public void TheParameterisationIsNotPreservedAndThatIsExpected()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        NurbsSurface converted = sphere.ToNurbsSurface();

        // A quarter of the way into the first span is not 22.5 degrees along the arc.
        double u = sphere.DomainU.Denormalise(0.25 / 4.0);

        Assert.True(
            sphere.PointAt(u, 0.0).DistanceTo(converted.PointAt(u, 0.0)) > 1e-3,
            "if these agreed the conversion would be reparameterising rather than converting, and "
            + "a reparameterised circle cannot be exact");
    }

    /// <summary>
    /// A converted sphere's area agrees with 4πr², which is an independent check on the whole
    /// chain: the weights, the knots, the derivatives and the quadrature.
    /// </summary>
    [Fact]
    public void AConvertedSpheresAreaIsStillFourPiRSquared() =>
        Assert.Equal(
            4.0 * Math.PI * 4.0,
            new SphericalSurface(Plane.WorldXY, 2.0).ToNurbsSurface().Area,
            1e-4);

    /// <summary>
    /// <b>A converted cylinder's normal points the same way the original's does at that point.</b>
    /// The positions alone would not say so: a sheet can be exactly right and oriented inside out,
    /// which is how a solid built from converted faces ends up with its inside on the outside.
    /// </summary>
    [Fact]
    public void AConvertedCylindersNormalsAgree()
    {
        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));
        NurbsSurface converted = cylinder.ToNurbsSurface();

        for (int i = 1; i < 8; i++)
        {
            double u = converted.DomainU.Denormalise(i / 8.0);

            Point3d point = converted.PointAt(u, 2.0);
            Vector3d actual = converted.NormalAt(u, 2.0);

            // Where that point *is* on the cylinder, whatever parameter it corresponds to — which
            // is the only way to compare two surfaces that do not share a parameterisation.
            cylinder.ClosestPoint(point, out double originalU, out double originalV);

            Assert.Equal(1.0, cylinder.NormalAt(originalU, originalV).Dot(actual), 1e-6);
        }
    }

    /// <summary>Cylinders whose conversion is checked: a whole one and a patch.</summary>
    public static TheoryData<CylindricalSurface> Cylinders() =>
    [
        new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)),
        new CylindricalSurface(Plane.WorldXZ, 1.5, new Interval(0.3, 2.2), new Interval(-1.0, 3.0)),
    ];

    /// <summary>Spheres whose conversion is checked: a whole one and a patch.</summary>
    public static TheoryData<SphericalSurface> Spheres() =>
    [
        new SphericalSurface(Plane.WorldXY, 2.5),
        new SphericalSurface(Plane.WorldXY, 2.5, new Interval(0.2, 3.0), new Interval(-0.5, 1.1)),
    ];

    /// <summary>Points across a surface, sampled on an odd grid.</summary>
    /// <remarks>
    /// <b>Odd on purpose.</b> An even grid lands on span boundaries, which is exactly where a wrong
    /// rational construction is still right — the control points are on the curve there. Seventeen
    /// steps put most samples in the middle of a span, where a wrong weight shows.
    /// </remarks>
    private static IEnumerable<Point3d> Grid(Surface surface)
    {
        for (int i = 0; i <= 17; i++)
        {
            for (int j = 0; j <= 17; j++)
            {
                yield return surface.PointAt(
                    surface.DomainU.Denormalise(i / 17.0), surface.DomainV.Denormalise(j / 17.0));
            }
        }
    }
}

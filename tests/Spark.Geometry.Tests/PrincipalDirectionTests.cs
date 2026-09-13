using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The principal curvature directions — `E2-T66`'s first item, the eigenvectors that
/// <c>PrincipalCurvatures</c> had been computing and throwing away.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the pairing, not the directions.</b> Any orthonormal pair in the tangent plane
/// passes a test of unit length, orthogonality and tangency, and so does the correct pair with its
/// two members swapped. What distinguishes the answer is that the <i>first</i> direction goes with
/// the <i>smaller</i> curvature: along a cylinder's axis, not around it; across a torus's tube, not
/// along it. Those are the assertions that go red when the eigenvectors are swapped, and the saddle
/// is the one that goes red when the parametric directions are returned instead of the
/// eigenvectors, because its principal directions are the diagonals of its parameter square.
/// </para>
/// </remarks>
public sealed class PrincipalDirectionTests
{
    private const double Loose = 1e-6;

    /// <summary>
    /// A cylinder is flat along its axis and curved around it, so the direction paired with the
    /// zero curvature runs along the axis and the other runs around the cylinder.
    /// </summary>
    [Fact]
    public void ACylindersFlatDirectionRunsAlongItsAxis()
    {
        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));

        (double minimum, double maximum) = cylinder.PrincipalCurvatures(1.0, 2.0);
        (Vector3d alongMinimum, Vector3d alongMaximum) = cylinder.PrincipalDirections(1.0, 2.0);

        Vector3d flat = Math.Abs(minimum) < Math.Abs(maximum) ? alongMinimum : alongMaximum;
        Vector3d curved = Math.Abs(minimum) < Math.Abs(maximum) ? alongMaximum : alongMinimum;

        Assert.Equal(1.0, Math.Abs(flat.Dot(Vector3d.ZAxis)), Loose);
        Assert.Equal(0.0, Math.Abs(curved.Dot(Vector3d.ZAxis)), Loose);
    }

    /// <summary>
    /// On the outer equator of a torus the tube bends hardest, so the direction paired with the
    /// larger curvature goes over the tube, along the axis, and the other goes around the ring.
    /// </summary>
    [Fact]
    public void ATorusPairsTheTubeWithItsLargerCurvature()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.0);
        const double u = 0.7;

        (double minimum, double maximum) = torus.PrincipalCurvatures(u, 0.0);
        (Vector3d alongMinimum, Vector3d alongMaximum) = torus.PrincipalDirections(u, 0.0);

        Vector3d overTheTube = Math.Abs(minimum) > Math.Abs(maximum) ? alongMinimum : alongMaximum;
        Vector3d aroundTheRing = Math.Abs(minimum) > Math.Abs(maximum) ? alongMaximum : alongMinimum;
        Vector3d ringTangent = new(-Math.Sin(u), Math.Cos(u), 0.0);

        Assert.Equal(1.0, Math.Max(Math.Abs(minimum), Math.Abs(maximum)), 1e-4);
        Assert.Equal(1.0 / 6.0, Math.Min(Math.Abs(minimum), Math.Abs(maximum)), 1e-4);
        Assert.Equal(1.0, Math.Abs(overTheTube.Dot(Vector3d.ZAxis)), Loose);
        Assert.Equal(1.0, Math.Abs(aroundTheRing.Dot(ringTangent)), Loose);
    }

    /// <summary>
    /// A surface of revolution's principal directions are its meridian and its parallel, at every
    /// point and not only on an equator: on a torus, over the tube and around the ring.
    /// </summary>
    [Fact]
    public void ASurfaceOfRevolutionsDirectionsAreItsMeridianAndParallel()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 1.0);
        const double u = 0.7;
        const double v = 1.1;

        (Vector3d first, Vector3d second) = torus.PrincipalDirections(u, v);

        Vector3d parallel = new(-Math.Sin(u), Math.Cos(u), 0.0);
        Vector3d meridian = new(-Math.Sin(v) * Math.Cos(u), -Math.Sin(v) * Math.Sin(u), Math.Cos(v));

        bool firstIsParallel = Math.Abs(first.Dot(parallel)) > 1.0 - Loose;
        Vector3d expectedFirst = firstIsParallel ? parallel : meridian;
        Vector3d expectedSecond = firstIsParallel ? meridian : parallel;

        Assert.Equal(1.0, Math.Abs(first.Dot(expectedFirst)), Loose);
        Assert.Equal(1.0, Math.Abs(second.Dot(expectedSecond)), Loose);
    }

    /// <summary>
    /// <b>The saddle, whose principal directions are not its parametric ones.</b> The bilinear patch
    /// through four alternating corners is <c>z = xy</c>; at its centre the curvatures are ±1 along
    /// the two diagonals, so the directions are the diagonals and not the axes — and the negative
    /// curvature belongs to the <c>x − y</c> diagonal when the normal points up.
    /// </summary>
    [Fact]
    public void ASaddlesDirectionsAreItsDiagonalsAndNotItsAxes()
    {
        NurbsSurface saddle = NurbsSurface.FromCorners(
        [
            new Point3d(-1.0, -1.0, 1.0),
            new Point3d(-1.0, 1.0, -1.0),
            new Point3d(1.0, -1.0, -1.0),
            new Point3d(1.0, 1.0, 1.0),
        ]);

        double u = saddle.DomainU.Mid;
        double v = saddle.DomainV.Mid;

        (double minimum, double maximum) = saddle.PrincipalCurvatures(u, v);
        (Vector3d alongMinimum, Vector3d alongMaximum) = saddle.PrincipalDirections(u, v);

        Vector3d normal = saddle.NormalAt(u, v);
        Assert.Equal(1.0, Math.Abs(normal.Dot(Vector3d.ZAxis)), Loose);

        Vector3d sum = new Vector3d(1.0, 1.0, 0.0).Normalised();
        Vector3d difference = new Vector3d(1.0, -1.0, 0.0).Normalised();

        // Along (1, 1) the height rises, along (1, -1) it falls; with the normal up, that is the
        // maximum and the minimum. With the normal down, the other way round.
        Vector3d expectedMinimum = normal.Z > 0.0 ? difference : sum;
        Vector3d expectedMaximum = normal.Z > 0.0 ? sum : difference;

        Assert.Equal(-1.0, minimum, 1e-4);
        Assert.Equal(1.0, maximum, 1e-4);
        Assert.Equal(1.0, Math.Abs(alongMinimum.Dot(expectedMinimum)), Loose);
        Assert.Equal(1.0, Math.Abs(alongMaximum.Dot(expectedMaximum)), Loose);
        Assert.True(Math.Abs(alongMinimum.Dot(Vector3d.XAxis)) < 0.9, "the axis is not a principal direction here.");
    }

    /// <summary>
    /// On every surface type the two directions are unit, orthogonal, and in the tangent plane.
    /// Sampled strictly inside the domains, because a sphere's poles are degenerate and curvature
    /// is undefined there for <c>PrincipalCurvatures</c> too.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyticSurfaceTests.EverySurface), MemberType = typeof(AnalyticSurfaceTests))]
    public void TheDirectionsAreUnitOrthogonalAndTangent(Surface surface)
    {
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                double u = surface.DomainU.Denormalise((i + 0.5) / 6.0);
                double v = surface.DomainV.Denormalise((j + 0.5) / 6.0);

                (Vector3d first, Vector3d second) = surface.PrincipalDirections(u, v);
                Vector3d normal = surface.NormalAt(u, v);

                Assert.Equal(1.0, first.Length, Loose);
                Assert.Equal(1.0, second.Length, Loose);
                Assert.Equal(0.0, first.Dot(second), Loose);
                Assert.Equal(0.0, first.Dot(normal), Loose);
                Assert.Equal(0.0, second.Dot(normal), Loose);
            }
        }
    }

    /// <summary>
    /// <b>The umbilic.</b> A sphere is umbilic everywhere: both curvatures are equal, every
    /// direction is principal, and the eigenvector problem has no answer. The member returns the
    /// documented pair rather than throwing or returning NaN.
    /// </summary>
    [Fact]
    public void ASphereIsUmbilicEverywhereAndStillAnswers()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 4.0);

        (double minimum, double maximum) = sphere.PrincipalCurvatures(1.0, 0.3);
        (Vector3d first, Vector3d second) = sphere.PrincipalDirections(1.0, 0.3);
        Vector3d normal = sphere.NormalAt(1.0, 0.3);

        Assert.Equal(minimum, maximum, 1e-6);
        Assert.Equal(1.0, first.Length, Loose);
        Assert.Equal(1.0, second.Length, Loose);
        Assert.Equal(0.0, first.Dot(second), Loose);
        Assert.Equal(0.0, first.Dot(normal), Loose);
        Assert.Equal(0.0, second.Dot(normal), Loose);
    }

    /// <summary>A plane is the other umbilic surface, with both curvatures zero.</summary>
    [Fact]
    public void APlaneIsUmbilicAndStillAnswers()
    {
        PlaneSurface plane = new(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3));

        (Vector3d first, Vector3d second) = plane.PrincipalDirections(1.0, 1.0);

        Assert.Equal(1.0, first.Length, Loose);
        Assert.Equal(1.0, second.Length, Loose);
        Assert.Equal(0.0, first.Dot(second), Loose);
        Assert.Equal(0.0, Math.Abs(first.Z), Loose);
        Assert.Equal(0.0, Math.Abs(second.Z), Loose);
    }

    /// <summary>The parameters are checked the way every other evaluation checks them.</summary>
    [Fact]
    public void OutOfRangeParametersAreRefused()
    {
        PlaneSurface plane = new(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => plane.PrincipalDirections(5.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => plane.PrincipalDirections(1.0, -1.0));
    }
}

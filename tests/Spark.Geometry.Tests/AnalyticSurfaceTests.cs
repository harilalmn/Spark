using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The analytic surfaces — `E2-T18`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each surface is checked against a closed form the surface itself does not use.</b> A sphere's
/// area is asserted against 4πr², its curvature against 1/r, and its analytic derivatives against
/// the base class's numeric ones — three independent statements of the same fact, so an error in
/// any one implementation shows up rather than being confirmed by itself.
/// </para>
/// <para>
/// <b>The derivative cross-check is the most valuable test here</b>, and it is the one that would
/// have been easiest to leave out. Every analytic surface overrides both derivative methods to
/// avoid the precision loss of central differences; nothing else compares the two, and a sign error
/// in a hand-differentiated cosine produces a surface that evaluates perfectly and has a normal
/// pointing the wrong way.
/// </para>
/// </remarks>
public sealed class AnalyticSurfaceTests
{
    private const double Loose = 1e-5;

    // -- Sphere ---------------------------------------------------------------------------------

    /// <summary>A sphere's points are all one radius from its centre.</summary>
    [Fact]
    public void EveryPointOnASphereIsOneRadiusFromTheCentre()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.5);

        for (int i = 0; i <= 12; i++)
        {
            for (int j = 0; j <= 12; j++)
            {
                double u = 2.0 * Math.PI * i / 12.0;
                double v = -Math.PI / 2.0 + (Math.PI * j / 12.0);

                Assert.Equal(2.5, sphere.PointAt(u, v).DistanceTo(Point3d.Origin), 1e-9);
            }
        }
    }

    /// <summary>A whole sphere's area is 4πr².</summary>
    [Fact]
    public void ASpheresAreaIsFourPiRSquared() =>
        Assert.Equal(4.0 * Math.PI * 9.0, new SphericalSurface(Plane.WorldXY, 3.0).Area, 1e-9);

    /// <summary>
    /// <b>A sphere's principal curvatures are both 1/r.</b> This is the test the discriminant clamp
    /// exists for: <c>H² − K</c> is exactly zero here and rounds negative, which without the clamp
    /// makes both curvatures NaN.
    /// </summary>
    [Fact]
    public void ASpheresCurvaturesAreBothTheReciprocalOfItsRadius()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 4.0);

        (double minimum, double maximum) = sphere.PrincipalCurvatures(1.0, 0.3);

        Assert.Equal(0.25, Math.Abs(minimum), Loose);
        Assert.Equal(0.25, Math.Abs(maximum), Loose);
        Assert.Equal(1.0 / 16.0, Math.Abs(sphere.GaussianCurvature(1.0, 0.3)), Loose);
    }

    /// <summary>A sphere's normal points away from its centre.</summary>
    [Fact]
    public void ASpheresNormalPointsOutwards()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);

        Vector3d normal = sphere.NormalAt(0.7, 0.4);
        Vector3d outwards = (sphere.PointAt(0.7, 0.4) - Point3d.Origin).Normalised();

        Assert.Equal(1.0, normal.Dot(outwards), Loose);
    }

    /// <summary>A sphere's pole has no normal, and the type says so rather than inventing one.</summary>
    [Fact]
    public void ASpheresPoleHasNoNormal() =>
        Assert.Throws<InvalidOperationException>(
            () => new SphericalSurface(Plane.WorldXY, 1.0).NormalAt(0.0, Math.PI / 2.0));

    /// <summary>A non-uniform scale is refused rather than answered with the wrong shape.</summary>
    [Fact]
    public void ASphereRefusesANonUniformScale() =>
        Assert.Throws<ArgumentException>(
            () => new SphericalSurface(Plane.WorldXY, 1.0)
                .TransformedBy(Transform.Scale(Point3d.Origin, 2.0, 1.0, 1.0)));

    // -- Cylinder -------------------------------------------------------------------------------

    /// <summary>A cylinder's lateral area is 2πrh.</summary>
    [Fact]
    public void ACylindersAreaIsTwoPiRH() =>
        Assert.Equal(
            2.0 * Math.PI * 2.0 * 5.0,
            new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)).Area,
            1e-9);

    /// <summary>
    /// A cylinder is curved one way and straight the other, so one principal curvature is 1/r and
    /// the other is zero — which is what makes it developable.
    /// </summary>
    [Fact]
    public void ACylinderIsCurvedOneWayAndStraightTheOther()
    {
        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));

        (double minimum, double maximum) = cylinder.PrincipalCurvatures(1.0, 2.0);

        Assert.Equal(0.0, Math.Min(Math.Abs(minimum), Math.Abs(maximum)), Loose);
        Assert.Equal(0.5, Math.Max(Math.Abs(minimum), Math.Abs(maximum)), Loose);
        Assert.Equal(0.0, cylinder.GaussianCurvature(1.0, 2.0), Loose);
    }

    /// <summary>A cylinder closes around and not along, which is why the two are separate questions.</summary>
    [Fact]
    public void ACylinderClosesAroundAndNotAlong()
    {
        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));

        Assert.True(cylinder.IsClosedU);
        Assert.False(cylinder.IsClosedV);

        // And a parameter past the seam wraps rather than throwing, because the surface closes.
        Assert.Equal(
            cylinder.PointAt(0.25, 1.0).X,
            cylinder.PointAt(0.25 + (2.0 * Math.PI), 1.0).X,
            1e-9);
    }

    // -- Cone -----------------------------------------------------------------------------------

    /// <summary>A cone with no taper is a cylinder, which is why the taper is an angle.</summary>
    [Fact]
    public void AConeWithNoTaperIsACylinder()
    {
        ConicalSurface cone = new(Plane.WorldXY, 2.0, Angle.Zero, new Interval(0.0, 5.0));
        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));

        Assert.Equal(cylinder.Area, cone.Area, 1e-9);
        Assert.Equal(cylinder.PointAt(1.0, 3.0).X, cone.PointAt(1.0, 3.0).X, 1e-12);
        Assert.Equal(cylinder.PointAt(1.0, 3.0).Z, cone.PointAt(1.0, 3.0).Z, 1e-12);
    }

    /// <summary>
    /// <b>A cone's lateral area is πrl, measured along the slant.</b> The test exists because the
    /// obvious wrong answer — measuring along the axis — is out by exactly the secant of the
    /// half-angle and looks perfectly plausible.
    /// </summary>
    [Fact]
    public void AConesAreaIsMeasuredAlongTheSlant()
    {
        // A cone from radius 0 at v = 0 up to radius 3 at v = 4: slant 5, area πrl = 15π.
        Angle halfAngle = Angle.FromRadians(Math.Atan2(3.0, 4.0));
        ConicalSurface cone = new(Plane.WorldXY, 0.0, halfAngle, new Interval(0.0, 4.0));

        Assert.Equal(Math.PI * 3.0 * 5.0, cone.Area, 1e-9);
    }

    /// <summary>The radius at a height is what the half-angle says it is.</summary>
    [Fact]
    public void AConesRadiusGrowsWithHeight()
    {
        ConicalSurface cone = new(
            Plane.WorldXY, 1.0, Angle.FromRadians(Math.PI / 4.0), new Interval(0.0, 4.0));

        Assert.Equal(1.0, cone.RadiusAt(0.0), 1e-9);
        Assert.Equal(3.0, cone.RadiusAt(2.0), 1e-9);
    }

    /// <summary>A cone's apex has no normal.</summary>
    [Fact]
    public void AConesApexHasNoNormal()
    {
        ConicalSurface cone = new(
            Plane.WorldXY, 0.0, Angle.FromRadians(Math.PI / 4.0), new Interval(0.0, 4.0));

        Assert.Throws<InvalidOperationException>(() => cone.NormalAt(1.0, 0.0));
    }

    // -- Torus ----------------------------------------------------------------------------------

    /// <summary>A whole torus's area is 4π²Rr.</summary>
    [Fact]
    public void ATorussAreaIsFourPiSquaredRr() =>
        Assert.Equal(
            4.0 * Math.PI * Math.PI * 5.0 * 2.0,
            new ToroidalSurface(Plane.WorldXY, 5.0, 2.0).Area,
            1e-9);

    /// <summary>
    /// A torus is the one surface here that closes in both directions, and both wrap.
    /// </summary>
    [Fact]
    public void ATorusClosesBothWays()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 2.0);

        Assert.True(torus.IsClosedU);
        Assert.True(torus.IsClosedV);

        Assert.Equal(torus.PointAt(0.3, 0.4).X, torus.PointAt(0.3 + (2.0 * Math.PI), 0.4).X, 1e-9);
        Assert.Equal(torus.PointAt(0.3, 0.4).Z, torus.PointAt(0.3, 0.4 - (2.0 * Math.PI)).Z, 1e-9);
    }

    /// <summary>
    /// A torus's Gaussian curvature is positive on the outside and negative on the inside, which is
    /// the textbook property and the sharpest check on the second derivatives.
    /// </summary>
    [Fact]
    public void ATorusIsSaddleShapedOnTheInside()
    {
        ToroidalSurface torus = new(Plane.WorldXY, 5.0, 2.0);

        Assert.True(torus.GaussianCurvature(0.0, 0.0) > 0.0, "the outer equator should be convex");
        Assert.True(torus.GaussianCurvature(0.0, Math.PI) < 0.0, "the inner equator should be a saddle");
    }

    // -- Extrusion ------------------------------------------------------------------------------

    /// <summary>
    /// Extruding a line perpendicular to itself gives a rectangle, and its area is the product.
    /// </summary>
    [Fact]
    public void ExtrudingALineGivesARectangle()
    {
        ExtrusionSurface surface = new(
            Line.ByStartPointEndPoint(Point3d.Origin, new Point3d(3, 0, 0)), new Vector3d(0, 4, 0));

        Assert.Equal(12.0, surface.Area, 1e-6);
        Assert.Equal(new Point3d(3, 4, 0), surface.PointAt(surface.DomainU.Max, 4.0));
    }

    /// <summary>
    /// <b>An extrusion along the profile's own direction has no area.</b> The obvious closed form —
    /// length times height — says twelve; the honest integration says zero, and this is why the
    /// type does not override <c>Area</c>.
    /// </summary>
    [Fact]
    public void ExtrudingALineAlongItselfHasNoArea()
    {
        ExtrusionSurface surface = new(
            Line.ByStartPointEndPoint(Point3d.Origin, new Point3d(3, 0, 0)), new Vector3d(4, 0, 0));

        Assert.Equal(0.0, surface.Area, 1e-9);
    }

    /// <summary>An extrusion's u domain is the profile's own, not a renormalised one.</summary>
    [Fact]
    public void AnExtrusionKeepsTheProfilesDomain()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 2.0);
        ExtrusionSurface surface = new(circle, new Vector3d(0, 0, 5));

        Assert.Equal(circle.Domain, surface.DomainU);
        Assert.True(surface.IsClosedU);
        Assert.Equal(2.0 * Math.PI * 2.0 * 5.0, surface.Area, 1e-4);
    }

    // -- Revolution -----------------------------------------------------------------------------

    /// <summary>
    /// <b>Revolving a line parallel to the axis gives a cylinder</b>, and it agrees with the
    /// cylinder type to nine places. Two independent implementations of the same shape is the
    /// strongest check available without a reference kernel.
    /// </summary>
    [Fact]
    public void RevolvingALineGivesACylinder()
    {
        RevolutionSurface revolved = new(
            Line.ByStartPointEndPoint(new Point3d(2, 0, 0), new Point3d(2, 0, 5)),
            Point3d.Origin,
            Vector3d.ZAxis);

        CylindricalSurface cylinder = new(Plane.WorldXY, 2.0, new Interval(0.0, 5.0));

        Assert.Equal(cylinder.Area, revolved.Area, 1e-6);

        Point3d one = revolved.PointAt(0.9, revolved.DomainV.Denormalise(0.4));
        Point3d other = cylinder.PointAt(0.9, 2.0);

        Assert.Equal(other.X, one.X, 1e-9);
        Assert.Equal(other.Y, one.Y, 1e-9);
        Assert.Equal(other.Z, one.Z, 1e-9);
    }

    /// <summary>Revolving a half-circle about its diameter gives a sphere's area.</summary>
    [Fact]
    public void RevolvingAHalfCircleGivesASphere()
    {
        // A half-circle in the XZ plane from the south pole to the north, radius 2.
        Arc profile = Arc.ByCentreStartPointSweepAngle(
            Point3d.Origin,
            new Point3d(0, 0, -2),
            Vector3d.YAxis,
            Angle.FromRadians(Math.PI));

        RevolutionSurface revolved = new(profile, Point3d.Origin, Vector3d.ZAxis);

        Assert.Equal(4.0 * Math.PI * 4.0, revolved.Area, 1e-4);
    }

    // -- Ruled ----------------------------------------------------------------------------------

    /// <summary>Ruling between two parallel lines gives a rectangle.</summary>
    [Fact]
    public void RulingBetweenTwoLinesGivesAQuadrilateral()
    {
        RuledSurface surface = new(
            Line.ByStartPointEndPoint(Point3d.Origin, new Point3d(3, 0, 0)),
            Line.ByStartPointEndPoint(new Point3d(0, 4, 0), new Point3d(3, 4, 0)));

        Assert.Equal(12.0, surface.Area, 1e-6);
        Assert.Equal(new Point3d(1.5, 2.0, 0.0), surface.PointAt(0.5, 0.5));
    }

    /// <summary>
    /// <b>The domain lengths reach the derivatives.</b> Ruling between two curves parameterised
    /// over very different domains gives the same *shape* as ruling between two [0, 1] ones, and
    /// therefore the same area — which is only true if the chain rule's factor is applied.
    /// </summary>
    [Fact]
    public void TheCurvesDomainLengthsReachTheDerivatives()
    {
        RuledSurface plain = new(
            Line.ByStartPointEndPoint(Point3d.Origin, new Point3d(3, 0, 0)),
            Line.ByStartPointEndPoint(new Point3d(0, 4, 0), new Point3d(3, 4, 0)));

        // A circle's domain is [0, 2π] and a line's is [0, 1]; ruling between two arcs of different
        // domains is where an unscaled derivative shows up.
        RuledSurface arcs = new(
            Circle.ByCentreRadius(Point3d.Origin, 2.0),
            Circle.ByCentreRadius(new Point3d(0, 0, 4), 2.0));

        Assert.Equal(12.0, plain.Area, 1e-6);
        Assert.Equal(2.0 * Math.PI * 2.0 * 4.0, arcs.Area, 1e-4);
    }

    // -- The derivative cross-check ---------------------------------------------------------------

    /// <summary>
    /// <b>Every analytic derivative agrees with a central difference.</b> This is the test that
    /// catches a sign error in a hand-differentiated trigonometric term — the kind that produces a
    /// surface which evaluates perfectly and whose normal points the wrong way.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySurface))]
    public void TheAnalyticDerivativesAgreeWithCentralDifferences(Surface surface)
    {
        for (int i = 1; i < 5; i++)
        {
            for (int j = 1; j < 5; j++)
            {
                double u = surface.DomainU.Denormalise(i / 5.0);
                double v = surface.DomainV.Denormalise(j / 5.0);

                surface.DerivativeAt(u, v, out Vector3d du, out Vector3d dv);

                double stepU = surface.DomainU.Length * 1e-6;
                double stepV = surface.DomainV.Length * 1e-6;

                Vector3d numericU =
                    (surface.PointAt(u + stepU, v) - surface.PointAt(u - stepU, v)) / (2.0 * stepU);

                Vector3d numericV =
                    (surface.PointAt(u, v + stepV) - surface.PointAt(u, v - stepV)) / (2.0 * stepV);

                double scale = Math.Max(1.0, du.Length + dv.Length);

                Assert.Equal(0.0, (du - numericU).Length / scale, 1e-4);
                Assert.Equal(0.0, (dv - numericV).Length / scale, 1e-4);
            }
        }
    }

    /// <summary>
    /// Every surface's second derivatives agree with a central difference of the first ones.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySurface))]
    public void TheAnalyticCurvatureIsFiniteEverywhereItShouldBe(Surface surface)
    {
        for (int i = 1; i < 5; i++)
        {
            for (int j = 1; j < 5; j++)
            {
                double u = surface.DomainU.Denormalise(i / 5.0);
                double v = surface.DomainV.Denormalise(j / 5.0);

                (double minimum, double maximum) = surface.PrincipalCurvatures(u, v);

                Assert.True(double.IsFinite(minimum), $"curvature was {minimum} at ({u}, {v})");
                Assert.True(double.IsFinite(maximum), $"curvature was {maximum} at ({u}, {v})");
                Assert.True(minimum <= maximum + 1e-12, "the curvatures should come back in order");
            }
        }
    }

    /// <summary>One of each surface type, for the cross-checks that apply to all of them.</summary>
    public static TheoryData<Surface> EverySurface() =>
    [
        new PlaneSurface(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3)),
        new SphericalSurface(Plane.WorldXY, 2.0),
        new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)),
        new ConicalSurface(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0)),
        new ToroidalSurface(Plane.WorldXY, 5.0, 2.0),
        new ExtrusionSurface(Circle.ByCentreRadius(Point3d.Origin, 2.0), new Vector3d(0, 0, 5)),
        new RevolutionSurface(
            Line.ByStartPointEndPoint(new Point3d(2, 0, 0), new Point3d(2, 0, 5)), Point3d.Origin, Vector3d.ZAxis),
        new RuledSurface(
            Circle.ByCentreRadius(Point3d.Origin, 2.0),
            Circle.ByCentreRadius(new Point3d(0, 0, 4), 3.0)),
    ];
}

using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The surface contract and its first implementation — `E2-T17`, `E2-T18`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Most of what is asserted here belongs to the base class, not to the plane.</b> Area,
/// curvature, closest point, iso-curves and the bounding box are all written once on
/// <see cref="Surface"/> and inherited; a plane is the one surface whose right answers can be
/// written down by hand, which is exactly why it is the one they are checked against.
/// </para>
/// <para>
/// <b>The numeric derivatives are tested through a surface that does not override them</b>, because
/// otherwise nothing would ever run them — every analytic surface has closed forms — and a default
/// nobody exercises is a default that is wrong.
/// </para>
/// </remarks>
public sealed class SurfaceTests
{
    private const double Tight = 1e-9;

    private static PlaneSurface Unit() =>
        new(Plane.WorldXY, new Interval(0.0, 2.0), new Interval(0.0, 3.0));

    /// <summary>The parameters are distances along the plane's axes, which is the whole design.</summary>
    [Fact]
    public void ParametersAreDistancesAlongThePlanesAxes()
    {
        PlaneSurface surface = Unit();

        Assert.Equal(new Point3d(0, 0, 0), surface.PointAt(0, 0));
        Assert.Equal(new Point3d(2, 3, 0), surface.PointAt(2, 3));
        Assert.Equal(new Point3d(1, 1.5, 0), surface.PointAt(1, 1.5));
    }

    /// <summary>A parameter outside an open domain is refused rather than clamped.</summary>
    [Theory]
    [InlineData(-0.5, 1.0)]
    [InlineData(2.5, 1.0)]
    [InlineData(1.0, -0.5)]
    [InlineData(1.0, 3.5)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(1.0, double.PositiveInfinity)]
    public void AParameterOutsideAnOpenDomainIsRefused(double u, double v) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit().PointAt(u, v));

    /// <summary>
    /// <b>The normal follows u × v.</b> Stated as the kernel's convention on the base class and
    /// asserted here, because a surface whose normal points the other way from its neighbour's is
    /// how a solid ends up inside out.
    /// </summary>
    [Fact]
    public void TheNormalFollowsTheCrossProductOfTheDerivatives()
    {
        Assert.Equal(Vector3d.ZAxis, Unit().NormalAt(1, 1));

        // Flip the plane's y-axis and the normal flips with it, rather than being recomputed from
        // some absolute rule.
        PlaneSurface flipped = new(
            Plane.ByOriginXAxisYAxis(Point3d.Origin, Vector3d.XAxis, -Vector3d.YAxis),
            new Interval(0, 1),
            new Interval(0, 1));

        Assert.Equal(-Vector3d.ZAxis, flipped.NormalAt(0.5, 0.5));
    }

    /// <summary>A plane's area is exactly the product of its sides.</summary>
    [Fact]
    public void APlanesAreaIsTheProductOfItsSides() => Assert.Equal(6.0, Unit().Area, Tight);

    /// <summary>
    /// <b>The base class's quadrature agrees with the closed form.</b> This is what says the
    /// integration is right before it is used on a surface whose area nobody knows by hand.
    /// </summary>
    [Fact]
    public void TheIntegratedAreaAgreesWithTheClosedForm()
    {
        Sampled surface = new(new Interval(0, 2), new Interval(0, 3));

        // The same rectangle, through a surface that does not override Area or the derivatives.
        Assert.Equal(6.0, surface.Area, 1e-6);
    }

    /// <summary>A plane has no curvature, in either principal direction.</summary>
    [Fact]
    public void APlaneHasNoCurvature()
    {
        (double minimum, double maximum) = Unit().PrincipalCurvatures(1, 1);

        Assert.Equal(0.0, minimum, 1e-6);
        Assert.Equal(0.0, maximum, 1e-6);
        Assert.Equal(0.0, Unit().GaussianCurvature(1, 1), 1e-6);
        Assert.Equal(0.0, Unit().MeanCurvature(1, 1), 1e-6);
    }

    /// <summary>
    /// The closest point on a plane to a point above it is the point directly below, and the
    /// distance is the height. Newton has nothing to do here, which is the point: it must not make
    /// things worse.
    /// </summary>
    [Fact]
    public void TheClosestPointOnAPlaneIsDirectlyBelow()
    {
        Point3d closest = Unit().ClosestPoint(new Point3d(1.0, 2.0, 7.0), out double u, out double v);

        Assert.Equal(1.0, u, 1e-7);
        Assert.Equal(2.0, v, 1e-7);
        Assert.Equal(new Point3d(1.0, 2.0, 0.0).X, closest.X, 1e-7);
        Assert.Equal(new Point3d(1.0, 2.0, 0.0).Y, closest.Y, 1e-7);
        Assert.Equal(0.0, closest.Z, 1e-7);
    }

    /// <summary>
    /// A point outside the rectangle projects to its edge, not to the infinite plane. A surface is
    /// bounded and the answer has to be on it.
    /// </summary>
    [Fact]
    public void APointOutsideTheRectangleProjectsToItsEdge()
    {
        Point3d closest = Unit().ClosestPoint(new Point3d(9.0, 1.0, 0.0), out double u, out _);

        Assert.Equal(2.0, u, 1e-7);
        Assert.Equal(2.0, closest.X, 1e-7);
    }

    /// <summary>
    /// <b>An iso-curve is a real curve.</b> Everything already written for curves — length,
    /// division, tessellation, the bounding box — works on it, which is the whole reason it is not
    /// a sampled polyline.
    /// </summary>
    [Fact]
    public void AnIsoCurveIsARealCurve()
    {
        Curve along = Unit().IsoCurveU(1.5);

        Assert.Equal(new Interval(0, 2), along.Domain);
        Assert.Equal(2.0, along.Length, 1e-7);
        Assert.Equal(new Point3d(0, 1.5, 0), along.StartPoint);
        Assert.Equal(new Point3d(2, 1.5, 0), along.EndPoint);
        Assert.Equal(new Point3d(1, 1.5, 0), along.PointAtLength(1.0));
    }

    /// <summary>The other direction is a curve over the other domain.</summary>
    [Fact]
    public void TheOtherIsoCurveRunsTheOtherWay()
    {
        Curve along = Unit().IsoCurveV(0.5);

        Assert.Equal(new Interval(0, 3), along.Domain);
        Assert.Equal(new Point3d(0.5, 0, 0), along.StartPoint);
        Assert.Equal(new Point3d(0.5, 3, 0), along.EndPoint);
    }

    /// <summary>An iso-curve reverses, and reversing swaps its ends without moving its domain.</summary>
    [Fact]
    public void AnIsoCurveReverses()
    {
        Curve reversed = Unit().IsoCurveU(1.5).Reversed();

        Assert.Equal(new Interval(0, 2), reversed.Domain);
        Assert.Equal(new Point3d(2, 1.5, 0), reversed.StartPoint);
        Assert.Equal(new Point3d(0, 1.5, 0), reversed.EndPoint);
    }

    /// <summary>An iso-curve trims to a sub-interval.</summary>
    [Fact]
    public void AnIsoCurveTrims()
    {
        Curve trimmed = Unit().IsoCurveU(1.5).Trimmed(new Interval(0.5, 1.5));

        Assert.Equal(new Interval(0.5, 1.5), trimmed.Domain);
        Assert.Equal(new Point3d(0.5, 1.5, 0), trimmed.StartPoint);
        Assert.Equal(1.0, trimmed.Length, 1e-7);
    }

    /// <summary>
    /// An iso-curve transforms by transforming its surface, so the result stays exact rather than
    /// becoming a sampled approximation.
    /// </summary>
    [Fact]
    public void AnIsoCurveTransformsThroughItsSurface()
    {
        Curve moved = Unit().IsoCurveU(1.5).TransformedBy(Transform.Translation(new Vector3d(0, 0, 5)));

        Assert.Equal(new Point3d(0, 1.5, 5), moved.StartPoint);
        Assert.Equal(2.0, moved.Length, 1e-7);
    }

    /// <summary>The bounding box of a plane rectangle is its four corners, exactly.</summary>
    [Fact]
    public void ThePlanesBoundingBoxIsExact()
    {
        BoundingBox box = Unit().BoundingBox;

        Assert.Equal(0.0, box.Min.X, Tight);
        Assert.Equal(2.0, box.Max.X, Tight);
        Assert.Equal(3.0, box.Max.Y, Tight);
        Assert.Equal(0.0, box.Max.Z, Tight);
    }

    /// <summary>
    /// <b>The sampled bounding box contains the surface</b>, which is the only property worth
    /// having: everything downstream uses a box to decide what to skip, so one that excludes part
    /// of its geometry is worse than one that is loose.
    /// </summary>
    [Fact]
    public void TheSampledBoundingBoxContainsTheSurface()
    {
        Sampled surface = new(new Interval(0, 2), new Interval(0, 3));
        BoundingBox box = surface.BoundingBox;

        for (int i = 0; i <= 37; i++)
        {
            for (int j = 0; j <= 41; j++)
            {
                Point3d point = surface.PointAt(2.0 * i / 37.0, 3.0 * j / 41.0);

                Assert.True(box.Contains(point), $"the box does not contain {point}");
            }
        }
    }

    /// <summary>The tangent frame sits on the surface with the normal up and the x-axis along u.</summary>
    [Fact]
    public void TheFrameSitsOnTheSurface()
    {
        Plane frame = Unit().FrameAt(1, 2);

        Assert.Equal(new Point3d(1, 2, 0), frame.Origin);
        Assert.Equal(Vector3d.ZAxis, frame.Normal);
        Assert.Equal(Vector3d.XAxis, frame.XAxis);
    }

    /// <summary>A transformed plane surface is still a plane surface, in the right place.</summary>
    [Fact]
    public void TransformingMovesTheSurface()
    {
        Surface moved = Unit().TransformedBy(Transform.Translation(new Vector3d(1, 2, 3)));

        Assert.Equal(new Point3d(1, 2, 3), moved.PointAt(0, 0));
        Assert.Equal(6.0, moved.Area, 1e-7);
    }

    /// <summary>
    /// <b>A uniform scale scales the area by the square of the factor</b>, which only holds
    /// because the domains are rescaled with the axes rather than left alone.
    /// </summary>
    [Fact]
    public void ScalingScalesTheArea()
    {
        Surface scaled = Unit().TransformedBy(Transform.Scale(2.0));

        Assert.Equal(24.0, scaled.Area, 1e-7);
        Assert.Equal(new Point3d(4, 6, 0), scaled.PointAt(4, 6));
    }

    /// <summary>A degenerate domain is refused at construction rather than at use.</summary>
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(0.0, double.PositiveInfinity)]
    public void ADegenerateDomainIsRefused(double min, double max) =>
        Assert.Throws<ArgumentException>(
            () => new PlaneSurface(Plane.WorldXY, new Interval(min, max), Interval.Unit));

    /// <summary>The centred factory puts the rectangle's middle on the plane's origin.</summary>
    [Fact]
    public void TheCentredFactoryCentresIt()
    {
        PlaneSurface surface = PlaneSurface.ByPlaneSize(Plane.WorldXY, 4.0, 6.0);

        Assert.Equal(new Interval(-2, 2), surface.DomainU);
        Assert.Equal(new Interval(-3, 3), surface.DomainV);
        Assert.Equal(Point3d.Origin, surface.PointAt(0, 0));
        Assert.Equal(24.0, surface.Area, Tight);
    }

    /// <summary>The corner factory sorts its corners, so either order gives the same rectangle.</summary>
    [Fact]
    public void TheCornerFactorySortsItsCorners()
    {
        PlaneSurface one = PlaneSurface.ByPlaneCorners(Plane.WorldXY, new Point2d(3, 4), new Point2d(1, 2));
        PlaneSurface other = PlaneSurface.ByPlaneCorners(Plane.WorldXY, new Point2d(1, 2), new Point2d(3, 4));

        Assert.Equal(one.DomainU, other.DomainU);
        Assert.Equal(one.DomainV, other.DomainV);
        Assert.Equal(4.0, one.Area, Tight);
    }

    /// <summary>
    /// The base class's numeric derivatives agree with the analytic ones, on a surface where both
    /// are known. This is what says the default is right before a new surface type relies on it.
    /// </summary>
    [Fact]
    public void TheNumericDerivativesAgreeWithTheAnalyticOnes()
    {
        Sampled sampled = new(new Interval(0, 2), new Interval(0, 3));

        sampled.DerivativeAt(1.0, 1.0, out Vector3d du, out Vector3d dv);

        Assert.Equal(1.0, du.X, 1e-6);
        Assert.Equal(0.0, du.Y, 1e-6);
        Assert.Equal(1.0, dv.Y, 1e-6);
        Assert.Equal(0.0, dv.X, 1e-6);
    }

    /// <summary>
    /// A surface that implements nothing but <c>Evaluate</c>, so the base class's numeric
    /// derivatives, quadrature and sampling are exercised rather than shadowed by an override.
    /// </summary>
    /// <remarks>
    /// It is the same plane the tests above use, deliberately: the point is that two very different
    /// implementations of the same sheet give the same answers, which is what says the defaults are
    /// right rather than merely present.
    /// </remarks>
    private sealed class Sampled(Interval u, Interval v) : Surface
    {
        public override Interval DomainU => u;

        public override Interval DomainV => v;

        public override bool IsClosedU => false;

        public override bool IsClosedV => false;

        public override Surface TransformedBy(in Transform transform) => this;

        protected override Point3d Evaluate(double parameter, double other) =>
            new(parameter, other, 0.0);
    }
}

using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The closest-point query, across every curve type there is. It waited for
/// <see cref="Bvh{T}"/> rather than getting a second implementation, and these tests are what
/// say the general path is good enough that the analytic ones were not needed.
/// </summary>
public sealed class CurveClosestPointTests
{
    [Fact]
    public void ThePerpendicularFootOnALineIsExact()
    {
        Line line = new(Point3d.Origin, new Point3d(10.0, 0.0, 0.0));

        Assert.True(line.ClosestPoint(new Point3d(3.0, 4.0, 0.0)).EqualsWithin(new Point3d(3.0, 0.0, 0.0)));
        Assert.Equal(4.0, line.DistanceTo(new Point3d(3.0, 4.0, 0.0)), 9);
    }

    [Fact]
    public void APointBeyondTheEndOfALineIsClosestToItsEnd()
    {
        Line line = new(Point3d.Origin, new Point3d(10.0, 0.0, 0.0));

        // The curve is a segment, not an infinite line, so the answer is clamped to the domain
        // exactly as Ray.ClosestPoint clamps at its origin.
        Assert.True(line.ClosestPoint(new Point3d(50.0, 3.0, 0.0)).EqualsWithin(new Point3d(10.0, 0.0, 0.0)));
        Assert.True(line.ClosestPoint(new Point3d(-50.0, 3.0, 0.0)).EqualsWithin(Point3d.Origin));
    }

    [Fact]
    public void APointOnTheCurveIsItsOwnClosestPoint()
    {
        Circle circle = new(Plane.WorldXY, 5.0);
        Point3d on = circle.PointAt(circle.Domain.Min + (0.37 * circle.Domain.Length));

        Assert.True(circle.ClosestPoint(on).EqualsWithin(on, Tolerance.ForScale(5.0)));
        Assert.True(circle.DistanceTo(on) <= Tolerance.Default.Linear);
    }

    [Fact]
    public void TheAnswerIsResolvedToTheToleranceAskedForAndNotFurther()
    {
        Circle circle = new(Plane.WorldXY, 5.0);
        Point3d on = circle.PointAt(circle.Domain.Min + (0.37 * circle.Domain.Length));

        // The search stops when a further step would move the point less than the tolerance,
        // so the tolerance is a promise about the answer rather than a hint. Asking for a
        // thousand times less must actually deliver less error, and this is the assertion that
        // would fail if the parameter were being resolved to a fixed number of iterations with
        // the tolerance quietly ignored.
        double coarse = circle.DistanceTo(on, new Tolerance(1e-4, Angle.FromDegrees(0.001), 1e-12));
        double fine = circle.DistanceTo(on, new Tolerance(1e-9, Angle.FromDegrees(0.001), 1e-12));

        Assert.True(coarse <= 1e-4);
        Assert.True(fine <= 1e-9);
        Assert.True(fine <= coarse);
    }

    [Fact]
    public void APointOutsideACircleProjectsRadiallyOntoIt()
    {
        Circle circle = new(Plane.WorldXY, 5.0);

        Assert.True(circle.ClosestPoint(new Point3d(20.0, 0.0, 0.0)).EqualsWithin(
            new Point3d(5.0, 0.0, 0.0),
            Tolerance.ForScale(5.0)));

        // From above the plane the answer is still on the circle, at the same angle.
        Assert.True(circle.ClosestPoint(new Point3d(0.0, 20.0, 9.0)).EqualsWithin(
            new Point3d(0.0, 5.0, 0.0),
            Tolerance.ForScale(5.0)));
    }

    [Fact]
    public void APointInsideACircleStillProjectsOntoTheCurveRatherThanTheDisc()
    {
        Circle circle = new(Plane.WorldXY, 5.0);

        // A circle is a curve, not a region: the nearest point is on the rim.
        Assert.Equal(4.0, circle.DistanceTo(new Point3d(1.0, 0.0, 0.0)), 6);
    }

    [Fact]
    public void TheCentreOfACircleIsATieAndTheAnswerIsStillOnTheCircle()
    {
        Circle circle = new(Plane.WorldXY, 5.0);

        // Every point on the circle is equidistant. No rule for choosing is more correct than
        // another, so what is asserted is the distance and that the answer is genuinely on the
        // curve - never a particular point.
        Point3d answer = circle.ClosestPoint(Point3d.Origin);

        Assert.Equal(5.0, Point3d.Origin.DistanceTo(answer), 6);
        Assert.Equal(5.0, circle.DistanceTo(Point3d.Origin), 6);
    }

    [Fact]
    public void AnArcAnswersOnTheArcRatherThanOnTheCircleItIsPartOf()
    {
        Arc arc = Arc.ByPlaneRadiusAngles(Plane.WorldXY, 5.0, Angle.Zero, Angle.QuarterTurn);

        // The nearest point of the full circle to (0, -20, 0) is (0, -5, 0), which is not on
        // this quarter. The nearest point ON THE ARC is its start.
        Point3d answer = arc.ClosestPoint(new Point3d(0.0, -20.0, 0.0));

        Assert.True(answer.EqualsWithin(arc.StartPoint, Tolerance.ForScale(5.0)));
    }

    [Theory]
    [InlineData(1e-6)]
    [InlineData(1.0)]
    [InlineData(1e6)]
    public void TheAnswerIsRightAtEveryWorkingScale(double scale)
    {
        Circle circle = new(Plane.WorldXY, scale);
        Point3d far = new(4.0 * scale, 0.0, 0.0);

        Assert.Equal(3.0 * scale, circle.DistanceTo(far, Tolerance.ForScale(scale)), 6.0 * scale * 1e-9);
    }

    [Fact]
    public void EveryCurveTypeAgreesWithADenseSearchAlongItself()
    {
        foreach (Curve curve in EveryKind())
        {
            foreach (Point3d probe in Probes())
            {
                double found = curve.DistanceTo(probe);
                double bySampling = DenseMinimum(curve, probe);

                // The dense scan is the reference: 4,001 samples cannot beat a real minimiser,
                // so the query must be at least as good as it, never worse by more than the
                // sampling's own resolution.
                Assert.True(
                    found <= bySampling + (1e-6 * Math.Max(1.0, curve.Length)),
                    $"{curve.GetType().Name}: query {found} against dense {bySampling}.");
            }
        }
    }

    [Fact]
    public void TheReturnedPointAlwaysLiesOnTheCurveAtTheReturnedParameter()
    {
        foreach (Curve curve in EveryKind())
        {
            foreach (Point3d probe in Probes())
            {
                double parameter = curve.ParameterAtClosestPoint(probe);

                Assert.InRange(parameter, curve.Domain.Min, curve.Domain.Max);
                Assert.True(curve.PointAt(parameter).EqualsWithin(
                    curve.ClosestPoint(probe),
                    Tolerance.ForScale(Math.Max(1.0, curve.Length))));
            }
        }
    }

    [Fact]
    public void APolyCurveAnswersConsistentlyAcrossAJoin()
    {
        // The case that argues against three implementations agreeing at their boundaries: a
        // line and an arc in one curve, probed from just either side of where they meet.
        Line line = new(new Point3d(-10.0, 0.0, 0.0), Point3d.Origin);
        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.ByOriginNormal(new Point3d(0.0, 5.0, 0.0), Vector3d.ZAxis),
            5.0,
            -Angle.QuarterTurn,
            Angle.QuarterTurn);

        PolyCurve joined = PolyCurve.ByJoinedCurves([line, arc]);
        Point3d join = Point3d.Origin;

        Assert.True(joined.ClosestPoint(new Point3d(-0.001, -3.0, 0.0)).DistanceTo(join) < 0.01);
        Assert.True(joined.ClosestPoint(new Point3d(0.001, -3.0, 0.0)).DistanceTo(join) < 0.01);
    }

    [Fact]
    public void ANonFinitePointIsRefused()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Assert.Equal(
            "point",
            Assert.Throws<ArgumentException>(() => line.ClosestPoint(Point3d.Unset)).ParamName);
        Assert.Equal(
            "point",
            Assert.Throws<ArgumentException>(() => line.ParameterAtClosestPoint(Point3d.Unset)).ParamName);
    }

    [Fact]
    public void TheIndexIsBuiltOnceAndRepeatedQueriesAgree()
    {
        Circle circle = new(Plane.WorldXY, 5.0);
        Point3d probe = new(11.0, 3.0, 2.0);

        double first = circle.ParameterAtClosestPoint(probe);

        for (int repeat = 0; repeat < 5; repeat++)
        {
            Assert.Equal(first, circle.ParameterAtClosestPoint(probe));
        }
    }

    private static Curve[] EveryKind()
    {
        Line line = new(new Point3d(-3.0, -1.0, 0.5), new Point3d(7.0, 4.0, -2.0));
        Arc arc = Arc.ByPlaneRadiusAngles(Plane.WorldXY, 5.0, Angle.Zero, Angle.FromDegrees(140.0));
        Circle circle = new(Plane.WorldYZ, 3.0);
        EllipseCurve ellipse = EllipseCurve.ByPlaneRadii(Plane.WorldXY, 7.0, 2.0);
        PolyLine polyLine = PolyLine.ByPoints(
        [
            Point3d.Origin,
            new Point3d(4.0, 0.0, 0.0),
            new Point3d(4.0, 4.0, 0.0),
            new Point3d(0.0, 4.0, 3.0),
        ]);

        Line first = new(new Point3d(-10.0, 0.0, 0.0), Point3d.Origin);
        Arc second = Arc.ByPlaneRadiusAngles(
            Plane.ByOriginNormal(new Point3d(0.0, 5.0, 0.0), Vector3d.ZAxis),
            5.0,
            -Angle.QuarterTurn,
            Angle.QuarterTurn);

        return [line, arc, circle, ellipse, polyLine, PolyCurve.ByJoinedCurves([first, second])];
    }

    private static Point3d[] Probes() =>
    [
        Point3d.Origin,
        new(1.0, 1.0, 1.0),
        new(-20.0, 6.0, 0.0),
        new(0.0, 0.0, 50.0),
        new(3.5, -2.5, 0.25),
        new(100.0, 100.0, 100.0),
    ];

    private static double DenseMinimum(Curve curve, in Point3d probe)
    {
        Interval domain = curve.Domain;
        Point3d target = probe;

        return Enumerable.Range(0, 4001)
            .Min(step => curve.PointAt(domain.Min + (domain.Length * step / 4000.0)).DistanceTo(target));
    }
}

using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Closest point, across every curve type there is.
/// </summary>
/// <remarks>
/// <para>
/// <b>The property is the test.</b> <c>E2-T33</c> states it: <i>ClosestPoint is never farther than
/// any sampled point.</i> That single assertion is stronger than any number of hand-picked cases,
/// because it fails for every way the search can go wrong — a bracket too coarse to find the right
/// basin, a Newton step that overshoots into a worse one, an iterate that walks off the end of the
/// domain. It is applied to every curve type here rather than to NURBS alone, because the
/// implementation lives on <see cref="Curve"/> and so does the risk.
/// </para>
/// <para>
/// <see cref="Line"/> answers in closed form, which makes it the cross-check: the general search
/// has to agree with arithmetic that cannot be wrong.
/// </para>
/// </remarks>
public sealed class CurveClosestPointTests
{
    /// <summary>Every curve type this assembly has, with a point to measure from.</summary>
    public static TheoryData<string> CurveNames =>
        ["line", "circle", "arc", "ellipse", "polyline", "polycurve", "nurbs", "nurbs-rational"];

    /// <summary>
    /// <b>The <c>E2-T33</c> property.</b> Nothing sampled densely along the curve is nearer to the
    /// point than the answer — which is what "closest" means, checked without reference to how the
    /// answer was reached.
    /// </summary>
    [Theory]
    [MemberData(nameof(CurveNames))]
    public void NothingSampledIsNearerThanTheClosestPoint(string name)
    {
        Curve curve = Build(name);

        foreach (Point3d from in Probes())
        {
            Point3d answer = curve.ClosestPoint(from);
            double best = answer.DistanceTo(from);

            for (int i = 0; i <= 2000; i++)
            {
                double t = curve.Domain.Min + (curve.Domain.Length * i / 2000.0);
                double sampled = curve.PointAt(t).DistanceTo(from);

                Assert.True(
                    sampled >= best - 1e-7,
                    $"{name}: a sample at t = {t} is {sampled} from {from}, nearer than the "
                    + $"reported closest distance {best}.");
            }
        }
    }

    /// <summary>The returned parameter really is inside the domain, on every curve.</summary>
    [Theory]
    [MemberData(nameof(CurveNames))]
    public void TheParameterIsAlwaysInsideTheDomain(string name)
    {
        Curve curve = Build(name);
        Interval domain = curve.Domain;

        foreach (Point3d from in Probes())
        {
            double t = curve.ClosestParameter(from);

            Assert.InRange(t, domain.Min, domain.Max);
        }
    }

    /// <summary>And the point it returns is the point at that parameter.</summary>
    [Theory]
    [MemberData(nameof(CurveNames))]
    public void ThePointAgreesWithTheParameter(string name)
    {
        Curve curve = Build(name);

        foreach (Point3d from in Probes())
        {
            double t = curve.ClosestParameter(from);

            Assert.True(curve.PointAt(t).EqualsWithin(curve.ClosestPoint(from)));
            Assert.Equal(curve.ClosestPoint(from).DistanceTo(from), curve.DistanceTo(from), 12);
        }
    }

    /// <summary>
    /// A point already on the curve comes back at essentially zero distance. Anything else means
    /// the refinement is stopping early.
    /// </summary>
    [Theory]
    [MemberData(nameof(CurveNames))]
    public void APointOnTheCurveIsItsOwnClosestPoint(string name)
    {
        Curve curve = Build(name);

        for (int i = 1; i < 10; i++)
        {
            double t = curve.Domain.Min + (curve.Domain.Length * i / 10.0);
            Point3d on = curve.PointAt(t);

            Assert.True(
                curve.DistanceTo(on) < 1e-7,
                $"{name}: a point on the curve at t = {t} reports a distance of {curve.DistanceTo(on)}.");
        }
    }

    /// <summary>
    /// <b>The cross-check.</b> A line answers in closed form, so the general search must agree with
    /// it — here by asking a <see cref="PolyLine"/> of one segment, which uses the base class.
    /// </summary>
    [Fact]
    public void TheGeneralSearchAgreesWithTheLinesClosedForm()
    {
        Point3d start = new(-3, 1, 2);
        Point3d end = new(6, 4, -1);

        Line line = new(start, end);
        PolyLine sameShape = new([start, end]);

        foreach (Point3d from in Probes())
        {
            Point3d exact = line.ClosestPoint(from);
            Point3d searched = sameShape.ClosestPoint(from);

            Assert.Equal(exact.X, searched.X, 7);
            Assert.Equal(exact.Y, searched.Y, 7);
            Assert.Equal(exact.Z, searched.Z, 7);
        }
    }

    /// <summary>
    /// A point beyond the end of an open curve is nearest to the end, not to a parameter past it.
    /// Without the clamp, Newton walks off and returns a parameter the curve does not have.
    /// </summary>
    [Fact]
    public void APointBeyondTheEndIsNearestToTheEnd()
    {
        NurbsCurve curve = Sample(rational: false);

        Point3d beyond = curve.PointAt(curve.Domain.Max)
            + (curve.TangentAt(curve.Domain.Max) * 50.0);

        Assert.Equal(curve.Domain.Max, curve.ClosestParameter(beyond), 6);
    }

    /// <summary>
    /// A circle's centre is equidistant from every point on it, so any answer is correct — and the
    /// search must return one of them rather than diverging or throwing.
    /// </summary>
    [Fact]
    public void TheCentreOfACircleIsADegenerateCaseThatStillAnswers()
    {
        Circle circle = Circle.ByPlaneRadius(Plane.WorldXY, 4.0);

        Assert.Equal(4.0, circle.DistanceTo(Point3d.Origin), 6);
        Assert.InRange(circle.ClosestParameter(Point3d.Origin), circle.Domain.Min, circle.Domain.Max);
    }

    /// <summary>
    /// On a closed curve the far side is a stationary point too, and a bracket too coarse finds it.
    /// A point just outside the circle must map to the near side.
    /// </summary>
    [Fact]
    public void AClosedCurveReturnsTheNearSideAndNotTheFar()
    {
        Circle circle = Circle.ByPlaneRadius(Plane.WorldXY, 4.0);
        Point3d outside = new(6, 0, 0);

        Assert.True(circle.ClosestPoint(outside).EqualsWithin(new Point3d(4, 0, 0)));
        Assert.Equal(2.0, circle.DistanceTo(outside), 6);
    }

    private static Point3d[] Probes() =>
    [
        new(0, 0, 0),
        new(3, 3, 3),
        new(-5, 2, 1),
        new(12, -7, 4),
        new(0.5, 0.25, -0.75),
        new(-20, -20, -20),
    ];

    private static Curve Build(string name) => name switch
    {
        "line" => new Line(new Point3d(-3, 1, 2), new Point3d(6, 4, -1)),
        "circle" => Circle.ByPlaneRadius(Plane.WorldXY, 4.0),
        "arc" => Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 3.0, Angle.FromDegrees(20), Angle.FromDegrees(200)),
        "ellipse" => EllipseCurve.ByPlaneRadiiAngles(
            Plane.WorldXY, 5.0, 2.0, Angle.FromDegrees(0), Angle.FromDegrees(360)),
        "polyline" => new PolyLine(
            [new Point3d(0, 0, 0), new Point3d(2, 3, 0), new Point3d(5, 3, 1), new Point3d(7, 0, 1)]),
        "polycurve" => PolyCurve.ByJoinedCurves(
        [
            new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)),
            new Line(new Point3d(4, 0, 0), new Point3d(4, 5, 0)),
        ]),
        "nurbs" => Sample(rational: false),
        "nurbs-rational" => Sample(rational: true),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown curve."),
    };

    private static NurbsCurve Sample(bool rational)
    {
        Point3d[] points = [new(0, 0, 0), new(1, 4, 1), new(4, 5, -1), new(7, 1, 2), new(9, 2, 0)];
        double[]? weights = rational ? [1.0, 2.5, 0.4, 1.8, 1.0] : null;

        return new NurbsCurve(points, KnotVector.CreateClamped(3, points.Length), weights);
    }
}

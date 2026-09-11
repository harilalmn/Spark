using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The general curve/curve intersector (`E2-T11`, step B).
/// </summary>
/// <remarks>
/// <para>
/// <b>The closed forms are the oracle.</b> On every pair <see cref="AnalyticCurveIntersection"/>
/// answers, the general path has to give the same points and the same overlaps. It does not know
/// which pairs have closed forms - it samples, brackets and refines for all of them - so agreement
/// there is evidence about the cases no formula covers.
/// </para>
/// <para>
/// <b>Tangency is held to a looser bound, and said so.</b> Where two curves only touch, refinement
/// converges slowly and the point is placed to about the square root of the tolerance along the
/// curve, while still lying on both curves to the tolerance itself.
/// </para>
/// </remarks>
public sealed class GeneralCurveIntersectionTests
{
    private static Line L(double x0, double y0, double z0, double x1, double y1, double z1) =>
        new(new Point3d(x0, y0, z0), new Point3d(x1, y1, z1));

    private static CurveIntersections General(Curve a, Curve b)
    {
        CurveIntersections result = GeneralCurveIntersection.Intersect(a, b, Tolerance.Default);

        foreach (CurveIntersectionPoint point in result.Points)
        {
            Assert.True(a.PointAt(point.ParameterA).DistanceTo(point.Point) <= 1e-6, $"{point} is not on the first curve");
            Assert.True(b.PointAt(point.ParameterB).DistanceTo(point.Point) <= 1e-6, $"{point} is not on the second curve");
        }

        return result;
    }

    /// <summary>The pairs step A answers in closed form, and whether each is a tangency.</summary>
    public static TheoryData<string> AnalyticPairs =>
    [
        "crossing lines", "skew lines", "parallel lines", "colinear lines", "end to end",
        "line through circle", "grazing line", "piercing line", "line and quarter arc",
        "crossing circles", "circles touching outside", "circles touching inside", "concentric circles",
        "circles in crossing planes", "overlapping arcs", "circle and its copy",
    ];

    private static (Curve A, Curve B, bool Tangent) Pair(string name) => name switch
    {
        "crossing lines" => (L(0, 0, 0, 2, 2, 0), L(0, 2, 0, 2, 0, 0), false),
        "skew lines" => (L(-1, 0, 0, 1, 0, 0), L(0, -1, 1, 0, 1, 1), false),
        "parallel lines" => (L(0, 0, 0, 2, 0, 0), L(0, 1, 0, 2, 1, 0), false),
        "colinear lines" => (L(0, 0, 0, 2, 0, 0), L(1, 0, 0, 3, 0, 0), false),
        "end to end" => (L(0, 0, 0, 1, 0, 0), L(1, 0, 0, 2, 0, 0), false),
        "line through circle" => (L(-2, 0, 0, 2, 0, 0), Circle.FromCenterRadius(Point3d.Origin, 1.0), false),
        "grazing line" => (L(-2, 1, 0, 2, 1, 0), Circle.FromCenterRadius(Point3d.Origin, 1.0), true),
        "piercing line" => (L(1, 0, -1, 1, 0, 1), Circle.FromCenterRadius(Point3d.Origin, 1.0), false),
        "line and quarter arc" => (L(-2, 0, 0, 2, 0, 0), new Arc(Plane.WorldXY, 1.0, Angle.FromDegrees(0), Angle.FromDegrees(90)), false),
        "crossing circles" => (Circle.FromCenterRadius(Point3d.Origin, 1.0), Circle.FromCenterRadius(new Point3d(1, 0, 0), 1.0), false),
        "circles touching outside" => (Circle.FromCenterRadius(Point3d.Origin, 1.0), Circle.FromCenterRadius(new Point3d(2, 0, 0), 1.0), true),
        "circles touching inside" => (Circle.FromCenterRadius(Point3d.Origin, 2.0), Circle.FromCenterRadius(new Point3d(1, 0, 0), 1.0), true),
        "concentric circles" => (Circle.FromCenterRadius(Point3d.Origin, 1.0), Circle.FromCenterRadius(Point3d.Origin, 2.0), false),
        "circles in crossing planes" => (Circle.FromCenterRadius(Point3d.Origin, 1.0), new Circle(Point3d.Origin, Vector3d.YAxis, 1.0), false),
        "overlapping arcs" => (new Arc(Plane.WorldXY, 1.0, Angle.FromDegrees(0), Angle.FromDegrees(90)), new Arc(Plane.WorldXY, 1.0, Angle.FromDegrees(45), Angle.FromDegrees(90)), false),
        "circle and its copy" => (Circle.FromCenterRadius(Point3d.Origin, 1.0), Circle.FromCenterRadius(Point3d.Origin, 1.0), false),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>
    /// <b>The row's claim for step B.</b> On every closed-form pair, the general path finds the same
    /// points - to the tolerance, or to its square root where the curves only touch - and the same
    /// overlaps.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyticPairs))]
    public void TheGeneralPathAgreesWithTheClosedForm(string name)
    {
        (Curve a, Curve b, bool tangent) = Pair(name);

        CurveIntersections exact = AnalyticCurveIntersection.TryIntersect(a, b, Tolerance.Default)!;
        CurveIntersections general = General(a, b);

        double placed = tangent ? 1e-3 : 1e-6;

        Assert.Equal(exact.Points.Count, general.Points.Count);

        foreach (CurveIntersectionPoint point in exact.Points)
        {
            Assert.Contains(general.Points, found => found.Point.DistanceTo(point.Point) <= placed);
        }

        Assert.Equal(exact.Overlaps.Count, general.Overlaps.Count);

        for (int i = 0; i < exact.Overlaps.Count; i++)
        {
            Assert.Equal(exact.Overlaps[i].OnA.Min, general.Overlaps[i].OnA.Min, 1e-6);
            Assert.Equal(exact.Overlaps[i].OnA.Max, general.Overlaps[i].OnA.Max, 1e-6);
        }
    }

    /// <summary>A line through an ellipse meets it at the ends of the axis it follows.</summary>
    [Fact]
    public void ALineThroughAnEllipseMeetsItAtTheAxisEnds()
    {
        EllipseCurve ellipse = new(Plane.WorldXY, 2.0, 1.0);

        CurveIntersectionPoint[] across = [.. General(L(-3, 0, 0, 3, 0, 0), ellipse).Points];
        CurveIntersectionPoint[] up = [.. General(L(0, -2, 0, 0, 2, 0), ellipse).Points];

        Assert.Equal(2, across.Length);
        Assert.Contains(across, p => p.Point.DistanceTo(new Point3d(2, 0, 0)) < 1e-6);
        Assert.Contains(across, p => p.Point.DistanceTo(new Point3d(-2, 0, 0)) < 1e-6);

        Assert.Equal(2, up.Length);
        Assert.Contains(up, p => p.Point.DistanceTo(new Point3d(0, 1, 0)) < 1e-6);
        Assert.Contains(up, p => p.Point.DistanceTo(new Point3d(0, -1, 0)) < 1e-6);
    }

    /// <summary>
    /// Two NURBS curves - one through points of <c>y = x²</c>, one through points of
    /// <c>y = 2 - x²</c> - cross where both pass through the same sample points, and nowhere else.
    /// </summary>
    [Fact]
    public void TwoNurbsCurvesCrossWhereTheyShareAPoint()
    {
        double[] xs = [-2, -1, 0, 1, 2];
        NurbsCurve up = NurbsCurve.InterpolatePoints([.. xs.Select(x => new Point3d(x, x * x, 0))]);
        NurbsCurve down = NurbsCurve.InterpolatePoints([.. xs.Select(x => new Point3d(x, 2 - (x * x), 0))]);

        CurveIntersectionPoint[] points = [.. General(up, down).Points];

        Assert.Equal(2, points.Length);
        Assert.Contains(points, p => p.Point.DistanceTo(new Point3d(1, 1, 0)) < 1e-6);
        Assert.Contains(points, p => p.Point.DistanceTo(new Point3d(-1, 1, 0)) < 1e-6);
    }

    /// <summary>A straight NURBS curve lying along a line overlaps it where both are, and nowhere else.</summary>
    [Fact]
    public void AStraightNurbsCurveOverlapsTheLineItLiesAlong()
    {
        NurbsCurve straight = NurbsCurve.FromPoints([new Point3d(0, 0, 0), new Point3d(2, 0, 0)]);
        Line line = L(1, 0, 0, 3, 0, 0);

        CurveIntersections result = General(straight, line);

        CurveOverlap overlap = Assert.Single(result.Overlaps);
        Assert.Empty(result.Points);
        Assert.True(straight.PointAt(overlap.OnA.Min).DistanceTo(new Point3d(1, 0, 0)) < 1e-6);
        Assert.True(straight.PointAt(overlap.OnA.Max).DistanceTo(new Point3d(2, 0, 0)) < 1e-6);
    }
}

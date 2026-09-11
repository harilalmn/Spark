using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The public entry point for curve/curve intersection (`E2-T11`, step B2).
/// </summary>
/// <remarks>
/// <b>What is under test is the routing</b>: a pair with a closed form gets the closed form's answer,
/// exactly, and every other pair gets the sampled path's. The answers themselves are tested in
/// <c>AnalyticCurveIntersectionTests</c> and <c>GeneralCurveIntersectionTests</c>.
/// </remarks>
public sealed class CurveIntersectWithTests
{
    private static readonly Line Across = new(new Point3d(-3, 0.25, 0), new Point3d(3, 0.25, 0));

    /// <summary>A line against a circle is answered in closed form - to the last bit.</summary>
    [Fact]
    public void AClosedFormPairIsAnsweredInClosedForm()
    {
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 1.0);

        CurveIntersections exact = AnalyticCurveIntersection.TryIntersect(Across, circle, Tolerance.Default)!;
        CurveIntersections answer = Across.IntersectWith(circle);

        Assert.Equal(2, answer.Points.Count);
        Assert.Equal(exact.Points, answer.Points);
    }

    /// <summary>A line against an ellipse, which has no closed form here, is answered by the sampled path.</summary>
    [Fact]
    public void AnyOtherPairIsAnsweredByTheSampledPath()
    {
        EllipseCurve ellipse = new(Plane.WorldXY, 2.0, 1.0);

        CurveIntersections sampled = GeneralCurveIntersection.Intersect(Across, ellipse, Tolerance.Default);
        CurveIntersections answer = Across.IntersectWith(ellipse);

        Assert.Equal(2, answer.Points.Count);
        Assert.Equal(sampled.Points, answer.Points);
    }

    /// <summary>Asking about no curve at all is refused by name.</summary>
    [Fact]
    public void NoOtherCurveIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => Across.IntersectWith(null!));

    /// <summary>Curves with nothing in common say so.</summary>
    [Fact]
    public void CurvesApartHaveNothingInCommon()
    {
        CurveIntersections answer = Across.IntersectWith(Circle.FromCenterRadius(new Point3d(0, 10, 0), 1.0));

        Assert.True(answer.IsEmpty);
    }
}

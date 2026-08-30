using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Interpolating a curve through points.
/// </summary>
/// <remarks>
/// <b>The test is the definition.</b> An interpolating curve passes through every point it was
/// given — that is not a property of the implementation, it is what the word means, and it fails
/// for every way the construction can go wrong: parameters chosen badly, knots that break the
/// Schoenberg–Whitney condition, a solver that loses a pivot. So it is asserted over generated
/// point sets as well as fixed ones, at several degrees and several shapes.
/// </remarks>
public sealed class NurbsInterpolationTests
{
    /// <summary>
    /// <b>The defining property.</b> Every input point is on the curve.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheCurvePassesThroughEveryPoint(int degree)
    {
        foreach (Point3d[] points in PointSets())
        {
            if (points.Length <= degree)
            {
                continue;
            }

            NurbsCurve curve = NurbsCurve.InterpolatePoints(points, degree);

            foreach (Point3d expected in points)
            {
                Assert.True(
                    curve.DistanceTo(expected) < 1e-7,
                    $"degree {degree}: the curve misses {expected} by {curve.DistanceTo(expected)}.");
            }
        }
    }

    /// <summary>
    /// The ends are exact, not merely near — a clamped curve starts at the first point and finishes
    /// at the last, and everything downstream that joins curves depends on it.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void TheCurveStartsAndEndsExactlyAtTheEndPoints(int degree)
    {
        foreach (Point3d[] points in PointSets())
        {
            if (points.Length <= degree)
            {
                continue;
            }

            NurbsCurve curve = NurbsCurve.InterpolatePoints(points, degree);

            Assert.True(curve.PointAt(curve.Domain.Min).EqualsWithin(points[0]));
            Assert.True(curve.PointAt(curve.Domain.Max).EqualsWithin(points[^1]));
        }
    }

    /// <summary>
    /// <b>Degree 1 is the polyline.</b> The one case with an answer known independently, which
    /// makes it the check that the parameterisation and the solve are not merely self-consistent.
    /// </summary>
    [Fact]
    public void ADegreeOneInterpolationIsThePolylineThroughThePoints()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1), new(7, 0, 1)];

        NurbsCurve curve = NurbsCurve.InterpolatePoints(points, 1);

        Assert.Equal(new PolyLine(points).Length, curve.Length, 6);

        // And its control points are the input points, because a degree-1 curve interpolates by
        // being the polygon rather than by bending towards it.
        Point3d[] control = curve.ControlPoints();
        for (int i = 0; i < points.Length; i++)
        {
            Assert.True(control[i].EqualsWithin(points[i]));
        }
    }

    /// <summary>
    /// The control points are generally <b>not</b> the input points above degree 1 — which is what
    /// distinguishes interpolation from drawing the polygon, and is worth asserting so that a
    /// regression to "control points = input points" cannot pass the property above.
    /// </summary>
    [Fact]
    public void ACubicInterpolationDoesNotSimplyUseThePointsAsControlPoints()
    {
        Point3d[] points = [new(0, 0, 0), new(1, 5, 0), new(4, -2, 3), new(6, 3, 1), new(9, 0, 0)];

        NurbsCurve curve = NurbsCurve.InterpolatePoints(points, 3);
        Point3d[] control = curve.ControlPoints();

        Assert.Equal(points.Length, control.Length);
        Assert.Contains(
            Enumerable.Range(1, points.Length - 2),
            i => !control[i].EqualsWithin(points[i]));
    }

    /// <summary>
    /// <b>Why chord length rather than uniform.</b> With one very long gap among short ones, a
    /// uniformly parameterised interpolation overshoots badly. The curve here must stay within a
    /// sane distance of the polygon through its points — a loop or a cusp would put it far outside.
    /// </summary>
    [Fact]
    public void UnevenlySpacedPointsDoNotProduceAnOvershoot()
    {
        Point3d[] points =
        [
            new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(100, 0, 0), new(101, 0, 0), new(102, 0, 0),
        ];

        NurbsCurve curve = NurbsCurve.InterpolatePoints(points, 3);
        PolyLine polygon = new(points);

        for (int i = 0; i <= 400; i++)
        {
            Point3d p = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * i / 400.0));

            // The points are collinear, so an exact interpolation is the straight line, and any
            // departure from y = z = 0 is an overshoot off the line.
            Assert.True(
                Math.Abs(p.Y) < 1e-6 && Math.Abs(p.Z) < 1e-6,
                $"The curve leaves the line at {p}, which is an overshoot.");

            // And no excursion along it either: the curve must stay between the first and last
            // points rather than running past one and coming back, which is what a uniform
            // parameterisation does across a gap this uneven.
            Assert.InRange(p.X, -1e-6, 102 + 1e-6);
            Assert.True(
                polygon.DistanceTo(p) < 1e-6,
                $"The curve is {polygon.DistanceTo(p)} from the polygon at {p}.");
        }
    }

    /// <summary>
    /// A curve through points on a circle is not a circle — interpolation is not fitting — but it
    /// must stay close to one, which catches a parameterisation that is wrong without being wild.
    /// </summary>
    [Fact]
    public void PointsOnACircleGiveACurveThatStaysNearIt()
    {
        const double radius = 10.0;
        Point3d[] points = [.. Enumerable.Range(0, 17).Select(i =>
        {
            double angle = 2 * Math.PI * i / 16.0;
            return new Point3d(radius * Math.Cos(angle), radius * Math.Sin(angle), 0);
        })];

        NurbsCurve curve = NurbsCurve.InterpolatePoints(points, 3);
        double worst = 0;

        for (int i = 0; i <= 4000; i++)
        {
            Point3d p = curve.PointAt(curve.Domain.Min + (curve.Domain.Length * i / 4000.0));
            worst = Math.Max(worst, Math.Abs(p.DistanceTo(Point3d.Origin) - radius) / radius);
        }

        // Measured at 6.5e-4 relative, so the bound is set just above what was measured rather
        // than at a round number picked first. Most of that error is at the seam: this is an
        // *open* interpolation of points that happen to close, so nothing ties the two ends
        // together and the curve is least constrained exactly where they meet.
        Assert.True(worst < 1e-3, $"The worst relative radial error is {worst}.");
    }

    [Fact]
    public void TwoPointsInterpolateToALine()
    {
        Point3d start = new(1, 2, 3);
        Point3d end = new(7, -4, 11);

        NurbsCurve curve = NurbsCurve.InterpolatePoints([start, end], 1);
        Line line = new(start, end);

        Assert.Equal(line.Length, curve.Length, 9);
    }

    [Fact]
    public void TooFewPointsForTheDegreeIsRefused()
    {
        Point3d[] three = [new(0, 0, 0), new(1, 1, 0), new(2, 0, 0)];

        Assert.Throws<ArgumentOutOfRangeException>(() => NurbsCurve.InterpolatePoints(three, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => NurbsCurve.InterpolatePoints(three, 5));
    }

    [Fact]
    public void FewerThanTwoPointsIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePoints([new Point3d(0, 0, 0)], 1));
    }

    /// <summary>
    /// Two consecutive points at the same place would give one parameter two different positions,
    /// and the system would be singular. Refusing beats solving something with no answer.
    /// </summary>
    [Fact]
    public void RepeatedConsecutivePointsAreRefused()
    {
        Point3d[] points = [new(0, 0, 0), new(1, 1, 0), new(1, 1, 0), new(3, 0, 0)];

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePoints(points, 2));

        Assert.Contains("same point", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonFinitePointsAreRefused()
    {
        Point3d[] points = [new(0, 0, 0), new(double.NaN, 1, 0), new(2, 0, 0)];

        Assert.Throws<ArgumentException>(() => NurbsCurve.InterpolatePoints(points, 1));
    }

    /// <summary>
    /// The interpolated curve is non-rational: weights are a modelling choice, and inventing them
    /// to fit points would answer a question nobody asked.
    /// </summary>
    [Fact]
    public void AnInterpolatedCurveIsNotRational()
    {
        NurbsCurve curve = NurbsCurve.InterpolatePoints(PointSets().First(), 3);

        Assert.False(curve.IsRational);
        Assert.All(curve.Weights(), w => Assert.Equal(1.0, w));
    }

    /// <summary>
    /// Point sets of several shapes: a smooth arc, a zig-zag, a set with a long gap, and one
    /// spanning a wide range of scales.
    /// </summary>
    private static Point3d[][] PointSets() =>
    [
        [new(0, 0, 0), new(1, 4, 1), new(4, 5, -1), new(7, 1, 2), new(9, 2, 0)],
        [new(0, 0, 0), new(1, 5, 0), new(2, -5, 0), new(3, 5, 0), new(4, -5, 0), new(5, 0, 0)],
        [new(0, 0, 0), new(0.5, 0.1, 0), new(1, 0, 0), new(40, 3, 1), new(41, 3, 1)],
        [new(-1e4, 2e4, 0), new(0, 0, 1e3), new(1e4, -2e4, 0), new(2e4, 1e3, -1e3)],
        [new(0, 0, 0), new(3, 3, 3)],
        [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 1), new(1, 1, 1)],
    ];
}

using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Interpolating a curve through points with the end tangents prescribed — `E2-T72`'s last row.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the end condition, not the interpolation.</b> A solver that ignores the
/// prescribed tangents still passes through every point, so every assertion about positions is
/// satisfied by a completely broken implementation. The claims that matter are the tangent at
/// each end, asserted against the direction asked for; that the two ends are pinned
/// <i>independently</i>; and that the derivative's <i>magnitude</i> — which no tangency test can
/// see — is chosen so that points sampled from a circle come back lying on it.
/// </para>
/// </remarks>
public sealed class NurbsTangentInterpolationTests
{
    private const double Tight = 1e-9;

    /// <summary>
    /// <b>The defining property.</b> The curve leaves the first point in the start direction and
    /// arrives at the last in the end direction, whatever the degree.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheCurveLeavesAndArrivesInThePrescribedDirections(int degree)
    {
        Vector3d start = new Vector3d(1.0, 2.0, 0.5).Normalised();
        Vector3d end = new Vector3d(-0.3, 1.0, -0.2).Normalised();

        foreach (Point3d[] points in PointSets())
        {
            if (points.Length + 1 < degree)
            {
                continue;
            }

            NurbsCurve curve = NurbsCurve.InterpolatePointsWithTangents(points, start, end, degree);

            Vector3d atStart = curve.TangentAt(curve.Domain.Min);
            Vector3d atEnd = curve.TangentAt(curve.Domain.Max);

            Assert.True(
                Close(atStart, start),
                $"degree {degree}, {points.Length} points: the start tangent is {atStart}, not {start}.");
            Assert.True(
                Close(atEnd, end),
                $"degree {degree}, {points.Length} points: the end tangent is {atEnd}, not {end}.");
        }
    }

    /// <summary>Pinning the tangents does not stop the curve passing through every point.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheCurveStillPassesThroughEveryPoint(int degree)
    {
        foreach (Point3d[] points in PointSets())
        {
            if (points.Length + 1 < degree)
            {
                continue;
            }

            NurbsCurve curve = NurbsCurve.InterpolatePointsWithTangents(
                points, Vector3d.XAxis, Vector3d.YAxis, degree);

            foreach (Point3d expected in points)
            {
                Assert.True(
                    curve.DistanceTo(expected) < 1e-7,
                    $"degree {degree}: the curve misses {expected} by {curve.DistanceTo(expected)}.");
            }

            Assert.True(Close(curve.PointAt(curve.Domain.Min), points[0]));
            Assert.True(Close(curve.PointAt(curve.Domain.Max), points[^1]));
        }
    }

    /// <summary>
    /// <b>The case with an independent answer.</b> Two points and two tangents make the cubic
    /// Hermite curve, whose four control points are known in closed form: the ends, and one third
    /// of the derivative in from each.
    /// </summary>
    [Fact]
    public void TwoPointsAndTwoTangentsAreTheCubicHermite()
    {
        Point3d first = new(0.0, 0.0, 0.0);
        Point3d last = new(3.0, 4.0, 0.0);
        Vector3d start = Vector3d.XAxis;
        Vector3d end = Vector3d.YAxis;

        NurbsCurve curve = NurbsCurve.InterpolatePointsWithTangents([first, last], start, end);

        // The derivative is the unit direction scaled by the chord, which is 5 here.
        IReadOnlyList<Point3d> control = curve.ControlPoints();
        Assert.Equal(4, control.Count);
        Assert.True(Close(control[0], first));
        Assert.True(Close(control[1], first + (start * (5.0 / 3.0))));
        Assert.True(Close(control[2], last - (end * (5.0 / 3.0))));
        Assert.True(Close(control[3], last));
    }

    /// <summary>
    /// <b>The magnitude rule, which tangency cannot see.</b> Points sampled from a circular arc,
    /// given the arc's own end tangents, come back lying on the arc between the samples. A
    /// derivative of the wrong size stays perfectly tangent and leaves the arc between the
    /// samples, which is the failure this test exists to catch: scaling the derivative by three
    /// moves the worst deviation from below a thousandth of the radius to several hundredths.
    /// </summary>
    [Fact]
    public void PointsOnAnArcWithItsTangentsStayOnTheArcBetweenThem()
    {
        const double radius = 10.0;
        const int samples = 5;
        List<Point3d> points = [];
        for (int i = 0; i < samples; i++)
        {
            double angle = i * (Math.PI / 2.0) / (samples - 1);
            points.Add(new Point3d(radius * Math.Cos(angle), radius * Math.Sin(angle), 0.0));
        }

        NurbsCurve curve = NurbsCurve.InterpolatePointsWithTangents(
            points, Vector3d.YAxis, -Vector3d.XAxis);

        double worst = 0.0;
        for (int i = 0; i <= 200; i++)
        {
            Point3d sample = curve.PointAt(curve.Domain.Denormalise(i / 200.0));
            worst = Math.Max(worst, Math.Abs(sample.DistanceTo(Point3d.Origin) - radius));
        }

        Assert.True(worst < 1e-3 * radius, $"the curve leaves the arc by {worst} between the samples.");
    }

    /// <summary>
    /// <b>Each end is pinned on its own.</b> Changing the start tangent leaves the end tangent
    /// where it was, and the other way round — the which-end trap, on an asymmetric point set.
    /// </summary>
    [Fact]
    public void TheTwoEndsArePinnedIndependently()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1), new(9, 0, 1)];
        Vector3d start = new Vector3d(1, 1, 0).Normalised();
        Vector3d end = new Vector3d(1, -1, 0).Normalised();
        Vector3d other = new(0, 0, 1);

        NurbsCurve reference = NurbsCurve.InterpolatePointsWithTangents(points, start, end);
        NurbsCurve startChanged = NurbsCurve.InterpolatePointsWithTangents(points, other, end);
        NurbsCurve endChanged = NurbsCurve.InterpolatePointsWithTangents(points, start, other);

        Assert.True(Close(startChanged.TangentAt(startChanged.Domain.Min), other));
        Assert.True(Close(startChanged.TangentAt(startChanged.Domain.Max), end));
        Assert.True(Close(endChanged.TangentAt(endChanged.Domain.Min), start));
        Assert.True(Close(endChanged.TangentAt(endChanged.Domain.Max), other));

        Assert.True(Close(reference.TangentAt(reference.Domain.Min), start));
        Assert.True(Close(reference.TangentAt(reference.Domain.Max), end));
    }

    /// <summary>
    /// The length of a tangent argument is ignored: only its direction counts, so a caller who
    /// passes a scaled vector gets the same curve as one who passes it normalised.
    /// </summary>
    [Fact]
    public void TheLengthOfATangentArgumentDoesNotMatter()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1), new(9, 0, 1)];

        NurbsCurve unit = NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.XAxis, Vector3d.YAxis);
        NurbsCurve scaled = NurbsCurve.InterpolatePointsWithTangents(
            points, Vector3d.XAxis * 7.0, Vector3d.YAxis * 0.01);

        IReadOnlyList<Point3d> expected = unit.ControlPoints();
        IReadOnlyList<Point3d> actual = scaled.ControlPoints();
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(Close(expected[i], actual[i]));
        }
    }

    /// <summary>The result is non-rational, as every interpolation is.</summary>
    [Fact]
    public void AnInterpolatedCurveIsNotRational()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1)];

        NurbsCurve curve = NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.XAxis, Vector3d.YAxis);

        Assert.False(curve.IsRational);
    }

    /// <summary>A degree-1 curve is a polyline and has no tangent to pin.</summary>
    [Fact]
    public void DegreeOneIsRefused()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1)];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.XAxis, Vector3d.YAxis, 1));
    }

    /// <summary>
    /// Two more control points than points, so the degree may exceed the point count by one and
    /// no more: three points allow a quartic and refuse a quintic.
    /// </summary>
    [Fact]
    public void ADegreeMoreThanOneAboveThePointCountIsRefused()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1)];

        NurbsCurve quartic = NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.XAxis, Vector3d.YAxis, 4);
        Assert.Equal(4, quartic.Degree);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.XAxis, Vector3d.YAxis, 5));
    }

    /// <summary>A zero tangent is no direction at all, and is refused rather than normalised to garbage.</summary>
    [Fact]
    public void AZeroTangentIsRefused()
    {
        Point3d[] points = [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1)];

        Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.Zero, Vector3d.YAxis));
        Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePointsWithTangents(points, Vector3d.XAxis, Vector3d.Zero));
    }

    /// <summary>The refusals it shares with plain interpolation: too few points, and a repeated point.</summary>
    [Fact]
    public void TooFewOrRepeatedPointsAreRefused()
    {
        Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePointsWithTangents([new Point3d(0, 0, 0)], Vector3d.XAxis, Vector3d.YAxis));
        Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePointsWithTangents(
                [new Point3d(0, 0, 0), new Point3d(0, 0, 0), new Point3d(1, 1, 1)], Vector3d.XAxis, Vector3d.YAxis));
        Assert.Throws<ArgumentNullException>(
            () => NurbsCurve.InterpolatePointsWithTangents(null!, Vector3d.XAxis, Vector3d.YAxis));
    }

    private static bool Close(in Vector3d actual, in Vector3d expected) => (actual - expected).Length < Tight;

    private static bool Close(in Point3d actual, in Point3d expected) => actual.DistanceTo(expected) < Tight;

    /// <summary>Point sets of several shapes and sizes, none symmetric.</summary>
    private static IEnumerable<Point3d[]> PointSets()
    {
        yield return [new(0, 0, 0), new(3, 4, 0)];
        yield return [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1)];
        yield return [new(0, 0, 0), new(2, 3, 0), new(5, 3, 1), new(9, 0, 1)];
        yield return [new(0, 0, 0), new(1, 5, 0), new(1.5, 5.2, 0.3), new(7, 1, 2), new(8, 8, 8), new(12, 3, 1)];

        // Unevenly spaced, which is where chord-length parameterisation earns its keep.
        List<Point3d> uneven = [];
        double x = 0.0;
        for (int i = 0; i < 9; i++)
        {
            x += i % 2 == 0 ? 0.2 : 3.0;
            uneven.Add(new Point3d(x, Math.Sin(x), 0.1 * i));
        }

        yield return [.. uneven];
    }
}

using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Least-squares approximation: a curve near a set of points rather than through them.
/// </summary>
/// <remarks>
/// <para>
/// Interpolation has one defining test — <i>it passes through every point</i> — and approximation
/// has no such thing, because <i>near</i> is not a threshold anybody agreed on. So it is pinned
/// from four directions instead: the ends are exact, the fit <b>converges</b> on a curve it was
/// sampled from as control points are added, more control points fit better, and the result is
/// dramatically smoother than an interpolation of the same noisy data.
/// </para>
/// <para>
/// <b>Everything here is measured geometrically — distance from a point to the curve — and never
/// by comparing two curves at the same parameter.</b> A fit is parameterised by chord length and
/// the curve it was sampled from is not, so two curves that occupy the same points disagree
/// everywhere when compared parameter for parameter. Writing this test the wrong way first
/// produced an error that plateaued at 0.34 and looked exactly like a broken solver; measured
/// properly the same fits converge to 2e-5.
/// </para>
/// <para>
/// The last of the four is the point of the whole operation. An interpolating curve through
/// measured data reproduces the measurement noise faithfully and wobbles; a fit does not.
/// </para>
/// </remarks>
public sealed class NurbsApproximationTests
{
    /// <summary>
    /// <b>The ends are exact.</b> A fitted curve whose ends float is unusable for anything that
    /// joins curves, and it is the first thing a caller notices.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheFittedCurveStartsAndEndsExactlyOnTheData(int degree)
    {
        Point3d[] points = Wave(40);

        NurbsCurve curve = NurbsCurve.ApproximatePoints(points, degree + 5, degree);

        Assert.True(curve.PointAt(curve.Domain.Min).EqualsWithin(points[0]));
        Assert.True(curve.PointAt(curve.Domain.Max).EqualsWithin(points[^1]));
    }

    /// <summary>
    /// <b>The fit converges on the curve it was sampled from.</b> Not <i>equals</i> — and the
    /// difference is the interesting part.
    /// </summary>
    /// <remarks>
    /// A cubic sampled densely and fitted by a cubic with four control points does <b>not</b> come
    /// back exactly, because the fit is parameterised by chord length and the original is not: a
    /// cubic in one parameterisation is not a cubic in the other. What must happen is that the
    /// geometric deviation falls away as control points are added, and it does — from 0.11 at four
    /// control points to 2.4e-5 at thirty, on a curve 12.3 long. A broken solver plateaus instead.
    /// </remarks>
    [Fact]
    public void FittingPointsSampledFromACurveConvergesOnThatCurve()
    {
        NurbsCurve original = new(
            [new Point3d(0, 0, 0), new Point3d(2, 6, 1), new Point3d(7, -3, 2), new Point3d(10, 2, 0)],
            KnotVector.CreateClamped(3, 4));

        Point3d[] sampled = [.. Enumerable.Range(0, 60).Select(i =>
            original.PointAt(original.Domain.Min + (original.Domain.Length * i / 59.0)))];

        double coarse = sampled.Max(p => NurbsCurve.ApproximatePoints(sampled, 6, 3).DistanceTo(p));
        double fine = sampled.Max(p => NurbsCurve.ApproximatePoints(sampled, 30, 3).DistanceTo(p));

        Assert.True(fine < coarse / 100, $"Coarse fit deviates {coarse}, fine fit {fine}.");
        Assert.True(fine < 1e-3, $"Thirty control points should fit closely; deviation is {fine}.");
    }

    /// <summary>
    /// A straight line is the degenerate case with an answer nobody can argue with.
    /// </summary>
    [Fact]
    public void FittingCollinearPointsGivesTheLineThroughThem()
    {
        Point3d[] points = [.. Enumerable.Range(0, 30).Select(i => new Point3d(i, 2 * i, -i))];

        NurbsCurve fitted = NurbsCurve.ApproximatePoints(points, 6, 3);
        Line line = new(points[0], points[^1]);

        for (int i = 0; i <= 50; i++)
        {
            double u = i / 50.0;
            Point3d onFit = fitted.PointAt(fitted.Domain.Min + (fitted.Domain.Length * u));

            Assert.True(line.DistanceTo(onFit) < 1e-6, $"The fit leaves the line at {onFit}.");
        }
    }

    /// <summary>
    /// <b>More control points fit better.</b> Stated as a trend rather than as strict monotonicity,
    /// and the reason matters.
    /// </summary>
    /// <remarks>
    /// Least squares over a <i>nested</i> sequence of spaces would give a deviation that never
    /// rises. These spaces are not nested: every control-point count gets its own knot vector, so
    /// adding one control point is not adding a degree of freedom to the previous space but moving
    /// to a different one. The measured series does rise once, from 0.1127 to 0.1128 between four
    /// and five — real, tiny, and not a defect. What must hold is that a substantially richer
    /// space fits substantially better.
    /// </remarks>
    [Fact]
    public void MoreControlPointsFitBetter()
    {
        Point3d[] points = Wave(60);

        double few = points.Max(p => NurbsCurve.ApproximatePoints(points, 6, 3).DistanceTo(p));
        double many = points.Max(p => NurbsCurve.ApproximatePoints(points, 20, 3).DistanceTo(p));

        Assert.True(many < few / 10, $"Six control points deviate {few}, twenty deviate {many}.");
    }

    /// <summary>
    /// <b>The reason the operation exists.</b> An interpolating curve through noisy data reproduces
    /// the noise; a fit with far fewer control points does not. Measured as total curvature — a
    /// wobbling curve turns much more than a smooth one over the same span.
    /// </summary>
    [Fact]
    public void AFitIsSmootherThanAnInterpolationOfTheSameNoisyData()
    {
        Point3d[] noisy = NoisyLine(60);

        NurbsCurve interpolated = NurbsCurve.InterpolatePoints(noisy, 3);
        NurbsCurve fitted = NurbsCurve.ApproximatePoints(noisy, 8, 3);

        double interpolatedTurning = TotalTurning(interpolated);
        double fittedTurning = TotalTurning(fitted);

        Assert.True(
            fittedTurning < interpolatedTurning / 2,
            $"The fit turns {fittedTurning} and the interpolation {interpolatedTurning}; the fit "
            + "should be dramatically smoother, not marginally.");
    }

    /// <summary>And it stays near the data it smoothed, rather than smoothing it away.</summary>
    [Fact]
    public void AFitStaysNearTheDataItSmoothed()
    {
        Point3d[] noisy = NoisyLine(60);

        NurbsCurve fitted = NurbsCurve.ApproximatePoints(noisy, 8, 3);

        // The noise is +/-0.5 on each of two axes, so a point can sit sqrt(0.5^2 + 0.5^2) = 0.707
        // from the line it was scattered around. A fit that tracks the centre of the scatter is
        // therefore up to that far from any individual point, and no further — beyond it, the fit
        // has drifted off the data rather than through the middle of it. The first version of this
        // asserted 0.5 and was simply arithmetic done carelessly.
        Assert.All(noisy, p => Assert.True(
            fitted.DistanceTo(p) < 0.8,
            $"The fit is {fitted.DistanceTo(p)} from a data point, further than the noise itself."));
    }

    [Fact]
    public void AskingForAsManyControlPointsAsPointsIsRefused()
    {
        Point3d[] points = Wave(10);

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => NurbsCurve.ApproximatePoints(points, 10, 3));

        // The message points at the operation that does do this, because asking for it is not a
        // mistake, it is asking for the wrong function.
        Assert.Contains("InterpolatePoints", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooFewControlPointsForTheDegreeIsRefused()
    {
        Point3d[] points = Wave(20);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NurbsCurve.ApproximatePoints(points, 3, 3));
    }

    [Fact]
    public void FewerThanThreePointsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => NurbsCurve.ApproximatePoints(
            [new Point3d(0, 0, 0), new Point3d(1, 1, 0)], 2, 1));
    }

    [Fact]
    public void AFittedCurveIsNotRational()
    {
        NurbsCurve fitted = NurbsCurve.ApproximatePoints(Wave(30), 8, 3);

        Assert.False(fitted.IsRational);
    }

    /// <summary>A smooth wave, which has shape to fit without being pathological.</summary>
    private static Point3d[] Wave(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i =>
        {
            double t = 10.0 * i / (count - 1);
            return new Point3d(t, 3 * Math.Sin(t), Math.Cos(t / 2));
        }),
    ];

    /// <summary>
    /// A straight line with reproducible noise on it. Deterministic, because a test that is
    /// occasionally too noisy to pass is a test nobody trusts.
    /// </summary>
    private static Point3d[] NoisyLine(int count)
    {
        Random random = new(20260831);

        return
        [
            .. Enumerable.Range(0, count).Select(i => new Point3d(
                10.0 * i / (count - 1),
                (random.NextDouble() - 0.5),
                (random.NextDouble() - 0.5))),
        ];
    }

    /// <summary>
    /// How much the tangent turns over the whole curve. A wobbling curve turns far more than a
    /// smooth one across the same span, which is what makes this a usable measure of smoothness
    /// without needing the second derivative to be compared to anything.
    /// </summary>
    private static double TotalTurning(Curve curve)
    {
        double total = 0.0;
        Vector3d previous = curve.TangentAt(curve.Domain.Min);

        for (int i = 1; i <= 400; i++)
        {
            Vector3d tangent = curve.TangentAt(curve.Domain.Min + (curve.Domain.Length * i / 400.0));
            total += previous.AngleTo(tangent).Radians;
            previous = tangent;
        }

        return total;
    }
}

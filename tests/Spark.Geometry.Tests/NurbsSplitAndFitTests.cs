using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Splitting a curve in two, and fitting one to a stated tolerance.
/// </summary>
/// <remarks>
/// Both are built on operations already proved, so what is tested here is what they add: that
/// splitting is exact and rejoinable, and that a fit either meets the tolerance it was given or
/// <b>says it did not</b>.
/// </remarks>
public sealed class NurbsSplitAndFitTests
{
    /// <summary>
    /// A tolerance a cubic can actually reach on this data. Measured, not chosen: the fit floor for
    /// the fifty-point wave below is about 0.0037, so asking for 1e-3 is asking for something no
    /// number of control points delivers — which is a fine thing to test, and not the thing these
    /// tests are for.
    /// </summary>
    private static Tolerance Loose => new(1e-2, Angle.FromDegrees(0.001), 1e-12);

    /// <summary>
    /// <b>`E2-T33`'s property.</b> Split at a parameter and the two halves, taken together, are the
    /// original — same length, same points, meeting exactly at the cut.
    /// </summary>
    [Theory]
    [InlineData(false, 0.25)]
    [InlineData(true, 0.25)]
    [InlineData(false, 0.5)]
    [InlineData(true, 0.73)]
    public void TheTwoHalvesOfASplitAreTheOriginal(bool rational, double at)
    {
        NurbsCurve original = Sample(rational);
        double t = original.Domain.Min + (original.Domain.Length * at);

        (NurbsCurve left, NurbsCurve right) = original.Split(t);

        Assert.Equal(original.Length, left.Length + right.Length, 6);
        Assert.Equal(t, left.Domain.Max, 9);
        Assert.Equal(t, right.Domain.Min, 9);
        Assert.True(left.PointAt(t).EqualsWithin(right.PointAt(t)));

        for (int i = 0; i <= 100; i++)
        {
            double u = original.Domain.Min + (original.Domain.Length * i / 100.0);
            Curve half = u <= t ? left : right;

            Assert.True(
                original.PointAt(u).EqualsWithin(half.PointAt(u)),
                $"At u = {u} the original is at {original.PointAt(u)} and the half at {half.PointAt(u)}.");
        }
    }

    [Fact]
    public void SplittingAtAnEndIsRefused()
    {
        NurbsCurve curve = Sample(rational: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Split(curve.Domain.Min));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Split(curve.Domain.Max));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Split(curve.Domain.Max + 1));
    }

    /// <summary>
    /// <b>A fit that says it fits, does.</b> Checked against the points independently of the search
    /// that produced the answer.
    /// </summary>
    [Fact]
    public void AFitThatReportsSuccessIsInsideItsTolerance()
    {
        Point3d[] points = Wave(50);

        (NurbsCurve curve, double deviation, bool fits) = NurbsCurve.FitPoints(points, Loose);

        Assert.True(fits);
        Assert.All(points, p => Assert.True(
            curve.DistanceTo(p) <= 1e-2,
            $"A point sits {curve.DistanceTo(p)} from a curve that claimed to fit within 1e-2."));

        Assert.Equal(points.Max(p => curve.DistanceTo(p)), deviation, 9);
    }

    /// <summary>
    /// <b>And it is the smallest curve that does.</b> One fewer control point must miss, or the
    /// search stopped early and the caller is carrying control points they did not need.
    /// </summary>
    [Fact]
    public void AFitUsesTheFewestControlPointsThatMeetTheTolerance()
    {
        Point3d[] points = Wave(50);

        (NurbsCurve curve, _, bool fits) = NurbsCurve.FitPoints(points, Loose);
        Assert.True(fits);

        int used = curve.ControlPoints().Length;
        if (used <= 4)
        {
            return;
        }

        NurbsCurve smaller = NurbsCurve.ApproximatePoints(points, used - 1, 3);

        Assert.True(
            points.Max(p => smaller.DistanceTo(p)) > 1e-2,
            $"{used - 1} control points also fit, so {used} was not the fewest.");
    }

    /// <summary>
    /// <b>The failure that matters.</b> An impossible tolerance on noisy data must be reported, not
    /// chased until the fit becomes an interpolation of the noise dressed as a fit.
    /// </summary>
    [Fact]
    public void AnImpossibleToleranceIsReportedRatherThanChased()
    {
        Point3d[] noisy = NoisyLine(40);
        Tolerance impossible = new(1e-12, Angle.FromDegrees(0.001), 1e-15);

        (NurbsCurve curve, double deviation, bool fits) = NurbsCurve.FitPoints(noisy, impossible);

        Assert.False(fits);
        Assert.True(deviation > 1e-12, "The reported deviation must be the one actually achieved.");

        // And the caller still gets a usable curve rather than null or an exception.
        Assert.True(curve.ControlPoints().Length < noisy.Length);
        Assert.Equal(noisy.Max(p => curve.DistanceTo(p)), deviation, 9);
    }

    /// <summary>
    /// A loose tolerance on smooth data is met with very few control points — the case that shows
    /// the search is not simply returning the largest curve it may.
    /// </summary>
    [Fact]
    public void ALooseToleranceOnSmoothDataUsesVeryFewControlPoints()
    {
        Point3d[] points = [.. Enumerable.Range(0, 40).Select(i => new Point3d(i, 2 * i, -i))];

        (NurbsCurve curve, _, bool fits) = NurbsCurve.FitPoints(points, Loose);

        Assert.True(fits);
        Assert.True(
            curve.ControlPoints().Length <= 6,
            $"A straight line needed {curve.ControlPoints().Length} control points.");
    }

    /// <summary>
    /// <b>More control points do not always fit better, and the search must survive that.</b> As
    /// the count approaches the number of points the system becomes nearly square and the normal
    /// equations ill-conditioned: on this data the deviation falls to 0.0037 at forty control
    /// points and rises to 0.33 at forty-nine. A search that trusted monotonicity would return the
    /// worse curve, so this asserts the returned fit is no worse than any count tried.
    /// </summary>
    [Fact]
    public void AnUnreachableToleranceStillReturnsTheBestCurveSeen()
    {
        Point3d[] points = Wave(50);

        // Unreachable: the floor for this data is about 0.0037.
        Tolerance beyondReach = new(1e-5, Angle.FromDegrees(0.001), 1e-12);

        (NurbsCurve curve, double deviation, bool fits) = NurbsCurve.FitPoints(points, beyondReach);

        Assert.False(fits);

        // The search runs to the largest count it may, where the system is nearly square and the
        // normal equations ill-conditioned - the deviation there is about 0.33, two orders worse
        // than what it had already found. Keeping the best rather than the last is what stops that
        // curve being the answer.
        NurbsCurve largest = NurbsCurve.ApproximatePoints(points, points.Length - 1, 3);
        double largestDeviation = points.Max(p => largest.DistanceTo(p));

        Assert.True(
            deviation < largestDeviation,
            $"The search returned {deviation}; the largest count it tried gives {largestDeviation}.");
        Assert.Equal(points.Max(p => curve.DistanceTo(p)), deviation, 9);
    }

    [Fact]
    public void FewerThanThreePointsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => NurbsCurve.FitPoints(
            [new Point3d(0, 0, 0), new Point3d(1, 1, 0)], Loose));
    }

    private static NurbsCurve Sample(bool rational)
    {
        Point3d[] points = [new(0, 0, 0), new(1, 4, 1), new(4, 5, -1), new(7, 1, 2), new(9, 2, 0)];
        double[]? weights = rational ? [1.0, 2.5, 0.4, 1.8, 1.0] : null;

        return new NurbsCurve(points, KnotVector.CreateClamped(3, points.Length), weights);
    }

    private static Point3d[] Wave(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i =>
        {
            double t = 10.0 * i / (count - 1);
            return new Point3d(t, 3 * Math.Sin(t), Math.Cos(t / 2));
        }),
    ];

    private static Point3d[] NoisyLine(int count)
    {
        Random random = new(20260831);

        return
        [
            .. Enumerable.Range(0, count).Select(i => new Point3d(
                10.0 * i / (count - 1),
                random.NextDouble() - 0.5,
                random.NextDouble() - 0.5)),
        ];
    }
}

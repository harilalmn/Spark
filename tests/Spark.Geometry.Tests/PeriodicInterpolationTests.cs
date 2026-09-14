using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="NurbsCurve.InterpolatePointsPeriodic"/> — `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the wrap in the matrix, and it shows as points being missed rather than as a
/// rough seam.</b> Smoothness comes free: <see cref="NurbsCurve.FromPeriodicControlPoints"/> wraps
/// the ring over a uniform knot vector, so <i>any</i> ring of control points gives a curve that is
/// smooth at the seam. What the cyclic solve buys is that the curve goes <b>through</b> the points,
/// and the ones either side of the seam are the ones a wrong wrap misses.
/// </para>
/// <para>
/// <b>The test that says why the member exists is the contrast.</b> The clamped interpolation
/// through the same ring, with the first point repeated, gives a curve that closes and has a corner
/// where it closes. Same points, two answers.
/// </para>
/// </remarks>
public sealed class PeriodicInterpolationTests
{
    /// <summary>
    /// <b>The branch.</b> Every point is on the curve — including the two either side of the seam.
    /// </summary>
    /// <param name="count">How many points in the ring.</param>
    /// <param name="degree">The degree.</param>
    [Theory]
    [InlineData(8, 3)]
    [InlineData(5, 3)]
    [InlineData(12, 3)]
    [InlineData(7, 2)]
    [InlineData(9, 4)]
    public void EveryPointOfTheRingIsOnTheCurve(int count, int degree)
    {
        Point3d[] ring = Ring(count, 5.0);

        NurbsCurve curve = NurbsCurve.InterpolatePointsPeriodic(ring, degree);

        for (int index = 0; index < count; index++)
        {
            Point3d wanted = ring[index];
            Point3d got = curve.PointAt(curve.Domain.Min + index);

            Assert.True(
                got.DistanceTo(wanted) < 1e-9,
                $"point {index} of {count} is at {wanted} and the curve is at {got}.");
        }
    }

    /// <summary>A ring that is not a circle, so the solve is not flattered by symmetry.</summary>
    [Fact]
    public void AnIrregularRingIsInterpolatedToo()
    {
        Point3d[] ring =
        [
            new(0, 0, 0),
            new(10, 1, 0),
            new(13, 7, 0),
            new(6, 12, 0),
            new(-2, 9, 2),
            new(-5, 3, 1),
        ];

        NurbsCurve curve = NurbsCurve.InterpolatePointsPeriodic(ring);

        for (int index = 0; index < ring.Length; index++)
        {
            Assert.True(
                curve.PointAt(curve.Domain.Min + index).DistanceTo(ring[index]) < 1e-9,
                $"point {index} is missed.");
        }
    }

    /// <summary>
    /// The curve is smooth where it closes. <b>This is not the branch</b> — it comes free from the
    /// wrapped control points, and it is asserted here so that a later reader does not mistake it
    /// for proof that the solve is right.
    /// </summary>
    [Fact]
    public void TheSeamIsSmoothAndThatComesFree()
    {
        NurbsCurve curve = NurbsCurve.InterpolatePointsPeriodic(Ring(8, 5.0));

        Vector3d arriving = curve.TangentAt(curve.Domain.Max);
        Vector3d leaving = curve.TangentAt(curve.Domain.Min);

        Assert.True(
            (arriving - leaving).Length < 1e-9,
            $"the direction jumps from {arriving} to {leaving} at the seam.");

        // Second order too, as far as the curve contract reaches: CurvatureAt is a named M3
        // exclusion, so the Frenet normal is what says the curve bends the same way either side.
        Vector3d bendingBefore = curve.NormalAt(curve.Domain.Max);
        Vector3d bendingAfter = curve.NormalAt(curve.Domain.Min);

        Assert.True(
            (bendingBefore - bendingAfter).Length < 1e-9,
            $"it bends towards {bendingBefore} arriving and {bendingAfter} leaving.");
    }

    /// <summary>
    /// <b>Why the member exists.</b> The clamped interpolation through the same ring, with the
    /// first point repeated to close it, gives a curve with a <i>corner</i> where it closes.
    /// </summary>
    [Fact]
    public void TheClampedInterpolationThroughTheSameRingHasACornerWhereItCloses()
    {
        Point3d[] ring = Ring(8, 5.0);
        List<Point3d> repeated = [.. ring, ring[0]];

        NurbsCurve clamped = NurbsCurve.InterpolatePoints(repeated);
        NurbsCurve periodic = NurbsCurve.InterpolatePointsPeriodic(ring);

        Vector3d clampedArriving = clamped.TangentAt(clamped.Domain.Max);
        Vector3d clampedLeaving = clamped.TangentAt(clamped.Domain.Min);
        double clampedJump = (clampedArriving - clampedLeaving).Length;

        Vector3d periodicArriving = periodic.TangentAt(periodic.Domain.Max);
        Vector3d periodicLeaving = periodic.TangentAt(periodic.Domain.Min);
        double periodicJump = (periodicArriving - periodicLeaving).Length;

        Assert.True(
            clampedJump > 0.1,
            $"the clamped curve was expected to have a corner at the seam; its jump is {clampedJump}.");
        Assert.True(
            periodicJump < 1e-9,
            $"the periodic curve has a jump of {periodicJump} at the seam.");
    }

    /// <summary>
    /// <b>The anchor.</b> Points on a circle give a curve that stays on that circle, not merely one
    /// that touches it at the points — about a thousandth of the radius over eight points.
    /// </summary>
    /// <param name="count">How many points in the ring.</param>
    /// <param name="allowed">How far it may stray, as a fraction of the radius.</param>
    /// <remarks>
    /// <b>These ceilings are measured and rounded up, not derived.</b> The exact constant in a
    /// cubic spline's circle error is not worth pinning; the property that <i>is</i> worth pinning
    /// has its own test below. These are here to catch a change of the wrong order of magnitude,
    /// which is what an arithmetic slip looks like.
    /// </remarks>
    [Theory]
    [InlineData(8, 0.0015)]
    [InlineData(16, 0.0001)]
    public void PointsOnACircleGiveACurveThatStaysOnIt(int count, double allowed)
    {
        const double Radius = 5.0;

        double worst = StrayFromCircle(count, Radius);

        Assert.True(
            worst / Radius <= allowed,
            $"the curve strays {worst} from a circle of radius {Radius} through {count} points.");
    }

    /// <summary>
    /// <b>The error falls like the fourth power of the spacing</b>, which is what a cubic
    /// interpolant promises and is a better claim than any single tolerance: doubling the points
    /// cuts the deviation by about sixteen.
    /// </summary>
    [Fact]
    public void TheErrorFallsLikeTheFourthPowerOfTheSpacing()
    {
        double ratio = StrayFromCircle(8, 5.0) / StrayFromCircle(16, 5.0);

        // Bracketed loosely around sixteen, because the asymptotic constant has not settled at
        // these counts and a tight bracket would be pinning arithmetic noise rather than the order.
        Assert.True(
            ratio is > 8.0 and < 32.0,
            $"halving the spacing cut the error by {ratio}, which is not a fourth-order fall.");
    }

    /// <summary>More points is a closer fit, at every size.</summary>
    [Fact]
    public void MorePointsFitTheCircleMoreClosely()
    {
        Assert.True(StrayFromCircle(24, 4.0) < StrayFromCircle(12, 4.0));
        Assert.True(StrayFromCircle(12, 4.0) < StrayFromCircle(6, 4.0));
    }

    /// <summary>The result is periodic, so it reports itself closed.</summary>
    [Fact]
    public void TheCurveIsClosed()
    {
        NurbsCurve curve = NurbsCurve.InterpolatePointsPeriodic(Ring(9, 3.0));

        Assert.True(
            curve.StartPoint.DistanceTo(curve.EndPoint) < 1e-9,
            $"the curve starts at {curve.StartPoint} and ends at {curve.EndPoint}.");
    }

    [Fact]
    public void ItsDegeneraciesAreRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => NurbsCurve.InterpolatePointsPeriodic(null!));

        // A ring needs three points to be a ring.
        Assert.Throws<ArgumentException>(
            () => NurbsCurve.InterpolatePointsPeriodic([new Point3d(0, 0, 0), new Point3d(1, 0, 0)]));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NurbsCurve.InterpolatePointsPeriodic(Ring(5, 1.0), 0));

        // Degree must be less than the number of points in the ring.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NurbsCurve.InterpolatePointsPeriodic(Ring(4, 1.0), 4));
    }

    /// <summary>How far the interpolation of a circle's points strays from that circle.</summary>
    /// <param name="count">How many points.</param>
    /// <param name="radius">The circle's radius.</param>
    /// <returns>The worst deviation over a dense sampling.</returns>
    private static double StrayFromCircle(int count, double radius)
    {
        NurbsCurve curve = NurbsCurve.InterpolatePointsPeriodic(Ring(count, radius));

        double worst = 0.0;
        for (int sample = 0; sample <= 400; sample++)
        {
            Point3d point = curve.PointAt(curve.Domain.Denormalise(sample / 400.0));

            worst = Math.Max(worst, Math.Abs(point.DistanceTo(Point3d.Origin) - radius));
        }

        return worst;
    }

    /// <summary>Evenly spaced points around a circle in the world XY plane.</summary>
    /// <param name="count">How many.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The ring, given once — the first point is not repeated at the end.</returns>
    private static Point3d[] Ring(int count, double radius)
    {
        Point3d[] points = new Point3d[count];

        for (int index = 0; index < count; index++)
        {
            double angle = 2.0 * Math.PI * index / count;
            points[index] = new Point3d(radius * Math.Cos(angle), radius * Math.Sin(angle), 0.0);
        }

        return points;
    }
}

using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// `Plane.FromBestFit` and `Plane.FromLineAndPoint` — `E2-T40`, and the row closes with them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The interesting half of a fit is what it refuses.</b> Any implementation gets a clean
/// planar sample right; the ones that differ, differ on points that are nearly collinear, where
/// the normal is decided by rounding error and a plausible-looking plane comes back anyway. Spark
/// refuses those, because <see cref="Plane"/> is a type whose invalid state is not representable —
/// the opposite of <see cref="Point3d"/>, whose polar factories return an invalid value rather
/// than throwing for exactly the same reason read the other way round.
/// </para>
/// <para>
/// <b>What is deliberately not asserted is a normal's exact direction where the input does not
/// determine one.</b> A fitted normal is an eigenvector, and an eigenvector is equally valid
/// negated; pinning the sign that this arithmetic happens to produce would turn an implementation
/// detail into a promise. The sign <i>is</i> asserted where the factory promises it — the winding
/// of a ring — and compared against <c>FromThreePoints</c>, which makes the same promise.
/// </para>
/// </remarks>
public sealed class PlaneFitTests
{
    private static readonly Tolerance Tight = Tolerance.Default;

    /// <summary>
    /// Points sampled on a known plane fit that plane back, origin and normal both.
    /// </summary>
    [Fact]
    public void PointsOnAPlaneFitThatPlane()
    {
        Plane source = Plane.FromOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 2.0, 2.0));

        List<Point3d> samples = [];
        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                samples.Add(source.To3d(new Point2d(i - 2.0, j - 2.0)));
            }
        }

        Plane fitted = Plane.FromBestFit(samples);

        Assert.True(source.IsCoplanar(fitted, Tight));
        Assert.True(fitted.Origin.EqualsWithin(source.Origin, Tight));
        Assert.All(samples, point => Assert.True(fitted.Contains(point, Tight)));
    }

    /// <summary>
    /// <b>The point of a least-squares fit is the points that are not on the plane.</b> Noise
    /// perpendicular to the plane, balanced about it, must not move the answer beyond the noise.
    /// </summary>
    [Fact]
    public void NoisyPointsStillRecoverThePlaneTheyWereSampledFrom()
    {
        Plane source = Plane.FromOriginNormal(Point3d.Origin, Vector3d.ZAxis);

        List<Point3d> samples = [];
        double sign = 1.0;

        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                Point3d onPlane = source.To3d(new Point2d(i - 2.5, j - 2.5));
                samples.Add(onPlane + (source.Normal * (sign * 1e-3)));
                sign = -sign;
            }
        }

        Plane fitted = Plane.FromBestFit(samples);

        Assert.Equal(0.0, Math.Abs(fitted.Normal.Dot(Vector3d.ZAxis)) - 1.0, 6);
        Assert.Equal(0.0, fitted.Origin.Z, 9);
    }

    /// <summary>
    /// Handed exactly three points, the fit is the plane through them — the same plane
    /// <c>FromThreePoints</c> gives, normal and all, because both promise the right-hand rule for
    /// the order given.
    /// </summary>
    [Fact]
    public void ThreePointsFitTheirOwnPlaneWithTheSameNormal()
    {
        Point3d a = new(0.0, 0.0, 0.0);
        Point3d b = new(4.0, 0.0, 0.0);
        Point3d c = new(0.0, 3.0, 1.0);

        Plane exact = Plane.FromThreePoints(a, b, c);
        Plane fitted = Plane.FromBestFit([a, b, c]);

        Assert.True(exact.IsCoplanar(fitted, Tight));
        Assert.Equal(1.0, exact.Normal.Dot(fitted.Normal), 9);
    }

    /// <summary>
    /// <b>Reversing the ring reverses the normal</b>, which is what "follows the winding" means
    /// and is the only claim the factory makes about the sign.
    /// </summary>
    [Fact]
    public void ReversingTheOrderReversesTheNormal()
    {
        Point3d[] ring =
        [
            new(1.0, 0.0, 0.0),
            new(0.0, 1.0, 0.0),
            new(-1.0, 0.0, 0.0),
            new(0.0, -1.0, 0.0),
        ];

        Plane forward = Plane.FromBestFit(ring);
        Plane backward = Plane.FromBestFit([ring[3], ring[2], ring[1], ring[0]]);

        Assert.Equal(1.0, forward.Normal.Dot(Vector3d.ZAxis), 9);
        Assert.Equal(-1.0, backward.Normal.Dot(forward.Normal), 9);
    }

    /// <summary>
    /// <b>Collinear points are refused rather than fitted.</b> They lie in infinitely many planes,
    /// and the one an eigensolver would hand back is chosen by rounding error.
    /// </summary>
    [Fact]
    public void CollinearPointsAreRefused()
    {
        List<Point3d> line = [];
        for (int i = 0; i < 10; i++)
        {
            line.Add(new Point3d(i * 1.5, i * -0.5, i * 2.0));
        }

        ArgumentException error = Assert.Throws<ArgumentException>(() => Plane.FromBestFit(line));
        Assert.Equal("points", error.ParamName);
    }

    /// <summary>
    /// <b>And so are points that are only nearly collinear.</b> This is the case the degeneracy
    /// threshold exists for: a metre-long line with a micron of wobble is a line, and a plane
    /// fitted through it would spin freely about it.
    /// </summary>
    [Fact]
    public void NearlyCollinearPointsAreRefused()
    {
        List<Point3d> line = [];
        for (int i = 0; i < 20; i++)
        {
            line.Add(new Point3d(i * 0.05, (i % 2 == 0 ? 1e-10 : -1e-10), 0.0));
        }

        Assert.Throws<ArgumentException>(() => Plane.FromBestFit(line));
    }

    /// <summary>Coincident points span nothing at all.</summary>
    [Fact]
    public void CoincidentPointsAreRefused()
    {
        Point3d here = new(2.0, 2.0, 2.0);

        Assert.Throws<ArgumentException>(() => Plane.FromBestFit([here, here, here, here]));
    }

    /// <summary>Fewer than three points, a null list, and a non-finite point are all refused.</summary>
    [Fact]
    public void TooFewPointsAndBadPointsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Plane.FromBestFit(null!));
        Assert.Throws<ArgumentException>(() => Plane.FromBestFit([]));
        Assert.Throws<ArgumentException>(
            () => Plane.FromBestFit([Point3d.Origin, new Point3d(1.0, 0.0, 0.0)]));
        Assert.Throws<ArgumentException>(
            () => Plane.FromBestFit(
                [Point3d.Origin, new Point3d(1.0, 0.0, 0.0), Point3d.Unset, new Point3d(0.0, 1.0, 0.0)]));
    }

    /// <summary>
    /// <b>The fit means the same thing at every scale.</b> The degeneracy threshold is relative,
    /// so a plane a thousand times larger fits exactly as well and is not mistaken for a line.
    /// </summary>
    [Fact]
    public void TheFitIsScaleIndependent()
    {
        Point3d[] small = [new(0.0, 0.0, 0.0), new(1e-3, 0.0, 0.0), new(0.0, 1e-3, 0.0)];
        Point3d[] large = [new(0.0, 0.0, 0.0), new(1e6, 0.0, 0.0), new(0.0, 1e6, 0.0)];

        Assert.Equal(1.0, Plane.FromBestFit(small).Normal.Dot(Vector3d.ZAxis), 9);
        Assert.Equal(1.0, Plane.FromBestFit(large).Normal.Dot(Vector3d.ZAxis), 9);
    }

    /// <summary>A line and a point off it give the plane through all three positions.</summary>
    [Fact]
    public void ALineAndAPointGiveThePlaneThroughBoth()
    {
        Line line = new(new Point3d(0.0, 0.0, 0.0), new Point3d(2.0, 0.0, 0.0));
        Point3d off = new(0.0, 3.0, 0.0);

        Plane plane = Plane.FromLineAndPoint(line, off);

        Assert.True(plane.Contains(line.StartPoint, Tight));
        Assert.True(plane.Contains(line.EndPoint, Tight));
        Assert.True(plane.Contains(off, Tight));
        Assert.True(plane.IsCoplanar(
            Plane.FromThreePoints(line.StartPoint, line.EndPoint, off), Tight));
        Assert.Equal(1.0, plane.XAxis.Dot(line.Direction), 12);
    }

    /// <summary>
    /// <b>A point on the line is refused, and the exception names <c>point</c>.</b> The refusal is
    /// the interesting half — a line and a point on it lie in infinitely many planes — and the
    /// parameter name is the other half: forwarding to <c>FromThreePoints</c> unchanged would
    /// report <c>third</c>, which is a parameter no caller of this method has ever seen.
    /// </summary>
    [Fact]
    public void APointOnTheLineIsRefusedAndTheExceptionNamesThePoint()
    {
        Line line = new(new Point3d(0.0, 0.0, 0.0), new Point3d(2.0, 0.0, 0.0));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Plane.FromLineAndPoint(line, new Point3d(1.0, 0.0, 0.0)));

        Assert.Equal("point", error.ParamName);
        Assert.NotNull(error.InnerException);

        Assert.Throws<ArgumentException>(
            () => Plane.FromLineAndPoint(line, new Point3d(5.0, 0.0, 0.0)));
        Assert.Throws<ArgumentNullException>(
            () => Plane.FromLineAndPoint(null!, new Point3d(0.0, 1.0, 0.0)));
    }

    /// <summary>Both constructors are the factories, not a second implementation of them.</summary>
    [Fact]
    public void TheConstructorsAgreeWithTheFactoriesTheyForwardTo()
    {
        Point3d[] ring = [new(1.0, 0.0, 0.5), new(0.0, 1.0, 0.5), new(-1.0, 0.0, 0.5)];
        Line line = new(new Point3d(0.0, 0.0, 0.0), new Point3d(0.0, 0.0, 4.0));
        Point3d off = new(1.0, 1.0, 1.0);

        Assert.Equal(Plane.FromBestFit(ring), new Plane(ring));
        Assert.Equal(Plane.FromLineAndPoint(line, off), new Plane(line, off));
    }
}

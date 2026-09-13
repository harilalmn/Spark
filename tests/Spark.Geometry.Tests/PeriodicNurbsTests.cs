using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Periodic NURBS curves — `E2-T72`'s third idea, that a NURBS curve could not close smoothly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test here exists to separate <i>closed</i> from <i>periodic</i>, because that is the
/// distinction the feature is.</b> A closed curve's ends meet. A periodic curve's ends meet
/// <b>smoothly</b> — the derivatives agree at the seam, to the curve's degree. A clamped curve
/// whose last control point sits on its first is closed and is <i>not</i> periodic, and it is in
/// this file as the control: it passes every test about positions and fails every test about
/// derivatives. Reporting one as the other is [N156](../../docs/NOTES.md)'s trap, which
/// <c>Surface.IsPeriodicInU</c> and <c>Face.SurfaceGeometry</c> have each already walked into.
/// </para>
/// <para>
/// <b>So an assertion that the two ends coincide is never the claim.</b> It is necessary, it is
/// satisfied by the thing being distinguished from, and on its own it proves nothing.
/// </para>
/// </remarks>
public sealed class PeriodicNurbsTests
{
    /// <summary>The control points of a regular polygon, given once and not wrapped.</summary>
    /// <param name="sides">How many.</param>
    /// <param name="radius">The circumradius.</param>
    /// <returns>The ring.</returns>
    private static List<Point3d> Ring(int sides, double radius = 1.0)
    {
        List<Point3d> points = [];

        for (int index = 0; index < sides; index++)
        {
            double angle = index * Math.PI * 2.0 / sides;
            points.Add(new Point3d(radius * Math.Cos(angle), radius * Math.Sin(angle), 0.0));
        }

        return points;
    }

    /// <summary>The same ring as a clamped curve with the first point repeated at the end.</summary>
    /// <param name="sides">How many.</param>
    /// <returns>A curve that is closed and is not periodic.</returns>
    private static NurbsCurve ClampedAndClosed(int sides)
    {
        List<Point3d> points = Ring(sides);
        points.Add(points[0]);

        return new NurbsCurve(points.Count - 1, points, KnotVector.CreateClamped(points.Count - 1, points.Count).ToArray());
    }

    [Fact]
    public void APeriodicCurveIsPeriodicAndAClampedClosedOneIsNot()
    {
        NurbsCurve periodic = NurbsCurve.FromPeriodicControlPoints(Ring(6));
        NurbsCurve closed = ClampedAndClosed(6);

        Assert.True(periodic.IsPeriodic);
        Assert.True(periodic.IsClosed);

        // The control, and the whole reason the two members are not one: this curve's ends meet
        // and it is not periodic.
        Assert.True(closed.IsClosed);
        Assert.False(closed.IsPeriodic);
    }

    [Fact]
    public void TheDerivativesAgreeAtTheSeamAndOnTheClampedCurveTheyDoNot()
    {
        // `E2-T72`: THE TEST THAT IS THE FEATURE. Positions meeting is the easy half and both
        // curves pass it; what separates them is whether the curve is SMOOTH where it closes.
        NurbsCurve periodic = NurbsCurve.FromPeriodicControlPoints(Ring(6));
        NurbsCurve closed = ClampedAndClosed(6);

        foreach (NurbsCurve curve in (NurbsCurve[])[periodic, closed])
        {
            Assert.Equal(
                0.0, curve.PointAt(curve.Domain.Min).DistanceTo(curve.PointAt(curve.Domain.Max)), 1e-9);
        }

        Vector3d inAtEnd = periodic.TangentAt(periodic.Domain.Max);
        Vector3d outAtStart = periodic.TangentAt(periodic.Domain.Min);

        Assert.Equal(1.0, inAtEnd.Dot(outAtStart), 1e-9);

        // And the second derivative too, which is what "to the degree" means for a cubic — a curve
        // can be tangent-continuous at a seam and still show a curvature break.
        Vector3d secondIn = periodic.SecondDerivativeAt(periodic.Domain.Max);
        Vector3d secondOut = periodic.SecondDerivativeAt(periodic.Domain.Min);

        Assert.Equal(0.0, (secondIn - secondOut).Length, 1e-8);

        // The clamped curve meets itself at a corner. Its two end tangents are nothing like
        // parallel, which is exactly the kink IsPeriodic exists to deny.
        Vector3d clampedIn = closed.TangentAt(closed.Domain.Max);
        Vector3d clampedOut = closed.TangentAt(closed.Domain.Min);

        Assert.True(
            clampedIn.Dot(clampedOut) < 0.9,
            $"the clamped closed curve was smooth at its seam ({clampedIn.Dot(clampedOut)}), "
            + "which would mean this test cannot tell the two apart.");
    }

    [Fact]
    public void APeriodicCurveOverARegularPolygonIsExactlySymmetric()
    {
        // The anchor with a known answer. A uniform periodic curve through a regular polygon's
        // vertices inherits the polygon's symmetry exactly: every span is congruent to every
        // other, so points an equal number of spans apart lie at an equal distance from the centre
        // and the curve is invariant under a rotation of one span. A clamped vector cannot produce
        // that, because its end spans are not like its middle ones.
        const int sides = 8;
        NurbsCurve curve = NurbsCurve.FromPeriodicControlPoints(Ring(sides));

        double span = curve.Domain.Length / sides;
        double first = curve.PointAt(curve.Domain.Min).DistanceTo(Point3d.Origin);

        for (int index = 1; index < sides; index++)
        {
            double here = curve.PointAt(curve.Domain.Min + (index * span)).DistanceTo(Point3d.Origin);

            Assert.Equal(first, here, 1e-12);
        }

        // And at the midpoints of the spans, which is a second orbit at a different radius.
        double firstMid = curve.PointAt(curve.Domain.Min + (span * 0.5)).DistanceTo(Point3d.Origin);

        for (int index = 1; index < sides; index++)
        {
            double here = curve
                .PointAt(curve.Domain.Min + ((index + 0.5) * span))
                .DistanceTo(Point3d.Origin);

            Assert.Equal(firstMid, here, 1e-12);
        }

        Assert.NotEqual(first, firstMid, 9);
    }

    [Fact]
    public void APeriodicCurveDoesNotPassThroughItsControlPoints()
    {
        // Stated as a test because it is the thing that surprises people, and because a caller
        // wanting the points ON the curve should be sent to InterpolatePoints instead.
        List<Point3d> ring = Ring(5, 2.0);
        NurbsCurve curve = NurbsCurve.FromPeriodicControlPoints(ring);

        foreach (Point3d point in ring)
        {
            Assert.True(
                curve.DistanceTo(point) > 1e-6,
                $"{point} was on the curve, which a periodic uniform curve's control points are not.");
        }

        // It stays inside the control polygon's circumcircle, which is the other half of the same
        // fact and is what makes the shape predictable.
        for (int index = 0; index <= 64; index++)
        {
            Point3d on = curve.PointAt(curve.Domain.Denormalise(index / 64.0));

            Assert.True(on.DistanceTo(Point3d.Origin) < 2.0);
        }
    }

    [Fact]
    public void TheWrapIsTheFactorysJobAndTheRingIsGivenOnce()
    {
        // Six points in, and the curve carries nine control points for a cubic: the ring plus the
        // three wrapped. A caller who repeated the first point themselves would get a doubled
        // control point, which is why the contract says "given once" out loud.
        NurbsCurve curve = NurbsCurve.FromPeriodicControlPoints(Ring(6));

        Assert.Equal(9, curve.ControlPoints().Length);
        Assert.Equal(3, curve.Degree);
        Assert.False(curve.Knots.IsClamped);
        Assert.True(curve.Knots.IsUniform);
    }

    [Fact]
    public void AUniformKnotVectorsDomainIsNotZeroToOne()
    {
        // And that is deliberate. Rescaling to [0, 1] would leave the spans equal in width but the
        // DOMAIN starting at the degree-th knot either way — what matters is that every span has
        // the same width, which is the property the symmetry test above rests on.
        KnotVector uniform = KnotVector.CreateUniform(3, 9);

        Assert.False(uniform.IsClamped);
        Assert.True(uniform.IsUniform);
        Assert.Equal(3.0, uniform.Domain.Min, 1e-12);
        Assert.Equal(9.0, uniform.Domain.Max, 1e-12);
        Assert.Equal(9, uniform.ControlPointCount);
    }

    [Fact]
    public void AClampedVectorIsNotUniformAndAUniformOneIsNotClamped()
    {
        KnotVector clamped = KnotVector.CreateClamped(3, 9);
        KnotVector uniform = KnotVector.CreateUniform(3, 9);

        Assert.True(clamped.IsClamped);
        Assert.False(clamped.IsUniform);
        Assert.True(uniform.IsUniform);
        Assert.False(uniform.IsClamped);
    }

    [Fact]
    public void ARationalPeriodicCurveWrapsItsWeightsTogetherWithItsPoints()
    {
        // A weight left behind by the wrap would put a curve's seam at a different weight from the
        // point it shares, which is a kink in the rational part rather than in the polygon and is
        // correspondingly harder to see.
        List<Point3d> ring = Ring(5);
        List<double> weights = [1.0, 2.0, 0.5, 1.5, 3.0];

        NurbsCurve curve = NurbsCurve.FromPeriodicControlPoints(ring, 3, weights);

        Assert.True(curve.IsPeriodic);
        Assert.True(curve.IsRational);

        double[] wrapped = curve.Weights();

        Assert.Equal(8, wrapped.Length);
        Assert.Equal(wrapped[0], wrapped[5]);
        Assert.Equal(wrapped[1], wrapped[6]);
        Assert.Equal(wrapped[2], wrapped[7]);

        Vector3d inAtEnd = curve.TangentAt(curve.Domain.Max);
        Vector3d outAtStart = curve.TangentAt(curve.Domain.Min);

        Assert.Equal(1.0, inAtEnd.Dot(outAtStart), 1e-9);
    }

    [Fact]
    public void TooFewControlPointsForTheDegreeIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => NurbsCurve.FromPeriodicControlPoints(Ring(3), 3));

        Assert.Equal("controlPoints", error.ParamName);
    }

    [Fact]
    public void MismatchedWeightsAreRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => NurbsCurve.FromPeriodicControlPoints(Ring(5), 3, [1.0, 1.0]));

        Assert.Equal("weights", error.ParamName);
    }

    [Fact]
    public void TheConstructorAndTheFactoryAgree()
    {
        List<Point3d> ring = Ring(7);

        NurbsCurve built = new(ring, 3);
        NurbsCurve made = NurbsCurve.FromPeriodicControlPoints(ring, 3);

        Assert.True(built.IsPeriodic);
        Assert.Equal(made.Length, built.Length, 1e-12);
        Assert.Equal(made.ControlPoints().Length, built.ControlPoints().Length);
    }

    [Fact]
    public void APeriodicCurveTessellatesWithoutAKinkAtTheSeam()
    {
        // The consumer's view of the same fact: a tessellation of a smooth closed curve turns by a
        // small amount at every vertex, INCLUDING the one where the two ends meet. A kink there is
        // what a user would actually see.
        NurbsCurve curve = NurbsCurve.FromPeriodicControlPoints(Ring(6, 5.0));
        Point3d[] points = curve.Tessellate(new Tolerance(1e-3, Angle.FromDegrees(0.001), 1e-12));

        double worst = 0.0;

        for (int index = 1; index < points.Length - 1; index++)
        {
            Vector3d before = (points[index] - points[index - 1]).Normalised();
            Vector3d after = (points[index + 1] - points[index]).Normalised();

            worst = Math.Max(worst, before.AngleTo(after).Degrees);
        }

        // The seam, which is the join between the last chord and the first.
        Vector3d last = (points[^1] - points[^2]).Normalised();
        Vector3d first = (points[1] - points[0]).Normalised();
        double atSeam = last.AngleTo(first).Degrees;

        Assert.True(
            atSeam <= worst + 1e-6,
            $"the seam turned {atSeam} degrees where the worst interior vertex turned {worst}.");
    }
}

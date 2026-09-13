using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="CurveOffset.Fillet"/> — the general two-curve fillet, `E2-T72`'s second idea.
/// </summary>
/// <remarks>
/// <para>
/// <b>A fillet is a promise about tangency, and that is what these tests assert.</b> An arc that
/// meets both curves at the right <i>points</i> and is not tangent to them there is the failure
/// that looks right in a screenshot and is wrong in a machined part — so the assertion is that the
/// arc's tangent at each end is parallel to the curve's tangent there, to near machine precision
/// rather than to a drawing tolerance. Meeting the points is the easy half and is not the claim.
/// </para>
/// <para>
/// <b><see cref="CurveOffset.FilletLines"/> is the oracle for the case they share.</b> It is a
/// closed form this repository already contained and did not write for this purpose, which makes
/// agreement with it evidence rather than a restatement — the same shape of anchor `E2-T70` step B
/// used when it checked a general intersector against a line against a plane.
/// </para>
/// </remarks>
public sealed class CurveFilletTests
{
    private static Tolerance At(double linear) => new(linear, Angle.FromDegrees(0.001), 1e-12);

    /// <summary>Asserts that an arc end is tangent to a curve at the point they share.</summary>
    /// <param name="arc">The fillet.</param>
    /// <param name="curve">The curve it should touch.</param>
    /// <param name="atStart">Whether to check the arc's start or its end.</param>
    private static void AssertTangent(Arc arc, Curve curve, bool atStart)
    {
        double parameter = atStart ? arc.Domain.Min : arc.Domain.Max;
        Point3d touch = arc.PointAt(parameter);

        Assert.True(
            curve.DistanceTo(touch) < 1e-9,
            $"the fillet's {(atStart ? "start" : "end")} was {curve.DistanceTo(touch)} off the curve.");

        Vector3d alongArc = arc.TangentAt(parameter);
        Vector3d alongCurve = curve.TangentAt(curve.ClosestParameter(touch));

        Assert.Equal(1.0, Math.Abs(alongArc.Dot(alongCurve)), 1e-9);
    }

    [Fact]
    public void TheGeneralFilletAgreesWithTheClosedFormOnTwoLines()
    {
        // The oracle. FilletLines solves this by bisecting the angle and stepping in by
        // radius / tan(half); the general method never computes an angle at all. Two derivations,
        // one answer.
        Line first = new(new Point3d(-4.0, 0.0, 0.0), Point3d.Origin);
        Line second = new(Point3d.Origin, new Point3d(0.0, 4.0, 0.0));

        (Arc closedForm, _, _) = CurveOffset.FilletLines(first, second, 1.5);
        (Arc general, _, _) = CurveOffset.Fillet(first, second, 1.5, Vector3d.ZAxis, At(1e-9));

        Assert.Equal(closedForm.Radius, general.Radius, 1e-9);
        Assert.Equal(0.0, closedForm.Center.DistanceTo(general.Center), 1e-9);
        Assert.Equal(0.0, closedForm.StartPoint.DistanceTo(general.StartPoint), 1e-9);
        Assert.Equal(0.0, closedForm.EndPoint.DistanceTo(general.EndPoint), 1e-9);
    }

    [Fact]
    public void AFilletBetweenTwoLinesIsTangentToBoth()
    {
        Line first = new(new Point3d(-5.0, 0.0, 0.0), Point3d.Origin);
        Line second = new(Point3d.Origin, new Point3d(3.0, 3.0, 0.0));

        (Arc fillet, Curve trimmedFirst, Curve trimmedSecond) =
            CurveOffset.Fillet(first, second, 1.0, Vector3d.ZAxis, At(1e-9));

        Assert.Equal(1.0, fillet.Radius, 1e-9);
        AssertTangent(fillet, first, atStart: true);
        AssertTangent(fillet, second, atStart: false);

        // And the curves come back trimmed, because a fillet that leaves the corner in place is
        // not what anybody asked for.
        Assert.True(trimmedFirst.Length < first.Length);
        Assert.True(trimmedSecond.Length < second.Length);
        Assert.Equal(0.0, trimmedFirst.EndPoint.DistanceTo(fillet.StartPoint), 1e-8);
    }

    [Fact]
    public void AFilletBetweenAnArcAndALineIsTangentToBoth()
    {
        // The case the closed form cannot do, and the reason the general method exists.
        // A quarter circle and a line crossing it at forty-five degrees. The first draft of this
        // test put the line at x = -5, which TOUCHES the circle at (-5, 0) rather than crossing it
        // — a tangent pair has no corner, and the fillet rightly could not be built.
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 5.0, Angle.Zero, Angle.FromDegrees(90.0));
        Line line = new(new Point3d(1.0, 1.0, 0.0), new Point3d(8.0, 8.0, 0.0));

        (Arc fillet, _, _) = CurveOffset.Fillet(arc, line, 0.8, Vector3d.ZAxis, At(1e-7));

        Assert.Equal(0.8, fillet.Radius, 1e-7);
        AssertTangent(fillet, arc, atStart: true);
        AssertTangent(fillet, line, atStart: false);
    }

    [Fact]
    public void AFilletBetweenTwoArcsIsTangentToBoth()
    {
        // Two arcs crossing: neither side of the corner is straight, so the fillet centre is the
        // intersection of two curved offsets and nothing about it is closed form.
        Arc first = Arc.FromThreePoints(
            new Point3d(-6.0, 0.0, 0.0), new Point3d(-3.0, 2.0, 0.0), new Point3d(0.0, 0.0, 0.0));
        Arc second = Arc.FromThreePoints(
            new Point3d(-1.0, -3.0, 0.0), new Point3d(-1.5, 0.5, 0.0), new Point3d(-4.0, 3.0, 0.0));

        (Arc fillet, _, _) = CurveOffset.Fillet(first, second, 0.5, Vector3d.ZAxis, At(1e-7));

        Assert.Equal(0.5, fillet.Radius, 1e-6);
        AssertTangent(fillet, first, atStart: true);
        AssertTangent(fillet, second, atStart: false);
    }

    [Fact]
    public void TheCentreIsRefinedPastTheAccuracyOfTheOffsetItWasSeededFrom()
    {
        // `E2-T72`: the branch this proves is the Newton refinement — and the curve here is a
        // NURBS curve ON PURPOSE. CurveOffset.Offset has exact branches for Line, Circle and Arc,
        // so a fillet between any two of those is seeded exactly and the refinement is a no-op:
        // the first draft of this test used an arc and a line and passed with the refinement
        // deleted, which proved nothing at all. A NURBS curve falls to FitOffset, which samples the
        // offset locus and fits a curve through it — so the seed is accurate to the TOLERANCE and
        // no better, and only the refinement makes the fillet tangent.
        NurbsCurve curved = new(
            3,
            [
                new Point3d(0.0, 0.0, 0.0),
                new Point3d(2.0, 3.0, 0.0),
                new Point3d(5.0, 3.0, 0.0),
                new Point3d(7.0, 0.0, 0.0),
            ],
            [0, 0, 0, 0, 1, 1, 1, 1]);

        Line line = new(new Point3d(3.5, -1.0, 0.0), new Point3d(3.5, 4.0, 0.0));

        // A coarse tolerance and a fine one, a thousand-fold apart, so the SEEDS differ visibly.
        (Arc coarse, _, _) = CurveOffset.Fillet(curved, line, 0.4, Vector3d.ZAxis, At(1e-3));
        (Arc fine, _, _) = CurveOffset.Fillet(curved, line, 0.4, Vector3d.ZAxis, At(1e-6));

        Assert.Equal(0.0, coarse.Center.DistanceTo(fine.Center), 1e-7);
        Assert.Equal(0.4, coarse.Radius, 1e-9);

        // And the tangency, which is what the refinement buys and what a fit's-accuracy seed
        // cannot deliver.
        AssertTangent(coarse, curved, atStart: true);
        AssertTangent(coarse, line, atStart: false);
    }

    [Fact]
    public void ARadiusTooLargeForTheCornerIsRefusedRatherThanOvershooting()
    {
        // There is no fillet of radius ten in a corner two across, and the offsets never meet —
        // which is the natural place to notice it. An arc that overshot both curves would be a
        // worse answer than a message.
        Line first = new(new Point3d(-2.0, 0.0, 0.0), Point3d.Origin);
        Line second = new(Point3d.Origin, new Point3d(0.0, 2.0, 0.0));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CurveOffset.Fillet(first, second, 10.0, Vector3d.ZAxis, At(1e-9)));

        Assert.Equal("radius", error.ParamName);
    }

    [Fact]
    public void CurvesThatDoNotCrossHaveNoCornerToFillet()
    {
        Line first = new(new Point3d(-5.0, 0.0, 0.0), new Point3d(-3.0, 0.0, 0.0));
        Line second = new(new Point3d(0.0, 2.0, 0.0), new Point3d(0.0, 5.0, 0.0));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CurveOffset.Fillet(first, second, 0.5, Vector3d.ZAxis, At(1e-9)));

        Assert.Equal("second", error.ParamName);
        Assert.Contains("do not cross", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonPositiveRadiusIsRefused()
    {
        Line first = new(new Point3d(-2.0, 0.0, 0.0), Point3d.Origin);
        Line second = new(Point3d.Origin, new Point3d(0.0, 2.0, 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurveOffset.Fillet(first, second, 0.0, Vector3d.ZAxis));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurveOffset.Fillet(first, second, -1.0, Vector3d.ZAxis));
    }

    [Fact]
    public void ThePlaneNormalIsAskedForRatherThanInferred()
    {
        // Two straight lines have no plane of their own to read — Curve.PlaneOf returns null for
        // each of them — which is exactly why the normal is a parameter. A zero one is refused
        // rather than guessed at.
        Line first = new(new Point3d(-2.0, 0.0, 0.0), Point3d.Origin);
        Line second = new(Point3d.Origin, new Point3d(0.0, 2.0, 0.0));

        Assert.Null(first.PlaneOf());

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CurveOffset.Fillet(first, second, 0.5, Vector3d.Zero));

        Assert.Equal("normal", error.ParamName);
    }

    [Fact]
    public void TheFilletRoundsThisCornerAndNotOneOfItsReflections()
    {
        // Four points sit at the fillet radius from both curves, one per combination of sides.
        // Only one of them rounds off the corner the caller can see, and the rule is that the
        // nearest to the crossing wins. A fillet centred on one of the other three is a perfectly
        // good arc tangent to both lines, somewhere else entirely — which is what makes this worth
        // a test rather than a comment.
        Line first = new(new Point3d(-4.0, 0.0, 0.0), new Point3d(4.0, 0.0, 0.0));
        Line second = new(new Point3d(0.0, -4.0, 0.0), new Point3d(0.0, 4.0, 0.0));

        (Arc fillet, _, _) = CurveOffset.Fillet(first, second, 1.0, Vector3d.ZAxis, At(1e-9));

        // The centre is one of (±1, ±1) — any of the four is tangent to both lines — and it must be
        // exactly a fillet radius' diagonal from the crossing at the origin.
        Assert.Equal(Math.Sqrt(2.0), fillet.Center.DistanceTo(Point3d.Origin), 1e-8);
        AssertTangent(fillet, first, atStart: true);
        AssertTangent(fillet, second, atStart: false);
    }
}

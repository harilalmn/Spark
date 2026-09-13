using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Curve.Extended(double, double)"/> — `E2-T71` family (4), the operation
/// <see cref="Curve.Trimmed(in Interval)"/> is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch that hides is <i>which end</i>.</b> The two distances differ only in a sign, which
/// is the classic place for a bug that is invisible on a symmetric curve — so the curves here are
/// deliberately asymmetric, and every test that extends one end asserts that the <b>other end did
/// not move</b>.
/// </para>
/// <para>
/// <b>And continuity at the join is the claim, not position.</b> An extension whose pieces meet at
/// the right point and turn a corner there is the failure that looks right in a picture, which is
/// the same distinction <see cref="NurbsCurve.IsPeriodic"/> turned on.
/// </para>
/// </remarks>
public sealed class CurveExtensionTests
{
    private const double Tight = 1e-9;

    [Fact]
    public void ALineBecomesALongerLineAtWhicheverEndWasAsked()
    {
        Line line = new(new Point3d(1.0, 0.0, 0.0), new Point3d(5.0, 0.0, 0.0));

        Curve atEnd = line.Extended(0.0, 2.0);
        Curve atStart = line.Extended(3.0, 0.0);

        Assert.IsType<Line>(atEnd);
        Assert.Equal(6.0, atEnd.Length, Tight);
        Assert.Equal(7.0, atEnd.EndPoint.X, Tight);

        // The end that was not asked for did not move, which is the whole of the sign bug this
        // test exists to catch.
        Assert.Equal(1.0, atEnd.StartPoint.X, Tight);

        Assert.Equal(-2.0, atStart.StartPoint.X, Tight);
        Assert.Equal(5.0, atStart.EndPoint.X, Tight);
    }

    [Fact]
    public void AnArcBecomesAWiderArcAndTheSweepIsTheLengthOverTheRadius()
    {
        // An arc travels at a constant speed of its radius, so the closed form is exact and is not
        // the implementation restated: a length of L adds L / r of sweep.
        const double radius = 4.0;

        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, radius, Angle.FromDegrees(30.0), Angle.FromDegrees(60.0));

        Arc longer = Assert.IsType<Arc>(arc.Extended(0.0, 2.0));

        Assert.Equal(radius, longer.Radius, Tight);
        Assert.Equal(arc.SweepAngle.Radians + (2.0 / radius), longer.SweepAngle.Radians, Tight);
        Assert.Equal(arc.Length + 2.0, longer.Length, Tight);

        // Same start, and the start angle untouched.
        Assert.Equal(0.0, longer.StartPoint.DistanceTo(arc.StartPoint), Tight);
        Assert.Equal(arc.StartAngle.Radians, longer.StartAngle.Radians, Tight);
    }

    [Fact]
    public void AnExtendedArcAgreesWithTheOriginalWhereTheyOverlap()
    {
        // The extension is the same curve on a wider interval, so restricted to the original's
        // reach it IS the original — asserted to the last few bits rather than to a tolerance,
        // because a wider sweep is the same evaluation and nothing has been approximated.
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldYZ, 3.0, Angle.FromDegrees(10.0), Angle.FromDegrees(100.0));

        Arc longer = Assert.IsType<Arc>(arc.Extended(1.0, 1.0));

        for (int index = 0; index <= 32; index++)
        {
            double along = arc.Length * index / 32.0;

            Assert.Equal(0.0, arc.PointAtLength(along).DistanceTo(longer.PointAtLength(along + 1.0)), 1e-12);
        }
    }

    [Fact]
    public void AnArcExtendedPastAFullTurnStopsAtAFullTurn()
    {
        // An arc that has come all the way round is a circle; a sweep past 2π is a curve through
        // its own points twice, which FromPlaneRadiusAngles refuses and this must not smuggle past.
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 1.0, Angle.Zero, Angle.FromDegrees(350.0));

        Arc longer = Assert.IsType<Arc>(arc.Extended(1.0, 1.0));

        Assert.Equal(Math.PI * 2.0, longer.SweepAngle.Radians, 1e-9);
    }

    [Fact]
    public void AHelixBecomesMoreTurns()
    {
        Helix helix = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FullTurn);

        Helix longer = Assert.IsType<Helix>(helix.Extended(0.0, helix.Length));

        // Twice the length is twice the sweep, because the speed is constant — and unlike an arc
        // there is no full turn to clamp against.
        Assert.Equal(helix.Length * 2.0, longer.Length, 1e-9);
        Assert.Equal(helix.SweepAngle.Radians * 2.0, longer.SweepAngle.Radians, 1e-9);
        Assert.Equal(helix.Radius, longer.Radius, Tight);
        Assert.Equal(helix.Pitch, longer.Pitch, Tight);
        Assert.Equal(0.0, longer.StartPoint.DistanceTo(helix.StartPoint), Tight);
    }

    [Fact]
    public void AHelixExtendedAtTheStartKeepsItsOtherEndWhereItWas()
    {
        Helix helix = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FullTurn);

        Helix longer = Assert.IsType<Helix>(helix.Extended(5.0, 0.0));

        Assert.Equal(helix.Length + 5.0, longer.Length, 1e-9);
        Assert.Equal(0.0, longer.EndPoint.DistanceTo(helix.EndPoint), 1e-8);
        Assert.True(
            longer.StartPoint.DistanceTo(helix.StartPoint) > 1.0,
            "the start did not move, so nothing was extended there.");
    }

    [Fact]
    public void ACurveWithNoContinuationGetsAStraightTailThatIsTangentAtTheJoin()
    {
        // A NURBS curve takes the base implementation. The claim is not that the pieces meet - of
        // course they meet - it is that the curve does not turn a corner where they do.
        NurbsCurve curve = new(
            3,
            [
                new Point3d(0.0, 0.0, 0.0),
                new Point3d(1.0, 3.0, 0.0),
                new Point3d(4.0, 3.0, 0.0),
                new Point3d(6.0, 0.0, 0.0),
            ],
            [0, 0, 0, 0, 1, 1, 1, 1]);

        PolyCurve longer = Assert.IsType<PolyCurve>(curve.Extended(1.5, 2.5));

        Assert.Equal(curve.Length + 4.0, longer.Length, 1e-7);
        Assert.Equal(3, longer.SegmentCount);

        // Tangent-continuous at both joins, which is what "extended along its own end tangent"
        // has to mean.
        Vector3d intoStart = longer.TangentAt(longer.ClosestParameter(curve.StartPoint));
        Assert.Equal(1.0, intoStart.Dot(curve.TangentAt(curve.Domain.Min)), 1e-7);

        Vector3d outOfEnd = longer.TangentAt(longer.ClosestParameter(curve.EndPoint));
        Assert.Equal(1.0, outOfEnd.Dot(curve.TangentAt(curve.Domain.Max)), 1e-7);

        // And the original is still in there, untouched.
        for (int index = 0; index <= 16; index++)
        {
            Point3d on = curve.PointAt(curve.Domain.Denormalise(index / 16.0));

            Assert.True(longer.DistanceTo(on) < 1e-9, $"{on} left the extended curve.");
        }
    }

    [Fact]
    public void AnEllipseTakesTheStraightTailAndThatIsDeliberate()
    {
        // Its equation continues perfectly well; its SPEED is not constant, so turning a requested
        // arc length into a sweep means integrating outside the domain its own arc-length machinery
        // is built for. The linear tail is the honest answer, and the type says so.
        EllipseCurve ellipse = EllipseCurve.FromPlaneRadiiAngles(
            Plane.WorldXY, 5.0, 2.0, Angle.Zero, Angle.FromDegrees(120.0));

        Curve longer = ellipse.Extended(0.0, 1.0);

        Assert.IsType<PolyCurve>(longer);
        Assert.Equal(ellipse.Length + 1.0, longer.Length, 1e-7);
    }

    [Fact]
    public void AClosedCurveHasNoEndsToExtend()
    {
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 2.0);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => circle.Extended(0.0, 1.0));

        Assert.Contains("no ends", error.Message, StringComparison.Ordinal);

        // But extending it by nothing is not an error, because nothing happened.
        Assert.Same(circle, circle.Extended(0.0, 0.0));
    }

    [Fact]
    public void ExtendingByNothingReturnsTheCurveItself()
    {
        Arc arc = Arc.FromPlaneRadiusAngles(Plane.WorldXY, 2.0, Angle.Zero, Angle.FromDegrees(90.0));
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Assert.Equal(arc.Length, arc.Extended(0.0, 0.0).Length, Tight);
        Assert.Equal(line.Length, line.Extended(0.0, 0.0).Length, Tight);
    }

    [Fact]
    public void ANegativeExtensionIsRefusedRatherThanTrimming()
    {
        // Extending by a negative amount is trimming, and a member that quietly did the opposite of
        // its name would be worse than one that refuses.
        Line line = new(Point3d.Origin, new Point3d(4.0, 0.0, 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => line.Extended(-1.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Extended(0.0, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Extended(double.NaN, 0.0));
    }

    [Fact]
    public void ExtendingBothEndsIsTheTwoSingleEndedExtensionsTogether()
    {
        // Dynamo's ExtendStart and ExtendEnd are this member with a zero, and the three have to
        // agree or the composition the register claims is not one.
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 3.0, Angle.FromDegrees(20.0), Angle.FromDegrees(80.0));

        Curve both = arc.Extended(1.0, 2.0);
        Curve stepwise = ((Arc)arc.Extended(1.0, 0.0)).Extended(0.0, 2.0);

        Assert.Equal(both.Length, stepwise.Length, 1e-9);
        Assert.Equal(0.0, both.StartPoint.DistanceTo(stepwise.StartPoint), 1e-9);
        Assert.Equal(0.0, both.EndPoint.DistanceTo(stepwise.EndPoint), 1e-9);
    }

    [Fact]
    public void TwoCurvesThatNearlyMeetCanBeMadeToMeetAndThenFilleted()
    {
        // The reason the family exists. CurveOffset.Fillet refuses a pair that does not already
        // cross, and until now nothing in Spark could make them cross.
        Line first = new(new Point3d(-4.0, 0.0, 0.0), new Point3d(-1.0, 0.0, 0.0));
        Line second = new(new Point3d(0.0, 1.0, 0.0), new Point3d(0.0, 4.0, 0.0));

        Assert.Throws<ArgumentException>(
            () => CurveOffset.Fillet(first, second, 0.5, Vector3d.ZAxis));

        Curve longerFirst = first.Extended(0.0, 2.0);
        Curve longerSecond = second.Extended(2.0, 0.0);

        (Arc fillet, _, _) = CurveOffset.Fillet(longerFirst, longerSecond, 0.5, Vector3d.ZAxis);

        Assert.Equal(0.5, fillet.Radius, 1e-8);
    }
}

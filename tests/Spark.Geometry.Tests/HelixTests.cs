using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Helix"/> — the eighth curve type (`E2-T73`, decision `D27`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The arc length is never pinned against the expression the type computes.</b> A helix's speed
/// is <c>sqrt(r² + (pitch/2π)²)</c>, and asserting that against <see cref="Curve.Length"/> asserts
/// the implementation against itself. The anchor here is the summed chord length of a very fine
/// sampling of <see cref="Curve.PointAt(double)"/>, which uses no closed form at all and converges
/// on the true length from below.
/// </para>
/// <para>
/// <b>And a division test is not evidence either.</b> Every curve Spark has except
/// <see cref="EllipseCurve"/> travels at a constant speed, so a <see cref="Curve.DivideEqually"/>
/// that used the parameter rather than the length would pass on all of them — `E2-T7` learnt that
/// once already. The division tests below check against the analytic arc length rather than being
/// the evidence for it.
/// </para>
/// </remarks>
public sealed class HelixTests
{
    private const double Tight = 1e-9;
    private const double FullTurn = Math.PI * 2.0;

    /// <summary>A right-handed helix of radius 2 and pitch 3, three turns about the world z axis.</summary>
    private static Helix Standard =>
        Helix.FromAxis(
            Point3d.Origin,
            Vector3d.ZAxis,
            new Point3d(2.0, 0.0, 0.0),
            3.0,
            Angle.FromRadians(3.0 * FullTurn));

    [Fact]
    public void AHelixTurnsAtItsRadiusAndRisesByItsPitchEveryTurn()
    {
        Helix helix = Standard;

        Assert.Equal(new Interval(0.0, 3.0 * FullTurn), helix.Domain);
        Assert.False(helix.IsClosed);
        Assert.Equal(2.0, helix.Radius, Tight);
        Assert.Equal(3.0, helix.Pitch, Tight);
        Assert.Equal(3.0 * FullTurn, helix.SweepAngle.Radians, Tight);
        Assert.Equal(Point3d.Origin, helix.AxisPoint);
        Assert.Equal(Vector3d.ZAxis, helix.AxisDirection);

        Assert.Equal(new Point3d(2.0, 0.0, 0.0), helix.StartPoint);
        Assert.Equal(new Point3d(2.0, 0.0, 9.0).X, helix.EndPoint.X, Tight);
        Assert.Equal(0.0, helix.EndPoint.Y, Tight);
        Assert.Equal(9.0, helix.EndPoint.Z, Tight);

        // A quarter turn: a quarter of the way round and a quarter of a pitch up.
        Point3d quarter = helix.PointAt(Math.PI / 2.0);
        Assert.Equal(0.0, quarter.X, Tight);
        Assert.Equal(2.0, quarter.Y, Tight);
        Assert.Equal(0.75, quarter.Z, Tight);
    }

    [Fact]
    public void TheArcLengthAgreesWithASummedChordOfTheCurveItself()
    {
        // The anchor: no closed form anywhere in it, only PointAt. A chord sum always UNDERSTATES a
        // curve's length, so agreement to nine figures is agreement that the analytic expression is
        // the length of the curve the type actually evaluates.
        Helix helix = Standard;
        const int samples = 400_000;

        double chords = 0.0;
        Point3d previous = helix.PointAt(0.0);
        for (int index = 1; index <= samples; index++)
        {
            Point3d next = helix.PointAt(helix.Domain.Denormalise((double)index / samples));
            chords += previous.DistanceTo(next);
            previous = next;
        }

        Assert.Equal(chords, helix.Length, helix.Length * 1e-9);
        Assert.True(chords <= helix.Length, "a chord sum cannot exceed the curve it samples.");
    }

    [Fact]
    public void AHelixUnrollsToTheHypotenuseOfARightTriangle()
    {
        // The second anchor, and an independent one: unroll the cylinder the helix lies on and the
        // curve becomes a straight line whose legs are the arc it turned through and the height it
        // rose. This is the reason the speed is constant, stated as a fact about lengths.
        Helix helix = Standard;
        double turned = helix.Radius * helix.SweepAngle.Radians;
        double risen = helix.Pitch * helix.SweepAngle.Radians / FullTurn;

        Assert.Equal(Math.Sqrt((turned * turned) + (risen * risen)), helix.Length, Tight);
        Assert.Equal(9.0, risen, Tight);
    }

    [Fact]
    public void LengthAtAndParameterAtLengthInvertEachOther()
    {
        Helix helix = Standard;

        for (int index = 0; index <= 20; index++)
        {
            double parameter = helix.Domain.Denormalise(index / 20.0);
            Assert.Equal(parameter, helix.ParameterAtLength(helix.LengthAt(parameter)), Tight);
        }

        Assert.Equal(0.0, helix.LengthAt(0.0), Tight);
        Assert.Equal(helix.Length, helix.LengthAt(helix.Domain.Max), Tight);

        // Clamped rather than refused at both ends, as Curve's own contract says.
        Assert.Equal(0.0, helix.ParameterAtLength(-5.0), Tight);
        Assert.Equal(helix.Domain.Max, helix.ParameterAtLength(helix.Length * 2.0), Tight);
    }

    [Fact]
    public void EqualDivisionsAreEqualInLengthRatherThanInParameter()
    {
        // Pinned against the ANALYTIC arc length, never against the division's own answer: on a
        // constant-speed curve the two parameterisations agree, so a division checked against
        // itself cannot tell a right answer from a plausible one.
        Helix helix = Standard;
        Point3d[] points = helix.DivideEqually(12);

        Assert.Equal(13, points.Length);
        Assert.Equal(helix.StartPoint, points[0]);

        double step = helix.Length / 12.0;
        for (int index = 0; index <= 12; index++)
        {
            Assert.Equal(helix.PointAtLength(step * index).X, points[index].X, Tight);
            Assert.Equal(helix.PointAtLength(step * index).Y, points[index].Y, Tight);
            Assert.Equal(helix.PointAtLength(step * index).Z, points[index].Z, Tight);

            // The rise is linear in arc length because the speed is constant, which is the whole
            // claim — and it is stated here without using the curve's own length machinery.
            Assert.Equal(9.0 * index / 12.0, points[index].Z, Tight);
        }
    }

    [Fact]
    public void TheSpeedIsTheSameAtEveryParameter()
    {
        Helix helix = Standard;
        double first = helix.DerivativeAt(0.0).Length;

        for (int index = 1; index <= 20; index++)
        {
            Assert.Equal(first, helix.DerivativeAt(helix.Domain.Denormalise(index / 20.0)).Length, Tight);
        }

        // The tangent leans out of the turn by exactly the rise per radian, which is the fact the
        // constant speed is made of.
        Assert.Equal(helix.Pitch / FullTurn, helix.TangentAt(0.0).Dot(Vector3d.ZAxis) * first, Tight);
    }

    [Fact]
    public void ANegativeSweepGivesTheSameCurveTurningAboutTheOppositeAxis()
    {
        // `E2-T73`: the branch this proves is the negative-sweep plane flip in FromAxis. Remove it
        // and the domain runs backwards, which every member of Curve relies on it not doing.
        Helix negative = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FromRadians(-FullTurn));

        Assert.Equal(0.0, negative.Domain.Min, Tight);
        Assert.Equal(FullTurn, negative.Domain.Max, Tight);
        Assert.Equal(-Vector3d.ZAxis, negative.AxisDirection);
        Assert.Equal(3.0, negative.Pitch, Tight);

        // Turning the other way about +z is turning the same way about -z, so the points are the
        // same points in the same order — and the helix therefore goes DOWN, even though its pitch
        // is positive, because the pitch is measured along the axis it reports.
        Helix mirror = Helix.FromAxis(
            Point3d.Origin, -Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FromRadians(FullTurn));

        for (int index = 0; index <= 16; index++)
        {
            double parameter = FullTurn * index / 16.0;
            Point3d expected = mirror.PointAt(parameter);
            Point3d actual = negative.PointAt(parameter);

            Assert.Equal(expected.X, actual.X, Tight);
            Assert.Equal(expected.Y, actual.Y, Tight);
            Assert.Equal(expected.Z, actual.Z, Tight);
        }

        Assert.Equal(-3.0, negative.EndPoint.Z, Tight);
    }

    [Fact]
    public void ANegativePitchIsTheOppositeHandRatherThanAnError()
    {
        Helix right = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(1.0, 0.0, 0.0), 2.0, Angle.FromRadians(FullTurn));
        Helix left = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(1.0, 0.0, 0.0), -2.0, Angle.FromRadians(FullTurn));

        Assert.Equal(right.Length, left.Length, Tight);

        for (int index = 0; index <= 8; index++)
        {
            double parameter = FullTurn * index / 8.0;
            Point3d one = right.PointAt(parameter);
            Point3d other = left.PointAt(parameter);

            // Reflected in the plane of the start: same turn, opposite rise. It cannot be reached
            // by flipping the axis, which is why the pitch is allowed to be negative.
            Assert.Equal(one.X, other.X, Tight);
            Assert.Equal(one.Y, other.Y, Tight);
            Assert.Equal(-one.Z, other.Z, Tight);
        }
    }

    [Fact]
    public void AZeroPitchIsRefusedAndTheRefusalNamesArc()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => Helix.FromAxis(
                Point3d.Origin, Vector3d.ZAxis, new Point3d(1.0, 0.0, 0.0), 0.0, Angle.FullTurn));

        Assert.Equal("pitch", error.ParamName);
        Assert.Contains("Arc", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStartPointOnTheAxisIsRefusedBecauseThereIsNoRadiusToTurnAt()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Helix.FromAxis(
                Point3d.Origin, Vector3d.ZAxis, new Point3d(0.0, 0.0, 5.0), 1.0, Angle.FullTurn));

        Assert.Equal("startPoint", error.ParamName);
    }

    [Fact]
    public void AZeroSweepIsRefused()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => Helix.FromAxis(
                Point3d.Origin, Vector3d.ZAxis, new Point3d(1.0, 0.0, 0.0), 1.0, Angle.Zero));

        Assert.Equal("sweepAngle", error.ParamName);
    }

    [Fact]
    public void TheAxisPointIsReportedBesideTheStartWhereverTheCallerPutTheOrigin()
    {
        // Only the axis LINE is in the curve: the start point fixes the height at angle zero, so an
        // origin anywhere else on the same line names the same helix. Two origins, one curve.
        Helix low = Helix.FromAxis(
            new Point3d(0.0, 0.0, -100.0), Vector3d.ZAxis, new Point3d(1.0, 0.0, 4.0), 1.0, Angle.FullTurn);
        Helix high = Helix.FromAxis(
            new Point3d(0.0, 0.0, 250.0), Vector3d.ZAxis, new Point3d(1.0, 0.0, 4.0), 1.0, Angle.FullTurn);

        Assert.Equal(new Point3d(0.0, 0.0, 4.0), low.AxisPoint);
        Assert.Equal(low.AxisPoint, high.AxisPoint);
        Assert.Equal(low.EndPoint, high.EndPoint);
        Assert.Equal(5.0, low.EndPoint.Z, Tight);
    }

    [Fact]
    public void AnAxisThatIsNotAUnitVectorIsNormalisedRatherThanScalingTheRise()
    {
        Helix helix = Helix.FromAxis(
            Point3d.Origin, new Vector3d(0.0, 0.0, 17.0), new Point3d(1.0, 0.0, 0.0), 2.0, Angle.FullTurn);

        Assert.Equal(Vector3d.ZAxis, helix.AxisDirection);
        Assert.Equal(2.0, helix.EndPoint.Z, Tight);
    }

    [Fact]
    public void ReversingGivesTheSamePointsInTheOppositeOrder()
    {
        Helix helix = Standard;
        Curve reversed = helix.Reversed();

        Assert.IsType<Helix>(reversed);
        Assert.Equal(helix.Length, reversed.Length, Tight);
        Assert.Equal(helix.EndPoint.Z, reversed.StartPoint.Z, Tight);
        Assert.Equal(helix.StartPoint.Z, reversed.EndPoint.Z, Tight);

        // The pitch survives the reversal because the axis flips with it: a helix traversed
        // backwards still rises along the direction it reports.
        Assert.Equal(helix.Pitch, ((Helix)reversed).Pitch, Tight);
        Assert.Equal(-helix.AxisDirection, ((Helix)reversed).AxisDirection);

        for (int index = 0; index <= 24; index++)
        {
            double t = index / 24.0;
            Point3d forwards = helix.PointAt(helix.Domain.Denormalise(t));
            Point3d backwards = reversed.PointAt(reversed.Domain.Denormalise(1.0 - t));

            Assert.Equal(forwards.X, backwards.X, Tight);
            Assert.Equal(forwards.Y, backwards.Y, Tight);
            Assert.Equal(forwards.Z, backwards.Z, Tight);
        }
    }

    [Fact]
    public void TrimmingAHelixGivesAHelixRatherThanChangingTheType()
    {
        Helix helix = Standard;
        Curve piece = helix.Trimmed(new Interval(FullTurn, 2.0 * FullTurn));

        Helix trimmed = Assert.IsType<Helix>(piece);
        Assert.Equal(FullTurn, trimmed.SweepAngle.Radians, Tight);
        Assert.Equal(helix.Radius, trimmed.Radius, Tight);
        Assert.Equal(helix.Pitch, trimmed.Pitch, Tight);
        Assert.Equal(helix.AxisDirection, trimmed.AxisDirection);
        Assert.Equal(3.0, trimmed.AxisPoint.Z, Tight);
        Assert.Equal(helix.Length / 3.0, trimmed.Length, Tight);

        for (int index = 0; index <= 12; index++)
        {
            double parameter = FullTurn * index / 12.0;
            Point3d whole = helix.PointAt(FullTurn + parameter);
            Point3d part = trimmed.PointAt(parameter);

            Assert.Equal(whole.X, part.X, Tight);
            Assert.Equal(whole.Y, part.Y, Tight);
            Assert.Equal(whole.Z, part.Z, Tight);
        }
    }

    [Fact]
    public void ADecreasingTrimGivesThePieceTraversedBackwards()
    {
        Helix helix = Standard;
        Helix backwards = (Helix)helix.Trimmed(new Interval(2.0 * FullTurn, FullTurn));

        Assert.Equal(FullTurn, backwards.SweepAngle.Radians, Tight);
        Assert.Equal(6.0, backwards.AxisPoint.Z, Tight);
        Assert.Equal(helix.PointAt(2.0 * FullTurn), backwards.StartPoint);
        Assert.Equal(3.0, backwards.EndPoint.Z, Tight);
    }

    [Fact]
    public void ATrimOutsideTheDomainIsRefused()
    {
        Helix helix = Standard;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => helix.Trimmed(new Interval(0.0, (3.0 * FullTurn) + 1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => helix.Trimmed(new Interval(-1.0, 1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => helix.Trimmed(new Interval(1.0, 1.0)));
    }

    [Fact]
    public void AUniformScaleScalesTheRiseAsWellAsTheRadius()
    {
        // `E2-T73`: the branch this proves is the axial factor in TransformedBy. Take the pitch
        // scaling out and this helix keeps its shape and loses its height — the easy mistake,
        // because under a uniform scale the radial and axial factors are equal and hide it.
        Helix helix = Standard;
        Helix scaled = (Helix)helix.TransformedBy(Transform.Scale(2.0));

        Assert.Equal(4.0, scaled.Radius, Tight);
        Assert.Equal(6.0, scaled.Pitch, Tight);
        Assert.Equal(helix.SweepAngle.Radians, scaled.SweepAngle.Radians, Tight);
        Assert.Equal(18.0, scaled.EndPoint.Z, Tight);
        Assert.Equal(helix.Length * 2.0, scaled.Length, Tight);
    }

    [Fact]
    public void AStretchAlongTheAxisChangesThePitchAndNotTheRadius()
    {
        // And the converse of the test above: the two factors are genuinely different numbers, so
        // one of them cannot stand in for the other.
        Helix helix = Standard;
        Helix stretched = (Helix)helix.TransformedBy(Transform.Scale(Point3d.Origin, 1.0, 1.0, 4.0));

        Assert.Equal(2.0, stretched.Radius, Tight);
        Assert.Equal(12.0, stretched.Pitch, Tight);
        Assert.Equal(36.0, stretched.EndPoint.Z, Tight);
    }

    [Fact]
    public void AMirrorGivesAHelixOfTheOppositeHand()
    {
        Helix helix = Standard;
        Helix mirrored = (Helix)helix.TransformedBy(Transform.Mirror(Plane.WorldXY));

        Assert.Equal(2.0, mirrored.Radius, Tight);
        Assert.Equal(-3.0, mirrored.Pitch, Tight);
        Assert.Equal(-9.0, mirrored.EndPoint.Z, Tight);
    }

    [Fact]
    public void ARigidMoveCarriesTheHelixWithoutChangingIt()
    {
        Helix helix = Standard;
        Transform move = Transform.Rotation(Vector3d.XAxis, Angle.QuarterTurn, Point3d.Origin)
            * Transform.Translation(new Vector3d(5.0, 0.0, 0.0));
        Helix moved = (Helix)helix.TransformedBy(move);

        Assert.Equal(helix.Radius, moved.Radius, Tight);
        Assert.Equal(helix.Pitch, moved.Pitch, Tight);
        Assert.Equal(helix.Length, moved.Length, Tight);
        Assert.Equal(move.OfPoint(helix.EndPoint), moved.EndPoint);
    }

    [Fact]
    public void AnUnevenScaleAcrossTheAxisIsRefusedBecauseTheResultIsNotAHelix()
    {
        Helix helix = Standard;

        Assert.Throws<ArgumentException>(
            () => helix.TransformedBy(Transform.Scale(Point3d.Origin, 2.0, 1.0, 1.0)));
    }

    [Fact]
    public void TheBoundingBoxContainsTheCurve()
    {
        Helix helix = Standard;
        BoundingBox box = helix.BoundingBox;

        Assert.Equal(-2.0, box.Min.X, Tight);
        Assert.Equal(2.0, box.Max.X, Tight);
        Assert.Equal(0.0, box.Min.Z, Tight);
        Assert.Equal(9.0, box.Max.Z, Tight);

        foreach (Point3d point in helix.Tessellate(new Tolerance(1e-4, Angle.FromDegrees(0.001), 1e-12)))
        {
            Assert.True(box.Contains(point), $"{point} escaped the bounding box {box}.");
        }
    }

    [Fact]
    public void TessellationFollowsTheCurveOverEveryTurnRatherThanCuttingAcrossIt()
    {
        // The seed span count scales with the sweep. Four seeds over three turns would each be a
        // chord through the axis, and the deviation test at such a chord's midpoint cannot see how
        // wrong it is — so this is a test of the seed and not only of the subdivision.
        Helix helix = Standard;
        Point3d[] points = helix.Tessellate(new Tolerance(1e-3, Angle.FromDegrees(0.001), 1e-12));

        Assert.True(points.Length > 100, $"{points.Length} points cannot follow three turns.");

        for (int index = 1; index < points.Length; index++)
        {
            Point3d middle = new(
                (points[index - 1].X + points[index].X) * 0.5,
                (points[index - 1].Y + points[index].Y) * 0.5,
                (points[index - 1].Z + points[index].Z) * 0.5);

            Assert.True(
                helix.DistanceTo(middle) <= 1e-3 + Tight,
                $"a chord at index {index} deviates by {helix.DistanceTo(middle)}.");
        }
    }

    [Fact]
    public void EveryConcreteCurveTypeSaysWhatItIs()
    {
        // [N161]: Curve.ToString()'s parity row rests on EVERY concrete curve type overriding it,
        // and an eighth that did not would quietly falsify a row already counted as Done.
        string text = Standard.ToString();

        Assert.StartsWith("Helix(", text, StringComparison.Ordinal);
        Assert.Contains("radius 2", text, StringComparison.Ordinal);
        Assert.Contains("pitch 3", text, StringComparison.Ordinal);
        Assert.Contains("turns 3", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorAndTheFactoryAgree()
    {
        Helix built = new(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FromRadians(3.0 * FullTurn));

        Assert.Equal(Standard.Radius, built.Radius, Tight);
        Assert.Equal(Standard.Pitch, built.Pitch, Tight);
        Assert.Equal(Standard.SweepAngle.Radians, built.SweepAngle.Radians, Tight);
        Assert.Equal(Standard.EndPoint, built.EndPoint);
    }
}

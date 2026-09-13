using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Chord stepping and division from a parameter — `E2-T71` family (3).
/// </summary>
/// <remarks>
/// <para>
/// <b>A chord is not an arc length, and a test suite that cannot tell them apart proves
/// nothing.</b> On a <i>line</i> the two agree exactly, which is worth pinning and is also why a
/// line can never be the case that decides anything here. On a <b>circle</b> they disagree by a
/// known amount — a chord of <c>d</c> on a circle of radius <c>r</c> subtends
/// <c>2·asin(d / 2r)</c> where an arc step of <c>d</c> subtends <c>d / r</c> — and that expression
/// is not in this repository, so it anchors the answer rather than restating it.
/// </para>
/// <para>
/// <b>The branch that matters is the bracket, not the refinement.</b> Distance from a fixed point
/// is not monotone along a curve that bends back, so a solver handed the whole remaining domain can
/// converge on a crossing that is not the first — a point past a lobe the caller wanted divided.
/// The tests that decide the implementation are therefore the ones on curves that double back.
/// </para>
/// </remarks>
public sealed class CurveChordDivisionTests
{
    private const double Tight = 1e-9;

    [Fact]
    public void OnALineChordAndArcAgreeExactly()
    {
        Line line = new(Point3d.Origin, new Point3d(10.0, 0.0, 0.0));

        Point3d[] byChord = line.DivideByChordLength(2.5);
        Point3d[] byLength = line.DivideByLength(2.5);

        Assert.Equal(byLength.Length, byChord.Length);

        for (int index = 0; index < byChord.Length; index++)
        {
            Assert.Equal(0.0, byChord[index].DistanceTo(byLength[index]), 1e-10);
        }

        Point3d[] equalChord = line.DivideEquallyByChord(4);
        Point3d[] equalArc = line.DivideEqually(4);

        for (int index = 0; index < equalChord.Length; index++)
        {
            Assert.Equal(0.0, equalChord[index].DistanceTo(equalArc[index]), 1e-10);
        }
    }

    [Fact]
    public void OnACircleTheChordStepSubtendsTwiceTheArcsineAndNotTheArcAngle()
    {
        // The anchor. Neither expression is in the kernel, so this pins the answer rather than
        // restating it — and the two differ by three per cent here, which is not a rounding
        // difference and is what makes them separate members.
        const double radius = 5.0;
        const double chord = 2.0;

        Circle circle = Circle.FromCenterRadius(Point3d.Origin, radius);
        Point3d[] points = circle.DivideByChordLength(chord);

        double expected = 2.0 * Math.Asin(chord / (2.0 * radius));

        for (int index = 1; index < points.Length; index++)
        {
            // Every consecutive pair is exactly the chord apart, which is the member's promise.
            Assert.Equal(chord, points[index - 1].DistanceTo(points[index]), 1e-9);

            double angle = Math.Atan2(points[index].Y, points[index].X);
            double previous = Math.Atan2(points[index - 1].Y, points[index - 1].X);
            double step = angle - previous;

            while (step < 0.0)
            {
                step += Math.PI * 2.0;
            }

            Assert.Equal(expected, step, 1e-8);
        }

        // And it is NOT the arc-length answer, which would be chord / radius.
        Assert.True(
            Math.Abs(expected - (chord / radius)) > 1e-3,
            "the two measures agreed, so this curve cannot tell them apart.");
    }

    [Fact]
    public void EqualChordDivisionOfACircleIsTheInscribedPolygon()
    {
        // n equal chords around a full circle is the regular n-gon inscribed in it, whose side is
        // 2r·sin(π/n). A closed curve is also the case where the bisection's lower bound carries no
        // information, because the two ends coincide — so this exercises that branch too.
        const double radius = 3.0;
        const int sides = 7;

        Circle circle = Circle.FromCenterRadius(Point3d.Origin, radius);
        Point3d[] points = circle.DivideEquallyByChord(sides);

        Assert.Equal(sides + 1, points.Length);

        double expected = 2.0 * radius * Math.Sin(Math.PI / sides);

        for (int index = 1; index < points.Length; index++)
        {
            Assert.Equal(expected, points[index - 1].DistanceTo(points[index]), 1e-7);
        }

        Assert.Equal(0.0, points[0].DistanceTo(points[^1]), 1e-7);
    }

    [Fact]
    public void EqualChordDivisionOfAnArcMatchesTheClosedForm()
    {
        // An arc of sweep θ into n equal chords: each subtends θ/n, so each chord is 2r·sin(θ/2n).
        const double radius = 4.0;
        const int pieces = 5;

        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, radius, Angle.FromDegrees(20.0), Angle.FromDegrees(140.0));

        Point3d[] points = arc.DivideEquallyByChord(pieces);
        double expected = 2.0 * radius * Math.Sin(arc.SweepAngle.Radians / (2.0 * pieces));

        Assert.Equal(pieces + 1, points.Length);
        Assert.Equal(0.0, points[0].DistanceTo(arc.StartPoint), 1e-9);
        Assert.Equal(0.0, points[^1].DistanceTo(arc.EndPoint), 1e-9);

        for (int index = 1; index < points.Length; index++)
        {
            Assert.Equal(expected, points[index - 1].DistanceTo(points[index]), 1e-8);
        }
    }

    [Fact]
    public void EqualChordDivisionIsNotEqualArcDivision()
    {
        // On an ellipse the two disagree visibly, which is the whole reason both exist.
        EllipseCurve ellipse = EllipseCurve.FromPlaneRadii(Plane.WorldXY, 6.0, 2.0);

        Point3d[] byChord = ellipse.DivideEquallyByChord(8);
        Point3d[] byArc = ellipse.DivideEqually(8);

        double worst = 0.0;

        for (int index = 0; index < byChord.Length; index++)
        {
            worst = Math.Max(worst, byChord[index].DistanceTo(byArc[index]));
        }

        Assert.True(worst > 0.05, $"the two divisions differed by only {worst}.");

        // And the chord one really does have equal chords, which the arc one does not.
        double first = byChord[0].DistanceTo(byChord[1]);

        for (int index = 1; index < byChord.Length; index++)
        {
            Assert.Equal(first, byChord[index - 1].DistanceTo(byChord[index]), 1e-7);
        }
    }

    [Fact]
    public void AChordStepTakesTheFirstCrossingOnACurveThatBendsBack()
    {
        // `E2-T71`: THE TEST THAT DECIDES THE IMPLEMENTATION. On a circle of radius 1 every chord
        // under the diameter has TWO answers ahead of any point — one just along the curve and one
        // most of the way round — and only the nearer is the next point. A solver handed the whole
        // remaining domain without a bracket can return the far one, and the result looks like a
        // plausible division that has skipped most of the circle.
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 1.0);
        Point3d[] points = circle.DivideByChordLength(1.0);

        // A chord of 1 on a unit circle subtends 60 degrees, so six of them close the circle.
        Assert.Equal(7, points.Length);

        for (int index = 1; index < points.Length; index++)
        {
            Assert.Equal(1.0, points[index - 1].DistanceTo(points[index]), 1e-9);

            // Every step advances by sixty degrees, not by three hundred: if the far crossing had
            // been taken, the angles would run backwards round the circle.
            double step = Math.Atan2(points[index].Y, points[index].X)
                - Math.Atan2(points[index - 1].Y, points[index - 1].X);

            while (step < 0.0)
            {
                step += Math.PI * 2.0;
            }

            Assert.Equal(Math.PI / 3.0, step, 1e-8);
        }
    }

    [Fact]
    public void AChordStepDoesNotSkipALobeOfAnSCurve()
    {
        // A tight S: the distance from a point near one end rises, falls as the curve turns back,
        // and rises again. An unbracketed solve can land on the second rise. Every step here is
        // checked to be moving FORWARD along the curve and to be the first such point, by
        // confirming no earlier parameter is also a chord away.
        NurbsCurve wiggle = new(
            3,
            [
                new Point3d(0.0, 0.0, 0.0),
                new Point3d(1.0, 4.0, 0.0),
                new Point3d(3.0, -4.0, 0.0),
                new Point3d(4.0, 0.0, 0.0),
            ],
            [0, 0, 0, 0, 1, 1, 1, 1]);

        Point3d[] points = wiggle.DivideByChordLength(0.75);

        Assert.True(points.Length > 4, $"{points.Length} points is not a division of this curve.");

        double previousParameter = wiggle.Domain.Min;

        for (int index = 1; index < points.Length; index++)
        {
            Assert.Equal(0.75, points[index - 1].DistanceTo(points[index]), 1e-8);

            double parameter = wiggle.ClosestParameter(points[index]);

            Assert.True(
                parameter > previousParameter,
                $"step {index} went backwards, from {previousParameter} to {parameter}.");

            // Nothing between the two is already a chord away: this is the FIRST crossing.
            for (int sample = 1; sample < 40; sample++)
            {
                double between = previousParameter
                    + ((parameter - previousParameter) * sample / 40.0);

                Assert.True(
                    points[index - 1].DistanceTo(wiggle.PointAt(between)) < 0.75 + 1e-9,
                    $"a point at {between} was already a chord away, so step {index} skipped it.");
            }

            previousParameter = parameter;
        }
    }

    [Fact]
    public void DividingByLengthFromAParameterStartsThereAndIncludesIt()
    {
        Line line = new(Point3d.Origin, new Point3d(10.0, 0.0, 0.0));

        Point3d[] points = line.DivideByLength(2.0, 0.3);

        Assert.Equal(new Point3d(3.0, 0.0, 0.0), points[0]);
        Assert.Equal(5.0, points[1].X, Tight);
        Assert.Equal(9.0, points[^1].X, Tight);

        // The remainder at the far end is dropped rather than becoming a short piece, as the
        // from-the-start division already does.
        Assert.Equal(4, points.Length);
    }

    [Fact]
    public void DividingByLengthFromAParameterAgreesWithTrimmingFirst()
    {
        // The member exists for the caller who does not know they could trim; it has to give the
        // same answer as the caller who does.
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 3.0, Angle.Zero, Angle.FromDegrees(180.0));

        double from = arc.Domain.Denormalise(0.25);

        Point3d[] direct = arc.DivideByLength(1.5, from);
        Point3d[] trimmed = arc.Trimmed(new Interval(from, arc.Domain.Max)).DivideByLength(1.5);

        Assert.Equal(trimmed.Length, direct.Length);

        for (int index = 0; index < direct.Length; index++)
        {
            Assert.Equal(0.0, direct[index].DistanceTo(trimmed[index]), 1e-8);
        }
    }

    [Fact]
    public void AStartParameterOutsideTheDomainIsClampedRatherThanRefused()
    {
        Line line = new(Point3d.Origin, new Point3d(10.0, 0.0, 0.0));

        Assert.Equal(Point3d.Origin, line.DivideByLength(2.0, -5.0)[0]);
        Assert.Equal(new Point3d(10.0, 0.0, 0.0), line.DivideByChordLength(2.0, 7.0)[0]);
    }

    [Fact]
    public void AChordLongerThanTheCurveGivesOnlyTheStartingPoint()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Assert.Single(line.DivideByChordLength(5.0));
    }

    [Fact]
    public void ANonPositiveSpacingIsRefused()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideByChordLength(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideByChordLength(-1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideByLength(0.0, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideByChordLength(1.0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideEquallyByChord(0));
    }

    [Fact]
    public void DividingIntoOnePieceIsTheTwoEnds()
    {
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldXY, 2.0, Angle.Zero, Angle.FromDegrees(90.0));

        Point3d[] points = arc.DivideEquallyByChord(1);

        Assert.Equal(2, points.Length);
        Assert.Equal(arc.StartPoint, points[0]);
        Assert.Equal(arc.EndPoint, points[1]);
    }

    [Fact]
    public void ChordSteppingFromAPointGoesThroughClosestParameter()
    {
        // Dynamo's member takes a point; Spark's takes a parameter, and this is the composition
        // that joins them. A point that is not quite on the curve is VISIBLY snapped here rather
        // than silently inside the member.
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 2.0);
        Point3d near = new(0.0, 2.1, 0.0);

        Point3d[] points = circle.DivideByChordLength(1.0, circle.ClosestParameter(near));

        Assert.Equal(0.0, points[0].DistanceTo(new Point3d(0.0, 2.0, 0.0)), 1e-9);
        Assert.Equal(1.0, points[0].DistanceTo(points[1]), 1e-9);
    }
}

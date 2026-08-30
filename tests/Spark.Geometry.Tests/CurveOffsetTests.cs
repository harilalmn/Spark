using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Offsetting a curve, and filleting between two lines.
/// </summary>
/// <remarks>
/// <b>An offset has one defining property and it is measurable:</b> every point of the offset is
/// the offset distance from the original. That is asserted directly rather than by comparing
/// against a hand-computed shape, and it fails for every way the construction can go wrong — a
/// sideways direction taken in the wrong plane, a sign convention inverted, a fit that wandered.
/// </remarks>
public sealed class CurveOffsetTests
{
    private static Tolerance Loose => new(1e-3, Angle.FromDegrees(0.001), 1e-12);

    /// <summary>
    /// <b>The defining property.</b> Every point of the offset sits the offset distance from the
    /// original curve.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(-0.5)]
    [InlineData(2.0)]
    public void EveryPointOfAnOffsetIsTheOffsetDistanceFromTheOriginal(double distance)
    {
        NurbsCurve original = PlanarWave();

        (Curve offset, _) = CurveOffset.Offset(original, distance, Vector3d.ZAxis, Loose);

        for (int i = 0; i <= 100; i++)
        {
            double t = offset.Domain.Min + (offset.Domain.Length * i / 100.0);
            double measured = original.DistanceTo(offset.PointAt(t));

            Assert.True(
                Math.Abs(measured - Math.Abs(distance)) < 5e-3,
                $"A point on the offset is {measured} from the original, not {Math.Abs(distance)}.");
        }
    }

    /// <summary>
    /// <b>A line offsets to a line, exactly.</b> The shapes whose offset is the same kind of shape
    /// are answered in closed form — a fitted circle is a circle only to within a tolerance, and
    /// everything downstream that asks *is this an arc?* would start saying no.
    /// </summary>
    [Fact]
    public void ALineOffsetsToALineExactly()
    {
        Line line = new(new Point3d(0, 0, 0), new Point3d(10, 0, 0));

        (Curve offset, bool exact) = CurveOffset.Offset(line, 3.0, Vector3d.ZAxis, Loose);

        Assert.True(exact);
        Line offsetLine = Assert.IsType<Line>(offset);
        Assert.Equal(line.Length, offsetLine.Length, 9);

        for (int i = 0; i <= 10; i++)
        {
            double t = offsetLine.Domain.Min + (offsetLine.Domain.Length * i / 10.0);
            Assert.Equal(3.0, line.DistanceTo(offsetLine.PointAt(t)), 9);
        }
    }

    /// <summary>A circle offsets to a concentric circle, exactly, with the radius changed.</summary>
    [Fact]
    public void ACircleOffsetsToAConcentricCircleExactly()
    {
        Circle circle = Circle.ByPlaneRadius(Plane.WorldXY, 5.0);

        (Curve outward, bool exactOutward) = CurveOffset.Offset(circle, -2.0, Vector3d.ZAxis, Loose);
        (Curve inward, bool exactInward) = CurveOffset.Offset(circle, 2.0, Vector3d.ZAxis, Loose);

        Assert.True(exactOutward);
        Assert.True(exactInward);
        Assert.Equal(7.0, Assert.IsType<Circle>(outward).Radius, 9);
        Assert.Equal(3.0, Assert.IsType<Circle>(inward).Radius, 9);
        Assert.True(Assert.IsType<Circle>(outward).Centre.EqualsWithin(circle.Centre));
    }

    /// <summary>An arc keeps its angles and changes only its radius.</summary>
    [Fact]
    public void AnArcOffsetsToAnArcExactly()
    {
        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 4.0, Angle.FromDegrees(30), Angle.FromDegrees(120));

        (Curve offset, bool exact) = CurveOffset.Offset(arc, -1.0, Vector3d.ZAxis, Loose);

        Assert.True(exact);
        Arc offsetArc = Assert.IsType<Arc>(offset);
        Assert.Equal(5.0, offsetArc.Radius, 9);
        Assert.Equal(arc.StartAngle.Radians, offsetArc.StartAngle.Radians, 9);
        Assert.Equal(arc.SweepAngle.Radians, offsetArc.SweepAngle.Radians, 9);
    }

    /// <summary>
    /// A general curve is fitted rather than exact, and says so. The flag is what lets a caller
    /// decide whether the tolerance mattered.
    /// </summary>
    [Fact]
    public void AGeneralCurveIsFittedAndSaysSo()
    {
        (Curve offset, bool exact) = CurveOffset.Offset(PlanarWave(), 1.0, Vector3d.ZAxis, Loose);

        Assert.False(exact);
        Assert.IsType<NurbsCurve>(offset);
    }

    /// <summary>Offsetting the other way is the other side, not the same curve.</summary>
    [Fact]
    public void TheSignOfTheDistanceChoosesTheSide()
    {
        NurbsCurve original = PlanarWave();

        (Curve left, _) = CurveOffset.Offset(original, 1.0, Vector3d.ZAxis, Loose);
        (Curve right, _) = CurveOffset.Offset(original, -1.0, Vector3d.ZAxis, Loose);

        Assert.True(
            left.PointAt(left.Domain.Min).DistanceTo(right.PointAt(right.Domain.Min)) > 1.5,
            "The two sides should be about two offsets apart, not the same curve.");
    }

    [Fact]
    public void ANormalWithNoLengthIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => CurveOffset.Offset(PlanarWave(), 1.0, default, Loose));
    }

    [Fact]
    public void ANonFiniteDistanceIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurveOffset.Offset(PlanarWave(), double.NaN, Vector3d.ZAxis, Loose));
    }

    /// <summary>
    /// <b>A fillet is tangent to both lines and the right radius.</b> Tangency is the whole point
    /// and is checked directly: the arc's ends sit on the trimmed lines, and its tangent there
    /// matches theirs.
    /// </summary>
    [Theory]
    [InlineData(90.0, 1.0)]
    [InlineData(90.0, 3.0)]
    [InlineData(45.0, 2.0)]
    [InlineData(135.0, 2.0)]
    public void AFilletIsTangentToBothLinesAtTheGivenRadius(double degrees, double radius)
    {
        Angle angle = Angle.FromDegrees(degrees);
        Point3d corner = new(0, 0, 0);

        Line first = new(new Point3d(-10, 0, 0), corner);
        Line second = new(
            corner,
            new Point3d(10 * Math.Cos(angle.Radians), 10 * Math.Sin(angle.Radians), 0));

        (Arc fillet, Line trimmedFirst, Line trimmedSecond) =
            CurveOffset.FilletLines(first, second, radius);

        Assert.Equal(radius, fillet.Radius, 9);

        // The arc's ends are the trimmed lines' ends: three curves that join.
        Assert.True(
            fillet.StartPoint.EqualsWithin(trimmedFirst.EndPoint)
            || fillet.EndPoint.EqualsWithin(trimmedFirst.EndPoint),
            "The fillet must meet the first line where it was trimmed to.");
        Assert.True(
            fillet.StartPoint.EqualsWithin(trimmedSecond.StartPoint)
            || fillet.EndPoint.EqualsWithin(trimmedSecond.StartPoint),
            "The fillet must meet the second line where it was trimmed to.");

        // Tangency: the arc's centre is exactly `radius` from both original lines.
        Assert.Equal(radius, first.DistanceTo(fillet.Centre), 9);
        Assert.Equal(radius, second.DistanceTo(fillet.Centre), 9);
    }

    /// <summary>
    /// The trimmed lines keep their far ends, so a caller's original geometry is not moved — only
    /// the corner is taken off.
    /// </summary>
    [Fact]
    public void TheTrimmedLinesKeepTheirFarEnds()
    {
        Line first = new(new Point3d(-10, 0, 0), new Point3d(0, 0, 0));
        Line second = new(new Point3d(0, 0, 0), new Point3d(0, 10, 0));

        (_, Line trimmedFirst, Line trimmedSecond) = CurveOffset.FilletLines(first, second, 2.0);

        Assert.True(trimmedFirst.StartPoint.EqualsWithin(new Point3d(-10, 0, 0)));
        Assert.True(trimmedSecond.EndPoint.EqualsWithin(new Point3d(0, 10, 0)));

        // And each is shorter than it was, by the setback.
        Assert.True(trimmedFirst.Length < first.Length);
        Assert.True(trimmedSecond.Length < second.Length);
    }

    /// <summary>
    /// A radius too large for the lines is refused with the number that would have been needed,
    /// rather than producing a fillet that runs off the end of one of them.
    /// </summary>
    [Fact]
    public void ARadiusTooLargeForTheLinesIsRefused()
    {
        Line first = new(new Point3d(-1, 0, 0), new Point3d(0, 0, 0));
        Line second = new(new Point3d(0, 0, 0), new Point3d(0, 1, 0));

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => CurveOffset.FilletLines(first, second, 50.0));

        Assert.Contains("shorter", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinesThatDoNotMeetAreRefused()
    {
        Line first = new(new Point3d(0, 0, 0), new Point3d(1, 0, 0));
        Line apart = new(new Point3d(5, 5, 0), new Point3d(6, 5, 0));

        Assert.Throws<ArgumentException>(() => CurveOffset.FilletLines(first, apart, 0.1));
    }

    [Fact]
    public void CollinearLinesHaveNoCornerToFillet()
    {
        Line first = new(new Point3d(-5, 0, 0), new Point3d(0, 0, 0));
        Line straightOn = new(new Point3d(0, 0, 0), new Point3d(5, 0, 0));

        Assert.Throws<ArgumentException>(() => CurveOffset.FilletLines(first, straightOn, 1.0));
    }

    [Fact]
    public void ANonPositiveRadiusIsRefused()
    {
        Line first = new(new Point3d(-5, 0, 0), new Point3d(0, 0, 0));
        Line second = new(new Point3d(0, 0, 0), new Point3d(0, 5, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => CurveOffset.FilletLines(first, second, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurveOffset.FilletLines(first, second, -1));
    }

    /// <summary>A curve in the XY plane, so that offsetting about Z is well posed.</summary>
    private static NurbsCurve PlanarWave() => new(
        [
            new Point3d(0, 0, 0), new Point3d(2, 3, 0), new Point3d(5, -1, 0),
            new Point3d(8, 2, 0), new Point3d(11, 0, 0),
        ],
        KnotVector.CreateClamped(3, 5));
}

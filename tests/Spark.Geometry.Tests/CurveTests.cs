using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The contract every curve shares: domains, evaluation, frames, arc length and division.
/// </summary>
/// <remarks>
/// The tests that matter most here are the arc-length ones on <see cref="EllipseCurve"/>. Every
/// other curve in this slice has a constant speed, so a division that used the parameter instead of
/// the length would pass on all of them; the ellipse is the only curve in the suite that can tell
/// the two apart, which is why it carries the load.
/// </remarks>
public sealed class CurveTests
{
    private const double Tight = 1e-9;

    [Fact]
    public void ALineRunsFromItsStartToItsEndOverAUnitDomain()
    {
        Line line = new(new Point3d(1.0, 2.0, 3.0), new Point3d(4.0, 6.0, 3.0));

        Assert.Equal(Interval.Unit, line.Domain);
        Assert.Equal(new Point3d(1.0, 2.0, 3.0), line.StartPoint);
        Assert.Equal(new Point3d(4.0, 6.0, 3.0), line.EndPoint);
        Assert.Equal(5.0, line.Length, Tight);
        Assert.False(line.IsClosed);
        Assert.Equal(new Point3d(2.5, 4.0, 3.0), line.PointAt(0.5));
        Assert.Equal(0.6, line.Direction.X, Tight);
    }

    [Fact]
    public void ALineWithCoincidentEndsIsRejectedRatherThanCarryingAZeroDirection()
    {
        Point3d point = new(1.0, 1.0, 1.0);

        ArgumentException error = Assert.Throws<ArgumentException>(() => new Line(point, point));
        Assert.Equal("end", error.ParamName);
    }

    [Fact]
    public void APointBeyondAnOpenCurvesDomainIsAnErrorRatherThanAnExtrapolation()
    {
        Line line = new(Point3d.Origin, new Point3d(0.0, 0.0, 1.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => line.PointAt(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.PointAt(-0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.PointAt(double.NaN));
    }

    [Fact]
    public void APointBeyondAClosedCurvesDomainWrapsInstead()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 2.0);

        Point3d wrapped = circle.PointAt((Math.PI * 2.0) + (Math.PI / 2.0));
        Point3d direct = circle.PointAt(Math.PI / 2.0);

        Assert.Equal(direct.X, wrapped.X, Tight);
        Assert.Equal(direct.Y, wrapped.Y, Tight);
        Assert.Equal(direct.Z, wrapped.Z, Tight);
    }

    [Fact]
    public void ACircleIsWhereItsPlaneSaysItIs()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(0.0, 0.0, 5.0), Vector3d.ZAxis);
        Circle circle = new(plane, 3.0);

        Assert.Equal(Math.PI * 6.0, circle.Length, Tight);
        Assert.True(circle.IsClosed);
        AssertClose(new Point3d(3.0, 0.0, 5.0), circle.PointAt(0.0));
        AssertClose(new Point3d(0.0, 3.0, 5.0), circle.PointAt(Math.PI / 2.0));
        AssertClose(new Point3d(-3.0, 0.0, 5.0), circle.PointAt(Math.PI));
        AssertClose(circle.StartPoint, circle.EndPoint);
    }

    [Fact]
    public void ACirclesBoundingBoxIsExactOnATiltedPlane()
    {
        // A tilted circle's box is the case a tessellated box gets wrong: the extreme in each
        // world axis falls between samples, so the box comes back systematically too small.
        Plane plane = Plane.ByOriginNormal(Point3d.Origin, new Vector3d(1.0, 1.0, 1.0));
        Circle circle = new(plane, 1.0);
        BoundingBox box = circle.BoundingBox;

        // For a unit circle the extent along any world axis is sqrt(1 - n^2), where n is that
        // axis's component of the unit normal. With a normal of (1,1,1)/sqrt(3) that is sqrt(2/3).
        double expected = Math.Sqrt(2.0 / 3.0);
        Assert.Equal(expected, box.Max.X, 1e-9);
        Assert.Equal(-expected, box.Min.X, 1e-9);
        Assert.Equal(expected, box.Max.Z, 1e-9);

        foreach (Point3d point in circle.DivideEqually(720))
        {
            Assert.True(box.Contains(point), $"The box does not contain {point}.");
        }
    }

    [Fact]
    public void AnArcThroughThreePointsPassesThroughAllThree()
    {
        Point3d first = new(1.0, 0.0, 0.0);
        Point3d second = new(0.0, 1.0, 0.0);
        Point3d third = new(-1.0, 0.0, 0.0);

        Arc arc = Arc.ByThreePoints(first, second, third);

        AssertClose(first, arc.StartPoint);
        AssertClose(third, arc.EndPoint);
        AssertClose(second, arc.MidPoint);
        Assert.Equal(1.0, arc.Radius, Tight);
        Assert.Equal(Math.PI, arc.SweepAngle.Radians, Tight);
    }

    [Fact]
    public void AnArcThroughThreePointsTakesTheLongWayRoundWhenTheMiddlePointIsThere()
    {
        // The same start and end as the half turn above, but with the middle point below the axis.
        // If ByThreePoints ignored the middle point, this would produce the identical arc.
        Point3d first = new(1.0, 0.0, 0.0);
        Point3d second = new(0.0, -1.0, 0.0);
        Point3d third = new(-1.0, 0.0, 0.0);

        Arc arc = Arc.ByThreePoints(first, second, third);

        AssertClose(second, arc.MidPoint);
        Assert.Equal(-1.0, arc.MidPoint.Y, Tight);
    }

    [Fact]
    public void ANegativeSweepFlipsThePlaneRatherThanReversingTheDomain()
    {
        Arc clockwise = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 1.0, Angle.Zero, Angle.FromDegrees(-90.0));

        Assert.True(clockwise.Domain.Min < clockwise.Domain.Max);
        Assert.Equal(Math.PI / 2.0, clockwise.SweepAngle.Radians, Tight);
        AssertClose(new Point3d(1.0, 0.0, 0.0), clockwise.StartPoint);
        AssertClose(new Point3d(0.0, -1.0, 0.0), clockwise.EndPoint);
        AssertClose(-Vector3d.ZAxis, clockwise.Plane.Normal);
    }

    [Fact]
    public void ReversingACurveWalksTheSamePathBackwards()
    {
        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 2.0, Angle.FromDegrees(30.0), Angle.FromDegrees(140.0));
        Curve reversed = arc.Reversed();

        Assert.Equal(arc.Length, reversed.Length, 1e-9);
        AssertClose(arc.StartPoint, reversed.EndPoint);
        AssertClose(arc.EndPoint, reversed.StartPoint);

        for (int step = 0; step <= 10; step++)
        {
            double distance = arc.Length * step / 10.0;
            AssertClose(arc.PointAtLength(distance), reversed.PointAtLength(arc.Length - distance));
        }
    }

    [Fact]
    public void TrimmingACircleProducesAnArcOverTheRequestedAngles()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 1.0);

        Curve trimmed = circle.Trimmed(new Interval(0.0, Math.PI / 2.0));

        Arc arc = Assert.IsType<Arc>(trimmed);
        Assert.False(arc.IsClosed);
        Assert.Equal(Math.PI / 2.0, arc.Length, Tight);
        AssertClose(new Point3d(1.0, 0.0, 0.0), arc.StartPoint);
        AssertClose(new Point3d(0.0, 1.0, 0.0), arc.EndPoint);
    }

    [Fact]
    public void DividingACircleEquallyPlacesPointsOnTheQuadrantsAndClosesTheLoop()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 1.0);

        Point3d[] points = circle.DivideEqually(4);

        Assert.Equal(5, points.Length);
        AssertClose(new Point3d(1.0, 0.0, 0.0), points[0]);
        AssertClose(new Point3d(0.0, 1.0, 0.0), points[1]);
        AssertClose(new Point3d(-1.0, 0.0, 0.0), points[2]);
        AssertClose(new Point3d(0.0, -1.0, 0.0), points[3]);
        AssertClose(points[0], points[4]);
    }

    [Fact]
    public void DividingAnEllipseEquallyDividesItByLengthRatherThanByParameter()
    {
        // The load-bearing test of the whole arc-length layer. On an ellipse of radii 3 and 1 the
        // speed varies by a factor of three, so a division by parameter gives spacings that differ
        // by nearly that much; only a division by arc length gives equal ones.
        EllipseCurve ellipse = EllipseCurve.ByPlaneRadii(Plane.WorldXY, 3.0, 1.0);
        const int divisions = 16;

        Point3d[] points = ellipse.DivideEqually(divisions);

        Assert.Equal(divisions + 1, points.Length);
        double expected = ellipse.Length / divisions;
        for (int index = 0; index < divisions; index++)
        {
            double from = ellipse.LengthAt(ellipse.ParameterAtLength(expected * index));
            double to = ellipse.LengthAt(ellipse.ParameterAtLength(expected * (index + 1)));
            Assert.Equal(expected, to - from, expected * 1e-6);
        }

        // And the same division done by parameter is visibly not equal, which is what makes the
        // assertion above capable of failing rather than merely true.
        double firstByParameter = ellipse.LengthAt(ellipse.Domain.Denormalise(1.0 / divisions));
        Assert.True(
            Math.Abs(firstByParameter - expected) > expected * 0.2,
            $"A parameter division gave {firstByParameter} against an equal-length {expected}, "
            + "which is too close for this test to be discriminating.");
    }

    [Fact]
    public void AnEllipsesLengthMatchesAFineIndependentPolygonalMeasurement()
    {
        EllipseCurve ellipse = EllipseCurve.ByPlaneRadii(Plane.WorldXY, 2.0, 1.0);

        // An inscribed polygon of many sides underestimates the perimeter, converging from below.
        // This is computed here from the ellipse's own parametric definition rather than from any
        // member under test, so it is an independent measurement rather than a restatement.
        const int sides = 200_000;
        double measured = 0.0;
        Point3d previous = new(2.0, 0.0, 0.0);
        for (int step = 1; step <= sides; step++)
        {
            double angle = Math.PI * 2.0 * step / sides;
            Point3d current = new(2.0 * Math.Cos(angle), Math.Sin(angle), 0.0);
            measured += current.DistanceTo(previous);
            previous = current;
        }

        Assert.Equal(measured, ellipse.Length, measured * 1e-9);
    }

    [Fact]
    public void AnEllipseParameterAndItsLengthAreInverses()
    {
        EllipseCurve ellipse = EllipseCurve.ByPlaneRadiiAngles(
            Plane.WorldXY, 5.0, 2.0, Angle.FromDegrees(20.0), Angle.FromDegrees(250.0));

        for (int step = 0; step <= 20; step++)
        {
            double parameter = ellipse.Domain.Denormalise(step / 20.0);
            double roundTripped = ellipse.ParameterAtLength(ellipse.LengthAt(parameter));
            Assert.Equal(parameter, roundTripped, 1e-7);
        }
    }

    [Fact]
    public void APolyLinesWholeNumberParametersAreItsVertices()
    {
        PolyLine polyline = PolyLine.ByPoints(
        [
            Point3d.Origin,
            new Point3d(3.0, 0.0, 0.0),
            new Point3d(3.0, 4.0, 0.0),
        ]);

        Assert.Equal(new Interval(0.0, 2.0), polyline.Domain);
        Assert.Equal(2, polyline.SegmentCount);
        Assert.Equal(7.0, polyline.Length, Tight);
        AssertClose(new Point3d(3.0, 0.0, 0.0), polyline.PointAt(1.0));
        AssertClose(new Point3d(1.5, 0.0, 0.0), polyline.PointAt(0.5));
        AssertClose(new Point3d(3.0, 2.0, 0.0), polyline.PointAt(1.5));
    }

    [Fact]
    public void APolyLineIsDividedByLengthAcrossItsVerticesRatherThanWithinThem()
    {
        // Three units along, then four up. A division every two units has to cross the corner: the
        // third point is one unit past it, which a per-segment division would put in the wrong place.
        PolyLine polyline = PolyLine.ByPoints(
        [
            Point3d.Origin,
            new Point3d(3.0, 0.0, 0.0),
            new Point3d(3.0, 4.0, 0.0),
        ]);

        Point3d[] points = polyline.DivideByLength(2.0);

        Assert.Equal(4, points.Length);
        AssertClose(Point3d.Origin, points[0]);
        AssertClose(new Point3d(2.0, 0.0, 0.0), points[1]);
        AssertClose(new Point3d(3.0, 1.0, 0.0), points[2]);
        AssertClose(new Point3d(3.0, 3.0, 0.0), points[3]);
    }

    [Fact]
    public void APolyLineWithACoincidentPairIsRejectedAndTheMessageNamesTheIndex()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => PolyLine.ByPoints(
        [
            Point3d.Origin,
            new Point3d(1.0, 0.0, 0.0),
            new Point3d(1.0, 0.0, 0.0),
        ]));

        Assert.Contains("1 and 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARectangleIsAClosedPolylineOfFourSegments()
    {
        PolyLine rectangle = PolyLine.ByRectangle(Plane.WorldXY, 4.0, 2.0);

        Assert.Equal(4, rectangle.SegmentCount);
        Assert.True(rectangle.IsClosed);
        Assert.Equal(12.0, rectangle.Length, Tight);
        Assert.Equal(new Point3d(-2.0, -1.0, 0.0), rectangle.PointAtIndex(0));
    }

    [Fact]
    public void ARegularPolygonClosesExactlyRatherThanNearly()
    {
        // Closure here is exact equality, so a polygon that closed by arithmetic rather than by
        // repeating its first point would report IsClosed false while looking closed on screen.
        PolyLine hexagon = PolyLine.ByRegularPolygon(Plane.WorldXY, 1.0, 6);

        Assert.True(hexagon.IsClosed);
        Assert.Equal(hexagon.StartPoint, hexagon.EndPoint);
        Assert.Equal(6, hexagon.SegmentCount);
        Assert.Equal(6.0, hexagon.Length, 1e-9);
    }

    [Fact]
    public void APolyCurveMeasuresLengthAcrossItsSegments()
    {
        Line line = new(Point3d.Origin, new Point3d(4.0, 0.0, 0.0));
        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.ByOriginNormal(new Point3d(4.0, 1.0, 0.0), Vector3d.ZAxis),
            1.0,
            Angle.FromDegrees(-90.0),
            Angle.FromDegrees(90.0));

        PolyCurve chain = PolyCurve.ByJoinedCurves([line, arc]);

        Assert.Equal(2, chain.SegmentCount);
        Assert.Equal(new Interval(0.0, 2.0), chain.Domain);
        Assert.Equal(4.0 + (Math.PI / 2.0), chain.Length, 1e-9);
        AssertClose(Point3d.Origin, chain.StartPoint);
        AssertClose(new Point3d(5.0, 1.0, 0.0), chain.EndPoint);

        // A length of 4 lands exactly on the joint; a length of 4 plus a quarter of the arc lands
        // a quarter of the way round it, which is the crossing a per-segment division gets wrong.
        AssertClose(new Point3d(4.0, 0.0, 0.0), chain.PointAtLength(4.0));
        AssertClose(
            new Point3d(4.0 + Math.Sin(Math.PI / 4.0), 1.0 - Math.Cos(Math.PI / 4.0), 0.0),
            chain.PointAtLength(4.0 + (Math.PI / 4.0)));
    }

    [Fact]
    public void APolyCurvesDerivativeCarriesTheChainRuleFactor()
    {
        // This test reaches through the internal seam on purpose. A polycurve maps one unit of its
        // own parameter onto the whole of a segment's domain, so the segment's derivative has to be
        // scaled by that domain's length. Every public route to the derivative — TangentAt,
        // NormalAt, CoordinateSystemAt — normalises, and normalising cancels exactly the factor
        // being checked, so a test written against the public surface passes whether the factor is
        // there or not. It was: this test replaces one that did precisely that.
        Line line = new(Point3d.Origin, new Point3d(4.0, 0.0, 0.0));
        Arc quarter = Arc.ByPlaneRadiusAngles(
            Plane.ByOriginNormal(new Point3d(4.0, 1.0, 0.0), Vector3d.ZAxis),
            1.0,
            Angle.FromDegrees(-90.0),
            Angle.FromDegrees(90.0));
        PolyCurve chain = PolyCurve.ByJoinedCurves([line, quarter]);

        // The line's domain is [0, 1] and it is 4 long, so its speed in chain parameters is 4. The
        // arc's domain is [0, π/2] and its speed there is its radius of 1, so the chain rule makes
        // its speed in chain parameters π/2. Without the factor it would read 1.
        Assert.Equal(4.0, chain.DerivativeWithin(0.5).Length, 1e-9);
        Assert.Equal(Math.PI / 2.0, chain.DerivativeWithin(1.5).Length, 1e-9);

        // And the general invariant behind those two numbers: the magnitude of the derivative is
        // the rate at which arc length accumulates, measured here through the public LengthAt.
        foreach (double parameter in new[] { 0.25, 0.75, 1.25, 1.75 })
        {
            const double step = 1e-6;
            double measured =
                (chain.LengthAt(parameter + step) - chain.LengthAt(parameter - step)) / (2.0 * step);
            Assert.Equal(measured, chain.DerivativeWithin(parameter).Length, 1e-6);
        }

        // The second derivative carries the factor squared, by the same argument.
        double before = chain.DerivativeWithin(1.5 - 1e-6).Length;
        double after = chain.DerivativeWithin(1.5 + 1e-6).Length;
        Assert.Equal(0.0, (after - before) / 2e-6, 1e-6);
        Assert.Equal(
            Math.PI * Math.PI / 4.0, chain.SecondDerivativeWithin(1.5).Length, 1e-9);
    }

    [Fact]
    public void APolyCurveTangentIsUnitLengthAcrossItsJoints()
    {
        Line line = new(Point3d.Origin, new Point3d(4.0, 0.0, 0.0));
        Circle circle = Circle.ByCentreRadius(new Point3d(5.0, 0.0, 0.0), 1.0);
        Curve half = circle.Trimmed(new Interval(Math.PI, Math.PI * 2.0));
        PolyCurve chain = PolyCurve.ByJoinedCurves([line, half]);

        for (int step = 0; step <= 20; step++)
        {
            double parameter = chain.Domain.Denormalise(step / 20.0);
            Assert.Equal(1.0, chain.TangentAt(parameter).Length, 1e-9);
        }

        Assert.Equal(4.0 + Math.PI, chain.Length, 1e-9);
    }

    [Fact]
    public void APolyCurveRefusesSegmentsThatDoNotMeetWithinTheGivenTolerance()
    {
        Line first = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        Line second = new(new Point3d(1.0, 0.01, 0.0), new Point3d(2.0, 0.0, 0.0));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PolyCurve.ByJoinedCurves([first, second]));
        Assert.Contains("0 and 1", error.Message, StringComparison.Ordinal);

        // The same pair is accepted when the caller says the gap is acceptable, because the
        // tolerance is passed rather than assumed.
        PolyCurve joined = PolyCurve.ByJoinedCurves(
            [first, second], new Tolerance(0.1, Angle.FromDegrees(0.001), 1e-12));
        Assert.Equal(2, joined.SegmentCount);
    }

    [Fact]
    public void JoiningPolyCurvesFlattensThemSoTheDomainDoesNotDependOnAssemblyOrder()
    {
        Line first = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        Line second = new(new Point3d(1.0, 0.0, 0.0), new Point3d(2.0, 0.0, 0.0));
        Line third = new(new Point3d(2.0, 0.0, 0.0), new Point3d(3.0, 0.0, 0.0));

        PolyCurve nested = PolyCurve.ByJoinedCurves(
            [PolyCurve.ByJoinedCurves([first, second]), third]);
        PolyCurve flat = PolyCurve.ByJoinedCurves([first, second, third]);

        Assert.Equal(3, nested.SegmentCount);
        Assert.Equal(flat.Domain, nested.Domain);
    }

    [Fact]
    public void TessellationStaysWithinTheToleranceItWasGiven()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 10.0);
        Tolerance tolerance = new(0.01, Angle.FromDegrees(0.001), 1e-12);

        Point3d[] points = circle.Tessellate(tolerance);

        Assert.True(points.Length > 4, $"A circle tessellated to {points.Length} points.");
        AssertClose(points[0], points[^1]);
        for (int index = 1; index < points.Length; index++)
        {
            // The deviation of a chord from a circle is the sagitta, and the worst case on each
            // chord is at its midpoint. Measuring it directly is what makes this a real check
            // rather than an assertion that some points came back.
            Point3d middle = points[index - 1].Midpoint(points[index]);
            double deviation = 10.0 - middle.DistanceTo(Point3d.Origin);
            Assert.True(deviation <= 0.01, $"Chord {index} deviates by {deviation}.");
        }
    }

    [Fact]
    public void ACoarserToleranceProducesFewerPoints()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 10.0);

        int fine = circle.Tessellate(new Tolerance(0.001, Angle.FromDegrees(0.001), 1e-12)).Length;
        int coarse = circle.Tessellate(new Tolerance(0.1, Angle.FromDegrees(0.001), 1e-12)).Length;

        Assert.True(fine > coarse, $"A fine tessellation gave {fine} points and a coarse one {coarse}.");
    }

    [Fact]
    public void AStraightCurveStillHasADeterministicNormalRatherThanAZeroVector()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Vector3d normal = line.NormalAt(0.5);

        Assert.Equal(1.0, normal.Length, Tight);
        Assert.Equal(0.0, normal.Dot(line.Direction), Tight);
        Assert.Equal(normal, line.NormalAt(0.25));
    }

    [Fact]
    public void ACurvesFrameIsRightHandedAndSitsOnTheCurve()
    {
        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 2.0, Angle.Zero, Angle.FromDegrees(120.0));

        CoordinateSystem frame = arc.CoordinateSystemAt(arc.Domain.Mid);
        Point3d point = arc.PointAt(arc.Domain.Mid);

        AssertClose(point, frame.Origin);
        Assert.Equal(1.0, frame.XAxis.Length, Tight);
        Assert.Equal(0.0, frame.XAxis.Dot(frame.YAxis), Tight);
        AssertClose(frame.XAxis.Cross(frame.YAxis), frame.ZAxis);

        // The principal normal of a circular arc points at the centre.
        AssertClose((arc.Centre - point).Normalised(), frame.YAxis);
    }

    [Fact]
    public void APlaneOnACurveHasTheTangentForItsNormal()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 1.0);

        Plane plane = circle.PlaneAt(0.0);

        AssertClose(circle.PointAt(0.0), plane.Origin);
        AssertClose(circle.TangentAt(0.0), plane.Normal);
    }

    [Fact]
    public void TransformingACurveMovesEveryPointOnIt()
    {
        Arc arc = Arc.ByPlaneRadiusAngles(
            Plane.WorldXY, 1.0, Angle.Zero, Angle.FromDegrees(90.0));
        Transform transform =
            Transform.Translation(new Vector3d(5.0, 0.0, 0.0)) * Transform.Scale(2.0);

        Curve moved = arc.TransformedBy(transform);

        Assert.Equal(arc.Length * 2.0, moved.Length, 1e-9);
        for (int step = 0; step <= 8; step++)
        {
            double fraction = step / 8.0;
            AssertClose(
                transform.OfPoint(arc.PointAtLength(arc.Length * fraction)),
                moved.PointAtLength(moved.Length * fraction));
        }
    }

    [Fact]
    public void ANonUniformScaleIsRefusedRatherThanQuietlyDeformingACircle()
    {
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 1.0);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => circle.TransformedBy(Transform.Scale(2.0, 1.0, 1.0)));
        Assert.Equal("transform", error.ParamName);

        // A scale along the circle's own normal leaves it a circle, and is allowed.
        Curve stretched = circle.TransformedBy(Transform.Scale(1.0, 1.0, 3.0));
        Assert.Equal(circle.Length, stretched.Length, Tight);
    }

    [Fact]
    public void TrimmingAPolyLineKeepsTheVerticesInBetween()
    {
        PolyLine polyline = PolyLine.ByPoints(
        [
            Point3d.Origin,
            new Point3d(2.0, 0.0, 0.0),
            new Point3d(2.0, 2.0, 0.0),
            new Point3d(4.0, 2.0, 0.0),
        ]);

        Curve trimmed = polyline.Trimmed(new Interval(0.5, 2.5));

        // Half of the first segment, all of the second, half of the third: 1 + 2 + 1.
        Assert.Equal(4.0, trimmed.Length, Tight);
        AssertClose(new Point3d(1.0, 0.0, 0.0), trimmed.StartPoint);
        AssertClose(new Point3d(3.0, 2.0, 0.0), trimmed.EndPoint);
        Assert.Equal(3, Assert.IsType<PolyLine>(trimmed).SegmentCount);
    }

    [Fact]
    public void TrimmingAPolyCurveCutsTheSegmentsAtTheEnds()
    {
        Line first = new(Point3d.Origin, new Point3d(2.0, 0.0, 0.0));
        Line second = new(new Point3d(2.0, 0.0, 0.0), new Point3d(2.0, 2.0, 0.0));
        Line third = new(new Point3d(2.0, 2.0, 0.0), new Point3d(4.0, 2.0, 0.0));
        PolyCurve chain = PolyCurve.ByJoinedCurves([first, second, third]);

        Curve trimmed = chain.Trimmed(new Interval(0.5, 2.5));

        Assert.Equal(3, Assert.IsType<PolyCurve>(trimmed).SegmentCount);
        Assert.Equal(4.0, trimmed.Length, Tight);
        AssertClose(new Point3d(1.0, 0.0, 0.0), trimmed.StartPoint);
        AssertClose(new Point3d(3.0, 2.0, 0.0), trimmed.EndPoint);
    }

    [Fact]
    public void DividingIntoFewerThanOneSegmentIsRejected()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideEqually(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideByLength(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.DivideByLength(double.NaN));
    }

    [Fact]
    public void EveryCurveTypeReportsALengthThatMatchesItsOwnTessellation()
    {
        // A cheap cross-check that catches a length expression that is wrong by a constant factor —
        // the kind of error a single hand-computed expectation per type would not catch twice.
        Tolerance fine = new(1e-7, Angle.FromDegrees(0.001), 1e-12);
        List<Curve> curves =
        [
            new Line(Point3d.Origin, new Point3d(1.0, 2.0, 3.0)),
            Circle.ByCentreRadius(Point3d.Origin, 2.5),
            Arc.ByPlaneRadiusAngles(Plane.WorldXY, 3.0, Angle.FromDegrees(15.0), Angle.FromDegrees(200.0)),
            EllipseCurve.ByPlaneRadii(Plane.WorldXY, 4.0, 1.5),
            PolyLine.ByRegularPolygon(Plane.WorldXY, 2.0, 7),
            PolyCurve.ByJoinedCurves(
            [
                new Line(Point3d.Origin, new Point3d(1.0, 0.0, 0.0)),
                Arc.ByPlaneRadiusAngles(
                    Plane.ByOriginNormal(new Point3d(1.0, 1.0, 0.0), Vector3d.ZAxis),
                    1.0,
                    Angle.FromDegrees(-90.0),
                    Angle.FromDegrees(90.0)),
            ]),
        ];

        foreach (Curve curve in curves)
        {
            Point3d[] points = curve.Tessellate(fine);
            double walked = 0.0;
            for (int index = 1; index < points.Length; index++)
            {
                walked += points[index].DistanceTo(points[index - 1]);
            }

            // A chord walk always underestimates, so the tolerance is one-sided in spirit; 1e-4
            // relative is loose enough for the polygonal approximation and far tighter than any
            // constant-factor error would be.
            Assert.Equal(curve.Length, walked, curve.Length * 1e-4);
        }
    }

    /// <summary>
    /// The worked example in <c>docs/help/concepts/curves.md</c> §1, asserted rather than
    /// remembered. A help topic that quotes a number nobody checks is how documentation starts
    /// lying, and this project has already paid once for a claim that read as verified.
    /// </summary>
    [Fact]
    public void TheHelpTopicsEllipseExampleIsTrue()
    {
        EllipseCurve ellipse = EllipseCurve.ByPlaneRadii(Plane.WorldXY, 3.0, 1.0);

        Point3d byParameter = ellipse.PointAt(ellipse.Domain.Denormalise(0.125));
        Point3d byLength = ellipse.PointAtLength(ellipse.Length * 0.125);

        Assert.Equal(0.48, byParameter.DistanceTo(byLength), 0.01);

        // And the quarter marks agree, because the four quadrants of an ellipse are congruent.
        // The topic says the two measures differ; that is true in general and false exactly here,
        // which is why the example uses an eighth.
        Assert.Equal(
            0.0,
            ellipse.PointAt(ellipse.Domain.Denormalise(0.25))
                .DistanceTo(ellipse.PointAtLength(ellipse.Length * 0.25)),
            1e-6);
    }

    private static void AssertClose(in Point3d expected, in Point3d actual)
    {
        Assert.True(
            expected.DistanceTo(actual) < 1e-9,
            $"Expected {expected} but got {actual}.");
    }

    private static void AssertClose(in Vector3d expected, in Vector3d actual)
    {
        Assert.True(
            (expected - actual).Length < 1e-9,
            $"Expected {expected} but got {actual}.");
    }
}

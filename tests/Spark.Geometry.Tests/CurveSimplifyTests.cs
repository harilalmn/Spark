using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Curve.Simplified"/> and its three overrides — `E2-T71`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is measuring against the retained chord rather than the immediate neighbours.</b>
/// Dropping a vertex whenever it sits within tolerance of the chord between its two neighbours is
/// the obvious algorithm and it <i>drifts</i>: on a finely sampled arc every removal is
/// individually acceptable and the accumulation flattens the arc. A test that only checks the
/// vertex count went down cannot see that, so the assertion is on <b>every original vertex's
/// distance to the result</b>.
/// </para>
/// <para>
/// <b>The base returns the same instance</b>, which is a stronger promise than returning an equal
/// one and is asserted as such.
/// </para>
/// </remarks>
public sealed class CurveSimplifyTests
{
    private static readonly Tolerance Loose = new(0.05, Angle.FromDegrees(1), 1e-12);

    /// <summary>An analytic curve is already the smallest description of itself.</summary>
    [Fact]
    public void AnAnalyticCurveIsReturnedUnchanged()
    {
        Line line = new(new Point3d(0, 0, 0), new Point3d(10, 0, 0));
        Arc arc = Arc.FromCenterStartEnd(Point3d.Origin, new Point3d(5, 0, 0), new Point3d(0, 5, 0));
        Circle circle = new(Plane.WorldXY, 4.0);

        Assert.Same(line, line.Simplified(Loose));
        Assert.Same(arc, arc.Simplified(Loose));
        Assert.Same(circle, circle.Simplified(Loose));
    }

    /// <summary>
    /// A curve interpolated through many collinear points carries interior knots it does not need,
    /// and simplification takes them off. The case that cannot argue.
    /// </summary>
    [Fact]
    public void AStraightInterpolatedCurveLosesItsRedundantKnots()
    {
        NurbsCurve straight = StraightThroughManyPoints();

        NurbsCurve simplified = Assert.IsType<NurbsCurve>(straight.Simplified(Loose));

        Assert.True(
            simplified.ControlPoints().Length < straight.ControlPoints().Length,
            $"nothing came off: {straight.ControlPoints().Length} control points before and "
            + $"{simplified.ControlPoints().Length} after.");

        // And it is still the same straight run.
        for (int sample = 0; sample <= 32; sample++)
        {
            Point3d before = straight.PointAt(straight.Domain.Denormalise(sample / 32.0));
            Point3d after = simplified.PointAt(simplified.Domain.Denormalise(sample / 32.0));

            Assert.True(before.DistanceTo(after) < 0.05, $"{before} became {after}.");
        }
    }

    /// <summary>
    /// <b>The limitation, pinned so that it is a decision rather than a surprise.</b> This removes
    /// knots, not degree. A straight line raised to degree five has six control points and
    /// <i>no interior knots at all</i>, so there is nothing for knot removal to take: its
    /// redundancy is in the degree, and reducing that is a separate algorithm Spark does not have.
    /// </summary>
    [Fact]
    public void ADegreeElevatedLineIsNotReducedBecauseItsRedundancyIsInTheDegree()
    {
        NurbsCurve elevated = new Line(new Point3d(0, 0, 0), new Point3d(10, 0, 0))
            .ToNurbsCurve().Curve.WithDegreeElevated(5);

        // Two control points became seven, and none of them is an interior knot.
        Assert.True(elevated.ControlPoints().Length > 2, "the fixture was not elevated.");

        NurbsCurve simplified = Assert.IsType<NurbsCurve>(elevated.Simplified(Loose));

        Assert.Equal(elevated.ControlPoints().Length, simplified.ControlPoints().Length);
    }

    /// <summary>A polyline's redundant vertices go; its corners stay.</summary>
    [Fact]
    public void APolylineLosesTheVerticesThatLieOnItsRuns()
    {
        // Three collinear points along the bottom, then a corner.
        PolyLine outline = new(
            [
                new Point3d(0, 0, 0),
                new Point3d(3, 0, 0),
                new Point3d(7, 0, 0),
                new Point3d(10, 0, 0),
                new Point3d(10, 10, 0),
            ]);

        PolyLine simplified = Assert.IsType<PolyLine>(outline.Simplified(Loose));

        Assert.Equal(3, simplified.Points().Length);
        Assert.True(simplified.Points()[0].DistanceTo(new Point3d(0, 0, 0)) < 1e-12);
        Assert.True(simplified.Points()[1].DistanceTo(new Point3d(10, 0, 0)) < 1e-12);
        Assert.True(simplified.Points()[2].DistanceTo(new Point3d(10, 10, 0)) < 1e-12);
    }

    /// <summary>
    /// <b>The branch.</b> Every <i>original</i> vertex is within the tolerance of the simplified
    /// result. The neighbour-walk version passes a vertex-count test and fails this one, because
    /// its error accumulates along a gently curving run.
    /// </summary>
    /// <param name="tolerance">The tolerance asked for.</param>
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    public void EveryOriginalVertexStaysWithinToleranceOfTheResult(double tolerance)
    {
        PolyLine arc = FinelySampledArc(400, 10.0);
        Tolerance asked = new(tolerance, Angle.FromDegrees(1), 1e-12);

        PolyLine simplified = Assert.IsType<PolyLine>(arc.Simplified(asked));

        Assert.True(
            simplified.Points().Length < arc.Points().Length,
            "nothing was removed, so this fixture proves nothing.");

        foreach (Point3d original in arc.Points())
        {
            double away = original.DistanceTo(simplified.ClosestPoint(original));

            Assert.True(
                away <= tolerance + 1e-9,
                $"a vertex of the original sits {away} from the simplified curve, which is more "
                + $"than the {tolerance} asked for — the error accumulated along the run.");
        }
    }

    /// <summary>A tighter tolerance keeps more vertices, which is the knob doing its job.</summary>
    [Fact]
    public void ATighterToleranceKeepsMoreVertices()
    {
        PolyLine arc = FinelySampledArc(400, 10.0);

        int tight = Kept(arc, 0.001);
        int middling = Kept(arc, 0.05);
        int loose = Kept(arc, 0.5);

        Assert.True(tight > middling, $"{tight} kept at 0.001 and {middling} at 0.05.");
        Assert.True(middling > loose, $"{middling} kept at 0.05 and {loose} at 0.5.");
    }

    /// <summary>A closed polyline keeps its seam, so simplifying cannot open the loop.</summary>
    [Fact]
    public void AClosedPolylineStaysClosed()
    {
        PolyLine ring = new(Plane.WorldXY, 6.0, 64);

        Assert.True(ring.IsClosed, "the fixture is not closed.");

        PolyLine simplified = Assert.IsType<PolyLine>(ring.Simplified(Loose));

        Assert.True(simplified.IsClosed, "simplifying opened the loop.");
        Assert.True(
            simplified.Points().Length < ring.Points().Length,
            "nothing was removed, so this fixture proves nothing.");
    }

    /// <summary>A polyline with nothing to remove comes back as itself.</summary>
    [Fact]
    public void APolylineWithNothingToRemoveIsUnchanged()
    {
        PolyLine zigzag = new(
            [
                new Point3d(0, 0, 0),
                new Point3d(1, 5, 0),
                new Point3d(2, 0, 0),
                new Point3d(3, 5, 0),
            ]);

        Assert.Same(zigzag, zigzag.Simplified(Loose));
    }

    /// <summary>A polycurve simplifies its segments and keeps its joints.</summary>
    [Fact]
    public void APolyCurveSimplifiesItsSegments()
    {
        NurbsCurve straight = StraightThroughManyPoints();
        Curve second = new Line(new Point3d(10, 0, 0), new Point3d(10, 6, 0));

        PolyCurve chain = PolyCurve.FromJoinedCurves([straight, second]);

        PolyCurve simplified = Assert.IsType<PolyCurve>(chain.Simplified(Loose));

        // Still two segments: nothing simplifies ACROSS a joint.
        Assert.Equal(2, simplified.SegmentCount);
        Assert.Equal(chain.Length, simplified.Length, 1e-6);

        NurbsCurve first = Assert.IsType<NurbsCurve>(simplified.SegmentAt(0));
        Assert.True(
            first.ControlPoints().Length < straight.ControlPoints().Length,
            "the segment kept all its control points.");
    }

    /// <summary>A polycurve whose segments are all minimal comes back as itself.</summary>
    [Fact]
    public void APolyCurveWithNothingToRemoveIsUnchanged()
    {
        PolyCurve chain = PolyCurve.FromJoinedCurves(
            [
                new Line(new Point3d(0, 0, 0), new Point3d(10, 0, 0)),
                new Line(new Point3d(10, 0, 0), new Point3d(10, 6, 0)),
            ]);

        Assert.Same(chain, chain.Simplified(Loose));
    }

    /// <summary>How many vertices survive at a tolerance.</summary>
    /// <param name="polyline">The polyline.</param>
    /// <param name="tolerance">The tolerance.</param>
    /// <returns>The vertex count of the result.</returns>
    private static int Kept(PolyLine polyline, double tolerance) =>
        ((PolyLine)polyline.Simplified(new Tolerance(tolerance, Angle.FromDegrees(1), 1e-12)))
        .Points().Length;

    /// <summary>
    /// A straight run interpolated through eleven collinear points, so that it carries interior
    /// knots which describe nothing.
    /// </summary>
    /// <returns>The curve.</returns>
    private static NurbsCurve StraightThroughManyPoints()
    {
        List<Point3d> points = [];

        for (int index = 0; index <= 10; index++)
        {
            points.Add(new Point3d(index, 0.0, 0.0));
        }

        return NurbsCurve.InterpolatePoints(points);
    }

    /// <summary>A quarter circle sampled into many short straight runs.</summary>
    /// <param name="samples">How many segments.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The polyline.</returns>
    private static PolyLine FinelySampledArc(int samples, double radius)
    {
        List<Point3d> points = [];

        for (int index = 0; index <= samples; index++)
        {
            double angle = Math.PI / 2.0 * index / samples;
            points.Add(new Point3d(radius * Math.Cos(angle), radius * Math.Sin(angle), 0.0));
        }

        return new PolyLine(points);
    }
}

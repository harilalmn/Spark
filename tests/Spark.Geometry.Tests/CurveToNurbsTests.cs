using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Curve.ToNurbsCurve(in Tolerance)"/> — `E2-T71` step A.
/// </summary>
/// <remarks>
/// <para>
/// <b>An exact conversion is asserted <i>as exact</i>, not as within a tolerance.</b> A
/// tolerance-sized assertion passes just as happily for a conversion that is merely a very good
/// approximation, and the whole point of <see cref="NurbsConversion.IsExact"/> is that those two
/// are different answers. So the degree-1 conversions here are compared bit for bit.
/// </para>
/// <para>
/// <b>And agreement is *as a set of points*, not point against point at equal parameters.</b> An
/// exact conversion traces the same curve; above degree 1 it does not visit it at the same
/// parameters, because a rational quadratic walks a circular arc by a projective function of the
/// angle. <see cref="SurfaceConversion"/>'s remarks say this at length for surfaces and it is the
/// same fact here. Degree 1 is the exception — <see cref="Line"/> and <see cref="PolyLine"/> do
/// preserve the parameterisation — and that is pinned below rather than assumed.
/// </para>
/// </remarks>
public sealed class CurveToNurbsTests
{
    private const double Tight = 1e-9;

    [Fact]
    public void ALineConvertsExactlyAndKeepsItsParameterisation()
    {
        Line line = new(new Point3d(1.0, 2.0, 3.0), new Point3d(4.0, 6.0, 15.0));
        NurbsConversion converted = line.ToNurbsCurve();

        Assert.True(converted.IsExact);
        Assert.Equal(1, converted.Curve.Degree);
        Assert.Equal(line.Domain, converted.Curve.Domain);
        Assert.False(converted.Curve.IsRational);

        // Bit for bit at every parameter, not within a tolerance: a degree-1 B-spline over two
        // clamped points IS the linear interpolation a Line already computes.
        for (int index = 0; index <= 16; index++)
        {
            double parameter = index / 16.0;
            Assert.Equal(line.PointAt(parameter), converted.Curve.PointAt(parameter));
        }
    }

    [Fact]
    public void APolyLineConvertsExactlyAndItsVerticesStayAtTheWholeNumbers()
    {
        PolyLine path = PolyLine.FromPoints(
        [
            Point3d.Origin,
            new Point3d(3.0, 0.0, 0.0),
            new Point3d(3.0, 4.0, 0.0),
            new Point3d(3.0, 4.0, 12.0),
        ]);

        NurbsConversion converted = path.ToNurbsCurve();

        Assert.True(converted.IsExact);
        Assert.Equal(1, converted.Curve.Degree);
        Assert.Equal(path.Domain, converted.Curve.Domain);

        // A polyline's domain is one unit per segment, so the whole numbers are its vertices — and
        // the converted curve has to agree about that, not merely about the set of points.
        for (int index = 0; index <= 30; index++)
        {
            double parameter = path.Domain.Denormalise(index / 30.0);
            Assert.Equal(path.PointAt(parameter), converted.Curve.PointAt(parameter));
        }

        Assert.Equal(path.Length, converted.Curve.Length, Tight);
    }

    [Fact]
    public void AClosedPolyLineSurvivesTheConversionAsAClosedCurve()
    {
        PolyLine hexagon = PolyLine.FromRegularPolygon(Plane.WorldXY, 2.0, 6);
        NurbsConversion converted = hexagon.ToNurbsCurve();

        Assert.True(converted.IsExact);
        Assert.True(hexagon.IsClosed);
        Assert.Equal(hexagon.StartPoint, converted.Curve.StartPoint);
        Assert.Equal(hexagon.EndPoint, converted.Curve.EndPoint);
        Assert.Equal(hexagon.Length, converted.Curve.Length, Tight);
    }

    [Fact]
    public void ANurbsCurveConvertsToItself()
    {
        NurbsCurve curve = new(
            3,
            [
                new Point3d(0, 0, 0),
                new Point3d(1, 4, 1),
                new Point3d(4, 5, -1),
                new Point3d(7, 1, 2),
                new Point3d(9, 2, 0),
            ],
            [0, 0, 0, 0, 0.5, 1, 1, 1, 1],
            [1.0, 2.0, 0.5, 1.5, 1.0]);

        NurbsConversion converted = curve.ToNurbsCurve();

        // The same object, and exact. Left to the base's fallback this would have been sampled and
        // interpolated — an approximation of the very thing being asked for, reported as one.
        Assert.True(converted.IsExact);
        Assert.Same(curve, converted.Curve);
    }

    [Fact]
    public void ACircleConvertsExactlyAsFourRationalSpans()
    {
        Circle circle = Circle.FromCenterNormalRadius(
            new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 1.0, 1.0), 2.5);

        NurbsConversion converted = circle.ToNurbsCurve();

        Assert.True(converted.IsExact);
        Assert.Equal(2, converted.Curve.Degree);
        Assert.True(converted.Curve.IsRational);

        // Nine, not three: the rational form is valid only to a half turn, so a full circle is
        // four spans. Three control points would be a curve that agrees at the ends of a half turn
        // and is not a circle anywhere between them.
        Assert.Equal(9, converted.Curve.Knots.ControlPointCount);
        Assert.Equal(circle.Domain, converted.Curve.Domain);

        // Exact means exact: every sampled point is ON the circle to within a few bits, not within
        // a tolerance. Distance rather than point-for-point, because above degree 1 the
        // parameterisation is NOT preserved and a positional comparison would fail on a correct
        // conversion.
        AssertOnCurve(circle, converted.Curve);
        Assert.Equal(circle.Length, converted.Curve.Length, 1e-9);
    }

    [Fact]
    public void TheWeightIsCosOfHalfTheSpanAndTheMiddleOfEachSpanIsWhatProvesIt()
    {
        // `E2-T71` step B: the branch this proves is the weight. cos(θ/2) rather than cos θ, and
        // the corner point at r/cos(θ/2) rather than at the arc's midpoint — both errors give a
        // curve through the right END points that bulges wrongly in between, so a test that
        // sampled only the span boundaries would pass on either of them.
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 4.0);
        NurbsCurve converted = circle.ToNurbsCurve().Curve;

        // The middles of the four spans, in the converted curve's own parameter space.
        for (int span = 0; span < 4; span++)
        {
            double parameter = circle.Domain.Denormalise((span + 0.5) / 4.0);
            Point3d point = converted.PointAt(parameter);

            Assert.Equal(4.0, point.DistanceTo(Point3d.Origin), 1e-12);
        }

        // And the control polygon really does reach outside the circle, which is the other half of
        // the same fact: a corner point ON the circle would be the midpoint error.
        Point3d[] controlPoints = converted.ControlPoints();
        Assert.Equal(4.0 / Math.Cos(Math.PI / 4.0), controlPoints[1].DistanceTo(Point3d.Origin), 1e-12);
        Assert.Equal(Math.Cos(Math.PI / 4.0), converted.Weights()[1], 1e-12);
    }

    [Fact]
    public void AnArcConvertsExactlyAndKeepsItsOwnDomain()
    {
        Arc arc = Arc.FromPlaneRadiusAngles(
            Plane.WorldYZ, 3.0, Angle.FromDegrees(37.0), Angle.FromDegrees(220.0));

        NurbsConversion converted = arc.ToNurbsCurve();

        Assert.True(converted.IsExact);
        Assert.Equal(2, converted.Curve.Degree);

        // An arc is parameterised from its OWN start, not from the plane's x axis, so the knots are
        // slid back by the start angle. Get that wrong and the curve is the right shape sitting at
        // the wrong parameters, which nothing but the domain would notice.
        Assert.Equal(0.0, converted.Curve.Domain.Min, 1e-12);
        Assert.Equal(arc.SweepAngle.Radians, converted.Curve.Domain.Max, 1e-12);

        Assert.Equal(arc.StartPoint, converted.Curve.StartPoint);
        Assert.Equal(arc.EndPoint.X, converted.Curve.EndPoint.X, 1e-12);
        Assert.Equal(arc.EndPoint.Y, converted.Curve.EndPoint.Y, 1e-12);
        Assert.Equal(arc.EndPoint.Z, converted.Curve.EndPoint.Z, 1e-12);

        AssertOnCurve(arc, converted.Curve);
        Assert.Equal(arc.Length, converted.Curve.Length, 1e-9);
    }

    [Fact]
    public void AnEllipseConvertsExactlyThroughTheSameConstructionAsTheCircle()
    {
        // An ellipse is an affine image of a circle and an affine map carries a rational B-spline
        // to a rational B-spline, so this shares every line of the circle's construction. What the
        // test has to check is that the two radii went to the two axes and not both to one.
        EllipseCurve ellipse = EllipseCurve.FromPlaneRadiiAngles(
            Plane.WorldXY, 5.0, 2.0, Angle.Zero, Angle.FromDegrees(300.0));

        NurbsConversion converted = ellipse.ToNurbsCurve();

        Assert.True(converted.IsExact);
        Assert.Equal(2, converted.Curve.Degree);
        Assert.Equal(ellipse.Domain.Max, converted.Curve.Domain.Max, 1e-12);

        AssertOnCurve(ellipse, converted.Curve);

        // A construction that used one radius for both axes would pass every circle test above and
        // has to fail here. Stated as the ELLIPSE'S OWN EQUATION rather than by looking for the
        // ends of the axes: (x/a)² + (y/b)² = 1 names both radii and separates them, and needs no
        // parameter to evaluate at — which matters, because the parameterisation is not preserved
        // and there is no parameter on the converted curve that can be relied on to be an axis end.
        for (int index = 0; index <= 400; index++)
        {
            Point3d point = converted.Curve.PointAt(converted.Curve.Domain.Denormalise(index / 400.0));

            Assert.Equal(1.0, (point.X * point.X / 25.0) + (point.Y * point.Y / 4.0), 1e-11);
            Assert.Equal(0.0, point.Z, 1e-12);
        }
    }

    [Fact]
    public void AConvertedCircleIsNotTheSameParameterisation()
    {
        // Stated as a test rather than left in a remark, because it is the thing people assume.
        // The two curves are the same SET of points and visit it differently.
        Circle circle = Circle.FromCenterRadius(Point3d.Origin, 1.0);
        NurbsCurve converted = circle.ToNurbsCurve().Curve;

        // NOT an eighth of the way round, which is where the first draft of this test looked and
        // where the two DO agree: an eighth is the middle of the first of four spans, and a knot
        // span's midpoint maps to its arc's midpoint by symmetry. The disagreement is everywhere
        // else, and a tenth of the way round is inside the first span and not at its centre.
        double tenth = circle.Domain.Denormalise(0.1);

        Assert.True(
            circle.PointAt(tenth).DistanceTo(converted.PointAt(tenth)) > 1e-3,
            "the parameterisations agreed, which would mean the conversion is not the rational one.");

        // The places they do agree are worth pinning too, because they are a fact about the
        // construction rather than a coincidence: the ends of every span, and every span's middle.
        for (int index = 0; index <= 8; index++)
        {
            double parameter = circle.Domain.Denormalise(index / 8.0);

            Assert.Equal(
                0.0, circle.PointAt(parameter).DistanceTo(converted.PointAt(parameter)), 1e-12);
        }
    }

    [Fact]
    public void AHelixIsConvertedApproximatelyAndSaysSo()
    {
        // `E2-T71`: the branch this proves is `IsExact` itself. Make the fallback report `true` and
        // this fails — without it the flag is decoration, because every other test here asserts it
        // on a path that genuinely is exact.
        Helix helix = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FromRadians(3.0 * Math.PI * 2.0));

        NurbsConversion converted = helix.ToNurbsCurve(new Tolerance(1e-3, Angle.FromDegrees(0.001), 1e-12));

        Assert.False(converted.IsExact);

        // And it is a good approximation, which is a different claim and is measured rather than
        // asserted: every sample of the NURBS is within the sag of the helix.
        foreach (Point3d point in converted.Curve.Tessellate(new Tolerance(1e-3, Angle.FromDegrees(0.001), 1e-12)))
        {
            Assert.True(helix.DistanceTo(point) < 1e-2, $"{point} strayed {helix.DistanceTo(point)} from the helix.");
        }
    }

    [Fact]
    public void TighteningTheToleranceBringsTheApproximationCloser()
    {
        Helix helix = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(2.0, 0.0, 0.0), 3.0, Angle.FromRadians(3.0 * Math.PI * 2.0));

        double coarse = Worst(helix, helix.ToNurbsCurve(new Tolerance(1e-1, Angle.FromDegrees(1.0), 1e-12)).Curve);
        double fine = Worst(helix, helix.ToNurbsCurve(new Tolerance(1e-4, Angle.FromDegrees(0.001), 1e-12)).Curve);

        Assert.True(fine < coarse, $"tightening the tolerance made it worse: {coarse} then {fine}.");
        Assert.True(fine < 1e-3, $"the fine conversion strayed {fine}.");
    }

    [Fact]
    public void APolyCurveIsConvertedApproximatelyForNowAndSaysSo()
    {
        // A polycurve of lines IS exactly convertible in principle; joining the segments means
        // merging their knot vectors, which is not written. The flag is honest in the meantime,
        // which is the difference between a gap and a silent wrong answer.
        PolyCurve chain = PolyCurve.FromJoinedCurves(
        [
            new Line(Point3d.Origin, new Point3d(3.0, 0.0, 0.0)),
            new Line(new Point3d(3.0, 0.0, 0.0), new Point3d(3.0, 4.0, 0.0)),
        ]);

        NurbsConversion converted = chain.ToNurbsCurve();

        Assert.False(converted.IsExact);
        Assert.Equal(chain.StartPoint, converted.Curve.StartPoint);
        Assert.Equal(chain.EndPoint, converted.Curve.EndPoint);
    }

    [Fact]
    public void TheResultSaysWhichAnswerItGave()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        Helix helix = Helix.FromAxis(
            Point3d.Origin, Vector3d.ZAxis, new Point3d(1.0, 0.0, 0.0), 1.0, Angle.FullTurn);

        Assert.Contains("exact", line.ToNurbsCurve().ToString(), StringComparison.Ordinal);
        Assert.Contains("approximate", helix.ToNurbsCurve().ToString(), StringComparison.Ordinal);
    }

    /// <summary>Asserts that every sampled point of one curve lies on the other, exactly.</summary>
    /// <param name="original">The curve the points must lie on.</param>
    /// <param name="converted">The curve to sample.</param>
    private static void AssertOnCurve(Curve original, Curve converted)
    {
        for (int index = 0; index <= 200; index++)
        {
            Point3d point = converted.PointAt(converted.Domain.Denormalise(index / 200.0));
            double strayed = original.DistanceTo(point);

            Assert.True(strayed < 1e-11, $"a converted point strayed {strayed}, which is not exact.");
        }
    }

    /// <summary>The worst distance from a sampling of one curve to the other.</summary>
    /// <param name="original">The curve to measure against.</param>
    /// <param name="converted">The curve to sample.</param>
    /// <returns>The largest distance found.</returns>
    private static double Worst(Curve original, Curve converted)
    {
        double worst = 0.0;

        for (int index = 0; index <= 500; index++)
        {
            Point3d point = converted.PointAt(converted.Domain.Denormalise(index / 500.0));
            worst = Math.Max(worst, original.DistanceTo(point));
        }

        return worst;
    }
}

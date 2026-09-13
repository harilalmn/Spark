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

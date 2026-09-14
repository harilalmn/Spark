using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Curve.PulledOntoPlane"/> — `E2-T71`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is affine against linear.</b> An orthogonal projection onto a plane through the
/// <i>origin</i> is a linear map, and one onto any other plane is affine — it has a translation in
/// it. An implementation that keeps only the linear part lands the curve on the parallel plane
/// through the origin: the right shape in the wrong place. So no fixture here uses a plane through
/// the origin, because such a fixture cannot tell the two apart.
/// </para>
/// <para>
/// <b>The hand-computable anchor is a circle pulled onto a tilted plane</b>, which is an ellipse
/// whose major axis is the circle's diameter and whose minor axis is that times the cosine of the
/// tilt.
/// </para>
/// </remarks>
public sealed class CurvePullOntoPlaneTests
{
    /// <summary>
    /// <b>The branch.</b> Every point of the result lies on the plane it was pulled onto — not on
    /// the parallel one through the origin.
    /// </summary>
    /// <param name="offset">How far the plane sits from the origin along its normal.</param>
    [Theory]
    [InlineData(5.0)]
    [InlineData(-3.0)]
    [InlineData(0.25)]
    public void EveryPointOfTheResultLiesOnTheOffsetPlane(double offset)
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(0, 0, offset), Vector3d.ZAxis);
        Circle circle = new(Plane.FromOriginNormal(new Point3d(1, 2, 9), Vector3d.ZAxis), 4.0);

        Curve pulled = circle.PulledOntoPlane(plane);

        for (int sample = 0; sample <= 64; sample++)
        {
            Point3d point = pulled.PointAt(pulled.Domain.Denormalise(sample / 64.0));

            Assert.True(
                Math.Abs(plane.DistanceTo(point)) < 1e-9,
                $"the sample is {plane.DistanceTo(point)} off the plane — it landed on the "
                + "parallel plane through the origin.");
        }
    }

    /// <summary>And on a tilted plane offset from the origin, where both parts of the map matter.</summary>
    [Fact]
    public void TheSameOnATiltedOffsetPlane()
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(2, -1, 4), new Vector3d(1, 1, 1));
        Arc arc = Arc.FromCenterStartEnd(
            new Point3d(0, 0, 8), new Point3d(3, 0, 8), new Point3d(0, 3, 8));

        Curve pulled = arc.PulledOntoPlane(plane);

        for (int sample = 0; sample <= 64; sample++)
        {
            Point3d point = pulled.PointAt(pulled.Domain.Denormalise(sample / 64.0));

            Assert.True(Math.Abs(plane.DistanceTo(point)) < 1e-9, $"{plane.DistanceTo(point)} off.");
        }
    }

    /// <summary>
    /// <b>The anchor.</b> A circle of radius four pulled onto a plane tilted by sixty degrees is an
    /// ellipse: eight across the untilted way and eight times cos 60° the other.
    /// </summary>
    [Fact]
    public void ACirclePulledOntoATiltedPlaneIsAnEllipseOfTheRightSize()
    {
        const double Radius = 4.0;
        double tilt = Math.PI / 3.0;

        // The plane is tilted about the x axis, so widths along x survive and widths along y are
        // foreshortened by the cosine of the tilt.
        Plane plane = Plane.FromOriginNormal(
            new Point3d(0, 0, 6), new Vector3d(0, Math.Sin(tilt), Math.Cos(tilt)));
        Circle circle = new(Plane.FromOriginNormal(new Point3d(0, 0, 11), Vector3d.ZAxis), Radius);

        Curve pulled = circle.PulledOntoPlane(plane);

        // Measured in the plane's own frame: along its x axis the circle keeps its diameter, and
        // across it the diameter is foreshortened.
        double alongX = Span(pulled, plane.XAxis);
        double alongY = Span(pulled, plane.YAxis);

        Assert.Equal(2.0 * Radius, Math.Max(alongX, alongY), 1e-6);
        Assert.Equal(2.0 * Radius * Math.Cos(tilt), Math.Min(alongX, alongY), 1e-6);
    }

    /// <summary>A curve already lying in the plane comes back where it was.</summary>
    [Fact]
    public void ACurveAlreadyOnThePlaneIsNotMoved()
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(0, 0, 7), Vector3d.ZAxis);
        Line line = new(new Point3d(1, 2, 7), new Point3d(9, -4, 7));

        Curve pulled = line.PulledOntoPlane(plane);

        Assert.True(pulled.StartPoint.DistanceTo(line.StartPoint) < 1e-12, $"{pulled.StartPoint}");
        Assert.True(pulled.EndPoint.DistanceTo(line.EndPoint) < 1e-12, $"{pulled.EndPoint}");
        Assert.Equal(line.Length, pulled.Length, 1e-9);
    }

    /// <summary>
    /// A straight line stays straight, which is the simplest thing the affine map has to preserve.
    /// </summary>
    [Fact]
    public void ALineStaysStraight()
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(0, 0, 3), Vector3d.ZAxis);
        Line line = new(new Point3d(0, 0, 10), new Point3d(6, 8, 20));

        Curve pulled = line.PulledOntoPlane(plane);

        Assert.True(
            pulled.StartPoint.DistanceTo(new Point3d(0, 0, 3)) < 1e-12,
            $"it starts at {pulled.StartPoint}.");
        Assert.True(
            pulled.EndPoint.DistanceTo(new Point3d(6, 8, 3)) < 1e-12,
            $"it ends at {pulled.EndPoint}.");
        Assert.Equal(10.0, pulled.Length, 1e-9);
    }

    /// <summary>
    /// A polyline's corners land on the plane and its straight runs stay straight, which a pull
    /// through sampling would only approximate.
    /// </summary>
    [Fact]
    public void APolylinesCornersLandOnThePlane()
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(0, 0, -2), Vector3d.ZAxis);
        PolyLine outline = new(
            [
                new Point3d(0, 0, 4),
                new Point3d(5, 0, 7),
                new Point3d(5, 5, 1),
                new Point3d(0, 5, 9),
            ]);

        Curve pulled = outline.PulledOntoPlane(plane);

        Assert.True(pulled.StartPoint.DistanceTo(new Point3d(0, 0, -2)) < 1e-12);
        Assert.True(pulled.EndPoint.DistanceTo(new Point3d(0, 5, -2)) < 1e-12);

        // The flattened outline is three sides of a five-by-five square.
        Assert.Equal(15.0, pulled.Length, 1e-9);
    }

    /// <summary>
    /// Pulling twice onto the same plane changes nothing the second time, which is what being a
    /// projection means.
    /// </summary>
    [Fact]
    public void PullingTwiceIsPullingOnce()
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(1, 1, 4), new Vector3d(0, 1, 2));
        Circle circle = new(Plane.FromOriginNormal(new Point3d(0, 0, 12), Vector3d.ZAxis), 3.0);

        Curve once = circle.PulledOntoPlane(plane);
        Curve twice = once.PulledOntoPlane(plane);

        for (int sample = 0; sample <= 32; sample++)
        {
            Point3d first = once.PointAt(once.Domain.Denormalise(sample / 32.0));
            Point3d second = twice.PointAt(twice.Domain.Denormalise(sample / 32.0));

            Assert.True(first.DistanceTo(second) < 1e-9, $"{first} became {second}.");
        }
    }

    /// <summary>
    /// <b>A rational curve stays rational, weights and all.</b> That is the partition-of-unity
    /// argument made visible: an affine map commutes with the rational blend only because the
    /// weights are carried across unchanged, so a pull that dropped them would be a different
    /// curve.
    /// </summary>
    /// <remarks>
    /// <b>This is the only test in the file that can see the weights, which was a surprise.</b> The
    /// ellipse-size test above measures the pulled circle's extent, and the extent does not change
    /// when the weights are dropped: on a quarter-circle Bezier the width works out to
    /// <c>r(1 - t^2)</c>, whose extreme is at the endpoint whether the curve is rational or not. A
    /// bounding measurement is a weaker assertion than it looks, and the mutation is what said so.
    /// </remarks>
    [Fact]
    public void ARationalCurveKeepsItsWeights()
    {
        Plane plane = Plane.FromOriginNormal(new Point3d(0, 0, 2), Vector3d.ZAxis);
        Circle circle = new(Plane.FromOriginNormal(new Point3d(0, 0, 9), Vector3d.ZAxis), 5.0);

        NurbsCurve source = circle.ToNurbsCurve().Curve;

        Assert.True(source.IsRational, "a circle converts to a rational curve; this fixture is wrong.");

        NurbsCurve pulled = Assert.IsType<NurbsCurve>(circle.PulledOntoPlane(plane));

        Assert.True(pulled.IsRational, "the pulled curve lost its weights.");
        Assert.Equal(source.Weights(), pulled.Weights());

        // And the shape survives: pulled straight down, a circle stays a circle of radius five.
        for (int sample = 0; sample <= 64; sample++)
        {
            Point3d point = pulled.PointAt(pulled.Domain.Denormalise(sample / 64.0));

            Assert.Equal(5.0, point.DistanceTo(new Point3d(0, 0, 2)), 1e-9);
        }
    }

    /// <summary>How far a curve reaches along a direction, sampled.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="direction">The direction to measure along.</param>
    /// <returns>The span between its extremes.</returns>
    private static double Span(Curve curve, in Vector3d direction)
    {
        double least = double.MaxValue;
        double most = double.MinValue;

        for (int sample = 0; sample <= 2000; sample++)
        {
            Point3d point = curve.PointAt(curve.Domain.Denormalise(sample / 2000.0));
            double along = (point - Point3d.Origin).Dot(direction);

            least = Math.Min(least, along);
            most = Math.Max(most, along);
        }

        return most - least;
    }
}

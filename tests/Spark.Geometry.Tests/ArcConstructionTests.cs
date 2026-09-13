using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Arc.FromCenterStartEnd"/> and <see cref="Arc.FromStartEndStartTangent"/> — `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every arc is checked against its own definition</b> — every sampled point at the radius from
/// the centre — because that is the property neither constructor supplies and the one that fails
/// when the reduction to an existing factory is wrong.
/// </para>
/// <para>
/// <b>The two have different branches.</b> The centre form is over-determined, so what it does with
/// an end point at the wrong radius is the claim. The tangent form is well posed, so the claim is
/// the tangent itself: an implementation that merely passed through both points would satisfy every
/// assertion about position.
/// </para>
/// </remarks>
public sealed class ArcConstructionTests
{
    private const double Loose = 1e-9;

    /// <summary>A quarter circle from centre, start and end, with the end already at the radius.</summary>
    [Fact]
    public void AQuarterCircleFromCenterStartAndEnd()
    {
        Arc arc = Arc.FromCenterStartEnd(
            Point3d.Origin, new Point3d(2, 0, 0), new Point3d(0, 2, 0));

        Assert.Equal(2.0, arc.Radius, Loose);
        Assert.Equal(Math.PI / 2.0, arc.SweepAngle.Radians, Loose);
        Assert.True(arc.StartPoint.DistanceTo(new Point3d(2, 0, 0)) < Loose);
        Assert.True(arc.EndPoint.DistanceTo(new Point3d(0, 2, 0)) < Loose);
        AssertOnItsOwnCircle(arc);
    }

    /// <summary>
    /// <b>The branch.</b> An end point at the wrong radius gives an arc ending on the <i>ray</i>
    /// towards it, at the start's radius — which is what over-determined means here, and what an
    /// implementation using the end point directly would get wrong.
    /// </summary>
    [Fact]
    public void TheEndPointGivesADirectionAndNotADistance()
    {
        // The end is at distance 7 from the centre; the start is at 2.
        Arc arc = Arc.FromCenterStartEnd(
            Point3d.Origin, new Point3d(2, 0, 0), new Point3d(0, 7, 0));

        Assert.Equal(2.0, arc.Radius, Loose);
        Assert.True(
            arc.EndPoint.DistanceTo(new Point3d(0, 2, 0)) < Loose,
            $"the arc ends at {arc.EndPoint}, not on the ray towards the end point at the start's radius.");
        AssertOnItsOwnCircle(arc);
    }

    /// <summary>The sweep is the shorter way round, at several angles.</summary>
    [Theory]
    [InlineData(0.4)]
    [InlineData(1.5)]
    [InlineData(3.0)]
    public void TheSweepIsTheShorterWayRound(double angle)
    {
        Point3d end = new(3.0 * Math.Cos(angle), 3.0 * Math.Sin(angle), 0.0);

        Arc arc = Arc.FromCenterStartEnd(Point3d.Origin, new Point3d(3, 0, 0), end);

        Assert.Equal(angle, arc.SweepAngle.Radians, 1e-9);
        Assert.True(arc.SweepAngle.Radians <= Math.PI + 1e-12, "the minor arc is never more than a half turn.");
    }

    /// <summary>It works off the world planes too, which is where a wrong normal would show.</summary>
    [Fact]
    public void ItWorksInATiltedPlane()
    {
        Point3d centre = new(1, 2, 3);
        Point3d start = new(1, 2, 3);

        Arc arc = Arc.FromCenterStartEnd(centre, start + new Vector3d(1, 1, 0), start + new Vector3d(0, 1, 1));

        AssertOnItsOwnCircle(arc);
        Assert.Equal(Math.Sqrt(2.0), arc.Radius, Loose);
    }

    [Fact]
    public void TheCenterFormRefusesItsDegeneracies()
    {
        Assert.Throws<ArgumentException>(
            () => Arc.FromCenterStartEnd(Point3d.Origin, Point3d.Origin, new Point3d(1, 0, 0)));
        Assert.Throws<ArgumentException>(
            () => Arc.FromCenterStartEnd(Point3d.Origin, new Point3d(1, 0, 0), Point3d.Origin));

        // Collinear: the end lies on the same ray as the start, so there is no plane.
        Assert.Throws<ArgumentException>(
            () => Arc.FromCenterStartEnd(Point3d.Origin, new Point3d(1, 0, 0), new Point3d(3, 0, 0)));
    }

    /// <summary>
    /// <b>The tangent form's branch.</b> The arc leaves the start in the direction given — which an
    /// implementation that merely passed through both points would not.
    /// </summary>
    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, -1.0, 0.0)]
    [InlineData(0.3, 1.0, 0.0)]
    [InlineData(0.0, 1.0, 0.5)]
    public void TheArcLeavesTheStartInTheDirectionGiven(double x, double y, double z)
    {
        Point3d start = new(0, 0, 0);
        Point3d end = new(4, 0, 0);
        Vector3d tangent = new Vector3d(x, y, z).Normalised();

        Arc arc = Arc.FromStartEndStartTangent(start, end, tangent);

        Vector3d actual = arc.TangentAt(arc.Domain.Min);

        Assert.True(
            (actual - tangent).Length < 1e-9,
            $"the arc sets off in {actual}, not {tangent}.");
    }

    /// <summary>And it passes through the end point exactly, which the centre form does not promise.</summary>
    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, -1.0, 0.0)]
    [InlineData(0.5, 1.0, 0.0)]
    public void TheArcPassesThroughTheEndPointExactly(double x, double y, double z)
    {
        Point3d start = new(1, 2, 0);
        Point3d end = new(5, 2, 0);

        Arc arc = Arc.FromStartEndStartTangent(start, end, new Vector3d(x, y, z));

        Assert.True(arc.StartPoint.DistanceTo(start) < 1e-9, $"the arc starts at {arc.StartPoint}.");
        Assert.True(arc.EndPoint.DistanceTo(end) < 1e-9, $"the arc ends at {arc.EndPoint}, not {end}.");
        AssertOnItsOwnCircle(arc);
    }

    /// <summary>
    /// A tangent perpendicular to the chord gives the semicircle, which is the one case with an
    /// answer known by hand: the radius is half the chord.
    /// </summary>
    [Fact]
    public void APerpendicularTangentGivesTheSemicircle()
    {
        Arc arc = Arc.FromStartEndStartTangent(
            new Point3d(0, 0, 0), new Point3d(6, 0, 0), Vector3d.YAxis);

        Assert.Equal(3.0, arc.Radius, Loose);
        Assert.Equal(Math.PI, arc.SweepAngle.Radians, 1e-9);
        Assert.True(arc.Center.DistanceTo(new Point3d(3, 0, 0)) < Loose, $"the centre is at {arc.Center}.");
    }

    [Fact]
    public void TheTangentFormRefusesItsDegeneracies()
    {
        Point3d start = new(0, 0, 0);
        Point3d end = new(4, 0, 0);

        Assert.Throws<ArgumentException>(() => Arc.FromStartEndStartTangent(start, start, Vector3d.YAxis));
        Assert.Throws<ArgumentException>(() => Arc.FromStartEndStartTangent(start, end, Vector3d.Zero));

        // Along the chord, either way: a straight line, not an arc.
        Assert.Throws<ArgumentException>(() => Arc.FromStartEndStartTangent(start, end, Vector3d.XAxis));
        Assert.Throws<ArgumentException>(() => Arc.FromStartEndStartTangent(start, end, -Vector3d.XAxis));
    }

    /// <summary>Every point of an arc is at its radius from its centre. Sampled, not assumed.</summary>
    private static void AssertOnItsOwnCircle(Arc arc)
    {
        for (int index = 0; index <= 20; index++)
        {
            Point3d point = arc.PointAt(arc.Domain.Denormalise(index / 20.0));

            Assert.Equal(arc.Radius, point.DistanceTo(arc.Center), 1e-9);
        }
    }
}

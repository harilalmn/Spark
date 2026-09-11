using System;
using Spark.Geometry;
using Spark.Geometry.Planar;

namespace Spark.Geometry.Tests;

/// <summary>
/// Offsetting and simplifying a region (`E2-T13`, step B).
/// </summary>
/// <remarks>
/// <b>Areas are the oracle again.</b> A 2 x 2 square grown by one with round corners is itself, four
/// 2 x 1 strips and four quarter circles: 4 + 8 + π. With mitred corners it is a 4 x 4 square: 16.
/// Shrunk by one it has no inside left at all.
/// </remarks>
public sealed class RegionOffsetTests
{
    private static Region Square() => Region.FromClosedPolyLines(
        Plane.WorldXY,
        [
            PolyLine.FromClosedPoints([new Point3d(0, 0, 0), new Point3d(2, 0, 0), new Point3d(2, 2, 0), new Point3d(0, 2, 0)]),
        ]);

    /// <summary><b>The row.</b> Grown with round corners, the square gains its strips and a whole circle's worth of corner.</summary>
    [Fact]
    public void GrowingWithRoundCornersAddsAStripAndAQuarterCircleAtEach()
    {
        Assert.Equal(4.0 + 8.0 + Math.PI, Square().Offset(1.0).Area, 3);
    }

    /// <summary>Grown with mitred corners, a square stays a square.</summary>
    [Fact]
    public void GrowingWithMitredCornersKeepsASquareSquare()
    {
        Assert.Equal(16.0, Square().Offset(1.0, RegionJoin.Miter).Area, 6);
    }

    /// <summary>Shrunk by half its width, a square has nothing left inside.</summary>
    [Fact]
    public void ShrinkingPastTheMiddleLeavesNothing()
    {
        Region gone = Square().Offset(-1.0);

        Assert.True(gone.IsEmpty);
        Assert.Equal(0.0, gone.Area);
    }

    /// <summary>
    /// A vertex a hair off a straight edge is removed by simplifying, and the area barely moves. It is
    /// a hair off rather than exactly on, because making the region already removes exact collinear
    /// points, and a test of simplify has to give it something to do.
    /// </summary>
    [Fact]
    public void SimplifyingRemovesAVertexThatBarelyBendsAnEdge()
    {
        Region bent = Region.FromClosedPolyLines(
            Plane.WorldXY,
            [
                PolyLine.FromClosedPoints(
                [
                    new Point3d(0, 0, 0),
                    new Point3d(1, -0.001, 0),
                    new Point3d(2, 0, 0),
                    new Point3d(2, 2, 0),
                    new Point3d(0, 2, 0),
                ]),
            ]);

        Assert.Equal(6, bent.ToPolyLines()[0].PointCount);

        Region simplified = bent.Simplify(0.01);

        Assert.Equal(5, simplified.ToPolyLines()[0].PointCount);
        Assert.Equal(4.0, simplified.Area, 6);
    }

    /// <summary>
    /// <b>`E2-T14`'s centroid.</b> An L of a 2 x 2 square and a 2 x 1 strip balances at the area-weighted
    /// mean of their centres: (4·1 + 2·3) / 6 across and (4·1 + 2·0.5) / 6 up.
    /// </summary>
    [Fact]
    public void AnLShapeBalancesWhereItsPartsWeighIt()
    {
        Region l = Region.FromClosedPolyLines(
            Plane.WorldXY,
            [
                PolyLine.FromClosedPoints(
                [
                    new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(4, 1, 0),
                    new Point3d(2, 1, 0), new Point3d(2, 2, 0), new Point3d(0, 2, 0),
                ]),
            ]);

        Point3d centre = l.Centroid();

        Assert.Equal(10.0 / 6.0, centre.X, 9);
        Assert.Equal(5.0 / 6.0, centre.Y, 9);
    }

    /// <summary>
    /// A hole pulls the centre away from itself: a 4 x 4 square centred on (2, 2) with a unit hole centred
    /// on (1, 1) balances at (16·2 − 1·1) / 15 on each axis.
    /// </summary>
    [Fact]
    public void AHolePullsTheCentreAwayFromItself()
    {
        Region pierced = Region.FromClosedPolyLines(
            Plane.WorldXY,
            [
                PolyLine.FromClosedPoints([new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(4, 4, 0), new Point3d(0, 4, 0)]),
                PolyLine.FromClosedPoints([new Point3d(0.5, 0.5, 0), new Point3d(1.5, 0.5, 0), new Point3d(1.5, 1.5, 0), new Point3d(0.5, 1.5, 0)]),
            ]);

        Point3d centre = pierced.Centroid();

        Assert.Equal(31.0 / 15.0, centre.X, 9);
        Assert.Equal(31.0 / 15.0, centre.Y, 9);
    }

    /// <summary>An empty region has no centre, and says so.</summary>
    [Fact]
    public void AnEmptyRegionHasNoCentroid() =>
        Assert.Throws<InvalidOperationException>(() => Square().Offset(-1.0).Centroid());

    /// <summary>Nonsense distances and limits are refused by name.</summary>
    [Fact]
    public void NonsenseIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Square().Offset(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Square().Offset(1.0, RegionJoin.Miter, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Square().Simplify(-1.0));
    }
}

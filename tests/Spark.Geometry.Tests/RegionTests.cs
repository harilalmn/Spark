using System;
using Spark.Geometry;
using Spark.Geometry.Planar;

namespace Spark.Geometry.Tests;

/// <summary>
/// Regions in a plane and the booleans between them (`E2-T13`, PRD FR-60).
/// </summary>
/// <remarks>
/// <b>Areas are the oracle.</b> Two 2 x 2 squares overlapping in a 1 x 1 corner have a union of 7, an
/// intersection of 1, a difference of 3 and a symmetric difference of 6 - numbers no implementation
/// can reach by accident, and each boolean has its own.
/// </remarks>
public sealed class RegionTests
{
    private static PolyLine Square(double x, double y, double size, double z = 0.0) => PolyLine.FromClosedPoints(
    [
        new Point3d(x, y, z),
        new Point3d(x + size, y, z),
        new Point3d(x + size, y + size, z),
        new Point3d(x, y + size, z),
    ]);

    private static Region Of(params PolyLine[] loops) => Region.FromClosedPolyLines(Plane.WorldXY, loops);

    /// <summary><b>The row.</b> Each boolean of two overlapping squares has its own area.</summary>
    [Fact]
    public void EachBooleanHasTheAreaItShould()
    {
        Region a = Of(Square(0, 0, 2));
        Region b = Of(Square(1, 1, 2));

        Assert.Equal(7.0, a.Union(b).Area, 6);
        Assert.Equal(1.0, a.Intersection(b).Area, 6);
        Assert.Equal(3.0, a.Difference(b).Area, 6);
        Assert.Equal(6.0, a.SymmetricDifference(b).Area, 6);
    }

    /// <summary>
    /// <b>A loop inside another is a hole</b>, whichever way round it was drawn: a 4 x 4 square with a
    /// 2 x 2 square in its middle encloses 12, holds a point in the ring and not one in the hole.
    /// </summary>
    [Fact]
    public void ALoopInsideAnotherIsAHole()
    {
        Region washer = Of(Square(0, 0, 4), Square(1, 1, 2));

        Assert.Equal(12.0, washer.Area, 6);
        Assert.Equal(2, washer.LoopCount);
        Assert.True(washer.Contains(new Point3d(0.5, 0.5, 0)));
        Assert.False(washer.Contains(new Point3d(2, 2, 0)));
        Assert.False(washer.Contains(new Point3d(5, 5, 0)));
    }

    /// <summary>A point off the region's plane is not in it, however it projects.</summary>
    [Fact]
    public void APointOffThePlaneIsNotInTheRegion() =>
        Assert.False(Of(Square(0, 0, 2)).Contains(new Point3d(1, 1, 0.5)));

    /// <summary>A region's own polylines make the same region again.</summary>
    [Fact]
    public void ItsPolylinesMakeItAgain()
    {
        Region washer = Of(Square(0, 0, 4), Square(1, 1, 2));
        Region again = Region.FromClosedPolyLines(Plane.WorldXY, washer.ToPolyLines());

        Assert.Equal(washer.Area, again.Area, 9);
        Assert.Equal(washer.LoopCount, again.LoopCount);
    }

    /// <summary>
    /// <b>Coplanar is enough.</b> A region in the same plane with its axes turned combines with one in
    /// the world frame exactly as if they shared it.
    /// </summary>
    [Fact]
    public void ACoplanarRegionInATurnedFrameCombines()
    {
        Region a = Of(Square(0, 0, 2));
        Plane turned = Plane.FromOriginXAxisYAxis(Point3d.Origin, Vector3d.YAxis, -Vector3d.XAxis);
        Region b = Region.FromClosedPolyLines(turned, [Square(1, 1, 2)]);

        Assert.Equal(7.0, a.Union(b).Area, 6);
        Assert.Equal(1.0, a.Intersection(b).Area, 6);
    }

    /// <summary>Regions in different planes have no boolean, and are refused by name.</summary>
    [Fact]
    public void RegionsInDifferentPlanesAreRefused()
    {
        Region flat = Of(Square(0, 0, 2));
        Region raised = Region.FromClosedPolyLines(
            new Plane(new Point3d(0, 0, 1), Vector3d.ZAxis), [Square(0, 0, 2, z: 1)]);

        Assert.Throws<ArgumentException>(() => flat.Union(raised));
    }

    /// <summary>An open polyline, or one leaving the plane, is not a boundary.</summary>
    [Fact]
    public void ABoundaryMustBeClosedAndInThePlane()
    {
        PolyLine open = PolyLine.FromPoints([new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0)]);

        Assert.Throws<ArgumentException>(() => Of(open));
        Assert.Throws<ArgumentException>(() => Region.FromClosedPolyLines(Plane.WorldXY, [Square(0, 0, 1, z: 0.25)]));
    }
}

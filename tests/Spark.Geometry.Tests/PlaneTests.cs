using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class PlaneTests
{
    [Fact]
    public void TheWorldPlanesHaveTheDocumentedFrames()
    {
        Assert.Equal(Vector3d.ZAxis, Plane.WorldXY.Normal);
        Assert.Equal(Vector3d.XAxis, Plane.WorldXY.XAxis);
        Assert.Equal(Vector3d.YAxis, Plane.WorldXY.YAxis);

        // The world XZ plane keeps the frame right-handed, which puts its normal on -Y.
        Assert.Equal(-Vector3d.YAxis, Plane.WorldXZ.Normal);
        Assert.Equal(Vector3d.XAxis, Plane.WorldYZ.Normal);
    }

    [Fact]
    public void EveryWorldPlaneHasARightHandedFrame()
    {
        AssertRightHanded(Plane.WorldXY);
        AssertRightHanded(Plane.WorldXZ);
        AssertRightHanded(Plane.WorldYZ);
    }

    [Fact]
    public void ADefaultConstructedPlaneIsNotValid()
    {
        Assert.False(default(Plane).IsValid);
        Assert.True(Plane.WorldXY.IsValid);
    }

    [Fact]
    public void APlaneWithAZNormalReproducesTheWorldXYFrameExactly()
    {
        Plane plane = Plane.ByOriginNormal(Point3d.Origin, new Vector3d(0.0, 0.0, 4.0));

        Assert.Equal(Vector3d.XAxis, plane.XAxis);
        Assert.Equal(Vector3d.YAxis, plane.YAxis);
        Assert.Equal(Vector3d.ZAxis, plane.Normal);
    }

    [Fact]
    public void AnArbitraryNormalStillProducesAnOrthonormalRightHandedFrame()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 2.0, 3.0));

        AssertRightHanded(plane);
        Assert.True(plane.Normal.IsUnit());
    }

    [Fact]
    public void ConstructingAPlaneFromADegenerateNormalThrows()
    {
        Assert.Throws<ArgumentException>(() => new Plane(Point3d.Origin, Vector3d.Zero));
        Assert.Throws<ArgumentException>(
            () => new Plane(Point3d.Origin, new Vector3d(double.NaN, 0.0, 1.0)));
        Assert.Throws<ArgumentException>(() => new Plane(Point3d.Unset, Vector3d.ZAxis));
    }

    [Fact]
    public void ByOriginXAxisYAxisKeepsTheXAxisAndOrthogonalisesTheY()
    {
        Plane plane = Plane.ByOriginXAxisYAxis(
            Point3d.Origin,
            Vector3d.XAxis,
            new Vector3d(1.0, 1.0, 0.0));

        Assert.Equal(Vector3d.XAxis, plane.XAxis);
        Assert.True(plane.YAxis.EqualsWithin(Vector3d.YAxis));
        AssertRightHanded(plane);
    }

    [Fact]
    public void ByOriginXAxisYAxisRejectsParallelAxes()
    {
        Assert.Throws<ArgumentException>(
            () => Plane.ByOriginXAxisYAxis(Point3d.Origin, Vector3d.XAxis, Vector3d.XAxis));
    }

    [Fact]
    public void ByThreePointsPutsTheOriginOnTheFirstPointAndTheXAxisTowardsTheSecond()
    {
        Plane plane = Plane.ByThreePoints(
            new Point3d(1.0, 1.0, 0.0),
            new Point3d(3.0, 1.0, 0.0),
            new Point3d(1.0, 5.0, 0.0));

        Assert.Equal(new Point3d(1.0, 1.0, 0.0), plane.Origin);
        Assert.True(plane.XAxis.EqualsWithin(Vector3d.XAxis));
        Assert.True(plane.Normal.EqualsWithin(Vector3d.ZAxis));
    }

    [Fact]
    public void ReversingTwoOfTheThreePointsFlipsTheNormal()
    {
        Point3d a = new(0.0, 0.0, 0.0);
        Point3d b = new(1.0, 0.0, 0.0);
        Point3d c = new(0.0, 1.0, 0.0);

        Assert.True(Plane.ByThreePoints(a, b, c).Normal.EqualsWithin(Vector3d.ZAxis));
        Assert.True(Plane.ByThreePoints(a, c, b).Normal.EqualsWithin(-Vector3d.ZAxis));
    }

    [Fact]
    public void ByThreePointsRejectsCollinearPoints()
    {
        Assert.Throws<ArgumentException>(() => Plane.ByThreePoints(
            new Point3d(0.0, 0.0, 0.0),
            new Point3d(1.0, 0.0, 0.0),
            new Point3d(2.0, 0.0, 0.0)));
    }

    [Fact]
    public void ByThreePointsBlamesAParameterItActuallyHas()
    {
        // It used to forward to ByOriginXAxisYAxis, so a caller who passed three collinear
        // points was told the problem was with "yAxis" - a parameter absent from the
        // signature they called.
        ArgumentException collinear = Assert.Throws<ArgumentException>(() => Plane.ByThreePoints(
            new Point3d(0.0, 0.0, 0.0),
            new Point3d(1.0, 0.0, 0.0),
            new Point3d(2.0, 0.0, 0.0)));

        Assert.Equal("third", collinear.ParamName);

        ArgumentException coincident = Assert.Throws<ArgumentException>(() => Plane.ByThreePoints(
            new Point3d(1.0, 1.0, 1.0),
            new Point3d(1.0, 1.0, 1.0),
            new Point3d(2.0, 0.0, 0.0)));

        Assert.Equal("second", coincident.ParamName);

        ArgumentException unset = Assert.Throws<ArgumentException>(() => Plane.ByThreePoints(
            Point3d.Origin,
            Point3d.Unset,
            new Point3d(2.0, 0.0, 0.0)));

        Assert.Equal("second", unset.ParamName);
    }

    [Fact]
    public void ContainsIsScaleAwareLikeEveryOtherProximityTestInTheLayer()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(0.0, 0.0, 1e12), Vector3d.ZAxis);

        Assert.True(plane.Contains(new Point3d(0.0, 0.0, 1e12 + 0.5)));
        Assert.False(plane.Contains(new Point3d(0.0, 0.0, 1e12 + 500.0)));
        Assert.False(plane.Contains(Point3d.Unset));
    }

    [Fact]
    public void DistanceToIsSignedByWhichSideOfThePlaneThePointIsOn()
    {
        Plane plane = Plane.WorldXY;

        Assert.Equal(3.0, plane.DistanceTo(new Point3d(10.0, -4.0, 3.0)), 12);
        Assert.Equal(-3.0, plane.DistanceTo(new Point3d(10.0, -4.0, -3.0)), 12);
        Assert.Equal(0.0, plane.DistanceTo(new Point3d(10.0, -4.0, 0.0)), 12);
    }

    [Fact]
    public void ClosestPointDropsThePerpendicularComponent()
    {
        Point3d closest = Plane.WorldXY.ClosestPoint(new Point3d(2.0, 3.0, 9.0));

        Assert.True(closest.EqualsWithin(new Point3d(2.0, 3.0, 0.0)));
        Assert.True(Plane.WorldXY.Contains(closest));
    }

    [Fact]
    public void ProjectingAVectorRemovesItsComponentAlongTheNormal()
    {
        Assert.True(Plane.WorldXY.Project(new Vector3d(1.0, 2.0, 3.0))
            .EqualsWithin(new Vector3d(1.0, 2.0, 0.0)));

        Assert.True(Plane.WorldXY.Project(Vector3d.ZAxis).IsZero());
        Assert.True(Plane.WorldXY.Project(Vector3d.XAxis).EqualsWithin(Vector3d.XAxis));
    }

    [Fact]
    public void FlippingReversesTheNormalAndKeepsTheFrameRightHanded()
    {
        Plane flipped = Plane.WorldXY.Flipped();

        Assert.True(flipped.Normal.EqualsWithin(-Vector3d.ZAxis));
        AssertRightHanded(flipped);
        Assert.Equal(-3.0, flipped.DistanceTo(new Point3d(0.0, 0.0, 3.0)), 12);
    }

    [Fact]
    public void ConvertingToTwoDimensionsAndBackRoundTripsAPointOnThePlane()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 1.0, 1.0));
        Point3d onPlane = plane.To3d(new Point2d(4.0, -7.0));

        Assert.True(plane.To3d(plane.To2d(onPlane)).EqualsWithin(onPlane));
        Assert.True(plane.To2d(onPlane).EqualsWithin(new Point2d(4.0, -7.0)));
    }

    [Fact]
    public void TheOriginIsTheZeroOfThePlanesTwoDimensionalCoordinates()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(5.0, 6.0, 7.0), Vector3d.ZAxis);

        Assert.True(plane.To2d(plane.Origin).EqualsWithin(Point2d.Origin));
    }

    [Fact]
    public void ConvertingAPointOffThePlaneProjectsItFirst()
    {
        Point2d projected = Plane.WorldXY.To2d(new Point3d(2.0, 3.0, 100.0));

        Assert.True(projected.EqualsWithin(new Point2d(2.0, 3.0)));
    }

    [Fact]
    public void CoplanarityIgnoresDirectionAndTheInPlaneAxes()
    {
        Plane plane = Plane.WorldXY;
        Plane shifted = Plane.ByOriginNormal(new Point3d(50.0, -20.0, 0.0), Vector3d.ZAxis);

        Assert.True(plane.IsCoplanar(shifted));
        Assert.True(plane.IsCoplanar(plane.Flipped()));
        Assert.False(plane.IsCoplanar(Plane.ByOriginNormal(new Point3d(0.0, 0.0, 1.0), Vector3d.ZAxis)));
        Assert.False(plane.IsCoplanar(Plane.WorldYZ));
    }

    [Fact]
    public void EqualsWithinIsStricterThanCoplanarity()
    {
        Plane plane = Plane.WorldXY;
        Plane shifted = Plane.ByOriginNormal(new Point3d(50.0, -20.0, 0.0), Vector3d.ZAxis);

        Assert.True(plane.IsCoplanar(shifted));
        Assert.False(plane.EqualsWithin(shifted));
        Assert.True(plane.EqualsWithin(Plane.WorldXY));
    }

    [Fact]
    public void EqualityIsExactAndEqualPlanesShareAHashCode()
    {
        Assert.True(Plane.WorldXY == Plane.WorldXY);
        Assert.True(Plane.WorldXY != Plane.WorldYZ);
        Assert.Equal(Plane.WorldXY.GetHashCode(), Plane.WorldXY.GetHashCode());
    }

    [Fact]
    public void ToStringNamesTheOriginAndNormal()
    {
        Assert.Equal("Plane(Origin=(0, 0, 0), Normal=(0, 0, 1))", Plane.WorldXY.ToString());
    }


    [Fact]
    public void OffsetMovesAlongTheNormalAndKeepsTheFrame()
    {
        Plane moved = Plane.WorldXY.Offset(3.0);

        Assert.Equal(new Point3d(0.0, 0.0, 3.0), moved.Origin);
        Assert.Equal(Plane.WorldXY.XAxis, moved.XAxis);
        Assert.Equal(Plane.WorldXY.YAxis, moved.YAxis);
        Assert.Equal(Plane.WorldXY.Normal, moved.Normal);
        AssertRightHanded(moved);
    }

    [Fact]
    public void OffsetShiftsSignedDistanceByExactlyTheDistance()
    {
        Point3d probe = new(1.0, 2.0, 10.0);
        Plane moved = Plane.WorldXY.Offset(4.0);

        Assert.Equal(Plane.WorldXY.DistanceTo(probe) - 4.0, moved.DistanceTo(probe), 12);

        // The frame is untouched, so in-plane coordinates do not move with the plane.
        Assert.Equal(Plane.WorldXY.To2d(probe), moved.To2d(probe));
    }

    [Fact]
    public void OffsetByZeroReturnsAnEqualPlane()
    {
        Assert.Equal(Plane.WorldYZ, Plane.WorldYZ.Offset(0.0));
    }

    [Fact]
    public void OffsetRejectsANonFiniteDistance()
    {
        // A plane whose origin is NaN is not a plane, and every factory guarantees IsValid.
        Assert.Throws<ArgumentException>(() => Plane.WorldXY.Offset(double.NaN));
        Assert.Throws<ArgumentException>(() => Plane.WorldXY.Offset(double.PositiveInfinity));
    }

    [Fact]
    public void ByOriginNormalXAxisUsesOnlyTheInPlaneComponentOfTheXAxis()
    {
        // The requested X axis leans out of the plane by 45 degrees; only what lies in the
        // plane survives, so the frame is orthonormal and XAxis is the projection.
        Plane plane = Plane.ByOriginNormalXAxis(
            Point3d.Origin,
            Vector3d.ZAxis,
            new Vector3d(1.0, 0.0, 1.0));

        Assert.True(plane.XAxis.EqualsWithin(Vector3d.XAxis));
        Assert.True(plane.Normal.EqualsWithin(Vector3d.ZAxis));
        AssertRightHanded(plane);
    }

    [Fact]
    public void ByOriginNormalXAxisPinsTheInPlaneRotation()
    {
        Plane rotated = Plane.ByOriginNormalXAxis(
            Point3d.Origin,
            Vector3d.ZAxis,
            new Vector3d(1.0, 1.0, 0.0));

        Assert.True(rotated.XAxis.EqualsWithin(new Vector3d(1.0, 1.0, 0.0).Normalised()));
        Assert.True(rotated.YAxis.EqualsWithin(new Vector3d(-1.0, 1.0, 0.0).Normalised()));

        // The same plane as WorldXY geometrically, and a different frame on it - which is the
        // whole reason this factory exists.
        Assert.True(rotated.IsCoplanar(Plane.WorldXY));
        Assert.False(rotated.XAxis.EqualsWithin(Plane.WorldXY.XAxis));
    }

    [Fact]
    public void ByOriginNormalXAxisRejectsAnXAxisParallelToTheNormal()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => Plane.ByOriginNormalXAxis(Point3d.Origin, Vector3d.ZAxis, new Vector3d(0.0, 0.0, -2.0)));

        Assert.Equal("xAxis", failure.ParamName);
    }

    [Fact]
    public void ByOriginNormalXAxisRejectsADegenerateNormalAndANonFiniteOrigin()
    {
        Assert.Throws<ArgumentException>(
            () => Plane.ByOriginNormalXAxis(Point3d.Origin, Vector3d.Zero, Vector3d.XAxis));
        Assert.Throws<ArgumentException>(
            () => Plane.ByOriginNormalXAxis(new Point3d(double.NaN, 0.0, 0.0), Vector3d.ZAxis, Vector3d.XAxis));
    }
    private static void AssertRightHanded(in Plane plane)
    {
        Assert.True(plane.XAxis.IsUnit());
        Assert.True(plane.YAxis.IsUnit());
        Assert.True(plane.Normal.IsUnit());
        Assert.True(plane.XAxis.IsPerpendicularTo(plane.YAxis));
        Assert.True(plane.XAxis.Cross(plane.YAxis).EqualsWithin(plane.Normal));
    }
}

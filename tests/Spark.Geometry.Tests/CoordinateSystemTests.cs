using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class CoordinateSystemTests
{
    [Fact]
    public void TheIdentityFrameIsTheWorldFrame()
    {
        CoordinateSystem identity = CoordinateSystem.Identity;

        Assert.Equal(Point3d.Origin, identity.Origin);
        Assert.Equal(Vector3d.XAxis, identity.XAxis);
        Assert.Equal(Vector3d.YAxis, identity.YAxis);
        Assert.Equal(Vector3d.ZAxis, identity.ZAxis);
        Assert.True(identity.IsValid);
    }

    [Fact]
    public void ADefaultConstructedFrameIsNotValid()
    {
        Assert.False(default(CoordinateSystem).IsValid);
    }

    [Fact]
    public void ByOriginKeepsTheWorldAxes()
    {
        CoordinateSystem frame = CoordinateSystem.ByOrigin(new Point3d(1.0, 2.0, 3.0));

        Assert.Equal(new Point3d(1.0, 2.0, 3.0), frame.Origin);
        Assert.Equal(Vector3d.XAxis, frame.XAxis);
        Assert.Equal(Vector3d.ZAxis, frame.ZAxis);
    }

    [Fact]
    public void TheAxesAreAlwaysOrthonormalAndRightHanded()
    {
        CoordinateSystem frame = CoordinateSystem.ByOriginXAxisYAxis(
            new Point3d(1.0, 2.0, 3.0),
            new Vector3d(2.0, 0.0, 0.0),
            new Vector3d(3.0, 4.0, 0.0));

        Assert.True(frame.XAxis.IsUnit());
        Assert.True(frame.YAxis.IsUnit());
        Assert.True(frame.ZAxis.IsUnit());
        Assert.True(frame.XAxis.EqualsWithin(Vector3d.XAxis));
        Assert.True(frame.YAxis.EqualsWithin(Vector3d.YAxis));
        Assert.True(frame.XAxis.Cross(frame.YAxis).EqualsWithin(frame.ZAxis));
    }

    [Fact]
    public void ConstructingAFrameFromDegenerateAxesThrows()
    {
        Assert.Throws<ArgumentException>(
            () => new CoordinateSystem(Point3d.Origin, Vector3d.Zero, Vector3d.YAxis));

        Assert.Throws<ArgumentException>(
            () => new CoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.XAxis));

        Assert.Throws<ArgumentException>(
            () => new CoordinateSystem(Point3d.Unset, Vector3d.XAxis, Vector3d.YAxis));
    }

    [Fact]
    public void ByOriginZAxisAlignsTheThirdAxisAndReproducesTheWorldFrameForZ()
    {
        CoordinateSystem frame = CoordinateSystem.ByOriginZAxis(Point3d.Origin, new Vector3d(0.0, 0.0, 9.0));

        Assert.Equal(CoordinateSystem.Identity, frame);
    }

    [Fact]
    public void ByOriginZAxisPointsTheThirdAxisWhereItIsTold()
    {
        Vector3d direction = new(1.0, 2.0, 3.0);
        CoordinateSystem frame = CoordinateSystem.ByOriginZAxis(Point3d.Origin, direction);

        Assert.True(frame.ZAxis.EqualsWithin(direction.Normalised()));
    }

    [Fact]
    public void AFrameAndAPlaneCarryTheSameInformation()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 1.0, 1.0));
        CoordinateSystem frame = CoordinateSystem.ByPlane(plane);

        Assert.Equal(plane.Origin, frame.Origin);
        Assert.Equal(plane.XAxis, frame.XAxis);
        Assert.Equal(plane.YAxis, frame.YAxis);
        Assert.Equal(plane.Normal, frame.ZAxis);
        Assert.True(frame.ToPlane().EqualsWithin(plane));
    }

    [Fact]
    public void ReadingAnInvalidFrameAsAPlaneOrTransformThrows()
    {
        CoordinateSystem invalid = default;

        Assert.Throws<InvalidOperationException>(() => invalid.ToPlane());
        Assert.Throws<InvalidOperationException>(() => invalid.ToTransform());
    }

    [Fact]
    public void ByPlaneRejectsAnInvalidPlane()
    {
        Assert.Throws<ArgumentException>(() => CoordinateSystem.ByPlane(default));
    }

    [Fact]
    public void ToLocalAndToWorldAreInversesForPoints()
    {
        CoordinateSystem frame = CoordinateSystem.ByOriginXAxisYAxis(
            new Point3d(10.0, 20.0, 30.0),
            new Vector3d(0.0, 1.0, 0.0),
            new Vector3d(0.0, 0.0, 1.0));

        Point3d world = new(1.0, 2.0, 3.0);
        Point3d local = frame.ToLocal(world);

        Assert.True(frame.ToWorld(local).EqualsWithin(world));
    }

    [Fact]
    public void TheFramesOriginIsTheZeroOfItsLocalCoordinates()
    {
        CoordinateSystem frame = CoordinateSystem.ByOrigin(new Point3d(5.0, 6.0, 7.0));

        Assert.True(frame.ToLocal(frame.Origin).EqualsWithin(Point3d.Origin));
        Assert.True(frame.ToWorld(Point3d.Origin).EqualsWithin(frame.Origin));
    }

    [Fact]
    public void ConvertingADirectionIgnoresTheOrigin()
    {
        CoordinateSystem frame = CoordinateSystem.ByOrigin(new Point3d(1000.0, 1000.0, 1000.0));

        Assert.True(frame.ToLocal(Vector3d.XAxis).EqualsWithin(Vector3d.XAxis));
        Assert.True(frame.ToWorld(Vector3d.XAxis).EqualsWithin(Vector3d.XAxis));
    }

    [Fact]
    public void ToTransformAgreesWithToWorld()
    {
        CoordinateSystem frame = CoordinateSystem.ByOriginXAxisYAxis(
            new Point3d(10.0, 20.0, 30.0),
            new Vector3d(1.0, 1.0, 0.0),
            new Vector3d(-1.0, 1.0, 0.0));

        Transform transform = frame.ToTransform();
        Point3d local = new(1.0, 2.0, 3.0);

        Assert.True(transform.OfPoint(local).EqualsWithin(frame.ToWorld(local)));
        Assert.True(transform.OfVector(Vector3d.XAxis).EqualsWithin(frame.ToWorld(Vector3d.XAxis)));
        Assert.True(transform.IsRigid());
    }

    [Fact]
    public void ToTransformIsTheInverseOfChangeBasisOnTheEquivalentPlane()
    {
        CoordinateSystem frame = CoordinateSystem.ByOriginZAxis(
            new Point3d(4.0, 5.0, 6.0),
            new Vector3d(1.0, -2.0, 3.0));

        Assert.True((Transform.ChangeBasis(frame.ToPlane()) * frame.ToTransform()).IsIdentity());
    }

    [Fact]
    public void EqualityIsExactAndEqualFramesShareAHashCode()
    {
        Assert.True(CoordinateSystem.Identity == CoordinateSystem.Identity);
        Assert.True(CoordinateSystem.Identity != CoordinateSystem.ByOrigin(new Point3d(1.0, 0.0, 0.0)));
        Assert.Equal(
            CoordinateSystem.Identity.GetHashCode(),
            CoordinateSystem.Identity.GetHashCode());
        Assert.True(CoordinateSystem.Identity.EqualsWithin(CoordinateSystem.Identity));
    }

    [Fact]
    public void ToStringNamesTheOriginAndAllThreeAxes()
    {
        Assert.Equal(
            "CoordinateSystem(Origin=(0, 0, 0), X=(1, 0, 0), Y=(0, 1, 0), Z=(0, 0, 1))",
            CoordinateSystem.Identity.ToString());
    }
}

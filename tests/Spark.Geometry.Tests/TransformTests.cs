using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class TransformTests
{
    [Fact]
    public void TheIdentityLeavesPointsAndVectorsWhereTheyAre()
    {
        Point3d point = new(1.0, 2.0, 3.0);
        Vector3d vector = new(4.0, 5.0, 6.0);

        Assert.Equal(point, Transform.Identity.OfPoint(point));
        Assert.Equal(vector, Transform.Identity.OfVector(vector));
        Assert.True(Transform.Identity.IsIdentity());
    }

    [Fact]
    public void ADefaultConstructedTransformIsNotTheIdentity()
    {
        Assert.False(default(Transform).IsIdentity());
        Assert.Equal(0.0, default(Transform).Determinant);
    }

    [Fact]
    public void TranslationMovesPointsAndIgnoresVectors()
    {
        Transform translation = Transform.Translation(new Vector3d(10.0, 0.0, -5.0));

        Assert.Equal(new Point3d(11.0, 2.0, -2.0), translation.OfPoint(new Point3d(1.0, 2.0, 3.0)));
        Assert.Equal(new Vector3d(1.0, 2.0, 3.0), translation.OfVector(new Vector3d(1.0, 2.0, 3.0)));
    }

    [Fact]
    public void TheComponentWiseTranslationOverloadMatchesTheVectorOne()
    {
        Assert.Equal(
            Transform.Translation(new Vector3d(1.0, 2.0, 3.0)),
            Transform.Translation(1.0, 2.0, 3.0));
    }

    [Fact]
    public void UniformScaleAboutTheOriginMultipliesEveryCoordinate()
    {
        Transform scale = Transform.Scale(3.0);

        Assert.Equal(new Point3d(3.0, 6.0, 9.0), scale.OfPoint(new Point3d(1.0, 2.0, 3.0)));
        Assert.Equal(27.0, scale.Determinant, 12);
    }

    [Fact]
    public void NonUniformScaleAppliesADifferentFactorPerAxis()
    {
        Transform scale = Transform.Scale(2.0, 3.0, 4.0);

        Assert.Equal(new Point3d(2.0, 6.0, 12.0), scale.OfPoint(new Point3d(1.0, 2.0, 3.0)));
    }

    [Fact]
    public void ScalingAboutAPointLeavesThatPointWhereItIs()
    {
        Point3d centre = new(5.0, 5.0, 5.0);
        Transform scale = Transform.Scale(centre, 4.0);

        Assert.True(scale.OfPoint(centre).EqualsWithin(centre));
        Assert.True(scale.OfPoint(new Point3d(6.0, 5.0, 5.0)).EqualsWithin(new Point3d(9.0, 5.0, 5.0)));
    }

    [Fact]
    public void ScalingByZeroProducesASingularTransform()
    {
        Assert.Equal(0.0, Transform.Scale(0.0).Determinant, 12);
        Assert.False(Transform.Scale(0.0).TryGetInverse(out Transform inverse));
        Assert.True(inverse.IsIdentity());
    }

    [Fact]
    public void RotationAboutZByAQuarterTurnCarriesXToY()
    {
        Transform rotation = Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(90));

        Assert.True(rotation.OfVector(Vector3d.XAxis).EqualsWithin(Vector3d.YAxis));
        Assert.True(rotation.OfVector(Vector3d.YAxis).EqualsWithin(-Vector3d.XAxis));
        Assert.True(rotation.OfVector(Vector3d.ZAxis).EqualsWithin(Vector3d.ZAxis));
    }

    [Fact]
    public void TheTransformRotationAgreesWithTheVectorRotation()
    {
        Vector3d axis = new(1.0, 2.0, 3.0);
        Angle angle = Angle.FromDegrees(37.0);
        Vector3d vector = new(4.0, -5.0, 6.0);

        Assert.True(Transform.Rotation(axis, angle).OfVector(vector)
            .EqualsWithin(vector.Rotate(axis, angle)));
    }

    [Fact]
    public void RotationAboutACentreLeavesThatCentreWhereItIs()
    {
        Point3d centre = new(7.0, -3.0, 2.0);
        Transform rotation = Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(90), centre);

        Assert.True(rotation.OfPoint(centre).EqualsWithin(centre));
        Assert.True(rotation.OfPoint(new Point3d(8.0, -3.0, 2.0))
            .EqualsWithin(new Point3d(7.0, -2.0, 2.0)));
    }

    [Fact]
    public void RotationIsRigidAndPreservesLength()
    {
        Transform rotation = Transform.Rotation(new Vector3d(1.0, 1.0, 0.0), Angle.FromDegrees(53.0));

        Assert.True(rotation.IsRigid());
        Assert.Equal(1.0, rotation.Determinant, 12);
        Assert.Equal(
            new Vector3d(1.0, 2.0, 3.0).Length,
            rotation.OfVector(new Vector3d(1.0, 2.0, 3.0)).Length,
            12);
    }

    [Fact]
    public void RotationAboutADegenerateAxisThrows()
    {
        Assert.Throws<ArgumentException>(
            () => Transform.Rotation(Vector3d.Zero, Angle.QuarterTurn));
    }

    [Fact]
    public void MirroringReflectsThroughThePlaneAndLeavesThePlaneAlone()
    {
        Transform mirror = Transform.Mirror(Plane.WorldXY);

        Assert.True(mirror.OfPoint(new Point3d(1.0, 2.0, 3.0)).EqualsWithin(new Point3d(1.0, 2.0, -3.0)));
        Assert.True(mirror.OfPoint(new Point3d(1.0, 2.0, 0.0)).EqualsWithin(new Point3d(1.0, 2.0, 0.0)));
    }

    [Fact]
    public void MirroringInAnOffsetPlaneKeepsThatPlaneFixed()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(0.0, 0.0, 5.0), Vector3d.ZAxis);
        Transform mirror = Transform.Mirror(plane);

        Assert.True(mirror.OfPoint(new Point3d(0.0, 0.0, 7.0)).EqualsWithin(new Point3d(0.0, 0.0, 3.0)));
        Assert.True(mirror.OfPoint(new Point3d(1.0, 1.0, 5.0)).EqualsWithin(new Point3d(1.0, 1.0, 5.0)));
    }

    [Fact]
    public void AMirrorIsNotRigidBecauseItReversesHandedness()
    {
        Transform mirror = Transform.Mirror(Plane.WorldXY);

        Assert.False(mirror.IsRigid());
        Assert.True(mirror.IsAffine());
        Assert.Equal(-1.0, mirror.Determinant, 12);
    }

    [Fact]
    public void MirroringTwiceIsTheIdentity()
    {
        Transform mirror = Transform.Mirror(
            Plane.ByOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, 1.0, 1.0)));

        Assert.True((mirror * mirror).IsIdentity());
    }

    [Fact]
    public void MirroringInAnInvalidPlaneThrows()
    {
        Assert.Throws<ArgumentException>(() => Transform.Mirror(default));
    }

    [Fact]
    public void CompositionAppliesTheRightHandOperandFirst()
    {
        Transform translate = Transform.Translation(10.0, 0.0, 0.0);
        Transform rotate = Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(90));

        // Rotate first, then translate.
        Assert.True((translate * rotate).OfPoint(new Point3d(1.0, 0.0, 0.0))
            .EqualsWithin(new Point3d(10.0, 1.0, 0.0)));

        // Translate first, then rotate.
        Assert.True((rotate * translate).OfPoint(new Point3d(1.0, 0.0, 0.0))
            .EqualsWithin(new Point3d(0.0, 11.0, 0.0)));
    }

    [Fact]
    public void CompositionIsNotCommutative()
    {
        Transform translate = Transform.Translation(10.0, 0.0, 0.0);
        Transform rotate = Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(90));

        Assert.False((translate * rotate).EqualsWithin(rotate * translate));
        Assert.Equal(translate * rotate, Transform.Multiply(translate, rotate));
    }

    [Fact]
    public void ATransformComposedWithItsOwnInverseIsTheIdentity()
    {
        Transform transform =
            Transform.Translation(3.0, -4.0, 5.0)
            * Transform.Rotation(new Vector3d(1.0, 2.0, 3.0), Angle.FromDegrees(41.0))
            * Transform.Scale(2.5);

        Assert.True(transform.TryGetInverse(out Transform inverse));
        Assert.True((transform * inverse).IsIdentity());
        Assert.True((inverse * transform).IsIdentity());
    }

    [Fact]
    public void InvertingATransformWithANonFiniteEntryFails()
    {
        Transform broken = new(
            double.NaN, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0);

        Assert.False(broken.TryGetInverse(out _));
    }

    [Fact]
    public void PlaneToPlaneCarriesTheSourceFrameOntoTheTarget()
    {
        Plane from = Plane.WorldXY;
        Plane to = Plane.ByOriginXAxisYAxis(
            new Point3d(10.0, 20.0, 30.0),
            new Vector3d(0.0, 1.0, 0.0),
            new Vector3d(0.0, 0.0, 1.0));

        Transform transform = Transform.PlaneToPlane(from, to);

        Assert.True(transform.OfPoint(from.Origin).EqualsWithin(to.Origin));
        Assert.True(transform.OfVector(from.XAxis).EqualsWithin(to.XAxis));
        Assert.True(transform.OfVector(from.YAxis).EqualsWithin(to.YAxis));
        Assert.True(transform.OfVector(from.Normal).EqualsWithin(to.Normal));
        Assert.True(transform.IsRigid());
    }

    [Fact]
    public void PlaneToPlaneWithTheSamePlaneTwiceIsTheIdentity()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(4.0, 5.0, 6.0));

        Assert.True(Transform.PlaneToPlane(plane, plane).IsIdentity());
    }

    [Fact]
    public void ChangeBasisReportsAPointsCoordinatesInThePlanesFrame()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(0.0, 0.0, 5.0), Vector3d.ZAxis);
        Transform changeBasis = Transform.ChangeBasis(plane);

        Point3d local = changeBasis.OfPoint(new Point3d(1.0, 2.0, 8.0));

        Assert.True(local.EqualsWithin(new Point3d(1.0, 2.0, 3.0)));
    }

    [Fact]
    public void ChangeBasisIsTheInverseOfPlacingGeometryOnThatPlane()
    {
        Plane plane = Plane.ByOriginNormal(new Point3d(1.0, 2.0, 3.0), new Vector3d(1.0, -2.0, 4.0));

        Transform onto = Transform.PlaneToPlane(Plane.WorldXY, plane);
        Transform back = Transform.ChangeBasis(plane);

        Assert.True((back * onto).IsIdentity());
    }

    [Fact]
    public void PlaneFactoriesRejectAnInvalidPlane()
    {
        Assert.Throws<ArgumentException>(() => Transform.ChangeBasis(default));
        Assert.Throws<ArgumentException>(() => Transform.PlaneToPlane(default, Plane.WorldXY));
        Assert.Throws<ArgumentException>(() => Transform.PlaneToPlane(Plane.WorldXY, default));
    }

    [Fact]
    public void TransformingABoxBoundsTheTransformedCorners()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox moved = Transform.Translation(10.0, 0.0, 0.0).OfBoundingBox(box);

        Assert.True(moved.EqualsWithin(new BoundingBox(
            new Point3d(10.0, 0.0, 0.0),
            new Point3d(11.0, 1.0, 1.0))));
    }

    [Fact]
    public void RotatingABoxInflatesItsAxisAlignedBound()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox rotated = Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(45)).OfBoundingBox(box);

        Assert.True(rotated.Volume > box.Volume);
    }

    [Fact]
    public void TransformingTheEmptyBoxGivesTheEmptyBox()
    {
        Assert.Equal(BoundingBox.Empty, Transform.Translation(1.0, 2.0, 3.0).OfBoundingBox(BoundingBox.Empty));
    }

    [Fact]
    public void TheApplicationOperatorsMatchTheNamedMethods()
    {
        Transform transform = Transform.Rotation(Vector3d.ZAxis, Angle.FromDegrees(30));
        Point3d point = new(1.0, 2.0, 3.0);
        Vector3d vector = new(4.0, 5.0, 6.0);
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.Equal(transform.OfPoint(point), transform * point);
        Assert.Equal(transform.OfVector(vector), transform * vector);
        Assert.Equal(transform.OfBoundingBox(box), transform * box);
    }

    [Fact]
    public void AProjectiveTransformDividesThroughByTheHomogeneousWeight()
    {
        Transform projective = new(
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 1.0, 0.0);

        Assert.False(projective.IsAffine());
        Assert.True(projective.OfPoint(new Point3d(4.0, 6.0, 2.0)).EqualsWithin(new Point3d(2.0, 3.0, 1.0)));
    }

    [Fact]
    public void TheIndexerReadsEntriesByRowAndColumn()
    {
        Transform translation = Transform.Translation(7.0, 8.0, 9.0);

        Assert.Equal(1.0, translation[0, 0]);
        Assert.Equal(7.0, translation[0, 3]);
        Assert.Equal(8.0, translation[1, 3]);
        Assert.Equal(9.0, translation[2, 3]);
        Assert.Equal(1.0, translation[3, 3]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(4, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4)]
    public void TheIndexerRejectsAnOutOfRangePosition(int row, int column)
    {
        Transform identity = Transform.Identity;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = identity[row, column];
        });
    }

    [Fact]
    public void EqualityIsExactAndEqualTransformsShareAHashCode()
    {
        Assert.True(Transform.Identity == Transform.Identity);
        Assert.True(Transform.Identity != Transform.Scale(2.0));
        Assert.Equal(Transform.Identity.GetHashCode(), Transform.Identity.GetHashCode());
        Assert.True(Transform.Identity.Equals((object)Transform.Identity));
    }

    [Fact]
    public void ToStringPrintsTheMatrixRowByRow()
    {
        Assert.Equal(
            "[[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]",
            Transform.Identity.ToString());
    }
}

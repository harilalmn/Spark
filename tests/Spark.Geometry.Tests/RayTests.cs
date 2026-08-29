using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class RayTests
{
    [Fact]
    public void TheDirectionIsNormalisedAndTheOriginIsNot()
    {
        Ray ray = new(new Point3d(1.0, 2.0, 3.0), new Vector3d(0.0, 0.0, 7.0));

        Assert.Equal(new Point3d(1.0, 2.0, 3.0), ray.Origin);
        Assert.Equal(Vector3d.ZAxis, ray.Direction);

        // The parameter is a distance because the direction is a unit vector, and every
        // comparison the BVH makes against a nearest-so-far depends on that.
        Assert.Equal(new Point3d(1.0, 2.0, 6.0), ray.PointAt(3.0));
    }

    [Fact]
    public void ADefaultRayIsNotValidAndAnswersNothing()
    {
        Ray unset = default;

        Assert.False(unset.IsValid);
        Assert.Throws<InvalidOperationException>(() => unset.PointAt(1.0));
        Assert.Throws<InvalidOperationException>(() => unset.ClosestPoint(Point3d.Origin));
        Assert.Throws<InvalidOperationException>(() => unset.Intersects(default));
    }

    [Fact]
    public void ConstructingFromADegenerateDirectionOrOriginThrows()
    {
        Assert.Equal(
            "direction",
            Assert.Throws<ArgumentException>(() => new Ray(Point3d.Origin, Vector3d.Zero)).ParamName);
        Assert.Equal(
            "origin",
            Assert.Throws<ArgumentException>(() => new Ray(Point3d.Unset, Vector3d.ZAxis)).ParamName);
        Assert.Equal(
            "towards",
            Assert.Throws<ArgumentException>(() => Ray.ByTwoPoints(Point3d.Origin, Point3d.Origin)).ParamName);
    }

    [Fact]
    public void ByTwoPointsPassesThroughBothOfThem()
    {
        Ray ray = Ray.ByTwoPoints(new Point3d(1.0, 1.0, 1.0), new Point3d(4.0, 1.0, 1.0));

        Assert.Equal(new Point3d(1.0, 1.0, 1.0), ray.Origin);
        Assert.True(ray.PointAt(3.0).EqualsWithin(new Point3d(4.0, 1.0, 1.0)));
    }

    [Fact]
    public void TheClosestPointIsClampedAtTheOriginBecauseARayDoesNotGoBackwards()
    {
        Ray ray = new(Point3d.Origin, Vector3d.XAxis);

        Assert.Equal(new Point3d(3.0, 0.0, 0.0), ray.ClosestPoint(new Point3d(3.0, 4.0, 0.0)));

        // An infinite line would answer (-3, 0, 0) here. The clamp is the difference between
        // the two types, and it is why a picking ray does not select what is behind the camera.
        Assert.Equal(Point3d.Origin, ray.ClosestPoint(new Point3d(-3.0, 4.0, 0.0)));
        Assert.Equal(5.0, ray.DistanceTo(new Point3d(-3.0, 4.0, 0.0)), 12);
    }

    [Fact]
    public void ARayThroughABoxReportsWhereItEntersAndLeaves()
    {
        Ray ray = new(new Point3d(-5.0, 0.5, 0.5), Vector3d.XAxis);
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.True(ray.TryIntersect(box, out Interval span));
        Assert.Equal(5.0, span.Min, 12);
        Assert.Equal(6.0, span.Max, 12);
    }

    [Fact]
    public void ARayStartingInsideABoxEntersAtZeroRatherThanMissing()
    {
        Ray ray = new(new Point3d(0.5, 0.5, 0.5), Vector3d.XAxis);
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.True(ray.TryIntersect(box, out Interval span));
        Assert.Equal(0.0, span.Min, 12);
        Assert.Equal(0.5, span.Max, 12);
    }

    [Fact]
    public void ABoxBehindTheOriginIsAMissRatherThanANegativeHit()
    {
        Ray ray = new(new Point3d(5.0, 0.5, 0.5), Vector3d.XAxis);
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.False(ray.Intersects(box));
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1.0, 0.5)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    public void ARayLyingExactlyInAFaceEdgeOrCornerOfABoxIsAHit(double y, double z)
    {
        // The branchless slab method divides by a zero direction component on purpose and
        // relies on the infinities cancelling. When the origin sits exactly on a slab plane
        // the product is 0 x infinity, which is NaN, and every comparison against it is false
        // - so the whole test reports a miss on precisely the alignments that axis-aligned
        // work produces most often. These four cases are that failure, written down.
        Ray ray = new(new Point3d(-5.0, y, z), Vector3d.XAxis);
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.True(ray.TryIntersect(box, out Interval span));
        Assert.Equal(5.0, span.Min, 12);
    }

    [Fact]
    public void ARayParallelToABoxAndOutsideItMissesRatherThanDividingByZero()
    {
        Ray ray = new(new Point3d(-5.0, 2.0, 0.5), Vector3d.XAxis);
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.False(ray.Intersects(box));
    }

    [Fact]
    public void AnInvalidBoxIsAMissRatherThanAThrow()
    {
        Ray ray = new(Point3d.Origin, Vector3d.XAxis);

        Assert.False(ray.Intersects(BoundingBox.Empty));
        Assert.False(ray.Intersects(new BoundingBox(Point3d.Unset, new Point3d(1.0, 1.0, 1.0))));
    }

    [Fact]
    public void TransformingARayMovesItAndKeepsTheDirectionAUnitVector()
    {
        Ray ray = new(Point3d.Origin, Vector3d.XAxis);
        Ray moved = ray.TransformedBy(
            Transform.Translation(new Vector3d(0.0, 0.0, 5.0)) * Transform.Rotation(Vector3d.ZAxis, Angle.QuarterTurn));

        Assert.True(moved.Origin.EqualsWithin(new Point3d(0.0, 0.0, 5.0)));
        Assert.True(moved.Direction.EqualsWithin(Vector3d.YAxis));

        // A scale changes where PointAt lands and not which points the ray passes through,
        // because the direction is re-normalised.
        Ray scaled = ray.TransformedBy(Transform.Scale(4.0));
        Assert.True(scaled.Direction.IsUnit());
    }

    [Fact]
    public void ATransformThatCollapsesTheDirectionIsRefused()
    {
        Ray ray = new(Point3d.Origin, Vector3d.XAxis);

        Assert.Equal(
            "transform",
            Assert.Throws<ArgumentException>(() => ray.TransformedBy(Transform.Scale(0.0))).ParamName);
    }

    [Fact]
    public void TwoRaysTracingTheSameHalfLineFromDifferentOriginsAreNotEqual()
    {
        Ray first = new(Point3d.Origin, Vector3d.XAxis);
        Ray second = new(new Point3d(1.0, 0.0, 0.0), Vector3d.XAxis);

        // They agree about every point on the shared part of the half-line and disagree about
        // every distance, which is what a ray is for.
        Assert.NotEqual(first, second);
        Assert.False(first.EqualsWithin(second));
        Assert.Equal(first, new Ray(Point3d.Origin, new Vector3d(9.0, 0.0, 0.0)));
        Assert.Equal(first.GetHashCode(), new Ray(Point3d.Origin, Vector3d.XAxis).GetHashCode());
    }

    [Fact]
    public void ToStringShowsTheOriginAndTheDirection()
    {
        Assert.Equal("(0, 0, 0) → (0, 0, 1)", new Ray(Point3d.Origin, Vector3d.ZAxis).ToString());
    }
}

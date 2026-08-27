using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class BoundingBoxTests
{
    [Fact]
    public void TheConstructorSortsItsTwoCornersSoTheirOrderDoesNotMatter()
    {
        BoundingBox one = new(new Point3d(10.0, 0.0, 5.0), new Point3d(0.0, 10.0, -5.0));
        BoundingBox other = new(new Point3d(0.0, 10.0, -5.0), new Point3d(10.0, 0.0, 5.0));

        Assert.Equal(one, other);
        Assert.Equal(new Point3d(0.0, 0.0, -5.0), one.Min);
        Assert.Equal(new Point3d(10.0, 10.0, 5.0), one.Max);
    }

    [Fact]
    public void TheBoxIsGenuinelyThreeDimensional()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(2.0, 3.0, 4.0));

        Assert.Equal(24.0, box.Volume, 12);
        Assert.Equal(52.0, box.Area, 12);
        Assert.Equal(new Vector3d(2.0, 3.0, 4.0), box.Diagonal);
        Assert.Equal(new Point3d(1.0, 1.5, 2.0), box.Centre);
    }

    [Fact]
    public void AFlatBoxCountsBothOfItsCoincidentFaces()
    {
        BoundingBox flat = new(Point3d.Origin, new Point3d(1.0, 1.0, 0.0));

        Assert.Equal(0.0, flat.Volume, 12);
        Assert.Equal(2.0, flat.Area, 12);
        Assert.True(flat.IsValid);
    }

    [Fact]
    public void TheEmptyBoxIsNotValidAndHasNoVolume()
    {
        Assert.False(BoundingBox.Empty.IsValid);
        Assert.Equal(0.0, BoundingBox.Empty.Volume);
        Assert.Equal(0.0, BoundingBox.Empty.Area);
    }

    [Fact]
    public void ADefaultConstructedBoxIsTheZeroSizedBoxAtTheOriginNotTheEmptyBox()
    {
        Assert.True(default(BoundingBox).IsValid);
        Assert.NotEqual(BoundingBox.Empty, default(BoundingBox));
    }

    [Fact]
    public void TheEmptyBoxIsTheIdentityForUnion()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.Equal(box, BoundingBox.Empty.Union(box));
        Assert.Equal(box, box.Union(BoundingBox.Empty));
    }

    [Fact]
    public void TheEmptyBoxContainsNothing()
    {
        Assert.False(BoundingBox.Empty.Contains(Point3d.Origin));
        Assert.False(BoundingBox.Empty.Intersects(BoundingBox.Empty));
    }

    [Fact]
    public void EveryBoxContainsTheEmptyBoxBecauseItOccupiesNoSpace()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.True(box.Contains(BoundingBox.Empty));
    }

    [Fact]
    public void UnionContainsBothInputsAndIsCommutative()
    {
        BoundingBox a = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox b = new(new Point3d(5.0, 5.0, 5.0), new Point3d(6.0, 6.0, 6.0));

        BoundingBox union = a.Union(b);

        Assert.Equal(union, b.Union(a));
        Assert.Equal(union, BoundingBox.Union(a, b));
        Assert.True(union.Contains(a));
        Assert.True(union.Contains(b));
    }

    [Fact]
    public void FromPointsBoundsEveryPoint()
    {
        Point3d[] points =
        [
            new Point3d(1.0, 2.0, 3.0),
            new Point3d(-4.0, 0.0, 7.0),
            new Point3d(0.0, -1.0, -2.0),
        ];

        BoundingBox box = BoundingBox.FromPoints(points);

        Assert.Equal(new Point3d(-4.0, -1.0, -2.0), box.Min);
        Assert.Equal(new Point3d(1.0, 2.0, 7.0), box.Max);
        Assert.Equal(box, BoundingBox.FromPoints((ReadOnlySpan<Point3d>)points));
    }

    [Fact]
    public void FromPointsOfAnEmptySequenceGivesTheEmptyBox()
    {
        Assert.Equal(BoundingBox.Empty, BoundingBox.FromPoints(Array.Empty<Point3d>()));
        Assert.Equal(BoundingBox.Empty, BoundingBox.FromPoints(ReadOnlySpan<Point3d>.Empty));
    }

    [Fact]
    public void FromPointsRejectsANullSequence()
    {
        Assert.Throws<ArgumentNullException>(() => BoundingBox.FromPoints((IEnumerable<Point3d>)null!));
    }

    [Fact]
    public void ContainsIncludesTheBoundaryAndWidensItByTheTolerance()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.True(box.Contains(Point3d.Origin));
        Assert.True(box.Contains(new Point3d(1.0, 1.0, 1.0)));
        Assert.True(box.Contains(new Point3d(-1e-9, 0.5, 0.5)));
        Assert.False(box.Contains(new Point3d(-1e-3, 0.5, 0.5)));
    }

    [Fact]
    public void ContainsRejectsAnUnsetPoint()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.False(box.Contains(Point3d.Unset));
    }

    [Fact]
    public void BoxesThatTouchOnAFaceIntersect()
    {
        BoundingBox a = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox b = new(new Point3d(1.0, 0.0, 0.0), new Point3d(2.0, 1.0, 1.0));
        BoundingBox apart = new(new Point3d(2.0, 0.0, 0.0), new Point3d(3.0, 1.0, 1.0));

        Assert.True(a.Intersects(b));
        Assert.False(a.Intersects(apart));
    }

    [Fact]
    public void IntersectionIgnoresBoxesThatOverlapOnlyInTwoOfThreeAxes()
    {
        // The seed library's box ignored Z entirely, so these two would have been reported
        // as intersecting.
        BoundingBox low = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox high = new(new Point3d(0.0, 0.0, 5.0), new Point3d(1.0, 1.0, 6.0));

        Assert.False(low.Intersects(high));
    }

    [Fact]
    public void ABoxWithANaNCornerIntersectsNothingInEitherOperandOrder()
    {
        // Intersects is built from negated comparisons and every comparison against NaN is
        // false, so an unguarded version reported this pair as overlapping - while Contains
        // on the very same pair correctly said no.
        BoundingBox real = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox broken = new(Point3d.Unset, new Point3d(1.0, 1.0, 1.0));

        Assert.False(real.Intersects(broken));
        Assert.False(broken.Intersects(real));
        Assert.False(broken.Intersects(broken));
    }

    [Fact]
    public void ABoxWithANaNCornerContainsNothingAndIsContainedByNothing()
    {
        BoundingBox real = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox broken = new(Point3d.Unset, new Point3d(1.0, 1.0, 1.0));

        Assert.False(broken.Contains(new Point3d(0.5, 0.5, 0.5)));
        Assert.False(broken.Contains(real));
        Assert.False(real.Contains(broken));
    }

    [Fact]
    public void InflateGrowsTheBoxInEveryDirection()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox inflated = box.Inflate(1.0);

        Assert.Equal(new Point3d(-1.0, -1.0, -1.0), inflated.Min);
        Assert.Equal(new Point3d(2.0, 2.0, 2.0), inflated.Max);
        Assert.Equal(new Point3d(0.0, -1.0, 0.0), box.Inflate(0.0, 1.0, 0.0).Min);
    }

    [Fact]
    public void DeflatingPastZeroSizeInvalidatesTheBoxRatherThanThrowing()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.False(box.Inflate(-1.0).IsValid);
    }

    [Fact]
    public void ThereAreEightCornersAndTheyAreAllContainedInTheBox()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(1.0, 2.0, 3.0));
        Point3d[] corners = box.Corners();

        Assert.Equal(8, corners.Length);
        Assert.Equal(box.Min, corners[0]);
        Assert.Equal(box.Max, corners[6]);

        foreach (Point3d corner in corners)
        {
            Assert.True(box.Contains(corner));
        }
    }

    [Fact]
    public void ClosestPointReturnsThePointItselfWhenItIsInside()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(10.0, 10.0, 10.0));
        Point3d inside = new(1.0, 2.0, 3.0);

        Assert.Equal(inside, box.ClosestPoint(inside));
    }

    [Fact]
    public void ClosestPointClampsAnOutsidePointOntoTheSurface()
    {
        BoundingBox box = new(Point3d.Origin, new Point3d(10.0, 10.0, 10.0));

        Assert.Equal(new Point3d(0.0, 10.0, 5.0), box.ClosestPoint(new Point3d(-5.0, 20.0, 5.0)));
    }

    [Fact]
    public void EqualityIsExactAndEqualBoxesShareAHashCode()
    {
        BoundingBox a = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox b = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.EqualsWithin(new BoundingBox(Point3d.Origin, new Point3d(1.0 + 1e-9, 1.0, 1.0))));
    }

    [Fact]
    public void ToStringNamesBothCorners()
    {
        Assert.Equal(
            "BoundingBox((0, 0, 0), (1, 1, 1))",
            new BoundingBox(Point3d.Origin, new Point3d(1.0, 1.0, 1.0)).ToString());
    }
}

using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class Point3dTests
{
    [Fact]
    public void ADefaultConstructedPointIsTheOrigin()
    {
        Assert.Equal(Point3d.Origin, default(Point3d));
        Assert.True(default(Point3d).IsValid);
    }

    [Fact]
    public void AnUnsetPointIsNotValidAndIsNotEqualToItselfUnderTheOperator()
    {
        Assert.False(Point3d.Unset.IsValid);
        Assert.False(Point3d.Unset == Point3d.Unset);
        Assert.True(Point3d.Unset.Equals(Point3d.Unset));
    }

    [Fact]
    public void AnInfinitePointIsNotValid()
    {
        Assert.False(new Point3d(double.PositiveInfinity, 0.0, 0.0).IsValid);
    }

    [Fact]
    public void DistanceIsSymmetricAndNonNegative()
    {
        Point3d a = new(1.0, 2.0, 3.0);
        Point3d b = new(4.0, 6.0, 3.0);

        Assert.Equal(5.0, a.DistanceTo(b), 12);
        Assert.Equal(5.0, b.DistanceTo(a), 12);
        Assert.Equal(25.0, a.DistanceSquaredTo(b), 12);
    }

    [Fact]
    public void TheDistanceToAnUnsetPointIsNaN()
    {
        Assert.True(double.IsNaN(Point3d.Origin.DistanceTo(Point3d.Unset)));
    }

    [Fact]
    public void TheMidpointIsHalfwayBetweenTwoPoints()
    {
        Point3d midpoint = new Point3d(0.0, 0.0, 0.0).Midpoint(new Point3d(2.0, 4.0, 6.0));

        Assert.Equal(new Point3d(1.0, 2.0, 3.0), midpoint);
    }

    [Fact]
    public void LerpReachesEachEndpointAndExtrapolatesBeyondThem()
    {
        Point3d start = new(0.0, 0.0, 0.0);
        Point3d end = new(10.0, 0.0, 0.0);

        Assert.Equal(start, Point3d.Lerp(start, end, 0.0));
        Assert.Equal(end, Point3d.Lerp(start, end, 1.0));
        Assert.Equal(new Point3d(5.0, 0.0, 0.0), Point3d.Lerp(start, end, 0.5));
        Assert.Equal(new Point3d(20.0, 0.0, 0.0), Point3d.Lerp(start, end, 2.0));
        Assert.Equal(new Point3d(-10.0, 0.0, 0.0), Point3d.Lerp(start, end, -1.0));
    }

    [Fact]
    public void SubtractingTwoPointsGivesTheVectorBetweenThem()
    {
        Point3d from = new(1.0, 1.0, 1.0);
        Point3d to = new(4.0, 1.0, 1.0);

        Vector3d displacement = to - from;

        Assert.Equal(new Vector3d(3.0, 0.0, 0.0), displacement);
        Assert.Equal(to, from + displacement);
        Assert.Equal(from, to - displacement);
    }

    [Fact]
    public void ConvertingBetweenAPointAndAVectorRequiresAnExplicitCast()
    {
        Point3d point = new(1.0, 2.0, 3.0);

        Assert.Equal(new Vector3d(1.0, 2.0, 3.0), (Vector3d)point);
        Assert.Equal(point, (Point3d)new Vector3d(1.0, 2.0, 3.0));
        Assert.Equal((Vector3d)point, point.ToVector3d());
    }

    [Fact]
    public void EqualityIsExactAndNotFuzzy()
    {
        Point3d a = new(1.0, 0.0, 0.0);
        Point3d b = new(1.0 + 1e-12, 0.0, 0.0);

        Assert.False(a == b);
        Assert.True(a != b);
        Assert.True(a.EqualsWithin(b));
    }

    [Fact]
    public void EqualsWithinMeasuresDistanceRatherThanComparingComponentsSeparately()
    {
        // Each component is inside the default tolerance, but the point is not: a spherical
        // test rejects this and a per-component box test would not.
        Point3d a = Point3d.Origin;
        Point3d b = new(9e-7, 9e-7, 9e-7);

        Assert.False(a.EqualsWithin(b));
        Assert.True(a.EqualsWithin(b, Tolerance.Default.Scaled(10.0)));
    }

    [Fact]
    public void EqualsWithinIsScaleAwareAndDoesNotDegenerateIntoBitEquality()
    {
        // At 1e12 the spacing between adjacent doubles is larger than the default linear
        // tolerance, so an absolute-only comparison answers "equal" only for identical bits.
        Point3d far = new(1e12, 0.0, 0.0);

        Assert.True(far.EqualsWithin(new Point3d(1e12 + 0.5, 0.0, 0.0)));
        Assert.False(far.EqualsWithin(new Point3d(1e12 + 500.0, 0.0, 0.0)));

        // The absolute term still governs near the origin, so small coordinates keep the
        // strict behaviour they had.
        Assert.False(Point3d.Origin.EqualsWithin(new Point3d(1e-3, 0.0, 0.0)));
        Assert.True(Point3d.Origin.EqualsWithin(new Point3d(1e-9, 0.0, 0.0)));
    }

    [Fact]
    public void EqualsWithinIsFalseForAnUnsetPoint()
    {
        Assert.False(Point3d.Unset.EqualsWithin(Point3d.Unset));
    }

    [Fact]
    public void EqualPointsShareAHashCode()
    {
        Assert.Equal(new Point3d(1.0, 2.0, 3.0).GetHashCode(), new Point3d(1.0, 2.0, 3.0).GetHashCode());
    }

    [Fact]
    public void ToStringUsesTheInvariantCulture()
    {
        Assert.Equal("(1, 2, 3)", new Point3d(1.0, 2.0, 3.0).ToString());
    }
}

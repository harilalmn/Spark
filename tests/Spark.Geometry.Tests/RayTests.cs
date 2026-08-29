using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class RayTests
{
    private static readonly BoundingBox UnitBox = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));

    [Fact]
    public void TheDirectionIsStoredNormalisedSoAParameterIsADistance()
    {
        Ray ray = new(Point3d.Origin, new Vector3d(0.0, 0.0, 7.0));

        Assert.True(ray.Direction.EqualsWithin(Vector3d.ZAxis));
        Assert.True(ray.PointAt(3.0).EqualsWithin(new Point3d(0.0, 0.0, 3.0)));
    }

    [Fact]
    public void ByTwoPointsAimsAtTheSecondPoint()
    {
        Ray ray = Ray.ByTwoPoints(new Point3d(1.0, 1.0, 1.0), new Point3d(1.0, 5.0, 1.0));

        Assert.True(ray.Direction.EqualsWithin(Vector3d.YAxis));
        Assert.True(ray.PointAt(4.0).EqualsWithin(new Point3d(1.0, 5.0, 1.0)));
    }

    [Fact]
    public void ConstructionRejectsWhatIsNotARay()
    {
        Assert.Throws<ArgumentException>(() => new Ray(Point3d.Origin, Vector3d.Zero));
        Assert.Throws<ArgumentException>(
            () => new Ray(new Point3d(double.NaN, 0.0, 0.0), Vector3d.XAxis));
        Assert.Throws<ArgumentException>(() => Ray.ByTwoPoints(Point3d.Origin, Point3d.Origin));
    }

    [Fact]
    public void ADefaultRayRefusesEveryQuery()
    {
        Ray invalid = default;

        Assert.False(invalid.IsValid);
        Assert.Throws<InvalidOperationException>(() => invalid.PointAt(1.0));
        Assert.Throws<InvalidOperationException>(() => invalid.Intersects(UnitBox));
        Assert.Throws<InvalidOperationException>(() => invalid.ClosestPointTo(Point3d.Origin));
    }

    [Fact]
    public void AHitReportsWhereItEntersAndLeaves()
    {
        Ray ray = new(new Point3d(-2.0, 0.5, 0.5), Vector3d.XAxis);

        Assert.True(ray.Intersects(UnitBox, out double entry, out double exit));
        Assert.Equal(2.0, entry, 12);
        Assert.Equal(3.0, exit, 12);
    }

    [Fact]
    public void ARayStartingInsideEntersAtZero()
    {
        Ray ray = new(new Point3d(0.5, 0.5, 0.5), Vector3d.XAxis);

        Assert.True(ray.Intersects(UnitBox, out double entry, out double exit));
        Assert.Equal(0.0, entry);
        Assert.Equal(0.5, exit, 12);
    }

    [Fact]
    public void ABoxBehindTheOriginIsAMissBecauseThisIsARayAndNotALine()
    {
        Ray away = new(new Point3d(-2.0, 0.5, 0.5), -Vector3d.XAxis);

        Assert.False(away.Intersects(UnitBox));

        // The same geometry as a line would hit, which is the whole distinction.
        Assert.True(new Ray(new Point3d(-2.0, 0.5, 0.5), Vector3d.XAxis).Intersects(UnitBox));
    }

    [Fact]
    public void ARayLyingExactlyOnAFaceStillHits()
    {
        // The 0 * infinity case: the direction is parallel to the Z slab and the origin sits
        // exactly on its plane, so the slab test produces NaN. Treating that NaN as a miss is
        // the classic bug in this routine, and a ray grazing a face is common rather than
        // exotic - it is what a click along an edge does.
        Ray grazing = new(new Point3d(-1.0, 0.5, 0.0), Vector3d.XAxis);

        Assert.True(grazing.Intersects(UnitBox, out double entry, out _));
        Assert.Equal(1.0, entry, 12);

        Ray onCorner = new(new Point3d(-1.0, 0.0, 0.0), Vector3d.XAxis);
        Assert.True(onCorner.Intersects(UnitBox));
    }

    [Fact]
    public void AMissIsAMissOnEveryAxis()
    {
        Assert.False(new Ray(new Point3d(-1.0, 2.0, 0.5), Vector3d.XAxis).Intersects(UnitBox));
        Assert.False(new Ray(new Point3d(-1.0, 0.5, 2.0), Vector3d.XAxis).Intersects(UnitBox));
        Assert.False(new Ray(new Point3d(2.0, 2.0, 2.0), Vector3d.XAxis).Intersects(UnitBox));
    }

    [Fact]
    public void AnInvalidBoxIsNeverHit()
    {
        Ray ray = new(Point3d.Origin, Vector3d.XAxis);

        Assert.False(ray.Intersects(BoundingBox.Empty));
        Assert.False(ray.Intersects(new BoundingBox(Point3d.Origin, new Point3d(double.NaN, 1.0, 1.0))));
    }

    [Fact]
    public void ClosestPointToClampsBehindTheOrigin()
    {
        Ray ray = new(Point3d.Origin, Vector3d.XAxis);

        (Point3d ahead, double along) = ray.ClosestPointTo(new Point3d(4.0, 3.0, 0.0));
        Assert.Equal(4.0, along, 12);
        Assert.True(ahead.EqualsWithin(new Point3d(4.0, 0.0, 0.0)));

        // Behind the origin there is no ray, so the origin is the closest point on it.
        (Point3d behind, double clamped) = ray.ClosestPointTo(new Point3d(-4.0, 3.0, 0.0));
        Assert.Equal(0.0, clamped);
        Assert.True(behind.EqualsWithin(Point3d.Origin));
    }

    [Fact]
    public void EqualityIsExactAndToleranceComparisonIsSeparate()
    {
        Ray a = new(Point3d.Origin, Vector3d.XAxis);
        Ray b = new(Point3d.Origin, new Vector3d(2.0, 0.0, 0.0));

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));

        Ray nudged = new(new Point3d(1e-12, 0.0, 0.0), Vector3d.XAxis);
        Assert.False(a == nudged);
        Assert.True(a.EqualsWithin(nudged));
        Assert.True(a != nudged);
    }

    [Fact]
    public void ToStringNamesTheOriginAndDirection()
    {
        Assert.Equal(
            "Ray(Origin=(0, 0, 0), Direction=(1, 0, 0))",
            new Ray(Point3d.Origin, Vector3d.XAxis).ToString());
    }
}

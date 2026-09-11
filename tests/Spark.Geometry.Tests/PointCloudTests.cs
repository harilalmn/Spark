using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="PointCloud"/> (`E2-T21`): a set of points that answers questions about itself.
/// </summary>
/// <remarks>
/// The queries are the k-d tree's, which `PointKdTreeTests` holds to brute force; what is under test
/// here is the cloud: that it copies what it is given, keeps a box that is the points' box, answers
/// through its own points, and stays immutable when it is moved or pruned.
/// </remarks>
public sealed class PointCloudTests
{
    private static Point3d[] Cloud(int count, int seed)
    {
        Random random = new(seed);

        return [.. Enumerable.Range(0, count).Select(_ => new Point3d(
            (random.NextDouble() * 10.0) - 5.0,
            (random.NextDouble() * 10.0) - 5.0,
            (random.NextDouble() * 10.0) - 5.0))];
    }

    /// <summary>The cloud's box is the box of its points.</summary>
    [Fact]
    public void TheBoxIsThePointsBox()
    {
        Point3d[] points = Cloud(500, 21);
        PointCloud cloud = new(points);

        Assert.Equal(500, cloud.Count);
        Assert.Equal(new BoundingBox(points), cloud.BoundingBox);
    }

    /// <summary>
    /// <b>The row.</b> Nearest and within-a-radius answer what a scan of the cloud's own points
    /// answers.
    /// </summary>
    [Fact]
    public void QueriesAnswerWhatAScanAnswers()
    {
        Point3d[] points = Cloud(800, 22);
        PointCloud cloud = new(points);
        Point3d probe = new(0.3, -1.2, 2.2);

        int nearest = Enumerable.Range(0, points.Length).OrderBy(i => points[i].DistanceSquaredTo(probe)).ThenBy(i => i).First();
        int[] within = [.. Enumerable.Range(0, points.Length).Where(i => points[i].DistanceTo(probe) <= 1.5)];

        Assert.Equal(nearest, cloud.NearestTo(probe).Index);
        Assert.Equal(within, cloud.Within(probe, 1.5));
    }

    /// <summary>What it was built from can change and the cloud does not; what it hands out can too.</summary>
    [Fact]
    public void TheCloudCopiesInAndOut()
    {
        Point3d[] points = [new(1, 1, 1), new(2, 2, 2)];
        PointCloud cloud = new(points);

        points[0] = new Point3d(9, 9, 9);
        Point3d[] handedOut = cloud.Points();
        handedOut[1] = new Point3d(7, 7, 7);

        Assert.Equal(new[] { new Point3d(1, 1, 1), new Point3d(2, 2, 2) }, cloud.Points());
    }

    /// <summary>Pruning and moving give new clouds, and leave this one alone.</summary>
    [Fact]
    public void PruningAndMovingMakeNewClouds()
    {
        PointCloud cloud = new([new Point3d(0, 0, 0), new Point3d(0, 0, 0.0001), new Point3d(1, 0, 0)]);

        PointCloud pruned = cloud.PruneDuplicates(0.001);
        PointCloud moved = cloud.TransformedBy(Transform.Translation(new Vector3d(0, 0, 5)));

        Assert.Equal(new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0) }, pruned.Points());
        Assert.Equal(3, cloud.Count);
        Assert.Equal(new Point3d(1, 0, 5), moved.Points()[2]);
        Assert.Equal(new Point3d(1, 0, 0), cloud.Points()[2]);
    }

    /// <summary>An empty cloud has an empty box and answers nothing.</summary>
    [Fact]
    public void AnEmptyCloudIsEmpty()
    {
        PointCloud cloud = new([]);

        Assert.False(cloud.BoundingBox.IsValid);
        Assert.Equal(-1, cloud.NearestTo(Point3d.Origin).Index);
        Assert.Empty(cloud.Within(Point3d.Origin, 1.0));
    }

    /// <summary>A point that is not finite is refused by name.</summary>
    [Fact]
    public void ANonFinitePointIsRefused() =>
        Assert.Throws<ArgumentException>(() => new PointCloud([new Point3d(0, double.NaN, 0)]));
}

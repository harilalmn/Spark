using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The k-d tree over points (`E2-T16`), checked against brute force.
/// </summary>
/// <remarks>
/// <b>The oracle is a linear scan.</b> It cannot be wrong in the ways a tree can - a pruned branch that
/// held the answer, a split that put a point on the wrong side - so every query is asked of both, over
/// random clouds, and must agree exactly, ties broken the same way.
/// </remarks>
public sealed class PointKdTreeTests
{
    private static Point3d[] Cloud(int count, int seed)
    {
        Random random = new(seed);
        Point3d[] points = new Point3d[count];

        for (int i = 0; i < count; i++)
        {
            points[i] = new Point3d(
                (random.NextDouble() * 20.0) - 10.0,
                (random.NextDouble() * 20.0) - 10.0,
                (random.NextDouble() * 20.0) - 10.0);
        }

        return points;
    }

    private static IEnumerable<Point3d> Probes(Point3d[] cloud, int seed)
    {
        Random random = new(seed);

        for (int i = 0; i < 150; i++)
        {
            yield return new Point3d(
                (random.NextDouble() * 24.0) - 12.0,
                (random.NextDouble() * 24.0) - 12.0,
                (random.NextDouble() * 24.0) - 12.0);
        }

        // And exactly on points of the cloud, where the distance is zero and ties are likeliest.
        for (int i = 0; i < cloud.Length; i += 97)
        {
            yield return cloud[i];
        }
    }

    private static (int Index, double Distance) BruteNearest(Point3d[] cloud, Point3d probe)
    {
        int best = -1;
        double bestSquared = double.PositiveInfinity;

        for (int i = 0; i < cloud.Length; i++)
        {
            double squared = cloud[i].DistanceSquaredTo(probe);

            if (squared < bestSquared)
            {
                best = i;
                bestSquared = squared;
            }
        }

        return (best, Math.Sqrt(bestSquared));
    }

    private static int[] BruteNearest(Point3d[] cloud, Point3d probe, int count) =>
        [.. Enumerable.Range(0, cloud.Length)
            .OrderBy(i => cloud[i].DistanceSquaredTo(probe))
            .ThenBy(i => i)
            .Take(count)];

    private static int[] BruteWithin(Point3d[] cloud, Point3d probe, double radius) =>
        [.. Enumerable.Range(0, cloud.Length).Where(i => cloud[i].DistanceSquaredTo(probe) <= radius * radius)];

    /// <summary><b>The row.</b> The nearest point to every probe is the one a linear scan finds.</summary>
    [Fact]
    public void TheNearestPointIsTheOneAScanFinds()
    {
        Point3d[] cloud = Cloud(1000, 1);
        PointKdTree tree = PointKdTree.Build(cloud);

        foreach (Point3d probe in Probes(cloud, 2))
        {
            (int index, double distance) = tree.Nearest(probe);
            (int expectedIndex, double expectedDistance) = BruteNearest(cloud, probe);

            Assert.Equal(expectedIndex, index);
            Assert.Equal(expectedDistance, distance);
        }
    }

    /// <summary>The k nearest points are the scan's, in the same order.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(32)]
    [InlineData(1500)]
    public void TheKNearestPointsAreTheOnesAScanFinds(int count)
    {
        Point3d[] cloud = Cloud(1000, 3);
        PointKdTree tree = PointKdTree.Build(cloud);

        foreach (Point3d probe in Probes(cloud, 4).Take(40))
        {
            Assert.Equal(BruteNearest(cloud, probe, count), tree.Nearest(probe, count));
        }
    }

    /// <summary>Everything within a radius is what the scan finds, the boundary included.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(3.0)]
    [InlineData(40.0)]
    public void EverythingWithinARadiusIsWhatAScanFinds(double radius)
    {
        Point3d[] cloud = Cloud(1000, 5);
        PointKdTree tree = PointKdTree.Build(cloud);

        foreach (Point3d probe in Probes(cloud, 6).Take(60))
        {
            Assert.Equal(BruteWithin(cloud, probe, radius), tree.Within(probe, radius));
        }
    }

    /// <summary>
    /// <b>Duplicates are all found</b>, and the nearest of several at the same place is the first:
    /// the two properties deduplicating points depends on.
    /// </summary>
    [Fact]
    public void DuplicatesAreAllFoundAndTheFirstIsNearest()
    {
        Point3d spot = new(1, 2, 3);
        Point3d[] cloud = [.. Cloud(200, 7), spot, spot, .. Cloud(50, 8), spot];
        PointKdTree tree = PointKdTree.Build(cloud);

        Assert.Equal(new[] { 200, 201, 252 }, tree.Within(spot, 0.0));
        Assert.Equal(200, tree.Nearest(spot).Index);
    }

    /// <summary>A cloud with no spread on an axis, or none at all, still answers like the scan.</summary>
    [Fact]
    public void DegenerateCloudsStillAgreeWithAScan()
    {
        Point3d[] row = [.. Enumerable.Range(0, 300).Select(i => new Point3d(i * 0.1, 0, 0))];
        Point3d[] pile = [.. Enumerable.Repeat(new Point3d(4, 4, 4), 50)];

        foreach (Point3d[] cloud in new[] { row, pile })
        {
            PointKdTree tree = PointKdTree.Build(cloud);

            foreach (Point3d probe in new[] { new Point3d(3.05, 0.2, 0), new Point3d(4, 4, 4), new Point3d(-1, 0, 0) })
            {
                Assert.Equal(BruteNearest(cloud, probe).Index, tree.Nearest(probe).Index);
                Assert.Equal(BruteWithin(cloud, probe, 0.25), tree.Within(probe, 0.25));
            }
        }
    }

    /// <summary>An empty tree answers every question with nothing.</summary>
    [Fact]
    public void AnEmptyTreeAnswersNothing()
    {
        PointKdTree tree = PointKdTree.Build([]);

        Assert.Equal(-1, tree.Nearest(Point3d.Origin).Index);
        Assert.Empty(tree.Nearest(Point3d.Origin, 3));
        Assert.Empty(tree.Within(Point3d.Origin, 1.0));
    }

    /// <summary>A point that is not finite has no place in the tree, and is refused by name.</summary>
    [Fact]
    public void ANonFinitePointIsRefused() =>
        Assert.Throws<ArgumentException>(() => PointKdTree.Build([Point3d.Origin, new Point3d(double.NaN, 0, 0)]));
}

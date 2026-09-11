using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Removing near-duplicate points through the k-d tree (`E2-T16`, step B).
/// </summary>
/// <remarks>
/// <b>The oracle is the O(n²) loop the tree replaces</b>: walk the points in order and keep each one
/// unless a point already kept is within the tolerance. The tree must give exactly that, on random
/// clouds dense enough that clusters are common.
/// </remarks>
public sealed class PointPruneDuplicatesTests
{
    private static List<Point3d> Scan(IReadOnlyList<Point3d> points, double tolerance)
    {
        List<Point3d> kept = [];

        foreach (Point3d point in points)
        {
            bool near = false;

            foreach (Point3d other in kept)
            {
                if (other.DistanceSquaredTo(point) <= tolerance * tolerance)
                {
                    near = true;
                    break;
                }
            }

            if (!near)
            {
                kept.Add(point);
            }
        }

        return kept;
    }

    /// <summary><b>The row.</b> On a dense random cloud the tree keeps exactly what the pairwise scan keeps.</summary>
    [Fact]
    public void TheTreeKeepsWhatAPairwiseScanKeeps()
    {
        Random random = new(11);
        Point3d[] cloud = new Point3d[2000];

        for (int i = 0; i < cloud.Length; i++)
        {
            cloud[i] = new Point3d(random.NextDouble(), random.NextDouble(), random.NextDouble());
        }

        Point3d[] pruned = Point3d.PruneDuplicates(cloud, 0.03);

        Assert.True(pruned.Length < cloud.Length, "a cloud this dense should have lost some points");
        Assert.Equal(Scan(cloud, 0.03), pruned);
    }

    /// <summary>The first of each cluster is kept, and what is kept stays in the input's order.</summary>
    [Fact]
    public void TheFirstOfEachClusterIsKeptInOrder()
    {
        Point3d[] points =
        [
            new(0, 0, 0),
            new(5, 0, 0),
            new(0, 0, 0.0005),
            new(5, 0.0004, 0),
            new(9, 9, 9),
        ];

        Assert.Equal(new[] { new Point3d(0, 0, 0), new Point3d(5, 0, 0), new Point3d(9, 9, 9) }, Point3d.PruneDuplicates(points, 0.001));
    }

    /// <summary>
    /// <b>A chain is judged against what was kept.</b> Each point is closer than the tolerance to the
    /// next; the second goes because the first stayed, and the third stays because the second went.
    /// </summary>
    [Fact]
    public void AChainIsJudgedAgainstWhatWasKept()
    {
        Point3d[] chain = [new(0, 0, 0), new(0.8, 0, 0), new(1.6, 0, 0)];

        Assert.Equal(new[] { new Point3d(0, 0, 0), new Point3d(1.6, 0, 0) }, Point3d.PruneDuplicates(chain, 1.0));
    }

    /// <summary>A tolerance of zero removes exact copies and nothing else.</summary>
    [Fact]
    public void ZeroRemovesExactCopiesOnly()
    {
        Point3d[] points = [new(1, 1, 1), new(1, 1, 1), new(1, 1, 1.000001)];

        Assert.Equal(new[] { new Point3d(1, 1, 1), new Point3d(1, 1, 1.000001) }, Point3d.PruneDuplicates(points));
    }

    /// <summary>A tolerance that is negative or not a number is refused, as is a point that is not finite.</summary>
    [Fact]
    public void NonsenseIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Point3d.PruneDuplicates([Point3d.Origin], -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Point3d.PruneDuplicates([Point3d.Origin], double.NaN));
        Assert.Throws<ArgumentException>(() => Point3d.PruneDuplicates([new Point3d(double.PositiveInfinity, 0, 0)], 0.1));
    }
}

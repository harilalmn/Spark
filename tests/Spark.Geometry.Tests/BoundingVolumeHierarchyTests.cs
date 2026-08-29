using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

public sealed class BoundingVolumeHierarchyTests
{
    [Fact]
    public void AnEmptyHierarchyAnswersNothingRatherThanThrowing()
    {
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(ReadOnlySpan<BoundingBox>.Empty);
        List<int> results = [];

        Assert.Equal(0, tree.Count);
        Assert.False(tree.Bounds.IsValid);

        tree.Query(new Ray(Point3d.Origin, Vector3d.XAxis), results);
        Assert.Empty(results);

        Assert.Equal(-1, tree.NearestTo(Point3d.Origin).Index);
        Assert.Equal(-1, tree.FirstHit(new Ray(Point3d.Origin, Vector3d.XAxis), (_, d) => d).Index);
    }

    [Fact]
    public void TheBoundsCoverEveryItem()
    {
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(Grid(4, 4, 4));

        Assert.Equal(64, tree.Count);
        Assert.True(tree.Bounds.Contains(new BoundingBox(Point3d.Origin, new Point3d(3.5, 3.5, 3.5))));
    }

    [Fact]
    public void ARayQueryReturnsExactlyWhatBruteForceReturns()
    {
        BoundingBox[] boxes = Grid(6, 6, 6);
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        List<int> results = [];

        // Two hundred pseudo-random rays from a fixed seed, so a failure is reproducible. The
        // hierarchy is only worth anything if it answers identically to the loop it replaces,
        // and that is the one assertion that cannot be satisfied by an accidentally-correct
        // traversal.
        Random random = new(20260829);

        for (int i = 0; i < 200; i++)
        {
            Ray ray = RandomRay(random);

            tree.Query(ray, results);

            int[] expected = [.. Enumerable.Range(0, boxes.Length).Where(index => ray.Intersects(boxes[index]))];

            Assert.Equal(expected.OrderBy(x => x), results.OrderBy(x => x));
        }
    }

    [Fact]
    public void ARegionQueryReturnsExactlyWhatBruteForceReturns()
    {
        BoundingBox[] boxes = Grid(6, 6, 6);
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        List<int> results = [];
        Random random = new(4711);

        for (int i = 0; i < 200; i++)
        {
            Point3d corner = new(random.NextDouble() * 6.0, random.NextDouble() * 6.0, random.NextDouble() * 6.0);
            BoundingBox region = new(
                corner,
                corner + new Vector3d(random.NextDouble() * 2.0, random.NextDouble() * 2.0, random.NextDouble() * 2.0));

            tree.Query(region, results);

            int[] expected = [.. Enumerable.Range(0, boxes.Length).Where(index => boxes[index].Intersects(region))];

            Assert.Equal(expected.OrderBy(x => x), results.OrderBy(x => x));
        }
    }

    [Fact]
    public void FirstHitReturnsTheNearestAndLetsTheCallerRejectACandidate()
    {
        BoundingBox[] boxes =
        [
            Cube(new Point3d(1.0, 0.0, 0.0)),
            Cube(new Point3d(3.0, 0.0, 0.0)),
            Cube(new Point3d(5.0, 0.0, 0.0)),
        ];

        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        Ray ray = new(new Point3d(-1.0, 0.25, 0.25), Vector3d.XAxis);

        (int index, double distance) = tree.FirstHit(ray, (_, entry) => entry);
        Assert.Equal(0, index);
        Assert.Equal(2.0, distance, 12);

        // The callback is where the real geometry test goes, and returning null means "the box
        // was a candidate and the thing inside it was not hit".
        (int second, _) = tree.FirstHit(ray, (item, entry) => item == 0 ? null : entry);
        Assert.Equal(1, second);

        Assert.Equal(-1, tree.FirstHit(ray, (_, _) => null).Index);
    }

    [Fact]
    public void FirstHitTakesTheCallersDistanceRatherThanTheBoxDistance()
    {
        // The nearest box is not always the nearest hit: a large box entered early can hold
        // geometry that is hit late. A traversal that stopped at the first box it met would
        // return the wrong answer here, and it is the reason FirstHit prunes on the reported
        // distance rather than on the box.
        BoundingBox[] boxes =
        [
            new(new Point3d(0.0, -1.0, -1.0), new Point3d(10.0, 1.0, 1.0)),
            Cube(new Point3d(2.0, 0.0, 0.0)),
        ];

        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        Ray ray = new(new Point3d(-1.0, 0.25, 0.25), Vector3d.XAxis);

        (int index, double distance) = tree.FirstHit(ray, (item, entry) => item == 0 ? 9.0 : entry);

        Assert.Equal(1, index);
        Assert.Equal(3.0, distance, 12);
    }

    [Fact]
    public void NearestToAgreesWithBruteForce()
    {
        BoundingBox[] boxes = Grid(5, 5, 5);
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        Random random = new(90210);

        for (int i = 0; i < 200; i++)
        {
            Point3d probe = new(
                (random.NextDouble() * 12.0) - 3.0,
                (random.NextDouble() * 12.0) - 3.0,
                (random.NextDouble() * 12.0) - 3.0);

            (int index, double distance) = tree.NearestTo(probe);

            double expected = boxes.Min(box => box.ClosestPoint(probe).DistanceTo(probe));

            Assert.True(index >= 0);
            Assert.Equal(expected, distance, 9);
        }
    }

    [Fact]
    public void InvalidBoxesKeepTheirIndexAndAreNeverReturned()
    {
        // Dropping them at build time would renumber everything after them, and the caller's
        // array would silently stop lining up with the answers.
        BoundingBox[] boxes =
        [
            Cube(new Point3d(1.0, 0.0, 0.0)),
            BoundingBox.Empty,
            Cube(new Point3d(2.0, 0.0, 0.0)),
        ];

        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        List<int> results = [];

        Assert.Equal(3, tree.Count);

        tree.Query(new Ray(new Point3d(-1.0, 0.25, 0.25), Vector3d.XAxis), results);
        Assert.Equal([0, 2], results.OrderBy(x => x));

        Assert.DoesNotContain(1, results);
        Assert.NotEqual(1, tree.NearestTo(new Point3d(1.5, 0.0, 0.0)).Index);
    }

    [Fact]
    public void AThousandCoincidentBoxesDoNotDegenerateTheTree()
    {
        // This is the input that turns a coordinate-median split into a linked list, and the
        // reason the split is a median on the index instead. Depth is bounded, so node count
        // stays linear rather than quadratic.
        BoundingBox[] boxes = [.. Enumerable.Repeat(Cube(new Point3d(1.0, 1.0, 1.0)), 1000)];

        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);
        List<int> results = [];

        Assert.Equal(1000, tree.Count);
        Assert.True(tree.NodeCount < 1000, $"node count {tree.NodeCount} suggests a degenerate tree");

        tree.Query(new Ray(new Point3d(-1.0, 1.25, 1.25), Vector3d.XAxis), results);
        Assert.Equal(1000, results.Count);
    }

    [Fact]
    public void QueriesAreSafeFromManyThreadsAtOnce()
    {
        // The evaluator runs a level's nodes in parallel, so anything it can reach has to expect
        // concurrent readers. No query touches instance state; this is what says so.
        BoundingBox[] boxes = Grid(5, 5, 5);
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(boxes);

        Parallel.For(0, 64, i =>
        {
            List<int> results = [];
            Ray ray = new(new Point3d(-1.0, (i % 5) + 0.25, ((i / 5) % 5) + 0.25), Vector3d.XAxis);

            tree.Query(ray, results);

            int[] expected = [.. Enumerable.Range(0, boxes.Length).Where(index => ray.Intersects(boxes[index]))];

            Assert.Equal(expected.OrderBy(x => x), results.OrderBy(x => x));
        });
    }

    [Fact]
    public void BuildingFromAListMatchesBuildingFromASpan()
    {
        BoundingBox[] boxes = Grid(3, 3, 3);

        BoundingVolumeHierarchy fromSpan = BoundingVolumeHierarchy.Build(boxes.AsSpan());
        BoundingVolumeHierarchy fromList = BoundingVolumeHierarchy.Build(boxes.ToList());

        Assert.Equal(fromSpan.Count, fromList.Count);
        Assert.Equal(fromSpan.NodeCount, fromList.NodeCount);
        Assert.Throws<ArgumentNullException>(() => BoundingVolumeHierarchy.Build((IReadOnlyList<BoundingBox>)null!));
    }

    [Fact]
    public void QueriesRejectANullResultList()
    {
        BoundingVolumeHierarchy tree = BoundingVolumeHierarchy.Build(Grid(2, 2, 2));

        Assert.Throws<ArgumentNullException>(() => tree.Query(new Ray(Point3d.Origin, Vector3d.XAxis), null!));
        Assert.Throws<ArgumentNullException>(() => tree.Query(BoundingBox.Empty, null!));
        Assert.Throws<ArgumentNullException>(() => tree.FirstHit(new Ray(Point3d.Origin, Vector3d.XAxis), null!));
    }

    private static BoundingBox Cube(in Point3d corner) => new(corner, corner + new Vector3d(0.5, 0.5, 0.5));

    private static BoundingBox[] Grid(int x, int y, int z)
    {
        List<BoundingBox> boxes = new(x * y * z);

        for (int i = 0; i < x; i++)
        {
            for (int j = 0; j < y; j++)
            {
                for (int k = 0; k < z; k++)
                {
                    boxes.Add(Cube(new Point3d(i, j, k)));
                }
            }
        }

        return [.. boxes];
    }

    private static Ray RandomRay(Random random)
    {
        Point3d origin = new(
            (random.NextDouble() * 10.0) - 2.0,
            (random.NextDouble() * 10.0) - 2.0,
            (random.NextDouble() * 10.0) - 2.0);

        Vector3d direction = new(
            (random.NextDouble() * 2.0) - 1.0,
            (random.NextDouble() * 2.0) - 1.0,
            (random.NextDouble() * 2.0) - 1.0);

        return direction.LengthSquared < 1e-9
            ? new Ray(origin, Vector3d.XAxis)
            : new Ray(origin, direction);
    }
}

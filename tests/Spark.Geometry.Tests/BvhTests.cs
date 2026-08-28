using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The hierarchy is checked against the linear scan it exists to replace, because that is the
/// only reference implementation that is obviously correct. A spatial index tested only against
/// hand-written expectations is tested against the cases its author thought of.
/// </summary>
public sealed class BvhTests
{
    private static readonly BoundingBox[] Grid = BuildGrid();

    [Fact]
    public void AnEmptyTreeIsUsableAndAnswersNothing()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build([], box => box);
        List<BoundingBox> results = [];

        Assert.Equal(0, tree.Count);
        Assert.Equal(0, tree.NodeCount);
        Assert.Equal(0, tree.MaximumDepth);
        Assert.Equal(BoundingBox.Empty, tree.Bounds);
        Assert.Equal(0, tree.Hit(new Ray(Point3d.Origin, Vector3d.XAxis), results));
        Assert.Equal(0, tree.Overlapping(new BoundingBox(Point3d.Origin, new Point3d(1.0, 1.0, 1.0)), results));
        Assert.False(tree.TryFindNearest(Point3d.Origin, _ => 0.0, out _, out double distance));
        Assert.Equal(double.PositiveInfinity, distance);
    }

    [Fact]
    public void ItemsWithAnInvalidBoxAreDroppedRatherThanRejectingTheWholeList()
    {
        BoundingBox[] items =
        [
            new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0)),
            BoundingBox.Empty,
            new(Point3d.Unset, new Point3d(1.0, 1.0, 1.0)),
            new(new Point3d(5.0, 5.0, 5.0), new Point3d(6.0, 6.0, 6.0)),
        ];

        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(items, box => box);

        Assert.Equal(2, tree.Count);
    }

    [Fact]
    public void TheRootBoxContainsEveryItem()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);

        Assert.Equal(Grid.Length, tree.Count);

        foreach (BoundingBox box in Grid)
        {
            Assert.True(tree.Bounds.Contains(box));
        }
    }

    [Fact]
    public void TheTreeIsLogarithmicallyDeepRatherThanALinkedList()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);

        // 1,000 items at four to a leaf need about 250 leaves and a depth near nine; this
        // build gives 543 nodes at depth 11. Both bounds are asserted rather than just the
        // upper one, and the LOWER bound is the one that matters: a tree that collapsed to a
        // single leaf would answer every query in this file correctly, by linear scan, and
        // every other test here would pass while the structure did nothing at all.
        Assert.InRange(tree.MaximumDepth, 9, 24);
        Assert.InRange(tree.NodeCount, Grid.Length / 4, (2 * Grid.Length) - 1);
    }

    [Fact]
    public void ARaySweepAgreesWithTheLinearScanItReplaces()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);

        foreach (Ray ray in SampleRays())
        {
            List<BoundingBox> found = [];
            tree.Hit(ray, found);

            Ray sweep = ray;
            HashSet<BoundingBox> expected = [.. Grid.Where(box => sweep.Intersects(box))];

            Assert.Equal(expected.Count, found.Count);
            Assert.True(expected.SetEquals(found));
        }
    }

    [Fact]
    public void ABoxSweepAgreesWithTheLinearScanItReplaces()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);

        foreach (BoundingBox query in SampleBoxes())
        {
            List<BoundingBox> found = [];
            tree.Overlapping(query, found);

            HashSet<BoundingBox> expected = [.. Grid.Where(box => box.Intersects(query))];

            Assert.Equal(expected.Count, found.Count);
            Assert.True(expected.SetEquals(found));
        }
    }

    [Fact]
    public void TheNearestItemIsTheNearestItemTheLinearScanFinds()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);

        foreach (Point3d point in SamplePoints())
        {
            Point3d from = point;

            Assert.True(tree.TryFindNearest(
                from,
                box => box.ClosestPoint(from).DistanceTo(from),
                out BoundingBox nearest,
                out double distance));

            double best = Grid.Min(box => box.ClosestPoint(from).DistanceTo(from));

            // The item may differ from the linear scan's when two are equidistant - the tie
            // rule is documented as "whichever the traversal reaches first" - so the distance
            // is what must agree, and it must agree exactly rather than within a tolerance.
            Assert.Equal(best, distance);
            Assert.Equal(best, nearest.ClosestPoint(from).DistanceTo(from));
        }
    }

    [Fact]
    public void ResultsAreAppendedRatherThanReplacingWhatIsAlreadyInTheList()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);
        List<BoundingBox> results = [BoundingBox.Empty];

        int added = tree.Overlapping(tree.Bounds, results);

        Assert.Equal(Grid.Length, added);
        Assert.Equal(Grid.Length + 1, results.Count);
        Assert.Equal(BoundingBox.Empty, results[0]);
    }

    [Fact]
    public void ACloudOfCoincidentBoxesStillBuildsAndStillAnswers()
    {
        // Every centroid at the same place: the surface-area heuristic has nothing to measure
        // and no split separates anything. The tree must still be correct, and the leaf it
        // produces must still be scanned.
        BoundingBox one = new(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        BoundingBox[] identical = [.. Enumerable.Repeat(one, 50)];

        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(identical, box => box);
        List<BoundingBox> results = [];

        Assert.Equal(50, tree.Count);
        Assert.Equal(50, tree.Overlapping(one, results));
        List<BoundingBox> hits = [];
        Assert.Equal(50, tree.Hit(new Ray(new Point3d(-5.0, 0.5, 0.5), Vector3d.XAxis), hits));
    }

    [Fact]
    public void OneEnormousItemAmongManySmallOnesDoesNotPoisonTheTree()
    {
        // The case the surface-area heuristic exists for. A median split puts the enormous box
        // in half the tree and every query then descends into it; the SAH keeps it in one leaf.
        List<BoundingBox> items = [.. Grid, new BoundingBox(new Point3d(-1e6, -1e6, -1e6), new Point3d(1e6, 1e6, 1e6))];

        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(items, box => box);
        List<BoundingBox> found = [];

        BoundingBox query = new(new Point3d(0.5, 0.5, 0.5), new Point3d(0.6, 0.6, 0.6));
        tree.Overlapping(query, found);

        Assert.Equal(items.Count(box => box.Intersects(query)), found.Count);
    }

    [Fact]
    public void ManyThreadsMayQueryOneTreeAtOnce()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);
        Ray[] rays = [.. SampleRays()];

        // Nothing is written after the build and every traversal keeps its stack local, so
        // this is a property of the design rather than of luck. The evaluator replicates in
        // parallel and the viewport picks on another thread; the alternative was a lock.
        Parallel.For(0, 200, iteration =>
        {
            Ray ray = rays[iteration % rays.Length];
            List<BoundingBox> found = [];
            tree.Hit(ray, found);

            Assert.Equal(Grid.Count(box => ray.Intersects(box)), found.Count);
        });
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Bvh<BoundingBox> tree = Bvh<BoundingBox>.Build(Grid, box => box);

        Assert.Throws<ArgumentNullException>(() => Bvh<BoundingBox>.Build(null!, box => box));
        Assert.Throws<ArgumentNullException>(() => Bvh<BoundingBox>.Build(Grid, null!));
        Assert.Throws<ArgumentNullException>(() => tree.Hit(new Ray(Point3d.Origin, Vector3d.XAxis), null!));
        Assert.Throws<ArgumentNullException>(() => tree.Overlapping(tree.Bounds, null!));
        Assert.Throws<ArgumentNullException>(() => tree.TryFindNearest(Point3d.Origin, null!, out _, out _));
    }

    private static BoundingBox[] BuildGrid()
    {
        // Ten by ten by ten unit cubes on a two-unit lattice, jittered so that nothing about
        // the tests depends on a perfectly regular arrangement.
        List<BoundingBox> boxes = new(1000);
        Random random = new(20260828);

        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                for (int z = 0; z < 10; z++)
                {
                    Point3d min = new(
                        (2.0 * x) + (random.NextDouble() * 0.5),
                        (2.0 * y) + (random.NextDouble() * 0.5),
                        (2.0 * z) + (random.NextDouble() * 0.5));

                    boxes.Add(new BoundingBox(min, min + new Vector3d(1.0, 1.0, 1.0)));
                }
            }
        }

        return [.. boxes];
    }

    private static IEnumerable<Ray> SampleRays()
    {
        yield return new Ray(new Point3d(-5.0, 0.5, 0.5), Vector3d.XAxis);
        yield return new Ray(new Point3d(-5.0, 9.0, 9.0), Vector3d.XAxis);
        yield return new Ray(new Point3d(9.0, 9.0, -5.0), Vector3d.ZAxis);
        yield return new Ray(new Point3d(-5.0, -5.0, -5.0), new Vector3d(1.0, 1.0, 1.0));
        yield return new Ray(new Point3d(100.0, 100.0, 100.0), Vector3d.XAxis);
        yield return new Ray(new Point3d(9.0, 9.0, 9.0), new Vector3d(-1.0, -0.5, 0.25));
        yield return new Ray(new Point3d(-5.0, 2.0, 2.0), Vector3d.XAxis);
    }

    private static IEnumerable<BoundingBox> SampleBoxes()
    {
        yield return new BoundingBox(Point3d.Origin, new Point3d(1.0, 1.0, 1.0));
        yield return new BoundingBox(new Point3d(-100.0, -100.0, -100.0), new Point3d(100.0, 100.0, 100.0));
        yield return new BoundingBox(new Point3d(50.0, 50.0, 50.0), new Point3d(60.0, 60.0, 60.0));
        yield return new BoundingBox(new Point3d(4.0, 4.0, 4.0), new Point3d(7.0, 7.0, 7.0));
        yield return new BoundingBox(new Point3d(1.4, 1.4, 1.4), new Point3d(1.6, 1.6, 1.6));
    }

    private static IEnumerable<Point3d> SamplePoints()
    {
        yield return Point3d.Origin;
        yield return new Point3d(9.0, 9.0, 9.0);
        yield return new Point3d(-50.0, 3.0, 3.0);
        yield return new Point3d(5.05, 5.05, 5.05);
        yield return new Point3d(1000.0, 1000.0, 1000.0);
        yield return new Point3d(3.0, 12.0, 3.0);
    }
}

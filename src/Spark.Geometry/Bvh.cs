using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// A bounding-volume hierarchy over any items that can be given a box: the acceleration
/// structure behind ray casting, picking, proximity queries and intersection seeding.
/// </summary>
/// <typeparam name="T">
/// The item type. The tree stores items by reference to the array it built from and never
/// interprets them; everything it knows about an item is the box supplied at build time.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>This is a broad phase and it is only a broad phase.</b> Every query here answers a
/// question about <i>boxes</i>: which items have a box the ray crosses, which have a box
/// overlapping this one. Whether the ray actually meets the item inside its box is the
/// caller's narrow phase, because the tree does not know what a <typeparamref name="T"/> is.
/// Saying so in the type documentation rather than leaving it to be discovered is deliberate:
/// a broad phase silently taken for an exact answer is a picking bug that reproduces only at
/// certain camera angles.
/// </para>
/// <para>
/// <b>The tree is immutable once built, and that is what makes it safe to query from many
/// threads.</b> Nothing is cached in a node, no field is written after
/// <see cref="Build(IReadOnlyList{T}, Func{T, BoundingBox})"/> returns, and every traversal
/// keeps its state in a local stack. The evaluator replicates over lists in parallel and the
/// viewport picks on a different thread again; a structure that memoised its last query would
/// have needed a lock on the hottest path in the kernel.
/// </para>
/// <para>
/// <b>Splitting is by binned surface-area heuristic, and it falls back to a median split
/// rather than to a leaf.</b> The SAH is what makes a hierarchy over unevenly distributed
/// geometry worth building at all — a median split on a model with one enormous element and
/// ten thousand small ones puts the enormous one in half the tree. But the SAH can also
/// legitimately fail to beat a leaf on a cluster of coincident boxes, and stopping there would
/// leave a leaf of arbitrary size that every query then scans linearly. The fallback bounds
/// the damage at a median split, which is never worse than linear.
/// </para>
/// </remarks>
public sealed class Bvh<T>
{
    // Items below this count are a leaf outright: at four items a linear scan beats the two
    // box tests plus the stack traffic a split would cost, and the node count halves.
    private const int LeafThreshold = 4;

    // Bins for the surface-area heuristic. Twelve is the usual answer and the sweep is O(bins)
    // per axis rather than O(items log items), which is what makes it cheap enough to always do.
    private const int Bins = 12;

    private readonly Node[] _nodes;
    private readonly T[] _items;
    private readonly BoundingBox[] _bounds;

    private Bvh(Node[] nodes, T[] items, BoundingBox[] bounds, int maximumDepth)
    {
        _nodes = nodes;
        _items = items;
        _bounds = bounds;
        MaximumDepth = maximumDepth;
    }

    /// <summary>The number of items in the tree.</summary>
    public int Count => _items.Length;

    /// <summary>The number of nodes, leaves included. Zero for an empty tree.</summary>
    public int NodeCount => _nodes.Length;

    /// <summary>
    /// The deepest path from the root to a leaf, counted in nodes. Zero for an empty tree,
    /// one for a tree that is a single leaf.
    /// </summary>
    /// <remarks>
    /// Exposed because it is the one number that says whether a build went badly. A tree over
    /// n items should be around log2(n) deep; a tree that is n deep is a linked list wearing a
    /// hierarchy's name, and every query on it is linear.
    /// </remarks>
    public int MaximumDepth { get; }

    /// <summary>
    /// The box containing every item, or <see cref="BoundingBox.Empty"/> for an empty tree.
    /// </summary>
    public BoundingBox Bounds => _nodes.Length == 0 ? BoundingBox.Empty : _nodes[0].Bounds;

    /// <summary>
    /// Builds a hierarchy over a list of items.
    /// </summary>
    /// <param name="items">The items. The list is read once and not retained.</param>
    /// <param name="bounds">
    /// The box for an item. Called exactly once per item, so it may be as expensive as it
    /// needs to be.
    /// </param>
    /// <returns>The tree.</returns>
    /// <remarks>
    /// <b>An item whose box is invalid is dropped, not rejected.</b> A degenerate element in
    /// a list of ten thousand is a normal occurrence in a model somebody is still building,
    /// and refusing to index the other 9,999 because of it would make the structure unusable
    /// on exactly the inputs that need it most. <see cref="Count"/> reports how many were
    /// kept, so a caller who cares can compare it against the list length.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="items"/> or <paramref name="bounds"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static Bvh<T> Build(IReadOnlyList<T> items, Func<T, BoundingBox> bounds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(bounds);

        List<T> kept = new(items.Count);
        List<BoundingBox> keptBounds = new(items.Count);

        for (int index = 0; index < items.Count; index++)
        {
            BoundingBox box = bounds(items[index]);

            if (box.IsValid)
            {
                kept.Add(items[index]);
                keptBounds.Add(box);
            }
        }

        if (kept.Count == 0)
        {
            return new Bvh<T>([], [], [], 0);
        }

        T[] ordered = [.. kept];
        BoundingBox[] orderedBounds = [.. keptBounds];

        // Centroids are what the split is decided on, and they are computed once here rather
        // than re-derived from the boxes at every level of the recursion.
        Point3d[] centroids = new Point3d[ordered.Length];

        for (int index = 0; index < ordered.Length; index++)
        {
            centroids[index] = orderedBounds[index].Centre;
        }

        // A binary tree over n leaves with at least one item each has at most 2n - 1 nodes.
        List<Node> nodes = new(Math.Max(1, (2 * ordered.Length) - 1));
        int depth = Split(nodes, ordered, orderedBounds, centroids, 0, ordered.Length, 1);

        return new Bvh<T>([.. nodes], ordered, orderedBounds, depth);
    }

    /// <summary>
    /// Collects every item whose box the ray crosses.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="results">
    /// The list to add to. It is <b>not</b> cleared first, so a caller sweeping many rays can
    /// accumulate into one list; clear it yourself if that is not what you want.
    /// </param>
    /// <returns>The number of items added.</returns>
    /// <remarks>
    /// Results are in the tree's own order, which is neither the input order nor sorted by
    /// distance. Sorting here would cost every caller who does not need it, and the callers
    /// who do need it are doing a narrow phase anyway and will have better distances than the
    /// box entry distance by the time they sort.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="results"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="ray"/> is not valid.
    /// </exception>
    public int Hit(in Ray ray, List<T> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (_nodes.Length == 0)
        {
            return 0;
        }

        int added = 0;
        // Depth-first, and the stack never exceeds the depth: each pop pushes two and
        // descends one, so one pending sibling accumulates per level. Local, so the traversal
        // holds no state on the instance and many threads may query at once.
        Span<int> stack = MaximumDepth < 64 ? stackalloc int[64] : new int[MaximumDepth + 1];
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            Node node = _nodes[stack[--top]];

            if (!ray.Intersects(node.Bounds))
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int index = node.Start; index < node.Start + node.Count; index++)
                {
                    if (ray.Intersects(_bounds[index]))
                    {
                        results.Add(_items[index]);
                        added++;
                    }
                }

                continue;
            }

            stack[top++] = node.Start;
            stack[top++] = node.RightChild;
        }

        return added;
    }

    /// <summary>
    /// Collects every item whose box overlaps a given box.
    /// </summary>
    /// <param name="box">The box to test against.</param>
    /// <param name="results">The list to add to. It is not cleared first.</param>
    /// <param name="tolerance">
    /// The tolerance for the overlap test, passed straight to
    /// <see cref="BoundingBox.Intersects(in BoundingBox, in Tolerance)"/>. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>The number of items added.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="results"/> is <see langword="null"/>.
    /// </exception>
    public int Overlapping(in BoundingBox box, List<T> results, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (_nodes.Length == 0 || !box.IsValid)
        {
            return 0;
        }

        int added = 0;
        Span<int> stack = MaximumDepth < 64 ? stackalloc int[64] : new int[MaximumDepth + 1];
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            Node node = _nodes[stack[--top]];

            if (!node.Bounds.Intersects(box, tolerance))
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int index = node.Start; index < node.Start + node.Count; index++)
                {
                    if (_bounds[index].Intersects(box, tolerance))
                    {
                        results.Add(_items[index]);
                        added++;
                    }
                }

                continue;
            }

            stack[top++] = node.Start;
            stack[top++] = node.RightChild;
        }

        return added;
    }

    /// <summary>
    /// Finds the item nearest a point, by a distance the caller defines.
    /// </summary>
    /// <param name="point">The point to search from.</param>
    /// <param name="distanceTo">
    /// The true distance from <paramref name="point"/> to an item. This is the narrow phase,
    /// and it must never return less than the distance to that item's box — the search prunes
    /// on the box distance, so a narrow phase that reports a distance shorter than
    /// geometrically possible makes the pruning unsound and the answer wrong.
    /// </param>
    /// <param name="nearest">The nearest item, or <c>default</c> when the tree is empty.</param>
    /// <param name="distance">
    /// The distance to it, or <see cref="double.PositiveInfinity"/> when the tree is empty.
    /// </param>
    /// <returns><see langword="true"/> when an item was found.</returns>
    /// <remarks>
    /// <b>Ties are broken by whichever item the traversal reaches first, which is not the
    /// input order.</b> Two items at exactly the same distance are a real situation — a point
    /// equidistant from two segments of a polyline meeting at a corner — and no rule for
    /// choosing between them is more correct than another. Callers who need determinism across
    /// builds must break the tie themselves.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="distanceTo"/> is <see langword="null"/>.
    /// </exception>
    public bool TryFindNearest(
        in Point3d point,
        Func<T, double> distanceTo,
        out T? nearest,
        out double distance)
    {
        ArgumentNullException.ThrowIfNull(distanceTo);

        nearest = default;
        distance = double.PositiveInfinity;

        if (_nodes.Length == 0 || !point.IsValid)
        {
            return false;
        }

        bool found = false;
        Span<int> stack = MaximumDepth < 64 ? stackalloc int[64] : new int[MaximumDepth + 1];
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            Node node = _nodes[stack[--top]];

            // The box distance is a lower bound on the distance to anything inside it, so a
            // node no closer than the best so far cannot contain a better answer. This is the
            // entire optimisation, and it is why distanceTo must not undercut the box.
            if (BoxDistance(node.Bounds, point) >= distance)
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int index = node.Start; index < node.Start + node.Count; index++)
                {
                    if (BoxDistance(_bounds[index], point) >= distance)
                    {
                        continue;
                    }

                    double candidate = distanceTo(_items[index]);

                    if (candidate < distance)
                    {
                        distance = candidate;
                        nearest = _items[index];
                        found = true;
                    }
                }

                continue;
            }

            // Descend into the nearer child first: it is the one likeliest to tighten the
            // bound, and a tighter bound is what lets the other subtree be rejected whole.
            int left = node.Start;
            int right = node.RightChild;

            if (BoxDistance(_nodes[left].Bounds, point) < BoxDistance(_nodes[right].Bounds, point))
            {
                stack[top++] = right;
                stack[top++] = left;
            }
            else
            {
                stack[top++] = left;
                stack[top++] = right;
            }
        }

        return found;
    }

    private static double BoxDistance(in BoundingBox box, in Point3d point) =>
        box.ClosestPoint(point).DistanceTo(point);

    // Builds the subtree over items[from, to) and returns the depth of the deepest path
    // through it. Items are reordered in place; a node then names a contiguous range.
    private static int Split(
        List<Node> nodes,
        T[] items,
        BoundingBox[] bounds,
        Point3d[] centroids,
        int from,
        int to,
        int depth)
    {
        int self = nodes.Count;
        int count = to - from;
        BoundingBox box = BoundsOf(bounds, from, to);

        nodes.Add(new Node(box, from, count, 0));

        if (count <= LeafThreshold)
        {
            return depth;
        }

        int middle = ChooseSplit(items, bounds, centroids, from, to, box);

        if (middle <= from || middle >= to)
        {
            return depth;
        }

        // The node stops being a leaf. Start is reused as the left child's index, which is
        // what keeps a node to four fields; Count of zero is what says to read it that way.
        int left = nodes.Count;
        int leftDepth = Split(nodes, items, bounds, centroids, from, middle, depth + 1);
        int right = nodes.Count;
        int rightDepth = Split(nodes, items, bounds, centroids, middle, to, depth + 1);

        nodes[self] = new Node(box, left, 0, right);

        return Math.Max(leftDepth, rightDepth);
    }

    // Partitions [from, to) and returns the split index, or from/to to say "leave it a leaf".
    private static int ChooseSplit(
        T[] items,
        BoundingBox[] bounds,
        Point3d[] centroids,
        int from,
        int to,
        in BoundingBox box)
    {
        BoundingBox centroidBox = BoundingBox.Empty;

        for (int index = from; index < to; index++)
        {
            centroidBox = centroidBox.Union(centroids[index]);
        }

        Vector3d extent = centroidBox.Diagonal;
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        double width = Component(extent, axis);

        if (width <= 0.0)
        {
            // Every centroid is at the same place, so no split separates anything and the SAH
            // has nothing to measure. A leaf is the honest answer; the LeafThreshold is not a
            // guarantee of leaf size and never was.
            return from;
        }

        double origin = Component((Vector3d)centroidBox.Min, axis);

        Span<int> counts = stackalloc int[Bins];
        Span<BoundingBox> binBounds = stackalloc BoundingBox[Bins];

        for (int bin = 0; bin < Bins; bin++)
        {
            binBounds[bin] = BoundingBox.Empty;
        }

        for (int index = from; index < to; index++)
        {
            int bin = BinOf(Component((Vector3d)centroids[index], axis), origin, width);
            counts[bin]++;
            binBounds[bin] = binBounds[bin].Union(bounds[index]);
        }

        // Sweep from the left accumulating, then from the right, so that the cost of every one
        // of the eleven candidate planes is known after two linear passes rather than eleven.
        Span<double> leftCost = stackalloc double[Bins];
        Span<double> rightCost = stackalloc double[Bins];

        BoundingBox running = BoundingBox.Empty;
        int runningCount = 0;

        for (int bin = 0; bin < Bins; bin++)
        {
            running = running.Union(binBounds[bin]);
            runningCount += counts[bin];
            leftCost[bin] = runningCount * SurfaceArea(running);
        }

        running = BoundingBox.Empty;
        runningCount = 0;

        for (int bin = Bins - 1; bin >= 0; bin--)
        {
            running = running.Union(binBounds[bin]);
            runningCount += counts[bin];
            rightCost[bin] = runningCount * SurfaceArea(running);
        }

        int best = -1;
        double bestCost = double.PositiveInfinity;

        for (int bin = 0; bin < Bins - 1; bin++)
        {
            double cost = leftCost[bin] + rightCost[bin + 1];

            if (cost < bestCost)
            {
                bestCost = cost;
                best = bin;
            }
        }

        int middle = best < 0
            ? from
            : Partition(items, bounds, centroids, from, to, axis, origin, width, best);

        // The SAH can lose to a leaf on coincident or heavily overlapping boxes, and it can
        // also choose a plane every centroid falls on one side of. Either way a median split
        // is the fallback rather than a leaf: an oversized leaf is scanned linearly by every
        // query for the life of the tree, and a median split is never worse than that.
        if (middle <= from || middle >= to)
        {
            middle = from + ((to - from) / 2);
            MedianPartition(items, bounds, centroids, from, to, axis, middle);
        }

        return middle;
    }

    private static int Partition(
        T[] items,
        BoundingBox[] bounds,
        Point3d[] centroids,
        int from,
        int to,
        int axis,
        double origin,
        double width,
        int lastLeftBin)
    {
        int left = from;
        int right = to - 1;

        while (left <= right)
        {
            if (BinOf(Component((Vector3d)centroids[left], axis), origin, width) <= lastLeftBin)
            {
                left++;
            }
            else
            {
                Swap(items, bounds, centroids, left, right);
                right--;
            }
        }

        return left;
    }

    private static void MedianPartition(
        T[] items,
        BoundingBox[] bounds,
        Point3d[] centroids,
        int from,
        int to,
        int axis,
        int middle)
    {
        // Quickselect: the range only has to be partitioned about the median, never sorted.
        int low = from;
        int high = to - 1;

        while (low < high)
        {
            double pivot = Component((Vector3d)centroids[(low + high) / 2], axis);
            int left = low;
            int right = high;

            while (left <= right)
            {
                while (Component((Vector3d)centroids[left], axis) < pivot)
                {
                    left++;
                }

                while (Component((Vector3d)centroids[right], axis) > pivot)
                {
                    right--;
                }

                if (left <= right)
                {
                    Swap(items, bounds, centroids, left, right);
                    left++;
                    right--;
                }
            }

            if (middle <= right)
            {
                high = right;
            }
            else if (middle >= left)
            {
                low = left;
            }
            else
            {
                return;
            }
        }
    }

    // The three arrays are parallel and must stay that way: a node names a contiguous RANGE
    // of indices, so an item that moved without its box moved into somebody else's node.
    private static void Swap(T[] items, BoundingBox[] bounds, Point3d[] centroids, int first, int second)
    {
        (items[first], items[second]) = (items[second], items[first]);
        (bounds[first], bounds[second]) = (bounds[second], bounds[first]);
        (centroids[first], centroids[second]) = (centroids[second], centroids[first]);
    }

    private static BoundingBox BoundsOf(BoundingBox[] bounds, int from, int to)
    {
        BoundingBox box = BoundingBox.Empty;

        for (int index = from; index < to; index++)
        {
            box = box.Union(bounds[index]);
        }

        return box;
    }

    private static int BinOf(double coordinate, double origin, double width)
    {
        int bin = (int)(Bins * ((coordinate - origin) / width));

        return Math.Clamp(bin, 0, Bins - 1);
    }

    private static double Component(in Vector3d vector, int axis) =>
        axis == 0 ? vector.X : axis == 1 ? vector.Y : vector.Z;

    private static double SurfaceArea(in BoundingBox box)
    {
        if (!box.IsValid)
        {
            return 0.0;
        }

        Vector3d size = box.Diagonal;

        return 2.0 * ((size.X * size.Y) + (size.Y * size.Z) + (size.Z * size.X));
    }

    // Bounds plus either a range of items or a pair of children. Count distinguishes them:
    // a leaf has Count > 0 and Start is the first item; an interior node has Count == 0 and
    // Start is the left child while RightChild is the right. Two node kinds in one struct
    // rather than two arrays, because the traversal touches both fields on every visit and a
    // second indirection would cost more than the wasted field.
    private readonly struct Node(BoundingBox bounds, int start, int count, int rightChild)
    {
        public BoundingBox Bounds { get; } = bounds;

        public int Start { get; } = start;

        public int Count { get; } = count;

        public int RightChild { get; } = rightChild;
    }
}

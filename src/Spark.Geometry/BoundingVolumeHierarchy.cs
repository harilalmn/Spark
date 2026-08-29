using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// A binary tree of bounding boxes over a fixed set of items, so that *what does this ray hit*
/// and *what is in this region* stop being questions with a linear answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>It indexes boxes, not geometry.</b> Build it over the bounds of whatever you have —
/// triangles, curves, canvas nodes, whole objects — and it answers with the <b>indices</b> of the
/// items whose boxes are candidates. A box query is a conservative filter and never the final
/// answer: the caller still tests the real geometry of the few items that come back. Keeping the
/// hierarchy ignorant of what it indexes is what lets one implementation serve mesh booleans,
/// viewport picking, intersection seeding and closest-point search rather than four.
/// </para>
/// <para>
/// <b>Immutable once built, and therefore safe to query from many threads at once.</b> No query
/// touches instance state: the traversal stack lives on the calling thread's stack. That is a
/// requirement rather than a nicety — the evaluator runs a level's nodes in parallel
/// (<c>ParallelEvaluationScheduler</c>), so any acceleration structure it can reach must expect
/// concurrent readers. There is no incremental update; a changed set is a new hierarchy.
/// </para>
/// <para>
/// <b>The split rule is a median on the index, not on the coordinate</b>, taken along the
/// longest axis of the node's box. That guarantees the two children hold the same number of
/// items to within one, so the depth is <c>ceil(log2 n) + 1</c> for any input at all — including
/// a thousand coincident boxes, which is exactly the input that makes a coordinate-median split
/// degenerate into a linked list. A surface-area heuristic would give faster ray queries on
/// typical scenes and gives up that guarantee; it is the obvious later change, and this is the
/// one that cannot go quadratic.
/// </para>
/// </remarks>
public sealed class BoundingVolumeHierarchy
{
    private const int MaximumLeafSize = 4;

    private readonly Node[] _nodes;
    private readonly int[] _items;
    private readonly BoundingBox[] _boxes;

    private BoundingVolumeHierarchy(Node[] nodes, int[] items, BoundingBox[] boxes)
    {
        _nodes = nodes;
        _items = items;
        _boxes = boxes;
    }

    /// <summary>How many items are indexed.</summary>
    public int Count => _boxes.Length;

    /// <summary>How many nodes the tree holds, leaves included.</summary>
    public int NodeCount => _nodes.Length;

    /// <summary>
    /// The box containing every indexed item, or <see cref="BoundingBox.Empty"/> when there are
    /// none.
    /// </summary>
    public BoundingBox Bounds => _nodes.Length == 0 ? BoundingBox.Empty : _nodes[0].Box;

    /// <summary>
    /// Builds a hierarchy over a set of boxes.
    /// </summary>
    /// <param name="boxes">
    /// The bounds of the items, in the caller's own order. Every query answers with indices into
    /// this sequence, so the caller keeps the mapping from index to item and the hierarchy never
    /// needs to know what an item is.
    /// </param>
    /// <returns>The hierarchy. Building over an empty set is legal and answers nothing.</returns>
    /// <remarks>
    /// **Invalid boxes are indexed rather than rejected**, and never returned by a query: an
    /// item with no geometry yet, or one whose bounds are <see cref="BoundingBox.Empty"/>, keeps
    /// its index so that the caller's array stays aligned. Dropping it here would silently
    /// renumber everything after it, which is the kind of bug that surfaces a long way from its
    /// cause.
    /// </remarks>
    public static BoundingVolumeHierarchy Build(ReadOnlySpan<BoundingBox> boxes)
    {
        BoundingBox[] copy = boxes.ToArray();

        if (copy.Length == 0)
        {
            return new BoundingVolumeHierarchy([], [], copy);
        }

        int[] items = new int[copy.Length];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = i;
        }

        List<Node> nodes = new(Math.Max(1, (copy.Length / MaximumLeafSize) * 2));
        BuildRange(nodes, copy, items, 0, items.Length);

        return new BoundingVolumeHierarchy([.. nodes], items, copy);
    }

    /// <summary>
    /// Builds a hierarchy over a set of boxes.
    /// </summary>
    /// <param name="boxes">The bounds of the items, in the caller's own order.</param>
    /// <returns>The hierarchy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="boxes"/> is null.</exception>
    public static BoundingVolumeHierarchy Build(IReadOnlyList<BoundingBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        BoundingBox[] copy = new BoundingBox[boxes.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = boxes[i];
        }

        return Build(copy.AsSpan());
    }

    /// <summary>
    /// Finds every item whose box the ray meets.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="results">
    /// Cleared, then filled with the indices of the candidates. Passing a reused list is what
    /// makes a per-frame query allocation-free.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the ray is not valid.</exception>
    /// <remarks>
    /// The order is unspecified and deliberately not distance-sorted: sorting costs more than the
    /// caller's own geometry test in the common case where two or three candidates come back.
    /// <see cref="FirstHit(in Ray, Func{int, double, double?})"/> is for when the nearest one is
    /// what you actually want.
    /// </remarks>
    public void Query(in Ray ray, List<int> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        results.Clear();

        if (_nodes.Length == 0)
        {
            return;
        }

        Span<int> stack = stackalloc int[64];
        int depth = 0;
        stack[depth++] = 0;

        while (depth > 0)
        {
            Node node = _nodes[stack[--depth]];

            if (!ray.Intersects(node.Box))
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int item = _items[i];
                    if (ray.Intersects(_boxes[item]))
                    {
                        results.Add(item);
                    }
                }

                continue;
            }

            stack[depth++] = node.Left;
            stack[depth++] = node.Right;
        }
    }

    /// <summary>
    /// Finds every item whose box overlaps a region.
    /// </summary>
    /// <param name="region">The region to search.</param>
    /// <param name="results">Cleared, then filled with the indices of the overlapping items.</param>
    /// <param name="tolerance">
    /// The tolerance used for the overlap test, matching
    /// <see cref="BoundingBox.Intersects(in BoundingBox, in Tolerance)"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is null.</exception>
    public void Query(in BoundingBox region, List<int> results, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(results);

        results.Clear();

        if (_nodes.Length == 0 || !region.IsValid)
        {
            return;
        }

        Span<int> stack = stackalloc int[64];
        int depth = 0;
        stack[depth++] = 0;

        while (depth > 0)
        {
            Node node = _nodes[stack[--depth]];

            if (!node.Box.Intersects(region, tolerance))
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int item = _items[i];
                    if (_boxes[item].Intersects(region, tolerance))
                    {
                        results.Add(item);
                    }
                }

                continue;
            }

            stack[depth++] = node.Left;
            stack[depth++] = node.Right;
        }
    }

    /// <summary>
    /// Finds the nearest thing the ray actually hits, letting the caller decide what a hit is.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="hit">
    /// Called for each candidate index with the distance at which the ray enters that item's
    /// box. Returns the true hit distance along the ray, or <see langword="null"/> when the
    /// item is not hit after all — which is where a triangle test, a curve test or a
    /// pixel-accurate test belongs.
    /// </param>
    /// <returns>
    /// The index of the nearest hit and its distance, or <c>(-1, <see cref="double.NaN"/>)</c>
    /// when nothing was hit.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hit"/> is null.</exception>
    /// <remarks>
    /// **This is the member that makes the hierarchy worth having**, because it is the one that
    /// can prune. A branch whose box is entered beyond the best hit found so far cannot contain
    /// anything nearer, and is skipped without descending — which is the difference between
    /// touching every leaf and touching a handful. <see cref="Query(in Ray, List{int})"/> cannot
    /// do that, because it does not know what counts as a hit.
    /// </remarks>
    public (int Index, double Distance) FirstHit(in Ray ray, Func<int, double, double?> hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        int nearestIndex = -1;
        double nearest = double.PositiveInfinity;

        if (_nodes.Length == 0)
        {
            return (-1, double.NaN);
        }

        Span<int> stack = stackalloc int[64];
        int depth = 0;
        stack[depth++] = 0;

        while (depth > 0)
        {
            Node node = _nodes[stack[--depth]];

            if (!ray.Intersects(node.Box, out double entry, out _) || entry > nearest)
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int item = _items[i];

                    if (!ray.Intersects(_boxes[item], out double itemEntry, out _) || itemEntry > nearest)
                    {
                        continue;
                    }

                    if (hit(item, itemEntry) is double distance && distance >= 0.0 && distance < nearest)
                    {
                        nearest = distance;
                        nearestIndex = item;
                    }
                }

                continue;
            }

            stack[depth++] = node.Left;
            stack[depth++] = node.Right;
        }

        return nearestIndex < 0 ? (-1, double.NaN) : (nearestIndex, nearest);
    }

    /// <summary>
    /// Finds the item whose box is nearest to a point.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>
    /// The index of the nearest item and the distance from the point to its box — zero when the
    /// point is inside it — or <c>(-1, <see cref="double.NaN"/>)</c> when there is nothing to
    /// find.
    /// </returns>
    /// <remarks>
    /// The distance is to the <b>box</b>, not to the geometry inside it, so this is a filter and
    /// a starting bound rather than an answer. Its value is that it prunes: any branch whose box
    /// is further away than the best found so far cannot hold anything nearer.
    /// </remarks>
    public (int Index, double Distance) NearestTo(in Point3d point)
    {
        int nearestIndex = -1;
        double nearest = double.PositiveInfinity;

        if (_nodes.Length == 0 || !point.IsValid)
        {
            return (-1, double.NaN);
        }

        Span<int> stack = stackalloc int[64];
        int depth = 0;
        stack[depth++] = 0;

        while (depth > 0)
        {
            Node node = _nodes[stack[--depth]];

            if (!node.Box.IsValid || DistanceTo(node.Box, point) > nearest)
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int item = _items[i];
                    BoundingBox box = _boxes[item];

                    if (!box.IsValid)
                    {
                        continue;
                    }

                    double distance = DistanceTo(box, point);

                    if (distance < nearest)
                    {
                        nearest = distance;
                        nearestIndex = item;
                    }
                }

                continue;
            }

            stack[depth++] = node.Left;
            stack[depth++] = node.Right;
        }

        return nearestIndex < 0 ? (-1, double.NaN) : (nearestIndex, nearest);
    }

    private static double DistanceTo(in BoundingBox box, in Point3d point) =>
        box.ClosestPoint(point).DistanceTo(point);

    private static int BuildRange(List<Node> nodes, BoundingBox[] boxes, int[] items, int start, int count)
    {
        int index = nodes.Count;
        BoundingBox bounds = BoundingBox.Empty;

        for (int i = start; i < start + count; i++)
        {
            BoundingBox box = boxes[items[i]];
            if (box.IsValid)
            {
                bounds = bounds.Union(box);
            }
        }

        nodes.Add(new Node(bounds, start, count, 0, 0));

        if (count <= MaximumLeafSize)
        {
            return index;
        }

        int axis = LongestAxis(bounds);
        Array.Sort(
            items,
            start,
            count,
            Comparer<int>.Create((a, b) => Centre(boxes[a], axis).CompareTo(Centre(boxes[b], axis))));

        int half = count / 2;
        int left = BuildRange(nodes, boxes, items, start, half);
        int right = BuildRange(nodes, boxes, items, start + half, count - half);

        nodes[index] = new Node(bounds, start, 0, left, right);

        return index;
    }

    private static int LongestAxis(in BoundingBox box)
    {
        if (!box.IsValid)
        {
            return 0;
        }

        Vector3d diagonal = box.Diagonal;

        if (diagonal.X >= diagonal.Y && diagonal.X >= diagonal.Z)
        {
            return 0;
        }

        return diagonal.Y >= diagonal.Z ? 1 : 2;
    }

    private static double Centre(in BoundingBox box, int axis)
    {
        if (!box.IsValid)
        {
            // An invalid box has no centre. Sorting it to one end keeps the comparison a total
            // order, which Array.Sort requires and which NaN would violate.
            return double.PositiveInfinity;
        }

        Point3d centre = box.Centre;

        return axis switch
        {
            0 => centre.X,
            1 => centre.Y,
            _ => centre.Z,
        };
    }

    private readonly record struct Node(BoundingBox Box, int Start, int Count, int Left, int Right);
}

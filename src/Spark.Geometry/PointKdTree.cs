using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// A k-d tree over points: the nearest, the k nearest, and everything within a radius
/// (<c>E2-T16</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Balanced by construction, and implicit.</b> The points are arranged in one index array so that
/// the median of every range, on the axis its depth chooses, sits at the range's middle; the left half
/// holds nothing beyond it on that axis and the right half nothing before it. There are no node
/// objects, so a tree over a million points is two arrays.
/// </para>
/// <para>
/// <b>Answers are deterministic.</b> The nearest point breaks a tie by the lower index, the k nearest
/// are ordered by distance and then by index, and a radius query returns indices in ascending order -
/// so the same cloud and the same question give the same answer on every machine, which a caller
/// deduplicating points relies on.
/// </para>
/// <para>
/// <b>Brute force is the oracle.</b> Every query is tested against a linear scan over random clouds.
/// </para>
/// </remarks>
internal sealed class PointKdTree
{
    private readonly Point3d[] _points;
    private readonly int[] _order;

    private PointKdTree(Point3d[] points, int[] order)
    {
        _points = points;
        _order = order;
    }

    /// <summary>How many points the tree holds.</summary>
    internal int Count => _points.Length;

    /// <summary>Builds a tree over a set of points.</summary>
    /// <param name="points">The points. Their indices are the ones every query answers with.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentException">A point is not finite.</exception>
    internal static PointKdTree Build(IReadOnlyList<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        Point3d[] copy = new Point3d[points.Count];
        int[] order = new int[points.Count];

        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = points[i];

            if (!copy[i].IsValid)
            {
                throw new ArgumentException($"Point {i} is not finite, so it has no place in the tree.", nameof(points));
            }

            order[i] = i;
        }

        BuildRange(copy, order, 0, copy.Length, 0);

        return new PointKdTree(copy, order);
    }

    /// <summary>The point nearest a probe, and how far it is.</summary>
    /// <param name="probe">Where to measure from.</param>
    /// <returns>The index and distance, or <c>(-1, +∞)</c> for an empty tree.</returns>
    internal (int Index, double Distance) Nearest(in Point3d probe)
    {
        Require(probe);

        int best = -1;
        double bestSquared = double.PositiveInfinity;

        NearestIn(0, _points.Length, 0, probe, ref best, ref bestSquared);

        return (best, Math.Sqrt(bestSquared));
    }

    /// <summary>The <paramref name="count"/> points nearest a probe, nearest first.</summary>
    /// <param name="probe">Where to measure from.</param>
    /// <param name="count">How many; fewer come back when the tree holds fewer.</param>
    /// <returns>The indices, by distance and then by index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    internal int[] Nearest(in Point3d probe, int count)
    {
        Require(probe);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0 || _points.Length == 0)
        {
            return [];
        }

        // A max-heap of the best found so far, keyed so the worst is on top: larger distance, then
        // larger index, which is the order a tie is broken against.
        PriorityQueue<int, (double Squared, int Index)> kept = new(
            count + 1,
            Comparer<(double Squared, int Index)>.Create((x, y) =>
            {
                int byDistance = y.Squared.CompareTo(x.Squared);
                return byDistance != 0 ? byDistance : y.Index.CompareTo(x.Index);
            }));

        NearestKIn(0, _points.Length, 0, probe, count, kept);

        List<(double Squared, int Index)> found = new(kept.Count);

        while (kept.TryDequeue(out int index, out (double Squared, int Index) key))
        {
            found.Add((key.Squared, index));
        }

        found.Sort((x, y) =>
        {
            int byDistance = x.Squared.CompareTo(y.Squared);
            return byDistance != 0 ? byDistance : x.Index.CompareTo(y.Index);
        });

        int[] indices = new int[found.Count];

        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = found[i].Index;
        }

        return indices;
    }

    /// <summary>Every point within a radius of a probe, the boundary included.</summary>
    /// <param name="probe">Where to measure from.</param>
    /// <param name="radius">How far. Zero finds the points exactly at the probe.</param>
    /// <returns>The indices, in ascending order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or not finite.</exception>
    internal int[] Within(in Point3d probe, double radius)
    {
        Require(probe);

        if (!double.IsFinite(radius) || radius < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius must be finite and not negative.");
        }

        List<int> found = [];
        WithinIn(0, _points.Length, 0, probe, radius, radius * radius, found);
        found.Sort();

        return [.. found];
    }

    private static void Require(in Point3d probe)
    {
        if (!probe.IsValid)
        {
            throw new ArgumentException("A probe must be finite.", nameof(probe));
        }
    }

    private static double Axis(in Point3d point, int axis) => axis switch
    {
        0 => point.X,
        1 => point.Y,
        _ => point.Z,
    };

    private static void BuildRange(Point3d[] points, int[] order, int low, int high, int depth)
    {
        if (high - low <= 1)
        {
            return;
        }

        int axis = depth % 3;
        int middle = (low + high) >> 1;
        Select(points, order, low, high - 1, middle, axis);

        BuildRange(points, order, low, middle, depth + 1);
        BuildRange(points, order, middle + 1, high, depth + 1);
    }

    /// <summary>Quickselect: puts the element that belongs at <paramref name="k"/> there, smaller before.</summary>
    private static void Select(Point3d[] points, int[] order, int left, int right, int k, int axis)
    {
        while (left < right)
        {
            // Median of three, so a sorted cloud - a row of points - is not the worst case.
            int middle = (left + right) >> 1;
            double a = Axis(points[order[left]], axis);
            double b = Axis(points[order[middle]], axis);
            double c = Axis(points[order[right]], axis);
            double pivot = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

            int i = left;
            int j = right;

            while (i <= j)
            {
                while (Axis(points[order[i]], axis) < pivot)
                {
                    i++;
                }

                while (Axis(points[order[j]], axis) > pivot)
                {
                    j--;
                }

                if (i <= j)
                {
                    (order[i], order[j]) = (order[j], order[i]);
                    i++;
                    j--;
                }
            }

            if (k <= j)
            {
                right = j;
            }
            else if (k >= i)
            {
                left = i;
            }
            else
            {
                return;
            }
        }
    }

    private void NearestIn(int low, int high, int depth, in Point3d probe, ref int best, ref double bestSquared)
    {
        if (low >= high)
        {
            return;
        }

        int middle = (low + high) >> 1;
        int index = _order[middle];
        Point3d point = _points[index];
        double squared = point.DistanceSquaredTo(probe);

        if (squared < bestSquared || (squared == bestSquared && index < best))
        {
            best = index;
            bestSquared = squared;
        }

        int axis = depth % 3;
        double delta = Axis(probe, axis) - Axis(point, axis);

        if (delta < 0.0)
        {
            NearestIn(low, middle, depth + 1, probe, ref best, ref bestSquared);

            if (delta * delta <= bestSquared)
            {
                NearestIn(middle + 1, high, depth + 1, probe, ref best, ref bestSquared);
            }
        }
        else
        {
            NearestIn(middle + 1, high, depth + 1, probe, ref best, ref bestSquared);

            if (delta * delta <= bestSquared)
            {
                NearestIn(low, middle, depth + 1, probe, ref best, ref bestSquared);
            }
        }
    }

    private void NearestKIn(
        int low,
        int high,
        int depth,
        in Point3d probe,
        int count,
        PriorityQueue<int, (double Squared, int Index)> kept)
    {
        if (low >= high)
        {
            return;
        }

        int middle = (low + high) >> 1;
        int index = _order[middle];
        Point3d point = _points[index];
        double squared = point.DistanceSquaredTo(probe);

        if (kept.Count < count)
        {
            kept.Enqueue(index, (squared, index));
        }
        else if (kept.TryPeek(out _, out (double Squared, int Index) worst)
            && (squared < worst.Squared || (squared == worst.Squared && index < worst.Index)))
        {
            kept.Dequeue();
            kept.Enqueue(index, (squared, index));
        }

        int axis = depth % 3;
        double delta = Axis(probe, axis) - Axis(point, axis);
        (int nearLow, int nearHigh, int farLow, int farHigh) = delta < 0.0
            ? (low, middle, middle + 1, high)
            : (middle + 1, high, low, middle);

        NearestKIn(nearLow, nearHigh, depth + 1, probe, count, kept);

        if (kept.Count < count
            || (kept.TryPeek(out _, out (double Squared, int Index) limit) && delta * delta <= limit.Squared))
        {
            NearestKIn(farLow, farHigh, depth + 1, probe, count, kept);
        }
    }

    private void WithinIn(int low, int high, int depth, in Point3d probe, double radius, double radiusSquared, List<int> found)
    {
        if (low >= high)
        {
            return;
        }

        int middle = (low + high) >> 1;
        int index = _order[middle];
        Point3d point = _points[index];

        if (point.DistanceSquaredTo(probe) <= radiusSquared)
        {
            found.Add(index);
        }

        int axis = depth % 3;
        double delta = Axis(probe, axis) - Axis(point, axis);

        if (delta <= radius)
        {
            WithinIn(low, middle, depth + 1, probe, radius, radiusSquared, found);
        }

        if (delta >= -radius)
        {
            WithinIn(middle + 1, high, depth + 1, probe, radius, radiusSquared, found);
        }
    }
}

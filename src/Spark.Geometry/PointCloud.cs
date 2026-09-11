using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// A set of points that answers questions about itself: its extent, the point nearest a probe, the
/// points within a radius, and itself without near duplicates (<c>E2-T21</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type and not a list.</b> A list of points already exists in every graph, so a cloud has to
/// earn its place by what a list cannot do cheaply: it keeps its bounding box instead of walking every
/// point for it, and it answers nearest and within-a-radius through a k-d tree (<c>E2-T16</c>) built
/// once, on the first question, instead of a scan per question. A scan of a million points is fine
/// once; a thousand probes against it is a billion distances.
/// </para>
/// <para>
/// <b>Immutable, like every value here.</b> The points are copied in and copied out; the tree and the
/// box are derived from them and never stored in a file (<see cref="GeometryJson"/> writes the points).
/// </para>
/// </remarks>
public sealed class PointCloud
{
    private readonly Point3d[] _points;
    private readonly BoundingBox _box;
    private PointKdTree? _tree;

    /// <summary>Creates a cloud from points.</summary>
    /// <param name="points">The points, in order. Their indices are the ones queries answer with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentException">A point is not finite.</exception>
    public PointCloud(IReadOnlyList<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        _points = new Point3d[points.Count];

        for (int i = 0; i < _points.Length; i++)
        {
            _points[i] = points[i];

            if (!_points[i].IsValid)
            {
                throw new ArgumentException($"Point {i} is not finite, so it has no place in a cloud.", nameof(points));
            }
        }

        _box = _points.Length == 0 ? BoundingBox.Empty : new BoundingBox(_points);
    }

    /// <summary>Creates a cloud from points.</summary>
    /// <param name="points">The points, in order.</param>
    /// <returns>The cloud.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentException">A point is not finite.</exception>
    public static PointCloud FromPoints(IReadOnlyList<Point3d> points) => new(points);

    /// <summary>How many points the cloud holds.</summary>
    public int Count => _points.Length;

    /// <summary>The smallest box holding every point, kept rather than recomputed.</summary>
    public BoundingBox BoundingBox => _box;

    /// <summary>The points, in order.</summary>
    /// <returns>A copy; the cloud is immutable and an exposed array would not be.</returns>
    public Point3d[] Points() => [.. _points];

    /// <summary>The point nearest a probe, and how far away it is.</summary>
    /// <param name="point">Where to measure from.</param>
    /// <returns>The index and the distance, or <c>(-1, +∞)</c> for an empty cloud. A tie goes to the lower index.</returns>
    /// <exception cref="ArgumentException"><paramref name="point"/> is not finite.</exception>
    public (int Index, double Distance) NearestTo(in Point3d point) => Tree().Nearest(point);

    /// <summary>Every point within a radius of a probe, the boundary included.</summary>
    /// <param name="point">Where to measure from.</param>
    /// <param name="radius">How far. Zero finds the points exactly at the probe.</param>
    /// <returns>The indices, in ascending order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or not finite.</exception>
    /// <exception cref="ArgumentException"><paramref name="point"/> is not finite.</exception>
    public int[] Within(in Point3d point, double radius) => Tree().Within(point, radius);

    /// <summary>The cloud without near duplicates, keeping the first of each cluster.</summary>
    /// <param name="tolerance">How close two points must be to count as one. Zero removes exact copies only.</param>
    /// <returns>A new cloud.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative or not finite.</exception>
    public PointCloud PruneDuplicates(double tolerance = 0.0) => new(Point3d.PruneDuplicates(_points, tolerance));

    /// <summary>The cloud moved by a transform.</summary>
    /// <param name="transform">The transform.</param>
    /// <returns>A new cloud.</returns>
    public PointCloud TransformedBy(in Transform transform)
    {
        Point3d[] moved = new Point3d[_points.Length];

        for (int i = 0; i < moved.Length; i++)
        {
            moved[i] = transform.OfPoint(_points[i]);
        }

        return new PointCloud(moved);
    }

    /// <summary>
    /// The tree, built on the first question. Two threads asking at once may both build one; the
    /// reference written last wins, and both are the same tree.
    /// </summary>
    private PointKdTree Tree() => _tree ??= PointKdTree.Build(_points);
}

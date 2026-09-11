using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Viewport;

/// <summary>
/// What a pick found: the package the ray met first, and how far along the ray (`E9-T8`).
/// </summary>
/// <param name="Key">The <c>(NodeId, PortIndex)</c> identity of the geometry that was hit.</param>
/// <param name="ElementPath">Which element of that output, as the package records it.</param>
/// <param name="Distance">How far from the eye, in world units, the hit is.</param>
public readonly record struct ViewportHit(GeometryKey Key, string ElementPath, double Distance);

/// <summary>
/// Finds the geometry under a pixel (`E9-T8`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Through the kernel's hierarchy, not over every triangle.</b> The packages' boxes go into a
/// <see cref="BoundingVolumeHierarchy"/> (`E2-T15`), whose <see cref="BoundingVolumeHierarchy.FirstHit"/>
/// prunes every branch the ray enters beyond the best hit found so far, so a scene of a thousand
/// solids is a handful of exact tests rather than a thousand.
/// </para>
/// <para>
/// <b>Exact where a surface is drawn, forgiving where a line is.</b> A shaded triangle is hit or not,
/// and the test is exact. A curve is a line a pixel wide and a point is a marker a few pixels across,
/// so both are hit within <c>pixelTolerance</c> pixels, converted to world units at the depth of
/// the candidate - the same few pixels near the eye and far from it.
/// </para>
/// <para>
/// <b>Avalonia-free, like the rest of this assembly.</b> It takes pixels and a camera and returns a
/// key; turning a click into pixels is the control's business, in <c>Spark.UI</c>.
/// </para>
/// </remarks>
public static class ViewportPicker
{
    /// <summary>How near, in pixels, a line or a point has to be to be picked.</summary>
    public const double DefaultPixelTolerance = 5.0;

    /// <summary>Finds the nearest geometry under a pixel.</summary>
    /// <param name="camera">The camera the scene is drawn with.</param>
    /// <param name="packages">The scene's packages, as <see cref="ViewportScene.Snapshot"/> gives them.</param>
    /// <param name="pixelX">The horizontal pixel, from the left edge.</param>
    /// <param name="pixelY">The vertical pixel, from the top edge.</param>
    /// <param name="pixelTolerance">How near a line or point has to be, in pixels.</param>
    /// <returns>The nearest hit, or null when the pixel shows nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> or <paramref name="packages"/> is null.</exception>
    public static ViewportHit? Pick(
        Camera camera,
        IReadOnlyList<RenderPackage> packages,
        double pixelX,
        double pixelY,
        double pixelTolerance = DefaultPixelTolerance)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(packages);

        if (packages.Count == 0)
        {
            return null;
        }

        Ray ray = camera.RayThrough(pixelX, pixelY);
        Point3d origin = ray.Origin;
        Vector3d direction = ray.Direction.Normalised();

        // One pixel at unit distance from the eye, in world units: the vertical field of view spread
        // over the viewport's height. A tolerance of n pixels at distance t is n * slope * t.
        double slope = 2.0 * Math.Tan(camera.FieldOfView / 2.0) / camera.ViewportHeight * Math.Max(pixelTolerance, 0.0);

        BoundingBox[] boxes = new BoundingBox[packages.Count];

        for (int i = 0; i < packages.Count; i++)
        {
            Bounds3 bounds = packages[i].ComputeBounds();

            if (bounds.IsEmpty)
            {
                boxes[i] = BoundingBox.Empty;
                continue;
            }

            BoundingBox box = new(
                new Point3d(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
                new Point3d(bounds.Max.X, bounds.Max.Y, bounds.Max.Z));

            // Grown by the tolerance at the box's far side, so a line on its edge is never pruned
            // before the exact test gets to see it. An over-large box costs a test, never a miss.
            double far = origin.DistanceTo(box.Center) + (box.Min.DistanceTo(box.Max) * 0.5);
            boxes[i] = box.Inflated(slope * far);
        }

        BoundingVolumeHierarchy hierarchy = BoundingVolumeHierarchy.Build(boxes.AsSpan());

        (int index, double distance) = hierarchy.FirstHit(
            ray, (candidate, _) => Nearest(packages[candidate], origin, direction, slope));

        return index < 0
            ? null
            : new ViewportHit(packages[index].Key, packages[index].ElementPath, distance);
    }

    /// <summary>The nearest distance along the ray at which one package is hit, or null.</summary>
    private static double? Nearest(RenderPackage package, in Point3d origin, in Vector3d direction, double slope)
    {
        float[] positions = package.PositionData;
        int[] triangles = package.IndexData;
        int[] edges = package.EdgeIndexData;

        double best = double.PositiveInfinity;

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            if (Triangle(origin, direction, Vertex(positions, triangles[i]), Vertex(positions, triangles[i + 1]), Vertex(positions, triangles[i + 2])) is double t
                && t < best)
            {
                best = t;
            }
        }

        for (int i = 0; i + 1 < edges.Length; i += 2)
        {
            if (Segment(origin, direction, Vertex(positions, edges[i]), Vertex(positions, edges[i + 1]), slope) is double t
                && t < best)
            {
                best = t;
            }
        }

        // A package with neither triangles nor edges is points: markers drawn a few pixels across.
        if (triangles.Length == 0 && edges.Length == 0)
        {
            for (int i = 0; i + 2 < positions.Length; i += 3)
            {
                if (PointHit(origin, direction, Vertex(positions, i / 3), slope) is double t && t < best)
                {
                    best = t;
                }
            }
        }

        return double.IsPositiveInfinity(best) ? null : best;
    }

    private static Point3d Vertex(float[] positions, int index) =>
        new(positions[index * 3], positions[(index * 3) + 1], positions[(index * 3) + 2]);

    /// <summary>Möller-Trumbore: the distance to a triangle, or null when the ray misses it.</summary>
    private static double? Triangle(in Point3d origin, in Vector3d direction, in Point3d a, in Point3d b, in Point3d c)
    {
        Vector3d ab = b - a;
        Vector3d ac = c - a;
        Vector3d p = direction.Cross(ac);
        double determinant = ab.Dot(p);

        // Edge-on, or degenerate: nothing to hit. Both windings count - a surface is picked from
        // behind as readily as from in front, because the renderer draws both sides.
        if (Math.Abs(determinant) < 1e-15)
        {
            return null;
        }

        double inverse = 1.0 / determinant;
        Vector3d toOrigin = origin - a;
        double u = toOrigin.Dot(p) * inverse;

        if (u < 0.0 || u > 1.0)
        {
            return null;
        }

        Vector3d q = toOrigin.Cross(ab);
        double v = direction.Dot(q) * inverse;

        if (v < 0.0 || u + v > 1.0)
        {
            return null;
        }

        double t = ac.Dot(q) * inverse;

        return t > 0.0 ? t : null;
    }

    /// <summary>
    /// The distance along the ray to the point nearest a segment, when the segment passes within the
    /// tolerance there; otherwise null.
    /// </summary>
    private static double? Segment(in Point3d origin, in Vector3d direction, in Point3d a, in Point3d b, double slope)
    {
        Vector3d along = b - a;
        Vector3d offset = a - origin;

        double lengthSquared = along.LengthSquared;
        double dot = direction.Dot(along);
        double denominator = lengthSquared - (dot * dot);

        // Closest points between the ray and the segment's line, then clamped onto the segment.
        double s = denominator > 1e-18
            ? Math.Clamp(((direction.Dot(offset) * dot) - offset.Dot(along)) / denominator, 0.0, 1.0)
            : 0.0;

        Point3d onSegment = a + (along * s);
        double t = (onSegment - origin).Dot(direction);

        if (t <= 0.0)
        {
            return null;
        }

        double miss = onSegment.DistanceTo(origin + (direction * t));

        return miss <= slope * t ? t : null;
    }

    /// <summary>The distance along the ray to a point, when it passes within the tolerance.</summary>
    private static double? PointHit(in Point3d origin, in Vector3d direction, in Point3d point, double slope)
    {
        double t = (point - origin).Dot(direction);

        if (t <= 0.0)
        {
            return null;
        }

        return point.DistanceTo(origin + (direction * t)) <= slope * t ? t : null;
    }
}

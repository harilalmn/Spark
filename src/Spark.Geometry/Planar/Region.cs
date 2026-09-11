using System;
using System.Collections.Generic;

namespace Spark.Geometry.Planar;

/// <summary>
/// An area in a plane - outer boundaries with holes - and the booleans between areas
/// (<c>E2-T13</c>, PRD FR-60).
/// </summary>
/// <remarks>
/// <para>
/// <b>A supporting layer, not a peer 2D API.</b> A region lives in the frame of a 3D
/// <see cref="Geometry.Plane"/>: it is made from closed polylines lying in that plane and handed back
/// as closed polylines, so a graph never has to know a 2D coordinate exists.
/// </para>
/// <para>
/// <b>What a hole is, is decided by nesting.</b> The loops a caller gives are normalised with even-odd
/// fill, so a loop inside another is a hole whichever way round it was drawn - which is how a person
/// sketching a washer means it.
/// </para>
/// <para>
/// <b>Coplanar is enough.</b> Two regions need not share a frame to be combined, only a plane: the
/// second is re-measured in the first's frame. Regions in different planes are refused - their
/// boolean is not a region.
/// </para>
/// <para>
/// <b>Not yet saved to a file</b>: a region has no <see cref="GeometryJson"/> form. It is made from
/// polylines, which are, and a graph recomputes it.
/// </para>
/// </remarks>
public sealed class Region
{
    private readonly Point2d[][] _loops;
    private readonly int _precision;

    private Region(Plane plane, Point2d[][] loops, int precision)
    {
        Plane = plane;
        _loops = loops;
        _precision = precision;
    }

    /// <summary>The plane the region lies in; its frame is the region's 2D coordinate system.</summary>
    public Plane Plane { get; }

    /// <summary>How many closed loops bound the region, outer boundaries and holes together.</summary>
    public int LoopCount => _loops.Length;

    /// <summary>Whether the region encloses nothing.</summary>
    public bool IsEmpty => _loops.Length == 0;

    /// <summary>The area enclosed, holes subtracted.</summary>
    public double Area => ClipperBridge.Area(_loops);

    /// <summary>Makes a region from closed polylines lying in a plane.</summary>
    /// <param name="plane">The plane. Every point of every loop must lie in it.</param>
    /// <param name="loops">The boundaries. A loop inside another is a hole, whichever way round it runs.</param>
    /// <param name="tolerance">How far off the plane a point may lie, and how finely the region is computed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loops"/> is null.</exception>
    /// <exception cref="ArgumentException">A loop is null or not closed, or a point lies off the plane.</exception>
    public Region(in Plane plane, IEnumerable<PolyLine> loops, in Tolerance tolerance = default)
        : this(plane, Normalised(plane, loops, tolerance), ClipperBridge.Precision(tolerance.Linear))
    {
    }

    /// <summary>Makes a region from closed polylines lying in a plane.</summary>
    /// <param name="plane">The plane. Every point of every loop must lie in it.</param>
    /// <param name="loops">The boundaries. A loop inside another is a hole, whichever way round it runs.</param>
    /// <param name="tolerance">How far off the plane a point may lie, and how finely the region is computed.</param>
    /// <returns>The region.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loops"/> is null.</exception>
    /// <exception cref="ArgumentException">A loop is null or not closed, or a point lies off the plane.</exception>
    public static Region FromClosedPolyLines(in Plane plane, IEnumerable<PolyLine> loops, in Tolerance tolerance = default) =>
        new(plane, loops, tolerance);

    /// <summary>A caller's loops, checked, projected into the plane's frame and put in canonical form.</summary>
    private static Point2d[][] Normalised(in Plane plane, IEnumerable<PolyLine> loops, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(loops);

        double linear = tolerance.Linear;
        List<Point2d[]> flat = [];

        foreach (PolyLine? loop in loops)
        {
            if (loop is null || !loop.IsClosed)
            {
                throw new ArgumentException("A region's boundary must be a closed polyline.", nameof(loops));
            }

            Point3d[] points = loop.Points();
            Point2d[] projected = new Point2d[points.Length - 1];

            for (int i = 0; i < projected.Length; i++)
            {
                if (Math.Abs(plane.DistanceTo(points[i])) > linear)
                {
                    throw new ArgumentException(
                        $"A boundary point lies {Math.Abs(plane.DistanceTo(points[i]))} off the region's plane.", nameof(loops));
                }

                projected[i] = plane.To2d(points[i]);
            }

            flat.Add(projected);
        }

        return ClipperBridge.Normalise(flat, ClipperBridge.Precision(linear));
    }

    /// <summary>The area in either region.</summary>
    /// <param name="other">The other region, in the same plane.</param>
    /// <returns>A new region.</returns>
    /// <exception cref="ArgumentException"><paramref name="other"/> is in a different plane.</exception>
    public Region Union(Region other) => Combine(other, ClipperBridge.Operation.Union);

    /// <summary>The area in both regions.</summary>
    /// <param name="other">The other region, in the same plane.</param>
    /// <returns>A new region.</returns>
    /// <exception cref="ArgumentException"><paramref name="other"/> is in a different plane.</exception>
    public Region Intersection(Region other) => Combine(other, ClipperBridge.Operation.Intersection);

    /// <summary>The area in this region and not the other.</summary>
    /// <param name="other">The other region, in the same plane.</param>
    /// <returns>A new region.</returns>
    /// <exception cref="ArgumentException"><paramref name="other"/> is in a different plane.</exception>
    public Region Difference(Region other) => Combine(other, ClipperBridge.Operation.Difference);

    /// <summary>The area in exactly one of the two regions.</summary>
    /// <param name="other">The other region, in the same plane.</param>
    /// <returns>A new region.</returns>
    /// <exception cref="ArgumentException"><paramref name="other"/> is in a different plane.</exception>
    public Region SymmetricDifference(Region other) => Combine(other, ClipperBridge.Operation.SymmetricDifference);

    /// <summary>Whether a point lies in the region or on its boundary.</summary>
    /// <param name="point">The point. Off the plane by more than the tolerance, it is not in the region.</param>
    /// <param name="tolerance">How far off the plane still counts as in it.</param>
    /// <returns>True when the point is in the region.</returns>
    public bool Contains(in Point3d point, in Tolerance tolerance = default)
    {
        if (Math.Abs(Plane.DistanceTo(point)) > tolerance.Linear)
        {
            return false;
        }

        return ClipperBridge.Contains(_loops, Plane.To2d(point), _precision);
    }

    /// <summary>The region's boundaries as closed polylines in its plane.</summary>
    /// <returns>One closed polyline per loop, outer boundaries and holes alike.</returns>
    public PolyLine[] ToPolyLines()
    {
        PolyLine[] polylines = new PolyLine[_loops.Length];

        for (int i = 0; i < polylines.Length; i++)
        {
            Point3d[] points = new Point3d[_loops[i].Length];

            for (int j = 0; j < points.Length; j++)
            {
                points[j] = Plane.To3d(_loops[i][j]);
            }

            polylines[i] = PolyLine.FromClosedPoints(points);
        }

        return polylines;
    }

    private Region Combine(Region other, ClipperBridge.Operation operation)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new Region(Plane, ClipperBridge.Boolean(operation, _loops, InThisFrame(other), _precision), _precision);
    }

    /// <summary>The other region's loops, re-measured in this region's frame; refused when not coplanar.</summary>
    private Point2d[][] InThisFrame(Region other)
    {
        double linear = Math.Pow(10.0, -Math.Min(_precision, other._precision));

        if (Plane.Normal.Cross(other.Plane.Normal).Length > Math.Sin(Tolerance.Default.Angular.Radians)
            || Math.Abs(Plane.DistanceTo(other.Plane.Origin)) > linear)
        {
            throw new ArgumentException("Regions in different planes have no boolean: their result is not a region.", nameof(other));
        }

        Point2d[][] loops = new Point2d[other._loops.Length][];

        for (int i = 0; i < loops.Length; i++)
        {
            Point2d[] source = other._loops[i];
            Point2d[] mapped = new Point2d[source.Length];

            for (int j = 0; j < mapped.Length; j++)
            {
                mapped[j] = Plane.To2d(other.Plane.To3d(source[j]));
            }

            loops[i] = mapped;
        }

        return loops;
    }
}

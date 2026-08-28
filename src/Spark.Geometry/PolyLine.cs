using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A chain of straight segments through a sequence of vertices.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is also Spark's polygon and its rectangle.</b> Dynamo has a <c>Polygon</c> and a
/// <c>Rectangle</c> type, each a subclass of the last; Spark has factories on this type. A
/// closed polyline is a polygon, and nothing about a rectangle needs a type of its own that a
/// factory cannot say — while a type would have to be serialised, versioned, documented and
/// turned into nodes forever.
/// </para>
/// <para>
/// <b>Parameterisation.</b> The domain is <c>[0, SegmentCount]</c> and the parameter counts
/// segments: an integer parameter <c>i</c> is vertex <c>i</c>, and the fractional part
/// interpolates along the segment that follows it. The parameter is therefore <b>not</b> arc
/// length unless every segment happens to be the same length — use
/// <see cref="Curve.ParameterAtLength(double, in Tolerance)"/> for that. Counting segments
/// rather than measuring length keeps every vertex at an exactly representable parameter, which
/// is what makes trimming and splitting at a vertex exact.
/// </para>
/// <para>
/// Consecutive vertices must differ. A repeated vertex is a segment with no direction, and it
/// would make the tangent, the frame and the closest-point search all need a special case for a
/// piece of curve that occupies no space.
/// </para>
/// </remarks>
public sealed class PolyLine : Curve
{
    private readonly Point3d[] _vertices;

    /// <summary>
    /// Creates a polyline through a sequence of vertices.
    /// </summary>
    /// <param name="vertices">
    /// The vertices, in order. Copied; the list is not retained. At least two are needed, every
    /// one must be finite, and no two consecutive vertices may be exactly coincident. The first
    /// and last <i>may</i> coincide, which is how a closed polyline is expressed.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when fewer than two vertices are given, a vertex is not finite, or two
    /// consecutive vertices coincide.
    /// </exception>
    public PolyLine(IReadOnlyList<Point3d> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        if (vertices.Count < 2)
        {
            throw new ArgumentException(
                "A polyline needs at least two vertices.",
                nameof(vertices));
        }

        _vertices = new Point3d[vertices.Count];

        for (int i = 0; i < vertices.Count; i++)
        {
            if (!vertices[i].IsValid)
            {
                throw new ArgumentException($"Vertex {i} is not finite.", nameof(vertices));
            }

            if (i > 0 && vertices[i] == vertices[i - 1])
            {
                throw new ArgumentException(
                    $"Vertices {i - 1} and {i} are coincident, so the segment between them has no "
                    + "direction.",
                    nameof(vertices));
            }

            _vertices[i] = vertices[i];
        }
    }

    /// <summary>The vertices, in order. The first is the start point and the last is the end point.</summary>
    public ReadOnlySpan<Point3d> Vertices => _vertices;

    /// <summary>How many straight segments the polyline has, which is one fewer than its vertex count.</summary>
    public int SegmentCount => _vertices.Length - 1;

    /// <inheritdoc/>
    /// <remarks>
    /// The domain is <c>[0, SegmentCount]</c>: an integer parameter is a vertex and the
    /// fraction interpolates along the following segment.
    /// </remarks>
    public override Interval Domain => new(0.0, _vertices.Length - 1);

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> when the first and last vertices are exactly coincident, which is
    /// how a polygon is expressed.
    /// </remarks>
    public override bool IsClosed => _vertices[0] == _vertices[^1];

    /// <inheritdoc/>
    /// <remarks>
    /// Always <see langword="false"/>. Even a closed polyline has a corner at the seam, so its
    /// tangent does not carry across it.
    /// </remarks>
    public override bool IsPeriodic => false;

    /// <inheritdoc/>
    /// <remarks>Tight: the box of the vertices, since every point of the curve is a convex combination of two of them.</remarks>
    public override BoundingBox BoundingBox => BoundingBox.FromPoints(Vertices);

    /// <summary>
    /// Creates an open polyline through a sequence of points.
    /// </summary>
    /// <param name="points">The vertices, in order.</param>
    /// <returns>The polyline.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when fewer than two points are given, a point is not finite, or two consecutive
    /// points coincide.
    /// </exception>
    public static PolyLine ByPoints(IReadOnlyList<Point3d> points) => new(points);

    /// <summary>
    /// Creates a closed polyline — a polygon — through a sequence of points.
    /// </summary>
    /// <param name="points">
    /// The corners, in order. The closing segment back to the first is added here, so do not
    /// repeat the first point at the end.
    /// </param>
    /// <returns>The closed polyline, with one more vertex than points given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the list is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when fewer than three points are given — two points and a closing segment
    /// describe a line traversed twice, not a polygon — or when any two consecutive points
    /// coincide.
    /// </exception>
    public static PolyLine ByPointsClosed(IReadOnlyList<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 3)
        {
            throw new ArgumentException(
                "A closed polyline needs at least three points; two points and a closing segment "
                + "describe a line traversed twice.",
                nameof(points));
        }

        Point3d[] vertices = new Point3d[points.Count + 1];

        for (int i = 0; i < points.Count; i++)
        {
            vertices[i] = points[i];
        }

        vertices[^1] = points[0];

        return new PolyLine(vertices);
    }

    /// <summary>
    /// Creates a closed rectangular polyline centred on a plane's origin.
    /// </summary>
    /// <param name="plane">
    /// The plane the rectangle lies in. Its origin is the rectangle's centre, its X axis the
    /// direction of <paramref name="width"/> and its Y axis the direction of
    /// <paramref name="height"/>.
    /// </param>
    /// <param name="width">The extent along the plane's X axis. Must be positive and finite.</param>
    /// <param name="height">The extent along the plane's Y axis. Must be positive and finite.</param>
    /// <returns>
    /// The rectangle as a closed polyline of five vertices, starting at the corner in the
    /// negative quadrant and running counter-clockwise about the plane's normal.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either extent is not a positive finite number.
    /// </exception>
    public static PolyLine ByRectangle(in Plane plane, double width, double height)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("A rectangle's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(width) || width <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "A rectangle's width must be a positive finite number.");
        }

        if (!double.IsFinite(height) || height <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "A rectangle's height must be a positive finite number.");
        }

        double halfWidth = width / 2.0;
        double halfHeight = height / 2.0;

        return new PolyLine(
        [
            plane.To3d(new Point2d(-halfWidth, -halfHeight)),
            plane.To3d(new Point2d(halfWidth, -halfHeight)),
            plane.To3d(new Point2d(halfWidth, halfHeight)),
            plane.To3d(new Point2d(-halfWidth, halfHeight)),
            plane.To3d(new Point2d(-halfWidth, -halfHeight)),
        ]);
    }

    /// <summary>
    /// Creates a closed regular polygon inscribed in a circle.
    /// </summary>
    /// <param name="plane">
    /// The plane the polygon lies in. Its origin is the centre and its X axis points at the
    /// first corner.
    /// </param>
    /// <param name="radius">
    /// The circumradius — the distance from the centre to each corner, not to the middle of an
    /// edge. Must be positive and finite.
    /// </param>
    /// <param name="sides">The number of sides. Must be at least three.</param>
    /// <returns>
    /// The polygon as a closed polyline of <c>sides + 1</c> vertices, running counter-clockwise
    /// about the plane's normal from the plane's X axis.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is not a positive finite number, or when fewer than three sides
    /// are asked for.
    /// </exception>
    public static PolyLine ByRegularPolygon(in Plane plane, double radius, int sides)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("A polygon's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "A polygon's circumradius must be a positive finite number.");
        }

        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sides),
                sides,
                "A polygon needs at least three sides.");
        }

        Point3d[] vertices = new Point3d[sides + 1];

        for (int i = 0; i < sides; i++)
        {
            vertices[i] = ConicNumerics.PointAtAngle(plane, radius, radius, Math.Tau * i / sides);
        }

        vertices[^1] = vertices[0];

        return new PolyLine(vertices);
    }

    /// <summary>
    /// The straight segment between two consecutive vertices.
    /// </summary>
    /// <param name="index">
    /// The segment index, from zero to <see cref="SegmentCount"/> minus one. Segment <c>i</c>
    /// runs from vertex <c>i</c> to vertex <c>i + 1</c> and covers the parameters
    /// <c>[i, i + 1]</c>.
    /// </param>
    /// <returns>The segment as a <see cref="Line"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public Line SegmentAt(int index)
    {
        if (index < 0 || index >= SegmentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The segment index must be between zero and {SegmentCount - 1}.");
        }

        return new Line(_vertices[index], _vertices[index + 1]);
    }

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter)
    {
        (int index, double local) = Locate(ClampParameter(parameter));

        return Point3d.Lerp(_vertices[index], _vertices[index + 1], local);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Order zero is the position and order one is the segment's full displacement — not a unit
    /// vector, because the parameter counts segments rather than measuring length, so a long
    /// segment is traversed faster. Every higher order is <see cref="Vector3d.Zero"/>.
    /// <b>The first derivative is discontinuous at every interior vertex</b>, and the value
    /// reported at an integer parameter is that of the segment <i>after</i> it, except at the
    /// very end where there is no segment after and the last one is used.
    /// </remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        double t = ClampParameter(parameter);

        if (order == 0)
        {
            return (Vector3d)PointAt(t);
        }

        if (order > 1)
        {
            return Vector3d.Zero;
        }

        (int index, _) = Locate(t);

        return _vertices[index + 1] - _vertices[index];
    }

    /// <inheritdoc/>
    /// <remarks>Exact: the sum of the segment lengths. The tolerance is ignored.</remarks>
    public override double Length(in Tolerance tolerance = default)
    {
        double total = 0.0;

        for (int i = 1; i < _vertices.Length; i++)
        {
            total += _vertices[i].DistanceTo(_vertices[i - 1]);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>Exact: whole segments plus the fraction of the one the parameter lands in.</remarks>
    public override double LengthAt(double parameter, in Tolerance tolerance = default)
    {
        (int index, double local) = Locate(ClampParameter(parameter));
        double total = 0.0;

        for (int i = 0; i < index; i++)
        {
            total += _vertices[i + 1].DistanceTo(_vertices[i]);
        }

        return total + (local * _vertices[index + 1].DistanceTo(_vertices[index]));
    }

    /// <inheritdoc/>
    /// <remarks>Exact: the segment containing the length is found by walking, and the remainder divided out.</remarks>
    public override double ParameterAtLength(double length, in Tolerance tolerance = default)
    {
        if (double.IsNaN(length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The arc length must not be NaN.");
        }

        if (length <= 0.0)
        {
            return 0.0;
        }

        double remaining = length;

        for (int i = 0; i < SegmentCount; i++)
        {
            double segment = _vertices[i + 1].DistanceTo(_vertices[i]);

            if (remaining <= segment)
            {
                return i + (remaining / segment);
            }

            remaining -= segment;
        }

        return Domain.Max;
    }

    /// <inheritdoc/>
    /// <remarks>Always zero away from a vertex, and zero at one too: a corner has no defined curvature and this does not invent one.</remarks>
    public override double CurvatureAt(double parameter)
    {
        ClampParameter(parameter);

        return 0.0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Exact rather than sampled: each segment is solved in closed form and the best taken.
    /// </remarks>
    public override Point3d ClosestPoint(in Point3d point, out double parameter, in Tolerance tolerance = default)
    {
        if (!point.IsValid)
        {
            throw new ArgumentException("The point must be finite.", nameof(point));
        }

        double bestDistance = double.PositiveInfinity;
        double bestParameter = 0.0;
        Point3d best = _vertices[0];

        for (int i = 0; i < SegmentCount; i++)
        {
            Vector3d edge = _vertices[i + 1] - _vertices[i];
            double lengthSquared = edge.LengthSquared;
            double local = Math.Clamp((point - _vertices[i]).Dot(edge) / lengthSquared, 0.0, 1.0);
            Point3d candidate = Point3d.Lerp(_vertices[i], _vertices[i + 1], local);
            double distance = point.DistanceSquaredTo(candidate);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestParameter = i + local;
                best = candidate;
            }
        }

        parameter = bestParameter;

        return best;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Exact: a polyline is planar exactly when its vertices are, because every point of it is a
    /// convex combination of two vertices.
    /// </remarks>
    public override bool IsPlanar(out Plane plane, in Tolerance tolerance = default) =>
        TryFitPlane(_vertices, tolerance, out plane);

    /// <inheritdoc/>
    /// <remarks>
    /// The trimmed polyline keeps every vertex strictly inside the interval and gains new end
    /// vertices where the interval cuts a segment. Its domain is <c>[0, newSegmentCount]</c>,
    /// which is <b>not</b> the same length as the interval trimmed — the parameter counts
    /// segments, and a trim that cuts two segments in half leaves one segment where it took
    /// half of each. This is the one place a polyline's parameterisation is not simply shifted,
    /// and it is a consequence of counting segments rather than measuring length.
    /// </remarks>
    public override PolyLine Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);
        (int firstIndex, _) = Locate(clipped.Min);
        (int lastIndex, double lastLocal) = Locate(clipped.Max);

        // A trim ending exactly on a vertex lands at the start of the following segment, and
        // the vertex it names is already the end of the previous one. Keeping the piece would
        // add a segment of zero length, which a polyline does not allow.
        if (lastLocal == 0.0 && lastIndex > firstIndex)
        {
            lastIndex--;
        }

        List<Point3d> vertices = [PointAt(clipped.Min)];

        for (int i = firstIndex + 1; i <= lastIndex; i++)
        {
            AppendIfDistinct(vertices, _vertices[i]);
        }

        AppendIfDistinct(vertices, PointAt(clipped.Max));

        return new PolyLine(vertices);
    }

    /// <inheritdoc/>
    public override PolyLine Reverse()
    {
        Point3d[] vertices = new Point3d[_vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = _vertices[^(i + 1)];
        }

        return new PolyLine(vertices);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A degree-one NURBS curve whose control points are the vertices and whose knots are the
    /// integers, so the parameterisation is preserved exactly and the two evaluate identically
    /// at every parameter.
    /// </remarks>
    public override NurbsCurve ToNurbsCurve()
    {
        double[] weights = new double[_vertices.Length];
        double[] knots = new double[_vertices.Length + 2];

        Array.Fill(weights, 1.0);

        knots[0] = 0.0;

        for (int i = 0; i < _vertices.Length; i++)
        {
            knots[i + 1] = i;
        }

        knots[^1] = _vertices.Length - 1;

        return new NurbsCurve(1, _vertices, weights, knots);
    }

    /// <inheritdoc/>
    /// <remarks>A polyline is a polyline under every affine transformation, so the type is always kept.</remarks>
    public override PolyLine Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        Point3d[] vertices = new Point3d[_vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = transform.OfPoint(_vertices[i]);
        }

        return new PolyLine(vertices);
    }

    /// <summary>
    /// Compares this polyline with another vertex by vertex, within a tolerance.
    /// </summary>
    /// <param name="other">The polyline to compare with. <see langword="null"/> is never equal.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both have the same number of vertices and every pair agrees
    /// within tolerance. This compares the <i>representation</i>, so a polyline with a redundant
    /// vertex in the middle of a straight run is not equal to the one without it.
    /// </returns>
    public bool EqualsWithin(PolyLine? other, in Tolerance tolerance = default)
    {
        if (other is null || other._vertices.Length != _vertices.Length)
        {
            return false;
        }

        for (int i = 0; i < _vertices.Length; i++)
        {
            if (!_vertices[i].EqualsWithin(other._vertices[i], tolerance))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Formats the vertex count and whether the polyline is closed, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>PolyLine(Vertices=5, Closed=True)</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"PolyLine(Vertices={_vertices.Length}, Closed={IsClosed})");

    /// <inheritdoc/>
    /// <remarks>Raised in proportion to the segment count, though no member of this type uses the generic fallbacks.</remarks>
    private protected override int SeedCount => Math.Max(DefaultSeedCount, 8 * SegmentCount);

    /// <summary>
    /// Appends a vertex unless it is exactly the one already at the end.
    /// </summary>
    /// <param name="vertices">The list being built.</param>
    /// <param name="vertex">The vertex to append.</param>
    /// <remarks>
    /// A trim starting a hair short of a vertex evaluates to that vertex, and appending it a
    /// second time would produce a segment of zero length that the constructor rightly refuses.
    /// The guard is exact rather than tolerant because the constructor's test is exact.
    /// </remarks>
    private static void AppendIfDistinct(List<Point3d> vertices, in Point3d vertex)
    {
        if (vertices[^1] != vertex)
        {
            vertices.Add(vertex);
        }
    }

    /// <summary>
    /// Splits a parameter into the segment it lies on and how far along that segment it is.
    /// </summary>
    /// <param name="parameter">The parameter, already clamped into the domain.</param>
    /// <returns>
    /// The segment index and a fraction in <c>[0, 1]</c>. A parameter at an interior vertex
    /// reports the segment <i>after</i> the vertex with a fraction of zero; the domain's upper
    /// end reports the last segment with a fraction of one, since there is no segment after it.
    /// </returns>
    private (int Index, double Local) Locate(double parameter)
    {
        int index = Math.Clamp((int)Math.Floor(parameter), 0, SegmentCount - 1);

        return (index, parameter - index);
    }
}

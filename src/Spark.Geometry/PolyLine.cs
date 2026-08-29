using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A chain of straight segments through a list of points, parameterised over [0, n] with one unit
/// per segment, so that its whole-number parameters are exactly its vertices.
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed polyline is what Dynamo calls a <c>Polygon</c>, and a rectangle is a factory rather
/// than a type.</b> A <c>Rectangle</c> that derives from a <c>Polygon</c> that derives from a
/// <c>PolyCurve</c> buys nothing over a closed polyline built by <see cref="ByRectangle"/>, and it
/// costs a public type that has to be serialised, versioned, documented and turned into nodes
/// forever. See <c>docs/DYNAMO-COVERAGE.md</c> §3.2.
/// </para>
/// <para>
/// <b>Closure is exact here, not tolerant.</b> <see cref="Curve.IsClosed"/> is true when the last
/// point equals the first exactly, and the closed factories repeat the first point rather than
/// relying on arithmetic to land back on it. Tolerance in Spark is always passed rather than
/// ambient (ADR-0010), so a property with no parameter cannot ask a tolerant question; a caller who
/// wants a tolerant answer compares <see cref="Curve.StartPoint"/> and <see cref="Curve.EndPoint"/>
/// with the tolerance they mean.
/// </para>
/// <para>
/// The derivative at a vertex is taken from the segment that follows it, so it is right-continuous.
/// At the very end of the curve there is no following segment and the last one is used. A polyline
/// is not differentiable at a vertex at all, and this is the choice that makes
/// <see cref="Curve.TangentAt(double)"/> total rather than the honest answer.
/// </para>
/// </remarks>
public sealed class PolyLine : Curve
{
    private readonly Point3d[] _points;
    private double[]? _cumulative;

    /// <summary>Creates a polyline through a list of points.</summary>
    /// <param name="points">
    /// At least two points. No two consecutive points may coincide: a zero-length segment has no
    /// direction, and it would put a division by zero inside every tangent query on the curve.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when there are fewer than two points, when any point is not finite, or when two
    /// consecutive points coincide. The message names the index, because on a list of ten thousand
    /// points that is the only part of the answer a caller can act on.
    /// </exception>
    public PolyLine(IEnumerable<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = [.. points];
        Validate(_points, nameof(points));
    }

    private PolyLine(Point3d[] points, string parameterName)
    {
        _points = points;
        if (parameterName.Length > 0)
        {
            Validate(_points, parameterName);
        }
    }

    /// <inheritdoc/>
    public override Interval Domain => new(0.0, _points.Length - 1);

    /// <inheritdoc/>
    public override bool IsClosed => _points[0] == _points[^1];

    /// <summary>How many straight segments the polyline has.</summary>
    public int SegmentCount => _points.Length - 1;

    /// <summary>How many points the polyline was built from.</summary>
    public int PointCount => _points.Length;

    /// <summary>Creates a polyline through a list of points.</summary>
    /// <param name="points">At least two points, no two consecutive ones coinciding.</param>
    /// <returns>The polyline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when there are fewer than two points, when any point is not finite, or when two
    /// consecutive points coincide.
    /// </exception>
    public static PolyLine ByPoints(IEnumerable<Point3d> points) => new(points);

    /// <summary>
    /// Creates a closed polyline through a list of points, repeating the first point at the end.
    /// </summary>
    /// <param name="points">
    /// At least three points, no two consecutive ones coinciding. The first point is repeated at the
    /// end unless it is already there.
    /// </param>
    /// <returns>The closed polyline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when there are fewer than three distinct points, when any point is not finite, or when
    /// two consecutive points coincide.
    /// </exception>
    public static PolyLine ByClosedPoints(IEnumerable<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        List<Point3d> loop = [.. points];
        if (loop.Count < 3)
        {
            throw new ArgumentException(
                "A closed polyline needs at least three points.", nameof(points));
        }

        if (loop[0] != loop[^1])
        {
            loop.Add(loop[0]);
        }

        return new PolyLine(loop);
    }

    /// <summary>Creates a closed rectangle centred on a plane's origin.</summary>
    /// <param name="plane">The plane. Its origin is the centre of the rectangle.</param>
    /// <param name="width">The size along the plane's x axis. Positive and finite.</param>
    /// <param name="length">The size along the plane's y axis. Positive and finite.</param>
    /// <returns>A closed polyline of four segments.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either size is not positive and finite.
    /// </exception>
    public static PolyLine ByRectangle(in Plane plane, double width, double length)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("A rectangle's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(width) || width <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), width, "A rectangle's width must be positive and finite.");
        }

        if (!double.IsFinite(length) || length <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, "A rectangle's length must be positive and finite.");
        }

        Vector3d x = plane.XAxis * (width * 0.5);
        Vector3d y = plane.YAxis * (length * 0.5);
        Point3d origin = plane.Origin;
        Point3d corner = origin - x - y;
        return new PolyLine(
            [corner, origin + x - y, origin + x + y, origin - x + y, corner],
            nameof(plane));
    }

    /// <summary>
    /// Creates a closed regular polygon inscribed in a circle, with its first vertex on the plane's
    /// x axis.
    /// </summary>
    /// <param name="plane">The plane. Its origin is the centre.</param>
    /// <param name="radius">The circumradius — the distance from the centre to each vertex.</param>
    /// <param name="sides">How many sides. At least three.</param>
    /// <returns>A closed polyline of <paramref name="sides"/> segments.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="radius"/> is not positive and finite, or when
    /// <paramref name="sides"/> is less than three.
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
                nameof(radius), radius, "A polygon's radius must be positive and finite.");
        }

        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sides), sides, "A polygon needs at least three sides.");
        }

        Point3d[] points = new Point3d[sides + 1];
        for (int vertex = 0; vertex < sides; vertex++)
        {
            double angle = Math.PI * 2.0 * vertex / sides;
            points[vertex] = CircularArcs.PointAt(plane, radius, radius, angle);
        }

        // The loop is closed by repeating the first point rather than by evaluating the full turn,
        // which would land a few ulps away and make IsClosed false on a shape that plainly is.
        points[sides] = points[0];
        return new PolyLine(points, nameof(plane));
    }

    /// <summary>The points the polyline runs through.</summary>
    /// <returns>A copy. The polyline's own array is never handed out.</returns>
    public Point3d[] Points() => [.. _points];

    /// <summary>The point at a vertex index.</summary>
    /// <param name="index">The index, from zero to <see cref="PointCount"/> minus one.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public Point3d PointAtIndex(int index)
    {
        if (index < 0 || index >= _points.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, "The vertex index is outside the polyline.");
        }

        return _points[index];
    }

    /// <summary>The straight segment at a segment index.</summary>
    /// <param name="index">The index, from zero to <see cref="SegmentCount"/> minus one.</param>
    /// <returns>The segment as a <see cref="Line"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
    public Line SegmentAt(int index)
    {
        if (index < 0 || index >= SegmentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, "The segment index is outside the polyline.");
        }

        return new Line(_points[index], _points[index + 1]);
    }

    /// <inheritdoc/>
    public override double LengthAt(double parameter)
    {
        double valid = CheckParameter(parameter);
        (int segment, double local) = Locate(valid);
        double[] cumulative = Cumulative();
        return cumulative[segment] + (local * (cumulative[segment + 1] - cumulative[segment]));
    }

    /// <inheritdoc/>
    public override double ParameterAtLength(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "A distance along a curve must be finite.");
        }

        double[] cumulative = Cumulative();
        double target = Math.Clamp(distance, 0.0, cumulative[^1]);

        int low = 0;
        int high = cumulative.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (cumulative[mid] <= target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        double span = cumulative[low + 1] - cumulative[low];
        return low + (span > 0.0 ? (target - cumulative[low]) / span : 0.0);
    }

    /// <inheritdoc/>
    public override Point3d[] Tessellate(in Tolerance tolerance = default) => [.. _points];

    /// <inheritdoc/>
    public override Curve Reversed()
    {
        Point3d[] reversed = new Point3d[_points.Length];
        for (int index = 0; index < _points.Length; index++)
        {
            reversed[index] = _points[^(index + 1)];
        }

        return new PolyLine(reversed, string.Empty);
    }

    /// <inheritdoc/>
    public override Curve Trimmed(in Interval domain)
    {
        CheckTrimDomain(domain, Domain);
        Interval increasing = domain.MakeIncreasing();
        (int firstSegment, _) = Locate(increasing.Min);
        (int lastSegment, _) = Locate(increasing.Max);

        List<Point3d> kept = [Evaluate(increasing.Min)];
        for (int vertex = firstSegment + 1; vertex <= lastSegment; vertex++)
        {
            if (_points[vertex] != kept[^1])
            {
                kept.Add(_points[vertex]);
            }
        }

        Point3d end = Evaluate(increasing.Max);
        if (end != kept[^1])
        {
            kept.Add(end);
        }

        if (kept.Count < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, "A trim domain must span more than a single point.");
        }

        PolyLine trimmed = new([.. kept], string.Empty);
        return domain.IsDecreasing ? trimmed.Reversed() : trimmed;
    }

    /// <inheritdoc/>
    public override Curve TransformedBy(in Transform transform)
    {
        Point3d[] mapped = new Point3d[_points.Length];
        for (int index = 0; index < _points.Length; index++)
        {
            mapped[index] = transform.OfPoint(_points[index]);
        }

        for (int index = 1; index < mapped.Length; index++)
        {
            if (!mapped[index].IsValid || mapped[index] == mapped[index - 1])
            {
                throw new ArgumentException(
                    "The transform collapses a segment of this polyline to a point.",
                    nameof(transform));
            }
        }

        return new PolyLine(mapped, string.Empty);
    }

    /// <summary>A readable description of the polyline, for diagnostics.</summary>
    /// <returns>The point count and whether it is closed.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"PolyLine({_points.Length} points, {(IsClosed ? "closed" : "open")})");

    /// <inheritdoc/>
    protected override double ComputeLength() => Cumulative()[^1];

    /// <inheritdoc/>
    protected override BoundingBox ComputeBoundingBox() => BoundingBox.FromPoints(_points);

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter)
    {
        (int segment, double local) = Locate(parameter);
        return Point3d.Lerp(_points[segment], _points[segment + 1], local);
    }

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter)
    {
        (int segment, _) = Locate(parameter);
        return _points[segment + 1] - _points[segment];
    }

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter) => Vector3d.Zero;

    /// <summary>
    /// One seed span per segment, because a corner is exactly where this curve stops being
    /// smooth.
    /// </summary>
    /// <remarks>
    /// <see cref="Tessellate(in Tolerance)"/> does not consult this — a polyline tessellates to
    /// its own points and nothing else will do. It is here for everything else that subdivides
    /// a curve and needs its pieces not to straddle a corner, and the proximity search behind
    /// <see cref="Curve.ClosestPoint(in Point3d, in Tolerance)"/> is the first of those: a span
    /// containing a corner holds two branches of a piecewise function, and a Newton step inside
    /// it follows the wrong one.
    /// </remarks>
    protected override int TessellationSeedSpans => SegmentCount;

    private static void Validate(Point3d[] points, string parameterName)
    {
        if (points.Length < 2)
        {
            throw new ArgumentException(
                "A polyline needs at least two points.", parameterName);
        }

        for (int index = 0; index < points.Length; index++)
        {
            if (!points[index].IsValid)
            {
                throw new ArgumentException(
                    $"The point at index {index} is not finite.", parameterName);
            }

            if (index > 0 && points[index] == points[index - 1])
            {
                throw new ArgumentException(
                    $"The points at indices {index - 1} and {index} coincide, "
                    + "which would give the polyline a segment with no direction.",
                    parameterName);
            }
        }
    }

    private (int Segment, double Local) Locate(double parameter)
    {
        int last = _points.Length - 2;
        int segment = (int)Math.Floor(parameter);
        if (segment < 0)
        {
            return (0, 0.0);
        }

        if (segment > last)
        {
            return (last, 1.0);
        }

        return (segment, parameter - segment);
    }

    private double[] Cumulative()
    {
        if (_cumulative is not null)
        {
            return _cumulative;
        }

        double[] cumulative = new double[_points.Length];
        double running = 0.0;
        for (int index = 1; index < _points.Length; index++)
        {
            running += _points[index].DistanceTo(_points[index - 1]);
            cumulative[index] = running;
        }

        _cumulative = cumulative;
        return cumulative;
    }
}

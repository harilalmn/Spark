using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A straight line segment between two points.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parameterisation.</b> The domain is <c>[0, Length]</c> and the parameter <i>is</i> arc
/// length: <c>PointAt(t)</c> is <c>Start + Direction * t</c>. That makes
/// <see cref="Curve.LengthAt(double, in Tolerance)"/> the identity and
/// <see cref="Curve.ParameterAtLength(double, in Tolerance)"/> a clamp, which is worth more on
/// a line than a <c>[0, 1]</c> domain would be.
/// </para>
/// <para>
/// A line of zero length cannot be constructed. Two coincident endpoints define no direction,
/// and every one of a curve's questions — tangent, frame, closest point — would have to answer
/// with a special case if they could.
/// </para>
/// </remarks>
public sealed class Line : Curve
{
    private readonly double _length;

    /// <summary>
    /// Creates a line segment between two points.
    /// </summary>
    /// <param name="start">The start point.</param>
    /// <param name="end">The end point.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is not finite, or when the two are exactly coincident and so
    /// define no direction.
    /// </exception>
    public Line(in Point3d start, in Point3d end)
    {
        if (!start.IsValid)
        {
            throw new ArgumentException("A line's start point must be finite.", nameof(start));
        }

        if (!end.IsValid)
        {
            throw new ArgumentException("A line's end point must be finite.", nameof(end));
        }

        Vector3d offset = end - start;

        if (!offset.TryNormalise(out Vector3d direction))
        {
            throw new ArgumentException(
                "A line's endpoints must differ: two coincident points define no direction.",
                nameof(end));
        }

        Start = start;
        End = end;
        Direction = direction;
        _length = offset.Length;
    }

    /// <summary>The start point, which is <c>PointAt(0)</c>.</summary>
    public Point3d Start { get; }

    /// <summary>The end point, which is <c>PointAt(Length)</c>.</summary>
    public Point3d End { get; }

    /// <summary>
    /// The unit direction from <see cref="Start"/> towards <see cref="End"/>. Always a unit
    /// vector, so it is a direction and not a displacement; multiply by
    /// <see cref="Curve.Length(in Tolerance)"/> for the displacement.
    /// </summary>
    public Vector3d Direction { get; }

    /// <inheritdoc/>
    /// <remarks>The domain is <c>[0, Length]</c>, so the parameter is arc length.</remarks>
    public override Interval Domain => new(0.0, _length);

    /// <inheritdoc/>
    /// <remarks>Always <see langword="false"/>: a line's endpoints are never coincident.</remarks>
    public override bool IsClosed => false;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="false"/>.</remarks>
    public override bool IsPeriodic => false;

    /// <inheritdoc/>
    /// <remarks>Tight: the box of the two endpoints.</remarks>
    public override BoundingBox BoundingBox => new(Start, End);

    /// <summary>
    /// Creates a line segment between two points. The factory form of the equivalent
    /// constructor.
    /// </summary>
    /// <param name="start">The start point.</param>
    /// <param name="end">The end point.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is not finite, or when the two are coincident.
    /// </exception>
    public static Line ByStartPointEndPoint(in Point3d start, in Point3d end) => new(start, end);

    /// <summary>
    /// Creates a line segment from a start point, a direction and a length.
    /// </summary>
    /// <param name="start">The start point.</param>
    /// <param name="direction">The direction to travel in. Need not be normalised.</param>
    /// <param name="length">
    /// The length of the segment. A negative length runs the segment backwards along
    /// <paramref name="direction"/>, which is the reading that makes
    /// <c>ByStartPointDirectionLength(p, v, -d)</c> the mirror of the positive case rather than
    /// an error.
    /// </param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="direction"/> is zero-length or not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="length"/> is zero, <see cref="double.NaN"/> or infinite.
    /// </exception>
    public static Line ByStartPointDirectionLength(in Point3d start, in Vector3d direction, double length)
    {
        if (!double.IsFinite(length) || length == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "A line's length must be a non-zero finite number.");
        }

        if (!direction.TryNormalise(out Vector3d unit))
        {
            throw new ArgumentException(
                "A line's direction must have non-zero length and finite components.",
                nameof(direction));
        }

        return new Line(start, start + (unit * length));
    }

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter) => Start + (Direction * ClampParameter(parameter));

    /// <inheritdoc/>
    /// <remarks>
    /// Order zero is the position, order one is <see cref="Direction"/> — a unit vector, because
    /// the parameter is arc length — and every higher order is <see cref="Vector3d.Zero"/>.
    /// </remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        return order switch
        {
            0 => (Vector3d)PointAt(parameter),
            1 => Direction,
            _ => Vector3d.Zero,
        };
    }

    /// <inheritdoc/>
    public override Vector3d TangentAt(double parameter)
    {
        ClampParameter(parameter);

        return Direction;
    }

    /// <inheritdoc/>
    /// <remarks>Exact and closed-form; the tolerance is ignored.</remarks>
    public override double Length(in Tolerance tolerance = default) => _length;

    /// <inheritdoc/>
    /// <remarks>
    /// Exact: the parameter is already arc length, so this is the clamped parameter itself.
    /// </remarks>
    public override double LengthAt(double parameter, in Tolerance tolerance = default) =>
        ClampParameter(parameter);

    /// <inheritdoc/>
    /// <remarks>Exact: the arc length clamped into the domain.</remarks>
    public override double ParameterAtLength(double length, in Tolerance tolerance = default)
    {
        if (double.IsNaN(length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The arc length must not be NaN.");
        }

        return Domain.Clamp(length);
    }

    /// <inheritdoc/>
    /// <remarks>Always zero: a line does not curve.</remarks>
    public override double CurvatureAt(double parameter)
    {
        ClampParameter(parameter);

        return 0.0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Exact rather than sampled: the point is projected onto the line and the projection is
    /// clamped to the segment.
    /// </remarks>
    public override Point3d ClosestPoint(in Point3d point, out double parameter, in Tolerance tolerance = default)
    {
        if (!point.IsValid)
        {
            throw new ArgumentException("The point must be finite.", nameof(point));
        }

        parameter = Domain.Clamp((point - Start).Dot(Direction));

        return Start + (Direction * parameter);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Always <see langword="true"/>. A line lies in infinitely many planes; the one reported
    /// contains the line and is chosen the same deterministic way
    /// <see cref="Plane(in Point3d, in Vector3d)"/> chooses a frame from a normal.
    /// </remarks>
    public override bool IsPlanar(out Plane plane, in Tolerance tolerance = default)
    {
        plane = Plane.ByOriginXAxisYAxis(Start, Direction, new Plane(Start, Direction).XAxis);

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The result's domain is <c>[0, interval.Length]</c>, so
    /// <c>Trim(i).PointAt(u)</c> equals <c>PointAt(i.Min + u)</c>.
    /// </remarks>
    public override Line Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);

        return new Line(PointAt(clipped.Min), PointAt(clipped.Max));
    }

    /// <inheritdoc/>
    public override Line Reverse() => new(End, Start);

    /// <inheritdoc/>
    /// <remarks>
    /// A degree-one NURBS curve with two control points over the same <c>[0, Length]</c>
    /// domain. The parameterisation is preserved exactly, so the two evaluate identically at
    /// every parameter.
    /// </remarks>
    public override NurbsCurve ToNurbsCurve() =>
        new(1, [Start, End], [1.0, 1.0], [0.0, 0.0, _length, _length]);

    /// <inheritdoc/>
    /// <remarks>A line is a line under every affine transformation, so the type is always kept.</remarks>
    public override Line Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        return new Line(transform.OfPoint(Start), transform.OfPoint(End));
    }

    /// <summary>
    /// Compares this line with another by its defining points, within a tolerance.
    /// </summary>
    /// <param name="other">The line to compare with. <see langword="null"/> is never equal.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both endpoints agree within tolerance. This compares the
    /// <i>representation</i>, so a line and its reverse are not equal even though they occupy
    /// the same positions.
    /// </returns>
    public bool EqualsWithin(Line? other, in Tolerance tolerance = default) =>
        other is not null
        && Start.EqualsWithin(other.Start, tolerance)
        && End.EqualsWithin(other.End, tolerance);

    /// <summary>
    /// Formats the endpoints, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>Line((0, 0, 0) -&gt; (1, 0, 0))</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Line({Start} -> {End})");
}

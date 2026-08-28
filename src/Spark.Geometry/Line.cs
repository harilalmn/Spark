using System;

namespace Spark.Geometry;

/// <summary>
/// A straight segment between two points, parameterised over [0, 1].
/// </summary>
/// <remarks>
/// <para>
/// A line's parameter and its arc length are proportional, so <see cref="Curve.PointAt(double)"/>
/// at 0.5 and <see cref="Curve.PointAtLength(double)"/> at half the length are the same point. It
/// is the only curve here for which that is obvious, which is exactly why the two are separate
/// members on <see cref="Curve"/>.
/// </para>
/// <para>
/// The domain is [0, 1] rather than [0, <see cref="Curve.Length"/>] so that a caller who wants a
/// fraction of the way along can use the parameter directly. Nothing else in the kernel relies on
/// that choice: ask <see cref="Curve.Domain"/>.
/// </para>
/// </remarks>
public sealed class Line : Curve
{
    private readonly Point3d _start;
    private readonly Point3d _end;
    private readonly Vector3d _direction;

    /// <summary>Creates a line between two points.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="end">The end point.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is not finite, or when the two coincide — a zero-length line has no
    /// direction, and every tangent query on it would be a division by zero dressed up as geometry.
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

        Vector3d direction = end - start;
        if (!direction.IsValid || direction.Length == 0.0)
        {
            throw new ArgumentException(
                "A line's start and end points must differ.", nameof(end));
        }

        _start = start;
        _end = end;
        _direction = direction;
    }

    /// <inheritdoc/>
    public override Interval Domain => Interval.Unit;

    /// <inheritdoc/>
    public override bool IsClosed => false;

    /// <summary>The unit direction from the start point towards the end point.</summary>
    public Vector3d Direction => _direction.Normalised();

    /// <summary>Creates a line between two points.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="end">The end point.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is not finite, or when the two coincide.
    /// </exception>
    public static Line ByStartPointEndPoint(in Point3d start, in Point3d end) => new(start, end);

    /// <summary>Creates a line from a start point, a direction and a length.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="direction">The direction. Normalised first, so its length is ignored.</param>
    /// <param name="length">The length. May be negative, which runs the line the other way.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="start"/> is not finite, when <paramref name="direction"/> is
    /// zero-length or not finite, or when <paramref name="length"/> is zero or not finite.
    /// </exception>
    public static Line ByStartPointDirectionLength(
        in Point3d start, in Vector3d direction, double length)
    {
        if (!direction.TryNormalise(out Vector3d unit))
        {
            throw new ArgumentException(
                "A line's direction must have non-zero length and finite components.",
                nameof(direction));
        }

        if (!double.IsFinite(length) || length == 0.0)
        {
            throw new ArgumentException(
                "A line's length must be non-zero and finite.", nameof(length));
        }

        return new Line(start, start + (unit * length));
    }

    /// <inheritdoc/>
    public override double LengthAt(double parameter) => CheckParameter(parameter) * Length;

    /// <inheritdoc/>
    public override double ParameterAtLength(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "A distance along a curve must be finite.");
        }

        return Math.Clamp(distance / Length, 0.0, 1.0);
    }

    /// <inheritdoc/>
    public override Point3d[] Tessellate(in Tolerance tolerance = default) => [_start, _end];

    /// <inheritdoc/>
    public override Curve Reversed() => new Line(_end, _start);

    /// <inheritdoc/>
    public override Curve Trimmed(in Interval domain)
    {
        CheckTrimDomain(domain, Domain);
        return new Line(Evaluate(domain.Min), Evaluate(domain.Max));
    }

    /// <inheritdoc/>
    public override Curve TransformedBy(in Transform transform)
    {
        Point3d start = transform.OfPoint(_start);
        Point3d end = transform.OfPoint(_end);
        if (!start.IsValid || !end.IsValid || start == end)
        {
            throw new ArgumentException(
                "The transform collapses this line to a point.", nameof(transform));
        }

        return new Line(start, end);
    }

    /// <summary>A readable description of the line, for diagnostics.</summary>
    /// <returns>The start and end points.</returns>
    public override string ToString() => $"Line({_start} → {_end})";

    /// <inheritdoc/>
    protected override double ComputeLength() => _direction.Length;

    /// <inheritdoc/>
    protected override BoundingBox ComputeBoundingBox() => new(_start, _end);

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter) => Point3d.Lerp(_start, _end, parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter) => _direction;

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter) => Vector3d.Zero;
}

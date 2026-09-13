using System;
using System.Collections.Generic;

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

    /// <summary>Lifts an instance's state into a new one, so a constructor can call a factory.</summary>
    /// <param name="other">The instance to copy. Already validated by whatever produced it.</param>
    /// <remarks>
    /// <b>`E2-T59` asked for a constructor beside every library factory, and a class constructor
    /// cannot return.</b> This is what lets the public ones below read <c>: this(SomeFactory(x))</c>.
    /// The arithmetic stays in the factory, which remains its only copy, so the constructor cannot
    /// drift away from the method it mirrors.
    /// </remarks>
    private Line(Line other)
        : this(other._start, other._end)
    {
    }

    /// <summary>Creates a line from a start point, a direction and a length.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="direction">The direction. Normalised first, so its length is ignored.</param>
    /// <param name="length">The length. A negative length runs the line the other way.</param>
    /// <exception cref="ArgumentException">Thrown when the direction has no length.</exception>
    /// <remarks>Forwards to <see cref="FromStartPointDirectionLength"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Line(in Point3d start, in Vector3d direction, double length)
        : this(FromStartPointDirectionLength(start, direction, length))
    {
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
    public static Line FromStartPointEndPoint(in Point3d start, in Point3d end) => new(start, end);

    /// <summary>Creates a line from a start point, a direction and a length.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="direction">The direction. Normalised first, so its length is ignored.</param>
    /// <param name="length">The length. May be negative, which runs the line the other way.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="start"/> is not finite, when <paramref name="direction"/> is
    /// zero-length or not finite, or when <paramref name="length"/> is zero or not finite.
    /// </exception>
    public static Line FromStartPointDirectionLength(
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
    /// <remarks>
    /// Closed form: the projection of the point onto the segment, clamped to it. The base class
    /// would sample and then iterate to the same answer, more slowly and less precisely — a line
    /// is the one curve where the search is approximating arithmetic that is already exact.
    /// </remarks>
    public override double ClosestParameter(in Point3d point)
    {
        Vector3d span = _end - _start;
        double lengthSquared = span.Dot(span);

        // A zero-length line is refused at construction, so this cannot divide by zero.
        return Math.Clamp((point - _start).Dot(span) / lengthSquared, 0.0, 1.0);
    }

    /// <inheritdoc/>
    public override Point3d[] Tessellate(in Tolerance tolerance = default) => [_start, _end];

    /// <summary>
    /// Fits the line that minimises the sum of squared distances from a set of points (`E2-T72`).
    /// </summary>
    /// <param name="points">
    /// At least two points, not all coincident and not spread isotropically — see the exceptions.
    /// </param>
    /// <returns>
    /// The line through the points' centroid along their principal direction, running from the
    /// first point's end towards the last's and <b>trimmed to the span of the points</b> projected
    /// onto it, so the result is a segment rather than an unbounded direction.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when there are fewer than two points, when one is not finite, or when the points have
    /// <b>no preferred direction</b> — they are coincident, or spread evenly in a plane or a ball,
    /// where every line through the centroid fits as well as every other. Refused rather than
    /// answered, for the reason <see cref="Plane.FromBestFit"/> refuses collinear points.
    /// </exception>
    /// <remarks>
    /// <b>The fit is closed form</b> — the largest eigenvector of the points' covariance matrix,
    /// from the trigonometric solution of the characteristic cubic — so there is no iteration and
    /// no convergence tolerance, and the only threshold is the relative one deciding the direction
    /// is ambiguous ([N143](../../docs/NOTES.md)). It is <see cref="Plane.FromBestFit"/> read the
    /// other way round: a plane wants the direction the points vary <i>least</i> along, a line
    /// wants the one they vary <i>most</i> along.
    /// </remarks>
    public static Line FromBestFit(IReadOnlyList<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count < 2)
        {
            throw new ArgumentException(
                $"A line needs at least two points to be fitted through; {points.Count} were given.",
                nameof(points));
        }

        if (!LeastSquares.TryFitLine(points, out Point3d centroid, out Vector3d direction))
        {
            throw new ArgumentException(
                "Those points have no preferred direction. They are coincident, spread evenly "
                + "about their centroid, or one of them is not finite — so no line fits them "
                + "better than any other.",
                nameof(points));
        }

        // Trimmed to the points rather than returned as an infinite direction: a Line in Spark is a
        // segment, and the segment a caller means is the one that spans what they handed in.
        double low = double.MaxValue;
        double high = double.MinValue;

        foreach (Point3d point in points)
        {
            double along = (point - centroid).Dot(direction);
            low = Math.Min(low, along);
            high = Math.Max(high, along);
        }

        return new Line(centroid + (direction * low), centroid + (direction * high));
    }

    /// <summary>Fits the line that best passes through a set of points.</summary>
    /// <param name="points">At least two points with a preferred direction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the points have no preferred direction.</exception>
    /// <remarks>Forwards to <see cref="FromBestFit"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Line(IReadOnlyList<Point3d> points)
        : this(FromBestFit(points))
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Always — and this is the case that makes <see cref="Curve.IsPlanar(in Tolerance)"/> and
    /// <see cref="PlaneOf(in Tolerance)"/> two separate members.</b> A straight line lies in
    /// infinitely many planes, so it is as planar as anything can be and there is no plane to name.
    /// </remarks>
    public override bool IsPlanar(in Tolerance tolerance = default) => true;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Always <see langword="null"/>, and that is an answer rather than a gap.</b> Every plane
    /// through the line contains it, so picking one would be inventing a rotation the caller did
    /// not ask for and would then have to guess at. <see cref="Curve.IsPlanar(in Tolerance)"/>
    /// returns <see langword="true"/> for the same line, which is the pair of answers this case
    /// actually has.
    /// </remarks>
    public override Plane? PlaneOf(in Tolerance tolerance = default) => null;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Exact, and the only conversion in Spark that also preserves the parameterisation.</b> A
    /// degree-1 B-spline over two clamped control points is a straight line traversed at a constant
    /// speed, which is what a <see cref="Line"/> already is — so <c>PointAt(t)</c> gives the same
    /// point on both, not merely the same set of points. Everything above degree 1 loses that; see
    /// <see cref="NurbsConversion"/>.
    /// </remarks>
    public override NurbsConversion ToNurbsCurve(in Tolerance tolerance = default) =>
        new(new NurbsCurve(1, [_start, _end], [0.0, 0.0, 1.0, 1.0]), true);

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

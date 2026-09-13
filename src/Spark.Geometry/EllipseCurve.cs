using System;

namespace Spark.Geometry;

/// <summary>
/// An ellipse, or any part of one, parameterised over [0, sweep] by the angle of its generating
/// circle rather than by the angle at its own center.
/// </summary>
/// <remarks>
/// <para>
/// <b>One type covers what Dynamo splits into <c>Ellipse</c> and <c>EllipseArc</c>.</b> A full
/// ellipse is the case where the sweep is a full turn, and giving that its own type would double
/// the surface to encode a property of the domain.
/// </para>
/// <para>
/// <b>This is the curve whose parameter is not proportional to its arc length</b>, and it is here in
/// the first slice for that reason as much as for its own sake. The point at half the domain of an
/// ellipse is not the point halfway along it — only on a circle are those the same — so this type
/// is what exercises <see cref="Curve.ParameterAtLength(double)"/>'s numerical path, and therefore
/// what proves <see cref="Curve.DivideEqually(int)"/> is doing arc-length division rather than
/// parameter division. Every other curve in this slice has a constant speed and would pass a
/// division test that was wrong.
/// </para>
/// <para>
/// The parameter is the eccentric anomaly: the point at angle <c>t</c> is
/// <c>center + xRadius·cos t·x + yRadius·sin t·y</c>. It is not the angle subtended at the center
/// unless the two radii are equal.
/// </para>
/// </remarks>
public sealed class EllipseCurve : Curve
{
    private const double FullTurn = Math.PI * 2.0;

    private readonly Plane _plane;
    private readonly double _xRadius;
    private readonly double _yRadius;
    private readonly double _startAngle;
    private readonly double _sweep;

    private EllipseCurve(
        in Plane plane, double xRadius, double yRadius, double startAngle, double sweep)
    {
        _plane = plane;
        _xRadius = xRadius;
        _yRadius = yRadius;
        _startAngle = startAngle;
        _sweep = sweep;
    }

    /// <summary>Lifts an instance's state into a new one, so a constructor can call a factory.</summary>
    /// <param name="other">The instance to copy. Already validated by whatever produced it.</param>
    /// <remarks>
    /// <b>`E2-T59` asked for a constructor beside every library factory, and a class constructor
    /// cannot return.</b> This is what lets the public ones below read <c>: this(SomeFactory(x))</c>.
    /// The arithmetic stays in the factory, which remains its only copy, so the constructor cannot
    /// drift away from the method it mirrors.
    /// </remarks>
    private EllipseCurve(EllipseCurve other)
        : this(other._plane, other._xRadius, other._yRadius, other._startAngle, other._sweep)
    {
    }

    /// <summary>Creates a whole ellipse in a plane.</summary>
    /// <param name="plane">The plane. Its origin is the center.</param>
    /// <param name="xRadius">The radius along the plane's x axis. Must be positive.</param>
    /// <param name="yRadius">The radius along the plane's y axis. Must be positive.</param>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a radius is not positive.</exception>
    /// <remarks>Forwards to <see cref="FromPlaneRadii"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public EllipseCurve(in Plane plane, double xRadius, double yRadius)
        : this(FromPlaneRadii(plane, xRadius, yRadius))
    {
    }

    /// <summary>Creates an elliptical arc in a plane.</summary>
    /// <param name="plane">The plane. Its origin is the center.</param>
    /// <param name="xRadius">The radius along the plane's x axis. Must be positive.</param>
    /// <param name="yRadius">The radius along the plane's y axis. Must be positive.</param>
    /// <param name="startAngle">Where it begins, measured from the plane's x axis.</param>
    /// <param name="sweepAngle">How far it turns. May be negative.</param>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a radius is not positive.</exception>
    /// <remarks>Forwards to <see cref="FromPlaneRadiiAngles"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public EllipseCurve(
        in Plane plane, double xRadius, double yRadius, Angle startAngle, Angle sweepAngle)
        : this(FromPlaneRadiiAngles(plane, xRadius, yRadius, startAngle, sweepAngle))
    {
    }

    /// <inheritdoc/>
    public override Interval Domain => new(0.0, _sweep);

    /// <inheritdoc/>
    public override bool IsClosed => _sweep >= FullTurn - 1e-12;

    /// <summary>The plane the ellipse lies in. Its origin is the center.</summary>
    public Plane Plane => _plane;

    /// <summary>The center.</summary>
    public Point3d Center => _plane.Origin;

    /// <summary>The radius along the plane's x axis.</summary>
    public double XRadius => _xRadius;

    /// <summary>The radius along the plane's y axis.</summary>
    public double YRadius => _yRadius;

    /// <summary>The angle at which the curve starts, in the plane the curve holds.</summary>
    public Angle StartAngle => Angle.FromRadians(_startAngle);

    /// <summary>The angle the curve sweeps through. Always positive.</summary>
    public Angle SweepAngle => Angle.FromRadians(_sweep);

    /// <summary>Creates a full ellipse in a plane.</summary>
    /// <param name="plane">The plane. Its origin is the center.</param>
    /// <param name="xRadius">The radius along the plane's x axis. Positive and finite.</param>
    /// <param name="yRadius">The radius along the plane's y axis. Positive and finite.</param>
    /// <returns>The ellipse.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either radius is not positive and finite.
    /// </exception>
    public static EllipseCurve FromPlaneRadii(in Plane plane, double xRadius, double yRadius) =>
        FromPlaneRadiiAngles(plane, xRadius, yRadius, Angle.Zero, Angle.FullTurn);

    /// <summary>Creates part of an ellipse in a plane.</summary>
    /// <param name="plane">The plane. Its origin is the center.</param>
    /// <param name="xRadius">The radius along the plane's x axis. Positive and finite.</param>
    /// <param name="yRadius">The radius along the plane's y axis. Positive and finite.</param>
    /// <param name="startAngle">The angle at which the curve starts.</param>
    /// <param name="sweepAngle">
    /// How far it sweeps. Must be non-zero and no more than a full turn. A negative sweep flips the
    /// plane, exactly as it does on <see cref="Arc"/>.
    /// </param>
    /// <returns>The elliptical arc.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either radius is not positive and finite, or when <paramref name="sweepAngle"/>
    /// is zero, not finite, or larger than a full turn.
    /// </exception>
    public static EllipseCurve FromPlaneRadiiAngles(
        in Plane plane, double xRadius, double yRadius, Angle startAngle, Angle sweepAngle)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("An ellipse's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(xRadius) || xRadius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(xRadius), xRadius, "An ellipse's radius must be positive and finite.");
        }

        if (!double.IsFinite(yRadius) || yRadius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(yRadius), yRadius, "An ellipse's radius must be positive and finite.");
        }

        double sweep = sweepAngle.Radians;
        double start = startAngle.Radians;
        if (!double.IsFinite(sweep) || sweep == 0.0 || Math.Abs(sweep) > FullTurn + 1e-12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sweepAngle),
                sweepAngle,
                "An ellipse's sweep must be non-zero and no larger than a full turn.");
        }

        if (!double.IsFinite(start))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAngle), startAngle, "An ellipse's start angle must be finite.");
        }

        sweep = Math.Clamp(sweep, -FullTurn, FullTurn);
        return sweep > 0.0
            ? new EllipseCurve(plane, xRadius, yRadius, start, sweep)
            : new EllipseCurve(
                Plane.FromOriginXAxisYAxis(plane.Origin, plane.XAxis, -plane.YAxis),
                xRadius,
                yRadius,
                -start,
                -sweep);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Exact, and it costs nothing beyond the circle's own construction.</b> An ellipse is an
    /// affine image of a circle, and an affine map carries a rational B-spline to a rational
    /// B-spline — so the control points, the weights and the knots are a circle's, mapped through
    /// this curve's frame with <c>x</c> scaled by <see cref="XRadius"/> and <c>y</c> by
    /// <see cref="YRadius"/>. There is no separate elliptical construction here to get wrong.
    /// <para>
    /// <b>The parameterisation is not preserved, and cannot be</b> — and on an ellipse that is
    /// doubly true, since even the original's parameter is the eccentric angle rather than the
    /// angle at the centre. The two curves agree as <i>sets of points</i>, and the domain is
    /// carried across so they have the same ends.
    /// </para>
    /// </remarks>
    public override NurbsConversion ToNurbsCurve(in Tolerance tolerance = default) =>
        new(RationalArcs.Elliptical(_plane, _xRadius, _yRadius, _startAngle, _sweep), true);

    /// <inheritdoc/>
    public override Curve Reversed() =>
        new EllipseCurve(
            Plane.FromOriginXAxisYAxis(_plane.Origin, _plane.XAxis, -_plane.YAxis),
            _xRadius,
            _yRadius,
            -(_startAngle + _sweep),
            _sweep);

    /// <inheritdoc/>
    public override Curve Trimmed(in Interval domain)
    {
        CheckTrimDomain(domain, Domain);
        return FromPlaneRadiiAngles(
            _plane,
            _xRadius,
            _yRadius,
            Angle.FromRadians(_startAngle + domain.Min),
            Angle.FromRadians(domain.Length));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only similarity transforms are accepted. A general affine map does take an ellipse to an
    /// ellipse, but recovering the new curve's axes from the mapped conjugate pair is Rytz's
    /// construction, and doing it wrongly would produce a curve that is quietly the wrong shape.
    /// It waits for M3 rather than being approximated here.
    /// </remarks>
    public override Curve TransformedBy(in Transform transform)
    {
        Plane plane = CircularArcs.TransformFrame(transform, _plane, out double scale);
        return new EllipseCurve(plane, _xRadius * scale, _yRadius * scale, _startAngle, _sweep);
    }

    /// <summary>A readable description of the curve, for diagnostics.</summary>
    /// <returns>The center, both radii and the sweep.</returns>
    public override string ToString() =>
        $"EllipseCurve(center {Center}, radii {_xRadius} × {_yRadius}, sweep {SweepAngle})";

    /// <inheritdoc/>
    protected override int TessellationSeedSpans =>
        Math.Max(1, (int)Math.Ceiling(_sweep / (Math.PI * 0.5)));

    /// <inheritdoc/>
    protected override BoundingBox ComputeBoundingBox() =>
        CircularArcs.Bounds(_plane, _xRadius, _yRadius, _startAngle, _sweep);

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter) =>
        CircularArcs.PointAt(_plane, _xRadius, _yRadius, _startAngle + parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter) =>
        CircularArcs.DerivativeAt(_plane, _xRadius, _yRadius, _startAngle + parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter) =>
        CircularArcs.SecondDerivativeAt(_plane, _xRadius, _yRadius, _startAngle + parameter);
}

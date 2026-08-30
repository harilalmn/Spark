using System;

namespace Spark.Geometry;

/// <summary>
/// A torus, or a patch of one: <c>u</c> runs around the axis, <c>v</c> around the tube.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only surface here that closes in both directions</b>, which makes it the one that
/// exercises parameter wrapping on both axes and the one worth reaching for when checking that a
/// tessellator or a NURBS conversion handles a seam.
/// </para>
/// <para>
/// <b>The minor radius may exceed the major one.</b> That gives a self-intersecting *spindle*
/// torus, which is a real and useful shape — it is what a fillet between two nearly-tangent faces
/// degenerates into — and refusing it would rule out a case the kernel will meet. The surface is
/// still perfectly well defined at every parameter; what it is not is *embedded*, and that is a
/// question for whatever builds a solid out of it rather than for the surface.
/// </para>
/// </remarks>
public sealed class ToroidalSurface : Surface
{
    private readonly Plane _frame;
    private readonly double _major;
    private readonly double _minor;
    private readonly Interval _domainU;
    private readonly Interval _domainV;

    /// <summary>Creates a whole torus.</summary>
    /// <param name="frame">The centre and the plane the tube's centreline lies in.</param>
    /// <param name="majorRadius">From the axis to the centre of the tube.</param>
    /// <param name="minorRadius">The radius of the tube itself.</param>
    /// <exception cref="ArgumentOutOfRangeException">A radius is not finite and positive.</exception>
    public ToroidalSurface(in Plane frame, double majorRadius, double minorRadius)
        : this(frame, majorRadius, minorRadius, new Interval(0.0, 2.0 * Math.PI), new Interval(0.0, 2.0 * Math.PI))
    {
    }

    /// <summary>Creates a patch of a torus.</summary>
    /// <param name="frame">The centre and the plane the tube's centreline lies in.</param>
    /// <param name="majorRadius">From the axis to the centre of the tube.</param>
    /// <param name="minorRadius">The radius of the tube itself.</param>
    /// <param name="domainU">The range of angle around the axis, in radians.</param>
    /// <param name="domainV">The range of angle around the tube, in radians.</param>
    /// <exception cref="ArgumentOutOfRangeException">A radius is not finite and positive.</exception>
    /// <exception cref="ArgumentException">A domain is empty.</exception>
    public ToroidalSurface(
        in Plane frame, double majorRadius, double minorRadius, in Interval domainU, in Interval domainV)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(majorRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minorRadius);

        if (!double.IsFinite(majorRadius) || !double.IsFinite(minorRadius))
        {
            throw new ArgumentOutOfRangeException(nameof(majorRadius), "A radius must be finite.");
        }

        _frame = frame;
        _major = majorRadius;
        _minor = minorRadius;
        _domainU = SurfaceDomain.Nonempty(domainU, nameof(domainU));
        _domainV = SurfaceDomain.Nonempty(domainV, nameof(domainV));
    }

    /// <summary>The centre, and the plane the tube's centreline lies in.</summary>
    public Plane Frame => _frame;

    /// <summary>From the axis to the centre of the tube.</summary>
    public double MajorRadius => _major;

    /// <summary>The radius of the tube.</summary>
    public double MinorRadius => _minor;

    /// <inheritdoc/>
    public override Interval DomainU => _domainU;

    /// <inheritdoc/>
    public override Interval DomainV => _domainV;

    /// <inheritdoc/>
    public override bool IsClosedU => _domainU.Length >= (2.0 * Math.PI) - 1e-12;

    /// <inheritdoc/>
    public override bool IsClosedV => _domainV.Length >= (2.0 * Math.PI) - 1e-12;

    /// <summary>
    /// The area in closed form: <c>r·Δu·(R·Δv + r·(sin v₁ − sin v₀))</c>, which is
    /// <c>4π²Rr</c> for a whole torus.
    /// </summary>
    public override double Area =>
        _minor * _domainU.Length
            * ((_major * _domainV.Length) + (_minor * (Math.Sin(_domainV.Max) - Math.Sin(_domainV.Min))));

    /// <inheritdoc/>
    /// <remarks>A torus survives a rigid motion and a uniform scale, and nothing else.</remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        double scale = SphericalSurface.UniformScale(transform, nameof(transform), "torus");

        return new ToroidalSurface(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(_frame.Origin),
                transform.OfVector(_frame.XAxis),
                transform.OfVector(_frame.YAxis)),
            _major * scale,
            _minor * scale,
            _domainU,
            _domainV);
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v)
    {
        double radius = _major + (_minor * Math.Cos(v));

        return _frame.Origin
            + (_frame.XAxis * (radius * Math.Cos(u)))
            + (_frame.YAxis * (radius * Math.Sin(u)))
            + (_frame.Normal * (_minor * Math.Sin(v)));
    }

    /// <inheritdoc/>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        double cosU = Math.Cos(u);
        double sinU = Math.Sin(u);
        double cosV = Math.Cos(v);
        double sinV = Math.Sin(v);
        double radius = _major + (_minor * cosV);

        derivativeU = (_frame.XAxis * (-radius * sinU)) + (_frame.YAxis * (radius * cosU));

        derivativeV =
            (_frame.XAxis * (-_minor * sinV * cosU))
            + (_frame.YAxis * (-_minor * sinV * sinU))
            + (_frame.Normal * (_minor * cosV));
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        double cosU = Math.Cos(u);
        double sinU = Math.Sin(u);
        double cosV = Math.Cos(v);
        double sinV = Math.Sin(v);
        double radius = _major + (_minor * cosV);

        secondU = (_frame.XAxis * (-radius * cosU)) + (_frame.YAxis * (-radius * sinU));

        mixed =
            (_frame.XAxis * (_minor * sinV * sinU)) + (_frame.YAxis * (-_minor * sinV * cosU));

        secondV =
            (_frame.XAxis * (-_minor * cosV * cosU))
            + (_frame.YAxis * (-_minor * cosV * sinU))
            + (_frame.Normal * (-_minor * sinV));
    }
}

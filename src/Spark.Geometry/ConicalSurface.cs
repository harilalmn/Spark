using System;

namespace Spark.Geometry;

/// <summary>
/// A cone, or a patch of one: <c>u</c> runs around it, <c>v</c> along its axis.
/// </summary>
/// <remarks>
/// <para>
/// <b>Described by a base radius and a half-angle, not by an apex and a base.</b> An apex-and-base
/// description cannot express a truncated cone whose apex lies outside the part you have, and it
/// makes the degenerate case — a cone with no taper, which is a cylinder — impossible to write down.
/// Here the radius at height <c>v</c> is <c>r + v·tan α</c>, so <c>α = 0</c> is a cylinder,
/// a positive <c>α</c> widens with height, and the apex is wherever the radius reaches zero,
/// inside the domain or not.
/// </para>
/// <para>
/// <b>The apex is degenerate and is not hidden.</b> Where the radius reaches zero the <c>u</c>
/// derivative vanishes and there is no normal, exactly as at a sphere's pole. A cone that includes
/// its apex is legal — it is a real shape — and asking for a normal there throws.
/// </para>
/// </remarks>
public sealed class ConicalSurface : Surface
{
    private readonly Plane _frame;
    private readonly double _radius;
    private readonly double _tangent;
    private readonly Interval _domainU;
    private readonly Interval _domainV;

    /// <summary>Creates a cone.</summary>
    /// <param name="baseFrame">
    /// The base: its origin is on the axis at <c>v = 0</c>, its normal is the axis.
    /// </param>
    /// <param name="radius">The radius at <c>v = 0</c>.</param>
    /// <param name="halfAngle">
    /// How fast the radius grows with height. Zero is a cylinder; a negative angle narrows.
    /// </param>
    /// <param name="height">How far the cone extends along the axis.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The radius is negative or not finite, or the half-angle is at or past a right angle.
    /// </exception>
    /// <exception cref="ArgumentException">A domain is empty.</exception>
    public ConicalSurface(in Plane baseFrame, double radius, Angle halfAngle, in Interval height)
        : this(baseFrame, radius, halfAngle, new Interval(0.0, 2.0 * Math.PI), height)
    {
    }

    /// <summary>Creates a patch of a cone.</summary>
    /// <param name="baseFrame">The base frame; see the other constructor.</param>
    /// <param name="radius">The radius at <c>v = 0</c>.</param>
    /// <param name="halfAngle">How fast the radius grows with height.</param>
    /// <param name="domainU">The range of angle, in radians.</param>
    /// <param name="height">How far the cone extends along the axis.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius or the half-angle is out of range.</exception>
    /// <exception cref="ArgumentException">A domain is empty.</exception>
    public ConicalSurface(
        in Plane baseFrame, double radius, Angle halfAngle, in Interval domainU, in Interval height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        if (!double.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius must be finite.");
        }

        double radians = halfAngle.Radians;

        if (!double.IsFinite(radians) || Math.Abs(radians) >= (Math.PI / 2.0) - 1e-9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfAngle),
                radians,
                "A cone's half-angle must be less than a right angle; at a right angle the surface "
                + "is a plane and the parameterisation stops being one.");
        }

        _frame = baseFrame;
        _radius = radius;
        _tangent = Math.Tan(radians);
        _domainU = SurfaceDomain.Nonempty(domainU, nameof(domainU));
        _domainV = SurfaceDomain.Nonempty(height, nameof(height));
    }

    /// <summary>The base frame: origin on the axis at <c>v = 0</c>, normal along the axis.</summary>
    public Plane Frame => _frame;

    /// <summary>The radius at <c>v = 0</c>.</summary>
    public double Radius => _radius;

    /// <summary>How fast the radius grows with height.</summary>
    public Angle HalfAngle => Angle.FromRadians(Math.Atan(_tangent));

    /// <summary>The radius at a height along the axis.</summary>
    /// <param name="v">A height in <see cref="DomainV"/>.</param>
    /// <returns>The radius there, which may be zero at the apex.</returns>
    public double RadiusAt(double v) => _radius + (v * _tangent);

    /// <inheritdoc/>
    public override Interval DomainU => _domainU;

    /// <inheritdoc/>
    public override Interval DomainV => _domainV;

    /// <inheritdoc/>
    public override bool IsClosedU => _domainU.Length >= (2.0 * Math.PI) - 1e-12;

    /// <inheritdoc/>
    public override bool IsClosedV => false;

    /// <summary>
    /// The lateral area in closed form: the integral of <c>r(v)·sec α</c> over the height, times
    /// the angular span.
    /// </summary>
    /// <remarks>
    /// The <c>sec α</c> is the part a first attempt leaves out. The slant length grows faster than
    /// the height when the cone tapers, and an area computed along the axis rather than along the
    /// slant is short by exactly that factor.
    /// </remarks>
    public override double Area
    {
        get
        {
            double slant = Math.Sqrt(1.0 + (_tangent * _tangent));
            double meanRadius = (RadiusAt(_domainV.Min) + RadiusAt(_domainV.Max)) * 0.5;

            return Math.Abs(_domainU.Length * meanRadius * _domainV.Length * slant);
        }
    }

    /// <inheritdoc/>
    /// <remarks>A cone survives a rigid motion and a uniform scale; the half-angle is unchanged.</remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        double scale = SphericalSurface.UniformScale(transform, nameof(transform), "cone");

        return new ConicalSurface(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(_frame.Origin),
                transform.OfVector(_frame.XAxis),
                transform.OfVector(_frame.YAxis)),
            _radius * scale,
            HalfAngle,
            _domainU,
            new Interval(_domainV.Min * scale, _domainV.Max * scale));
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v)
    {
        double radius = RadiusAt(v);

        return _frame.Origin
            + (_frame.XAxis * (radius * Math.Cos(u)))
            + (_frame.YAxis * (radius * Math.Sin(u)))
            + (_frame.Normal * v);
    }

    /// <inheritdoc/>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        double radius = RadiusAt(v);
        double cosU = Math.Cos(u);
        double sinU = Math.Sin(u);

        derivativeU = (_frame.XAxis * (-radius * sinU)) + (_frame.YAxis * (radius * cosU));

        derivativeV =
            (_frame.XAxis * (_tangent * cosU)) + (_frame.YAxis * (_tangent * sinU)) + _frame.Normal;
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        double radius = RadiusAt(v);
        double cosU = Math.Cos(u);
        double sinU = Math.Sin(u);

        secondU = (_frame.XAxis * (-radius * cosU)) + (_frame.YAxis * (-radius * sinU));
        mixed = (_frame.XAxis * (-_tangent * sinU)) + (_frame.YAxis * (_tangent * cosU));

        // Zero: a cone is ruled, so its straight generators have no curvature along v.
        secondV = Vector3d.Zero;
    }
}

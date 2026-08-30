using System;

namespace Spark.Geometry;

/// <summary>
/// A cylinder, or a patch of one: <c>u</c> runs around it, <c>v</c> along its axis.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two directions are parameterised in different units, and that is on purpose.</b> <c>u</c>
/// is an angle in radians over [0, 2π]; <c>v</c> is a <i>distance</i> along the axis. Making both
/// fractions would be tidier and would make <c>∂S/∂v</c> depend on the height, which every
/// derivative-consuming operation would then have to divide back out. A distance keeps the axial
/// derivative a unit vector.
/// </para>
/// <para>
/// <b>Closed in u, open in v</b>, which is the case that justifies <see cref="Surface.IsClosedU"/>
/// and <see cref="Surface.IsClosedV"/> being two separate questions.
/// </para>
/// </remarks>
public sealed class CylindricalSurface : Surface
{
    private readonly Plane _frame;
    private readonly double _radius;
    private readonly Interval _domainU;
    private readonly Interval _domainV;

    /// <summary>Creates a cylinder.</summary>
    /// <param name="baseFrame">
    /// The base: its origin is on the axis, its normal is the axis, and <c>u = 0</c> is along its
    /// x-axis.
    /// </param>
    /// <param name="radius">The radius.</param>
    /// <param name="height">How far the cylinder extends along the axis, as an interval.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive.</exception>
    /// <exception cref="ArgumentException">The height interval is empty.</exception>
    public CylindricalSurface(in Plane baseFrame, double radius, in Interval height)
        : this(baseFrame, radius, new Interval(0.0, 2.0 * Math.PI), height)
    {
    }

    /// <summary>Creates a patch of a cylinder.</summary>
    /// <param name="baseFrame">The base frame; see the other constructor.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="domainU">The range of angle, in radians.</param>
    /// <param name="height">How far the cylinder extends along the axis.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive.</exception>
    /// <exception cref="ArgumentException">A domain is empty.</exception>
    public CylindricalSurface(in Plane baseFrame, double radius, in Interval domainU, in Interval height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        if (!double.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius must be finite.");
        }

        _frame = baseFrame;
        _radius = radius;
        _domainU = SurfaceDomain.Nonempty(domainU, nameof(domainU));
        _domainV = SurfaceDomain.Nonempty(height, nameof(height));
    }

    /// <summary>The base frame: origin on the axis, normal along it.</summary>
    public Plane Frame => _frame;

    /// <summary>The radius.</summary>
    public double Radius => _radius;

    /// <summary>The axis direction.</summary>
    public Vector3d Axis => _frame.Normal;

    /// <inheritdoc/>
    public override Interval DomainU => _domainU;

    /// <inheritdoc/>
    public override Interval DomainV => _domainV;

    /// <inheritdoc/>
    public override bool IsClosedU => _domainU.Length >= (2.0 * Math.PI) - 1e-12;

    /// <inheritdoc/>
    public override bool IsClosedV => false;

    /// <summary>The lateral area in closed form: <c>r·Δu·Δv</c>, or <c>2πrh</c> when whole.</summary>
    public override double Area => _radius * _domainU.Length * _domainV.Length;

    /// <inheritdoc/>
    /// <remarks>
    /// A cylinder survives a rigid motion and a uniform scale. A non-uniform one turns its circular
    /// section into an ellipse, which the kernel has no type for, so it refuses.
    /// </remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        double scale = SphericalSurface.UniformScale(transform, nameof(transform), "cylinder");

        return new CylindricalSurface(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(_frame.Origin),
                transform.OfVector(_frame.XAxis),
                transform.OfVector(_frame.YAxis)),
            _radius * scale,
            _domainU,
            new Interval(_domainV.Min * scale, _domainV.Max * scale));
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v) =>
        _frame.Origin
        + (_frame.XAxis * (_radius * Math.Cos(u)))
        + (_frame.YAxis * (_radius * Math.Sin(u)))
        + (_frame.Normal * v);

    /// <inheritdoc/>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        derivativeU =
            (_frame.XAxis * (-_radius * Math.Sin(u))) + (_frame.YAxis * (_radius * Math.Cos(u)));

        derivativeV = _frame.Normal;
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        secondU =
            (_frame.XAxis * (-_radius * Math.Cos(u))) + (_frame.YAxis * (-_radius * Math.Sin(u)));

        // Both zero, and exactly zero: a cylinder is a ruled surface, straight along its axis, so
        // it has no curvature in v and none of the mixed kind either.
        mixed = Vector3d.Zero;
        secondV = Vector3d.Zero;
    }
}

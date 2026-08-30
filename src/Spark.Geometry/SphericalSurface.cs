using System;

namespace Spark.Geometry;

/// <summary>
/// A sphere, or a patch of one: <c>u</c> runs around the equator, <c>v</c> from pole to pole.
/// </summary>
/// <remarks>
/// <para>
/// <b>The parameterisation is longitude and latitude, in radians</b>, measured in the frame's own
/// axes: <c>u</c> from the frame's x-axis towards its y-axis over [0, 2π], and <c>v</c> from
/// −π/2 at the south pole to π/2 at the north. It is the parameterisation every CAD kernel uses
/// and the one a NURBS conversion expects, and it has the property that matters here: the two
/// derivatives are orthogonal everywhere except the poles.
/// </para>
/// <para>
/// <b>The poles are degenerate and the type says so rather than hiding it.</b> At <c>v = ±π/2</c>
/// the <c>u</c> derivative is zero — the whole circle of longitude collapses to one point — so
/// there is no normal and no tangent plane, and <see cref="Surface.NormalAt"/> throws there.
/// Papering over it by returning the axis would be a lie that propagates: a tessellator would
/// produce a fan of zero-area triangles and a BRep face would carry an edge of length zero.
/// </para>
/// <para>
/// <b>The normal points outwards</b>, which follows from the u × v convention and the
/// longitude-then-latitude order. That is worth stating because a sphere is the shape most likely
/// to be used to check whether a boolean got its insides and outsides the right way round.
/// </para>
/// </remarks>
public sealed class SphericalSurface : Surface
{
    private readonly Plane _frame;
    private readonly double _radius;
    private readonly Interval _domainU;
    private readonly Interval _domainV;

    /// <summary>Creates a whole sphere.</summary>
    /// <param name="centre">The centre, and the frame the parameterisation is measured in.</param>
    /// <param name="radius">The radius.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive.</exception>
    public SphericalSurface(in Plane centre, double radius)
        : this(centre, radius, new Interval(0.0, 2.0 * Math.PI), new Interval(-Math.PI / 2.0, Math.PI / 2.0))
    {
    }

    /// <summary>Creates a patch of a sphere.</summary>
    /// <param name="centre">The centre, and the frame the parameterisation is measured in.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="domainU">The range of longitude, in radians.</param>
    /// <param name="domainV">The range of latitude, in radians, within [−π/2, π/2].</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive.</exception>
    /// <exception cref="ArgumentException">A domain is empty, or the latitude leaves the poles.</exception>
    public SphericalSurface(in Plane centre, double radius, in Interval domainU, in Interval domainV)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        if (!double.IsFinite(radius))
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius must be finite.");
        }

        _frame = centre;
        _radius = radius;
        _domainU = SurfaceDomain.Nonempty(domainU, nameof(domainU));
        _domainV = SurfaceDomain.Nonempty(domainV, nameof(domainV));

        if (_domainV.Min < -Math.PI / 2.0 - 1e-12 || _domainV.Max > Math.PI / 2.0 + 1e-12)
        {
            throw new ArgumentException(
                "Latitude runs from -π/2 at the south pole to π/2 at the north; a wider range would "
                + "cover the sphere twice and make the parameterisation ambiguous.",
                nameof(domainV));
        }
    }

    /// <summary>The centre, and the frame longitude and latitude are measured in.</summary>
    public Plane Frame => _frame;

    /// <summary>The radius.</summary>
    public double Radius => _radius;

    /// <summary>The centre of the sphere.</summary>
    public Point3d Centre => _frame.Origin;

    /// <inheritdoc/>
    public override Interval DomainU => _domainU;

    /// <inheritdoc/>
    public override Interval DomainV => _domainV;

    /// <inheritdoc/>
    public override bool IsClosedU => _domainU.Length >= (2.0 * Math.PI) - 1e-12;

    /// <inheritdoc/>
    /// <remarks>
    /// Never. A sphere meets itself at the poles, but that is a degeneracy rather than a seam — the
    /// two ends of the <c>v</c> domain are single points, not a shared edge.
    /// </remarks>
    public override bool IsClosedV => false;

    /// <summary>
    /// The area of the patch, in closed form: <c>r²·Δu·(sin v₁ − sin v₀)</c>, which is
    /// <c>4πr²</c> for a whole sphere.
    /// </summary>
    public override double Area =>
        _radius * _radius * _domainU.Length * (Math.Sin(_domainV.Max) - Math.Sin(_domainV.Min));

    /// <inheritdoc/>
    /// <remarks>
    /// <b>A sphere survives a rigid motion and a uniform scale, and nothing else.</b> Under a
    /// non-uniform scale it becomes an ellipsoid, and the kernel has no ellipsoid — so it refuses
    /// rather than returning a sphere of some averaged radius, which would be wrong in a way
    /// nothing downstream could detect.
    /// </remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        double scale = UniformScale(transform, nameof(transform), "sphere");

        return new SphericalSurface(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(_frame.Origin),
                transform.OfVector(_frame.XAxis),
                transform.OfVector(_frame.YAxis)),
            _radius * scale,
            _domainU,
            _domainV);
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v)
    {
        double cosV = Math.Cos(v);

        return _frame.Origin
            + (_frame.XAxis * (_radius * cosV * Math.Cos(u)))
            + (_frame.YAxis * (_radius * cosV * Math.Sin(u)))
            + (_frame.Normal * (_radius * Math.Sin(v)));
    }

    /// <inheritdoc/>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        double cosU = Math.Cos(u);
        double sinU = Math.Sin(u);
        double cosV = Math.Cos(v);
        double sinV = Math.Sin(v);

        derivativeU =
            (_frame.XAxis * (-_radius * cosV * sinU)) + (_frame.YAxis * (_radius * cosV * cosU));

        derivativeV =
            (_frame.XAxis * (-_radius * sinV * cosU))
            + (_frame.YAxis * (-_radius * sinV * sinU))
            + (_frame.Normal * (_radius * cosV));
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        double cosU = Math.Cos(u);
        double sinU = Math.Sin(u);
        double cosV = Math.Cos(v);
        double sinV = Math.Sin(v);

        secondU =
            (_frame.XAxis * (-_radius * cosV * cosU)) + (_frame.YAxis * (-_radius * cosV * sinU));

        mixed =
            (_frame.XAxis * (_radius * sinV * sinU)) + (_frame.YAxis * (-_radius * sinV * cosU));

        secondV =
            (_frame.XAxis * (-_radius * cosV * cosU))
            + (_frame.YAxis * (-_radius * cosV * sinU))
            + (_frame.Normal * (-_radius * sinV));
    }

    /// <summary>Checks that a transform is a rigid motion with at most a uniform scale.</summary>
    /// <param name="transform">The transform.</param>
    /// <param name="name">The parameter name, for the message.</param>
    /// <param name="kind">What kind of surface is refusing, for the message.</param>
    /// <returns>The uniform scale factor.</returns>
    /// <exception cref="ArgumentException">The transform is not similar.</exception>
    /// <remarks>
    /// Shared by every surface whose shape is defined by radii — a sphere, a cylinder, a cone, a
    /// torus. All four are exactly as fragile under a non-uniform scale, and all four should say so
    /// in the same words.
    /// </remarks>
    internal static double UniformScale(in Transform transform, string name, string kind)
    {
        double x = transform.OfVector(Vector3d.XAxis).Length;
        double y = transform.OfVector(Vector3d.YAxis).Length;
        double z = transform.OfVector(Vector3d.ZAxis).Length;

        if (x <= 0.0 || Math.Abs(y - x) > x * 1e-9 || Math.Abs(z - x) > x * 1e-9)
        {
            throw new ArgumentException(
                $"A {kind} stays a {kind} under a rigid motion and a uniform scale, and nothing "
                + "else. This transform scales the axes differently, and the kernel has no surface "
                + "type for the result — so refusing is more honest than returning a shape that is "
                + "the wrong one.",
                name);
        }

        return x;
    }

}

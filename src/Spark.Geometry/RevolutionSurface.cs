using System;

namespace Spark.Geometry;

/// <summary>
/// A curve turned about an axis: <c>u</c> is the angle of rotation, <c>v</c> follows the curve.
/// </summary>
/// <remarks>
/// <para>
/// <b>Angle first, profile second</b>, which puts this in the same order as the sphere, the
/// cylinder, the cone and the torus — all four of which are surfaces of revolution in disguise.
/// Anything written against the analytic surfaces' <c>u</c>-is-an-angle convention then works here
/// too, and a tessellator that seams along <c>u = 0</c> seams all five in the same place.
/// </para>
/// <para>
/// <b>A profile that touches the axis is legal and degenerate there.</b> That is how a sphere is
/// made — revolve a half-circle whose ends are on the axis — so refusing it would rule out the most
/// ordinary use there is. The point on the axis has no normal, exactly as a sphere's pole does not.
/// </para>
/// </remarks>
public sealed class RevolutionSurface : Surface
{
    private readonly Curve _profile;
    private readonly Point3d _origin;
    private readonly Vector3d _axis;
    private readonly Interval _domainU;

    /// <summary>Creates a full surface of revolution.</summary>
    /// <param name="profile">The curve to revolve.</param>
    /// <param name="axisOrigin">A point on the axis.</param>
    /// <param name="axisDirection">The axis direction; its length is ignored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException">The axis has no length.</exception>
    public RevolutionSurface(Curve profile, in Point3d axisOrigin, in Vector3d axisDirection)
        : this(profile, axisOrigin, axisDirection, new Interval(0.0, 2.0 * Math.PI))
    {
    }

    /// <summary>Creates part of a surface of revolution.</summary>
    /// <param name="profile">The curve to revolve.</param>
    /// <param name="axisOrigin">A point on the axis.</param>
    /// <param name="axisDirection">The axis direction; its length is ignored.</param>
    /// <param name="sweep">How far to turn, in radians.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException">The axis has no length, or the sweep is empty.</exception>
    public RevolutionSurface(
        Curve profile, in Point3d axisOrigin, in Vector3d axisDirection, in Interval sweep)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (axisDirection.LengthSquared <= 0.0 || !double.IsFinite(axisDirection.LengthSquared))
        {
            throw new ArgumentException(
                "A surface of revolution needs an axis with a direction.", nameof(axisDirection));
        }

        _profile = profile;
        _origin = axisOrigin;
        _axis = axisDirection.Normalised();
        _domainU = SurfaceDomain.Nonempty(sweep, nameof(sweep));
    }

    /// <summary>The curve being revolved.</summary>
    public Curve Profile => _profile;

    /// <summary>A point on the axis.</summary>
    public Point3d AxisOrigin => _origin;

    /// <summary>The unit axis direction.</summary>
    public Vector3d Axis => _axis;

    /// <inheritdoc/>
    public override Interval DomainU => _domainU;

    /// <inheritdoc/>
    public override Interval DomainV => _profile.Domain;

    /// <inheritdoc/>
    public override bool IsClosedU => _domainU.Length >= (2.0 * Math.PI) - 1e-12;

    /// <inheritdoc/>
    public override bool IsClosedV => _profile.IsClosed;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Piegl and Tiller's construction (their algorithm A8.1): each control point of the profile's
    /// NURBS curve is swept by the rational circle of the sweep, in its own plane perpendicular to
    /// the axis and at its own distance from it, and the weights multiply. The knots are the
    /// circle's along <c>u</c> and the profile's along <c>v</c>, so both domains are the original's.
    /// </para>
    /// <para>
    /// <b>Exact exactly when the profile converts exactly</b>, and never parameterised like the
    /// original, because the rational circle is not: a revolved <see cref="Helix"/> is an
    /// approximation, and everything else is the same sheet visited at different parameters. A
    /// profile control point on the axis sweeps to a point, which is the degeneracy this type
    /// documents and the conversion carries across unchanged.
    /// </para>
    /// </remarks>
    public override NurbsSurfaceConversion ToNurbsSurface(in Tolerance tolerance = default)
    {
        NurbsConversion profile = _profile.ToNurbsCurve(tolerance);
        NurbsCurve curve = profile.Curve;

        (Point2d[] around, double[] aroundWeights, KnotVector knotsU) = RationalArcs.Arc(_domainU, 1.0);
        Point3d[] points = curve.ControlPoints();
        double[] weights = curve.Weights();

        Point3d[,] net = new Point3d[around.Length, points.Length];
        double[,] netWeights = new double[around.Length, points.Length];

        for (int j = 0; j < points.Length; j++)
        {
            Vector3d offset = points[j] - _origin;
            Vector3d axial = _axis * offset.Dot(_axis);
            Vector3d radial = offset - axial;
            double radius = radial.Length;

            // On the axis there is no radial direction and nothing to sweep: the whole row collapses
            // onto the point, which is what the surface does there too.
            Vector3d first = radius > 0.0 ? radial / radius : Vector3d.Zero;
            Vector3d second = _axis.Cross(first);
            Point3d centre = _origin + axial;

            for (int i = 0; i < around.Length; i++)
            {
                net[i, j] = centre + (first * (around[i].X * radius)) + (second * (around[i].Y * radius));
                netWeights[i, j] = aroundWeights[i] * weights[j];
            }
        }

        return new NurbsSurfaceConversion(
            new NurbsSurface(knotsU, curve.Knots, net, netWeights), profile.IsExact, false);
    }

    /// <inheritdoc/>
    public override Surface TransformedBy(in Transform transform) =>
        new RevolutionSurface(
            _profile.TransformedBy(transform),
            transform.OfPoint(_origin),
            transform.OfVector(_axis),
            _domainU);

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v) =>
        _origin + Rotate(_profile.PointAt(v) - _origin, u);

    /// <inheritdoc/>
    /// <remarks>
    /// <b><c>∂S/∂u</c> is the axis crossed with the offset from the axis</b>, which is the velocity
    /// of a point going round a circle — zero exactly on the axis, which is the degeneracy this type
    /// documents. <c>∂S/∂v</c> is the profile's own derivative, rotated: rotated rather than
    /// re-derived, and <b>not normalised</b>, because the surface's <c>v</c> parameterisation is
    /// the curve's and losing its speed would make every area and curvature here subtly wrong.
    /// </remarks>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        Vector3d offset = Rotate(_profile.PointAt(v) - _origin, u);

        derivativeU = _axis.Cross(offset);
        derivativeV = Rotate(_profile.DerivativeAt(v), u);
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        Vector3d offset = Rotate(_profile.PointAt(v) - _origin, u);
        Vector3d tangent = Rotate(_profile.DerivativeAt(v), u);

        // Turning twice: the second derivative of a rotation is the axis applied twice, which for
        // a unit axis is the component of the offset perpendicular to it, negated.
        secondU = _axis.Cross(_axis.Cross(offset));
        mixed = _axis.Cross(tangent);
        secondV = Rotate(_profile.SecondDerivativeAt(v), u);
    }

    private Vector3d Rotate(in Vector3d vector, double angle) =>
        vector.Rotate(_axis, Angle.FromRadians(angle));
}

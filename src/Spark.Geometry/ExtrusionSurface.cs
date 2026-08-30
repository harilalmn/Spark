using System;

namespace Spark.Geometry;

/// <summary>
/// A curve swept along a straight line: <c>u</c> follows the curve, <c>v</c> the sweep.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>v</c> is a distance along a unit direction, not a fraction.</b> That keeps
/// <c>∂S/∂v</c> a unit vector whatever the height, which is what makes the area come out as
/// <i>curve length × height</i> without a correction factor, and what makes the surface's normal
/// independent of how far it was extruded.
/// </para>
/// <para>
/// <b>The <c>u</c> domain is the curve's own.</b> Renormalising it to [0, 1] would make an
/// extrusion of a NURBS curve carry a parameterisation that no longer matches the curve it came
/// from, and every operation that needed to relate the two — trimming, a BRep edge, an iso-curve —
/// would have to undo it.
/// </para>
/// </remarks>
public sealed class ExtrusionSurface : Surface
{
    private readonly Curve _profile;
    private readonly Vector3d _direction;
    private readonly Interval _domainV;

    /// <summary>Creates an extrusion.</summary>
    /// <param name="profile">The curve to sweep.</param>
    /// <param name="direction">
    /// Which way and how far to sweep it. Its length becomes the <c>v</c> domain [0, |d|].
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException">The direction has no length.</exception>
    public ExtrusionSurface(Curve profile, in Vector3d direction)
        : this(profile, direction.LengthSquared > 0.0 ? direction.Normalised() : direction, new Interval(0.0, direction.Length))
    {
    }

    /// <summary>Creates an extrusion over an explicit height range.</summary>
    /// <param name="profile">The curve to sweep.</param>
    /// <param name="direction">A unit direction to sweep along.</param>
    /// <param name="height">How far to sweep, as an interval of distances.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException">The direction has no length, or the height is empty.</exception>
    public ExtrusionSurface(Curve profile, in Vector3d direction, in Interval height)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (direction.LengthSquared <= 0.0 || !double.IsFinite(direction.LengthSquared))
        {
            throw new ArgumentException(
                "An extrusion needs a direction with a length; a zero direction produces a surface "
                + "with no area and no normal.",
                nameof(direction));
        }

        _profile = profile;
        _direction = direction.Normalised();
        _domainV = SurfaceDomain.Nonempty(height, nameof(height));
    }

    /// <summary>The curve being swept.</summary>
    public Curve Profile => _profile;

    /// <summary>The unit direction of the sweep.</summary>
    public Vector3d Direction => _direction;

    /// <inheritdoc/>
    public override Interval DomainU => _profile.Domain;

    /// <inheritdoc/>
    public override Interval DomainV => _domainV;

    /// <inheritdoc/>
    public override bool IsClosedU => _profile.IsClosed;

    /// <inheritdoc/>
    public override bool IsClosedV => false;

    /// <summary>
    /// The area in closed form when the sweep is perpendicular to the curve's plane, and by
    /// quadrature otherwise.
    /// </summary>
    /// <remarks>
    /// <b>Not simply <i>length × height</i>.</b> That holds only when the direction is
    /// perpendicular to the curve everywhere; sweep a straight line along its own direction and the
    /// "surface" has no area at all. The base class's integration of |∂S/∂u × ∂S/∂v| gets it right
    /// in both cases, so this type does not override it — which is worth a note precisely because
    /// the closed form looks so obviously available.
    /// </remarks>
    public override double Area => base.Area;

    /// <inheritdoc/>
    public override Surface TransformedBy(in Transform transform) =>
        new ExtrusionSurface(
            _profile.TransformedBy(transform),
            transform.OfVector(_direction),
            ScaledHeight(transform));

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v) => _profile.PointAt(u) + (_direction * v);

    /// <inheritdoc/>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        derivativeU = _profile.DerivativeAt(u);
        derivativeV = _direction;
    }

    /// <inheritdoc/>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        secondU = _profile.SecondDerivativeAt(u);

        // Both exactly zero: the sweep is straight and the profile does not change along it, so
        // there is no curvature in v and no mixed term.
        mixed = Vector3d.Zero;
        secondV = Vector3d.Zero;
    }

    /// <summary>The height interval after a transform, which scales with the direction.</summary>
    private Interval ScaledHeight(in Transform transform)
    {
        double scale = transform.OfVector(_direction).Length;

        if (scale <= 0.0 || !double.IsFinite(scale))
        {
            throw new ArgumentException(
                "The transform collapses the extrusion's direction, so there is no surface left.",
                nameof(transform));
        }

        return new Interval(_domainV.Min * scale, _domainV.Max * scale);
    }
}

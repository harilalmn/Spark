using System;

namespace Spark.Geometry;

/// <summary>
/// A bounded rectangle of a plane: the simplest surface there is, and the one every other is
/// checked against.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see cref="Plane"/> is unbounded and this is not.</b> The plane says where the sheet lies
/// and which way is up; the two domains say how far it extends along the plane's x- and y-axes.
/// Keeping them apart is what lets a plane go on being the lightweight frame that
/// <see cref="Curve"/>, <see cref="Transform"/> and the node library all pass around, while a
/// surface is a thing with an area and a boundary.
/// </para>
/// <para>
/// <b>Its parameters are distances, not fractions.</b> <c>PointAt(3, 4)</c> is three units along
/// the plane's x-axis and four along its y-axis from the origin, which makes every derivative a
/// unit vector and the area exactly the product of the two domain lengths. A [0, 1] × [0, 1]
/// parameterisation would have been tidier to look at and would have made every downstream
/// calculation carry a scale factor.
/// </para>
/// </remarks>
public sealed class PlaneSurface : Surface
{
    private readonly Plane _plane;
    private readonly Interval _domainU;
    private readonly Interval _domainV;

    /// <summary>Creates a bounded piece of a plane.</summary>
    /// <param name="plane">The plane it lies in.</param>
    /// <param name="domainU">How far it extends along the plane's x-axis.</param>
    /// <param name="domainV">How far it extends along the plane's y-axis.</param>
    /// <exception cref="ArgumentException">
    /// A domain is not finite, or has no length. A surface with a zero-width side has no area and
    /// no normal, and every operation on it would have to special-case it.
    /// </exception>
    public PlaneSurface(in Plane plane, in Interval domainU, in Interval domainV)
    {
        _plane = plane;
        _domainU = Check(domainU, nameof(domainU));
        _domainV = Check(domainV, nameof(domainV));
    }

    /// <summary>Creates a rectangle centred on a plane's origin.</summary>
    /// <param name="plane">The plane it lies in, and the centre of the rectangle.</param>
    /// <param name="width">Its extent along the plane's x-axis.</param>
    /// <param name="height">Its extent along the plane's y-axis.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A side is not finite and positive.</exception>
    public static PlaneSurface ByPlaneSize(in Plane plane, double width, double height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return new PlaneSurface(
            plane,
            new Interval(-width * 0.5, width * 0.5),
            new Interval(-height * 0.5, height * 0.5));
    }

    /// <summary>Creates the rectangle spanned by two corners of a plane's coordinates.</summary>
    /// <param name="plane">The plane it lies in.</param>
    /// <param name="corner">One corner, in the plane's own coordinates.</param>
    /// <param name="oppositeCorner">The opposite corner, in the plane's own coordinates.</param>
    /// <returns>The surface.</returns>
    /// <exception cref="ArgumentException">The corners share an x or a y coordinate.</exception>
    public static PlaneSurface ByPlaneCorners(in Plane plane, in Point2d corner, in Point2d oppositeCorner) =>
        new(
            plane,
            new Interval(corner.X, oppositeCorner.X).MakeIncreasing(),
            new Interval(corner.Y, oppositeCorner.Y).MakeIncreasing());

    /// <summary>The plane this surface is a piece of.</summary>
    public Plane Plane => _plane;

    /// <inheritdoc/>
    public override Interval DomainU => _domainU;

    /// <inheritdoc/>
    public override Interval DomainV => _domainV;

    /// <inheritdoc/>
    public override bool IsClosedU => false;

    /// <inheritdoc/>
    public override bool IsClosedV => false;

    /// <summary>
    /// The rectangle's area, exactly: the product of the two domain lengths, because the
    /// parameters are distances.
    /// </summary>
    public override double Area => _domainU.Length * _domainV.Length;

    /// <summary>
    /// The box around the four corners, exactly rather than by sampling.
    /// </summary>
    public override BoundingBox BoundingBox =>
        BoundingBox.Empty
            .Union(Evaluate(_domainU.Min, _domainV.Min))
            .Union(Evaluate(_domainU.Max, _domainV.Min))
            .Union(Evaluate(_domainU.Min, _domainV.Max))
            .Union(Evaluate(_domainU.Max, _domainV.Max));

    /// <inheritdoc/>
    /// <remarks>
    /// A plane survives every transform there is — including a non-uniform scale, which shears the
    /// rectangle but leaves it planar. **The domains are not rescaled**, because the transformed
    /// axes are no longer unit length and the parameterisation stays a distance along *those* axes,
    /// which is what keeps <c>PointAt</c> agreeing with the transform.
    /// </remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        Point3d origin = transform.OfPoint(_plane.Origin);
        Vector3d x = transform.OfVector(_plane.XAxis);
        Vector3d y = transform.OfVector(_plane.YAxis);

        double scaleX = x.Length;
        double scaleY = y.Length;

        if (scaleX <= 0.0 || scaleY <= 0.0)
        {
            throw new ArgumentException(
                "The transform collapses the plane, so there is no surface left to return.",
                nameof(transform));
        }

        // Scaled domains against normalised axes, rather than unnormalised axes against the
        // original domains: `Plane` requires unit axes, so the scale has to go somewhere, and the
        // domain is the only place it can go without changing what a parameter means.
        return new PlaneSurface(
            Plane.ByOriginXAxisYAxis(origin, x, y),
            new Interval(_domainU.Min * scaleX, _domainU.Max * scaleX),
            new Interval(_domainV.Min * scaleY, _domainV.Max * scaleY));
    }

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v) =>
        _plane.Origin + (_plane.XAxis * u) + (_plane.YAxis * v);

    /// <inheritdoc/>
    /// <remarks>Exactly the plane's axes, everywhere — which is the definition of a plane.</remarks>
    protected override void EvaluateDerivatives(
        double u, double v, out Vector3d derivativeU, out Vector3d derivativeV)
    {
        derivativeU = _plane.XAxis;
        derivativeV = _plane.YAxis;
    }

    /// <inheritdoc/>
    /// <remarks>All zero: a plane does not curve, which is why its curvatures are both zero.</remarks>
    protected override void EvaluateSecondDerivatives(
        double u, double v, out Vector3d secondU, out Vector3d mixed, out Vector3d secondV)
    {
        secondU = Vector3d.Zero;
        mixed = Vector3d.Zero;
        secondV = Vector3d.Zero;
    }

    private static Interval Check(in Interval domain, string name)
    {
        if (!domain.IsValid || domain.Length == 0.0)
        {
            throw new ArgumentException(
                "A plane surface needs a finite domain with a non-zero length; a side of zero width "
                + "has no area and no normal.",
                name);
        }

        return domain.MakeIncreasing();
    }
}

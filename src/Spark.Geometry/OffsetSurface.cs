using System;

namespace Spark.Geometry;

/// <summary>
/// A surface at a constant distance from another, along that surface's own normal (`E2-T66`).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists because an offset is exact and a NURBS surface cannot hold it.</b> The
/// offset of a polynomial surface is generally not polynomial, which is the same fact that makes
/// <see cref="CurveOffset"/> fit rather than compute — but <see cref="Surface"/> is an evaluatable
/// type rather than a representation, so <c>S(u, v) + d · N(u, v)</c> is a surface Spark can carry
/// <i>exactly</i>. Approximation is what happens when such a surface is asked for its NURBS form,
/// and nowhere earlier.
/// </para>
/// <para>
/// <b>The parameterisation is the base surface's, point for point.</b> The offset at
/// <c>(u, v)</c> is the base's point at <c>(u, v)</c> moved along the base's normal there, so
/// anything that pairs the two by parameter — a ruling, a trim, a BRep face — relates them
/// directly.
/// </para>
/// <para>
/// <b>An offset larger than the surface's smallest radius of curvature folds.</b> Where the
/// distance exceeds the radius of curvature the offset develops cusps and crosses itself; that is
/// a property of offsetting rather than of this implementation, and no surface type can avoid it.
/// The fold is visible as a degenerate normal, so <see cref="Surface.PrincipalCurvatures"/> and
/// everything built on it throw there rather than answering quietly.
/// </para>
/// </remarks>
public sealed class OffsetSurface : Surface
{
    private readonly Surface _basis;
    private readonly double _distance;

    /// <summary>Creates an offset of a surface.</summary>
    /// <param name="basis">The surface to offset.</param>
    /// <param name="distance">
    /// How far to move it along its own normal. Negative moves the other way. Zero is allowed and
    /// gives a surface that evaluates to the original.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="basis"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is not finite.</exception>
    public OffsetSurface(Surface basis, double distance)
    {
        ArgumentNullException.ThrowIfNull(basis);

        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "An offset distance must be finite.");
        }

        _basis = basis;
        _distance = distance;
    }

    /// <summary>The surface this one is offset from.</summary>
    public Surface Basis => _basis;

    /// <summary>How far along the base surface's normal this one sits.</summary>
    public double Distance => _distance;

    /// <inheritdoc/>
    public override Interval DomainU => _basis.DomainU;

    /// <inheritdoc/>
    public override Interval DomainV => _basis.DomainV;

    /// <inheritdoc/>
    public override bool IsClosedU => _basis.IsClosedU;

    /// <inheritdoc/>
    public override bool IsClosedV => _basis.IsClosedV;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>A transform that changes distances changes what the offset means</b>, so the distance is
    /// scaled with it. The factor is the cube root of the matrix determinant, which is the scale a
    /// similarity transform applies to every length — exactly right for a rotation, a translation,
    /// a mirror or a uniform scale, which is every transform that maps an offset to an offset.
    /// </para>
    /// <para>
    /// <b>A non-uniform scale has no such factor, and no answer here would be correct</b>: the
    /// offset of a squashed sphere is not a squashed offset of a sphere, because the surface's
    /// normals no longer point where they did. This returns the nearest honest thing rather than
    /// throwing — the offset of the transformed surface by a representative distance — and a caller
    /// scaling non-uniformly should offset afterwards rather than before.
    /// </para>
    /// </remarks>
    public override Surface TransformedBy(in Transform transform)
    {
        double determinant =
            (transform.M00 * ((transform.M11 * transform.M22) - (transform.M12 * transform.M21)))
            - (transform.M01 * ((transform.M10 * transform.M22) - (transform.M12 * transform.M20)))
            + (transform.M02 * ((transform.M10 * transform.M21) - (transform.M11 * transform.M20)));

        return new OffsetSurface(
            _basis.TransformedBy(transform), _distance * Math.Cbrt(Math.Abs(determinant)));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Refused rather than approximated.</b> The offset of a NURBS surface is not a NURBS
    /// surface, so there is no exact answer to give, and Spark has no surface fitting to give an
    /// inexact one with — that capability is <c>Surface.ApproximateWithTolerance</c>, which is not
    /// written. Returning a silently fitted surface would be exactly the claim
    /// <see cref="NurbsSurfaceConversion"/> exists to prevent.
    /// </remarks>
    public override NurbsSurfaceConversion ToNurbsSurface(in Tolerance tolerance = default) =>
        throw new NotSupportedException(
            "An offset surface has no NURBS form: the offset of a polynomial surface is not "
            + "polynomial. Approximating one needs surface fitting to a tolerance, which Spark does "
            + "not have yet (Surface.ApproximateWithTolerance).");

    /// <inheritdoc/>
    /// <remarks>Offsetting an offset adds the distances rather than nesting the wrappers.</remarks>
    public override Surface Offset(double distance) =>
        new OffsetSurface(_basis, _distance + distance);

    /// <inheritdoc/>
    public override string ToString() =>
        $"OffsetSurface({_basis.GetType().Name}, "
        + $"{_distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";

    /// <inheritdoc/>
    protected override Point3d Evaluate(double u, double v) =>
        _basis.PointAt(u, v) + (_basis.NormalAt(u, v) * _distance);
}

using System;

namespace Spark.Geometry;

/// <summary>
/// The result of turning a surface into a NURBS surface: the surface, whether it is the
/// <i>same sheet</i> or an approximation to it, and whether it is visited at the same parameters
/// (`E2-T66`).
/// </summary>
/// <remarks>
/// <para>
/// The surface-side twin of <see cref="NurbsConversion"/>, and it exists for the same reason: a
/// member handing back a bare <see cref="NurbsSurface"/> gives a caller no way to tell an exact
/// conversion from a good approximation, and Spark has both. The five analytic surfaces convert
/// exactly through <see cref="SurfaceConversion"/>; an <see cref="ExtrusionSurface"/>, a
/// <see cref="RevolutionSurface"/> and a <see cref="RuledSurface"/> convert exactly when the curves
/// they are built from do, and a <see cref="Helix"/> profile makes any of them approximate
/// ([N165](../../docs/NOTES.md)).
/// </para>
/// <para>
/// <b><see cref="IsExact"/> is about the set of points, not about the parameterisation</b>, and
/// <see cref="PreservesParameterisation"/> is the second, stronger claim. A sphere's NURBS form
/// traces exactly the same sheet and visits it at different parameters, because a rational
/// quadratic walks a circle by a projective function of the angle; a plane's bilinear form is the
/// same sheet at the same parameters. The distinction matters to anything that pairs parameters
/// across two surfaces or two curves — a ruled surface does, which is why a ruled surface between
/// two arcs is <i>not</i> exact even though each arc is.
/// </para>
/// </remarks>
public sealed class NurbsSurfaceConversion
{
    /// <summary>Creates a conversion result.</summary>
    /// <param name="surface">The NURBS surface.</param>
    /// <param name="isExact">
    /// Whether <paramref name="surface"/> is exactly the same set of points as the original.
    /// </param>
    /// <param name="preservesParameterisation">
    /// Whether <paramref name="surface"/> also visits those points at the same parameters as the
    /// original. Implies <paramref name="isExact"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The parameterisation is claimed preserved on a conversion that is not exact.
    /// </exception>
    public NurbsSurfaceConversion(NurbsSurface surface, bool isExact, bool preservesParameterisation)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (preservesParameterisation && !isExact)
        {
            throw new ArgumentException(
                "A conversion cannot keep the parameterisation of a sheet it does not reproduce.",
                nameof(preservesParameterisation));
        }

        Surface = surface;
        IsExact = isExact;
        PreservesParameterisation = preservesParameterisation;
    }

    /// <summary>The NURBS surface.</summary>
    public NurbsSurface Surface { get; }

    /// <summary>
    /// Whether <see cref="Surface"/> is the same set of points as the surface it came from, rather
    /// than an approximation to it.
    /// </summary>
    /// <remarks>
    /// <b>When this is <see langword="false"/> the accuracy is the caller's to measure</b>, with
    /// <see cref="Spark.Geometry.Surface.ClosestPoint(in Point3d, out double, out double)"/> against
    /// the original at as many points as the caller cares about. Nothing here is a proved bound.
    /// </remarks>
    public bool IsExact { get; }

    /// <summary>
    /// Whether <see cref="Surface"/> visits the same points at the same parameters as the original,
    /// so that <c>PointAt(u, v)</c> agrees between the two and not only the sheets.
    /// </summary>
    /// <remarks>
    /// True for a plane, for a NURBS surface, and for an extrusion or a ruled surface built from
    /// curves whose own conversions keep theirs. False for anything with a rational circle in it.
    /// </remarks>
    public bool PreservesParameterisation { get; }

    /// <summary>A readable description, for diagnostics.</summary>
    /// <returns>Whether the conversion was exact, and the surface's degrees and control-net size.</returns>
    public override string ToString() =>
        $"NurbsSurfaceConversion({(IsExact ? "exact" : "approximate")}"
        + $"{(PreservesParameterisation ? ", same parameters" : string.Empty)}, "
        + $"degree {Surface.KnotsU.Degree} × {Surface.KnotsV.Degree}, "
        + $"{Surface.KnotsU.ControlPointCount} × {Surface.KnotsV.ControlPointCount} control points)";
}

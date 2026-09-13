using System;

namespace Spark.Geometry;

/// <summary>
/// The result of turning a curve into a NURBS curve: the curve, and whether it is the
/// <i>same curve</i> or a good approximation to it (`E2-T71`).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists because one of Spark's curves cannot be converted exactly and the rest
/// can</b>, and a member handing back a bare <see cref="NurbsCurve"/> would give a caller no way to
/// tell which had happened. <see cref="Line"/>, <see cref="PolyLine"/>, <see cref="Arc"/>,
/// <see cref="Circle"/> and <see cref="EllipseCurve"/> are all exactly representable —
/// <see cref="Helix"/> is <b>provably not</b>, at any degree, with any knot vector
/// ([N165](../../docs/NOTES.md)). The difference is the difference between a boolean that is right
/// and one that is merely plausible, so it travels with the answer rather than being something the
/// caller has to know to ask for.
/// </para>
/// <para>
/// It is the same shape as <see cref="CurveIntersections"/> and
/// <see cref="CurveSurfaceIntersections"/>: a result type whose second member is the caveat. A
/// class rather than a record for the same reason those are — what it publishes is the curve, and
/// an equality contract over a NURBS curve is not something anybody asked for.
/// </para>
/// <para>
/// <b><see cref="IsExact"/> is about the set of points, not about the parameterisation.</b> An
/// exact conversion traces the same points as the original and, above degree 1, does <i>not</i>
/// visit them at the same parameters: a rational quadratic walks a circular arc by a projective
/// function of the angle rather than by the angle. <see cref="SurfaceConversion"/>'s remarks set
/// this out at length for surfaces and every word of it applies here.
/// </para>
/// </remarks>
public sealed class NurbsConversion
{
    /// <summary>Creates a conversion result.</summary>
    /// <param name="curve">The NURBS curve.</param>
    /// <param name="isExact">
    /// Whether <paramref name="curve"/> traces exactly the same points as the original.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="curve"/> is <see langword="null"/>.</exception>
    public NurbsConversion(NurbsCurve curve, bool isExact)
    {
        ArgumentNullException.ThrowIfNull(curve);

        Curve = curve;
        IsExact = isExact;
    }

    /// <summary>The NURBS curve.</summary>
    public NurbsCurve Curve { get; }

    /// <summary>
    /// Whether <see cref="Curve"/> is the same set of points as the curve it came from, rather than
    /// an approximation to it.
    /// </summary>
    /// <remarks>
    /// <b>When this is <see langword="false"/> the accuracy is the caller's to measure</b>, with
    /// <see cref="Curve.DistanceTo(in Point3d)"/> against the original at as many points as the
    /// caller cares about. The tolerance passed to
    /// <see cref="Spark.Geometry.Curve.ToNurbsCurve(in Tolerance)"/> drives how finely the original
    /// is sampled; it is not a proved bound on the deviation, and saying it was would be the same
    /// kind of claim this type exists to stop being made silently.
    /// </remarks>
    public bool IsExact { get; }

    /// <summary>A readable description, for diagnostics.</summary>
    /// <returns>Whether the conversion was exact, and the curve's degree and control-point count.</returns>
    public override string ToString() =>
        $"NurbsConversion({(IsExact ? "exact" : "approximate")}, degree {Curve.Degree}, "
        + $"{Curve.Knots.ControlPointCount} control points)";
}

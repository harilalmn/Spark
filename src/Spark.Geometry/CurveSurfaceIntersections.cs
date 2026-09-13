using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Where a curve meets a surface at a point: the point, the parameter on the curve, and the
/// surface parameters (<c>E2-T70</c>, step B).
/// </summary>
/// <param name="Point">Where they meet.</param>
/// <param name="Parameter">The parameter on the curve.</param>
/// <param name="Uv">The surface parameters.</param>
/// <remarks>
/// <b>Both parameters are here on purpose.</b> A caller trimming the curve needs
/// <paramref name="Parameter"/> and a caller splitting or marking the surface needs
/// <paramref name="Uv"/>; a result carrying only the point makes the member half useless, because
/// recovering either parameter afterwards is a second intersection problem.
/// </remarks>
public readonly record struct CurveSurfaceIntersectionPoint(Point3d Point, double Parameter, UV Uv);

/// <summary>
/// Everything a curve and a surface have in common: the points where the curve crosses or touches
/// the surface (<c>E2-T70</c>, step B).
/// </summary>
/// <remarks>
/// <para>
/// A class rather than a record, for the reason <see cref="CurveIntersections"/> is one: what it
/// publishes is the list, and a record would add a copy constructor and an equality contract nobody
/// asked for.
/// </para>
/// <para>
/// <b>There is no overlap list here, and the absence is deliberate.</b> Two curves can run together
/// along a shared stretch, which is why <see cref="CurveIntersections"/> reports overlaps; a curve
/// lying <i>in</i> a surface is the same situation one dimension up, and
/// <see cref="Curve.IntersectWith(Surface, in Tolerance)"/> <b>refuses</b> it rather than returning a
/// list of points that is really a curve. <see cref="LiesOnSurface"/> says that happened.
/// </para>
/// </remarks>
public sealed class CurveSurfaceIntersections
{
    /// <summary>Creates the result.</summary>
    /// <param name="points">The points, in order along the curve.</param>
    /// <param name="liesOnSurface">Whether the curve was found to lie in the surface.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    public CurveSurfaceIntersections(IReadOnlyList<CurveSurfaceIntersectionPoint> points, bool liesOnSurface = false)
    {
        ArgumentNullException.ThrowIfNull(points);

        Points = points;
        LiesOnSurface = liesOnSurface;
    }

    /// <summary>Nothing in common.</summary>
    public static CurveSurfaceIntersections None { get; } = new([]);

    /// <summary>The points, in order along the curve.</summary>
    public IReadOnlyList<CurveSurfaceIntersectionPoint> Points { get; }

    /// <summary>
    /// Whether the curve lies in the surface rather than crossing it, in which case
    /// <see cref="Points"/> is empty.
    /// </summary>
    /// <remarks>
    /// The whole of the curve within the surface's domain stayed within the tolerance of the
    /// surface at every sample. That is a shared <i>curve</i> and not a set of points, and
    /// reporting the samples as intersections would be reporting the sampling rather than the
    /// geometry.
    /// </remarks>
    public bool LiesOnSurface { get; }

    /// <summary>Whether the curve and the surface have nothing in common.</summary>
    public bool IsEmpty => Points.Count == 0 && !LiesOnSurface;
}

using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Curve/surface intersection for any curve and any surface: bracket by sampling a signed distance,
/// then refine by Newton on the three-equation system (<c>E2-T70</c>, step B).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is managed rather than behind the kernel seam.</b>
/// <see cref="Spark.Geometry.Curve.IntersectWith(Curve, in Tolerance)"/> is exact for curve/curve
/// (<c>E2-T11</c>) and solid/solid is the provider's, but curve/surface had no member anywhere —
/// which <c>E2-T46</c>'s assessment found and <c>DYNAMO-COVERAGE</c> §3.8 records. Surface/surface is
/// ADR-0002's research-grade problem and stays behind the seam; this one is not, and cutting a curve
/// with a surface is a thing AEC graphs do constantly.
/// </para>
/// <para>
/// <b>Bracket by a signed distance.</b> A surface has no signed distance of its own, so one is built
/// at each sample: <see cref="Surface.ClosestPoint(in Point3d, out double, out double)"/> gives the
/// nearest point and its parameters, and the displacement from it to the curve point, dotted with
/// <see cref="Surface.NormalAt(double, double)"/> there, is a signed distance that changes sign
/// exactly where the curve passes through. It is continuous away from the surface's medial axis,
/// which is where a *nearest* point jumps — so a bracket is a hypothesis, and the refinement below is
/// what confirms it.
/// </para>
/// <para>
/// <b>Refine by Newton on three equations.</b> <c>F(t, u, v) = C(t) − S(u, v) = 0</c> is three
/// equations in three unknowns, and its Jacobian columns are <c>C′(t)</c>, <c>−S_u</c> and
/// <c>−S_v</c>. That converges quadratically and lands on the curve *and* the surface at once, which
/// is why the result can carry both parameters honestly. <b>A singular Jacobian is not a failure, it
/// is a tangency</b> — the curve's direction has fallen into the surface's tangent plane — and the
/// fall-back is bisection on the bracketed signed distance, which cannot converge quadratically but
/// cannot diverge either.
/// </para>
/// <para>
/// <b>What it cannot do, stated rather than hidden.</b> A tangency that the sampling steps over is
/// missed: the signed distance touches zero without changing sign, and no bracket forms. Raising
/// <see cref="Samples"/> makes that rarer and never impossible, which is the same bargain
/// <see cref="GeneralCurveIntersection"/> makes and states. A curve lying *in* the surface is not a
/// set of points at all and is reported as
/// <see cref="CurveSurfaceIntersections.LiesOnSurface"/> rather than as the samples that happened to
/// be taken.
/// </para>
/// </remarks>
internal static class CurveSurfaceIntersection
{
    /// <summary>
    /// Samples along the curve. The same count <see cref="GeneralCurveIntersection"/> uses, and for
    /// the same reason: enough that a chord's box holds its arc for the curves Spark has.
    /// </summary>
    internal const int Samples = 128;

    private const int MaximumIterations = 40;

    private const int BisectionIterations = 60;

    /// <summary>Intersects a curve with a surface.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="surface">The surface.</param>
    /// <param name="tolerance">How close counts as meeting.</param>
    /// <returns>The points, in order along the curve.</returns>
    internal static CurveSurfaceIntersections Intersect(Curve curve, Surface surface, in Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(surface);

        double tol = tolerance.Linear;

        // A box that cannot reach the other box cannot meet it. The surface box is virtual on the
        // base and exact on the analytic surfaces, so this is cheap and never wrong in the unsafe
        // direction.
        if (!curve.BoundingBox.Inflated(tol).Intersects(surface.BoundingBox.Inflated(tol)))
        {
            return CurveSurfaceIntersections.None;
        }

        Interval domain = curve.Domain;
        double[] parameters = new double[Samples + 1];
        double[] signed = new double[Samples + 1];
        Point3d[] points = new Point3d[Samples + 1];
        bool everOff = false;

        for (int i = 0; i <= Samples; i++)
        {
            double t = domain.Min + ((domain.Max - domain.Min) * i / Samples);
            parameters[i] = t;
            points[i] = curve.PointAt(t);
            signed[i] = SignedDistance(surface, points[i]);

            if (Math.Abs(signed[i]) > tol)
            {
                everOff = true;
            }
        }

        // Every sample sat on the surface. That is a shared curve rather than a list of points, and
        // returning the samples would be reporting the sampling.
        if (!everOff)
        {
            return new CurveSurfaceIntersections([], liesOnSurface: true);
        }

        List<CurveSurfaceIntersectionPoint> found = [];

        for (int i = 0; i <= Samples; i++)
        {
            // A sample already on the surface is its own bracket, and refining from it costs
            // nothing. This is also what catches a crossing exactly at an end of the domain.
            if (Math.Abs(signed[i]) <= tol)
            {
                Add(found, curve, surface, parameters[i], tol);
                continue;
            }

            if (i == Samples)
            {
                break;
            }

            if (Math.Abs(signed[i + 1]) <= tol || Math.Sign(signed[i]) == Math.Sign(signed[i + 1]))
            {
                continue;
            }

            double bracketed = Bisect(curve, surface, parameters[i], parameters[i + 1], signed[i], tol);
            Add(found, curve, surface, bracketed, tol);
        }

        found.Sort(static (a, b) => a.Parameter.CompareTo(b.Parameter));

        return found.Count == 0 ? CurveSurfaceIntersections.None : new CurveSurfaceIntersections(found);
    }

    /// <summary>
    /// The displacement from the nearest point on the surface to the curve point, measured along the
    /// surface normal there.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="point">The point on the curve.</param>
    /// <returns>Positive on the normal's side, negative on the other, zero on the surface.</returns>
    /// <remarks>
    /// The sign is what makes a bracket possible. Its magnitude is the true distance only when the
    /// nearest point really is nearest — at a pole or an apex a degenerate parameterisation can make
    /// the search stop early (<c>N137</c>) — but the <i>sign</i> survives that, and the refinement
    /// does not trust the magnitude.
    /// </remarks>
    private static double SignedDistance(Surface surface, in Point3d point)
    {
        Point3d nearest = surface.ClosestPoint(point, out double u, out double v);
        Vector3d normal = surface.NormalAt(u, v);
        Vector3d away = point - nearest;

        return normal.IsZero() ? away.Length : away.Dot(normal);
    }

    /// <summary>Bisects the bracketed signed distance down to the tolerance.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="surface">The surface.</param>
    /// <param name="low">The parameter at the low end of the bracket.</param>
    /// <param name="high">The parameter at the high end.</param>
    /// <param name="signedAtLow">The signed distance at <paramref name="low"/>.</param>
    /// <param name="tolerance">The linear tolerance.</param>
    /// <returns>A parameter whose point is on the surface to within the tolerance, or the best found.</returns>
    private static double Bisect(Curve curve, Surface surface, double low, double high, double signedAtLow, double tolerance)
    {
        int lowSign = Math.Sign(signedAtLow);

        for (int i = 0; i < BisectionIterations; i++)
        {
            double mid = 0.5 * (low + high);
            double value = SignedDistance(surface, curve.PointAt(mid));

            if (Math.Abs(value) <= tolerance)
            {
                return mid;
            }

            if (Math.Sign(value) == lowSign)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>
    /// Refines a bracketed parameter by Newton on <c>C(t) − S(u, v) = 0</c> and adds it, unless it
    /// leaves a domain or duplicates a point already found.
    /// </summary>
    /// <param name="found">The points so far.</param>
    /// <param name="curve">The curve.</param>
    /// <param name="surface">The surface.</param>
    /// <param name="seed">The bracketed curve parameter.</param>
    /// <param name="tolerance">The linear tolerance.</param>
    /// <remarks>
    /// <b>The tolerance decides what counts as meeting, not how accurately the answer is reported</b>,
    /// and the two are different questions. Stopping the iteration as soon as the residual is inside
    /// the tolerance hands back a point that is only <i>as good as it had to be</i> - a millionth of a
    /// unit out on the kernel's default - and that showed up immediately as a general path that could
    /// not match a closed form to better than the tolerance it was given. Newton converges
    /// quadratically, so running it until the residual stops improving costs two or three more
    /// evaluations and lands near machine precision. <b>The best point seen is kept</b> rather than
    /// wherever the loop happened to stop, for the reason
    /// <see cref="Surface.ClosestPoint(in Point3d, out double, out double)"/> keeps its best
    /// (<c>N137</c>): Newton is not monotone, and returning the last step makes the answer depend on
    /// where the budget ran out.
    /// </remarks>
    private static void Add(
        List<CurveSurfaceIntersectionPoint> found,
        Curve curve,
        Surface surface,
        double seed,
        double tolerance)
    {
        double t = seed;
        Point3d onCurve = curve.PointAt(t);
        surface.ClosestPoint(onCurve, out double u, out double v);

        double bestResidual = double.MaxValue;
        double bestT = t;
        double bestU = u;
        double bestV = v;
        int worse = 0;

        for (int i = 0; i < MaximumIterations; i++)
        {
            Point3d c = curve.PointAt(t);
            Point3d s = surface.PointAt(u, v);
            Vector3d residual = c - s;
            double length = residual.Length;

            if (length < bestResidual)
            {
                bestResidual = length;
                bestT = t;
                bestU = u;
                bestV = v;
                worse = 0;
            }
            else if (++worse >= 2)
            {
                // Converged as far as arithmetic allows, and now wandering in the last bits.
                break;
            }

            Vector3d dc = curve.DerivativeAt(t);
            surface.DerivativeAt(u, v, out Vector3d su, out Vector3d sv);

            if (!Solve(dc, -su, -sv, -residual, out double dt, out double du, out double dv))
            {
                // Singular: the curve's direction lies in the surface's tangent plane, which is what
                // a tangency looks like. The best point so far is the answer.
                break;
            }

            t = curve.Domain.Clamp(t + dt);
            u = surface.DomainU.Clamp(u + du);
            v = surface.DomainV.Clamp(v + dv);
        }

        t = bestT;
        u = bestU;
        v = bestV;

        Point3d point = curve.PointAt(t);

        // The refinement may walk out of the surface's patch, and a clamped parameter is then not a
        // solution but the nearest edge of one. Check the answer rather than the iteration.
        if (point.DistanceTo(surface.PointAt(u, v)) > tolerance)
        {
            return;
        }

        foreach (CurveSurfaceIntersectionPoint already in found)
        {
            if (already.Point.DistanceTo(point) <= tolerance)
            {
                return;
            }
        }

        found.Add(new CurveSurfaceIntersectionPoint(point, t, new UV(u, v)));
    }

    /// <summary>Solves the 3x3 system whose columns are the three vectors, by Cramer's rule.</summary>
    /// <param name="a">The first column.</param>
    /// <param name="b">The second column.</param>
    /// <param name="c">The third column.</param>
    /// <param name="rhs">The right-hand side.</param>
    /// <param name="x">The first unknown.</param>
    /// <param name="y">The second unknown.</param>
    /// <param name="z">The third unknown.</param>
    /// <returns><see langword="false"/> when the matrix is singular, and nothing is written.</returns>
    /// <remarks>
    /// Cramer's rule rather than an elimination, because a 3x3 determinant is four triple products
    /// and no pivoting strategy to get wrong. The singularity test is <b>relative</b> to the columns'
    /// magnitudes: an absolute one calls a small well-conditioned system singular, which on a surface
    /// measured in millimetres is every system.
    /// </remarks>
    private static bool Solve(
        in Vector3d a,
        in Vector3d b,
        in Vector3d c,
        in Vector3d rhs,
        out double x,
        out double y,
        out double z)
    {
        x = y = z = 0.0;

        double determinant = a.Dot(b.Cross(c));
        double scale = a.Length * b.Length * c.Length;

        if (scale <= 0.0 || Math.Abs(determinant) <= 1e-12 * scale)
        {
            return false;
        }

        x = rhs.Dot(b.Cross(c)) / determinant;
        y = a.Dot(rhs.Cross(c)) / determinant;
        z = a.Dot(b.Cross(rhs)) / determinant;

        return true;
    }
}

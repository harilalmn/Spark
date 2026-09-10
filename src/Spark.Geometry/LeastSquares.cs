using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Fitting geometry through points that do not quite lie on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists as its own type because three callers want the same arithmetic.</b>
/// <see cref="Plane.FromBestFit(IReadOnlyList{Point3d})"/> is the first;
/// <c>Circle.FromBestFit</c> and <c>Line.FromBestFit</c> are named as wanting it in
/// <c>DYNAMO-COVERAGE §3.1</c>, and a circle fitted through points has to know the plane they lie
/// in before it can fit anything. Written once, in one place, so the three cannot disagree about
/// a degenerate input — which is the only place any of them is interesting.
/// </para>
/// <para>
/// <b>It is internal on purpose.</b> A public fitting API would have to decide what it offers
/// beyond a normal — residuals, a condition number, a weighting — and none of those questions has
/// a caller yet. The public surface is the factories that use it.
/// </para>
/// </remarks>
internal static class LeastSquares
{
    /// <summary>
    /// How far the smallest principal minor of the covariance matrix has to sit above the noise
    /// before a set of points is believed to span a plane.
    /// </summary>
    /// <remarks>
    /// <b>It is relative, and it has to be</b> ([N143](../../docs/NOTES.md)). The minors below are
    /// sums of fourth powers of coordinates, so an absolute cut would mean something different for
    /// a set spanning millimetres and one spanning kilometres. Exactly collinear points produce minors around
    /// <c>1e-16</c> of the trace squared purely from rounding; <c>1e-12</c> leaves four orders of
    /// magnitude of margin and still rejects a line.
    /// </remarks>
    private const double DegeneracyRatio = 1e-12;

    /// <summary>
    /// Fits the plane that minimises the sum of squared distances from a set of points.
    /// </summary>
    /// <param name="points">The points to fit through.</param>
    /// <param name="centroid">The points' centroid, which is the fitted plane's origin.</param>
    /// <param name="normal">The fitted unit normal, oriented as described below.</param>
    /// <returns>
    /// <see langword="true"/> when the points span a plane. <see langword="false"/> when there are
    /// fewer than three, when any is not finite, or when they are collinear or coincident and so
    /// define no unique plane.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The method is the covariance matrix and its smallest eigenvector, computed in closed
    /// form.</b> Subtract the centroid, accumulate the symmetric 3×3 covariance, and take the
    /// direction along which the points vary least. That direction is the null vector of the
    /// covariance when the fit is exact, and it is a row of the matrix's adjugate — the three
    /// candidate rows below are exactly those, and the largest of the three principal minors picks
    /// the numerically best-conditioned one. **No iterative eigensolver is involved**, so there is
    /// no tolerance, no iteration cap and no convergence to argue about; there is only the
    /// degeneracy test.
    /// </para>
    /// <para>
    /// <b>The normal's sign follows the winding of the points where that means anything.</b> The
    /// eigenvector is a direction, not an orientation — it is equally valid negated — so the sign
    /// is settled afterwards against Newell's normal for the points taken in the order given.
    /// A ring of points therefore fits a plane whose normal obeys the right-hand rule for the
    /// ring, matching what <see cref="Plane.FromThreePoints"/> promises for three. When Newell's
    /// normal is degenerate — a self-crossing order, or points laid out symmetrically about the
    /// centroid — the sign is left as the adjugate produced it, which is deterministic for a given
    /// input but not otherwise meaningful.
    /// </para>
    /// </remarks>
    internal static bool TryFitPlane(
        IReadOnlyList<Point3d> points,
        out Point3d centroid,
        out Vector3d normal)
    {
        centroid = Point3d.Origin;
        normal = Vector3d.Zero;

        if (points is null || points.Count < 3)
        {
            return false;
        }

        double sx = 0.0;
        double sy = 0.0;
        double sz = 0.0;

        foreach (Point3d point in points)
        {
            if (!point.IsValid)
            {
                return false;
            }

            sx += point.X;
            sy += point.Y;
            sz += point.Z;
        }

        double count = points.Count;
        centroid = new Point3d(sx / count, sy / count, sz / count);

        double xx = 0.0;
        double xy = 0.0;
        double xz = 0.0;
        double yy = 0.0;
        double yz = 0.0;
        double zz = 0.0;

        foreach (Point3d point in points)
        {
            double dx = point.X - centroid.X;
            double dy = point.Y - centroid.Y;
            double dz = point.Z - centroid.Z;

            xx += dx * dx;
            xy += dx * dy;
            xz += dx * dz;
            yy += dy * dy;
            yz += dy * dz;
            zz += dz * dz;
        }

        double trace = xx + yy + zz;

        if (!double.IsFinite(trace) || trace <= 0.0)
        {
            // Every point is the centroid. Coincident points span nothing.
            return false;
        }

        double detX = (yy * zz) - (yz * yz);
        double detY = (xx * zz) - (xz * xz);
        double detZ = (xx * yy) - (xy * xy);

        double best = Math.Max(detX, Math.Max(detY, detZ));

        if (best <= DegeneracyRatio * trace * trace)
        {
            // The covariance has rank one at most: the points lie on a line, or on top of one
            // another. Either way there is no unique plane through them.
            return false;
        }

        Vector3d candidate = best == detX
            ? new Vector3d(detX, (xz * yz) - (xy * zz), (xy * yz) - (xz * yy))
            : best == detY
            ? new Vector3d((xz * yz) - (xy * zz), detY, (xy * xz) - (yz * xx))
            : new Vector3d((xy * yz) - (xz * yy), (xy * xz) - (yz * xx), detZ);

        if (!candidate.TryNormalise(out normal))
        {
            return false;
        }

        if (Newell(points, centroid).Dot(normal) < 0.0)
        {
            normal = -normal;
        }

        return true;
    }

    /// <summary>
    /// Newell's normal for the points taken as a closed ring, about their centroid.
    /// </summary>
    /// <param name="points">The points, in order.</param>
    /// <param name="centroid">Their centroid, subtracted first so the sum does not lose precision far from the origin.</param>
    /// <returns>
    /// A vector along the ring's normal by the right-hand rule, whose length is twice the ring's
    /// projected area. It is the zero vector when the order carries no winding, which is why the
    /// caller treats a zero as <i>no opinion</i> rather than as an answer.
    /// </returns>
    private static Vector3d Newell(IReadOnlyList<Point3d> points, in Point3d centroid)
    {
        double nx = 0.0;
        double ny = 0.0;
        double nz = 0.0;

        for (int i = 0; i < points.Count; i++)
        {
            Point3d current = points[i];
            Point3d next = points[(i + 1) % points.Count];

            double cx = current.X - centroid.X;
            double cy = current.Y - centroid.Y;
            double cz = current.Z - centroid.Z;
            double nx2 = next.X - centroid.X;
            double ny2 = next.Y - centroid.Y;
            double nz2 = next.Z - centroid.Z;

            nx += (cy * nz2) - (cz * ny2);
            ny += (cz * nx2) - (cx * nz2);
            nz += (cx * ny2) - (cy * nx2);
        }

        return new Vector3d(nx, ny, nz);
    }
}

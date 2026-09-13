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

        // Coincident points span nothing, which is what a zero trace means and why Covariance
        // reports it rather than leaving each caller to test for it.
        if (!Covariance(points, out centroid, out double[] c))
        {
            return false;
        }

        double xx = c[0];
        double xy = c[1];
        double xz = c[2];
        double yy = c[3];
        double yz = c[4];
        double zz = c[5];
        double trace = xx + yy + zz;

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
    /// Fits the line that minimises the sum of squared distances from a set of points.
    /// </summary>
    /// <param name="points">The points to fit through.</param>
    /// <param name="centroid">The points' centroid, which the fitted line passes through.</param>
    /// <param name="direction">The fitted unit direction, oriented from the first point towards the last.</param>
    /// <returns>
    /// <see langword="true"/> when the points have one principal direction.
    /// <see langword="false"/> when there are fewer than two, when any is not finite, when they are
    /// coincident, or when <b>no direction is preferred</b> — see the remarks.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This is <see cref="TryFitPlane"/> read the other way round.</b> A plane fit wants the
    /// direction the points vary <i>least</i> along; a line fit wants the direction they vary
    /// <i>most</i> along. Both are eigenvectors of the same covariance matrix, so this file gains a
    /// method rather than the codebase gaining a second fitting routine.
    /// </para>
    /// <para>
    /// <b>The smallest eigenvector had a closed form and the largest needs the eigenvalue
    /// first.</b> The plane fit can take a row of the covariance's adjugate directly, because the
    /// smallest eigenvector is the adjugate's range when the fit is exact. There is no such trick
    /// for the largest, so the largest eigenvalue comes from the closed-form solution of the
    /// symmetric 3×3 characteristic cubic — the trigonometric one, which is exact arithmetic and
    /// not an iteration — and the eigenvector is then a column of <c>adj(C - λI)</c>. Still no
    /// convergence to argue about.
    /// </para>
    /// <para>
    /// <b>Points on a circle have no best-fit line, and this refuses them.</b> When the two largest
    /// eigenvalues are equal, every direction in that plane fits equally well and picking one would
    /// be inventing an answer — the exact mirror of the plane fit refusing collinear points, and
    /// the reason both refusals live in one file.
    /// </para>
    /// </remarks>
    internal static bool TryFitLine(
        IReadOnlyList<Point3d> points,
        out Point3d centroid,
        out Vector3d direction)
    {
        centroid = Point3d.Origin;
        direction = Vector3d.Zero;

        if (points is null || points.Count < 2)
        {
            return false;
        }

        if (!Covariance(points, out centroid, out double[] c))
        {
            return false;
        }

        double trace = c[0] + c[3] + c[5];

        if (!LargestEigenvalue(c, trace, out double largest, out double second))
        {
            return false;
        }

        // No preferred direction: the two largest eigenvalues agree, so the points are spread
        // isotropically in some plane — a ring, a cloud — and every line through the centroid in
        // that plane fits as well as every other.
        if (largest - second <= DegeneracyRatio * trace)
        {
            return false;
        }

        if (!Eigenvector(c, largest, out direction))
        {
            return false;
        }

        // The eigenvector is a direction and not an orientation, so the sign is settled the way a
        // reader would expect: the fitted line runs from the first point towards the last.
        if ((points[^1] - points[0]).Dot(direction) < 0.0)
        {
            direction = -direction;
        }

        return true;
    }

    /// <summary>
    /// Fits the circle that best passes through a set of coplanar 2D points.
    /// </summary>
    /// <param name="points">The points, in the plane they were projected into.</param>
    /// <param name="centre">The fitted centre.</param>
    /// <param name="radius">The fitted radius.</param>
    /// <returns>
    /// <see langword="true"/> when a circle was fitted; <see langword="false"/> when there are
    /// fewer than three points or they are collinear.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Two fits, because one of them is exact and the other is unbiased, and they are not the
    /// same one.</b> The seed is the <i>algebraic</i> fit — minimise
    /// <c>Σ(|p − c|² − r²)²</c>, which is linear in <c>(cx, cy, cx² + cy² − r²)</c> and therefore a
    /// 3×3 solve with no iteration. It is <b>exact</b> for points that genuinely lie on a circle,
    /// whatever arc they cover. What it is not is unbiased: under noise, on a <i>short</i> arc, it
    /// systematically <b>under-estimates the radius</b>, because squaring the distance weights the
    /// points furthest from the centre hardest and a short arc has no points on the other side to
    /// balance them.
    /// </para>
    /// <para>
    /// <b>So the algebraic answer is refined by Gauss–Newton on the <i>geometric</i> residual</b> —
    /// <c>Σ(|p − c| − r)²</c>, the thing anybody fitting a circle actually means — which has no
    /// closed form and converges in a handful of steps from a seed this good. On exact points the
    /// refinement moves nothing, because the seed is already exact; on a noisy short arc it is the
    /// difference between a right answer and a plausible one.
    /// </para>
    /// <para>
    /// <b>The test that separates them is a short arc with noise on it.</b> A full circle hides the
    /// bias — the weighting cancels by symmetry — and exact points hide it because both fits are
    /// then correct. Neither of those cases can tell a working implementation from a biased one.
    /// </para>
    /// </remarks>
    internal static bool TryFitCircle(
        IReadOnlyList<Point2d> points,
        out Point2d centre,
        out double radius)
    {
        centre = Point2d.Origin;
        radius = 0.0;

        if (points is null || points.Count < 3)
        {
            return false;
        }

        double sx = 0.0;
        double sy = 0.0;

        foreach (Point2d point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                return false;
            }

            sx += point.X;
            sy += point.Y;
        }

        // Centred, because the algebraic system is badly conditioned far from the origin: the
        // normal equations accumulate fourth powers of the coordinates, and a circle of radius one
        // a million units away loses every digit that distinguishes its points.
        double count = points.Count;
        double ox = sx / count;
        double oy = sy / count;

        double sxx = 0.0;
        double sxy = 0.0;
        double syy = 0.0;
        double sxz = 0.0;
        double syz = 0.0;

        foreach (Point2d point in points)
        {
            double x = point.X - ox;
            double y = point.Y - oy;
            double z = (x * x) + (y * y);

            sxx += x * x;
            sxy += x * y;
            syy += y * y;
            sxz += x * z;
            syz += y * z;
        }

        double determinant = (sxx * syy) - (sxy * sxy);
        double scale = sxx + syy;

        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= DegeneracyRatio * scale * scale)
        {
            // Collinear, or coincident. No circle passes through them.
            return false;
        }

        double cx = ((sxz * syy) - (syz * sxy)) / (2.0 * determinant);
        double cy = ((syz * sxx) - (sxz * sxy)) / (2.0 * determinant);

        Refine(points, ox, oy, ref cx, ref cy, out radius);

        centre = new Point2d(cx + ox, cy + oy);

        return double.IsFinite(radius) && radius > 0.0;
    }

    /// <summary>
    /// Gauss–Newton on the geometric residual, from the algebraic fit's seed.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="ox">The x offset the points were centred by.</param>
    /// <param name="oy">The y offset the points were centred by.</param>
    /// <param name="cx">The centre's x, refined in place.</param>
    /// <param name="cy">The centre's y, refined in place.</param>
    /// <param name="radius">The radius that goes with the refined centre.</param>
    /// <remarks>
    /// <b>The radius is not a third unknown.</b> For any centre the best radius is the mean
    /// distance to it, so eliminating it leaves a two-dimensional problem whose normal equations
    /// are a 2×2 solve — which is both faster and better conditioned than carrying three.
    /// </remarks>
    private static void Refine(
        IReadOnlyList<Point2d> points, double ox, double oy, ref double cx, ref double cy, out double radius)
    {
        radius = 0.0;

        for (int iteration = 0; iteration < 32; iteration++)
        {
            double sumDistance = 0.0;
            double sumUx = 0.0;
            double sumUy = 0.0;

            foreach (Point2d point in points)
            {
                double dx = (point.X - ox) - cx;
                double dy = (point.Y - oy) - cy;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));

                if (distance <= 0.0)
                {
                    // A point exactly at the centre has no radial direction, so the step it would
                    // contribute is undefined. Leaving the seed alone is the honest answer.
                    return;
                }

                sumDistance += distance;
                sumUx += dx / distance;
                sumUy += dy / distance;
            }

            double count = points.Count;
            radius = sumDistance / count;

            double jxx = 0.0;
            double jxy = 0.0;
            double jyy = 0.0;
            double gx = 0.0;
            double gy = 0.0;

            foreach (Point2d point in points)
            {
                double dx = (point.X - ox) - cx;
                double dy = (point.Y - oy) - cy;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                double ux = dx / distance;
                double uy = dy / distance;

                // d(residual)/d(centre), with the radius already eliminated as the mean distance:
                // the mean unit vector is what the elimination leaves behind.
                double ax = -ux + (sumUx / count);
                double ay = -uy + (sumUy / count);

                jxx += ax * ax;
                jxy += ax * ay;
                jyy += ay * ay;

                double residual = distance - radius;
                gx += ax * residual;
                gy += ay * residual;
            }

            double determinant = (jxx * jyy) - (jxy * jxy);

            if (!double.IsFinite(determinant) || determinant == 0.0)
            {
                return;
            }

            double stepX = -((gx * jyy) - (gy * jxy)) / determinant;
            double stepY = -((gy * jxx) - (gx * jxy)) / determinant;

            cx += stepX;
            cy += stepY;

            // Relative to the radius rather than absolute: a circle a nanometre across and one a
            // kilometre across must converge by the same standard, which is N143's rule again.
            if (Math.Abs(stepX) + Math.Abs(stepY) <= radius * 1e-15)
            {
                break;
            }
        }

        double total = 0.0;

        foreach (Point2d point in points)
        {
            double dx = (point.X - ox) - cx;
            double dy = (point.Y - oy) - cy;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        radius = total / points.Count;
    }

    /// <summary>The centroid and the upper triangle of the covariance matrix.</summary>
    /// <param name="points">The points.</param>
    /// <param name="centroid">Their centroid.</param>
    /// <param name="covariance">xx, xy, xz, yy, yz, zz.</param>
    /// <returns><see langword="false"/> when a point is not finite or they are coincident.</returns>
    private static bool Covariance(
        IReadOnlyList<Point3d> points, out Point3d centroid, out double[] covariance)
    {
        centroid = Point3d.Origin;
        covariance = new double[6];

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

        foreach (Point3d point in points)
        {
            double dx = point.X - centroid.X;
            double dy = point.Y - centroid.Y;
            double dz = point.Z - centroid.Z;

            covariance[0] += dx * dx;
            covariance[1] += dx * dy;
            covariance[2] += dx * dz;
            covariance[3] += dy * dy;
            covariance[4] += dy * dz;
            covariance[5] += dz * dz;
        }

        double trace = covariance[0] + covariance[3] + covariance[5];

        return double.IsFinite(trace) && trace > 0.0;
    }

    /// <summary>
    /// The two largest eigenvalues of a symmetric 3×3, in closed form.
    /// </summary>
    /// <param name="c">The upper triangle: xx, xy, xz, yy, yz, zz.</param>
    /// <param name="trace">Its trace, already computed by the caller.</param>
    /// <param name="largest">The largest eigenvalue.</param>
    /// <param name="second">The second largest.</param>
    /// <returns><see langword="false"/> when the result is not finite.</returns>
    /// <remarks>
    /// <b>The trigonometric solution of the characteristic cubic</b>, which is exact arithmetic
    /// rather than an iteration — the same reason <see cref="TryFitPlane"/> uses the adjugate
    /// instead of an eigensolver. A diagonal matrix is handled first, because the general formula
    /// divides by a spread that is zero there.
    /// </remarks>
    private static bool LargestEigenvalue(double[] c, double trace, out double largest, out double second)
    {
        double offDiagonal = (c[1] * c[1]) + (c[2] * c[2]) + (c[4] * c[4]);

        if (offDiagonal <= 0.0)
        {
            double[] diagonal = [c[0], c[3], c[5]];
            Array.Sort(diagonal);
            largest = diagonal[2];
            second = diagonal[1];

            return double.IsFinite(largest);
        }

        double mean = trace / 3.0;
        double spread = (((c[0] - mean) * (c[0] - mean))
            + ((c[3] - mean) * (c[3] - mean))
            + ((c[5] - mean) * (c[5] - mean))
            + (2.0 * offDiagonal)) / 6.0;

        double p = Math.Sqrt(spread);

        if (!double.IsFinite(p) || p <= 0.0)
        {
            largest = mean;
            second = mean;

            return double.IsFinite(mean);
        }

        double b00 = (c[0] - mean) / p;
        double b01 = c[1] / p;
        double b02 = c[2] / p;
        double b11 = (c[3] - mean) / p;
        double b12 = c[4] / p;
        double b22 = (c[5] - mean) / p;

        double determinant = (b00 * ((b11 * b22) - (b12 * b12)))
            - (b01 * ((b01 * b22) - (b12 * b02)))
            + (b02 * ((b01 * b12) - (b11 * b02)));

        double angle = Math.Acos(Math.Clamp(determinant / 2.0, -1.0, 1.0)) / 3.0;

        largest = mean + (2.0 * p * Math.Cos(angle));
        double smallest = mean + (2.0 * p * Math.Cos(angle + (2.0 * Math.PI / 3.0)));
        second = trace - largest - smallest;

        return double.IsFinite(largest) && double.IsFinite(second);
    }

    /// <summary>A unit eigenvector of a symmetric 3×3 for a known eigenvalue.</summary>
    /// <param name="c">The upper triangle: xx, xy, xz, yy, yz, zz.</param>
    /// <param name="eigenvalue">The eigenvalue.</param>
    /// <param name="eigenvector">The unit eigenvector.</param>
    /// <returns><see langword="false"/> when no column of the adjugate has any length.</returns>
    /// <remarks>
    /// <b>A column of <c>adj(C - λI)</c>, and the longest one is taken</b> for the same reason
    /// <see cref="TryFitPlane"/> takes the largest principal minor: all three columns are valid
    /// eigenvectors when the arithmetic is exact, and the longest is the best conditioned when it
    /// is not.
    /// </remarks>
    private static bool Eigenvector(double[] c, double eigenvalue, out Vector3d eigenvector)
    {
        double a00 = c[0] - eigenvalue;
        double a11 = c[3] - eigenvalue;
        double a22 = c[5] - eigenvalue;

        Vector3d first = new(
            (a11 * a22) - (c[4] * c[4]),
            (c[2] * c[4]) - (c[1] * a22),
            (c[1] * c[4]) - (c[2] * a11));

        Vector3d secondColumn = new(
            (c[2] * c[4]) - (c[1] * a22),
            (a00 * a22) - (c[2] * c[2]),
            (c[1] * c[2]) - (a00 * c[4]));

        Vector3d third = new(
            (c[1] * c[4]) - (c[2] * a11),
            (c[1] * c[2]) - (a00 * c[4]),
            (a00 * a11) - (c[1] * c[1]));

        Vector3d best = first;

        if (secondColumn.LengthSquared > best.LengthSquared)
        {
            best = secondColumn;
        }

        if (third.LengthSquared > best.LengthSquared)
        {
            best = third;
        }

        return best.TryNormalise(out eigenvector);
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

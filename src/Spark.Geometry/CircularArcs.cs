using System;

namespace Spark.Geometry;

/// <summary>
/// The arithmetic <see cref="Circle"/>, <see cref="Arc"/> and <see cref="EllipseCurve"/> share:
/// evaluating a point on an elliptical frame, bounding an angular sweep exactly, and deciding
/// whether a transform keeps a circle a circle.
/// </summary>
/// <remarks>
/// It is internal and static because it is arithmetic rather than API. Everything here works on an
/// elliptical frame — an origin with two conjugate radius vectors — and a circle is the case where
/// the two radii are equal, which is why one file serves three types.
/// </remarks>
internal static class CircularArcs
{
    /// <summary>The point at an angle on an elliptical frame.</summary>
    /// <param name="plane">The frame.</param>
    /// <param name="xRadius">The radius along the frame's x axis.</param>
    /// <param name="yRadius">The radius along the frame's y axis.</param>
    /// <param name="angle">The angle in radians, measured from the x axis towards the y axis.</param>
    /// <returns>The point.</returns>
    internal static Point3d PointAt(in Plane plane, double xRadius, double yRadius, double angle) =>
        plane.Origin
        + (plane.XAxis * (xRadius * Math.Cos(angle)))
        + (plane.YAxis * (yRadius * Math.Sin(angle)));

    /// <summary>The derivative with respect to the angle.</summary>
    /// <param name="plane">The frame.</param>
    /// <param name="xRadius">The radius along the frame's x axis.</param>
    /// <param name="yRadius">The radius along the frame's y axis.</param>
    /// <param name="angle">The angle in radians.</param>
    /// <returns>The derivative, whose length is the speed at that angle.</returns>
    internal static Vector3d DerivativeAt(
        in Plane plane, double xRadius, double yRadius, double angle) =>
        (plane.XAxis * (-xRadius * Math.Sin(angle))) + (plane.YAxis * (yRadius * Math.Cos(angle)));

    /// <summary>The second derivative with respect to the angle, which points at the centre.</summary>
    /// <param name="plane">The frame.</param>
    /// <param name="xRadius">The radius along the frame's x axis.</param>
    /// <param name="yRadius">The radius along the frame's y axis.</param>
    /// <param name="angle">The angle in radians.</param>
    /// <returns>The second derivative.</returns>
    internal static Vector3d SecondDerivativeAt(
        in Plane plane, double xRadius, double yRadius, double angle) =>
        (plane.XAxis * (-xRadius * Math.Cos(angle))) + (plane.YAxis * (-yRadius * Math.Sin(angle)));

    /// <summary>
    /// The exact bounding box of an angular sweep on an elliptical frame.
    /// </summary>
    /// <remarks>
    /// Each world axis is bounded by writing the coordinate as a single cosine — the component of
    /// the point along that axis is <c>origin + R·cos(angle - phase)</c> — and testing whether that
    /// cosine's peak and trough lie inside the sweep. Tessellating instead would be simpler and
    /// would return a box that is systematically too small, which is a culling bug rather than a
    /// rounding one.
    /// </remarks>
    /// <param name="plane">The frame.</param>
    /// <param name="xRadius">The radius along the frame's x axis.</param>
    /// <param name="yRadius">The radius along the frame's y axis.</param>
    /// <param name="startAngle">The angle the sweep starts at, in radians.</param>
    /// <param name="sweep">The sweep, in radians. Positive.</param>
    /// <returns>The bounding box.</returns>
    internal static BoundingBox Bounds(
        in Plane plane, double xRadius, double yRadius, double startAngle, double sweep)
    {
        BoundingBox box = new(
            PointAt(plane, xRadius, yRadius, startAngle),
            PointAt(plane, xRadius, yRadius, startAngle + sweep));

        Vector3d x = plane.XAxis * xRadius;
        Vector3d y = plane.YAxis * yRadius;
        Span<double> xs = [x.X, x.Y, x.Z];
        Span<double> ys = [y.X, y.Y, y.Z];

        for (int axis = 0; axis < 3; axis++)
        {
            if (xs[axis] == 0.0 && ys[axis] == 0.0)
            {
                continue;
            }

            double phase = Math.Atan2(ys[axis], xs[axis]);
            for (int half = 0; half < 2; half++)
            {
                double angle = phase + (half * Math.PI);
                if (Includes(startAngle, sweep, angle))
                {
                    box = box.Union(PointAt(plane, xRadius, yRadius, angle));
                }
            }
        }

        return box;
    }

    /// <summary>Whether an angle lies inside a sweep, wrapping at a full turn.</summary>
    /// <param name="startAngle">The angle the sweep starts at, in radians.</param>
    /// <param name="sweep">The sweep, in radians. Positive.</param>
    /// <param name="angle">The angle to test, in radians.</param>
    /// <returns><see langword="true"/> when the angle is on the sweep.</returns>
    internal static bool Includes(double startAngle, double sweep, double angle)
    {
        const double turn = Math.PI * 2.0;
        double delta = (angle - startAngle) % turn;
        if (delta < 0.0)
        {
            delta += turn;
        }

        // The slack matters at the ends: an extremum landing exactly on the start of the sweep
        // arrives as -1e-17 rather than as zero, and dropping it would lose a bound the box needs.
        return delta <= sweep + 1e-12 || delta >= turn - 1e-12;
    }

    /// <summary>
    /// Maps a circular frame through a transform, refusing anything that would stop the result being
    /// a circle.
    /// </summary>
    /// <remarks>
    /// The test is deliberately narrow: the two in-plane axes must stay perpendicular and must scale
    /// by the same factor. A transform that scales along the plane's normal is fine — it moves a
    /// circle without deforming it — and one that shears within the plane is not, because the answer
    /// would be an ellipse and this method's caller is a circle.
    /// </remarks>
    /// <param name="transform">The transform.</param>
    /// <param name="plane">The frame to map.</param>
    /// <param name="scale">The factor the radius must be multiplied by.</param>
    /// <returns>The mapped frame.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the transform is not affine, is degenerate, or is not a similarity within the
    /// plane.
    /// </exception>
    internal static Plane TransformFrame(in Transform transform, in Plane plane, out double scale)
    {
        if (!transform.IsAffine())
        {
            throw new ArgumentException(
                "A circular curve can only be mapped through an affine transform.",
                nameof(transform));
        }

        Vector3d x = transform.OfVector(plane.XAxis);
        Vector3d y = transform.OfVector(plane.YAxis);
        double xLength = x.Length;
        double yLength = y.Length;

        if (xLength <= 0.0 || yLength <= 0.0 || !double.IsFinite(xLength) || !double.IsFinite(yLength))
        {
            throw new ArgumentException(
                "The transform collapses this curve's plane.", nameof(transform));
        }

        double relative = Math.Abs(xLength - yLength) / Math.Max(xLength, yLength);
        double skew = Math.Abs(x.Dot(y)) / (xLength * yLength);
        if (relative > 1e-9 || skew > 1e-9)
        {
            throw new ArgumentException(
                "The transform scales this curve's plane unevenly, which would turn a circular "
                + "curve into one this type cannot represent.",
                nameof(transform));
        }

        scale = xLength;
        return Plane.ByOriginXAxisYAxis(transform.OfPoint(plane.Origin), x, y);
    }
}

using System;

namespace Spark.Geometry;

/// <summary>
/// The one copy of the rational-quadratic circular arc: the control points, weights and knot vector
/// that trace a circular sweep <i>exactly</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Piegl and Tiller A7.1, split into spans of at most a half turn.</b> Each span contributes two
/// control points after the first: a corner point on the intersection of the two end tangents,
/// weighted <c>cos(θ/2)</c>, and the span's end point on the arc, weighted 1.
/// </para>
/// <para>
/// <b>The corner point is not the midpoint of the arc.</b> It is where the tangents meet, at radius
/// <c>r / cos(θ/2)</c> — <i>outside</i> the arc — and putting the midpoint there instead gives a
/// curve through the right three points that is not a circle anywhere else. That is the error this
/// file exists to not make, and the test for it measures the <i>middle</i> of each span rather than
/// its ends.
/// </para>
/// <para>
/// <b>The weight is <c>cos(θ/2)</c> and not <c>cos θ</c>.</b> Halving the sweep is the step easiest
/// to lose, and losing it produces a curve that passes through the right endpoints and bulges
/// wrongly in between — which no test that samples only the ends can see.
/// </para>
/// <para>
/// <b>An arc of more than a half turn is split.</b> The rational form is valid only up to π: at a
/// half turn the middle weight reaches zero, and beyond it the control polygon turns inside out. So
/// a full circle is four quarters, and that is why a whole circle converts to nine control points
/// rather than three.
/// </para>
/// <para>
/// <b>This was private to <see cref="SurfaceConversion"/> until `E2-T71` step B.</b> The curve layer
/// needs exactly the same points for <see cref="Circle"/>, <see cref="Arc"/> and
/// <see cref="EllipseCurve"/>, and deriving it a second time is how two copies come to disagree
/// about a degenerate sweep — so it moved here rather than being written out again.
/// </para>
/// </remarks>
internal static class RationalArcs
{
    /// <summary>How many rational spans a sweep needs, at most a half turn each.</summary>
    /// <param name="sweep">The sweep, in radians.</param>
    /// <returns>At least one span.</returns>
    internal static int Spans(double sweep) =>
        Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2.0)));

    /// <summary>
    /// The rational control points, weights and knot vector of a circular arc of a given sweep.
    /// </summary>
    /// <param name="sweep">The angular domain, in radians.</param>
    /// <param name="radius">The radius to scale the unit points by.</param>
    /// <returns>The points in the plane, their weights, and the knot vector over the sweep.</returns>
    internal static (Point2d[] Points, double[] Weights, KnotVector Knots) Arc(
        in Interval sweep, double radius)
    {
        int spans = Spans(sweep.Length);
        double step = sweep.Length / spans;
        double half = step / 2.0;
        double cosHalf = Math.Cos(half);

        Point2d[] points = new Point2d[(2 * spans) + 1];
        double[] weights = new double[points.Length];

        points[0] = OnCircle(sweep.Min, radius);
        weights[0] = 1.0;

        for (int s = 0; s < spans; s++)
        {
            double start = sweep.Min + (s * step);
            double mid = start + half;
            double end = start + step;

            // The tangent intersection: on the bisector of the span, at radius r / cos(half).
            points[(2 * s) + 1] = OnCircle(mid, radius / cosHalf);
            weights[(2 * s) + 1] = cosHalf;

            points[(2 * s) + 2] = OnCircle(end, radius);
            weights[(2 * s) + 2] = 1.0;
        }

        // A clamped degree-2 knot vector with each interior span's knot doubled, so the spans join
        // with the right continuity rather than being smoothed across.
        double[] knots = new double[points.Length + 3];

        knots[0] = knots[1] = knots[2] = sweep.Min;

        for (int s = 1; s < spans; s++)
        {
            knots[(2 * s) + 1] = knots[(2 * s) + 2] = sweep.Min + (s * step);
        }

        knots[^3] = knots[^2] = knots[^1] = sweep.Max;

        return (points, weights, new KnotVector(2, knots));
    }

    /// <summary>
    /// The exact NURBS curve of an elliptical sweep in a plane — a circle when the two radii agree.
    /// </summary>
    /// <remarks>
    /// <b>One method serves the circle, the arc and the ellipse, because an ellipse is an affine
    /// image of a circle and an affine map carries a rational B-spline to a rational B-spline.</b>
    /// The control points, the weights and the knots are the circle's; only the frame they are
    /// mapped through is different — <c>x</c> scaled by one radius and <c>y</c> by the other. There
    /// is no separate ellipse construction to get wrong.
    /// </remarks>
    /// <param name="plane">The frame. Its origin is the centre.</param>
    /// <param name="xRadius">The radius along the frame's x axis.</param>
    /// <param name="yRadius">The radius along the frame's y axis.</param>
    /// <param name="startAngle">Where the sweep begins, measured from the frame's x axis.</param>
    /// <param name="sweep">
    /// How far it sweeps, in radians. The returned curve's domain is <c>[0, sweep]</c> — measured
    /// from the curve's own start, as <see cref="Arc"/> and <see cref="EllipseCurve"/> both are —
    /// rather than from the frame's x axis.
    /// </param>
    /// <returns>The curve.</returns>
    internal static NurbsCurve Elliptical(
        in Plane plane, double xRadius, double yRadius, double startAngle, double sweep)
    {
        // Built on the UNIT circle and scaled per axis on the way into the frame, rather than on a
        // circle of one of the radii: passing a radius here and the other as a factor would be two
        // ways of saying the same thing, and the ellipse would inherit whichever one was wrong.
        (Point2d[] flat, double[] weights, KnotVector knots) =
            Arc(new Interval(startAngle, startAngle + sweep), 1.0);

        // THE DOMAIN HAS TO BE CARRIED ACROSS, and for these curves that means shifting it. The
        // points are built at their angles IN THE FRAME, because that is where they are; Arc and
        // EllipseCurve are parameterised from their OWN START, so the knots are slid back by the
        // start angle. Sliding a knot vector is an affine reparameterisation and leaves the curve
        // untouched - which is exactly what is wanted, because the converted curve has to be the
        // same shape at the same parameters' worth of sweep, not the same shape somewhere else in
        // parameter space.
        double[] shifted = knots.ToArray();

        for (int index = 0; index < shifted.Length; index++)
        {
            shifted[index] -= startAngle;
        }

        Point3d[] controlPoints = new Point3d[flat.Length];

        for (int index = 0; index < flat.Length; index++)
        {
            controlPoints[index] = plane.Origin
                + (plane.XAxis * (flat[index].X * xRadius))
                + (plane.YAxis * (flat[index].Y * yRadius));
        }

        return new NurbsCurve(controlPoints, new KnotVector(2, shifted), weights);
    }

    /// <summary>The point at an angle on a circle of a given radius, in the frame's own plane.</summary>
    /// <param name="angle">The angle in radians.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The point.</returns>
    private static Point2d OnCircle(double angle, double radius) =>
        new(radius * Math.Cos(angle), radius * Math.Sin(angle));
}

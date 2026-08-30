using System;

namespace Spark.Geometry;

/// <summary>
/// Turns an analytic surface into the NURBS surface that is <i>exactly</i> the same sheet
/// (`E2-T19`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Exact, not approximate, and that is the whole point of the conversion.</b> A sphere, a
/// cylinder, a cone and a torus are all *rational* quadrics, so each one is representable to the
/// last bit by a NURBS surface of degree 2 with the right weights — no tolerance, no sampling, no
/// deviation. A conversion that fitted them instead would make round-tripping a model through
/// NURBS lossy, and would make "convert everything to NURBS and work in one representation" — which
/// is what a BRep kernel does constantly — quietly destructive.
/// </para>
/// <para>
/// <b>The trick in every case is the rational quarter-circle.</b> Three control points with
/// weights <c>1, cos(θ/2), 1</c> reproduce a circular arc of sweep <c>θ</c> exactly, for any sweep
/// under a half turn. Everything here is built from that one fact: a cylinder is an arc extruded, a
/// sphere is an arc revolved, a torus is an arc revolved about an arc. **The weight is
/// <c>cos(θ/2)</c> and not <c>cos θ</c>** — halving the sweep is the step that is easiest to lose
/// and produces a curve that looks like an arc, passes through the right endpoints, and bulges
/// wrongly in between.
/// </para>
/// <para>
/// <b>An arc of more than a half turn is split.</b> The rational form is only valid up to π: at a
/// half turn the middle weight reaches zero, and beyond it the control polygon turns inside out.
/// So a full circle is four quarters, and the splitting is why a whole sphere has nine control
/// points around rather than three.
/// </para>
/// <para>
/// <b>The <i>sheet</i> is exact. The <i>parameterisation</i> is not preserved, and cannot be.</b>
/// This is the one thing about these conversions that surprises people, so it is stated here rather
/// than discovered: a rational quadratic traces a circular arc exactly, but its parameter is a
/// projective function of the angle rather than the angle itself. Halfway along the knot span of a
/// quarter circle is the arc's midpoint; a quarter of the way along is <i>not</i> 22.5°. Every
/// point of the converted surface is on the original and every point of the original is on the
/// converted one — the two are the same set of points — but
/// <c>sphere.PointAt(u, v)</c> and <c>sphere.ToNurbsSurface().PointAt(u, v)</c> are different
/// points.
/// </para>
/// <para>
/// <b>What <i>is</i> preserved is the domain and therefore the extent.</b> The knot vectors span
/// the original's domains, so the two surfaces have the same four corners and the same edges, and
/// a patch converts to a patch of the same shape rather than to a whole sphere. That is what makes
/// the conversion usable for trimming and for a BRep face; a reparameterisation that also matched
/// angle for angle would need a non-rational representation and would no longer be exact, which is
/// the trade every kernel makes the same way.
/// </para>
/// </remarks>
public static class SurfaceConversion
{
    /// <summary>How many rational spans a sweep needs, at most a half turn each.</summary>
    private static int Spans(double sweep) => Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2.0)));

    /// <summary>The NURBS surface that is exactly this plane rectangle.</summary>
    /// <param name="surface">The plane surface.</param>
    /// <returns>A bilinear surface through its four corners.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    public static NurbsSurface ToNurbsSurface(this PlaneSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        Point3d[,] net = new Point3d[2, 2];
        net[0, 0] = surface.PointAt(surface.DomainU.Min, surface.DomainV.Min);
        net[0, 1] = surface.PointAt(surface.DomainU.Min, surface.DomainV.Max);
        net[1, 0] = surface.PointAt(surface.DomainU.Max, surface.DomainV.Min);
        net[1, 1] = surface.PointAt(surface.DomainU.Max, surface.DomainV.Max);

        return new NurbsSurface(
            Clamped(1, 2, surface.DomainU), Clamped(1, 2, surface.DomainV), net);
    }

    /// <summary>The NURBS surface that is exactly this cylinder.</summary>
    /// <param name="surface">The cylinder.</param>
    /// <returns>A rational surface, degree 2 around and degree 1 along.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <remarks>
    /// Degree 1 along the axis, because a cylinder is ruled: a straight generator is a degree-1
    /// B-spline exactly, and using degree 2 there would add control points that describe nothing.
    /// </remarks>
    public static NurbsSurface ToNurbsSurface(this CylindricalSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (Point2d[] section, double[] weights, KnotVector knots) = Arc(surface.DomainU, surface.Radius);

        Point3d[,] net = new Point3d[section.Length, 2];
        double[,] netWeights = new double[section.Length, 2];

        for (int i = 0; i < section.Length; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                double height = j == 0 ? surface.DomainV.Min : surface.DomainV.Max;

                net[i, j] = surface.Frame.Origin
                    + (surface.Frame.XAxis * section[i].X)
                    + (surface.Frame.YAxis * section[i].Y)
                    + (surface.Frame.Normal * height);

                netWeights[i, j] = weights[i];
            }
        }

        return new NurbsSurface(knots, Clamped(1, 2, surface.DomainV), net, netWeights);
    }

    /// <summary>The NURBS surface that is exactly this cone.</summary>
    /// <param name="surface">The cone.</param>
    /// <returns>A rational surface, degree 2 around and degree 1 along.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    public static NurbsSurface ToNurbsSurface(this ConicalSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (Point2d[] section, double[] weights, KnotVector knots) = Arc(surface.DomainU, 1.0);

        Point3d[,] net = new Point3d[section.Length, 2];
        double[,] netWeights = new double[section.Length, 2];

        for (int i = 0; i < section.Length; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                double height = j == 0 ? surface.DomainV.Min : surface.DomainV.Max;

                // The section is on the unit circle and is scaled by the radius *at that height*,
                // which is what makes one net serve a taper: the two rings differ only in scale.
                double radius = surface.RadiusAt(height);

                net[i, j] = surface.Frame.Origin
                    + (surface.Frame.XAxis * (section[i].X * radius))
                    + (surface.Frame.YAxis * (section[i].Y * radius))
                    + (surface.Frame.Normal * height);

                netWeights[i, j] = weights[i];
            }
        }

        return new NurbsSurface(knots, Clamped(1, 2, surface.DomainV), net, netWeights);
    }

    /// <summary>The NURBS surface that is exactly this sphere.</summary>
    /// <param name="surface">The sphere.</param>
    /// <returns>A rational surface, degree 2 in both directions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <remarks>
    /// <b>Both directions are arcs</b>, so both are rational: longitude around the axis and
    /// latitude as a half-circle from pole to pole. The result is the tensor product of the two,
    /// weights multiplied — which is exactly what a NURBS surface's weights mean.
    /// </remarks>
    public static NurbsSurface ToNurbsSurface(this SphericalSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (Point2d[] around, double[] aroundWeights, KnotVector knotsU) = Arc(surface.DomainU, 1.0);
        (Point2d[] profile, double[] profileWeights, KnotVector knotsV) = Arc(surface.DomainV, surface.Radius);

        Point3d[,] net = new Point3d[around.Length, profile.Length];
        double[,] weights = new double[around.Length, profile.Length];

        for (int i = 0; i < around.Length; i++)
        {
            for (int j = 0; j < profile.Length; j++)
            {
                // The profile arc lies in a half-plane: its x is the distance from the axis and its
                // y is the height. Rotating it by the section's unit direction gives the net.
                net[i, j] = surface.Frame.Origin
                    + (surface.Frame.XAxis * (around[i].X * profile[j].X))
                    + (surface.Frame.YAxis * (around[i].Y * profile[j].X))
                    + (surface.Frame.Normal * profile[j].Y);

                weights[i, j] = aroundWeights[i] * profileWeights[j];
            }
        }

        return new NurbsSurface(knotsU, knotsV, net, weights);
    }

    /// <summary>The NURBS surface that is exactly this torus.</summary>
    /// <param name="surface">The torus.</param>
    /// <returns>A rational surface, degree 2 in both directions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    public static NurbsSurface ToNurbsSurface(this ToroidalSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        (Point2d[] around, double[] aroundWeights, KnotVector knotsU) = Arc(surface.DomainU, 1.0);
        (Point2d[] tube, double[] tubeWeights, KnotVector knotsV) = Arc(surface.DomainV, surface.MinorRadius);

        Point3d[,] net = new Point3d[around.Length, tube.Length];
        double[,] weights = new double[around.Length, tube.Length];

        for (int i = 0; i < around.Length; i++)
        {
            for (int j = 0; j < tube.Length; j++)
            {
                double radius = surface.MajorRadius + tube[j].X;

                net[i, j] = surface.Frame.Origin
                    + (surface.Frame.XAxis * (around[i].X * radius))
                    + (surface.Frame.YAxis * (around[i].Y * radius))
                    + (surface.Frame.Normal * tube[j].Y);

                weights[i, j] = aroundWeights[i] * tubeWeights[j];
            }
        }

        return new NurbsSurface(knotsU, knotsV, net, weights);
    }

    /// <summary>
    /// The rational control points, weights and knot vector of a circular arc of a given sweep.
    /// </summary>
    /// <param name="sweep">The angular domain, in radians.</param>
    /// <param name="radius">The radius to scale the unit points by.</param>
    /// <returns>The points in the plane, their weights, and the knot vector over the sweep.</returns>
    /// <remarks>
    /// <para>
    /// <b>Piegl and Tiller A7.1, split into spans of at most a half turn.</b> Each span contributes
    /// two control points after the first: a corner point on the intersection of the two end
    /// tangents, weighted <c>cos(θ/2)</c>, and the span's end point on the arc, weighted 1.
    /// </para>
    /// <para>
    /// <b>The corner point is not the midpoint of the arc.</b> It is where the tangents meet, at
    /// radius <c>r / cos(θ/2)</c> — outside the arc — and putting the midpoint there instead gives a
    /// curve through the right three points that is not a circle anywhere else. That is the error
    /// this method exists to not make, and the test for it measures the *middle* of each span
    /// rather than its ends.
    /// </para>
    /// </remarks>
    private static (Point2d[] Points, double[] Weights, KnotVector Knots) Arc(in Interval sweep, double radius)
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

    private static Point2d OnCircle(double angle, double radius) =>
        new(radius * Math.Cos(angle), radius * Math.Sin(angle));

    /// <summary>A clamped knot vector over a chosen domain rather than over [0, 1].</summary>
    /// <remarks>
    /// <b>The domain has to be carried across</b>, or the converted surface is the same shape with a
    /// different parameterisation — and every caller comparing the two at a parameter would be
    /// comparing different places. It is the sort of difference that shows up only when something
    /// trims.
    /// </remarks>
    private static KnotVector Clamped(int degree, int controlPoints, in Interval domain)
    {
        double[] knots = new double[degree + controlPoints + 1];

        for (int i = 0; i < knots.Length; i++)
        {
            knots[i] = i <= degree ? domain.Min : domain.Max;
        }

        return new KnotVector(degree, knots);
    }
}

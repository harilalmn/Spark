using System;

namespace Spark.Geometry;

/// <summary>
/// The geometry <see cref="Circle"/>, <see cref="Arc"/> and <see cref="EllipseCurve"/> share.
/// All three are the same shape in a plane — a point at
/// <c>O + a·cos(θ)·X + b·sin(θ)·Y</c> — so the sweep arithmetic, the exact bounding box and the
/// rational-quadratic NURBS construction are written once here rather than three times.
/// </summary>
/// <remarks>
/// Angles here are bare radians rather than <see cref="Angle"/> values. That is deliberate and
/// is the one place in the kernel where it is: this type is <c>internal</c>, every caller is a
/// few lines away, and the alternative is wrapping and unwrapping a struct inside the
/// innermost loop of every evaluation. ADR-0011's rule is about <i>public</i> signatures, where
/// the reader cannot see the convention; it is not about arithmetic.
/// </remarks>
internal static class ConicNumerics
{
    /// <summary>
    /// How far an angle lies along a sweep, measured from its start in the direction the sweep
    /// travels.
    /// </summary>
    /// <param name="startRadians">The angle the sweep starts at.</param>
    /// <param name="sweepRadians">
    /// The signed sweep. Negative means the sweep runs clockwise about the plane's normal.
    /// </param>
    /// <param name="angleRadians">The angle to locate.</param>
    /// <returns>
    /// A value in <c>[0, 2π)</c>: the distance travelled from the start, in the sweep's own
    /// direction, to reach the angle. Compare it against the magnitude of the sweep to decide
    /// whether the angle is on the arc at all.
    /// </returns>
    /// <remarks>
    /// Measuring the offset in the sweep's direction, rather than normalising all three angles
    /// into <c>[0, 2π)</c> and comparing them, is what makes this right for arcs that cross the
    /// zero angle and for arcs that run clockwise. C2VGeometry's <c>IsAngleInArc</c> did the
    /// normalising version and could not tell a 20-degree arc written as 350° to 370° from the
    /// 340-degree arc that 350° to 10° describes.
    /// </remarks>
    internal static double SweepOffset(double startRadians, double sweepRadians, double angleRadians)
    {
        double direction = sweepRadians < 0.0 ? -1.0 : 1.0;
        double offset = (direction * (angleRadians - startRadians)) % Math.Tau;

        return offset < 0.0 ? offset + Math.Tau : offset;
    }

    /// <summary>
    /// Whether a sweep passes through an angle.
    /// </summary>
    /// <param name="startRadians">The angle the sweep starts at.</param>
    /// <param name="sweepRadians">The signed sweep.</param>
    /// <param name="angleRadians">The angle to test.</param>
    /// <returns><see langword="true"/> when the angle lies on the swept arc.</returns>
    internal static bool SweepReaches(double startRadians, double sweepRadians, double angleRadians) =>
        SweepOffset(startRadians, sweepRadians, angleRadians) <= Math.Abs(sweepRadians);

    /// <summary>
    /// A point on an axis-aligned conic in a plane.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the conic's centre.</param>
    /// <param name="radiusX">The radius along the plane's X axis.</param>
    /// <param name="radiusY">The radius along the plane's Y axis.</param>
    /// <param name="angleRadians">The angle parameter.</param>
    /// <returns>The point <c>O + a·cos(θ)·X + b·sin(θ)·Y</c>.</returns>
    internal static Point3d PointAtAngle(in Plane plane, double radiusX, double radiusY, double angleRadians) =>
        plane.Origin
        + (plane.XAxis * (radiusX * Math.Cos(angleRadians)))
        + (plane.YAxis * (radiusY * Math.Sin(angleRadians)));

    /// <summary>
    /// A derivative of a swept conic with respect to its own angle parameter.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the conic's centre.</param>
    /// <param name="radiusX">The radius along the plane's X axis.</param>
    /// <param name="radiusY">The radius along the plane's Y axis.</param>
    /// <param name="angleRadians">The angle to differentiate at.</param>
    /// <param name="order">The order of the derivative. Must be one or more.</param>
    /// <param name="clockwise">
    /// Whether the parameter runs clockwise about the plane's normal, which negates every
    /// odd-order derivative and leaves the even ones alone.
    /// </param>
    /// <returns>The derivative vector, exact at every order.</returns>
    /// <remarks>
    /// Each differentiation of <c>a·cos(θ)</c>, <c>b·sin(θ)</c> advances the angle by a quarter
    /// turn, so every order is the same expression evaluated a quarter turn further along. No
    /// order is ever zero and none is approximated — which is why a conic never needs the
    /// finite differences a general curve would.
    /// </remarks>
    internal static Vector3d ConicDerivative(
        in Plane plane,
        double radiusX,
        double radiusY,
        double angleRadians,
        int order,
        bool clockwise)
    {
        double turned = angleRadians + (order * (Math.PI / 2.0));
        double sign = clockwise && (order % 2) != 0 ? -1.0 : 1.0;

        return (plane.XAxis * (sign * radiusX * Math.Cos(turned)))
            + (plane.YAxis * (sign * radiusY * Math.Sin(turned)));
    }

    /// <summary>
    /// The tight world-axis-aligned bounding box of a swept conic.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the conic's centre.</param>
    /// <param name="radiusX">The radius along the plane's X axis.</param>
    /// <param name="radiusY">The radius along the plane's Y axis.</param>
    /// <param name="startRadians">The angle the sweep starts at.</param>
    /// <param name="sweepRadians">The signed sweep.</param>
    /// <returns>
    /// The smallest world-aligned box containing the swept arc — the two endpoints, widened by
    /// each axis extreme the sweep actually reaches.
    /// </returns>
    /// <remarks>
    /// Along a world axis <c>e</c> the coordinate is <c>O·e + A·cos(θ) + B·sin(θ)</c> with
    /// <c>A = a(X·e)</c> and <c>B = b(Y·e)</c>, which is stationary at
    /// <c>θ = atan2(B, A)</c> and half a turn from it. Testing only those two angles per axis
    /// gives the exact box in closed form. Returning the box of the whole conic instead — which
    /// C2VGeometry's <c>VArc.GetBounds</c> originally did — is four times too large for a
    /// quarter arc, and everything downstream of a bounding box believes it.
    /// </remarks>
    internal static BoundingBox ConicBounds(
        in Plane plane,
        double radiusX,
        double radiusY,
        double startRadians,
        double sweepRadians)
    {
        Point3d start = PointAtAngle(plane, radiusX, radiusY, startRadians);
        Point3d end = PointAtAngle(plane, radiusX, radiusY, startRadians + sweepRadians);

        double[] origin = [plane.Origin.X, plane.Origin.Y, plane.Origin.Z];
        double[] xAxis = [plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z];
        double[] yAxis = [plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z];
        double[] minimum = [Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Min(start.Z, end.Z)];
        double[] maximum = [Math.Max(start.X, end.X), Math.Max(start.Y, end.Y), Math.Max(start.Z, end.Z)];

        for (int axis = 0; axis < 3; axis++)
        {
            double a = radiusX * xAxis[axis];
            double b = radiusY * yAxis[axis];
            double stationary = Math.Atan2(b, a);

            for (int half = 0; half < 2; half++)
            {
                double angle = stationary + (half * Math.PI);

                if (!SweepReaches(startRadians, sweepRadians, angle))
                {
                    continue;
                }

                double value = origin[axis] + (a * Math.Cos(angle)) + (b * Math.Sin(angle));

                minimum[axis] = Math.Min(minimum[axis], value);
                maximum[axis] = Math.Max(maximum[axis], value);
            }
        }

        return new BoundingBox(
            new Point3d(minimum[0], minimum[1], minimum[2]),
            new Point3d(maximum[0], maximum[1], maximum[2]));
    }

    /// <summary>
    /// The exact rational quadratic NURBS representation of a swept conic.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the conic's centre.</param>
    /// <param name="radiusX">The radius along the plane's X axis.</param>
    /// <param name="radiusY">The radius along the plane's Y axis.</param>
    /// <param name="startRadians">The angle the sweep starts at.</param>
    /// <param name="sweepRadians">
    /// The signed sweep, whose magnitude must be greater than zero and at most a full turn.
    /// </param>
    /// <returns>
    /// A degree-two rational NURBS curve over the domain <c>[0, |sweep|]</c> occupying exactly
    /// the same positions as the conic, with the same start and end points.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The sweep is divided into equal spans of at most a quarter turn, and each span becomes
    /// one rational quadratic Bézier: the two end control points sit on the curve with weight
    /// one, and the middle control point sits at the half angle, pushed out by
    /// <c>1 / cos(Δ/2)</c>, with weight <c>cos(Δ/2)</c>. Ninety degrees is the limit because
    /// the middle weight reaches zero at a half turn and the middle control point runs off to
    /// infinity.
    /// </para>
    /// <para>
    /// The ellipse case is free: an ellipse is the image of a circle under the affine map that
    /// scales X by <c>a</c> and Y by <c>b</c>, and NURBS curves are closed under affine maps,
    /// so the control points of the circle map to the control points of the ellipse and the
    /// weights are untouched.
    /// </para>
    /// <para>
    /// The knots are scaled so the domain is the sweep in radians, matching the analytic
    /// curve's own domain. The two curves therefore agree at the ends and at every span
    /// boundary. They do <b>not</b> agree in between: the NURBS parameter is a rational
    /// function of the angle, and no rational reparameterisation of a circle by arc length
    /// exists.
    /// </para>
    /// </remarks>
    internal static NurbsCurve ConicNurbs(
        in Plane plane,
        double radiusX,
        double radiusY,
        double startRadians,
        double sweepRadians)
    {
        double magnitude = Math.Abs(sweepRadians);
        int spans = Math.Max(1, (int)Math.Ceiling((magnitude / (Math.PI / 2.0)) - 1e-12));
        double step = sweepRadians / spans;
        double middleWeight = Math.Cos(step / 2.0);
        double middleRadius = 1.0 / middleWeight;

        int count = (2 * spans) + 1;
        Point3d[] controlPoints = new Point3d[count];
        double[] weights = new double[count];

        controlPoints[0] = PointAtAngle(plane, radiusX, radiusY, startRadians);
        weights[0] = 1.0;

        for (int span = 0; span < spans; span++)
        {
            double middleAngle = startRadians + ((span + 0.5) * step);
            double endAngle = startRadians + ((span + 1) * step);

            controlPoints[(2 * span) + 1] = plane.Origin
                + (plane.XAxis * (radiusX * middleRadius * Math.Cos(middleAngle)))
                + (plane.YAxis * (radiusY * middleRadius * Math.Sin(middleAngle)));
            weights[(2 * span) + 1] = middleWeight;

            controlPoints[(2 * span) + 2] = PointAtAngle(plane, radiusX, radiusY, endAngle);
            weights[(2 * span) + 2] = 1.0;
        }

        // A full turn must come back to exactly where it started. Trigonometry does not
        // guarantee that — sin(2π) is about -2.4e-16, not zero — and the difference is enough
        // to make a converted circle report itself as open, since IsClosed is an exact test.
        if (magnitude == Math.Tau)
        {
            controlPoints[count - 1] = controlPoints[0];
        }

        double[] knots = new double[count + 3];

        knots[0] = 0.0;
        knots[1] = 0.0;
        knots[2] = 0.0;

        for (int span = 1; span < spans; span++)
        {
            double value = magnitude * span / spans;

            knots[(2 * span) + 1] = value;
            knots[(2 * span) + 2] = value;
        }

        knots[count] = magnitude;
        knots[count + 1] = magnitude;
        knots[count + 2] = magnitude;

        return new NurbsCurve(2, controlPoints, weights, knots);
    }
}

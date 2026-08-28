using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A circular arc: part of a circle, carried exactly as a plane, a radius and a pair of angles
/// rather than as an approximating spline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Definition.</b> The arc's centre is the origin of <see cref="Plane"/>, and angles are
/// measured in that plane from its <see cref="Plane.XAxis"/> towards its
/// <see cref="Plane.YAxis"/> — counter-clockwise looking down the plane's normal. The arc
/// begins at <see cref="StartAngle"/> and turns through <see cref="SweepAngle"/>, which is
/// signed: a negative sweep runs clockwise about the normal.
/// </para>
/// <para>
/// <b>Parameterisation.</b> The domain is <c>[0, |SweepAngle|]</c> in radians, so the parameter
/// is the angle turned through and arc length is simply the parameter times the radius. That
/// makes <see cref="LengthAt(double, in Tolerance)"/> and
/// <see cref="ParameterAtLength(double, in Tolerance)"/> exact rather than quadrature, and it
/// keeps the domain increasing whichever way the arc runs.
/// </para>
/// <para>
/// <b>Sweeps are not folded.</b> An arc from 350° to 370° is a twenty-degree arc and stays one.
/// Normalising the two angles independently into <c>[0, 360)</c> would rewrite it as 350° to
/// 10°, which reads as a 340-degree arc going the other way — a defect this kernel inherited a
/// warning about from C2VGeometry, where rotating an arc by zero degrees was enough to turn a
/// short arc into its complement.
/// </para>
/// </remarks>
public sealed class Arc : Curve
{
    private readonly double _start;
    private readonly double _sweep;

    /// <summary>
    /// Creates an arc from a plane, a radius and a pair of angles.
    /// </summary>
    /// <param name="plane">
    /// The plane the arc lies in. Its origin is the arc's centre and its X axis is the zero of
    /// the angle measurement.
    /// </param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <param name="startAngle">
    /// The angle the arc starts at, measured in the plane from its X axis. Any value is
    /// accepted and is kept as given; it is not folded into a single turn.
    /// </param>
    /// <param name="sweepAngle">
    /// The signed angle swept. Positive turns counter-clockwise about the plane's normal,
    /// negative turns clockwise. Its magnitude must be greater than zero and at most a full
    /// turn: a sweep beyond a full turn would revisit points already on the arc, which breaks
    /// every closest-point and parameter query the type offers.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is not positive and finite, when either angle is not finite, or
    /// when the sweep is zero or exceeds a full turn.
    /// </exception>
    public Arc(in Plane plane, double radius, Angle startAngle, Angle sweepAngle)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("An arc's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "An arc's radius must be a positive finite number.");
        }

        if (!double.IsFinite(startAngle.Radians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAngle),
                startAngle.Radians,
                "An arc's start angle must be finite.");
        }

        double sweep = sweepAngle.Radians;

        if (!double.IsFinite(sweep) || sweep == 0.0 || Math.Abs(sweep) > Math.Tau)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sweepAngle),
                sweep,
                "An arc's sweep must be non-zero and at most a full turn.");
        }

        Plane = plane;
        Radius = radius;
        _start = startAngle.Radians;
        _sweep = sweep;
    }

    /// <summary>
    /// The plane the arc lies in. Its origin is the arc's centre and its X axis is the zero of
    /// the angle measurement.
    /// </summary>
    public Plane Plane { get; }

    /// <summary>The arc's centre, which is the origin of <see cref="Plane"/>.</summary>
    public Point3d Centre => Plane.Origin;

    /// <summary>The arc's radius. Always positive.</summary>
    public double Radius { get; }

    /// <summary>The angle the arc starts at, measured in <see cref="Plane"/> from its X axis.</summary>
    public Angle StartAngle => Angle.FromRadians(_start);

    /// <summary>
    /// The signed angle the arc sweeps through. Positive counter-clockwise about the plane's
    /// normal, negative clockwise. Never zero, never beyond a full turn.
    /// </summary>
    public Angle SweepAngle => Angle.FromRadians(_sweep);

    /// <summary>
    /// The angle the arc ends at, which is <see cref="StartAngle"/> plus
    /// <see cref="SweepAngle"/>. Not folded into a single turn: an arc from 350° sweeping 20°
    /// ends at 370°, and saying so is what keeps the sweep recoverable.
    /// </summary>
    public Angle EndAngle => Angle.FromRadians(_start + _sweep);

    /// <inheritdoc/>
    /// <remarks>
    /// The domain is <c>[0, |SweepAngle|]</c> in radians: the parameter is the angle turned
    /// through since the start.
    /// </remarks>
    public override Interval Domain => new(0.0, Math.Abs(_sweep));

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> only for an arc whose sweep is exactly a full turn, which is a
    /// complete circle. Anything less has distinct ends.
    /// </remarks>
    public override bool IsClosed => Math.Abs(_sweep) == Math.Tau;

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> exactly when <see cref="IsClosed"/> is: a full turn joins up with
    /// matching tangents, and anything less does not join up at all.
    /// </remarks>
    public override bool IsPeriodic => IsClosed;

    /// <inheritdoc/>
    /// <remarks>
    /// Tight, and computed in closed form from the endpoints plus whichever axis extremes the
    /// sweep actually reaches — not the box of the whole circle.
    /// </remarks>
    public override BoundingBox BoundingBox => ConicNumerics.ConicBounds(Plane, Radius, Radius, _start, _sweep);

    /// <summary>
    /// Creates an arc from a plane, a radius and a pair of angles. The factory form of the
    /// equivalent constructor.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the centre.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <param name="startAngle">The angle the arc starts at.</param>
    /// <param name="sweepAngle">The signed angle swept.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is not positive and finite, or when the sweep is zero or beyond a
    /// full turn.
    /// </exception>
    public static Arc ByPlaneRadiusAngles(in Plane plane, double radius, Angle startAngle, Angle sweepAngle) =>
        new(plane, radius, startAngle, sweepAngle);

    /// <summary>
    /// Creates an arc about a centre, in the plane with a given normal.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <param name="startAngle">
    /// The angle the arc starts at, measured from the X axis the plane derives from the normal.
    /// That axis is chosen deterministically but arbitrarily, so this factory fixes the arc's
    /// size and its plane, not the compass direction it starts in — use
    /// <see cref="ByPlaneRadiusAngles(in Plane, double, Angle, Angle)"/> when the start
    /// direction matters.
    /// </param>
    /// <param name="sweepAngle">The signed angle swept.</param>
    /// <param name="normal">The plane's normal. Need not be normalised.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the centre is not finite or the normal is zero-length.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is not positive and finite, or when the sweep is zero or beyond a
    /// full turn.
    /// </exception>
    public static Arc ByCentreRadiusAnglesNormal(
        in Point3d centre,
        double radius,
        Angle startAngle,
        Angle sweepAngle,
        in Vector3d normal) =>
        new(new Plane(centre, normal), radius, startAngle, sweepAngle);

    /// <summary>
    /// Creates an arc from its centre, its start point and how far it sweeps.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="startPoint">
    /// The point the arc starts at. Its distance from the centre is the radius.
    /// </param>
    /// <param name="sweepAngle">
    /// The signed angle swept, positive counter-clockwise about <paramref name="normal"/>.
    /// </param>
    /// <param name="normal">The normal of the plane the arc turns in. Need not be normalised.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the start point is coincident with the centre, when either point is not
    /// finite, or when the normal is zero-length or parallel to the radius from the centre to
    /// the start point — in which case the start point does not lie in the plane at all.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the sweep is zero or beyond a full turn.
    /// </exception>
    public static Arc ByCentreStartPointSweepAngle(
        in Point3d centre,
        in Point3d startPoint,
        Angle sweepAngle,
        in Vector3d normal)
    {
        Vector3d radial = startPoint - centre;

        if (!radial.TryNormalise(out Vector3d xAxis))
        {
            throw new ArgumentException(
                "An arc's start point must differ from its centre.",
                nameof(startPoint));
        }

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "An arc's normal must have non-zero length and finite components.",
                nameof(normal));
        }

        return new Arc(
            Plane.ByOriginXAxisYAxis(centre, xAxis, unitNormal.Cross(xAxis)),
            radial.Length,
            Angle.Zero,
            sweepAngle);
    }

    /// <summary>
    /// Creates an arc from its centre, its start point and its end point.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="startPoint">The point the arc starts at; its distance from the centre is the radius.</param>
    /// <param name="endPoint">
    /// A point fixing the direction the arc ends in. <b>Its distance from the centre is
    /// ignored</b> — only the direction from the centre to it matters, so an end point that is
    /// not exactly at the radius is projected radially rather than rejected. That is the
    /// forgiving reading, and it is the one that makes this factory usable with points that
    /// have been through a transformation.
    /// </param>
    /// <returns>
    /// The arc turning from the start to the end the short way, through at most a half turn,
    /// counter-clockwise about the normal of the plane the three points define. For anything
    /// larger the sweep has to be stated, so use
    /// <see cref="ByCentreStartPointSweepAngle(in Point3d, in Point3d, Angle, in Vector3d)"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is coincident with the centre, when any point is not finite, or
    /// when the three are collinear — a start and an end diametrically opposite each other name
    /// two different half-circles and this factory cannot choose between them.
    /// </exception>
    public static Arc ByCentreStartPointEndPoint(
        in Point3d centre,
        in Point3d startPoint,
        in Point3d endPoint)
    {
        Vector3d radial = startPoint - centre;
        Vector3d toEnd = endPoint - centre;

        if (!radial.TryNormalise(out Vector3d xAxis))
        {
            throw new ArgumentException(
                "An arc's start point must differ from its centre.",
                nameof(startPoint));
        }

        if (!radial.Cross(toEnd).TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "The centre, the start point and the end point are collinear, so they do not "
                + "determine which way the arc turns.",
                nameof(endPoint));
        }

        Plane plane = Plane.ByOriginXAxisYAxis(centre, xAxis, unitNormal.Cross(xAxis));
        double sweep = Math.Atan2(toEnd.Dot(plane.YAxis), toEnd.Dot(plane.XAxis));

        return new Arc(plane, radial.Length, Angle.Zero, Angle.FromRadians(sweep));
    }

    /// <summary>
    /// Creates an arc from its centre, its start point and the length of curve to travel.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="startPoint">The point the arc starts at; its distance from the centre is the radius.</param>
    /// <param name="arcLength">
    /// The signed length of the arc. Positive travels counter-clockwise about
    /// <paramref name="normal"/>. Its magnitude must not exceed the circumference, since an arc
    /// may not sweep past a full turn.
    /// </param>
    /// <param name="normal">The normal of the plane the arc turns in. Need not be normalised.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the start point is coincident with the centre, or the normal is degenerate.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the arc length is zero, not finite, or longer than the circumference.
    /// </exception>
    public static Arc ByCentreStartPointArcLength(
        in Point3d centre,
        in Point3d startPoint,
        double arcLength,
        in Vector3d normal)
    {
        double radius = (startPoint - centre).Length;

        if (radius == 0.0 || !double.IsFinite(radius))
        {
            throw new ArgumentException(
                "An arc's start point must differ from its centre.",
                nameof(startPoint));
        }

        if (!double.IsFinite(arcLength) || arcLength == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arcLength),
                arcLength,
                "An arc's length must be a non-zero finite number.");
        }

        return ByCentreStartPointSweepAngle(centre, startPoint, Angle.FromRadians(arcLength / radius), normal);
    }

    /// <summary>
    /// Creates an arc through three points.
    /// </summary>
    /// <param name="startPoint">The point the arc starts at.</param>
    /// <param name="pointOnArc">A point the arc passes through between the other two.</param>
    /// <param name="endPoint">The point the arc ends at.</param>
    /// <returns>
    /// The arc of the circle through all three points, running from the start through the
    /// middle point to the end. The sweep may exceed a half turn: which way round the arc goes
    /// is decided by the middle point, which is the whole reason to have this factory.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the three points are collinear or any two coincide, since no unique circle
    /// passes through them, or when any point is not finite.
    /// </exception>
    public static Arc ByThreePoints(in Point3d startPoint, in Point3d pointOnArc, in Point3d endPoint)
    {
        Plane basis = Plane.ByThreePoints(startPoint, pointOnArc, endPoint);
        Point2d a = basis.To2d(startPoint);
        Point2d b = basis.To2d(pointOnArc);
        Point2d c = basis.To2d(endPoint);

        // Twice the signed area of the triangle. It is zero exactly when the points are
        // collinear, which Plane.ByThreePoints has already rejected, so this is a guard against
        // rounding rather than against the caller.
        double determinant = 2.0
            * ((a.X * (b.Y - c.Y)) + (b.X * (c.Y - a.Y)) + (c.X * (a.Y - b.Y)));

        if (determinant == 0.0)
        {
            throw new ArgumentException(
                "The three points are collinear, so no unique circle passes through them.",
                nameof(pointOnArc));
        }

        double sa = (a.X * a.X) + (a.Y * a.Y);
        double sb = (b.X * b.X) + (b.Y * b.Y);
        double sc = (c.X * c.X) + (c.Y * c.Y);

        double cx = ((sa * (b.Y - c.Y)) + (sb * (c.Y - a.Y)) + (sc * (a.Y - b.Y))) / determinant;
        double cy = ((sa * (c.X - b.X)) + (sb * (a.X - c.X)) + (sc * (b.X - a.X))) / determinant;

        Point3d centre = basis.To3d(new Point2d(cx, cy));
        Vector3d radial = startPoint - centre;
        Vector3d xAxis = radial.Normalised();

        // Plane.ByThreePoints orients its normal by the right-hand rule on the points in the
        // order given, so travelling counter-clockwise about that normal from the start always
        // reaches the middle point before the end. The sweep is therefore the counter-clockwise
        // offset of the end, with no case analysis needed.
        Plane plane = Plane.ByOriginXAxisYAxis(centre, xAxis, basis.Normal.Cross(xAxis));
        Vector3d toEnd = endPoint - centre;
        double sweep = ConicNumerics.SweepOffset(
            0.0,
            1.0,
            Math.Atan2(toEnd.Dot(plane.YAxis), toEnd.Dot(plane.XAxis)));

        return new Arc(plane, radial.Length, Angle.Zero, Angle.FromRadians(sweep));
    }

    /// <summary>
    /// Creates an arc between two points with a given radius.
    /// </summary>
    /// <param name="startPoint">The point the arc starts at.</param>
    /// <param name="endPoint">The point the arc ends at.</param>
    /// <param name="radius">
    /// The radius. Must be at least half the distance between the two points, since no smaller
    /// circle reaches both.
    /// </param>
    /// <param name="normal">
    /// The normal of the plane the arc turns in. The arc runs counter-clockwise about it from
    /// the start to the end. Need not be normalised, and need not be perpendicular to the
    /// chord — only the component perpendicular to the chord is used.
    /// </param>
    /// <param name="largeArc">
    /// <see langword="false"/> for the arc sweeping at most a half turn, <see langword="true"/>
    /// for its complement. The two share endpoints and a radius and differ in nothing else,
    /// which is why the flag has to exist.
    /// </param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the two points coincide, when any input is not finite, or when the normal is
    /// zero-length or parallel to the chord.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is not positive and finite, or is less than half the chord.
    /// </exception>
    public static Arc ByStartPointEndPointRadius(
        in Point3d startPoint,
        in Point3d endPoint,
        double radius,
        in Vector3d normal,
        bool largeArc = false)
    {
        Vector3d chord = endPoint - startPoint;
        double distance = chord.Length;

        if (distance == 0.0 || !double.IsFinite(distance))
        {
            throw new ArgumentException("An arc's endpoints must differ.", nameof(endPoint));
        }

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "An arc's radius must be a positive finite number.");
        }

        if (radius < distance / 2.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "The radius is too small: no circle of this radius reaches both points.");
        }

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "An arc's normal must have non-zero length and finite components.",
                nameof(normal));
        }

        if (!unitNormal.Cross(chord).TryNormalise(out Vector3d perpendicular))
        {
            throw new ArgumentException(
                "The normal is parallel to the chord, so it does not define a plane containing "
                + "both points.",
                nameof(normal));
        }

        double half = distance / 2.0;
        double offset = Math.Sqrt(Math.Max(0.0, (radius * radius) - (half * half)));
        Point3d midpoint = startPoint.Midpoint(endPoint);

        // The two candidate centres sit either side of the chord and give complementary
        // sweeps — one at most a half turn, the other at least one. Building both and choosing
        // by the sweep that comes out is what makes the large-arc flag actually mean what it
        // says. Choosing the centre and hoping, which is what C2VGeometry did, gets it wrong
        // whenever the two endpoint angles straddle the branch cut of atan2.
        Arc first = FromCentreEndpoints(midpoint + (perpendicular * offset), startPoint, endPoint, unitNormal);
        Arc second = FromCentreEndpoints(midpoint - (perpendicular * offset), startPoint, endPoint, unitNormal);
        bool firstIsLarge = Math.Abs(first._sweep) > Math.PI;

        return firstIsLarge == largeArc ? first : second;
    }

    /// <summary>
    /// Creates an arc between two points that sweeps through a given angle.
    /// </summary>
    /// <param name="startPoint">The point the arc starts at.</param>
    /// <param name="endPoint">The point the arc ends at.</param>
    /// <param name="sweepAngle">
    /// The signed angle swept, positive counter-clockwise about <paramref name="normal"/>. Its
    /// magnitude must be greater than zero and strictly less than a full turn: at a full turn
    /// the two points would have to coincide and the radius would be infinite.
    /// </param>
    /// <param name="normal">The normal of the plane the arc turns in. Need not be normalised.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the two points coincide, when any input is not finite, or when the normal is
    /// zero-length or parallel to the chord.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the sweep is zero or at least a full turn.
    /// </exception>
    public static Arc ByStartPointEndPointSweepAngle(
        in Point3d startPoint,
        in Point3d endPoint,
        Angle sweepAngle,
        in Vector3d normal)
    {
        double sweep = sweepAngle.Radians;

        if (!double.IsFinite(sweep) || sweep == 0.0 || Math.Abs(sweep) >= Math.Tau)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sweepAngle),
                sweep,
                "The sweep must be non-zero and less than a full turn: a full turn brings the "
                + "arc back to its start, so it has no distinct end point.");
        }

        double chord = (endPoint - startPoint).Length;
        double radius = chord / (2.0 * Math.Sin(Math.Abs(sweep) / 2.0));

        // A sweep clockwise about the given normal is the same arc as one counter-clockwise
        // about its opposite, which is how the sign is honoured without a second code path.
        return ByStartPointEndPointRadius(
            startPoint,
            endPoint,
            radius,
            sweep < 0.0 ? -normal : normal,
            Math.Abs(sweep) > Math.PI);
    }

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter) =>
        ConicNumerics.PointAtAngle(Plane, Radius, Radius, AngleAt(parameter));

    /// <inheritdoc/>
    /// <remarks>
    /// Exact at every order. Each differentiation advances the angle by a quarter turn and
    /// multiplies by the radius and by the sweep's sign, so no order is ever zero and none is
    /// approximated.
    /// </remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        double angle = AngleAt(parameter);

        if (order == 0)
        {
            return (Vector3d)ConicNumerics.PointAtAngle(Plane, Radius, Radius, angle);
        }

        return ConicNumerics.ConicDerivative(Plane, Radius, Radius, angle, order, _sweep < 0.0);
    }

    /// <inheritdoc/>
    /// <remarks>Exact: the radius times the total sweep. The tolerance is ignored.</remarks>
    public override double Length(in Tolerance tolerance = default) => Radius * Math.Abs(_sweep);

    /// <inheritdoc/>
    /// <remarks>Exact: the radius times the parameter, since the parameter is the angle turned.</remarks>
    public override double LengthAt(double parameter, in Tolerance tolerance = default) =>
        Radius * ClampParameter(parameter);

    /// <inheritdoc/>
    /// <remarks>Exact: the arc length divided by the radius, clamped into the domain.</remarks>
    public override double ParameterAtLength(double length, in Tolerance tolerance = default)
    {
        if (double.IsNaN(length))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The arc length must not be NaN.");
        }

        return Domain.Clamp(length / Radius);
    }

    /// <inheritdoc/>
    /// <remarks>Exact and constant: the reciprocal of the radius.</remarks>
    public override double CurvatureAt(double parameter)
    {
        ClampParameter(parameter);

        return 1.0 / Radius;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Exact rather than sampled. The point is projected into the arc's plane and its angle
    /// read off directly; if that angle is not on the sweep the answer is whichever end point
    /// is nearer <b>in space</b>, which is not always the one nearer in angle.
    /// </remarks>
    public override Point3d ClosestPoint(in Point3d point, out double parameter, in Tolerance tolerance = default)
    {
        if (!point.IsValid)
        {
            throw new ArgumentException("The point must be finite.", nameof(point));
        }

        Vector3d offset = point - Centre;
        double x = offset.Dot(Plane.XAxis);
        double y = offset.Dot(Plane.YAxis);

        // A point on the arc's axis is equidistant from every point of it. There is no closest
        // point to find, so the start is returned: an arbitrary answer stated in the docs beats
        // a NaN produced by atan2(0, 0) and passed on.
        if (x == 0.0 && y == 0.0)
        {
            parameter = 0.0;

            return StartPoint;
        }

        double travelled = ConicNumerics.SweepOffset(_start, _sweep, Math.Atan2(y, x));

        if (travelled <= Math.Abs(_sweep))
        {
            parameter = travelled;

            return PointAt(travelled);
        }

        Point3d start = StartPoint;
        Point3d end = EndPoint;

        if (point.DistanceSquaredTo(start) <= point.DistanceSquaredTo(end))
        {
            parameter = Domain.Min;

            return start;
        }

        parameter = Domain.Max;

        return end;
    }

    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>, exactly, and the plane is the arc's own.</remarks>
    public override bool IsPlanar(out Plane plane, in Tolerance tolerance = default)
    {
        plane = Plane;

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The result's domain is <c>[0, interval.Length]</c>, so
    /// <c>Trim(i).PointAt(u)</c> equals <c>PointAt(i.Min + u)</c>.
    /// </remarks>
    public override Arc Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);
        double direction = _sweep < 0.0 ? -1.0 : 1.0;

        return new Arc(
            Plane,
            Radius,
            Angle.FromRadians(_start + (direction * clipped.Min)),
            Angle.FromRadians(direction * clipped.Length));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reversed arc starts where this one ended and sweeps the other way in the same plane,
    /// so its plane and radius are unchanged and only the angles move.
    /// </remarks>
    public override Arc Reverse() =>
        new(Plane, Radius, Angle.FromRadians(_start + _sweep), Angle.FromRadians(-_sweep));

    /// <inheritdoc/>
    public override NurbsCurve ToNurbsCurve() =>
        ConicNumerics.ConicNurbs(Plane, Radius, Radius, _start, _sweep);

    /// <inheritdoc/>
    /// <remarks>
    /// An arc stays an arc under any similarity — a rigid motion with a uniform scale, with or
    /// without a reflection. Under a non-uniform scale or a shear it becomes an elliptical arc
    /// or worse, and the result is the transformed <see cref="ToNurbsCurve"/> instead, which is
    /// exact.
    /// </remarks>
    public override Curve Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        if (!IsSimilarity(transform, tolerance, out double scale))
        {
            return ToNurbsCurve().Transform(transform, tolerance);
        }

        return new Arc(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(Centre),
                transform.OfVector(Plane.XAxis),
                transform.OfVector(Plane.YAxis)),
            Radius * scale,
            StartAngle,
            SweepAngle);
    }

    /// <summary>
    /// Compares this arc with another by its defining plane, radius and angles, within a
    /// tolerance.
    /// </summary>
    /// <param name="other">The arc to compare with. <see langword="null"/> is never equal.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the planes, radii and both angles agree. This compares the
    /// <i>representation</i>: an arc and the same arc expressed in a plane rotated about its
    /// own normal are not equal even though they occupy the same positions.
    /// </returns>
    public bool EqualsWithin(Arc? other, in Tolerance tolerance = default) =>
        other is not null
        && Plane.EqualsWithin(other.Plane, tolerance)
        && tolerance.AreEqual(Radius, other.Radius)
        && StartAngle.EqualsWithin(other.StartAngle, tolerance)
        && SweepAngle.EqualsWithin(other.SweepAngle, tolerance);

    /// <summary>
    /// Formats the centre, radius and angles, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>Arc(Centre=(0, 0, 0), R=1, 0° sweeping 90°)</c>.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Arc(Centre={Centre}, R={Radius}, {StartAngle} sweeping {SweepAngle})");

    /// <summary>
    /// Builds the arc running counter-clockwise about a normal from one point to another about
    /// a known centre. Used by the start–end–radius construction, which produces two candidate
    /// centres and has to compare the arcs they give.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="startPoint">The start point.</param>
    /// <param name="endPoint">The end point.</param>
    /// <param name="unitNormal">The unit normal of the plane both points lie in.</param>
    /// <returns>The arc from the start to the end, counter-clockwise about the normal.</returns>
    private static Arc FromCentreEndpoints(
        in Point3d centre,
        in Point3d startPoint,
        in Point3d endPoint,
        in Vector3d unitNormal)
    {
        Vector3d radial = startPoint - centre;
        Vector3d xAxis = radial.Normalised();
        Plane plane = Plane.ByOriginXAxisYAxis(centre, xAxis, unitNormal.Cross(xAxis));
        Vector3d toEnd = endPoint - centre;
        double sweep = ConicNumerics.SweepOffset(
            0.0,
            1.0,
            Math.Atan2(toEnd.Dot(plane.YAxis), toEnd.Dot(plane.XAxis)));

        return new Arc(plane, radial.Length, Angle.Zero, Angle.FromRadians(sweep));
    }

    /// <summary>
    /// The angle in the arc's plane at a parameter.
    /// </summary>
    /// <param name="parameter">The parameter, clamped into the domain.</param>
    /// <returns>The angle in radians, measured from the plane's X axis.</returns>
    private double AngleAt(double parameter) =>
        _start + ((_sweep < 0.0 ? -1.0 : 1.0) * ClampParameter(parameter));
}

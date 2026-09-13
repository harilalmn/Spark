using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// A circular arc: part of a circle, parameterised over [0, sweep] in radians measured from the
/// arc's own start rather than from its plane's x axis.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sweep is always positive, and a negative one flips the plane instead.</b> An arc swept
/// clockwise about a normal is the same set of points as one swept anticlockwise about the opposite
/// normal, traversed the same way, so the constructor normalises to the second form. That keeps the
/// domain increasing — which the whole of <see cref="Curve"/> relies on — at the cost of
/// <see cref="Plane"/> sometimes reporting a normal the caller did not pass in.
/// <see cref="StartAngle"/> is likewise reported in the plane the arc actually holds.
/// </para>
/// <para>
/// The parameter is measured from the arc's start, so <c>PointAt(0)</c> is the start point of any
/// arc whatever its plane. The absolute angle within the plane is <see cref="StartAngle"/> plus the
/// parameter.
/// </para>
/// </remarks>
public sealed class Arc : Curve
{
    private const double FullTurn = Math.PI * 2.0;

    private readonly Plane _plane;
    private readonly double _radius;
    private readonly double _startAngle;
    private readonly double _sweep;

    private Arc(in Plane plane, double radius, double startAngle, double sweep)
    {
        _plane = plane;
        _radius = radius;
        _startAngle = startAngle;
        _sweep = sweep;
    }

    /// <summary>Lifts an instance's state into a new one, so a constructor can call a factory.</summary>
    /// <param name="other">The instance to copy. Already validated by whatever produced it.</param>
    /// <remarks>
    /// <b>`E2-T59` asked for a constructor beside every library factory, and a class constructor
    /// cannot return.</b> This is what lets the public ones below read <c>: this(SomeFactory(x))</c>.
    /// The arithmetic stays in the factory, which remains its only copy, so the constructor cannot
    /// drift away from the method it mirrors.
    /// </remarks>
    private Arc(Arc other)
        : this(other._plane, other._radius, other._startAngle, other._sweep)
    {
    }

    /// <summary>Creates an arc in a plane from a radius and two angles.</summary>
    /// <param name="plane">The plane. Its origin is the center.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <param name="startAngle">Where the arc begins, measured from the plane's x axis.</param>
    /// <param name="sweepAngle">How far it turns. May be negative.</param>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the radius is not positive.</exception>
    /// <remarks>Forwards to <see cref="FromPlaneRadiusAngles"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Arc(in Plane plane, double radius, Angle startAngle, Angle sweepAngle)
        : this(FromPlaneRadiusAngles(plane, radius, startAngle, sweepAngle))
    {
    }

    /// <summary>Creates the arc through three points.</summary>
    /// <param name="first">The start point.</param>
    /// <param name="second">A point the arc passes through.</param>
    /// <param name="third">The end point.</param>
    /// <exception cref="ArgumentException">Thrown when the points are collinear or coincident.</exception>
    /// <remarks>Forwards to <see cref="FromThreePoints"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Arc(in Point3d first, in Point3d second, in Point3d third)
        : this(FromThreePoints(first, second, third))
    {
    }

    /// <summary>Creates an arc from a center, a start point and a sweep.</summary>
    /// <param name="center">The center.</param>
    /// <param name="startPoint">Where the arc begins. Its distance from the center is the radius.</param>
    /// <param name="normal">The axis the sweep turns about. Need not be unit length.</param>
    /// <param name="sweepAngle">How far it turns. May be negative.</param>
    /// <exception cref="ArgumentException">Thrown when the start point sits on the center.</exception>
    /// <remarks>Forwards to <see cref="FromCenterStartPointSweepAngle"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Arc(in Point3d center, in Point3d startPoint, in Vector3d normal, Angle sweepAngle)
        : this(FromCenterStartPointSweepAngle(center, startPoint, normal, sweepAngle))
    {
    }

    /// <inheritdoc/>
    public override Interval Domain => new(0.0, _sweep);

    /// <inheritdoc/>
    public override bool IsClosed => _sweep >= FullTurn - 1e-12;

    /// <summary>The plane the arc lies in. Its origin is the center of the arc's circle.</summary>
    public Plane Plane => _plane;

    /// <summary>The center of the arc's circle.</summary>
    public Point3d Center => _plane.Origin;

    /// <summary>The radius.</summary>
    public double Radius => _radius;

    /// <summary>
    /// The angle from <see cref="Plane"/>'s x axis to the arc's start point, measured in the plane
    /// the arc holds rather than the one the caller may have passed.
    /// </summary>
    public Angle StartAngle => Angle.FromRadians(_startAngle);

    /// <summary>The angle the arc sweeps through. Always positive.</summary>
    public Angle SweepAngle => Angle.FromRadians(_sweep);

    /// <summary>The point halfway along the arc.</summary>
    public Point3d MidPoint => Evaluate(_sweep * 0.5);

    /// <summary>Creates an arc from a plane, a radius and two angles.</summary>
    /// <param name="plane">The plane. Its origin is the center of the arc's circle.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <param name="startAngle">The angle from the plane's x axis at which the arc starts.</param>
    /// <param name="sweepAngle">
    /// How far the arc sweeps. Must be non-zero and no more than a full turn. A negative sweep is
    /// normalised by flipping the plane, as described on <see cref="Arc"/>.
    /// </param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="radius"/> is not positive and finite, or when
    /// <paramref name="sweepAngle"/> is zero, not finite, or larger than a full turn.
    /// </exception>
    public static Arc FromPlaneRadiusAngles(
        in Plane plane, double radius, Angle startAngle, Angle sweepAngle)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("An arc's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "An arc's radius must be positive and finite.");
        }

        double sweep = sweepAngle.Radians;
        double start = startAngle.Radians;
        if (!double.IsFinite(sweep) || sweep == 0.0 || Math.Abs(sweep) > FullTurn + 1e-12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sweepAngle),
                sweepAngle,
                "An arc's sweep must be non-zero and no larger than a full turn.");
        }

        if (!double.IsFinite(start))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAngle), startAngle, "An arc's start angle must be finite.");
        }

        sweep = Math.Clamp(sweep, -FullTurn, FullTurn);
        return sweep > 0.0
            ? new Arc(plane, radius, start, sweep)
            : new Arc(
                Plane.FromOriginXAxisYAxis(plane.Origin, plane.XAxis, -plane.YAxis),
                radius,
                -start,
                -sweep);
    }

    /// <summary>Creates the arc that runs from the first point through the second to the third.</summary>
    /// <param name="first">The start point.</param>
    /// <param name="second">A point on the arc between the other two.</param>
    /// <param name="third">The end point.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the three points are collinear or coincident, so no arc passes through them.
    /// </exception>
    public static Arc FromThreePoints(in Point3d first, in Point3d second, in Point3d third)
    {
        (Plane plane, double radius) = Circumcircle(first, second, third);

        // No direction test is needed here, and the absence is the interesting part. The
        // circumcircle's normal comes from (second - first) × (third - first), which is the
        // right-handed normal of the triangle in the order the caller gave its corners; in that
        // frame, sweeping anticlockwise from the first point always reaches the second before the
        // third. The middle point therefore steers this method through the plane's orientation
        // rather than through a branch. A version of this code did test the order, and the branch
        // was unreachable — found by mutating it to a constant and watching every test still pass.
        return new Arc(plane, radius, 0.0, Wrap(AngleOf(plane, third)));
    }

    /// <summary>
    /// The arc from a start point to an end point about a given centre (`E2-T72`).
    /// </summary>
    /// <param name="center">The centre.</param>
    /// <param name="startPoint">Where the arc begins. Its distance from the centre is the radius.</param>
    /// <param name="endPoint">
    /// Where the arc ends — or rather, the <i>direction</i> in which it ends; see the remarks.
    /// </param>
    /// <returns>The arc, sweeping the shorter way round from start to end.</returns>
    /// <exception cref="ArgumentException">
    /// The start or the end coincides with the centre, or the three points are collinear, so no
    /// plane is determined.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>A centre, a start and an end over-determine an arc, and this is what that means here.</b>
    /// Three arbitrary points do not lie on a common circle about the first of them: the end point
    /// is generally at a different distance from the centre than the start is. The radius is taken
    /// from the <b>start</b>, and the end point is used only for the <i>direction</i> it lies in —
    /// so the arc finishes on the ray from the centre towards it, at the start's radius. A caller
    /// handing in three measured points will get an arc that misses the third, and this paragraph
    /// is why.
    /// </para>
    /// <para>
    /// <b>The sweep is the shorter way round.</b> Two rays from a centre bound two arcs, and the
    /// minor one is what everybody means. Use
    /// <see cref="FromCenterStartPointSweepAngle(in Point3d, in Point3d, in Vector3d, Angle)"/>
    /// with an explicit angle for the other one, or for anything past a half turn.
    /// </para>
    /// <para>
    /// <b>There is no constructor for this, and the reason is that one already means something
    /// else.</b> <c>new Arc(a, b, c)</c> takes three points <i>on</i> the arc — it is
    /// <see cref="FromThreePoints"/> — so a second reading of the same three arguments would be a
    /// coin flip the reader of the call could not resolve. This factory is named for that reason.
    /// </para>
    /// </remarks>
    public static Arc FromCenterStartEnd(in Point3d center, in Point3d startPoint, in Point3d endPoint)
    {
        Vector3d toStart = startPoint - center;
        Vector3d toEnd = endPoint - center;

        if (!toStart.TryNormalise(out Vector3d startDirection))
        {
            throw new ArgumentException(
                "An arc's start point must not coincide with its center.", nameof(startPoint));
        }

        if (!toEnd.TryNormalise(out Vector3d endDirection))
        {
            throw new ArgumentException(
                "An arc's end point must not coincide with its center: it gives no direction to end in.",
                nameof(endPoint));
        }

        Vector3d normal = startDirection.Cross(endDirection);

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "The center, the start and the end are collinear, so they determine no plane and no arc.",
                nameof(endPoint));
        }

        return FromCenterStartPointSweepAngle(center, startPoint, unitNormal, startDirection.AngleTo(endDirection));
    }

    /// <summary>
    /// The arc from a start point to an end point that leaves the start in a given direction
    /// (`E2-T72`).
    /// </summary>
    /// <param name="startPoint">Where the arc begins.</param>
    /// <param name="endPoint">Where it ends. It passes through this point exactly.</param>
    /// <param name="startTangent">The direction it sets off in. Only its direction is used.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// The two points coincide, the tangent is zero, or the tangent points along the chord — in
    /// which case the answer is a straight line and not an arc.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Unlike the centre form, this one is well posed</b>: two points and a direction determine
    /// exactly one arc. Its centre is where the perpendicular bisector of the chord meets the line
    /// through the start perpendicular to the tangent — both lines lie in the plane the three
    /// inputs span, so the whole construction is two-dimensional.
    /// </para>
    /// <para>
    /// <b>A tangent along the chord is refused rather than straightened.</b> The two perpendiculars
    /// are then parallel and there is no centre: the shape wanted is a straight line, and returning
    /// an arc of enormous radius pretending to be one would be a worse answer than saying so.
    /// </para>
    /// <para>
    /// The arc passes through <paramref name="endPoint"/> <i>exactly</i>, which the centre form
    /// does not promise — the difference between a well-posed construction and an over-determined
    /// one.
    /// </para>
    /// </remarks>
    public static Arc FromStartEndStartTangent(
        in Point3d startPoint, in Point3d endPoint, in Vector3d startTangent)
    {
        Vector3d chord = endPoint - startPoint;

        if (!chord.TryNormalise(out Vector3d alongChord))
        {
            throw new ArgumentException(
                "An arc needs two different points to run between.", nameof(endPoint));
        }

        if (!startTangent.TryNormalise(out Vector3d tangent))
        {
            throw new ArgumentException(
                "An arc's start tangent is zero, which is no direction at all.", nameof(startTangent));
        }

        Vector3d normal = tangent.Cross(alongChord);

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "The start tangent points along the chord, so the two points and the direction "
                + "describe a straight line rather than an arc.",
                nameof(startTangent));
        }

        // The centre lies perpendicular to the tangent at the start, at whatever distance puts it
        // equidistant from both points: |r·d|² = |r·d − chord|², which solves for r directly.
        Vector3d towardsCentre = unitNormal.Cross(tangent);
        double projection = chord.Dot(towardsCentre);

        if (projection == 0.0)
        {
            throw new ArgumentException(
                "The start tangent points along the chord, so the two points and the direction "
                + "describe a straight line rather than an arc.",
                nameof(startTangent));
        }

        double radius = chord.LengthSquared / (2.0 * projection);
        Point3d center = startPoint + (towardsCentre * radius);

        // With the centre known the sweep is the angle between the two radii - and it is the major
        // arc whenever the tangent points away from the end, which the sign of the projection of
        // the chord on the tangent decides.
        Vector3d toStart = startPoint - center;
        Vector3d toEnd = endPoint - center;
        Angle between = toStart.AngleTo(toEnd);
        Angle sweep = chord.Dot(tangent) >= 0.0
            ? between
            : Angle.FromRadians((2.0 * Math.PI) - between.Radians);

        Vector3d sweepNormal = radius >= 0.0 ? unitNormal : -unitNormal;

        return FromCenterStartPointSweepAngle(center, startPoint, sweepNormal, sweep);
    }

    /// <summary>Creates an arc from its center, its start point, a normal and a sweep.</summary>
    /// <param name="center">The center of the arc's circle.</param>
    /// <param name="startPoint">The arc's start point. Its distance from the center is the radius.</param>
    /// <param name="normal">
    /// The axis the arc turns about, following the right-hand rule. Need not be normalised, and need
    /// not be perpendicular to the line from the center to the start point: its component along that
    /// line is removed.
    /// </param>
    /// <param name="sweepAngle">How far the arc sweeps. Must be non-zero and no more than a full turn.</param>
    /// <returns>The arc.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the start point coincides with the center, or when the normal is zero-length,
    /// not finite, or parallel to the line from the center to the start point.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="sweepAngle"/> is zero, not finite, or larger than a full turn.
    /// </exception>
    public static Arc FromCenterStartPointSweepAngle(
        in Point3d center, in Point3d startPoint, in Vector3d normal, Angle sweepAngle)
    {
        Vector3d radial = startPoint - center;
        if (!radial.TryNormalise(out Vector3d xAxis))
        {
            throw new ArgumentException(
                "An arc's start point must not coincide with its center.", nameof(startPoint));
        }

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "An arc's normal must have non-zero length and finite components.", nameof(normal));
        }

        Vector3d yAxis = unitNormal.Cross(xAxis);
        if (!yAxis.TryNormalise(out Vector3d unitY))
        {
            throw new ArgumentException(
                "An arc's normal must not be parallel to the line from its center to its start point.",
                nameof(normal));
        }

        Plane plane = Plane.FromOriginXAxisYAxis(center, xAxis, unitY);
        return FromPlaneRadiusAngles(plane, radial.Length, Angle.Zero, sweepAngle);
    }

    /// <inheritdoc/>
    public override double LengthAt(double parameter) => CheckParameter(parameter) * _radius;

    /// <inheritdoc/>
    public override double ParameterAtLength(double distance)
    {
        if (!double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance), distance, "A distance along a curve must be finite.");
        }

        return Math.Clamp(distance / _radius, 0.0, _sweep);
    }

    /// <summary>
    /// Fits the arc that best passes through a set of points (`E2-T72`).
    /// </summary>
    /// <param name="points">At least three points, in order along the arc, not collinear.</param>
    /// <returns>
    /// The arc, spanning the points from the first to the last <b>the way they are ordered</b>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when fewer than three points are given, when one is not finite, when they are
    /// collinear or coincident, or when they do not advance monotonically around the fitted circle.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The circle is <see cref="Circle.FromBestFit"/>'s, and the only new question is the
    /// sweep.</b> Fitting is a question about a <i>circle</i>; which part of it the points cover is
    /// a question about their <i>order</i>, and the two are answered separately so the fit cannot
    /// be affected by the ordering.
    /// </para>
    /// <para>
    /// <b>The order of the points is data, not a hint.</b> The sweep is accumulated from one point
    /// to the next, each step taken the short way round, so points given end-to-end trace the arc
    /// they were sampled from — including one that passes the plane's x axis, which a first-to-last
    /// angle difference would get wrong by a whole turn. <b>Points out of order are refused</b>
    /// rather than fitted to whatever sweep their shuffled angles happen to sum to: a step that
    /// reverses direction means the caller does not have an arc.
    /// </para>
    /// </remarks>
    public static Arc FromBestFit(IReadOnlyList<Point3d> points)
    {
        (Plane plane, double radius) = Circle.FitInPlane(points);

        double[] angles = new double[points.Count];

        for (int index = 0; index < points.Count; index++)
        {
            Point2d flat = plane.To2d(points[index]);
            angles[index] = Math.Atan2(flat.Y, flat.X);
        }

        double sweep = 0.0;
        int sign = 0;

        for (int index = 1; index < points.Count; index++)
        {
            double step = angles[index] - angles[index - 1];

            // Each step the short way round. A step of more than half a turn between consecutive
            // samples is not an arc anybody sampled; it is two points on opposite sides.
            while (step > Math.PI)
            {
                step -= FullTurn;
            }

            while (step <= -Math.PI)
            {
                step += FullTurn;
            }

            int thisSign = Math.Sign(step);

            if (thisSign != 0 && sign != 0 && thisSign != sign)
            {
                throw new ArgumentException(
                    "Those points do not advance around the fitted circle in one direction, so "
                    + "they do not describe an arc. Order them along the arc, or fit a circle.",
                    nameof(points));
            }

            if (thisSign != 0)
            {
                sign = thisSign;
            }

            sweep += step;
        }

        if (sweep == 0.0)
        {
            throw new ArgumentException(
                "Those points cover no sweep of the fitted circle.", nameof(points));
        }

        return FromPlaneRadiusAngles(
            plane, radius, Angle.FromRadians(angles[0]), Angle.FromRadians(sweep));
    }

    /// <summary>Fits the arc that best passes through a set of points.</summary>
    /// <param name="points">At least three points, in order along the arc.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when no arc fits the points.</exception>
    /// <remarks>Forwards to <see cref="FromBestFit"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Arc(IReadOnlyList<Point3d> points)
        : this(FromBestFit(points))
    {
    }

    /// <inheritdoc/>
    /// <remarks>Always, and without fitting anything: an arc carries its plane.</remarks>
    public override bool IsPlanar(in Tolerance tolerance = default) => true;

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="Plane"/>, exactly — <b>not</b> a fit through a sampling. The base implementation
    /// would give an answer agreeing to the last few bits and would still be the wrong member to
    /// leave in place: it costs a tessellation and a covariance matrix to recover a frame this type
    /// was constructed from.
    /// </remarks>
    public override Plane? PlaneOf(in Tolerance tolerance = default) => _plane;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Exact, through the rational quadratic in <see cref="RationalArcs"/> — the same
    /// construction <see cref="SurfaceConversion"/> has used since `E2-T19`.</b> The control points are built at their angles in the plane and the knots are then slid back by <see cref="StartAngle"/>, because an arc is parameterised from its own start rather than from the plane's x axis.
    /// <para>
    /// <b>The parameterisation is not preserved, and cannot be.</b> A rational quadratic traces the
    /// curve exactly and walks it by a projective function of the angle rather than by the angle,
    /// so the two agree as <i>sets of points</i> and disagree about which parameter is where. The
    /// domain <i>is</i> preserved, so the two have the same ends and the same extent.
    /// </para>
    /// </remarks>
    public override NurbsConversion ToNurbsCurve(in Tolerance tolerance = default) =>
        new(RationalArcs.Elliptical(_plane, _radius, _radius, _startAngle, _sweep), true);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A wider <see cref="Arc"/>, exactly: an arc travels at a constant speed of
    /// <see cref="Radius"/>, so a requested arc length is a sweep of <c>length / radius</c> and
    /// there is nothing to integrate or approximate.
    /// </para>
    /// <para>
    /// <b>Extending past a full turn gives a full turn and no more</b>, rather than an arc that
    /// overlaps itself. An arc that has come all the way round is a circle, and a sweep of more
    /// than <c>2π</c> is a curve that passes through its own points twice — which
    /// <see cref="FromPlaneRadiusAngles"/> refuses and this will not smuggle past it.
    /// </para>
    /// </remarks>
    public override Curve Extended(double atStart, double atEnd)
    {
        CheckExtension(atStart, atEnd);

        double before = atStart / _radius;
        double after = atEnd / _radius;
        double sweep = Math.Min(_sweep + before + after, FullTurn);

        // When the two extensions together would pass a full turn, the surplus is dropped from the
        // END rather than shared, because a caller extending both ends of an almost-closed arc
        // means "close it" and the start is where the arc is anchored.
        double start = _startAngle - Math.Min(before, sweep - _sweep);

        return FromPlaneRadiusAngles(
            _plane, _radius, Angle.FromRadians(start), Angle.FromRadians(sweep));
    }

    /// <inheritdoc/>
    public override Curve Reversed() =>
        new Arc(
            Plane.FromOriginXAxisYAxis(_plane.Origin, _plane.XAxis, -_plane.YAxis),
            _radius,
            -(_startAngle + _sweep),
            _sweep);

    /// <inheritdoc/>
    public override Curve Trimmed(in Interval domain)
    {
        CheckTrimDomain(domain, Domain);
        return FromPlaneRadiusAngles(
            _plane,
            _radius,
            Angle.FromRadians(_startAngle + domain.Min),
            Angle.FromRadians(domain.Length));
    }

    /// <inheritdoc/>
    public override Curve TransformedBy(in Transform transform)
    {
        Plane plane = CircularArcs.TransformFrame(transform, _plane, out double scale);
        return new Arc(plane, _radius * scale, _startAngle, _sweep);
    }

    /// <summary>A readable description of the arc, for diagnostics.</summary>
    /// <returns>The center, radius and sweep.</returns>
    public override string ToString() =>
        $"Arc(center {Center}, radius {_radius}, sweep {SweepAngle})";

    /// <summary>
    /// The circle through three points, returned as a frame whose origin is the circumcenter and
    /// whose x axis points at the first point, so that the first point sits at angle zero.
    /// </summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <param name="third">The third point.</param>
    /// <returns>The frame and the radius.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the three points are collinear or coincident.
    /// </exception>
    internal static (Plane Plane, double Radius) Circumcircle(
        in Point3d first, in Point3d second, in Point3d third)
    {
        Vector3d toSecond = second - first;
        Vector3d toThird = third - first;
        if (!toSecond.Cross(toThird).TryNormalise(out Vector3d normal))
        {
            throw new ArgumentException(
                "Three collinear or coincident points do not define a circle.", nameof(second));
        }

        // Working in the plane's own 2d coordinates turns the circumcenter into the standard
        // determinant expression, which is both shorter and better conditioned than solving the
        // three-dimensional system directly.
        Plane frame = Plane.FromOriginNormal(first, normal);
        Point2d a = frame.To2d(first);
        Point2d b = frame.To2d(second);
        Point2d c = frame.To2d(third);

        double d = 2.0 * ((a.X * (b.Y - c.Y)) + (b.X * (c.Y - a.Y)) + (c.X * (a.Y - b.Y)));
        if (d == 0.0 || !double.IsFinite(d))
        {
            throw new ArgumentException(
                "Three collinear or coincident points do not define a circle.", nameof(second));
        }

        double aSquared = (a.X * a.X) + (a.Y * a.Y);
        double bSquared = (b.X * b.X) + (b.Y * b.Y);
        double cSquared = (c.X * c.X) + (c.Y * c.Y);
        double x = ((aSquared * (b.Y - c.Y)) + (bSquared * (c.Y - a.Y)) + (cSquared * (a.Y - b.Y))) / d;
        double y = ((aSquared * (c.X - b.X)) + (bSquared * (a.X - c.X)) + (cSquared * (b.X - a.X))) / d;

        Point3d center = frame.To3d(new Point2d(x, y));
        Vector3d radial = first - center;
        double radius = radial.Length;
        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentException(
                "Three collinear or coincident points do not define a circle.", nameof(second));
        }

        Vector3d xAxis = radial.Normalised();
        return (Plane.FromOriginXAxisYAxis(center, xAxis, normal.Cross(xAxis)), radius);
    }

    /// <inheritdoc/>
    protected override int TessellationSeedSpans =>
        Math.Max(1, (int)Math.Ceiling(_sweep / (Math.PI * 0.5)));

    /// <inheritdoc/>
    protected override double ComputeLength() => _radius * _sweep;

    /// <inheritdoc/>
    protected override BoundingBox ComputeBoundingBox() =>
        CircularArcs.Bounds(_plane, _radius, _radius, _startAngle, _sweep);

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter) =>
        CircularArcs.PointAt(_plane, _radius, _radius, _startAngle + parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter) =>
        CircularArcs.DerivativeAt(_plane, _radius, _radius, _startAngle + parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter) =>
        CircularArcs.SecondDerivativeAt(_plane, _radius, _radius, _startAngle + parameter);

    private static double AngleOf(in Plane plane, in Point3d point)
    {
        Vector3d radial = point - plane.Origin;
        return Math.Atan2(radial.Dot(plane.YAxis), radial.Dot(plane.XAxis));
    }

    private static double Wrap(double angle)
    {
        double wrapped = angle % FullTurn;
        return wrapped < 0.0 ? wrapped + FullTurn : wrapped;
    }
}

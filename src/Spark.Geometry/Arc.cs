using System;

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

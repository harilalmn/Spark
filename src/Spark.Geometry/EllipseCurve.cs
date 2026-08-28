using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// An ellipse or an elliptical arc, carried exactly as a plane, two radii and a pair of angles.
/// </summary>
/// <remarks>
/// <para>
/// <b>One type, not two.</b> Dynamo has an <c>Ellipse</c> and an <c>EllipseArc</c>; Spark has
/// this, over a sub-domain. A full ellipse is simply one whose sweep is a full turn, and
/// <see cref="Curve.IsClosed"/> says so. The name carries the <c>Curve</c> suffix because
/// <c>Ellipse</c> alone would read as a region rather than as a curve once the planar layer
/// arrives.
/// </para>
/// <para>
/// <b>Definition and parameterisation.</b> A point at angle <c>θ</c> is
/// <c>O + a·cos(θ)·X + b·sin(θ)·Y</c>, where <c>O</c>, <c>X</c> and <c>Y</c> come from
/// <see cref="Plane"/> and <c>a</c>, <c>b</c> are <see cref="RadiusX"/> and
/// <see cref="RadiusY"/>. The domain is <c>[0, |SweepAngle|]</c> and the parameter is that
/// angle, which is <b>not</b> arc length and is not the angle subtended at the centre either —
/// it is the eccentric angle, the standard parameterisation of an ellipse. Use
/// <see cref="Curve.ParameterAtLength(double, in Tolerance)"/> to work in arc length.
/// </para>
/// <para>
/// <b>The radii are not sorted.</b> <see cref="RadiusX"/> is the radius along the plane's X
/// axis, whether or not it is the larger. Sorting them would mean silently rotating the user's
/// plane, and a factory that quietly changes the frame you gave it is worse than one that lets
/// you describe a tall ellipse.
/// </para>
/// <para>
/// The arc length of an ellipse is an elliptic integral with no elementary closed form, so
/// <see cref="Curve.Length(in Tolerance)"/> is quadrature and honours the tolerance passed to
/// it. Everything else — points, derivatives of every order, the bounding box — is exact.
/// </para>
/// </remarks>
public sealed class EllipseCurve : Curve
{
    private readonly double _start;
    private readonly double _sweep;

    /// <summary>
    /// Creates an ellipse or elliptical arc from a plane, two radii and a pair of angles.
    /// </summary>
    /// <param name="plane">
    /// The plane the ellipse lies in. Its origin is the centre and its axes are the ellipse's
    /// own axes.
    /// </param>
    /// <param name="radiusX">The radius along the plane's X axis. Must be positive and finite.</param>
    /// <param name="radiusY">The radius along the plane's Y axis. Must be positive and finite.</param>
    /// <param name="startAngle">
    /// The angle the arc starts at. Any value is accepted and is kept as given; it is not
    /// folded into a single turn.
    /// </param>
    /// <param name="sweepAngle">
    /// The signed angle swept, positive counter-clockwise about the plane's normal. Its
    /// magnitude must be greater than zero and at most a full turn.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either radius is not positive and finite, when either angle is not finite,
    /// or when the sweep is zero or beyond a full turn.
    /// </exception>
    public EllipseCurve(in Plane plane, double radiusX, double radiusY, Angle startAngle, Angle sweepAngle)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("An ellipse's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(radiusX) || radiusX <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusX),
                radiusX,
                "An ellipse's radius must be a positive finite number.");
        }

        if (!double.IsFinite(radiusY) || radiusY <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusY),
                radiusY,
                "An ellipse's radius must be a positive finite number.");
        }

        if (!double.IsFinite(startAngle.Radians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAngle),
                startAngle.Radians,
                "An ellipse arc's start angle must be finite.");
        }

        double sweep = sweepAngle.Radians;

        if (!double.IsFinite(sweep) || sweep == 0.0 || Math.Abs(sweep) > Math.Tau)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sweepAngle),
                sweep,
                "An ellipse arc's sweep must be non-zero and at most a full turn.");
        }

        Plane = plane;
        RadiusX = radiusX;
        RadiusY = radiusY;
        _start = startAngle.Radians;
        _sweep = sweep;
    }

    /// <summary>The plane the ellipse lies in; its origin is the centre and its axes are the ellipse's.</summary>
    public Plane Plane { get; }

    /// <summary>The centre, which is the origin of <see cref="Plane"/>.</summary>
    public Point3d Centre => Plane.Origin;

    /// <summary>The radius along the plane's X axis. Positive, and not necessarily the larger of the two.</summary>
    public double RadiusX { get; }

    /// <summary>The radius along the plane's Y axis. Positive, and not necessarily the smaller of the two.</summary>
    public double RadiusY { get; }

    /// <summary>The larger of the two radii — the semi-major axis length.</summary>
    public double MajorRadius => Math.Max(RadiusX, RadiusY);

    /// <summary>The smaller of the two radii — the semi-minor axis length.</summary>
    public double MinorRadius => Math.Min(RadiusX, RadiusY);

    /// <summary>The angle the arc starts at, measured in <see cref="Plane"/> from its X axis.</summary>
    public Angle StartAngle => Angle.FromRadians(_start);

    /// <summary>
    /// The signed angle swept. Positive counter-clockwise about the plane's normal. Never zero,
    /// never beyond a full turn.
    /// </summary>
    public Angle SweepAngle => Angle.FromRadians(_sweep);

    /// <summary>
    /// The angle the arc ends at, which is <see cref="StartAngle"/> plus
    /// <see cref="SweepAngle"/>. Not folded into a single turn.
    /// </summary>
    public Angle EndAngle => Angle.FromRadians(_start + _sweep);

    /// <inheritdoc/>
    /// <remarks>
    /// The domain is <c>[0, |SweepAngle|]</c> in radians, and the parameter is the eccentric
    /// angle rather than arc length.
    /// </remarks>
    public override Interval Domain => new(0.0, Math.Abs(_sweep));

    /// <inheritdoc/>
    /// <remarks><see langword="true"/> only for a full turn.</remarks>
    public override bool IsClosed => Math.Abs(_sweep) == Math.Tau;

    /// <inheritdoc/>
    /// <remarks><see langword="true"/> exactly when <see cref="IsClosed"/> is.</remarks>
    public override bool IsPeriodic => IsClosed;

    /// <inheritdoc/>
    /// <remarks>Tight, and computed in closed form from the endpoints plus the axis extremes the sweep reaches.</remarks>
    public override BoundingBox BoundingBox =>
        ConicNumerics.ConicBounds(Plane, RadiusX, RadiusY, _start, _sweep);

    /// <summary>
    /// Creates a full ellipse in a plane.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the centre and whose axes are the ellipse's.</param>
    /// <param name="radiusX">The radius along the plane's X axis.</param>
    /// <param name="radiusY">The radius along the plane's Y axis.</param>
    /// <returns>The ellipse, swept a full turn counter-clockwise from the plane's X axis.</returns>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either radius is not positive and finite.</exception>
    public static EllipseCurve ByPlaneRadii(in Plane plane, double radiusX, double radiusY) =>
        new(plane, radiusX, radiusY, Angle.Zero, Angle.FullTurn);

    /// <summary>
    /// Creates an elliptical arc in a plane.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the centre and whose axes are the ellipse's.</param>
    /// <param name="radiusX">The radius along the plane's X axis.</param>
    /// <param name="radiusY">The radius along the plane's Y axis.</param>
    /// <param name="startAngle">The angle the arc starts at.</param>
    /// <param name="sweepAngle">The signed angle swept.</param>
    /// <returns>The elliptical arc.</returns>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either radius is not positive and finite, or when the sweep is zero or
    /// beyond a full turn.
    /// </exception>
    public static EllipseCurve ByPlaneRadiiAngles(
        in Plane plane,
        double radiusX,
        double radiusY,
        Angle startAngle,
        Angle sweepAngle) =>
        new(plane, radiusX, radiusY, startAngle, sweepAngle);

    /// <summary>
    /// Creates a full ellipse about a centre in the plane with a given normal.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radiusX">The radius along the derived plane's X axis.</param>
    /// <param name="radiusY">The radius along the derived plane's Y axis.</param>
    /// <param name="normal">The normal of the plane the ellipse lies in. Need not be normalised.</param>
    /// <returns>
    /// The ellipse. Which way its axes point in the plane is decided by the deterministic but
    /// arbitrary frame derived from the normal; pass a plane, or use
    /// <see cref="ByCentreVectors(in Point3d, in Vector3d, in Vector3d)"/>, when that matters.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the centre is not finite or the normal is zero-length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either radius is not positive and finite.</exception>
    public static EllipseCurve ByCentreRadiiNormal(
        in Point3d centre,
        double radiusX,
        double radiusY,
        in Vector3d normal) =>
        new(new Plane(centre, normal), radiusX, radiusY, Angle.Zero, Angle.FullTurn);

    /// <summary>
    /// Creates a full ellipse from its centre and its two axis vectors.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="xAxis">
    /// The first semi-axis, as a vector from the centre to the point at angle zero. Its length
    /// is <see cref="RadiusX"/> and its direction is the plane's X axis.
    /// </param>
    /// <param name="yAxis">
    /// The second semi-axis. Only its length and the part of it perpendicular to
    /// <paramref name="xAxis"/> are used, so a pair of axes that are not quite perpendicular
    /// gives an ellipse with the second radius you asked for and a corrected direction rather
    /// than a sheared shape.
    /// </param>
    /// <returns>The ellipse.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either vector is zero-length or not finite, or when the two are parallel and
    /// so span no plane.
    /// </exception>
    public static EllipseCurve ByCentreVectors(in Point3d centre, in Vector3d xAxis, in Vector3d yAxis) =>
        new(
            Plane.ByOriginXAxisYAxis(centre, xAxis, yAxis),
            xAxis.Length,
            yAxis.Length,
            Angle.Zero,
            Angle.FullTurn);

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter) =>
        ConicNumerics.PointAtAngle(Plane, RadiusX, RadiusY, AngleAt(parameter));

    /// <inheritdoc/>
    /// <remarks>Exact at every order.</remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        double angle = AngleAt(parameter);

        return order == 0
            ? (Vector3d)ConicNumerics.PointAtAngle(Plane, RadiusX, RadiusY, angle)
            : ConicNumerics.ConicDerivative(Plane, RadiusX, RadiusY, angle, order, _sweep < 0.0);
    }

    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>, exactly, and the plane is the ellipse's own.</remarks>
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
    public override EllipseCurve Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);
        double direction = _sweep < 0.0 ? -1.0 : 1.0;

        return new EllipseCurve(
            Plane,
            RadiusX,
            RadiusY,
            Angle.FromRadians(_start + (direction * clipped.Min)),
            Angle.FromRadians(direction * clipped.Length));
    }

    /// <inheritdoc/>
    public override EllipseCurve Reverse() =>
        new(Plane, RadiusX, RadiusY, Angle.FromRadians(_start + _sweep), Angle.FromRadians(-_sweep));

    /// <inheritdoc/>
    public override NurbsCurve ToNurbsCurve() =>
        ConicNumerics.ConicNurbs(Plane, RadiusX, RadiusY, _start, _sweep);

    /// <inheritdoc/>
    /// <remarks>
    /// An ellipse keeps its shape under a similarity, which scales both radii by the same
    /// factor. Under a non-uniform scale it is still an ellipse, but one whose axes are no
    /// longer the images of these axes — recovering them means solving for the principal axes
    /// of the transformed quadratic form, which this does not do. The result in that case is
    /// the transformed <see cref="ToNurbsCurve"/>, which is exact.
    /// </remarks>
    public override Curve Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        if (!IsSimilarity(transform, tolerance, out double scale))
        {
            return ToNurbsCurve().Transform(transform, tolerance);
        }

        return new EllipseCurve(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(Centre),
                transform.OfVector(Plane.XAxis),
                transform.OfVector(Plane.YAxis)),
            RadiusX * scale,
            RadiusY * scale,
            StartAngle,
            SweepAngle);
    }

    /// <summary>
    /// Compares this ellipse with another by its defining plane, radii and angles, within a
    /// tolerance.
    /// </summary>
    /// <param name="other">The ellipse to compare with. <see langword="null"/> is never equal.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the planes, both radii and both angles agree. This compares
    /// the <i>representation</i>, so an ellipse and the same ellipse described with its axes
    /// exchanged and its plane turned a quarter turn are not equal.
    /// </returns>
    public bool EqualsWithin(EllipseCurve? other, in Tolerance tolerance = default) =>
        other is not null
        && Plane.EqualsWithin(other.Plane, tolerance)
        && tolerance.AreEqual(RadiusX, other.RadiusX)
        && tolerance.AreEqual(RadiusY, other.RadiusY)
        && StartAngle.EqualsWithin(other.StartAngle, tolerance)
        && SweepAngle.EqualsWithin(other.SweepAngle, tolerance);

    /// <summary>
    /// Formats the centre, radii and angles, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>EllipseCurve(Centre=(0, 0, 0), Rx=2, Ry=1, 0° sweeping 360°)</c>.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"EllipseCurve(Centre={Centre}, Rx={RadiusX}, Ry={RadiusY}, {StartAngle} sweeping {SweepAngle})");

    /// <summary>
    /// The eccentric angle at a parameter.
    /// </summary>
    /// <param name="parameter">The parameter, clamped into the domain.</param>
    /// <returns>The angle in radians, measured from the plane's X axis.</returns>
    private double AngleAt(double parameter) =>
        _start + ((_sweep < 0.0 ? -1.0 : 1.0) * ClampParameter(parameter));
}

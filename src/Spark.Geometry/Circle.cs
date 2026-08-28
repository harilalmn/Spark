using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A complete circle, carried exactly as a plane and a radius.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parameterisation.</b> The domain is <c>[0, 2π]</c> and the parameter is the angle in the
/// circle's plane, measured from its <see cref="Plane.XAxis"/> towards its
/// <see cref="Plane.YAxis"/>. Arc length is the parameter times the radius.
/// </para>
/// <para>
/// A circle is a separate type from a full-sweep <see cref="Arc"/> because the two mean
/// different things to a reader and to the node library, and because the radius a user typed
/// survives every query as the number they typed. <see cref="Trim(in Interval)"/> gives back an
/// <see cref="Arc"/>, which is what a piece of a circle is.
/// </para>
/// </remarks>
public sealed class Circle : Curve
{
    /// <summary>
    /// Creates a circle in a plane.
    /// </summary>
    /// <param name="plane">The plane the circle lies in. Its origin is the circle's centre.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the radius is zero, negative, infinite or <see cref="double.NaN"/>.
    /// </exception>
    public Circle(in Plane plane, double radius)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("A circle's plane must be valid.", nameof(plane));
        }

        if (!double.IsFinite(radius) || radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "A circle's radius must be a positive finite number.");
        }

        Plane = plane;
        Radius = radius;
    }

    /// <summary>The plane the circle lies in; its origin is the centre.</summary>
    public Plane Plane { get; }

    /// <summary>The centre, which is the origin of <see cref="Plane"/>.</summary>
    public Point3d Centre => Plane.Origin;

    /// <summary>The radius. Always positive.</summary>
    public double Radius { get; }

    /// <summary>
    /// The circle's normal, which is the normal of its plane and the axis it turns
    /// counter-clockwise about.
    /// </summary>
    public Vector3d Normal => Plane.Normal;

    /// <summary>The area enclosed by the circle.</summary>
    public double Area => Math.PI * Radius * Radius;

    /// <inheritdoc/>
    /// <remarks>The domain is <c>[0, 2π]</c>: the parameter is the angle in the circle's plane.</remarks>
    public override Interval Domain => new(0.0, Math.Tau);

    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>.</remarks>
    public override bool IsClosed => true;

    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>: the point and the tangent match across the seam.</remarks>
    public override bool IsPeriodic => true;

    /// <inheritdoc/>
    /// <remarks>
    /// Tight and closed-form. The extent along a world axis <c>e</c> is
    /// <c>r·√((X·e)² + (Y·e)²)</c>, which is the radius for a circle facing that axis and zero
    /// for one edge-on to it.
    /// </remarks>
    public override BoundingBox BoundingBox =>
        ConicNumerics.ConicBounds(Plane, Radius, Radius, 0.0, Math.Tau);

    /// <summary>
    /// Creates a circle in a plane. The factory form of the equivalent constructor.
    /// </summary>
    /// <param name="plane">The plane, whose origin is the centre.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <returns>The circle.</returns>
    /// <exception cref="ArgumentException">Thrown when the plane is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the radius is not positive and finite.</exception>
    public static Circle ByPlaneRadius(in Plane plane, double radius) => new(plane, radius);

    /// <summary>
    /// Creates a circle about a centre, lying in a plane parallel to the world XY plane.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <returns>The circle, with its normal along <c>+Z</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when the centre is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the radius is not positive and finite.</exception>
    public static Circle ByCentreRadius(in Point3d centre, double radius) =>
        new(new Plane(centre, Vector3d.ZAxis), radius);

    /// <summary>
    /// Creates a circle about a centre in the plane with a given normal.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <param name="normal">The normal of the plane the circle lies in. Need not be normalised.</param>
    /// <returns>
    /// The circle. Where its parameterisation starts on the rim is decided by the deterministic
    /// but arbitrary X axis the plane derives from the normal; pass a plane instead when that
    /// matters.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the centre is not finite or the normal is zero-length.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the radius is not positive and finite.</exception>
    public static Circle ByCentreRadiusNormal(in Point3d centre, double radius, in Vector3d normal) =>
        new(new Plane(centre, normal), radius);

    /// <summary>
    /// Creates the circle through three points.
    /// </summary>
    /// <param name="first">The first point, which becomes <c>PointAt(0)</c>.</param>
    /// <param name="second">The second point.</param>
    /// <param name="third">The third point.</param>
    /// <returns>
    /// The circumscribed circle. Its normal follows the right-hand rule for the three points in
    /// the order given, so the circle runs first, second, third as the parameter increases.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the points are collinear or any two coincide, since no unique circle passes
    /// through them, or when any point is not finite.
    /// </exception>
    public static Circle ByThreePoints(in Point3d first, in Point3d second, in Point3d third)
    {
        Arc arc = Arc.ByThreePoints(first, second, third);

        return new Circle(arc.Plane, arc.Radius);
    }

    /// <inheritdoc/>
    public override Point3d PointAt(double parameter) =>
        ConicNumerics.PointAtAngle(Plane, Radius, Radius, ClampParameter(parameter));

    /// <inheritdoc/>
    /// <remarks>Exact at every order.</remarks>
    public override Vector3d DerivativeAt(double parameter, int order)
    {
        ThrowIfOrderIsNegative(order);

        double angle = ClampParameter(parameter);

        return order == 0
            ? (Vector3d)ConicNumerics.PointAtAngle(Plane, Radius, Radius, angle)
            : ConicNumerics.ConicDerivative(Plane, Radius, Radius, angle, order, false);
    }

    /// <inheritdoc/>
    /// <remarks>Exact: the circumference. The tolerance is ignored.</remarks>
    public override double Length(in Tolerance tolerance = default) => Math.Tau * Radius;

    /// <inheritdoc/>
    /// <remarks>Exact: the radius times the parameter.</remarks>
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
    /// Exact rather than sampled: the point is projected into the circle's plane and its angle
    /// read off directly. A point on the circle's axis is equidistant from the whole rim; the
    /// answer there is <c>PointAt(0)</c>, stated rather than left to <c>atan2(0, 0)</c>.
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

        if (x == 0.0 && y == 0.0)
        {
            parameter = 0.0;

            return StartPoint;
        }

        parameter = ConicNumerics.SweepOffset(0.0, 1.0, Math.Atan2(y, x));

        return PointAt(parameter);
    }

    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>, exactly, and the plane is the circle's own.</remarks>
    public override bool IsPlanar(out Plane plane, in Tolerance tolerance = default)
    {
        plane = Plane;

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Part of a circle is an <see cref="Arc"/>, so that is what comes back — even when the
    /// interval covers the whole domain, because a type that changed with the interval would be
    /// worse to program against than one that never does.
    /// </remarks>
    public override Arc Trim(in Interval interval)
    {
        Interval clipped = ClipToDomain(interval);

        return new Arc(
            Plane,
            Radius,
            Angle.FromRadians(clipped.Min),
            Angle.FromRadians(clipped.Length));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The reversed circle occupies the same positions with its normal flipped, so it turns the
    /// other way while keeping the same start point and the same domain.
    /// </remarks>
    public override Circle Reverse() =>
        new(Plane.ByOriginXAxisYAxis(Centre, Plane.XAxis, -Plane.YAxis), Radius);

    /// <inheritdoc/>
    public override NurbsCurve ToNurbsCurve() =>
        ConicNumerics.ConicNurbs(Plane, Radius, Radius, 0.0, Math.Tau);

    /// <inheritdoc/>
    /// <remarks>
    /// A circle stays a circle under any similarity. Under a non-uniform scale it becomes an
    /// ellipse and under a shear something with no simpler name, and in both cases the result
    /// is the transformed <see cref="ToNurbsCurve"/>, which is exact.
    /// </remarks>
    public override Curve Transform(in Transform transform, in Tolerance tolerance = default)
    {
        ValidateTransform(transform, tolerance);

        if (!IsSimilarity(transform, tolerance, out double scale))
        {
            return ToNurbsCurve().Transform(transform, tolerance);
        }

        return new Circle(
            Plane.ByOriginXAxisYAxis(
                transform.OfPoint(Centre),
                transform.OfVector(Plane.XAxis),
                transform.OfVector(Plane.YAxis)),
            Radius * scale);
    }

    /// <summary>
    /// Compares this circle with another by its defining plane and radius, within a tolerance.
    /// </summary>
    /// <param name="other">The circle to compare with. <see langword="null"/> is never equal.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the planes and radii agree. This compares the
    /// <i>representation</i>: two circles occupying the same positions but starting their
    /// parameterisation at different points on the rim are not equal.
    /// </returns>
    public bool EqualsWithin(Circle? other, in Tolerance tolerance = default) =>
        other is not null
        && Plane.EqualsWithin(other.Plane, tolerance)
        && tolerance.AreEqual(Radius, other.Radius);

    /// <summary>
    /// Formats the centre and radius, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>Circle(Centre=(0, 0, 0), R=1)</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Circle(Centre={Centre}, R={Radius})");
}

using System;

namespace Spark.Geometry;

/// <summary>
/// A full circle in a plane, parameterised over [0, 2π] in radians measured from the plane's x axis
/// towards its y axis.
/// </summary>
/// <remarks>
/// <para>
/// A circle's speed is constant and equal to its radius, so its parameter is proportional to arc
/// length: the point at parameter <c>t</c> is the point at distance <c>radius × t</c> along it.
/// That makes <see cref="Curve.DivideEqually(int)"/> exact rather than iterative, and it is why
/// <see cref="ParameterAtLength(double)"/> is overridden here.
/// </para>
/// <para>
/// Trimming a circle produces an <see cref="Arc"/>, not a shorter circle. That is the one place in
/// this slice where an operation changes the type of the thing it is given, and it is right: a
/// circle that is not closed is not a circle.
/// </para>
/// </remarks>
public sealed class Circle : Curve
{
    private readonly Plane _plane;
    private readonly double _radius;

    /// <summary>Creates a circle in a plane.</summary>
    /// <param name="plane">The plane. Its origin is the circle's center.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="radius"/> is not positive and finite.
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
                nameof(radius), radius, "A circle's radius must be positive and finite.");
        }

        _plane = plane;
        _radius = radius;
    }

    /// <summary>Lifts an instance's state into a new one, so a constructor can call a factory.</summary>
    /// <param name="other">The instance to copy. Already validated by whatever produced it.</param>
    /// <remarks>
    /// <b>`E2-T59` asked for a constructor beside every library factory, and a class constructor
    /// cannot return.</b> This is what lets the public ones below read <c>: this(SomeFactory(x))</c>.
    /// The arithmetic stays in the factory, which remains its only copy, so the constructor cannot
    /// drift away from the method it mirrors.
    /// </remarks>
    private Circle(Circle other)
        : this(other._plane, other._radius)
    {
    }

    /// <summary>Creates a circle in the plane through its center parallel to the world xy plane.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the radius is not positive.</exception>
    /// <remarks>Forwards to <see cref="FromCenterRadius"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Circle(in Point3d center, double radius)
        : this(FromCenterRadius(center, radius))
    {
    }

    /// <summary>Creates a circle about an axis.</summary>
    /// <param name="center">The center.</param>
    /// <param name="normal">The axis the circle turns about. Need not be unit length.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the radius is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when the normal has no length.</exception>
    /// <remarks>Forwards to <see cref="FromCenterNormalRadius"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Circle(in Point3d center, in Vector3d normal, double radius)
        : this(FromCenterNormalRadius(center, normal, radius))
    {
    }

    /// <summary>Creates the circle through three points.</summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <param name="third">The third point.</param>
    /// <exception cref="ArgumentException">Thrown when the points are collinear or coincident.</exception>
    /// <remarks>Forwards to <see cref="FromThreePoints"/>, so the two cannot drift apart (`E2-T59`).</remarks>
    public Circle(in Point3d first, in Point3d second, in Point3d third)
        : this(FromThreePoints(first, second, third))
    {
    }

    /// <inheritdoc/>
    public override Interval Domain => new(0.0, Math.PI * 2.0);

    /// <inheritdoc/>
    public override bool IsClosed => true;

    /// <summary>The plane the circle lies in. Its origin is the center.</summary>
    public Plane Plane => _plane;

    /// <summary>The center.</summary>
    public Point3d Center => _plane.Origin;

    /// <summary>The radius.</summary>
    public double Radius => _radius;

    /// <summary>Creates a circle in a plane.</summary>
    /// <param name="plane">The plane. Its origin is the circle's center.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <returns>The circle.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="radius"/> is not positive and finite.
    /// </exception>
    public static Circle FromPlaneRadius(in Plane plane, double radius) => new(plane, radius);

    /// <summary>Creates a circle in the world xy plane.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <returns>The circle.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="center"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="radius"/> is not positive and finite.
    /// </exception>
    public static Circle FromCenterRadius(in Point3d center, double radius) =>
        new(Plane.FromOriginNormal(center, Vector3d.ZAxis), radius);

    /// <summary>Creates a circle in the plane defined by a center and a normal.</summary>
    /// <param name="center">The center.</param>
    /// <param name="normal">The circle's normal. Need not be normalised.</param>
    /// <param name="radius">The radius. Must be positive and finite.</param>
    /// <returns>The circle.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="center"/> is not finite, or <paramref name="normal"/> is
    /// zero-length or not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="radius"/> is not positive and finite.
    /// </exception>
    public static Circle FromCenterNormalRadius(
        in Point3d center, in Vector3d normal, double radius) =>
        new(Plane.FromOriginNormal(center, normal), radius);

    /// <summary>Creates the circle through three points.</summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <param name="third">The third point.</param>
    /// <returns>The circle, in the plane of the three points.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the three points are collinear or coincident, so no circle passes through them.
    /// </exception>
    public static Circle FromThreePoints(
        in Point3d first, in Point3d second, in Point3d third)
    {
        (Plane plane, double radius) = Arc.Circumcircle(first, second, third);
        return new Circle(plane, radius);
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

        return Math.Clamp(distance / _radius, 0.0, Math.PI * 2.0);
    }

    /// <inheritdoc/>
    public override Curve Reversed() =>
        new Circle(Plane.FromOriginXAxisYAxis(_plane.Origin, _plane.XAxis, -_plane.YAxis), _radius);

    /// <summary>Returns the arc between two parameters on the circle.</summary>
    /// <param name="domain">
    /// The sub-domain to keep, in radians. Because a circle is closed, this may start anywhere and
    /// may wrap past the seam at 2π; a decreasing interval sweeps the other way round.
    /// </param>
    /// <returns>An <see cref="Arc"/>. The circle is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="domain"/> is not finite or has zero length.
    /// </exception>
    public override Curve Trimmed(in Interval domain)
    {
        if (!double.IsFinite(domain.Min) || !double.IsFinite(domain.Max) || domain.Length == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(domain), domain, "A trim domain must be finite and of non-zero length.");
        }

        double sweep = Math.Min(Math.Abs(domain.Length), Math.PI * 2.0);
        return domain.Length > 0.0
            ? Arc.FromPlaneRadiusAngles(_plane, _radius, Angle.FromRadians(domain.Min), Angle.FromRadians(sweep))
            : Arc.FromPlaneRadiusAngles(_plane, _radius, Angle.FromRadians(domain.Min), Angle.FromRadians(-sweep));
    }

    /// <inheritdoc/>
    public override Curve TransformedBy(in Transform transform)
    {
        Plane plane = CircularArcs.TransformFrame(transform, _plane, out double scale);
        return new Circle(plane, _radius * scale);
    }

    /// <summary>A readable description of the circle, for diagnostics.</summary>
    /// <returns>The center and radius.</returns>
    public override string ToString() => $"Circle(center {Center}, radius {_radius})";

    /// <inheritdoc/>
    protected override int TessellationSeedSpans => 4;

    /// <inheritdoc/>
    protected override double ComputeLength() => Math.PI * 2.0 * _radius;

    /// <inheritdoc/>
    protected override BoundingBox ComputeBoundingBox() =>
        CircularArcs.Bounds(_plane, _radius, _radius, 0.0, Math.PI * 2.0);

    /// <inheritdoc/>
    protected override Point3d Evaluate(double parameter) =>
        CircularArcs.PointAt(_plane, _radius, _radius, parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateDerivative(double parameter) =>
        CircularArcs.DerivativeAt(_plane, _radius, _radius, parameter);

    /// <inheritdoc/>
    protected override Vector3d EvaluateSecondDerivative(double parameter) =>
        CircularArcs.SecondDerivativeAt(_plane, _radius, _radius, parameter);
}

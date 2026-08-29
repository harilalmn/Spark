using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A half-line: a point and a direction, going one way and not the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>The direction is always unit length</b>, normalised on construction, which is what makes
/// the parameter along a ray a <b>distance</b> rather than a multiple of whatever length the
/// caller happened to pass in. Every member that returns a parameter returns a distance in the
/// same units as the coordinates, and <see cref="PointAt(double)"/> reads the same way.
/// </para>
/// <para>
/// <b>A ray is not a line and not a segment.</b> It starts at <see cref="Origin"/> and has no
/// end. Negative parameters are behind it and are excluded from every intersection here, which
/// is the difference that matters for picking: a click does not select what is behind the
/// camera.
/// </para>
/// <para>
/// <c>default(Ray)</c> has a zero direction and is not a ray. Every geometric member throws
/// <see cref="InvalidOperationException"/> on it, as <see cref="Plane"/> and
/// <see cref="Quaternion"/> do.
/// </para>
/// </remarks>
public readonly struct Ray : IEquatable<Ray>
{
    /// <summary>
    /// Creates a ray from a starting point and a direction.
    /// </summary>
    /// <param name="origin">Where the ray starts.</param>
    /// <param name="direction">
    /// Which way it goes. Need not be normalised; it is stored normalised, so the length passed
    /// in does not change what any parameter means.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="origin"/> is not finite, or when
    /// <paramref name="direction"/> is zero-length or non-finite.
    /// </exception>
    public Ray(in Point3d origin, in Vector3d direction)
    {
        if (!origin.IsValid)
        {
            throw new ArgumentException("A ray's origin must be finite.", nameof(origin));
        }

        if (!direction.TryNormalise(out Vector3d unit))
        {
            throw new ArgumentException(
                "A ray's direction must have non-zero length and finite components.",
                nameof(direction));
        }

        Origin = origin;
        Direction = unit;
    }

    /// <summary>Where the ray starts.</summary>
    public Point3d Origin { get; }

    /// <summary>The unit direction the ray travels in.</summary>
    public Vector3d Direction { get; }

    /// <summary>
    /// <see langword="true"/> when this value is a ray, which a default-constructed one is not.
    /// </summary>
    public bool IsValid => Origin.IsValid && Direction.IsValid && Direction.LengthSquared > 0.0;

    /// <summary>
    /// Creates the ray from one point towards another.
    /// </summary>
    /// <param name="from">Where the ray starts.</param>
    /// <param name="towards">A point the ray passes through.</param>
    /// <returns>The ray.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is not finite, or when they are the same point and therefore
    /// give no direction.
    /// </exception>
    public static Ray ByTwoPoints(in Point3d from, in Point3d towards) => new(from, towards - from);

    /// <summary>
    /// The point a given distance along the ray.
    /// </summary>
    /// <param name="distance">
    /// How far along, in coordinate units. Negative values are behind the origin: this returns
    /// the point rather than refusing, because the arithmetic is meaningful even where the ray
    /// is not — every *intersection* member excludes them instead.
    /// </param>
    /// <returns>The point.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this value is not a ray.</exception>
    public Point3d PointAt(double distance)
    {
        ThrowIfInvalid();

        return Origin + (Direction * distance);
    }

    /// <summary>
    /// Finds where this ray enters and leaves a box.
    /// </summary>
    /// <param name="box">The box to test against.</param>
    /// <param name="entry">
    /// On a hit, the distance at which the ray enters the box, clamped to zero when the origin
    /// is already inside it.
    /// </param>
    /// <param name="exit">On a hit, the distance at which it leaves.</param>
    /// <returns>
    /// <see langword="true"/> when the ray meets the box at a non-negative distance.
    /// <see langword="false"/> when it misses, when the box lies entirely behind the origin, or
    /// when the box is not <see cref="BoundingBox.IsValid"/> — which covers
    /// <see cref="BoundingBox.Empty"/>.
    /// </returns>
    /// <remarks>
    /// The slab test, and the reason it is written with divisions rather than with a branch per
    /// axis is that IEEE division by zero is exactly right here: a ray parallel to an axis gets
    /// ±∞ for that slab, and the comparisons that follow do the correct thing with it. A
    /// hand-rolled *is this component zero* branch is where implementations of this test
    /// usually go wrong, because the answer also has to be right when the origin lies exactly
    /// on a face.
    /// </remarks>
    public bool Intersects(in BoundingBox box, out double entry, out double exit)
    {
        ThrowIfInvalid();

        entry = 0.0;
        exit = 0.0;

        if (!box.IsValid)
        {
            return false;
        }

        double near = 0.0;
        double far = double.PositiveInfinity;

        if (!Slab(Origin.X, Direction.X, box.Min.X, box.Max.X, ref near, ref far)
            || !Slab(Origin.Y, Direction.Y, box.Min.Y, box.Max.Y, ref near, ref far)
            || !Slab(Origin.Z, Direction.Z, box.Min.Z, box.Max.Z, ref near, ref far))
        {
            return false;
        }

        entry = near;
        exit = far;
        return true;
    }

    /// <summary>
    /// Tests whether this ray meets a box at all.
    /// </summary>
    /// <param name="box">The box to test against.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this value is not a ray.</exception>
    public bool Intersects(in BoundingBox box) => Intersects(box, out _, out _);

    /// <summary>
    /// The distance from a point to this ray, and where along the ray the closest approach is.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>
    /// The closest point on the ray and its distance parameter. **The parameter is clamped to
    /// zero**, so a point behind the origin returns the origin — which is what makes this a ray
    /// query rather than a line query.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when this value is not a ray.</exception>
    public (Point3d Point, double Distance) ClosestPointTo(in Point3d point)
    {
        ThrowIfInvalid();

        double along = Math.Max(0.0, (point - Origin).Dot(Direction));

        return (PointAt(along), along);
    }

    /// <summary>
    /// Tests whether this ray and another have the same origin and direction within a tolerance.
    /// </summary>
    /// <param name="other">The ray to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when both agree.</returns>
    public bool EqualsWithin(in Ray other, in Tolerance tolerance = default) =>
        Origin.EqualsWithin(other.Origin, tolerance)
        && Direction.EqualsWithin(other.Direction, tolerance);

    /// <summary>Exact equality of origin and stored direction.</summary>
    /// <param name="left">The first ray.</param>
    /// <param name="right">The second ray.</param>
    /// <returns><see langword="true"/> when both are exactly equal.</returns>
    public static bool operator ==(in Ray left, in Ray right) =>
        left.Origin == right.Origin && left.Direction == right.Direction;

    /// <summary>Exact inequality.</summary>
    /// <param name="left">The first ray.</param>
    /// <param name="right">The second ray.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(in Ray left, in Ray right) => !(left == right);

    /// <inheritdoc/>
    public bool Equals(Ray other) => Origin.Equals(other.Origin) && Direction.Equals(other.Direction);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Ray other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Origin, Direction);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Ray(Origin={Origin}, Direction={Direction})");

    private static bool Slab(double origin, double direction, double min, double max, ref double near, ref double far)
    {
        double inverse = 1.0 / direction;
        double first = (min - origin) * inverse;
        double second = (max - origin) * inverse;

        if (first > second)
        {
            (first, second) = (second, first);
        }

        // NaN arises when the origin sits exactly on a slab plane and the direction is parallel
        // to it: 0 * infinity. That case is a hit on this axis, not a miss, so the NaN must not
        // be allowed to narrow the interval.
        if (!double.IsNaN(first))
        {
            near = Math.Max(near, first);
        }

        if (!double.IsNaN(second))
        {
            far = Math.Min(far, second);
        }

        return near <= far;
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default-constructed Ray has no origin and no direction, so nothing can be "
                + "asked of it. Construct one from a point and a direction.");
        }
    }
}

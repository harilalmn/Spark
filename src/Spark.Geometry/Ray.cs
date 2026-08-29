using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A half-infinite line: an origin and a unit direction, extending forwards only.
/// </summary>
/// <remarks>
/// <para>
/// A ray is the query, not the geometry. It is what a picking gesture, a containment test and
/// an intersection seed are all expressed as, which is why it lives in the kernel rather than
/// in the viewport that first needed it.
/// </para>
/// <para>
/// <b>The direction is normalised on construction and the origin is not moved.</b> That makes
/// the ray parameter a distance in the same units as the coordinates — <c>PointAt(3.0)</c> is
/// three units along — and it is what lets <see cref="Bvh{T}"/> compare a hit distance against
/// a nearest-so-far without either side knowing how the ray was built. An unnormalised
/// direction would make every one of those comparisons wrong by a factor nobody was tracking.
/// </para>
/// <para>
/// <b>A ray is not a line and does not extend backwards.</b> Everything here that returns a
/// parameter returns a non-negative one, and a box entirely behind the origin is a miss rather
/// than a negative hit. Where the two-sided answer is wanted, the caller has an infinite line
/// and should say so; conflating the two is how a picking ray selects the object behind the
/// camera.
/// </para>
/// </remarks>
public readonly struct Ray : IEquatable<Ray>
{
    /// <summary>
    /// Creates a ray from a starting point and a direction.
    /// </summary>
    /// <param name="origin">Where the ray starts.</param>
    /// <param name="direction">
    /// Which way it points. Need not be normalised; it is stored normalised.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="origin"/> is not finite, or when
    /// <paramref name="direction"/> is zero-length or not finite.
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

    /// <summary>Which way it points, always a unit vector.</summary>
    public Vector3d Direction { get; }

    /// <summary>
    /// Whether this value denotes a ray.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> for a default-constructed <see cref="Ray"/>, whose direction is
    /// the zero vector and which therefore points nowhere.
    /// </returns>
    public bool IsValid => Origin.IsValid && Direction.LengthSquared > 0.0;

    /// <summary>
    /// Creates the ray from one point towards another.
    /// </summary>
    /// <param name="from">The origin.</param>
    /// <param name="towards">A point the ray passes through.</param>
    /// <returns>The ray.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either point is not finite, or when they are the same point and therefore
    /// name no direction.
    /// </exception>
    public static Ray ByTwoPoints(in Point3d from, in Point3d towards)
    {
        if (!from.IsValid)
        {
            throw new ArgumentException("A ray's origin must be finite.", nameof(from));
        }

        if (!towards.IsValid)
        {
            throw new ArgumentException("A ray's target must be finite.", nameof(towards));
        }

        if (from == towards)
        {
            throw new ArgumentException(
                "The two points are the same, so they name no direction.",
                nameof(towards));
        }

        return new Ray(from, towards - from);
    }

    /// <summary>
    /// The point a given distance along the ray.
    /// </summary>
    /// <param name="distance">
    /// How far along, in the same units as the coordinates, because the direction is a unit
    /// vector. Negative distances are behind the origin and are <b>not</b> on the ray; this
    /// member evaluates them anyway, because the arithmetic is well defined and the guards
    /// belong in the queries that decide what counts as a hit.
    /// </param>
    /// <returns>The point.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this ray is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Point3d PointAt(double distance)
    {
        ThrowIfInvalid();

        return Origin + (Direction * distance);
    }

    /// <summary>
    /// The point on the ray closest to a given point.
    /// </summary>
    /// <param name="point">The point to approach.</param>
    /// <returns>
    /// The foot of the perpendicular, or <see cref="Origin"/> when that foot lies behind the
    /// origin. <b>The clamp is what distinguishes this from the same query on an infinite
    /// line</b>, and it is the whole reason a ray is a separate type.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this ray is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Point3d ClosestPoint(in Point3d point)
    {
        ThrowIfInvalid();

        return PointAt(Math.Max(0.0, (point - Origin).Dot(Direction)));
    }

    /// <summary>
    /// The distance from a point to the nearest point of the ray.
    /// </summary>
    /// <param name="point">The point to measure from.</param>
    /// <returns>The distance, never negative.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this ray is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public double DistanceTo(in Point3d point) => ClosestPoint(point).DistanceTo(point);

    /// <summary>
    /// Finds where, if anywhere, this ray passes through an axis-aligned box.
    /// </summary>
    /// <param name="box">The box to test.</param>
    /// <param name="span">
    /// On a hit, the interval of ray distances inside the box. <c>Min</c> is zero when the
    /// origin is already inside, which is the case a caller most often gets wrong: an
    /// entry distance of zero is a hit, not a miss.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the ray meets the box at a non-negative distance. A box
    /// entirely behind the origin is a miss. A box the ray only grazes — meeting it in a face,
    /// an edge or a corner — is a hit, because the alternative is a picking ray that fails on
    /// exactly the alignments a user is most likely to set up deliberately.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this ray is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    /// <remarks>
    /// The slab method, with the zero-direction case branched rather than divided through.
    /// The branchless form divides by a component of zero on purpose and relies on the
    /// resulting infinities cancelling — except that an origin lying exactly on a slab plane
    /// makes the product <c>0 × ∞</c>, which is <see cref="double.NaN"/>, and every subsequent
    /// comparison against it is false. That is a miss reported for a ray lying in the plane of
    /// a face, which is the single most common alignment in axis-aligned work.
    /// </remarks>
    public bool TryIntersect(in BoundingBox box, out Interval span)
    {
        ThrowIfInvalid();

        span = default;

        if (!box.IsValid)
        {
            return false;
        }

        double enter = 0.0;
        double exit = double.PositiveInfinity;

        if (!Clip(Origin.X, Direction.X, box.Min.X, box.Max.X, ref enter, ref exit)
            || !Clip(Origin.Y, Direction.Y, box.Min.Y, box.Max.Y, ref enter, ref exit)
            || !Clip(Origin.Z, Direction.Z, box.Min.Z, box.Max.Z, ref enter, ref exit))
        {
            return false;
        }

        span = new Interval(enter, exit);
        return true;
    }

    /// <summary>
    /// Tests whether this ray meets a box at all.
    /// </summary>
    /// <param name="box">The box to test.</param>
    /// <returns><see langword="true"/> when the ray meets the box at a non-negative distance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this ray is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public bool Intersects(in BoundingBox box) => TryIntersect(box, out _);

    /// <summary>
    /// Returns this ray moved by a transform.
    /// </summary>
    /// <param name="transform">The transform to apply.</param>
    /// <returns>
    /// The transformed ray. The direction is transformed as a vector and re-normalised, so a
    /// scaling transform changes where <c>PointAt(3.0)</c> lands but not which points the ray
    /// passes through.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this ray is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the transform collapses the direction to zero length, which a projection or
    /// a zero scale does.
    /// </exception>
    public Ray TransformedBy(in Transform transform)
    {
        ThrowIfInvalid();

        Vector3d direction = transform.OfVector(Direction);

        if (direction.LengthSquared == 0.0 || !direction.IsValid)
        {
            throw new ArgumentException(
                "This transform collapses the ray's direction, leaving no ray.",
                nameof(transform));
        }

        return new Ray(transform.OfPoint(Origin), direction);
    }

    /// <summary>
    /// Compares two rays componentwise within a tolerance.
    /// </summary>
    /// <param name="other">The ray to compare against.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the origins and directions agree. <b>Two rays tracing the
    /// same half-line from different origins are not equal by this test</b>, because they are
    /// not the same ray: they disagree about every distance.
    /// </returns>
    public bool EqualsWithin(in Ray other, in Tolerance tolerance = default) =>
        Origin.EqualsWithin(other.Origin, tolerance)
        && Direction.EqualsWithin(other.Direction, tolerance);

    /// <summary>
    /// Exact componentwise equality, following IEEE 754.
    /// </summary>
    /// <param name="left">The first ray.</param>
    /// <param name="right">The second ray.</param>
    /// <returns><see langword="true"/> when origin and direction are exactly equal.</returns>
    public static bool operator ==(in Ray left, in Ray right) =>
        left.Origin == right.Origin && left.Direction == right.Direction;

    /// <summary>
    /// The negation of <c>operator ==</c>.
    /// </summary>
    /// <param name="left">The first ray.</param>
    /// <param name="right">The second ray.</param>
    /// <returns><see langword="true"/> when the two are not exactly equal.</returns>
    public static bool operator !=(in Ray left, in Ray right) => !(left == right);

    /// <summary>
    /// Componentwise equality treating <see cref="double.NaN"/> as equal to itself.
    /// </summary>
    /// <param name="other">The ray to compare against.</param>
    /// <returns><see langword="true"/> when origin and direction are equal.</returns>
    public bool Equals(Ray other) => Origin.Equals(other.Origin) && Direction.Equals(other.Direction);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Ray other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Origin, Direction);

    /// <summary>
    /// Formats the origin and direction, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>(0, 0, 0) → (0, 0, 1)</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Origin} → {Direction}");

    // One slab of the box, clipping the surviving [enter, exit] interval. Returns false as
    // soon as the interval is empty, which is what lets the caller stop after one axis.
    private static bool Clip(
        double origin,
        double direction,
        double min,
        double max,
        ref double enter,
        ref double exit)
    {
        if (direction == 0.0)
        {
            // Parallel to this pair of planes: either the ray is between them for its whole
            // length or it never meets the box at all. No division, and therefore no NaN.
            return origin >= min && origin <= max;
        }

        double first = (min - origin) / direction;
        double second = (max - origin) / direction;

        if (first > second)
        {
            (first, second) = (second, first);
        }

        enter = Math.Max(enter, first);
        exit = Math.Min(exit, second);

        return enter <= exit;
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default-constructed Ray has no direction and answers no questions.");
        }
    }
}

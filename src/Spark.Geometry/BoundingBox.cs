using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// An axis-aligned box in three-dimensional space, given by its lowest and highest corner.
/// </summary>
/// <remarks>
/// <para>
/// This box is genuinely three-dimensional. The Z extent is carried and used by every
/// member — the seed library's bounding box silently ignored Z, which is safe in a drawing
/// library and wrong in a kernel.
/// </para>
/// <para>
/// The public constructor sorts its two corners component-wise, so
/// <c>new BoundingBox(a, b)</c> is well defined for any two opposite corners in any order.
/// The one box that cannot be produced that way is <see cref="Empty"/>, which is inverted on
/// purpose so that it acts as the identity for <see cref="Union(in BoundingBox)"/>. Seed
/// accumulations with <see cref="Empty"/>, not with <c>default</c> — a default-constructed
/// box is the zero-size box at the origin, which is a real box and would drag every union
/// back to the origin.
/// </para>
/// </remarks>
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    /// <summary>
    /// Creates the smallest axis-aligned box containing two corner points.
    /// </summary>
    /// <param name="corner">One corner of the box.</param>
    /// <param name="oppositeCorner">
    /// The opposite corner. The two corners are sorted component-wise, so their order does
    /// not matter and neither does which one happens to be lower on any given axis.
    /// </param>
    /// <remarks>
    /// If either corner holds a <see cref="double.NaN"/>, the resulting box is not valid and
    /// <see cref="IsValid"/> reports that; no exception is thrown, because a NaN coordinate
    /// almost always originates far upstream and throwing here would report the wrong cause.
    /// </remarks>
    public BoundingBox(in Point3d corner, in Point3d oppositeCorner)
    {
        Min = new Point3d(
            Math.Min(corner.X, oppositeCorner.X),
            Math.Min(corner.Y, oppositeCorner.Y),
            Math.Min(corner.Z, oppositeCorner.Z));

        Max = new Point3d(
            Math.Max(corner.X, oppositeCorner.X),
            Math.Max(corner.Y, oppositeCorner.Y),
            Math.Max(corner.Z, oppositeCorner.Z));
    }

    // Builds a box from bounds that are already known to be in the right order, which is the
    // only way to produce the deliberately inverted Empty box.
    private BoundingBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Min = new Point3d(minX, minY, minZ);
        Max = new Point3d(maxX, maxY, maxZ);
    }

    /// <summary>
    /// The corner with the lowest coordinate on every axis. For <see cref="Empty"/> this is
    /// positive infinity on every axis.
    /// </summary>
    public Point3d Min { get; }

    /// <summary>
    /// The corner with the highest coordinate on every axis. For <see cref="Empty"/> this is
    /// negative infinity on every axis.
    /// </summary>
    public Point3d Max { get; }

    /// <summary>
    /// The inverted infinite box: <see cref="Min"/> at positive infinity and
    /// <see cref="Max"/> at negative infinity. It contains nothing, is not
    /// <see cref="IsValid"/>, and is the identity for <see cref="Union(in BoundingBox)"/>,
    /// which makes it the correct seed for accumulating a box over a sequence.
    /// </summary>
    public static BoundingBox Empty => new(
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity);

    /// <summary>
    /// <see langword="true"/> when both corners are finite and <see cref="Min"/> is not above
    /// <see cref="Max"/> on any axis. A box of zero size on one or more axes — a rectangle, a
    /// segment or a point — is valid.
    /// </summary>
    public bool IsValid =>
        Min.IsValid
        && Max.IsValid
        && Min.X <= Max.X
        && Min.Y <= Max.Y
        && Min.Z <= Max.Z;

    /// <summary>
    /// The point at the centre of the box. Meaningless for an invalid box.
    /// </summary>
    public Point3d Centre => Min.Midpoint(Max);

    /// <summary>
    /// The vector from <see cref="Min"/> to <see cref="Max"/>. Its components are the extents
    /// of the box on each axis, and its length is the length of the box's space diagonal —
    /// the usual choice of characteristic length for
    /// <see cref="Tolerance.ForScale(double)"/>.
    /// </summary>
    public Vector3d Diagonal => Max - Min;

    /// <summary>
    /// The volume enclosed by the box, or zero when the box is not valid.
    /// </summary>
    public double Volume
    {
        get
        {
            if (!IsValid)
            {
                return 0.0;
            }

            Vector3d size = Diagonal;

            return size.X * size.Y * size.Z;
        }
    }

    /// <summary>
    /// The total surface area of the box's six faces, or zero when the box is not valid. A
    /// flat box counts both of its coincident faces, so a unit square in the XY plane has an
    /// area of two.
    /// </summary>
    public double Area
    {
        get
        {
            if (!IsValid)
            {
                return 0.0;
            }

            Vector3d size = Diagonal;

            return 2.0 * ((size.X * size.Y) + (size.Y * size.Z) + (size.Z * size.X));
        }
    }

    /// <summary>
    /// Builds the smallest box containing every point in a sequence.
    /// </summary>
    /// <param name="points">The points to bound.</param>
    /// <returns>
    /// The bounding box of the points, or <see cref="Empty"/> when the sequence contains no
    /// points.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="points"/> is <see langword="null"/>.
    /// </exception>
    public static BoundingBox FromPoints(IEnumerable<Point3d> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        BoundingBox result = Empty;

        foreach (Point3d point in points)
        {
            result = result.Union(point);
        }

        return result;
    }

    /// <summary>
    /// Builds the smallest box containing every point in a span.
    /// </summary>
    /// <param name="points">The points to bound.</param>
    /// <returns>
    /// The bounding box of the points, or <see cref="Empty"/> when the span is empty.
    /// </returns>
    public static BoundingBox FromPoints(ReadOnlySpan<Point3d> points)
    {
        BoundingBox result = Empty;

        foreach (Point3d point in points)
        {
            result = result.Union(point);
        }

        return result;
    }

    /// <summary>
    /// Tests whether a point lies inside the box.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> and
    /// <see cref="Tolerance.RelativeEpsilon"/> are consulted. A default-constructed tolerance
    /// means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the point is inside or on the boundary, with the boundary
    /// widened outwards by the tolerance. <see cref="Empty"/> contains nothing, and an unset
    /// or non-finite point is contained by nothing.
    /// </returns>
    public bool Contains(in Point3d point, in Tolerance tolerance = default) =>
        point.IsValid
        && !HasNaN
        && !tolerance.IsLessThan(point.X, Min.X) && !tolerance.IsGreaterThan(point.X, Max.X)
        && !tolerance.IsLessThan(point.Y, Min.Y) && !tolerance.IsGreaterThan(point.Y, Max.Y)
        && !tolerance.IsLessThan(point.Z, Min.Z) && !tolerance.IsGreaterThan(point.Z, Max.Z);

    /// <summary>
    /// Tests whether another box lies entirely inside this one.
    /// </summary>
    /// <param name="other">The box to test.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both corners of <paramref name="other"/> are contained,
    /// with the boundary widened outwards by the tolerance. Every box, including
    /// <see cref="Empty"/>, contains <see cref="Empty"/>.
    /// </returns>
    public bool Contains(in BoundingBox other, in Tolerance tolerance = default)
    {
        if (HasNaN || other.HasNaN)
        {
            return false;
        }

        if (other.Min.X > other.Max.X || other.Min.Y > other.Max.Y || other.Min.Z > other.Max.Z)
        {
            // An inverted box, Empty included, occupies no space, so it is inside everything.
            return true;
        }

        return Contains(other.Min, tolerance) && Contains(other.Max, tolerance);
    }

    /// <summary>
    /// Tests whether this box and another share any space.
    /// </summary>
    /// <param name="other">The box to test against.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two boxes overlap or touch, with the touching test
    /// widened by the tolerance. <see cref="Empty"/> intersects nothing, including itself,
    /// and a box with a <see cref="double.NaN"/> corner intersects nothing in either operand
    /// position.
    /// </returns>
    /// <remarks>
    /// The <see cref="double.NaN"/> guard is explicit, for the same reason
    /// <see cref="Contains(in Point3d, in Tolerance)"/> guards its argument: this test is
    /// built from negated predicates, every comparison against a <see cref="double.NaN"/> is
    /// false, and so an unguarded version reports a meaningless box as overlapping a real
    /// one. That is what it used to do, while <c>Contains</c> on the same pair correctly said
    /// no — two containment predicates in one assembly disagreeing about NaN.
    /// </remarks>
    public bool Intersects(in BoundingBox other, in Tolerance tolerance = default) =>
        !HasNaN
        && !other.HasNaN
        && !tolerance.IsGreaterThan(Min.X, other.Max.X) && !tolerance.IsLessThan(Max.X, other.Min.X)
        && !tolerance.IsGreaterThan(Min.Y, other.Max.Y) && !tolerance.IsLessThan(Max.Y, other.Min.Y)
        && !tolerance.IsGreaterThan(Min.Z, other.Max.Z) && !tolerance.IsLessThan(Max.Z, other.Min.Z);

    /// <summary>
    /// Returns the smallest box containing both this box and another.
    /// </summary>
    /// <param name="other">The box to combine with.</param>
    /// <returns>
    /// The union. Combining with <see cref="Empty"/> returns the other operand unchanged, in
    /// either order, which is what makes <see cref="Empty"/> a usable accumulation seed.
    /// </returns>
    public BoundingBox Union(in BoundingBox other) => new(
        Math.Min(Min.X, other.Min.X),
        Math.Min(Min.Y, other.Min.Y),
        Math.Min(Min.Z, other.Min.Z),
        Math.Max(Max.X, other.Max.X),
        Math.Max(Max.Y, other.Max.Y),
        Math.Max(Max.Z, other.Max.Z));

    /// <summary>
    /// Returns the smallest box containing both this box and a point.
    /// </summary>
    /// <param name="point">The point to include.</param>
    /// <returns>The expanded box.</returns>
    public BoundingBox Union(in Point3d point) => new(
        Math.Min(Min.X, point.X),
        Math.Min(Min.Y, point.Y),
        Math.Min(Min.Z, point.Z),
        Math.Max(Max.X, point.X),
        Math.Max(Max.Y, point.Y),
        Math.Max(Max.Z, point.Z));

    /// <summary>
    /// Returns the smallest box containing two boxes. The static alternate to
    /// <see cref="Union(in BoundingBox)"/>.
    /// </summary>
    /// <param name="a">The first box.</param>
    /// <param name="b">The second box.</param>
    /// <returns>The union, which does not depend on the order of the arguments.</returns>
    public static BoundingBox Union(in BoundingBox a, in BoundingBox b) => a.Union(b);

    /// <summary>
    /// Returns the box covered by both this box and another, or <see langword="null"/> when
    /// they do not overlap. The counterpart to <see cref="Union(in BoundingBox)"/>.
    /// </summary>
    /// <param name="other">The box to intersect with.</param>
    /// <param name="tolerance">
    /// The tolerance to use, matching <see cref="Intersects(in BoundingBox, in Tolerance)"/>
    /// so that the two agree about boxes that touch. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// The overlap, which may be flat, thin or a single point when the boxes only touch, or
    /// <see langword="null"/> when they miss each other on any axis. Returns
    /// <see langword="null"/> when either box is not <see cref="IsValid"/> — which covers
    /// <see cref="Empty"/> and any box with a <see cref="double.NaN"/> corner, and matches
    /// what <see cref="Intersects(in BoundingBox, in Tolerance)"/> answers for both.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each axis is delegated to <see cref="Interval.Intersection(in Interval, in Tolerance)"/>
    /// rather than reimplemented, which is what guarantees that a box and its three intervals
    /// cannot disagree about a boundary case. The tolerance rule therefore arrives here
    /// unchanged: boxes separated by a gap no wider than the tolerance count as touching, and
    /// the result on that axis is the single value at the middle of the gap.
    /// </para>
    /// <para>
    /// <see langword="null"/> rather than <see cref="Empty"/> is deliberate and is the same
    /// argument <see cref="Interval.Intersection(in Interval, in Tolerance)"/> makes: an
    /// empty result and a real degenerate one must not be the same value.
    /// </para>
    /// </remarks>
    public BoundingBox? Intersection(in BoundingBox other, in Tolerance tolerance = default)
    {
        if (!IsValid || !other.IsValid)
        {
            return null;
        }

        Interval? x = new Interval(Min.X, Max.X).Intersection(new Interval(other.Min.X, other.Max.X), tolerance);
        Interval? y = new Interval(Min.Y, Max.Y).Intersection(new Interval(other.Min.Y, other.Max.Y), tolerance);
        Interval? z = new Interval(Min.Z, Max.Z).Intersection(new Interval(other.Min.Z, other.Max.Z), tolerance);

        if (x is null || y is null || z is null)
        {
            return null;
        }

        return new BoundingBox(
            new Point3d(x.Value.Min, y.Value.Min, z.Value.Min),
            new Point3d(x.Value.Max, y.Value.Max, z.Value.Max));
    }

    /// <summary>
    /// Returns the box grown by the same amount on every axis, in both directions.
    /// </summary>
    /// <param name="amount">
    /// The distance to move each face outwards. A negative amount shrinks the box, and
    /// shrinking by more than half an extent inverts that axis and makes the box invalid
    /// rather than throwing.
    /// </param>
    /// <returns>The inflated box.</returns>
    public BoundingBox Inflated(double amount) => Inflated(amount, amount, amount);

    /// <summary>
    /// Returns the box grown by a separate amount on each axis, in both directions.
    /// </summary>
    /// <param name="x">The distance to move the two X faces outwards.</param>
    /// <param name="y">The distance to move the two Y faces outwards.</param>
    /// <param name="z">The distance to move the two Z faces outwards.</param>
    /// <returns>
    /// The inflated box. Negative amounts shrink, and shrinking past zero size on an axis
    /// inverts it and makes the box invalid rather than throwing.
    /// </returns>
    public BoundingBox Inflated(double x, double y, double z) => new(
        Min.X - x,
        Min.Y - y,
        Min.Z - z,
        Max.X + x,
        Max.Y + y,
        Max.Z + z);

    /// <summary>
    /// Returns the eight corners of the box.
    /// </summary>
    /// <returns>
    /// A newly allocated array of eight points. The first four are the face at
    /// <see cref="Min"/>'s Z, listed counter-clockwise when viewed from above — that is
    /// <c>(min, min)</c>, <c>(max, min)</c>, <c>(max, max)</c>, <c>(min, max)</c> in X and Y
    /// — and the last four are the same four in the same order at <see cref="Max"/>'s Z. The
    /// array is freshly built on every call, so callers may keep and mutate it.
    /// </returns>
    public Point3d[] Corners() =>
    [
        new Point3d(Min.X, Min.Y, Min.Z),
        new Point3d(Max.X, Min.Y, Min.Z),
        new Point3d(Max.X, Max.Y, Min.Z),
        new Point3d(Min.X, Max.Y, Min.Z),
        new Point3d(Min.X, Min.Y, Max.Z),
        new Point3d(Max.X, Min.Y, Max.Z),
        new Point3d(Max.X, Max.Y, Max.Z),
        new Point3d(Min.X, Max.Y, Max.Z),
    ];

    /// <summary>
    /// Returns the point of the box closest to a given point.
    /// </summary>
    /// <param name="point">The point to find the closest position to.</param>
    /// <returns>
    /// The given point itself when it is inside the box, and otherwise the nearest point on
    /// the box's surface. The result is meaningless for an invalid box.
    /// </returns>
    public Point3d ClosestPoint(in Point3d point) => new(
        Math.Min(Math.Max(point.X, Min.X), Max.X),
        Math.Min(Math.Max(point.Y, Min.Y), Max.Y),
        Math.Min(Math.Max(point.Z, Min.Z), Max.Z));

    /// <summary>
    /// Tests whether this box and another have the same corners within a tolerance.
    /// </summary>
    /// <param name="other">The box to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when both corners are coincident within tolerance.</returns>
    public bool EqualsWithin(in BoundingBox other, in Tolerance tolerance = default) =>
        Min.EqualsWithin(other.Min, tolerance) && Max.EqualsWithin(other.Max, tolerance);

    /// <summary>
    /// Compares two boxes for exact equality of both corners, following IEEE rules.
    /// </summary>
    /// <param name="left">The first box.</param>
    /// <param name="right">The second box.</param>
    /// <returns>
    /// <see langword="true"/> when both corners are exactly equal. Use
    /// <see cref="EqualsWithin(in BoundingBox, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(in BoundingBox left, in BoundingBox right) =>
        left.Min == right.Min && left.Max == right.Max;

    /// <summary>Compares two boxes for exact inequality.</summary>
    /// <param name="left">The first box.</param>
    /// <param name="right">The second box.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in BoundingBox left, in BoundingBox right) => !(left == right);

    /// <summary>
    /// Tests exact equality of both corners, treating <see cref="double.NaN"/> as equal to
    /// itself so that boxes remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The box to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when both corners are equal under
    /// <see cref="Point3d.Equals(Point3d)"/>.
    /// </returns>
    public bool Equals(BoundingBox other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BoundingBox other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>
    /// Formats the two corners, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>BoundingBox((0, 0, 0), (1, 1, 1))</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"BoundingBox({Min}, {Max})");

    // A box holding a NaN coordinate describes nothing. Every predicate below is built from
    // negated comparisons, and comparisons against NaN are false, so without this the
    // negations all come out true and the box would appear to contain and intersect
    // everything.
    private bool HasNaN =>
        double.IsNaN(Min.X) || double.IsNaN(Min.Y) || double.IsNaN(Min.Z)
        || double.IsNaN(Max.X) || double.IsNaN(Max.Y) || double.IsNaN(Max.Z);
}

using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A position in a two-dimensional plane. Used for planar work and, from M6, for BRep trim
/// curves expressed in a surface's UV space.
/// </summary>
/// <remarks>
/// As in three dimensions, a point is not a vector: the conversions between
/// <see cref="Point2d"/> and <see cref="Vector2d"/> are explicit in both directions.
/// </remarks>
public readonly struct Point2d : IEquatable<Point2d>
{
    /// <summary>
    /// Creates a point from its two coordinates.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    public Point2d(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The X coordinate.</summary>
    public double X { get; }

    /// <summary>The Y coordinate.</summary>
    public double Y { get; }

    /// <summary>
    /// The origin, <c>(0, 0)</c>. This is also the value of a default-constructed
    /// <see cref="Point2d"/>.
    /// </summary>
    public static Point2d Origin => new(0.0, 0.0);

    /// <summary>
    /// A point whose coordinates are both <see cref="double.NaN"/>, representing the absence
    /// of a position. Test for it with <see cref="IsValid"/>, never with <c>==</c>.
    /// </summary>
    public static Point2d Unset => new(double.NaN, double.NaN);

    /// <summary>
    /// <see langword="true"/> when both coordinates are finite. <see langword="false"/> for
    /// <see cref="Unset"/>.
    /// </summary>
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y);

    /// <summary>
    /// The straight-line distance from this point to another.
    /// </summary>
    /// <param name="other">The point to measure to.</param>
    /// <returns>The Euclidean distance, always non-negative.</returns>
    public double DistanceTo(in Point2d other) => Math.Sqrt(DistanceSquaredTo(other));

    /// <summary>
    /// The squared straight-line distance from this point to another.
    /// </summary>
    /// <param name="other">The point to measure to.</param>
    /// <returns>The squared Euclidean distance, which avoids a square root.</returns>
    public double DistanceSquaredTo(in Point2d other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;

        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// The point halfway between this point and another.
    /// </summary>
    /// <param name="other">The other point.</param>
    /// <returns>The midpoint.</returns>
    public Point2d Midpoint(in Point2d other) => new((X + other.X) * 0.5, (Y + other.Y) * 0.5);

    /// <summary>
    /// Interpolates between two points.
    /// </summary>
    /// <param name="start">The point returned at <paramref name="t"/> equal to zero.</param>
    /// <param name="end">The point returned at <paramref name="t"/> equal to one.</param>
    /// <param name="t">
    /// The interpolation parameter. Values outside <c>[0, 1]</c> are not clamped and
    /// extrapolate along the line through the two points.
    /// </param>
    /// <returns>The interpolated point.</returns>
    public static Point2d Lerp(in Point2d start, in Point2d end, double t) => new(
        start.X + ((end.X - start.X) * t),
        start.Y + ((end.Y - start.Y) * t));

    /// <summary>
    /// Tests whether this point and another are coincident within a tolerance.
    /// </summary>
    /// <param name="other">The point to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the distance between the two points is negligible at the
    /// scale of the point further from the origin, by
    /// <see cref="Tolerance.IsNegligible(double, double)"/> — the same scale-aware rule the
    /// rest of the value layer uses.
    /// </returns>
    public bool EqualsWithin(in Point2d other, in Tolerance tolerance = default) =>
        tolerance.IsNegligible(
            DistanceTo(other),
            Math.Max(((Vector2d)this).Length, ((Vector2d)other).Length));

    /// <summary>
    /// Subtracts one point from another, giving the vector between them.
    /// </summary>
    /// <param name="left">The point to subtract from — the head of the resulting vector.</param>
    /// <param name="right">The point to subtract — the tail of the resulting vector.</param>
    /// <returns>The displacement vector that carries <paramref name="right"/> to <paramref name="left"/>.</returns>
    public static Vector2d operator -(in Point2d left, in Point2d right) =>
        new(left.X - right.X, left.Y - right.Y);

    /// <summary>Translates a point by a vector.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The displacement to apply.</param>
    /// <returns>The translated point.</returns>
    public static Point2d operator +(in Point2d point, in Vector2d offset) =>
        new(point.X + offset.X, point.Y + offset.Y);

    /// <summary>Translates a point by the reverse of a vector.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The displacement to apply in reverse.</param>
    /// <returns>The translated point.</returns>
    public static Point2d operator -(in Point2d point, in Vector2d offset) =>
        new(point.X - offset.X, point.Y - offset.Y);

    /// <summary>
    /// Reinterprets a point as the vector from the origin to that point.
    /// </summary>
    /// <param name="point">The point to reinterpret.</param>
    /// <returns>A vector with the same components.</returns>
    /// <remarks>Explicit on purpose: a point and a vector behave differently under transformation.</remarks>
    public static explicit operator Vector2d(in Point2d point) => new(point.X, point.Y);

    /// <summary>
    /// Reinterprets a vector as the position reached by following it from the origin.
    /// </summary>
    /// <param name="vector">The vector to reinterpret.</param>
    /// <returns>A point with the same components.</returns>
    /// <remarks>Explicit on purpose; see the remarks on the opposite conversion.</remarks>
    public static explicit operator Point2d(in Vector2d vector) => new(vector.X, vector.Y);

    /// <summary>
    /// Subtracts one point from another. The named alternate to <c>operator -</c>.
    /// </summary>
    /// <param name="left">The head of the resulting vector.</param>
    /// <param name="right">The tail of the resulting vector.</param>
    /// <returns>The displacement vector between the two points.</returns>
    public static Vector2d Subtract(in Point2d left, in Point2d right) => left - right;

    /// <summary>Translates a point by a vector. The named alternate to <c>operator +</c>.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The displacement to apply.</param>
    /// <returns>The translated point.</returns>
    public static Point2d Add(in Point2d point, in Vector2d offset) => point + offset;

    /// <summary>
    /// Reinterprets this point as a vector from the origin. The named alternate to the
    /// explicit conversion.
    /// </summary>
    /// <returns>A vector with the same components.</returns>
    public Vector2d ToVector2d() => new(X, Y);

    /// <summary>
    /// Compares two points for exact component-wise equality, following IEEE rules.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>
    /// <see langword="true"/> when both coordinates are equal. <see cref="Unset"/> is not
    /// equal to itself under this operator. Use
    /// <see cref="EqualsWithin(in Point2d, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(in Point2d left, in Point2d right) =>
        left.X == right.X && left.Y == right.Y;

    /// <summary>Compares two points for exact inequality.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Point2d left, in Point2d right) => !(left == right);

    /// <summary>
    /// Tests exact component-wise equality, treating <see cref="double.NaN"/> as equal to
    /// itself so that points remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The point to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when both coordinates are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(Point2d other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Point2d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>
    /// Formats the coordinates, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>(1, 2)</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}

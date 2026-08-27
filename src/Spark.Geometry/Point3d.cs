using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A position in three-dimensional space. Coordinates are unitless and are interpreted in
/// Spark's right-handed coordinate system.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Point3d"/> is not a <see cref="Vector3d"/>. A point has a position and no
/// direction; a vector has a direction and no position. Translating a point moves it, while
/// translating a vector does nothing. The conversions between the two are therefore
/// <b>explicit</b>: writing <c>(Vector3d)point</c> is a deliberate statement that you want
/// the position interpreted as an offset from the origin.
/// </para>
/// <para>
/// The natural arithmetic is available and is type-safe: subtracting two points gives the
/// <see cref="Vector3d"/> between them, and adding a vector to a point gives another point.
/// There is deliberately no <c>point + point</c>, because the sum of two positions is not a
/// position — use <see cref="Lerp(in Point3d, in Point3d, double)"/> or
/// <see cref="Midpoint(in Point3d)"/> if an average is what you want.
/// </para>
/// </remarks>
public readonly struct Point3d : IEquatable<Point3d>
{
    /// <summary>
    /// Creates a point from its three coordinates.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="z">The Z coordinate.</param>
    public Point3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The X coordinate.</summary>
    public double X { get; }

    /// <summary>The Y coordinate.</summary>
    public double Y { get; }

    /// <summary>The Z coordinate.</summary>
    public double Z { get; }

    /// <summary>
    /// The world origin, <c>(0, 0, 0)</c>. This is also the value of a default-constructed
    /// <see cref="Point3d"/>, which is a deliberate choice: a default point is a real
    /// position at the origin, not a missing one. Use <see cref="Unset"/> when you need to
    /// represent the absence of a position.
    /// </summary>
    public static Point3d Origin => new(0.0, 0.0, 0.0);

    /// <summary>
    /// A point whose coordinates are all <see cref="double.NaN"/>, used to represent the
    /// absence of a position.
    /// </summary>
    /// <remarks>
    /// Because it is built from <see cref="double.NaN"/>, <c>Unset == Unset</c> is
    /// <see langword="false"/> — IEEE equality says nothing is equal to a NaN, including
    /// another NaN. Test for it with <see cref="IsValid"/>, never with <c>==</c>.
    /// <see cref="Equals(Point3d)"/> does return <see langword="true"/> for two unset points,
    /// following <see cref="double.Equals(double)"/>, so unset points still behave sanely as
    /// dictionary keys.
    /// </remarks>
    public static Point3d Unset => new(double.NaN, double.NaN, double.NaN);

    /// <summary>
    /// <see langword="true"/> when every coordinate is finite. <see langword="false"/> for
    /// <see cref="Unset"/> and for any point holding an infinity or a
    /// <see cref="double.NaN"/>.
    /// </summary>
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    /// <summary>
    /// The straight-line distance from this point to another.
    /// </summary>
    /// <param name="other">The point to measure to.</param>
    /// <returns>
    /// The Euclidean distance, always non-negative. Returns <see cref="double.NaN"/> when
    /// either point is unset.
    /// </returns>
    public double DistanceTo(in Point3d other) => Math.Sqrt(DistanceSquaredTo(other));

    /// <summary>
    /// The squared straight-line distance from this point to another.
    /// </summary>
    /// <param name="other">The point to measure to.</param>
    /// <returns>
    /// The squared Euclidean distance. Cheaper than <see cref="DistanceTo(in Point3d)"/> and
    /// sufficient whenever distances are only being compared with one another.
    /// </returns>
    public double DistanceSquaredTo(in Point3d other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        double dz = other.Z - Z;

        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>
    /// The point halfway between this point and another.
    /// </summary>
    /// <param name="other">The other point.</param>
    /// <returns>The midpoint. Equivalent to <c>Lerp(this, other, 0.5)</c>.</returns>
    public Point3d Midpoint(in Point3d other) => new(
        (X + other.X) * 0.5,
        (Y + other.Y) * 0.5,
        (Z + other.Z) * 0.5);

    /// <summary>
    /// Interpolates between two points.
    /// </summary>
    /// <param name="start">The point returned at <paramref name="t"/> equal to zero.</param>
    /// <param name="end">The point returned at <paramref name="t"/> equal to one.</param>
    /// <param name="t">
    /// The interpolation parameter. Values outside <c>[0, 1]</c> are <b>not</b> clamped and
    /// extrapolate along the line through the two points, which is usually what a caller
    /// wants and is never a silent surprise because it is documented here.
    /// </param>
    /// <returns>The interpolated point.</returns>
    public static Point3d Lerp(in Point3d start, in Point3d end, double t) => new(
        start.X + ((end.X - start.X) * t),
        start.Y + ((end.Y - start.Y) * t),
        start.Z + ((end.Z - start.Z) * t));

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
    /// <see cref="Tolerance.IsNegligible(double, double)"/>. This is a spherical test, not a
    /// per-component box test, so the answer does not depend on how the points are oriented
    /// relative to the axes, and it is scale-aware, so it does not degenerate into
    /// bit-equality at large coordinates. Returns <see langword="false"/> when either point
    /// is unset.
    /// </returns>
    public bool EqualsWithin(in Point3d other, in Tolerance tolerance = default) =>
        tolerance.IsNegligible(
            DistanceTo(other),
            Math.Max(((Vector3d)this).Length, ((Vector3d)other).Length));

    /// <summary>
    /// Subtracts one point from another, giving the vector between them.
    /// </summary>
    /// <param name="left">The point to subtract from — the head of the resulting vector.</param>
    /// <param name="right">The point to subtract — the tail of the resulting vector.</param>
    /// <returns>The displacement vector that carries <paramref name="right"/> to <paramref name="left"/>.</returns>
    public static Vector3d operator -(in Point3d left, in Point3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Translates a point by a vector.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The displacement to apply.</param>
    /// <returns>The translated point.</returns>
    public static Point3d operator +(in Point3d point, in Vector3d offset) =>
        new(point.X + offset.X, point.Y + offset.Y, point.Z + offset.Z);

    /// <summary>Translates a point by the reverse of a vector.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The displacement to apply in reverse.</param>
    /// <returns>The translated point.</returns>
    public static Point3d operator -(in Point3d point, in Vector3d offset) =>
        new(point.X - offset.X, point.Y - offset.Y, point.Z - offset.Z);

    /// <summary>
    /// Reinterprets a point as the vector from the world origin to that point.
    /// </summary>
    /// <param name="point">The point to reinterpret.</param>
    /// <returns>A vector with the same components.</returns>
    /// <remarks>
    /// Explicit on purpose. A point and a vector behave differently under transformation — a
    /// vector ignores translation — so an accidental conversion produces geometry that is
    /// wrong in a way that only shows up once something is moved.
    /// </remarks>
    public static explicit operator Vector3d(in Point3d point) => new(point.X, point.Y, point.Z);

    /// <summary>
    /// Reinterprets a vector as the position reached by following it from the world origin.
    /// </summary>
    /// <param name="vector">The vector to reinterpret.</param>
    /// <returns>A point with the same components.</returns>
    /// <remarks>Explicit on purpose; see the remarks on the opposite conversion.</remarks>
    public static explicit operator Point3d(in Vector3d vector) => new(vector.X, vector.Y, vector.Z);

    /// <summary>
    /// Subtracts one point from another. The named alternate to <c>operator -</c>.
    /// </summary>
    /// <param name="left">The head of the resulting vector.</param>
    /// <param name="right">The tail of the resulting vector.</param>
    /// <returns>The displacement vector between the two points.</returns>
    public static Vector3d Subtract(in Point3d left, in Point3d right) => left - right;

    /// <summary>Translates a point by a vector. The named alternate to <c>operator +</c>.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">The displacement to apply.</param>
    /// <returns>The translated point.</returns>
    public static Point3d Add(in Point3d point, in Vector3d offset) => point + offset;

    /// <summary>
    /// Reinterprets a point as a vector from the world origin. The named alternate to the
    /// explicit conversion.
    /// </summary>
    /// <returns>A vector with the same components.</returns>
    public Vector3d ToVector3d() => new(X, Y, Z);

    /// <summary>
    /// Compares two points for exact component-wise equality.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>
    /// <see langword="true"/> when all three coordinates are equal under IEEE equality. This
    /// is exact and follows IEEE rules, so <see cref="Unset"/> is not equal to itself. Use
    /// <see cref="EqualsWithin(in Point3d, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(in Point3d left, in Point3d right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    /// <summary>Compares two points for exact inequality.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Point3d left, in Point3d right) => !(left == right);

    /// <summary>
    /// Tests exact component-wise equality, treating <see cref="double.NaN"/> as equal to
    /// itself so that points — including <see cref="Unset"/> — remain usable as dictionary
    /// keys. This differs from <c>operator ==</c> in exactly the way, and for exactly the
    /// reason, that <see cref="double.Equals(double)"/> differs from <c>==</c>.
    /// </summary>
    /// <param name="other">The point to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when all three coordinates are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(Point3d other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Point3d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>
    /// Formats the coordinates, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>(1, 2, 3)</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");
}

using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Creates a point from cylindrical coordinates. Forwards to
    /// <see cref="FromCylindrical(double, Angle, double)"/>, which documents the convention.
    /// </summary>
    /// <param name="radius">The distance from the Z axis, measured in the XY plane.</param>
    /// <param name="azimuth">The angle in the XY plane, measured from <c>+X</c> towards <c>+Y</c>.</param>
    /// <param name="height">The Z coordinate, used unchanged.</param>
    /// <remarks>
    /// <b>It exists because <c>E2-T59</c> says every factory is also a constructor</b> — a code
    /// block is C#, and in C# one asks for a point with <c>new</c>. It forwards rather than
    /// repeating the arithmetic so the two cannot drift apart.
    /// </remarks>
    public Point3d(double radius, Angle azimuth, double height)
        : this(FromCylindrical(radius, azimuth, height))
    {
    }

    /// <summary>
    /// Creates a point from spherical coordinates. Forwards to
    /// <see cref="FromSpherical(double, Angle, Angle)"/>, which documents the convention —
    /// in particular that <paramref name="polar"/> is measured <b>from <c>+Z</c></b> and is not
    /// an elevation above the XY plane.
    /// </summary>
    /// <param name="radius">The distance from the origin.</param>
    /// <param name="azimuth">The angle in the XY plane, measured from <c>+X</c> towards <c>+Y</c>.</param>
    /// <param name="polar">The angle away from the <c>+Z</c> axis.</param>
    public Point3d(double radius, Angle azimuth, Angle polar)
        : this(FromSpherical(radius, azimuth, polar))
    {
    }

    private Point3d(in Point3d other)
    {
        X = other.X;
        Y = other.Y;
        Z = other.Z;
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
    /// Creates a point from cylindrical coordinates about the world Z axis.
    /// </summary>
    /// <param name="radius">
    /// The distance from the Z axis, measured in the XY plane. A negative value is <b>not</b> an
    /// error: it reflects through the axis, exactly as <paramref name="azimuth"/> plus half a turn
    /// would, and refusing it would make the two spellings of the same point disagree.
    /// </param>
    /// <param name="azimuth">
    /// The angle in the XY plane, measured from the <c>+X</c> axis towards <c>+Y</c>. Zero is
    /// <c>+X</c> and a quarter turn is <c>+Y</c>.
    /// </param>
    /// <param name="height">The Z coordinate, used unchanged.</param>
    /// <returns>
    /// <c>(radius·cos azimuth, radius·sin azimuth, height)</c>.
    /// </returns>
    /// <remarks>
    /// <b>A non-finite argument is not refused, and that is this type's convention rather than an
    /// oversight.</b> <see cref="Point3d"/>'s constructor accepts any three doubles — <see cref="Unset"/>
    /// is built from <see cref="double.NaN"/> — so a non-finite input yields a point that answers
    /// <see langword="false"/> to <see cref="IsValid"/>, which is where the caller checks. A factory
    /// on <see cref="Plane"/> throws in the same situation because an invalid <see cref="Plane"/> is
    /// not representable at all; here it is.
    /// </remarks>
    public static Point3d FromCylindrical(double radius, Angle azimuth, double height) => new(
        radius * Math.Cos(azimuth.Radians),
        radius * Math.Sin(azimuth.Radians),
        height);

    /// <summary>
    /// Creates a point from spherical coordinates about the world Z axis.
    /// </summary>
    /// <param name="radius">
    /// The distance from the origin. Negative reflects through the origin rather than being an
    /// error, for the same reason it does in
    /// <see cref="FromCylindrical(double, Angle, double)"/>.
    /// </param>
    /// <param name="azimuth">
    /// The angle in the XY plane, measured from the <c>+X</c> axis towards <c>+Y</c>.
    /// </param>
    /// <param name="polar">
    /// The angle away from the <c>+Z</c> axis — the <i>polar</i> or <i>zenith</i> angle, <b>not</b>
    /// an elevation measured up from the XY plane. Zero is <c>+Z</c>, a quarter turn is the XY
    /// plane, and half a turn is <c>-Z</c>. The two conventions differ by a quarter turn and
    /// nothing in the arithmetic reveals which one was meant, so it is stated here.
    /// </param>
    /// <returns>
    /// <c>(radius·sin polar·cos azimuth, radius·sin polar·sin azimuth, radius·cos polar)</c>.
    /// </returns>
    /// <remarks>
    /// A non-finite argument is reported by <see cref="IsValid"/> rather than thrown on — see
    /// <see cref="FromCylindrical(double, Angle, double)"/> for why.
    /// </remarks>
    public static Point3d FromSpherical(double radius, Angle azimuth, Angle polar)
    {
        double sinPolar = Math.Sin(polar.Radians);

        return new Point3d(
            radius * sinPolar * Math.Cos(azimuth.Radians),
            radius * sinPolar * Math.Sin(azimuth.Radians),
            radius * Math.Cos(polar.Radians));
    }

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
    /// The points with every later one that lies within a tolerance of an earlier kept one removed
    /// (<c>E2-T16</c>).
    /// </summary>
    /// <param name="points">The points, in order.</param>
    /// <param name="tolerance">How close two points must be to count as one. Zero removes exact copies only.</param>
    /// <returns>The first point of each cluster, in the order the points came.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative or not finite.</exception>
    /// <exception cref="ArgumentException">A point is not finite.</exception>
    /// <remarks>
    /// <para>
    /// <b>Through a k-d tree, not every pair.</b> A hundred thousand points cost a hundred thousand radius
    /// queries instead of five billion distances.
    /// </para>
    /// <para>
    /// <b>Judged against what was kept.</b> A point is removed only when a point already kept lies within
    /// the tolerance - so in a chain of points each a little closer than the tolerance to the next, the
    /// first is kept, the second removed, and the third kept, because what it was close to is gone. That
    /// is the rule that makes the answer independent of anything but the order of the input.
    /// </para>
    /// </remarks>
    public static Point3d[] PruneDuplicates(IReadOnlyList<Point3d> points, double tolerance = 0.0)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (!double.IsFinite(tolerance) || tolerance < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "A tolerance must be finite and not negative.");
        }

        PointKdTree tree = PointKdTree.Build(points);
        bool[] removed = new bool[points.Count];
        List<Point3d> kept = new(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            if (removed[i])
            {
                continue;
            }

            kept.Add(points[i]);

            foreach (int j in tree.Within(points[i], tolerance))
            {
                if (j > i)
                {
                    removed[j] = true;
                }
            }
        }

        return [.. kept];
    }

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

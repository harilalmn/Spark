using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
    /// Creates a point from cylindrical coordinates in a plane's frame.
    /// </summary>
    /// <param name="plane">
    /// The frame the coordinates are measured in. <paramref name="angle"/> is measured from its
    /// <see cref="Plane.XAxis"/> towards its <see cref="Plane.YAxis"/>, and
    /// <paramref name="height"/> along its <see cref="Plane.Normal"/>.
    /// </param>
    /// <param name="radius">
    /// The distance from the plane's axis. A negative radius points the other way, which is the
    /// same thing as adding half a turn to the angle, and is allowed for that reason rather than
    /// refused as an error.
    /// </param>
    /// <param name="angle">The angle around the axis, counter-clockwise seen from the normal's end.</param>
    /// <param name="height">The distance along the normal.</param>
    /// <returns>The point.</returns>
    /// <remarks>
    /// <b>The frame is a parameter rather than the world.</b> Dynamo's counterpart takes a
    /// coordinate system for the same reason: cylindrical coordinates about the world Z axis are
    /// almost never the ones anybody wants, and a version that assumed them would be followed
    /// immediately by a transform undoing the assumption.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="plane"/> is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when any of the coordinates is not finite.
    /// </exception>
    public static Point3d ByCylindricalCoordinates(
        in Plane plane,
        double radius,
        Angle angle,
        double height)
    {
        CheckFrame(plane);
        CheckFinite(radius, nameof(radius));
        CheckFinite(angle.Radians, nameof(angle));
        CheckFinite(height, nameof(height));

        return plane.Origin
            + (plane.XAxis * (radius * Math.Cos(angle.Radians)))
            + (plane.YAxis * (radius * Math.Sin(angle.Radians)))
            + (plane.Normal * height);
    }

    /// <summary>
    /// Creates a point from spherical coordinates in a plane's frame.
    /// </summary>
    /// <param name="plane">The frame the coordinates are measured in.</param>
    /// <param name="radius">The distance from the plane's origin.</param>
    /// <param name="azimuth">
    /// The angle around the normal, measured from <see cref="Plane.XAxis"/> towards
    /// <see cref="Plane.YAxis"/>.
    /// </param>
    /// <param name="inclination">
    /// The angle <b>from the normal</b>, not from the plane. Zero is straight up the normal, a
    /// quarter turn is in the plane, and a half turn is straight down. This is the physics
    /// convention and it is the one that makes a full sphere sweep a range of half a turn; the
    /// alternative — an elevation measured from the plane — differs from it by a sign as well as
    /// an offset, which is exactly the confusion worth stating here rather than leaving to a
    /// reader's assumption.
    /// </param>
    /// <returns>The point.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="plane"/> is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when any of the coordinates is not finite.
    /// </exception>
    public static Point3d BySphericalCoordinates(
        in Plane plane,
        double radius,
        Angle azimuth,
        Angle inclination)
    {
        CheckFrame(plane);
        CheckFinite(radius, nameof(radius));
        CheckFinite(azimuth.Radians, nameof(azimuth));
        CheckFinite(inclination.Radians, nameof(inclination));

        double flat = radius * Math.Sin(inclination.Radians);

        return plane.Origin
            + (plane.XAxis * (flat * Math.Cos(azimuth.Radians)))
            + (plane.YAxis * (flat * Math.Sin(azimuth.Radians)))
            + (plane.Normal * (radius * Math.Cos(inclination.Radians)));
    }

    /// <summary>
    /// Removes points that coincide with an earlier one, within a tolerance.
    /// </summary>
    /// <param name="points">The points. Read once, in order, and not modified.</param>
    /// <param name="map">
    /// For each input point, the index of the point it became in the result, or <c>-1</c> for one
    /// that was dropped. This is what a mesh welder needs and cannot recover afterwards: without
    /// it a caller has the deduplicated positions and no way to renumber the faces that referred
    /// to them.
    /// </param>
    /// <param name="tolerance">
    /// How close counts as the same place; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>The surviving points, in input order.</returns>
    /// <remarks>
    /// <para>
    /// <b>The first occurrence wins, and the result keeps input order.</b> Both matter: a rule
    /// that kept the last occurrence would move a vertex by a tolerance's width every time the
    /// list was re-pruned, and an order that depended on the search structure would make the same
    /// input produce a different output between releases.
    /// </para>
    /// <para>
    /// <b>Coinciding is not transitive, and this member does not pretend it is.</b> Three points
    /// spaced a little over half a tolerance apart form a chain in which each is within tolerance
    /// of its neighbour and the ends are not within tolerance of each other. No partition of such
    /// a chain is not arbitrary. What is defined here is the greedy answer: a point is dropped
    /// only when it is within tolerance of a point that was <b>kept</b>, so the middle of that
    /// chain is dropped and both ends survive. Following a dropped point through to its own
    /// survivor would be the other answer, and it is the wrong one: it makes coincidence
    /// transitive, a chain has no length limit, and a point can end up merged into a
    /// representative arbitrarily far away. <b>This rule moves no point by more than one
    /// tolerance</b>, which is the property a caller can actually rely on.
    /// </para>
    /// <para>
    /// <b>Points that are not finite are dropped</b> and their entries in
    /// <paramref name="map"/> are <c>-1</c>. A <see cref="double.NaN"/> coordinate has no
    /// position, so <i>is this the same place</i> has no answer for it; treating each one as
    /// distinct would let a single upstream defect multiply into thousands of survivors.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="points"/> is <see langword="null"/>.
    /// </exception>
    public static Point3d[] PruneDuplicates(
        IReadOnlyList<Point3d> points,
        out int[] map,
        in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(points);

        map = new int[points.Count];
        double linear = tolerance.Linear;

        // One hierarchy over all the input points, built once. The alternative is a structure
        // grown as points are kept, which needs an index supporting insertion - and E2-T16's
        // KD-tree was going to be that index. It is deliberately not built: searching ALL the
        // points and then asking whether each neighbour was kept answers the same question with
        // the structure that already exists, and a second spatial index is a second thing to get
        // right, to test and to keep true. See the exclusion note on E2-T16 in TASKS.md.
        Bvh<int> tree = Bvh<int>.Build(
            [.. Enumerable.Range(0, points.Count)],
            index => points[index].IsValid
                ? new BoundingBox(points[index], points[index])
                : BoundingBox.Empty);

        List<Point3d> kept = [];
        List<int> neighbours = [];
        bool[] isRepresentative = new bool[points.Count];
        Vector3d reach = new(linear, linear, linear);

        for (int index = 0; index < points.Count; index++)
        {
            Point3d point = points[index];

            if (!point.IsValid)
            {
                map[index] = -1;
                continue;
            }

            neighbours.Clear();
            tree.Overlapping(new BoundingBox(point - reach, point + reach), neighbours);

            int survivor = -1;

            foreach (int neighbour in neighbours)
            {
                // Only points EARLIER in the input can absorb this one, and only ones that were
                // KEPT rather than merged away. Following a dropped point to its own survivor
                // instead would make coincidence transitive along a chain, and a chain has no
                // length limit: a point could end up merged into a representative arbitrarily
                // far from it. Comparing against representatives only is what bounds the
                // movement of any point at exactly one tolerance.
                if (neighbour < index
                    && isRepresentative[neighbour]
                    && point.DistanceTo(points[neighbour]) <= linear)
                {
                    survivor = map[neighbour];
                    break;
                }
            }

            if (survivor >= 0)
            {
                map[index] = survivor;
                continue;
            }

            map[index] = kept.Count;
            isRepresentative[index] = true;
            kept.Add(point);
        }

        return [.. kept];
    }

    /// <summary>
    /// Removes points that coincide with an earlier one, within a tolerance.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="tolerance">How close counts as the same place.</param>
    /// <returns>The surviving points, in input order.</returns>
    public static Point3d[] PruneDuplicates(IReadOnlyList<Point3d> points, in Tolerance tolerance = default) =>
        PruneDuplicates(points, out _, tolerance);

    private static void CheckFrame(in Plane plane)
    {
        if (!plane.IsValid)
        {
            throw new InvalidOperationException(
                "A default-constructed Plane has no frame, so no coordinates can be measured in it.");
        }
    }

    private static void CheckFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("A coordinate must be finite.", name);
        }
    }

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

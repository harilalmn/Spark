using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A direction and magnitude in three-dimensional space. Vectors are unitless and are
/// interpreted in Spark's right-handed coordinate system.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Vector3d"/> is not a <see cref="Point3d"/>. A vector has no position, so
/// translating it does nothing, and a transform applies only its linear part to a vector.
/// Conversion between the two exists but is <b>explicit</b> in both directions, because
/// silently treating one as the other is a classic and very hard-to-see source of geometry
/// bugs.
/// </para>
/// <para>
/// <c>operator ==</c> is exact. Use <see cref="EqualsWithin(in Vector3d, in Tolerance)"/>
/// for geometric comparison. A fuzzy equality operator was deliberately rejected: it breaks
/// hashing and transitivity, and the seed library's 1e-9 fuzzy <c>operator ==</c> is one of
/// the things this kernel does not inherit.
/// </para>
/// </remarks>
public readonly struct Vector3d : IEquatable<Vector3d>
{
    /// <summary>
    /// Creates a vector from its three components.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    public Vector3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The X component.</summary>
    public double X { get; }

    /// <summary>The Y component.</summary>
    public double Y { get; }

    /// <summary>The Z component.</summary>
    public double Z { get; }

    /// <summary>
    /// The zero vector. This is also the value of a default-constructed
    /// <see cref="Vector3d"/>. It has no direction, so normalising it fails and asking for
    /// an angle to it throws.
    /// </summary>
    public static Vector3d Zero => new(0.0, 0.0, 0.0);

    /// <summary>The unit vector along the world X axis, <c>(1, 0, 0)</c>.</summary>
    public static Vector3d XAxis => new(1.0, 0.0, 0.0);

    /// <summary>The unit vector along the world Y axis, <c>(0, 1, 0)</c>.</summary>
    public static Vector3d YAxis => new(0.0, 1.0, 0.0);

    /// <summary>
    /// The unit vector along the world Z axis, <c>(0, 0, 1)</c>. In Spark's right-handed
    /// system this is <see cref="XAxis"/> crossed with <see cref="YAxis"/>.
    /// </summary>
    public static Vector3d ZAxis => new(0.0, 0.0, 1.0);

    /// <summary>
    /// The Euclidean length of this vector. Computed with a square root; prefer
    /// <see cref="LengthSquared"/> when only relative comparison is needed.
    /// </summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>
    /// The squared Euclidean length of this vector. Cheaper than <see cref="Length"/> and
    /// sufficient whenever lengths are only being compared with one another.
    /// </summary>
    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    /// <summary>
    /// <see langword="true"/> when every component is finite, that is neither
    /// <see cref="double.NaN"/> nor infinite.
    /// </summary>
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    /// <summary>
    /// Returns this vector scaled to unit length.
    /// </summary>
    /// <returns>A vector in the same direction with a length of one.</returns>
    /// <remarks>
    /// The components are pre-scaled by the largest of their magnitudes before the length is
    /// taken, so this succeeds for vectors whose squared length would otherwise overflow or
    /// underflow to zero.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the vector has no direction to preserve — that is, when it is exactly
    /// zero, or when any component is <see cref="double.NaN"/> or infinite. This is a
    /// deliberate divergence from the seed library, which returned the zero vector and let
    /// a meaningless direction propagate silently. Use
    /// <see cref="TryNormalise(out Vector3d)"/> where failure is expected.
    /// </exception>
    public Vector3d Normalised()
    {
        if (!TryNormalise(out Vector3d unit))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite vector has no direction and cannot be normalised.");
        }

        return unit;
    }

    /// <summary>
    /// Attempts to scale this vector to unit length.
    /// </summary>
    /// <param name="unit">
    /// On success, this vector scaled to a length of one. On failure, <see cref="Zero"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the vector had a usable direction; <see langword="false"/>
    /// when it is exactly zero or has a <see cref="double.NaN"/> or infinite component.
    /// </returns>
    public bool TryNormalise(out Vector3d unit)
    {
        double scale = Math.Max(Math.Max(Math.Abs(X), Math.Abs(Y)), Math.Abs(Z));

        if (scale == 0.0 || !double.IsFinite(scale) || double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z))
        {
            unit = Zero;
            return false;
        }

        double x = X / scale;
        double y = Y / scale;
        double z = Z / scale;
        double length = Math.Sqrt((x * x) + (y * y) + (z * z));

        unit = new Vector3d(x / length, y / length, z / length);
        return true;
    }

    /// <summary>
    /// Tests whether this vector is the zero vector within a tolerance.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the length is at most the linear tolerance.</returns>
    public bool IsZero(in Tolerance tolerance = default) => tolerance.IsZero(Length);

    /// <summary>
    /// Tests whether this vector already has unit length within a tolerance.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted in practice. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the length is within tolerance of one.</returns>
    public bool IsUnit(in Tolerance tolerance = default) => tolerance.AreEqual(Length, 1.0);

    /// <summary>
    /// The dot product of this vector with another.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>
    /// The scalar product, equal to the product of the two lengths and the cosine of the
    /// angle between them. Zero when the vectors are perpendicular, negative when they point
    /// in broadly opposite directions.
    /// </returns>
    public double Dot(in Vector3d other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);

    /// <summary>
    /// The dot product of two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The scalar product of <paramref name="a"/> and <paramref name="b"/>.</returns>
    public static double Dot(in Vector3d a, in Vector3d b) => a.Dot(b);

    /// <summary>
    /// The cross product of this vector with another.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>
    /// A vector perpendicular to both operands, whose length is the area of the
    /// parallelogram they span, and whose direction follows the right-hand rule: with this
    /// vector along the fingers and <paramref name="other"/> curling from it, the result
    /// points along the thumb. Returns <see cref="Zero"/> when the operands are parallel.
    /// </returns>
    public Vector3d Cross(in Vector3d other) => new(
        (Y * other.Z) - (Z * other.Y),
        (Z * other.X) - (X * other.Z),
        (X * other.Y) - (Y * other.X));

    /// <summary>
    /// The cross product of two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>A vector perpendicular to both, following the right-hand rule.</returns>
    public static Vector3d Cross(in Vector3d a, in Vector3d b) => a.Cross(b);

    /// <summary>
    /// The scalar triple product of three vectors, <c>a · (b × c)</c>.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <param name="c">The third vector.</param>
    /// <returns>
    /// The signed volume of the parallelepiped the three vectors span. Positive when they
    /// form a right-handed set, negative when left-handed, and zero when they are coplanar.
    /// </returns>
    public static double TripleProduct(in Vector3d a, in Vector3d b, in Vector3d c) => a.Dot(b.Cross(c));

    /// <summary>
    /// The scalar triple product of this vector with two others, <c>this · (b × c)</c>.
    /// </summary>
    /// <param name="b">The second vector.</param>
    /// <param name="c">The third vector.</param>
    /// <returns>The signed volume of the parallelepiped the three vectors span.</returns>
    public double TripleProduct(in Vector3d b, in Vector3d c) => Dot(b.Cross(c));

    /// <summary>
    /// The unsigned angle between this vector and another.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>An angle in the closed range <c>[0, pi]</c> radians, that is 0 to 180 degrees.</returns>
    /// <remarks>
    /// Computed as <c>atan2(|a × b|, a · b)</c> on the normalised operands rather than as
    /// <c>acos(a · b)</c>. The seed library used the arc-cosine form, which loses most of its
    /// significant figures for nearly-parallel and nearly-antiparallel vectors — exactly the
    /// cases collinearity tests care about.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either vector is zero-length or non-finite, because a vector with no
    /// direction has no angle to anything.
    /// </exception>
    public Angle AngleTo(in Vector3d other)
    {
        (Vector3d a, Vector3d b) = NormalisedPair(this, other);

        return AngleBetweenUnitVectors(a, b);
    }

    /// <summary>
    /// The signed angle from this vector to another, measured about a reference axis.
    /// </summary>
    /// <param name="other">The vector to measure to.</param>
    /// <param name="axis">
    /// The axis the rotation is measured about. Only its direction matters; it need not be
    /// perpendicular to either operand, and it need not be normalised.
    /// </param>
    /// <returns>
    /// An angle in the range <c>(-pi, pi]</c>. It is positive when the shortest rotation from
    /// this vector to <paramref name="other"/> is counter-clockwise as seen from the positive
    /// end of <paramref name="axis"/>, and negative when it is clockwise. When the two
    /// vectors are exactly antiparallel the result is <c>+pi</c>, because the sense of that
    /// rotation is genuinely undetermined and a positive answer is the conventional choice.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either vector, or the axis, is zero-length or non-finite.
    /// </exception>
    public Angle SignedAngleTo(in Vector3d other, in Vector3d axis)
    {
        (Vector3d a, Vector3d b) = NormalisedPair(this, other);

        if (!axis.TryNormalise(out Vector3d referenceAxis))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite axis gives no sense of rotation.");
        }

        // The sign is taken from the cross product of the *normalised* operands, not of the
        // originals. Two vectors around 1e-170 have a true cross product around 1e-340, which
        // underflows to signed zero; the test that follows is a strict comparison, -0.0 is not
        // less than 0.0, and the sign flip was silently skipped — so a clockwise turn reported
        // +90 degrees. Normalising first keeps the cross product at a magnitude that carries
        // its sign.
        Vector3d cross = a.Cross(b);
        Angle unsigned = AngleBetweenUnitVectors(a, b, cross);

        return cross.Dot(referenceAxis) < 0.0 ? -unsigned : unsigned;
    }

    /// <summary>
    /// Tests whether this vector is parallel to another, within the angular tolerance.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Angular"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two directions are within the angular tolerance of
    /// either parallel or <b>antiparallel</b> — opposing directions count as parallel here,
    /// which is what collinearity tests want. Returns <see langword="false"/> when either
    /// vector is zero-length or non-finite, because such a vector has no direction to be
    /// parallel to.
    /// </returns>
    public bool IsParallelTo(in Vector3d other, in Tolerance tolerance = default)
    {
        if (!TryNormalise(out Vector3d a) || !other.TryNormalise(out Vector3d b))
        {
            return false;
        }

        // |a x b| is |sin t| for unit vectors, which is the deviation from parallel near
        // both 0 and pi, and is far better conditioned there than a dot product would be.
        return a.Cross(b).Length <= Math.Sin(tolerance.Angular.Radians);
    }

    /// <summary>
    /// Tests whether this vector is perpendicular to another, within the angular tolerance.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Angular"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the angle between the two directions is within the angular
    /// tolerance of a quarter turn. Returns <see langword="false"/> when either vector is
    /// zero-length or non-finite.
    /// </returns>
    public bool IsPerpendicularTo(in Vector3d other, in Tolerance tolerance = default)
    {
        if (!TryNormalise(out Vector3d a) || !other.TryNormalise(out Vector3d b))
        {
            return false;
        }

        // |a . b| is |cos t| for unit vectors, which is the deviation from perpendicular.
        return Math.Abs(a.Dot(b)) <= Math.Sin(tolerance.Angular.Radians);
    }

    /// <summary>
    /// Projects this vector onto the line spanned by another vector.
    /// </summary>
    /// <param name="direction">The direction to project onto. Need not be normalised.</param>
    /// <returns>
    /// The component of this vector lying along <paramref name="direction"/>. The result is
    /// always parallel or antiparallel to <paramref name="direction"/>, and subtracting it
    /// from this vector leaves the perpendicular component.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="direction"/> is zero-length or non-finite, because a line
    /// with no direction is not something a vector can be projected onto.
    /// </exception>
    public Vector3d ProjectOnto(in Vector3d direction)
    {
        if (!direction.TryNormalise(out Vector3d unit))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite direction defines no line to project onto.");
        }

        return unit * Dot(unit);
    }

    /// <summary>
    /// Reflects this vector in the plane through the origin with the given normal.
    /// </summary>
    /// <param name="normal">The plane normal. Need not be normalised.</param>
    /// <returns>
    /// The reflected vector, of the same length as this one. Its component along
    /// <paramref name="normal"/> is negated and its component in the plane is unchanged, so
    /// a vector already lying in the plane is returned unchanged.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="normal"/> is zero-length or non-finite.
    /// </exception>
    public Vector3d Reflect(in Vector3d normal)
    {
        if (!normal.TryNormalise(out Vector3d unit))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite normal defines no plane to reflect in.");
        }

        return this - (unit * (2.0 * Dot(unit)));
    }

    /// <summary>
    /// Rotates this vector about an axis through the origin.
    /// </summary>
    /// <param name="axis">The rotation axis. Need not be normalised.</param>
    /// <param name="angle">
    /// The rotation angle. Positive angles rotate counter-clockwise when viewed from the
    /// positive end of <paramref name="axis"/> looking back towards the origin.
    /// </param>
    /// <returns>
    /// The rotated vector, of the same length as this one. Rotating by a whole number of
    /// turns returns the original vector to within floating-point rounding, not exactly.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="axis"/> is zero-length or non-finite.
    /// </exception>
    public Vector3d Rotate(in Vector3d axis, Angle angle)
    {
        if (!axis.TryNormalise(out Vector3d k))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite axis defines no rotation.");
        }

        // Rodrigues' rotation formula.
        double cos = Math.Cos(angle.Radians);
        double sin = Math.Sin(angle.Radians);

        return (this * cos) + (k.Cross(this) * sin) + (k * (k.Dot(this) * (1.0 - cos)));
    }

    /// <summary>
    /// Tests whether this vector and another are equal within a tolerance.
    /// </summary>
    /// <param name="other">The vector to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the length of the difference is negligible at the scale of
    /// the longer operand, by <see cref="Tolerance.IsNegligible(double, double)"/>. This is a
    /// spherical test, not a per-component box test, so the answer does not depend on how the
    /// vectors are oriented relative to the axes, and it is scale-aware, so it does not
    /// degenerate into bit-equality at large magnitudes.
    /// </returns>
    public bool EqualsWithin(in Vector3d other, in Tolerance tolerance = default) =>
        tolerance.IsNegligible((this - other).Length, Math.Max(Length, other.Length));

    /// <summary>Adds two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector3d operator +(in Vector3d left, in Vector3d right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary>Subtracts one vector from another.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector3d operator -(in Vector3d left, in Vector3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary>Reverses a vector.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>A vector of the same length pointing in the opposite direction.</returns>
    public static Vector3d operator -(in Vector3d value) => new(-value.X, -value.Y, -value.Z);

    /// <summary>Scales a vector.</summary>
    /// <param name="vector">The vector to scale.</param>
    /// <param name="factor">The scale factor. A negative factor also reverses the direction.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3d operator *(in Vector3d vector, double factor) =>
        new(vector.X * factor, vector.Y * factor, vector.Z * factor);

    /// <summary>Scales a vector.</summary>
    /// <param name="factor">The scale factor. A negative factor also reverses the direction.</param>
    /// <param name="vector">The vector to scale.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3d operator *(double factor, in Vector3d vector) => vector * factor;

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="vector">The vector to divide.</param>
    /// <param name="divisor">
    /// The divisor. A divisor of zero yields infinite or <see cref="double.NaN"/> components
    /// rather than an exception, matching <see cref="double"/> division.
    /// </param>
    /// <returns>The divided vector.</returns>
    public static Vector3d operator /(in Vector3d vector, double divisor) =>
        new(vector.X / divisor, vector.Y / divisor, vector.Z / divisor);

    /// <summary>Adds two vectors. The named alternate to <c>operator +</c>.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector3d Add(in Vector3d left, in Vector3d right) => left + right;

    /// <summary>Subtracts one vector from another. The named alternate to <c>operator -</c>.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector3d Subtract(in Vector3d left, in Vector3d right) => left - right;

    /// <summary>Scales a vector. The named alternate to <c>operator *</c>.</summary>
    /// <param name="vector">The vector to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3d Multiply(in Vector3d vector, double factor) => vector * factor;

    /// <summary>Divides a vector by a scalar. The named alternate to <c>operator /</c>.</summary>
    /// <param name="vector">The vector to divide.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector3d Divide(in Vector3d vector, double divisor) => vector / divisor;

    /// <summary>Reverses a vector. The named alternate to unary <c>operator -</c>.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>The reversed vector.</returns>
    public static Vector3d Negate(in Vector3d value) => -value;

    /// <summary>
    /// Compares two vectors for exact component-wise equality.
    /// </summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>
    /// <see langword="true"/> when all three components are equal under IEEE equality. This
    /// is exact and follows IEEE rules, so a vector containing <see cref="double.NaN"/> is
    /// not equal to itself. Use <see cref="EqualsWithin(in Vector3d, in Tolerance)"/> for
    /// geometric comparison.
    /// </returns>
    public static bool operator ==(in Vector3d left, in Vector3d right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    /// <summary>Compares two vectors for exact inequality.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Vector3d left, in Vector3d right) => !(left == right);

    /// <summary>
    /// Tests exact component-wise equality, treating <see cref="double.NaN"/> as equal to
    /// itself so that vectors remain usable as dictionary keys. This is the one place where
    /// the behaviour differs from <c>operator ==</c>, and it differs in the same way, and for
    /// the same reason, that <see cref="double.Equals(double)"/> differs from <c>==</c>.
    /// </summary>
    /// <param name="other">The vector to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when all three components are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(Vector3d other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vector3d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>
    /// Formats the components, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>(1, 0, 0)</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");

    private static (Vector3d First, Vector3d Second) NormalisedPair(in Vector3d first, in Vector3d second)
    {
        if (!first.TryNormalise(out Vector3d a) || !second.TryNormalise(out Vector3d b))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite vector has no direction, so no angle to it is defined.");
        }

        return (a, b);
    }

    private static Angle AngleBetweenUnitVectors(in Vector3d a, in Vector3d b) =>
        AngleBetweenUnitVectors(a, b, a.Cross(b));

    private static Angle AngleBetweenUnitVectors(in Vector3d a, in Vector3d b, in Vector3d cross) =>
        Angle.FromRadians(Math.Atan2(cross.Length, a.Dot(b)));
}

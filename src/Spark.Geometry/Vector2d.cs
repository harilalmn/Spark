using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A direction and magnitude in a two-dimensional plane. Used for planar work and, from M6,
/// for BRep trim curves in a surface's UV space.
/// </summary>
/// <remarks>
/// The plane is right-handed with an implied outward normal along <c>+Z</c>, so a positive
/// rotation carries <see cref="XAxis"/> towards <see cref="YAxis"/> — counter-clockwise when
/// the plane is drawn with X to the right and Y upwards.
/// </remarks>
public readonly struct Vector2d : IEquatable<Vector2d>
{
    /// <summary>
    /// Creates a vector from its two components.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    public Vector2d(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The X component.</summary>
    public double X { get; }

    /// <summary>The Y component.</summary>
    public double Y { get; }

    /// <summary>
    /// The zero vector. This is also the value of a default-constructed
    /// <see cref="Vector2d"/>.
    /// </summary>
    public static Vector2d Zero => new(0.0, 0.0);

    /// <summary>The unit vector along the X axis, <c>(1, 0)</c>.</summary>
    public static Vector2d XAxis => new(1.0, 0.0);

    /// <summary>The unit vector along the Y axis, <c>(0, 1)</c>.</summary>
    public static Vector2d YAxis => new(0.0, 1.0);

    /// <summary>The Euclidean length of this vector.</summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>
    /// The squared Euclidean length of this vector. Cheaper than <see cref="Length"/> and
    /// sufficient whenever lengths are only being compared with one another.
    /// </summary>
    public double LengthSquared => (X * X) + (Y * Y);

    /// <summary>
    /// <see langword="true"/> when both components are finite.
    /// </summary>
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y);

    /// <summary>
    /// Returns this vector scaled to unit length.
    /// </summary>
    /// <returns>A vector in the same direction with a length of one.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the vector is exactly zero or has a non-finite component, because such a
    /// vector has no direction to preserve. Use <see cref="TryNormalise(out Vector2d)"/>
    /// where failure is expected.
    /// </exception>
    public Vector2d Normalised()
    {
        if (!TryNormalise(out Vector2d unit))
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
    /// when it is exactly zero or has a non-finite component. The components are pre-scaled
    /// by the larger of their magnitudes, so this succeeds for vectors whose squared length
    /// would overflow or underflow.
    /// </returns>
    public bool TryNormalise(out Vector2d unit)
    {
        double scale = Math.Max(Math.Abs(X), Math.Abs(Y));

        if (scale == 0.0 || !double.IsFinite(scale) || double.IsNaN(X) || double.IsNaN(Y))
        {
            unit = Zero;
            return false;
        }

        double x = X / scale;
        double y = Y / scale;
        double length = Math.Sqrt((x * x) + (y * y));

        unit = new Vector2d(x / length, y / length);
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
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the length is within tolerance of one.</returns>
    public bool IsUnit(in Tolerance tolerance = default) => tolerance.AreEqual(Length, 1.0);

    /// <summary>
    /// The dot product of this vector with another.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>The scalar product, zero when the vectors are perpendicular.</returns>
    public double Dot(in Vector2d other) => (X * other.X) + (Y * other.Y);

    /// <summary>
    /// The dot product of two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The scalar product.</returns>
    public static double Dot(in Vector2d a, in Vector2d b) => a.Dot(b);

    /// <summary>
    /// The two-dimensional cross product of this vector with another.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>
    /// The scalar <c>X*other.Y - Y*other.X</c>, which is the Z component the equivalent
    /// three-dimensional cross product would have. It is the signed area of the parallelogram
    /// the two vectors span: positive when <paramref name="other"/> lies counter-clockwise
    /// from this vector, negative when clockwise, and zero when they are parallel.
    /// </returns>
    public double Cross(in Vector2d other) => (X * other.Y) - (Y * other.X);

    /// <summary>
    /// The two-dimensional cross product of two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The signed area of the parallelogram the two vectors span.</returns>
    public static double Cross(in Vector2d a, in Vector2d b) => a.Cross(b);

    /// <summary>
    /// Returns this vector rotated a quarter turn counter-clockwise.
    /// </summary>
    /// <returns>The vector <c>(-Y, X)</c>, which has the same length and is perpendicular.</returns>
    public Vector2d Perpendicular() => new(-Y, X);

    /// <summary>
    /// The unsigned angle between this vector and another.
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>An angle in the closed range <c>[0, pi]</c> radians.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either vector is zero-length or non-finite.
    /// </exception>
    public Angle AngleTo(in Vector2d other) => SignedAngleTo(other).Abs();

    /// <summary>
    /// The signed angle from this vector to another.
    /// </summary>
    /// <param name="other">The vector to measure to.</param>
    /// <returns>
    /// An angle in the range <c>(-pi, pi]</c>, positive when the shortest rotation from this
    /// vector to <paramref name="other"/> is counter-clockwise. Exactly antiparallel vectors
    /// give <c>+pi</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either vector is zero-length or non-finite.
    /// </exception>
    public Angle SignedAngleTo(in Vector2d other)
    {
        if (!TryNormalise(out Vector2d a) || !other.TryNormalise(out Vector2d b))
        {
            throw new InvalidOperationException(
                "A zero-length or non-finite vector has no direction, so no angle to it is defined.");
        }

        return Angle.FromRadians(Math.Atan2(a.Cross(b), a.Dot(b)));
    }

    /// <summary>
    /// Rotates this vector about the origin.
    /// </summary>
    /// <param name="angle">
    /// The rotation angle. Positive angles rotate counter-clockwise, carrying
    /// <see cref="XAxis"/> towards <see cref="YAxis"/>.
    /// </param>
    /// <returns>The rotated vector, of the same length as this one.</returns>
    public Vector2d Rotate(Angle angle)
    {
        double cos = Math.Cos(angle.Radians);
        double sin = Math.Sin(angle.Radians);

        return new Vector2d((X * cos) - (Y * sin), (X * sin) + (Y * cos));
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
    /// the longer operand, by <see cref="Tolerance.IsNegligible(double, double)"/> — the same
    /// scale-aware rule the rest of the value layer uses.
    /// </returns>
    public bool EqualsWithin(in Vector2d other, in Tolerance tolerance = default) =>
        tolerance.IsNegligible((this - other).Length, Math.Max(Length, other.Length));

    /// <summary>Adds two vectors.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector2d operator +(in Vector2d left, in Vector2d right) =>
        new(left.X + right.X, left.Y + right.Y);

    /// <summary>Subtracts one vector from another.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector2d operator -(in Vector2d left, in Vector2d right) =>
        new(left.X - right.X, left.Y - right.Y);

    /// <summary>Reverses a vector.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>A vector of the same length pointing in the opposite direction.</returns>
    public static Vector2d operator -(in Vector2d value) => new(-value.X, -value.Y);

    /// <summary>Scales a vector.</summary>
    /// <param name="vector">The vector to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector2d operator *(in Vector2d vector, double factor) =>
        new(vector.X * factor, vector.Y * factor);

    /// <summary>Scales a vector.</summary>
    /// <param name="factor">The scale factor.</param>
    /// <param name="vector">The vector to scale.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector2d operator *(double factor, in Vector2d vector) => vector * factor;

    /// <summary>Divides a vector by a scalar.</summary>
    /// <param name="vector">The vector to divide.</param>
    /// <param name="divisor">
    /// The divisor. A divisor of zero yields infinite or <see cref="double.NaN"/> components
    /// rather than an exception.
    /// </param>
    /// <returns>The divided vector.</returns>
    public static Vector2d operator /(in Vector2d vector, double divisor) =>
        new(vector.X / divisor, vector.Y / divisor);

    /// <summary>Adds two vectors. The named alternate to <c>operator +</c>.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The component-wise sum.</returns>
    public static Vector2d Add(in Vector2d left, in Vector2d right) => left + right;

    /// <summary>Subtracts one vector from another. The named alternate to <c>operator -</c>.</summary>
    /// <param name="left">The vector to subtract from.</param>
    /// <param name="right">The vector to subtract.</param>
    /// <returns>The component-wise difference.</returns>
    public static Vector2d Subtract(in Vector2d left, in Vector2d right) => left - right;

    /// <summary>Scales a vector. The named alternate to <c>operator *</c>.</summary>
    /// <param name="vector">The vector to scale.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector2d Multiply(in Vector2d vector, double factor) => vector * factor;

    /// <summary>Divides a vector by a scalar. The named alternate to <c>operator /</c>.</summary>
    /// <param name="vector">The vector to divide.</param>
    /// <param name="divisor">The divisor.</param>
    /// <returns>The divided vector.</returns>
    public static Vector2d Divide(in Vector2d vector, double divisor) => vector / divisor;

    /// <summary>Reverses a vector. The named alternate to unary <c>operator -</c>.</summary>
    /// <param name="value">The vector to negate.</param>
    /// <returns>The reversed vector.</returns>
    public static Vector2d Negate(in Vector2d value) => -value;

    /// <summary>
    /// Compares two vectors for exact component-wise equality, following IEEE rules.
    /// </summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>
    /// <see langword="true"/> when both components are equal. Use
    /// <see cref="EqualsWithin(in Vector2d, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(in Vector2d left, in Vector2d right) =>
        left.X == right.X && left.Y == right.Y;

    /// <summary>Compares two vectors for exact inequality.</summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Vector2d left, in Vector2d right) => !(left == right);

    /// <summary>
    /// Tests exact component-wise equality, treating <see cref="double.NaN"/> as equal to
    /// itself so that vectors remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The vector to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when both components are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(Vector2d other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vector2d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>
    /// Formats the components, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>(1, 0)</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}

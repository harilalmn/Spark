using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A rotation in three-dimensional space, carried as four numbers rather than as a matrix.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for that <see cref="Transform"/> is not.</b> A <see cref="Transform"/> is a
/// 4×4 matrix and can express any affine map, rotation included, so nothing here is reachable
/// only through a quaternion. What a quaternion adds is three things a matrix is bad at.
/// **Composition does not drift**: multiplying rotation matrices accumulates floating-point
/// error that shows up as shear and scale, and a quaternion's error stays a rotation and is
/// removed by renormalising. **Interpolation is well defined**: <see cref="Slerp"/> walks the
/// shortest arc between two orientations at constant angular speed, which no sensible operation
/// on two matrices does. And **four numbers serialise and store better than sixteen**, which is
/// what a camera, a frame or an animation key actually wants to keep.
/// </para>
/// <para>
/// <b>Two quaternions represent every rotation.</b> <c>q</c> and <c>-q</c> are the same
/// rotation, so <see cref="EqualsWithin(in Quaternion, in Tolerance)"/> — which compares
/// components — will answer <see langword="false"/> for a pair that rotates identically. That
/// is not a defect and it is not something to paper over in equality: use
/// <see cref="IsSameRotation(in Quaternion, in Tolerance)"/> when the question is about the
/// rotation, and component equality when the question is about the value.
/// </para>
/// <para>
/// <b><c>default(Quaternion)</c> is not a rotation.</b> Its components are all zero, which is
/// not a rotation of anything, and every geometric member throws
/// <see cref="InvalidOperationException"/> on it, exactly as <see cref="Plane"/> does. The
/// identity rotation is <see cref="Identity"/> and has to be asked for by name.
/// </para>
/// <para>
/// <b>Not here, deliberately.</b> There is no matrix-to-quaternion extraction: recovering a
/// rotation from a general <see cref="Transform"/> means deciding what to do with a matrix that
/// is nearly but not quite a rotation, and that decision belongs with the surface work that
/// first needs it rather than being guessed at now. There are no Euler angles either — twelve
/// conventions, no default worth defaulting to, and every one of them a source of bugs that
/// only appear at gimbal lock.
/// </para>
/// </remarks>
public readonly struct Quaternion : IEquatable<Quaternion>
{
    /// <summary>
    /// Creates a quaternion from its four components. Not normalised, and not checked: this is
    /// the raw constructor, and <see cref="ByAxisAngle(in Vector3d, Angle)"/> is what most
    /// callers want.
    /// </summary>
    /// <param name="x">The X component of the vector part.</param>
    /// <param name="y">The Y component of the vector part.</param>
    /// <param name="z">The Z component of the vector part.</param>
    /// <param name="w">The scalar part.</param>
    public Quaternion(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>The X component of the vector part.</summary>
    public double X { get; }

    /// <summary>The Y component of the vector part.</summary>
    public double Y { get; }

    /// <summary>The Z component of the vector part.</summary>
    public double Z { get; }

    /// <summary>The scalar part.</summary>
    public double W { get; }

    /// <summary>
    /// The rotation that does nothing: <c>(0, 0, 0, 1)</c>. Note that this is <b>not</b> the
    /// value of a default-constructed <see cref="Quaternion"/>, which is all zeros and is not a
    /// rotation at all.
    /// </summary>
    public static Quaternion Identity => new(0.0, 0.0, 0.0, 1.0);

    /// <summary>
    /// <see langword="true"/> when every component is finite and at least one is non-zero —
    /// which is exactly the condition under which this value denotes a rotation.
    /// </summary>
    public bool IsValid =>
        double.IsFinite(X)
        && double.IsFinite(Y)
        && double.IsFinite(Z)
        && double.IsFinite(W)
        && (X != 0.0 || Y != 0.0 || Z != 0.0 || W != 0.0);

    /// <summary>The sum of the squared components. Free of a square root.</summary>
    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z) + (W * W);

    /// <summary>The Euclidean length of the four-component vector.</summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>The vector part, as a vector.</summary>
    public Vector3d Vector => new(X, Y, Z);

    /// <summary>
    /// Creates the rotation of a given angle about a given axis.
    /// </summary>
    /// <param name="axis">The rotation axis. Need not be normalised.</param>
    /// <param name="angle">
    /// The rotation angle, counter-clockwise when viewed from the positive end of the axis —
    /// the same right-handed convention
    /// <see cref="Transform.Rotation(in Vector3d, Angle)"/> uses, and the same one every
    /// rotation in this assembly uses.
    /// </param>
    /// <returns>The rotation, as a unit quaternion.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="axis"/> is zero-length or non-finite, or when
    /// <paramref name="angle"/> is not finite.
    /// </exception>
    public static Quaternion ByAxisAngle(in Vector3d axis, Angle angle)
    {
        if (!axis.TryNormalise(out Vector3d k))
        {
            throw new ArgumentException(
                "A rotation axis must have non-zero length and finite components.",
                nameof(axis));
        }

        if (!double.IsFinite(angle.Radians))
        {
            throw new ArgumentException("A rotation angle must be finite.", nameof(angle));
        }

        double half = angle.Radians * 0.5;
        double s = Math.Sin(half);

        return new Quaternion(k.X * s, k.Y * s, k.Z * s, Math.Cos(half));
    }

    /// <summary>
    /// Creates the shortest rotation taking one direction onto another.
    /// </summary>
    /// <param name="from">The starting direction. Need not be normalised.</param>
    /// <param name="to">The finishing direction. Need not be normalised.</param>
    /// <returns>
    /// A unit quaternion <c>q</c> for which <c>q.OfVector(from)</c> is parallel to
    /// <paramref name="to"/>. Only the directions matter; the lengths are discarded.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either direction is zero-length or non-finite.
    /// </exception>
    /// <remarks>
    /// The <b>antiparallel case has no unique answer</b> — every axis perpendicular to the
    /// input turns it onto its opposite — and this returns a half turn about one of them,
    /// chosen from the world axis least aligned with <paramref name="from"/> so that the choice
    /// is stable rather than a function of floating-point noise. Callers who need a *particular*
    /// axis in that case must say so themselves, with
    /// <see cref="ByAxisAngle(in Vector3d, Angle)"/>.
    /// </remarks>
    public static Quaternion ByRotationBetween(in Vector3d from, in Vector3d to)
    {
        if (!from.TryNormalise(out Vector3d a))
        {
            throw new ArgumentException(
                "A direction must have non-zero length and finite components.",
                nameof(from));
        }

        if (!to.TryNormalise(out Vector3d b))
        {
            throw new ArgumentException(
                "A direction must have non-zero length and finite components.",
                nameof(to));
        }

        double dot = a.Dot(b);

        if (dot >= 1.0 - 1e-15)
        {
            return Identity;
        }

        if (dot <= -1.0 + 1e-15)
        {
            Vector3d perpendicular = LeastAlignedWorldAxis(a).Cross(a);

            // The cross product of two unit vectors that are not parallel is non-zero, and
            // LeastAlignedWorldAxis guarantees they are not parallel.
            Vector3d axis = perpendicular.Normalised();

            return new Quaternion(axis.X, axis.Y, axis.Z, 0.0);
        }

        Vector3d cross = a.Cross(b);

        return new Quaternion(cross.X, cross.Y, cross.Z, 1.0 + dot).Normalised();
    }

    /// <summary>
    /// Tests whether this quaternion is of unit length, which is the condition under which it
    /// is a rotation and nothing else.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the length is one within the tolerance.</returns>
    public bool IsUnit(in Tolerance tolerance = default) => tolerance.AreEqual(LengthSquared, 1.0);

    /// <summary>
    /// Returns this quaternion scaled to unit length.
    /// </summary>
    /// <returns>The unit quaternion denoting the same rotation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this value is not <see cref="IsValid"/>, which includes
    /// <c>default(Quaternion)</c>.
    /// </exception>
    public Quaternion Normalised()
    {
        if (!TryNormalise(out Quaternion unit))
        {
            throw new InvalidOperationException(
                "A zero or non-finite quaternion is not a rotation and cannot be normalised.");
        }

        return unit;
    }

    /// <summary>
    /// Attempts to scale this quaternion to unit length.
    /// </summary>
    /// <param name="unit">
    /// On success, this quaternion scaled to a length of one. On failure,
    /// <see cref="Identity"/> — a usable rotation rather than a nonsense one, on the same
    /// argument <see cref="Vector3d.TryNormalise(out Vector3d)"/> makes for returning
    /// <see cref="Vector3d.Zero"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when this value denotes a rotation; <see langword="false"/> when
    /// it is all zeros or has a non-finite component.
    /// </returns>
    public bool TryNormalise(out Quaternion unit)
    {
        double scale = Math.Max(Math.Max(Math.Abs(X), Math.Abs(Y)), Math.Max(Math.Abs(Z), Math.Abs(W)));

        if (scale == 0.0 || !double.IsFinite(scale) || !IsValid)
        {
            unit = Identity;
            return false;
        }

        double x = X / scale;
        double y = Y / scale;
        double z = Z / scale;
        double w = W / scale;
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        unit = new Quaternion(x / length, y / length, z / length, w / length);
        return true;
    }

    /// <summary>
    /// Returns this quaternion with its vector part negated.
    /// </summary>
    /// <returns>
    /// The conjugate. For a unit quaternion this is also the inverse rotation, which is why
    /// <see cref="TryGetInverse(out Quaternion)"/> is a normalisation away from this.
    /// </returns>
    public Quaternion Conjugate() => new(-X, -Y, -Z, W);

    /// <summary>
    /// Attempts to produce the rotation that undoes this one.
    /// </summary>
    /// <param name="inverse">
    /// On success, the inverse rotation as a unit quaternion. On failure,
    /// <see cref="Identity"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when this value denotes a rotation; <see langword="false"/> when
    /// it does not, which includes <c>default(Quaternion)</c>.
    /// </returns>
    /// <remarks>
    /// Named to match <see cref="Transform.TryGetInverse(out Transform)"/>. Unlike a general
    /// transform, a rotation always has an inverse, so the only way this returns
    /// <see langword="false"/> is a value that was never a rotation.
    /// </remarks>
    public bool TryGetInverse(out Quaternion inverse)
    {
        if (!TryNormalise(out Quaternion unit))
        {
            inverse = Identity;
            return false;
        }

        inverse = unit.Conjugate();
        return true;
    }

    /// <summary>
    /// Rotates a vector.
    /// </summary>
    /// <param name="vector">The vector to rotate.</param>
    /// <returns>The rotated vector, the same length as the input.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this value is not <see cref="IsValid"/>.
    /// </exception>
    /// <remarks>
    /// Named to match <see cref="Transform.OfVector(in Vector3d)"/> rather than <c>Rotate</c>,
    /// which would read as a command to change the argument. **A non-unit quaternion is handled
    /// rather than rejected**: the formula divides by the squared length, which costs a division
    /// and no square root, so composing a long chain and normalising once at the end is both the
    /// fast path and the accurate one.
    /// </remarks>
    public Vector3d OfVector(in Vector3d vector)
    {
        ThrowIfInvalid();

        Vector3d u = new(X, Y, Z);
        Vector3d t = u.Cross(vector);
        double scale = 2.0 / LengthSquared;

        return vector + (scale * ((W * t) + u.Cross(t)));
    }

    /// <summary>
    /// Rotates a point about the world origin.
    /// </summary>
    /// <param name="point">The point to rotate.</param>
    /// <returns>The rotated point.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this value is not <see cref="IsValid"/>.
    /// </exception>
    /// <remarks>
    /// A quaternion carries no centre, so this always rotates about the origin. Rotating about
    /// some other point is <see cref="Transform.Rotation(in Vector3d, Angle, in Point3d)"/>, or
    /// this composed with a pair of translations — and the fact that you have to say which is
    /// the point of keeping position out of a rotation.
    /// </remarks>
    public Point3d OfPoint(in Point3d point) =>
        Point3d.Origin + OfVector(new Vector3d(point.X, point.Y, point.Z));

    /// <summary>
    /// Composes two rotations. <c>a * b</c> applies <paramref name="b"/> first, then
    /// <paramref name="a"/> — the same order as matrix multiplication and as
    /// <see cref="Transform.operator *(in Transform, in Transform)"/>.
    /// </summary>
    /// <param name="a">The rotation applied second.</param>
    /// <param name="b">The rotation applied first.</param>
    /// <returns>The composed rotation, unnormalised.</returns>
    public static Quaternion operator *(in Quaternion a, in Quaternion b) => new(
        (a.W * b.X) + (a.X * b.W) + (a.Y * b.Z) - (a.Z * b.Y),
        (a.W * b.Y) - (a.X * b.Z) + (a.Y * b.W) + (a.Z * b.X),
        (a.W * b.Z) + (a.X * b.Y) - (a.Y * b.X) + (a.Z * b.W),
        (a.W * b.W) - (a.X * b.X) - (a.Y * b.Y) - (a.Z * b.Z));

    /// <summary>The named alternate to <see cref="operator *(in Quaternion, in Quaternion)"/>.</summary>
    /// <param name="a">The rotation applied second.</param>
    /// <param name="b">The rotation applied first.</param>
    /// <returns>The composed rotation.</returns>
    public static Quaternion Multiply(in Quaternion a, in Quaternion b) => a * b;

    /// <summary>
    /// Interpolates between two rotations along the shortest arc, at constant angular speed.
    /// </summary>
    /// <param name="from">The rotation at <paramref name="parameter"/> zero.</param>
    /// <param name="to">The rotation at <paramref name="parameter"/> one.</param>
    /// <param name="parameter">
    /// Where to sample. Values outside <c>[0, 1]</c> are not clamped and extrapolate along the
    /// same great circle, which is occasionally what an animation wants and is always what the
    /// arithmetic does.
    /// </param>
    /// <returns>The interpolated rotation, as a unit quaternion.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either input is not a rotation, or when <paramref name="parameter"/> is not
    /// finite.
    /// </exception>
    /// <remarks>
    /// <b>The shortest arc is why one input may be negated internally.</b> Because <c>q</c> and
    /// <c>-q</c> are the same rotation, two of the four ways to connect them go the long way
    /// round; taking the sign of the dot product picks the short one. Without it, interpolating
    /// between a rotation and itself-expressed-differently spins through 360°, which is the
    /// classic quaternion animation bug.
    /// </remarks>
    public static Quaternion Slerp(in Quaternion from, in Quaternion to, double parameter)
    {
        if (!from.TryNormalise(out Quaternion a))
        {
            throw new ArgumentException("A quaternion that is not a rotation cannot be interpolated.", nameof(from));
        }

        if (!to.TryNormalise(out Quaternion b))
        {
            throw new ArgumentException("A quaternion that is not a rotation cannot be interpolated.", nameof(to));
        }

        if (!double.IsFinite(parameter))
        {
            throw new ArgumentException("An interpolation parameter must be finite.", nameof(parameter));
        }

        double dot = (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W);

        if (dot < 0.0)
        {
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
            dot = -dot;
        }

        // Nearly parallel: the arc is shorter than the numerical noise in sin(theta), so fall
        // back to straight-line interpolation and renormalise. The two agree to well within a
        // tolerance at this separation.
        if (dot > 1.0 - 1e-12)
        {
            return new Quaternion(
                a.X + ((b.X - a.X) * parameter),
                a.Y + ((b.Y - a.Y) * parameter),
                a.Z + ((b.Z - a.Z) * parameter),
                a.W + ((b.W - a.W) * parameter)).Normalised();
        }

        double theta = Math.Acos(dot);
        double sinTheta = Math.Sin(theta);
        double weightA = Math.Sin((1.0 - parameter) * theta) / sinTheta;
        double weightB = Math.Sin(parameter * theta) / sinTheta;

        return new Quaternion(
            (a.X * weightA) + (b.X * weightB),
            (a.Y * weightA) + (b.Y * weightB),
            (a.Z * weightA) + (b.Z * weightB),
            (a.W * weightA) + (b.W * weightB));
    }

    /// <summary>
    /// Decomposes this rotation into the axis it turns about and the angle it turns through.
    /// </summary>
    /// <returns>
    /// The unit axis and the angle, which is always in <c>[0, π]</c> — the axis carries the
    /// sign. <b>The identity rotation returns <see cref="Vector3d.ZAxis"/> and
    /// <see cref="Angle.Zero"/></b>, because a rotation of nothing turns about no particular
    /// axis and something has to be returned; do not read meaning into that axis.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this value is not <see cref="IsValid"/>.
    /// </exception>
    public (Vector3d Axis, Angle Angle) ToAxisAngle()
    {
        ThrowIfInvalid();

        Quaternion unit = Normalised();

        // Both signs denote the same rotation; taking the positive-scalar one puts the angle in
        // [0, pi] and lets the axis carry the direction.
        if (unit.W < 0.0)
        {
            unit = new Quaternion(-unit.X, -unit.Y, -unit.Z, -unit.W);
        }

        if (!new Vector3d(unit.X, unit.Y, unit.Z).TryNormalise(out Vector3d axis))
        {
            return (Vector3d.ZAxis, Angle.Zero);
        }

        return (axis, Angle.FromRadians(2.0 * Math.Acos(Math.Clamp(unit.W, -1.0, 1.0))));
    }

    /// <summary>
    /// Returns this rotation as a <see cref="Transform"/>.
    /// </summary>
    /// <returns>The equivalent rotation matrix, about the world origin.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this value is not <see cref="IsValid"/>.
    /// </exception>
    public Transform ToTransform()
    {
        ThrowIfInvalid();

        Quaternion q = Normalised();
        double xx = q.X * q.X;
        double yy = q.Y * q.Y;
        double zz = q.Z * q.Z;
        double xy = q.X * q.Y;
        double xz = q.X * q.Z;
        double yz = q.Y * q.Z;
        double wx = q.W * q.X;
        double wy = q.W * q.Y;
        double wz = q.W * q.Z;

        return new Transform(
            1.0 - (2.0 * (yy + zz)), 2.0 * (xy - wz), 2.0 * (xz + wy), 0.0,
            2.0 * (xy + wz), 1.0 - (2.0 * (xx + zz)), 2.0 * (yz - wx), 0.0,
            2.0 * (xz - wy), 2.0 * (yz + wx), 1.0 - (2.0 * (xx + yy)), 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Tests whether this quaternion and another denote the same rotation, which is a
    /// different question from whether they are the same value.
    /// </summary>
    /// <param name="other">The quaternion to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two turn every vector to the same place. Because
    /// <c>q</c> and <c>-q</c> are the same rotation, this accepts a pair that
    /// <see cref="EqualsWithin(in Quaternion, in Tolerance)"/> rejects. Lengths are ignored:
    /// both are normalised first. Returns <see langword="false"/> if either is not a rotation.
    /// </returns>
    public bool IsSameRotation(in Quaternion other, in Tolerance tolerance = default)
    {
        if (!TryNormalise(out Quaternion a) || !other.TryNormalise(out Quaternion b))
        {
            return false;
        }

        return a.EqualsWithin(b, tolerance)
            || a.EqualsWithin(new Quaternion(-b.X, -b.Y, -b.Z, -b.W), tolerance);
    }

    /// <summary>
    /// Tests whether this quaternion and another have the same components within a tolerance.
    /// </summary>
    /// <param name="other">The quaternion to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when all four components agree. This is a question about the
    /// <b>value</b>; <see cref="IsSameRotation(in Quaternion, in Tolerance)"/> is the question
    /// about the rotation, and the two differ on <c>q</c> against <c>-q</c>.
    /// </returns>
    public bool EqualsWithin(in Quaternion other, in Tolerance tolerance = default) =>
        tolerance.AreEqual(X, other.X)
        && tolerance.AreEqual(Y, other.Y)
        && tolerance.AreEqual(Z, other.Z)
        && tolerance.AreEqual(W, other.W);

    /// <summary>Exact component equality, following IEEE 754.</summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns><see langword="true"/> when all four components are exactly equal.</returns>
    public static bool operator ==(in Quaternion left, in Quaternion right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>Exact component inequality.</summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns><see langword="true"/> when any component differs.</returns>
    public static bool operator !=(in Quaternion left, in Quaternion right) => !(left == right);

    /// <inheritdoc/>
    public bool Equals(Quaternion other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Quaternion({X:G6}, {Y:G6}, {Z:G6}, {W:G6})");

    private static Vector3d LeastAlignedWorldAxis(in Vector3d direction)
    {
        double x = Math.Abs(direction.X);
        double y = Math.Abs(direction.Y);
        double z = Math.Abs(direction.Z);

        if (x <= y && x <= z)
        {
            return Vector3d.XAxis;
        }

        return y <= z ? Vector3d.YAxis : Vector3d.ZAxis;
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default-constructed or non-finite Quaternion is not a rotation, so it has no "
                + "axis, no angle and nothing to rotate with. Use Quaternion.Identity for the "
                + "rotation that does nothing.");
        }
    }
}

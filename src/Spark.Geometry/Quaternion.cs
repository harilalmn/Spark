using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A rotation in three-dimensional space, carried as a unit quaternion.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this type is for.</b> <see cref="Transform"/> already represents rotation, and it
/// is what the rest of the kernel consumes. A quaternion exists alongside it for the two jobs
/// a matrix does badly: <b>composing many rotations without drift</b>, because renormalising
/// four numbers is cheap and re-orthonormalising nine is not, and <b>interpolating between two
/// orientations</b>, which <see cref="Slerp(in Quaternion, in Quaternion, double)"/> does
/// along the shortest arc and no amount of matrix blending does at all. Everything else should
/// go on using <see cref="Transform"/>; <see cref="ToTransform"/> and
/// <see cref="ByRotation(in Transform)"/> are the crossings.
/// </para>
/// <para>
/// <b>Two distinct quaternions denote the same rotation, and this is not a rounding
/// artefact.</b> <c>q</c> and <c>-q</c> rotate every vector identically — the unit
/// quaternions double-cover the rotation group — so a rotation of 30° and a rotation of 30°
/// reached the other way round the sphere are equal as rotations and unequal as values.
/// This type refuses to hide that. <c>operator ==</c>, <see cref="Equals(Quaternion)"/> and
/// <see cref="EqualsWithin(in Quaternion, in Tolerance)"/> all compare <b>components</b>, in
/// line with every other value in this namespace, and
/// <see cref="RepresentsSameRotationAs(in Quaternion, in Tolerance)"/> is the separate,
/// explicitly named member that compares <b>rotations</b>. Folding the double cover into
/// equality would have made a hash code impossible to define consistently and would have
/// silently disagreed with <see cref="Transform"/>, where the two are genuinely equal
/// matrices.
/// </para>
/// <para>
/// <b><c>default(Quaternion)</c> is not the identity rotation.</b> It is four zeros, which is
/// no rotation at all, and <see cref="IsValid"/> reports that. The tempting alternative —
/// treating the default as <see cref="Identity"/> — would make an uninitialised field mean
/// "leave it as it is", which is exactly the reading that lets a missing assignment ship. Use
/// <see cref="Identity"/> when identity is what is meant. Every member that needs an actual
/// rotation throws <see cref="InvalidOperationException"/> on an invalid value; equality,
/// hashing, formatting and <see cref="IsValid"/> work on anything, because their job is to
/// describe the value rather than to answer a question about a rotation.
/// </para>
/// <para>
/// <b>Non-unit quaternions are representable and are not rotations.</b> The constructor takes
/// any four finite numbers and does not normalise them, so that
/// <see cref="ByRotation(in Transform)"/> and arithmetic can be checked against what they
/// actually produced. The rotation members — <see cref="Rotate(in Vector3d)"/>,
/// <see cref="ToTransform"/>, <see cref="Axis"/>, <see cref="Angle"/> — normalise on the way
/// in, so a quaternion that has drifted off the unit sphere still rotates correctly rather
/// than quietly scaling its argument.
/// </para>
/// </remarks>
public readonly struct Quaternion : IEquatable<Quaternion>
{
    /// <summary>
    /// Creates a quaternion from its four components. The components are stored as given
    /// and are <b>not</b> normalised.
    /// </summary>
    /// <param name="x">The X component of the vector part.</param>
    /// <param name="y">The Y component of the vector part.</param>
    /// <param name="z">The Z component of the vector part.</param>
    /// <param name="w">The scalar part.</param>
    /// <remarks>
    /// The parameter order is <c>x, y, z, w</c> — vector part first — which is the order the
    /// components are written in almost everywhere outside a mathematics textbook, and the
    /// order Dynamo's own <c>Quaternion</c> uses. The scalar part is nonetheless
    /// <see cref="W"/> and is listed first by <see cref="ToString"/>, because that is how the
    /// value reads: <c>w</c> is the cosine of half the angle and the other three are the axis.
    /// </remarks>
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

    /// <summary>The scalar part, which is the cosine of half the rotation angle.</summary>
    public double W { get; }

    /// <summary>
    /// The rotation that changes nothing.
    /// </summary>
    public static Quaternion Identity => new(0.0, 0.0, 0.0, 1.0);

    /// <summary>
    /// Whether this value denotes a rotation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every component is finite and the four are not all zero.
    /// This is deliberately not a unit-length test: a quaternion that has drifted off the
    /// unit sphere still names a rotation, and <see cref="IsUnit(in Tolerance)"/> is the
    /// separate question of whether it is normalised.
    /// </returns>
    public bool IsValid =>
        double.IsFinite(X)
        && double.IsFinite(Y)
        && double.IsFinite(Z)
        && double.IsFinite(W)
        && LengthSquared > 0.0;

    /// <summary>
    /// The quaternion's norm. A rotation has a norm of one.
    /// </summary>
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>
    /// The squared norm, which avoids the square root where only a comparison is needed.
    /// </summary>
    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z) + (W * W);

    /// <summary>
    /// The axis this quaternion rotates about.
    /// </summary>
    /// <returns>
    /// The unit axis, oriented so that <see cref="Angle"/> is the counter-clockwise rotation
    /// about it when viewed from its positive end. <b>The identity rotation has no axis</b>
    /// and reports <see cref="Vector3d.ZAxis"/>, chosen so that the pair
    /// <c>(<see cref="Axis"/>, <see cref="Angle"/>)</c> is always usable as an argument to
    /// <see cref="Transform.Rotation(in Vector3d, Angle)"/> — a zero angle about any axis is
    /// the identity, so the arbitrary choice is harmless there and a zero vector would not be.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this quaternion is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Vector3d Axis
    {
        get
        {
            Quaternion q = Canonical();

            return new Vector3d(q.X, q.Y, q.Z).TryNormalise(out Vector3d axis)
                ? axis
                : Vector3d.ZAxis;
        }
    }

    /// <summary>
    /// The angle this quaternion rotates through, about <see cref="Axis"/>.
    /// </summary>
    /// <returns>
    /// An angle in <c>[0, π]</c>, always. <b>A quaternion cannot report a rotation of more
    /// than half a turn</b>: past that point the shorter rotation about the opposite axis is
    /// the same rotation, and it is the one that comes back. A caller who needs to know that
    /// a mechanism turned through 350° rather than −10° needs to track that itself; the
    /// rotation does not carry it, and neither does a matrix.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this quaternion is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Angle Angle
    {
        get
        {
            Quaternion q = Canonical();

            // atan2 of the vector length against the scalar part, rather than acos(w): near
            // zero and near half a turn, acos loses most of its significant digits because
            // its argument is flat there, and the vector length is well conditioned exactly
            // where the scalar part is not.
            double vector = Math.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z));

            return Geometry.Angle.FromRadians(2.0 * Math.Atan2(vector, q.W));
        }
    }

    /// <summary>
    /// Creates the rotation about an axis through the world origin.
    /// </summary>
    /// <param name="axis">The rotation axis. Need not be normalised.</param>
    /// <param name="angle">
    /// The rotation angle. Positive angles rotate counter-clockwise when viewed from the
    /// positive end of <paramref name="axis"/> looking back towards the origin, which is the
    /// same convention <see cref="Transform.Rotation(in Vector3d, Angle)"/> follows.
    /// </param>
    /// <returns>The unit quaternion denoting that rotation.</returns>
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

        double half = angle.Radians / 2.0;
        double s = Math.Sin(half);

        return new Quaternion(k.X * s, k.Y * s, k.Z * s, Math.Cos(half));
    }

    /// <summary>
    /// Creates the shortest rotation taking one direction to another.
    /// </summary>
    /// <param name="from">The starting direction. Need not be normalised.</param>
    /// <param name="to">The finishing direction. Need not be normalised.</param>
    /// <returns>
    /// The unit quaternion <c>q</c> for which <c>q.Rotate(from)</c> is parallel to and in the
    /// same direction as <paramref name="to"/>, rotating about the axis perpendicular to
    /// both. Lengths are ignored: only the directions matter.
    /// </returns>
    /// <remarks>
    /// <b>Opposite directions have no shortest rotation, and this member picks one rather
    /// than throwing.</b> Every half turn about every axis perpendicular to
    /// <paramref name="from"/> takes it to <paramref name="to"/>, and they are all equally
    /// short; a perpendicular is chosen deterministically from the axis
    /// <paramref name="from"/> is least aligned with, so the same pair always gives the same
    /// answer. Throwing was rejected because the antiparallel case arises constantly when
    /// aligning normals, and a caller who cares which perpendicular is used already has one
    /// and should call <see cref="ByAxisAngle(in Vector3d, Angle)"/> with it.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when either direction is zero-length or non-finite.
    /// </exception>
    public static Quaternion ByTwoVectors(in Vector3d from, in Vector3d to)
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

        Vector3d cross = a.Cross(b);
        double dot = a.Dot(b);

        // The cross product vanishes for parallel and for antiparallel directions alike, so
        // the sign of the dot product is what separates "already there" from "half a turn".
        if (cross.TryNormalise(out Vector3d axis))
        {
            return ByAxisAngle(axis, Geometry.Angle.FromRadians(Math.Atan2(cross.Length, dot)));
        }

        if (dot > 0.0)
        {
            return Identity;
        }

        // Cross with the world axis this direction is least aligned with: the result is the
        // furthest from degenerate that a fixed choice can be, so the normalisation below
        // never runs out of significant digits.
        double ax = Math.Abs(a.X);
        double ay = Math.Abs(a.Y);
        double az = Math.Abs(a.Z);
        Vector3d least = ax <= ay && ax <= az
            ? Vector3d.XAxis
            : ay <= az ? Vector3d.YAxis : Vector3d.ZAxis;

        return ByAxisAngle(a.Cross(least), Geometry.Angle.HalfTurn);
    }

    /// <summary>
    /// Reads the rotation out of a rigid transform.
    /// </summary>
    /// <param name="transform">
    /// The transform to read. Its translation is ignored, and its linear part must be a
    /// rotation.
    /// </param>
    /// <returns>The unit quaternion denoting the same rotation.</returns>
    /// <remarks>
    /// The sign convention is that <see cref="W"/> comes back non-negative, so this member
    /// and <see cref="ToTransform"/> round-trip a transform exactly but round-trip a
    /// quaternion only up to the double cover — <c>-q</c> goes in and <c>q</c> comes back.
    /// That is a property of rotations rather than of this implementation, and
    /// <see cref="RepresentsSameRotationAs(in Quaternion, in Tolerance)"/> is what a test of
    /// the round trip should use.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when the transform's linear part is not a rotation — that is, when it is not
    /// orthonormal, or when it reverses handedness. A reflection is rejected rather than
    /// silently dropped: no quaternion denotes one, and returning the rotation part of a
    /// mirror would answer a question the caller did not ask.
    /// </exception>
    public static Quaternion ByRotation(in Transform transform)
    {
        if (!transform.IsRigid())
        {
            // IsRigid already excludes reflections through its determinant test, so the two
            // failures are distinguished here rather than there: they are equally invalid and
            // a caller who mirrored something is looking for a different mistake from one who
            // scaled or sheared it.
            throw new ArgumentException(
                transform.IsAffine() && transform.Determinant < 0.0
                    ? "This transform reverses handedness, and no quaternion denotes a reflection."
                    : "Only a rigid transform has a rotation to read; this one is not orthonormal.",
                nameof(transform));
        }

        // Shepperd's method: form the quaternion from whichever of the four diagonal
        // combinations is largest, so the divisor is never small. Reading it from the trace
        // alone loses all precision near a half turn, where the trace approaches -1.
        double trace = transform.M00 + transform.M11 + transform.M22;

        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;

            return new Quaternion(
                (transform.M21 - transform.M12) / s,
                (transform.M02 - transform.M20) / s,
                (transform.M10 - transform.M01) / s,
                0.25 * s).Normalised();
        }

        if (transform.M00 > transform.M11 && transform.M00 > transform.M22)
        {
            double s = Math.Sqrt(1.0 + transform.M00 - transform.M11 - transform.M22) * 2.0;

            return new Quaternion(
                0.25 * s,
                (transform.M01 + transform.M10) / s,
                (transform.M02 + transform.M20) / s,
                (transform.M21 - transform.M12) / s).Normalised();
        }

        if (transform.M11 > transform.M22)
        {
            double s = Math.Sqrt(1.0 + transform.M11 - transform.M00 - transform.M22) * 2.0;

            return new Quaternion(
                (transform.M01 + transform.M10) / s,
                0.25 * s,
                (transform.M12 + transform.M21) / s,
                (transform.M02 - transform.M20) / s).Normalised();
        }

        double t = Math.Sqrt(1.0 + transform.M22 - transform.M00 - transform.M11) * 2.0;

        return new Quaternion(
            (transform.M02 + transform.M20) / t,
            (transform.M12 + transform.M21) / t,
            0.25 * t,
            (transform.M10 - transform.M01) / t).Normalised();
    }

    /// <summary>
    /// Scales this quaternion to unit norm.
    /// </summary>
    /// <returns>The unit quaternion denoting the same rotation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this quaternion is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Quaternion Normalised()
    {
        if (!TryNormalise(out Quaternion unit))
        {
            throw new InvalidOperationException(
                "A zero or non-finite quaternion denotes no rotation and cannot be normalised.");
        }

        return unit;
    }

    /// <summary>
    /// Attempts to scale this quaternion to unit norm.
    /// </summary>
    /// <param name="unit">
    /// On success, this quaternion scaled to a norm of one. On failure,
    /// <see cref="Identity"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the quaternion denotes a rotation; <see langword="false"/>
    /// when it is all zeros or has a non-finite component.
    /// </returns>
    public bool TryNormalise(out Quaternion unit)
    {
        // Divide by the largest component before squaring, exactly as Vector3d does: the
        // norm of a quaternion whose components are near double.MaxValue overflows to
        // infinity otherwise, and the answer is then a quaternion of zeros.
        double scale = Math.Max(
            Math.Max(Math.Abs(X), Math.Abs(Y)),
            Math.Max(Math.Abs(Z), Math.Abs(W)));

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
    /// Tests whether this quaternion is already normalised.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the norm is one to within the tolerance.</returns>
    public bool IsUnit(in Tolerance tolerance = default) => tolerance.AreEqual(Length, 1.0);

    /// <summary>
    /// Returns this quaternion with its vector part negated.
    /// </summary>
    /// <returns>
    /// The conjugate. For a unit quaternion this is the inverse rotation, which is why
    /// <see cref="Inverse"/> exists as a separate member: they coincide only on the unit
    /// sphere, and conflating them is the classic way to get a rotation that also scales.
    /// <b>They coincide to within rounding rather than bitwise</b>, even for a quaternion a
    /// factory here produced — <see cref="ByAxisAngle(in Vector3d, Angle)"/> at a quarter
    /// turn has a squared norm of 0.9999999999999998, and dividing by that moves the last
    /// bit. Compare the two with <see cref="EqualsWithin(in Quaternion, in Tolerance)"/>.
    /// </returns>
    public Quaternion Conjugate() => new(-X, -Y, -Z, W);

    /// <summary>
    /// Returns the rotation that undoes this one.
    /// </summary>
    /// <returns>
    /// The inverse: the conjugate divided by the squared norm, so that composing it with this
    /// quaternion gives <see cref="Identity"/> whether or not this one was normalised.
    /// <b>The result is a unit quaternion only when the input was one</b> — a quaternion of
    /// norm 3 inverts to one of norm one third, which is what makes the composition come out
    /// at the identity rather than at the identity scaled.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this quaternion is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Quaternion Inverse()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A zero or non-finite quaternion denotes no rotation and cannot be inverted.");
        }

        // The algebraic inverse, conjugate over squared norm, rather than the conjugate of
        // the normalised value. The two agree on the unit sphere and differ everywhere else,
        // and this one is the definition that makes q * q.Inverse() the identity for ANY
        // valid q: normalising first would give a unit quaternion whose product with the
        // original is the identity scaled by the squared norm.
        double squared = LengthSquared;

        return new Quaternion(-X / squared, -Y / squared, -Z / squared, W / squared);
    }

    /// <summary>
    /// Rotates a vector by this rotation.
    /// </summary>
    /// <param name="vector">The vector to rotate.</param>
    /// <returns>
    /// The rotated vector, with its length preserved. The quaternion is normalised first, so
    /// a value that has drifted off the unit sphere rotates rather than also scaling.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this quaternion is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Vector3d Rotate(in Vector3d vector)
    {
        Quaternion q = Normalised();
        Vector3d u = new(q.X, q.Y, q.Z);

        // v + 2u x (u x v + wv). Two cross products rather than the sandwich product q v q*,
        // which is the same answer for a third fewer multiplications and no temporaries that
        // are quaternions.
        Vector3d t = u.Cross(vector) + (vector * q.W);

        return vector + (u.Cross(t) * 2.0);
    }

    /// <summary>
    /// Converts this rotation to the equivalent transform.
    /// </summary>
    /// <returns>
    /// A rotation transform about the world origin. Composing transforms and composing
    /// quaternions agree: <c>(a * b).ToTransform()</c> equals
    /// <c>a.ToTransform() * b.ToTransform()</c>, to within floating-point rounding.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this quaternion is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Transform ToTransform()
    {
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
    /// The four-component dot product.
    /// </summary>
    /// <param name="other">The quaternion to take the dot product with.</param>
    /// <returns>
    /// The dot product. For two unit quaternions this is the cosine of half the angle between
    /// the rotations, and its <b>sign</b> is what says which of <c>other</c> and
    /// <c>-other</c> is the near end of the double cover — which is why
    /// <see cref="Slerp(in Quaternion, in Quaternion, double)"/> consults it.
    /// </returns>
    public double Dot(in Quaternion other) =>
        (X * other.X) + (Y * other.Y) + (Z * other.Z) + (W * other.W);

    /// <summary>
    /// Interpolates along the shortest arc between two rotations.
    /// </summary>
    /// <param name="from">The rotation at <paramref name="t"/> of zero.</param>
    /// <param name="to">The rotation at <paramref name="t"/> of one.</param>
    /// <param name="t">
    /// The parameter. Values outside <c>[0, 1]</c> extrapolate along the same great circle
    /// rather than being clamped, which is what makes this usable for easing curves that
    /// overshoot.
    /// </param>
    /// <returns>
    /// The interpolated unit rotation. <b>Angular speed is constant</b>, which is the whole
    /// reason to prefer this over interpolating components: a component-wise blend of two
    /// rotations traverses a chord rather than the arc, and arrives visibly early in the
    /// middle of the motion.
    /// </returns>
    /// <remarks>
    /// <b>The shortest arc is chosen, so <c>Slerp(a, b, t)</c> and <c>Slerp(a, -b, t)</c>
    /// give the same result</b> even though <c>b</c> and <c>-b</c> are unequal values: the
    /// far end is negated when the dot product is negative. Without that, half of all pairs
    /// of endpoints — the half nobody thinks to test — would take the long way round, which
    /// is a rotation of up to 359° in place of one of 1°.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either quaternion is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="t"/> is not finite.
    /// </exception>
    public static Quaternion Slerp(in Quaternion from, in Quaternion to, double t)
    {
        if (!double.IsFinite(t))
        {
            throw new ArgumentException("An interpolation parameter must be finite.", nameof(t));
        }

        Quaternion a = from.Normalised();
        Quaternion b = to.Normalised();

        double dot = a.Dot(b);

        if (dot < 0.0)
        {
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);
            dot = -dot;
        }

        // Near-coincident endpoints leave sin(theta) too small to divide by, and the arc is
        // then indistinguishable from the chord anyway. Blend linearly and renormalise: the
        // error is below the precision the slerp itself would have had.
        const double Coincident = 1.0 - 1e-12;

        if (dot > Coincident)
        {
            return new Quaternion(
                a.X + ((b.X - a.X) * t),
                a.Y + ((b.Y - a.Y) * t),
                a.Z + ((b.Z - a.Z) * t),
                a.W + ((b.W - a.W) * t)).Normalised();
        }

        double theta = Math.Acos(Math.Clamp(dot, -1.0, 1.0));
        double sinTheta = Math.Sin(theta);
        double scaleFrom = Math.Sin((1.0 - t) * theta) / sinTheta;
        double scaleTo = Math.Sin(t * theta) / sinTheta;

        return new Quaternion(
            (a.X * scaleFrom) + (b.X * scaleTo),
            (a.Y * scaleFrom) + (b.Y * scaleTo),
            (a.Z * scaleFrom) + (b.Z * scaleTo),
            (a.W * scaleFrom) + (b.W * scaleTo)).Normalised();
    }

    /// <summary>
    /// Composes two rotations.
    /// </summary>
    /// <param name="left">The rotation applied second.</param>
    /// <param name="right">The rotation applied first.</param>
    /// <returns>
    /// The rotation equivalent to applying <paramref name="right"/> and then
    /// <paramref name="left"/>. <b>The order matches <see cref="Transform"/>'s
    /// <c>operator *</c> and matrix convention</b>, and it is the opposite of the order the
    /// operands read in. Rotation is not commutative, so this is not a detail.
    /// </returns>
    public static Quaternion operator *(in Quaternion left, in Quaternion right) => new(
        (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
        (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
        (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W),
        (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z));

    /// <summary>
    /// Composes two rotations. The named alternate to <c>operator *</c>.
    /// </summary>
    /// <param name="left">The rotation applied second.</param>
    /// <param name="right">The rotation applied first.</param>
    /// <returns>The composition.</returns>
    public static Quaternion Multiply(in Quaternion left, in Quaternion right) => left * right;

    /// <summary>
    /// Rotates a vector. The operator form of <see cref="Rotate(in Vector3d)"/>.
    /// </summary>
    /// <param name="rotation">The rotation to apply.</param>
    /// <param name="vector">The vector to rotate.</param>
    /// <returns>The rotated vector.</returns>
    public static Vector3d operator *(in Quaternion rotation, in Vector3d vector) =>
        rotation.Rotate(vector);

    /// <summary>
    /// Tests whether two quaternions denote the same rotation, allowing for the fact that
    /// <c>q</c> and <c>-q</c> do.
    /// </summary>
    /// <param name="other">The rotation to compare against.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two rotate every vector to the same place. This is
    /// <b>not</b> what <see cref="EqualsWithin(in Quaternion, in Tolerance)"/> answers, and
    /// the difference is the whole reason both exist: a rotation has two representations and
    /// a value has one.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either quaternion is not valid.
    /// </exception>
    public bool RepresentsSameRotationAs(in Quaternion other, in Tolerance tolerance = default)
    {
        // |dot| of two unit quaternions is 1 exactly when they denote the same rotation, and
        // the absolute value is what folds the double cover in. Comparing componentwise
        // against both other and -other would work too and would need the caller to know
        // that -other exists, which is the knowledge this member is here to hold.
        return tolerance.AreEqual(Math.Abs(Normalised().Dot(other.Normalised())), 1.0);
    }

    /// <summary>
    /// Compares two quaternions componentwise within a tolerance.
    /// </summary>
    /// <param name="other">The quaternion to compare against.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when all four components agree. <b>Two quaternions denoting
    /// the same rotation can fail this test</b> — see
    /// <see cref="RepresentsSameRotationAs(in Quaternion, in Tolerance)"/>.
    /// </returns>
    public bool EqualsWithin(in Quaternion other, in Tolerance tolerance = default) =>
        tolerance.AreEqual(X, other.X)
        && tolerance.AreEqual(Y, other.Y)
        && tolerance.AreEqual(Z, other.Z)
        && tolerance.AreEqual(W, other.W);

    /// <summary>
    /// Exact componentwise equality, following IEEE 754: a quaternion holding
    /// <see cref="double.NaN"/> is equal to nothing, including itself.
    /// </summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns><see langword="true"/> when all four components are exactly equal.</returns>
    public static bool operator ==(in Quaternion left, in Quaternion right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>
    /// The negation of <c>operator ==</c>.
    /// </summary>
    /// <param name="left">The first quaternion.</param>
    /// <param name="right">The second quaternion.</param>
    /// <returns><see langword="true"/> when the two are not exactly equal.</returns>
    public static bool operator !=(in Quaternion left, in Quaternion right) => !(left == right);

    /// <summary>
    /// Componentwise equality treating <see cref="double.NaN"/> as equal to itself, so that
    /// quaternions stay usable as dictionary keys.
    /// </summary>
    /// <param name="other">The quaternion to compare against.</param>
    /// <returns><see langword="true"/> when all four components are equal.</returns>
    public bool Equals(Quaternion other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <summary>
    /// Formats the components, using the invariant culture.
    /// </summary>
    /// <returns>
    /// A string of the form <c>(w: 1, x: 0, y: 0, z: 0)</c>. The components are named because
    /// there is no ordering convention a reader can rely on — this type takes <c>x, y, z, w</c>
    /// and mathematics writes <c>w, x, y, z</c> — and an unlabelled tuple of four numbers is
    /// ambiguous in exactly the way that matters.
    /// </returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"(w: {W}, x: {X}, y: {Y}, z: {Z})");

    // The unit representative of this rotation whose scalar part is non-negative.
    //
    // Axis and Angle both need it, and neither is correct without it. A quaternion with a
    // negative W denotes the same rotation as its negation, but reading the axis straight off
    // its vector part gives the OPPOSITE axis while the angle comes back positive - which
    // describes the rotation backwards. Taking the absolute value of W in the angle and
    // leaving the axis alone fixes the number and not the sign, and the pair then names a
    // rotation the quaternion does not perform.
    private Quaternion Canonical()
    {
        Quaternion q = Normalised();

        return q.W < 0.0 ? new Quaternion(-q.X, -q.Y, -q.Z, -q.W) : q;
    }
}

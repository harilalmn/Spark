using System;
using System.Globalization;
using System.Text;

namespace Spark.Geometry;

/// <summary>
/// A 4x4 transformation matrix: rotation, scale, shear, reflection, translation and
/// projection, composable and invertible.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convention.</b> Points and vectors are treated as <b>column</b> vectors and the matrix
/// multiplies from the left, so applying a transform is <c>p' = M p</c> and the translation
/// lives in the fourth <i>column</i>: <see cref="M03"/>, <see cref="M13"/> and
/// <see cref="M23"/>. Composition follows the same reading order as the matrix algebra:
/// <c>a * b</c> is the transform that applies <b>b first and then a</b>. The naming
/// <c>Mrc</c> is row then column, so <see cref="M12"/> is row 1, column 2.
/// </para>
/// <para>
/// <b>Points and vectors transform differently.</b> <see cref="OfPoint(in Point3d)"/> applies
/// the whole matrix including the translation; <see cref="OfVector(in Vector3d)"/> applies
/// only the upper-left 3x3 and <b>ignores translation entirely</b>, because a direction has
/// no position to move. This is the single most common source of quiet errors in transform
/// code, and it is the reason <see cref="Point3d"/> and <see cref="Vector3d"/> are separate
/// types with no implicit conversion between them.
/// </para>
/// <para>
/// This type is written from scratch. The seed library's <c>VTransform</c> is mutable, holds
/// no matrix, and has no composition, inverse, scale or translation, so there was nothing to
/// harvest.
/// </para>
/// </remarks>
public readonly struct Transform : IEquatable<Transform>
{
    /// <summary>
    /// Creates a transform from its sixteen entries, given row by row.
    /// </summary>
    /// <param name="m00">Row 0, column 0.</param>
    /// <param name="m01">Row 0, column 1.</param>
    /// <param name="m02">Row 0, column 2.</param>
    /// <param name="m03">Row 0, column 3 — the X translation for an affine transform.</param>
    /// <param name="m10">Row 1, column 0.</param>
    /// <param name="m11">Row 1, column 1.</param>
    /// <param name="m12">Row 1, column 2.</param>
    /// <param name="m13">Row 1, column 3 — the Y translation for an affine transform.</param>
    /// <param name="m20">Row 2, column 0.</param>
    /// <param name="m21">Row 2, column 1.</param>
    /// <param name="m22">Row 2, column 2.</param>
    /// <param name="m23">Row 2, column 3 — the Z translation for an affine transform.</param>
    /// <param name="m30">Row 3, column 0. Zero for an affine transform.</param>
    /// <param name="m31">Row 3, column 1. Zero for an affine transform.</param>
    /// <param name="m32">Row 3, column 2. Zero for an affine transform.</param>
    /// <param name="m33">Row 3, column 3. One for an affine transform.</param>
    public Transform(
        double m00, double m01, double m02, double m03,
        double m10, double m11, double m12, double m13,
        double m20, double m21, double m22, double m23,
        double m30, double m31, double m32, double m33)
    {
        M00 = m00;
        M01 = m01;
        M02 = m02;
        M03 = m03;
        M10 = m10;
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M20 = m20;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M30 = m30;
        M31 = m31;
        M32 = m32;
        M33 = m33;
    }

    /// <summary>Row 0, column 0.</summary>
    public double M00 { get; }

    /// <summary>Row 0, column 1.</summary>
    public double M01 { get; }

    /// <summary>Row 0, column 2.</summary>
    public double M02 { get; }

    /// <summary>Row 0, column 3: the X translation for an affine transform.</summary>
    public double M03 { get; }

    /// <summary>Row 1, column 0.</summary>
    public double M10 { get; }

    /// <summary>Row 1, column 1.</summary>
    public double M11 { get; }

    /// <summary>Row 1, column 2.</summary>
    public double M12 { get; }

    /// <summary>Row 1, column 3: the Y translation for an affine transform.</summary>
    public double M13 { get; }

    /// <summary>Row 2, column 0.</summary>
    public double M20 { get; }

    /// <summary>Row 2, column 1.</summary>
    public double M21 { get; }

    /// <summary>Row 2, column 2.</summary>
    public double M22 { get; }

    /// <summary>Row 2, column 3: the Z translation for an affine transform.</summary>
    public double M23 { get; }

    /// <summary>Row 3, column 0. Zero for an affine transform.</summary>
    public double M30 { get; }

    /// <summary>Row 3, column 1. Zero for an affine transform.</summary>
    public double M31 { get; }

    /// <summary>Row 3, column 2. Zero for an affine transform.</summary>
    public double M32 { get; }

    /// <summary>Row 3, column 3. One for an affine transform.</summary>
    public double M33 { get; }

    /// <summary>
    /// The identity transform, which leaves every point and vector unchanged.
    /// </summary>
    /// <remarks>
    /// Note that this is <b>not</b> <c>default(Transform)</c>, which is the all-zero matrix
    /// and collapses everything to the origin. Always start from <see cref="Identity"/>.
    /// </remarks>
    public static Transform Identity => new(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0);

    /// <summary>
    /// Reads a single entry by row and column.
    /// </summary>
    /// <param name="row">The row index, from 0 to 3.</param>
    /// <param name="column">The column index, from 0 to 3.</param>
    /// <returns>The entry at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when either index is outside the range 0 to 3.
    /// </exception>
    public double this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(row, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(row, 3);
            ArgumentOutOfRangeException.ThrowIfLessThan(column, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(column, 3);

            return At(row, column);
        }
    }

    /// <summary>
    /// The determinant of the matrix.
    /// </summary>
    /// <remarks>
    /// For an affine transform this is the factor by which volumes are multiplied. It is
    /// negative exactly when the transform reverses handedness, as a mirror does, and zero
    /// when the transform is singular and therefore has no inverse.
    /// </remarks>
    public double Determinant
    {
        get
        {
            double s0 = (M00 * M11) - (M01 * M10);
            double s1 = (M00 * M12) - (M02 * M10);
            double s2 = (M00 * M13) - (M03 * M10);
            double s3 = (M01 * M12) - (M02 * M11);
            double s4 = (M01 * M13) - (M03 * M11);
            double s5 = (M02 * M13) - (M03 * M12);

            double c5 = (M22 * M33) - (M23 * M32);
            double c4 = (M21 * M33) - (M23 * M31);
            double c3 = (M21 * M32) - (M22 * M31);
            double c2 = (M20 * M33) - (M23 * M30);
            double c1 = (M20 * M32) - (M22 * M30);
            double c0 = (M20 * M31) - (M21 * M30);

            return (s0 * c5) - (s1 * c4) + (s2 * c3) + (s3 * c2) - (s4 * c1) + (s5 * c0);
        }
    }

    /// <summary>
    /// Creates a transform that moves everything by a displacement.
    /// </summary>
    /// <param name="offset">The displacement to apply to points. Vectors are unaffected.</param>
    /// <returns>The translation transform.</returns>
    public static Transform Translation(in Vector3d offset) => new(
        1.0, 0.0, 0.0, offset.X,
        0.0, 1.0, 0.0, offset.Y,
        0.0, 0.0, 1.0, offset.Z,
        0.0, 0.0, 0.0, 1.0);

    /// <summary>
    /// Creates a transform that moves everything by a displacement given component-wise.
    /// </summary>
    /// <param name="x">The X displacement.</param>
    /// <param name="y">The Y displacement.</param>
    /// <param name="z">The Z displacement.</param>
    /// <returns>The translation transform.</returns>
    public static Transform Translation(double x, double y, double z) =>
        Translation(new Vector3d(x, y, z));

    /// <summary>
    /// Creates a uniform scale about the world origin.
    /// </summary>
    /// <param name="factor">
    /// The scale factor. A factor of one is the identity, a negative factor also reflects
    /// through the origin, and a factor of zero collapses everything to the origin and
    /// produces a singular transform that cannot be inverted.
    /// </param>
    /// <returns>The scale transform.</returns>
    public static Transform Scale(double factor) => Scale(factor, factor, factor);

    /// <summary>
    /// Creates a non-uniform scale about the world origin.
    /// </summary>
    /// <param name="x">The scale factor along the world X axis.</param>
    /// <param name="y">The scale factor along the world Y axis.</param>
    /// <param name="z">The scale factor along the world Z axis.</param>
    /// <returns>
    /// The scale transform. A non-uniform scale does not preserve angles, so normals do not
    /// transform by it — they transform by the inverse transpose. Nothing here does that for
    /// you.
    /// </returns>
    public static Transform Scale(double x, double y, double z) => new(
        x, 0.0, 0.0, 0.0,
        0.0, y, 0.0, 0.0,
        0.0, 0.0, z, 0.0,
        0.0, 0.0, 0.0, 1.0);

    /// <summary>
    /// Creates a uniform scale about a fixed point.
    /// </summary>
    /// <param name="centre">The point that stays where it is.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scale transform.</returns>
    public static Transform Scale(in Point3d centre, double factor) =>
        Scale(centre, factor, factor, factor);

    /// <summary>
    /// Creates a non-uniform scale about a fixed point.
    /// </summary>
    /// <param name="centre">The point that stays where it is.</param>
    /// <param name="x">The scale factor along the world X axis.</param>
    /// <param name="y">The scale factor along the world Y axis.</param>
    /// <param name="z">The scale factor along the world Z axis.</param>
    /// <returns>The scale transform.</returns>
    public static Transform Scale(in Point3d centre, double x, double y, double z)
    {
        Vector3d offset = (Vector3d)centre;

        return Translation(offset) * Scale(x, y, z) * Translation(-offset);
    }

    /// <summary>
    /// Creates a rotation about an axis through the world origin.
    /// </summary>
    /// <param name="axis">The rotation axis. Need not be normalised.</param>
    /// <param name="angle">
    /// The rotation angle. Positive angles rotate counter-clockwise when viewed from the
    /// positive end of <paramref name="axis"/> looking back towards the origin.
    /// </param>
    /// <returns>The rotation transform.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="axis"/> is zero-length or non-finite.
    /// </exception>
    public static Transform Rotation(in Vector3d axis, Angle angle)
    {
        if (!axis.TryNormalise(out Vector3d k))
        {
            throw new ArgumentException(
                "A rotation axis must have non-zero length and finite components.",
                nameof(axis));
        }

        double c = Math.Cos(angle.Radians);
        double s = Math.Sin(angle.Radians);
        double t = 1.0 - c;

        return new Transform(
            c + (t * k.X * k.X), (t * k.X * k.Y) - (s * k.Z), (t * k.X * k.Z) + (s * k.Y), 0.0,
            (t * k.Y * k.X) + (s * k.Z), c + (t * k.Y * k.Y), (t * k.Y * k.Z) - (s * k.X), 0.0,
            (t * k.Z * k.X) - (s * k.Y), (t * k.Z * k.Y) + (s * k.X), c + (t * k.Z * k.Z), 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Creates a rotation about an axis through a given point.
    /// </summary>
    /// <param name="axis">The rotation axis direction. Need not be normalised.</param>
    /// <param name="angle">
    /// The rotation angle, counter-clockwise when viewed from the positive end of the axis.
    /// </param>
    /// <param name="centre">A point the axis passes through, which stays where it is.</param>
    /// <returns>The rotation transform.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="axis"/> is zero-length or non-finite.
    /// </exception>
    public static Transform Rotation(in Vector3d axis, Angle angle, in Point3d centre)
    {
        Vector3d offset = (Vector3d)centre;

        return Translation(offset) * Rotation(axis, angle) * Translation(-offset);
    }

    /// <summary>
    /// Creates a reflection in a plane.
    /// </summary>
    /// <param name="plane">The mirror plane. Points on it stay where they are.</param>
    /// <returns>
    /// The mirror transform. Its determinant is negative because a reflection reverses
    /// handedness, which is why it is not considered rigid by
    /// <see cref="IsRigid(in Tolerance)"/>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    public static Transform Mirror(in Plane plane)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("A mirror plane must be valid.", nameof(plane));
        }

        Vector3d n = plane.Normal;
        double d = 2.0 * ((Vector3d)plane.Origin).Dot(n);

        return new Transform(
            1.0 - (2.0 * n.X * n.X), -2.0 * n.X * n.Y, -2.0 * n.X * n.Z, d * n.X,
            -2.0 * n.Y * n.X, 1.0 - (2.0 * n.Y * n.Y), -2.0 * n.Y * n.Z, d * n.Y,
            -2.0 * n.Z * n.X, -2.0 * n.Z * n.Y, 1.0 - (2.0 * n.Z * n.Z), d * n.Z,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Creates the rigid transform that carries one plane onto another.
    /// </summary>
    /// <param name="from">The plane the geometry is currently laid out on.</param>
    /// <param name="to">The plane it should end up on.</param>
    /// <returns>
    /// The transform mapping <paramref name="from"/>'s origin to <paramref name="to"/>'s
    /// origin and <paramref name="from"/>'s three axes onto <paramref name="to"/>'s. Because
    /// both frames are orthonormal and right-handed, the result is always a rotation followed
    /// by a translation, with no scaling and no reflection.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when either plane is not valid.</exception>
    public static Transform PlaneToPlane(in Plane from, in Plane to)
    {
        if (!from.IsValid)
        {
            throw new ArgumentException("The source plane must be valid.", nameof(from));
        }

        if (!to.IsValid)
        {
            throw new ArgumentException("The target plane must be valid.", nameof(to));
        }

        return LocalToWorld(to) * ChangeBasis(from);
    }

    /// <summary>
    /// Creates the transform that expresses world coordinates in a plane's own frame.
    /// </summary>
    /// <param name="plane">The frame to express coordinates in.</param>
    /// <returns>
    /// The transform that maps the plane's origin to the world origin and its X, Y and normal
    /// axes to the world X, Y and Z axes. It inverts
    /// <c>PlaneToPlane(Plane.WorldXY, plane)</c> <b>to within floating-point rounding rather
    /// than exactly</b> — composing the two gives a matrix that is the identity only up to a
    /// few units in the last place, and proportionally more for planes far from the origin.
    /// Applying it to a point gives that point's coordinates in the plane's frame, with the
    /// third component being the point's signed distance from the plane.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    public static Transform ChangeBasis(in Plane plane)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("The basis plane must be valid.", nameof(plane));
        }

        Vector3d x = plane.XAxis;
        Vector3d y = plane.YAxis;
        Vector3d z = plane.Normal;
        Vector3d o = (Vector3d)plane.Origin;

        // The rows are the plane's axes, so the linear part is the transpose of — and for an
        // orthonormal frame therefore the inverse of — the local-to-world rotation.
        return new Transform(
            x.X, x.Y, x.Z, -x.Dot(o),
            y.X, y.Y, y.Z, -y.Dot(o),
            z.X, z.Y, z.Z, -z.Dot(o),
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Composes two transforms.
    /// </summary>
    /// <param name="left">The transform applied second.</param>
    /// <param name="right">The transform applied first.</param>
    /// <returns>
    /// The matrix product <c>left * right</c>, which is the transform that applies
    /// <paramref name="right"/> and then <paramref name="left"/>. Composition is not
    /// commutative: rotating and then translating is not the same as translating and then
    /// rotating.
    /// </returns>
    public static Transform operator *(in Transform left, in Transform right) => new(
        (left.M00 * right.M00) + (left.M01 * right.M10) + (left.M02 * right.M20) + (left.M03 * right.M30),
        (left.M00 * right.M01) + (left.M01 * right.M11) + (left.M02 * right.M21) + (left.M03 * right.M31),
        (left.M00 * right.M02) + (left.M01 * right.M12) + (left.M02 * right.M22) + (left.M03 * right.M32),
        (left.M00 * right.M03) + (left.M01 * right.M13) + (left.M02 * right.M23) + (left.M03 * right.M33),
        (left.M10 * right.M00) + (left.M11 * right.M10) + (left.M12 * right.M20) + (left.M13 * right.M30),
        (left.M10 * right.M01) + (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31),
        (left.M10 * right.M02) + (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32),
        (left.M10 * right.M03) + (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
        (left.M20 * right.M00) + (left.M21 * right.M10) + (left.M22 * right.M20) + (left.M23 * right.M30),
        (left.M20 * right.M01) + (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31),
        (left.M20 * right.M02) + (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32),
        (left.M20 * right.M03) + (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
        (left.M30 * right.M00) + (left.M31 * right.M10) + (left.M32 * right.M20) + (left.M33 * right.M30),
        (left.M30 * right.M01) + (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31),
        (left.M30 * right.M02) + (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32),
        (left.M30 * right.M03) + (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));

    /// <summary>
    /// Composes two transforms. The named alternate to <c>operator *</c>.
    /// </summary>
    /// <param name="left">The transform applied second.</param>
    /// <param name="right">The transform applied first.</param>
    /// <returns>The transform that applies <paramref name="right"/> and then <paramref name="left"/>.</returns>
    public static Transform Multiply(in Transform left, in Transform right) => left * right;

    /// <summary>
    /// Applies this transform to a point.
    /// </summary>
    /// <param name="point">The point to transform.</param>
    /// <returns>
    /// The transformed point. The whole matrix is applied, translation included. For a
    /// projective transform — one whose bottom row is not <c>(0, 0, 0, 1)</c> — the result is
    /// divided through by the resulting homogeneous weight; a weight of zero gives infinite
    /// coordinates rather than an exception, because the point genuinely maps to infinity.
    /// </returns>
    public Point3d OfPoint(in Point3d point)
    {
        double x = (M00 * point.X) + (M01 * point.Y) + (M02 * point.Z) + M03;
        double y = (M10 * point.X) + (M11 * point.Y) + (M12 * point.Z) + M13;
        double z = (M20 * point.X) + (M21 * point.Y) + (M22 * point.Z) + M23;
        double w = (M30 * point.X) + (M31 * point.Y) + (M32 * point.Z) + M33;

        if (w == 1.0)
        {
            return new Point3d(x, y, z);
        }

        return new Point3d(x / w, y / w, z / w);
    }

    /// <summary>
    /// Applies this transform to a vector.
    /// </summary>
    /// <param name="vector">The vector to transform.</param>
    /// <returns>
    /// The transformed vector. Only the upper-left 3x3 block is applied: <b>translation is
    /// ignored</b>, because a direction has no position to move. Under a non-uniform scale or
    /// a shear this does not preserve length or angle, and a surface normal transformed this
    /// way will no longer be normal to the transformed surface — normals need the inverse
    /// transpose, which this method deliberately does not silently apply.
    /// </returns>
    public Vector3d OfVector(in Vector3d vector) => new(
        (M00 * vector.X) + (M01 * vector.Y) + (M02 * vector.Z),
        (M10 * vector.X) + (M11 * vector.Y) + (M12 * vector.Z),
        (M20 * vector.X) + (M21 * vector.Y) + (M22 * vector.Z));

    /// <summary>
    /// Applies this transform to an axis-aligned box.
    /// </summary>
    /// <param name="box">The box to transform.</param>
    /// <returns>
    /// The axis-aligned box of the eight transformed corners. Under anything but an
    /// axis-aligned scale and translation this is <b>larger</b> than the transformed box: a
    /// rotated box is not axis-aligned, so its axis-aligned bound has to grow to contain it.
    /// Transforming a box repeatedly therefore inflates it, and the fix is always to
    /// transform the underlying geometry and re-bound it rather than to chain box transforms.
    /// <see cref="BoundingBox.Empty"/> transforms to itself.
    /// </returns>
    public BoundingBox OfBoundingBox(in BoundingBox box)
    {
        if (!box.IsValid)
        {
            return BoundingBox.Empty;
        }

        BoundingBox result = BoundingBox.Empty;

        foreach (Point3d corner in box.Corners())
        {
            result = result.Union(OfPoint(corner));
        }

        return result;
    }

    /// <summary>Applies a transform to a point.</summary>
    /// <param name="transform">The transform to apply.</param>
    /// <param name="point">The point to transform.</param>
    /// <returns>The transformed point.</returns>
    public static Point3d operator *(in Transform transform, in Point3d point) => transform.OfPoint(point);

    /// <summary>Applies a transform to a vector, ignoring translation.</summary>
    /// <param name="transform">The transform to apply.</param>
    /// <param name="vector">The vector to transform.</param>
    /// <returns>The transformed vector.</returns>
    public static Vector3d operator *(in Transform transform, in Vector3d vector) =>
        transform.OfVector(vector);

    /// <summary>Applies a transform to a box, returning the box of the transformed corners.</summary>
    /// <param name="transform">The transform to apply.</param>
    /// <param name="box">The box to transform.</param>
    /// <returns>The axis-aligned box of the eight transformed corners.</returns>
    public static BoundingBox operator *(in Transform transform, in BoundingBox box) =>
        transform.OfBoundingBox(box);

    /// <summary>
    /// Attempts to compute the inverse of this transform.
    /// </summary>
    /// <param name="inverse">
    /// On success, the transform that undoes this one, so that composing the two in either
    /// order gives the identity to within rounding. On failure,
    /// <see cref="Identity"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the matrix is invertible. <see langword="false"/> when it
    /// is singular — a scale by zero, a projection onto a plane, or any transform that
    /// collapses a dimension — or when it holds a non-finite entry.
    /// </returns>
    /// <remarks>
    /// Uses Gauss-Jordan elimination with partial pivoting rather than the cofactor formula,
    /// because pivoting keeps the result usable for the badly scaled matrices that arise when
    /// a model is built at one scale and transformed to another.
    /// </remarks>
    public bool TryGetInverse(out Transform inverse)
    {
        Span<double> augmented = stackalloc double[32];

        // Zeroed explicitly rather than relying on stackalloc's implicit zeroing. That
        // guarantee is real today, but it is exactly what [SkipLocalsInit] removes, and a
        // future annotation on this assembly for an unrelated hot path would silently turn
        // the untouched cells of this buffer into whatever was on the stack — producing
        // arbitrary inverses with no compile error and no test that obviously targets it.
        augmented.Clear();

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double value = At(row, column);

                if (!double.IsFinite(value))
                {
                    inverse = Identity;
                    return false;
                }

                augmented[(row * 8) + column] = value;
            }

            augmented[(row * 8) + 4 + row] = 1.0;
        }

        for (int pivotIndex = 0; pivotIndex < 4; pivotIndex++)
        {
            int best = pivotIndex;
            double bestMagnitude = Math.Abs(augmented[(pivotIndex * 8) + pivotIndex]);

            for (int candidate = pivotIndex + 1; candidate < 4; candidate++)
            {
                double magnitude = Math.Abs(augmented[(candidate * 8) + pivotIndex]);

                if (magnitude > bestMagnitude)
                {
                    best = candidate;
                    bestMagnitude = magnitude;
                }
            }

            if (bestMagnitude == 0.0)
            {
                inverse = Identity;
                return false;
            }

            if (best != pivotIndex)
            {
                for (int column = 0; column < 8; column++)
                {
                    (augmented[(pivotIndex * 8) + column], augmented[(best * 8) + column]) =
                        (augmented[(best * 8) + column], augmented[(pivotIndex * 8) + column]);
                }
            }

            double pivot = augmented[(pivotIndex * 8) + pivotIndex];

            for (int column = 0; column < 8; column++)
            {
                augmented[(pivotIndex * 8) + column] /= pivot;
            }

            for (int row = 0; row < 4; row++)
            {
                if (row == pivotIndex)
                {
                    continue;
                }

                double factor = augmented[(row * 8) + pivotIndex];

                if (factor == 0.0)
                {
                    continue;
                }

                for (int column = 0; column < 8; column++)
                {
                    augmented[(row * 8) + column] -= factor * augmented[(pivotIndex * 8) + column];
                }
            }
        }

        for (int index = 0; index < 32; index++)
        {
            if (!double.IsFinite(augmented[index]))
            {
                inverse = Identity;
                return false;
            }
        }

        inverse = new Transform(
            augmented[4], augmented[5], augmented[6], augmented[7],
            augmented[12], augmented[13], augmented[14], augmented[15],
            augmented[20], augmented[21], augmented[22], augmented[23],
            augmented[28], augmented[29], augmented[30], augmented[31]);

        return true;
    }

    /// <summary>
    /// Tests whether this transform leaves everything where it is.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> and
    /// <see cref="Tolerance.RelativeEpsilon"/> are consulted. A default-constructed tolerance
    /// means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every entry is within tolerance of the corresponding entry
    /// of <see cref="Identity"/>. Note that this is an entry-wise test on the matrix, not a
    /// bound on how far any particular point moves — a transform that is within tolerance of
    /// the identity can still move a point a long way if the point is a long way from the
    /// origin.
    /// </returns>
    public bool IsIdentity(in Tolerance tolerance = default)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (!tolerance.AreEqual(At(row, column), row == column ? 1.0 : 0.0))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether this transform is affine — that it maps parallel lines to parallel lines
    /// and involves no perspective division.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the bottom row is within tolerance of
    /// <c>(0, 0, 0, 1)</c>. Every transform this type constructs is affine; a non-affine one
    /// can only arrive through the sixteen-entry constructor.
    /// </returns>
    public bool IsAffine(in Tolerance tolerance = default) =>
        tolerance.AreEqual(M30, 0.0)
        && tolerance.AreEqual(M31, 0.0)
        && tolerance.AreEqual(M32, 0.0)
        && tolerance.AreEqual(M33, 1.0);

    /// <summary>
    /// Tests whether this transform is a rigid motion: a rotation and a translation, with no
    /// scaling, shear or reflection.
    /// </summary>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the transform is affine, its three basis columns are unit
    /// length and mutually perpendicular, and its determinant is positive. A <b>mirror is
    /// not rigid</b> by this definition: it preserves distances but reverses handedness, and
    /// treating it as rigid is how inside-out solids get made.
    /// </returns>
    public bool IsRigid(in Tolerance tolerance = default)
    {
        if (!IsAffine(tolerance))
        {
            return false;
        }

        Vector3d x = new(M00, M10, M20);
        Vector3d y = new(M01, M11, M21);
        Vector3d z = new(M02, M12, M22);

        return tolerance.AreEqual(x.LengthSquared, 1.0)
            && tolerance.AreEqual(y.LengthSquared, 1.0)
            && tolerance.AreEqual(z.LengthSquared, 1.0)
            && tolerance.IsZero(x.Dot(y))
            && tolerance.IsZero(y.Dot(z))
            && tolerance.IsZero(z.Dot(x))
            && Determinant > 0.0;
    }

    /// <summary>
    /// Tests whether this transform and another agree entry by entry within a tolerance.
    /// </summary>
    /// <param name="other">The transform to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when all sixteen entries agree within tolerance.</returns>
    public bool EqualsWithin(in Transform other, in Tolerance tolerance = default)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (!tolerance.AreEqual(At(row, column), other.At(row, column)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two transforms for exact entry-wise equality, following IEEE rules.
    /// </summary>
    /// <param name="left">The first transform.</param>
    /// <param name="right">The second transform.</param>
    /// <returns>
    /// <see langword="true"/> when all sixteen entries are exactly equal. Composition
    /// introduces rounding, so two transforms that ought to be the same rarely are; use
    /// <see cref="EqualsWithin(in Transform, in Tolerance)"/> instead for anything computed.
    /// </returns>
    public static bool operator ==(in Transform left, in Transform right) =>
        left.M00 == right.M00 && left.M01 == right.M01 && left.M02 == right.M02 && left.M03 == right.M03
        && left.M10 == right.M10 && left.M11 == right.M11 && left.M12 == right.M12 && left.M13 == right.M13
        && left.M20 == right.M20 && left.M21 == right.M21 && left.M22 == right.M22 && left.M23 == right.M23
        && left.M30 == right.M30 && left.M31 == right.M31 && left.M32 == right.M32 && left.M33 == right.M33;

    /// <summary>Compares two transforms for exact inequality.</summary>
    /// <param name="left">The first transform.</param>
    /// <param name="right">The second transform.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Transform left, in Transform right) => !(left == right);

    /// <summary>
    /// Tests exact entry-wise equality, treating <see cref="double.NaN"/> as equal to itself
    /// so that transforms remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The transform to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when all sixteen entries are equal under
    /// <see cref="double.Equals(double)"/>.
    /// </returns>
    public bool Equals(Transform other)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (!At(row, column).Equals(other.At(row, column)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Transform other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                hash.Add(At(row, column));
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Formats the matrix row by row, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>[[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]</c>.</returns>
    public override string ToString()
    {
        StringBuilder builder = new();

        builder.Append('[');

        for (int row = 0; row < 4; row++)
        {
            if (row > 0)
            {
                builder.Append(", ");
            }

            builder.Append('[');

            for (int column = 0; column < 4; column++)
            {
                if (column > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(At(row, column).ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(']');
        }

        builder.Append(']');

        return builder.ToString();
    }

    private static Transform LocalToWorld(in Plane plane)
    {
        Vector3d x = plane.XAxis;
        Vector3d y = plane.YAxis;
        Vector3d z = plane.Normal;
        Point3d o = plane.Origin;

        return new Transform(
            x.X, y.X, z.X, o.X,
            x.Y, y.Y, z.Y, o.Y,
            x.Z, y.Z, z.Z, o.Z,
            0.0, 0.0, 0.0, 1.0);
    }

    private double At(int row, int column) => (row, column) switch
    {
        (0, 0) => M00,
        (0, 1) => M01,
        (0, 2) => M02,
        (0, 3) => M03,
        (1, 0) => M10,
        (1, 1) => M11,
        (1, 2) => M12,
        (1, 3) => M13,
        (2, 0) => M20,
        (2, 1) => M21,
        (2, 2) => M22,
        (2, 3) => M23,
        (3, 0) => M30,
        (3, 1) => M31,
        (3, 2) => M32,
        _ => M33,
    };
}

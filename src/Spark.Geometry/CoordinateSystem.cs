using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A right-handed orthonormal frame: an origin and three mutually perpendicular unit axes.
/// </summary>
/// <remarks>
/// <para>
/// A coordinate system is a full three-dimensional frame, where a <see cref="Plane"/> is the
/// same information read as a surface. The two are interchangeable through
/// <see cref="ByPlane(in Plane)"/> and <see cref="ToPlane"/>; use whichever names the thing
/// you are actually talking about, since a reader learns more from
/// <c>CoordinateSystem</c> in a signature about placement than from <c>Plane</c>.
/// </para>
/// <para>
/// Every factory orthonormalises what it is given, so the invariant — unit axes, mutually
/// perpendicular, <see cref="ZAxis"/> equal to <see cref="XAxis"/> crossed with
/// <see cref="YAxis"/> — holds for every value except <c>default</c>, whose axes are all
/// zero. <see cref="IsValid"/> reports that case.
/// </para>
/// <para>
/// Every member that would have to answer a geometric question about a frame that has none
/// throws <see cref="InvalidOperationException"/> on <c>default</c>: all four
/// <c>ToLocal</c> and <c>ToWorld</c> overloads, <see cref="ToPlane"/> and
/// <see cref="ToTransform"/>. Equality, hashing, formatting and <see cref="IsValid"/> work
/// on any value. Silently answering <c>(0, 0, 0)</c> for every input, which an unguarded
/// dot product against three zero axes does, is the failure this rules out.
/// </para>
/// </remarks>
public readonly struct CoordinateSystem : IEquatable<CoordinateSystem>
{
    /// <summary>
    /// Creates a coordinate system from an origin and two directions, orthonormalising them.
    /// </summary>
    /// <param name="origin">The frame's origin.</param>
    /// <param name="xAxis">
    /// The direction of the frame's X axis. Used as given once normalised.
    /// </param>
    /// <param name="yAxis">
    /// A second direction. Only its component perpendicular to <paramref name="xAxis"/> is
    /// kept, so the frame's <see cref="YAxis"/> may differ from what was passed in. The Z
    /// axis is then the cross product of the two, making the frame right-handed.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when either direction is zero-length or non-finite, when the two are parallel,
    /// or when the origin is not finite.
    /// </exception>
    public CoordinateSystem(in Point3d origin, in Vector3d xAxis, in Vector3d yAxis)
    {
        if (!origin.IsValid)
        {
            throw new ArgumentException("A coordinate system's origin must be finite.", nameof(origin));
        }

        if (!xAxis.TryNormalise(out Vector3d x))
        {
            throw new ArgumentException(
                "The X axis must have non-zero length and finite components.",
                nameof(xAxis));
        }

        if (!x.Cross(yAxis).TryNormalise(out Vector3d z))
        {
            throw new ArgumentException(
                "The two axes are parallel or degenerate, so they define no frame.",
                nameof(yAxis));
        }

        Origin = origin;
        XAxis = x;
        YAxis = z.Cross(x);
        ZAxis = z;
    }

    private CoordinateSystem(in Point3d origin, in Vector3d xAxis, in Vector3d yAxis, in Vector3d zAxis)
    {
        Origin = origin;
        XAxis = xAxis;
        YAxis = yAxis;
        ZAxis = zAxis;
    }

    /// <summary>The frame's origin: the point whose local coordinates are <c>(0, 0, 0)</c>.</summary>
    public Point3d Origin { get; }

    /// <summary>The frame's first axis, a unit vector.</summary>
    public Vector3d XAxis { get; }

    /// <summary>The frame's second axis, a unit vector perpendicular to <see cref="XAxis"/>.</summary>
    public Vector3d YAxis { get; }

    /// <summary>
    /// The frame's third axis, equal to <see cref="XAxis"/> crossed with
    /// <see cref="YAxis"/>, which is what makes the frame right-handed.
    /// </summary>
    public Vector3d ZAxis { get; }

    /// <summary>
    /// The world coordinate system: origin at <c>(0, 0, 0)</c> with the three world axes.
    /// Note that this is not <c>default(CoordinateSystem)</c>, whose axes are all zero.
    /// </summary>
    public static CoordinateSystem Identity =>
        new(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);

    /// <summary>
    /// <see langword="true"/> when the origin is finite and the axes form a usable frame,
    /// which is the case for every value except <c>default</c>.
    /// </summary>
    public bool IsValid =>
        Origin.IsValid
        && XAxis.IsValid
        && YAxis.IsValid
        && ZAxis.IsValid
        && ZAxis.LengthSquared > 0.0;

    /// <summary>
    /// Creates a coordinate system at a point, with the world axes.
    /// </summary>
    /// <param name="origin">The frame's origin.</param>
    /// <returns>A world-aligned frame at that point.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="origin"/> is not finite.</exception>
    public static CoordinateSystem ByOrigin(in Point3d origin)
    {
        if (!origin.IsValid)
        {
            throw new ArgumentException("A coordinate system's origin must be finite.", nameof(origin));
        }

        return new CoordinateSystem(origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
    }

    /// <summary>
    /// Creates a coordinate system from an origin and two directions. The factory form of the
    /// equivalent constructor.
    /// </summary>
    /// <param name="origin">The frame's origin.</param>
    /// <param name="xAxis">The direction of the frame's X axis.</param>
    /// <param name="yAxis">
    /// A second direction; only its component perpendicular to <paramref name="xAxis"/> is
    /// kept.
    /// </param>
    /// <returns>The orthonormalised frame.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either direction is degenerate or the two are parallel.
    /// </exception>
    public static CoordinateSystem ByOriginXAxisYAxis(
        in Point3d origin,
        in Vector3d xAxis,
        in Vector3d yAxis) => new(origin, xAxis, yAxis);

    /// <summary>
    /// Creates a coordinate system with its Z axis along a given direction, choosing the
    /// remaining two axes arbitrarily but deterministically.
    /// </summary>
    /// <param name="origin">The frame's origin.</param>
    /// <param name="zAxis">The direction the frame's Z axis should point. Need not be normalised.</param>
    /// <returns>
    /// A frame whose Z axis is the normalised <paramref name="zAxis"/>. A Z axis of
    /// <c>(0, 0, 1)</c> reproduces the world axes exactly; for other directions the X and Y
    /// axes are whatever the deterministic choice yields, so do not rely on them without
    /// checking.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="zAxis"/> is zero-length or non-finite.
    /// </exception>
    public static CoordinateSystem ByOriginZAxis(in Point3d origin, in Vector3d zAxis) =>
        ByPlane(new Plane(origin, zAxis));

    /// <summary>
    /// Creates the coordinate system whose X and Y axes are a plane's in-plane axes and whose
    /// Z axis is that plane's normal.
    /// </summary>
    /// <param name="plane">The plane to read as a frame.</param>
    /// <returns>The equivalent coordinate system.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="plane"/> is not valid.</exception>
    public static CoordinateSystem ByPlane(in Plane plane)
    {
        if (!plane.IsValid)
        {
            throw new ArgumentException("The plane must be valid.", nameof(plane));
        }

        return new CoordinateSystem(plane.Origin, plane.XAxis, plane.YAxis, plane.Normal);
    }

    /// <summary>
    /// Reads this frame as a plane, dropping nothing: the plane's in-plane axes are this
    /// frame's X and Y axes and its normal is this frame's Z axis.
    /// </summary>
    /// <returns>The equivalent plane.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this coordinate system is not valid.
    /// </exception>
    public Plane ToPlane()
    {
        ThrowIfInvalid();

        return Plane.ByOriginXAxisYAxis(Origin, XAxis, YAxis);
    }

    /// <summary>
    /// Converts a point from world coordinates into this frame's coordinates.
    /// </summary>
    /// <param name="worldPoint">The point, in world coordinates.</param>
    /// <returns>
    /// The point's coordinates in this frame: its offset from <see cref="Origin"/> measured
    /// along each of the three axes.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this coordinate system is not valid, which for a <c>readonly struct</c>
    /// means a default-constructed one.
    /// </exception>
    public Point3d ToLocal(in Point3d worldPoint)
    {
        ThrowIfInvalid();

        Vector3d offset = worldPoint - Origin;

        return new Point3d(offset.Dot(XAxis), offset.Dot(YAxis), offset.Dot(ZAxis));
    }

    /// <summary>
    /// Converts a direction from world coordinates into this frame's coordinates.
    /// </summary>
    /// <param name="worldVector">The direction, in world coordinates.</param>
    /// <returns>
    /// The direction's components along this frame's three axes. The origin plays no part,
    /// because a direction has no position.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this coordinate system is not valid, which for a <c>readonly struct</c>
    /// means a default-constructed one.
    /// </exception>
    public Vector3d ToLocal(in Vector3d worldVector)
    {
        ThrowIfInvalid();

        return new Vector3d(
            worldVector.Dot(XAxis),
            worldVector.Dot(YAxis),
            worldVector.Dot(ZAxis));
    }

    /// <summary>
    /// Converts a point from this frame's coordinates into world coordinates.
    /// </summary>
    /// <param name="localPoint">The point, in this frame's coordinates.</param>
    /// <returns>
    /// The corresponding world point. This inverts <see cref="ToLocal(in Point3d)"/> <b>to
    /// within floating-point rounding rather than exactly</b> — both directions are dot and
    /// scale products against the frame, and each loses a few units in the last place,
    /// proportionally more the further the point is from the origin.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this coordinate system is not valid, which for a <c>readonly struct</c>
    /// means a default-constructed one.
    /// </exception>
    public Point3d ToWorld(in Point3d localPoint)
    {
        ThrowIfInvalid();

        return Origin + (XAxis * localPoint.X) + (YAxis * localPoint.Y) + (ZAxis * localPoint.Z);
    }

    /// <summary>
    /// Converts a direction from this frame's coordinates into world coordinates.
    /// </summary>
    /// <param name="localVector">The direction, in this frame's coordinates.</param>
    /// <returns>
    /// The corresponding world direction. The origin plays no part, because a direction has
    /// no position.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this coordinate system is not valid, which for a <c>readonly struct</c>
    /// means a default-constructed one.
    /// </exception>
    public Vector3d ToWorld(in Vector3d localVector)
    {
        ThrowIfInvalid();

        return (XAxis * localVector.X) + (YAxis * localVector.Y) + (ZAxis * localVector.Z);
    }

    /// <summary>
    /// Builds the transform that carries local coordinates into world coordinates.
    /// </summary>
    /// <returns>
    /// The transform for which <c>transform.OfPoint(p)</c> equals <c>ToWorld(p)</c>. Its
    /// inverse is <see cref="Transform.ChangeBasis(in Plane)"/> of the equivalent plane, and
    /// because the frame is orthonormal and right-handed the transform is always rigid.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this coordinate system is not valid.
    /// </exception>
    public Transform ToTransform()
    {
        ThrowIfInvalid();

        return new Transform(
            XAxis.X, YAxis.X, ZAxis.X, Origin.X,
            XAxis.Y, YAxis.Y, ZAxis.Y, Origin.Y,
            XAxis.Z, YAxis.Z, ZAxis.Z, Origin.Z,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>
    /// Tests whether this frame and another have the same origin and axes within a tolerance.
    /// </summary>
    /// <param name="other">The frame to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns><see langword="true"/> when the origin and all three axes agree within tolerance.</returns>
    public bool EqualsWithin(in CoordinateSystem other, in Tolerance tolerance = default) =>
        Origin.EqualsWithin(other.Origin, tolerance)
        && XAxis.EqualsWithin(other.XAxis, tolerance)
        && YAxis.EqualsWithin(other.YAxis, tolerance)
        && ZAxis.EqualsWithin(other.ZAxis, tolerance);

    /// <summary>
    /// Compares two frames for exact equality of origin and axes, following IEEE rules.
    /// </summary>
    /// <param name="left">The first frame.</param>
    /// <param name="right">The second frame.</param>
    /// <returns>
    /// <see langword="true"/> when the origin and all three axes are exactly equal. Use
    /// <see cref="EqualsWithin(in CoordinateSystem, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(in CoordinateSystem left, in CoordinateSystem right) =>
        left.Origin == right.Origin
        && left.XAxis == right.XAxis
        && left.YAxis == right.YAxis
        && left.ZAxis == right.ZAxis;

    /// <summary>Compares two frames for exact inequality.</summary>
    /// <param name="left">The first frame.</param>
    /// <param name="right">The second frame.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in CoordinateSystem left, in CoordinateSystem right) => !(left == right);

    /// <summary>
    /// Tests exact equality of origin and axes, treating <see cref="double.NaN"/> as equal to
    /// itself so that frames remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The frame to compare with.</param>
    /// <returns><see langword="true"/> when the origin and all three axes are equal.</returns>
    public bool Equals(CoordinateSystem other) =>
        Origin.Equals(other.Origin)
        && XAxis.Equals(other.XAxis)
        && YAxis.Equals(other.YAxis)
        && ZAxis.Equals(other.ZAxis);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CoordinateSystem other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Origin, XAxis, YAxis, ZAxis);

    /// <summary>
    /// Formats the origin and the three axes, using the invariant culture.
    /// </summary>
    /// <returns>
    /// A string of the form
    /// <c>CoordinateSystem(Origin=(0, 0, 0), X=(1, 0, 0), Y=(0, 1, 0), Z=(0, 0, 1))</c>.
    /// </returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"CoordinateSystem(Origin={Origin}, X={XAxis}, Y={YAxis}, Z={ZAxis})");

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default-constructed CoordinateSystem has no origin and no axes, so no "
                + "conversion into or out of it is meaningful. Build one with a constructor "
                + "or a By* factory, and test IsValid if a frame may have reached you unset.");
        }
    }
}

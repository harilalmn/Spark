using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// An infinite plane, carried as an origin and a right-handed orthonormal frame.
/// </summary>
/// <remarks>
/// <para>
/// The frame is always orthonormal and always right-handed:
/// <see cref="XAxis"/> crossed with <see cref="YAxis"/> is <see cref="Normal"/>. Every
/// factory orthonormalises its inputs, so a caller can hand in convenient, non-perpendicular
/// directions and still get a usable frame back.
/// </para>
/// <para>
/// <b>Why a struct rather than a sealed class.</b> A plane is a value, it is small, and it
/// appears once per element in replication over lists that can run to six figures — a class
/// would put an allocation and a pointer chase on that path for no benefit. The cost of the
/// choice is that <c>default(Plane)</c> exists and has a zero normal, which is not a plane;
/// <see cref="IsValid"/> reports that, and every member documents what it does with an
/// invalid plane by throwing <see cref="InvalidOperationException"/>. Planes are passed by
/// <c>in</c> throughout the kernel so the 96-byte copy is not paid on calls.
/// </para>
/// <para>
/// The members that throw on <c>default</c> are every geometric query and construction:
/// <see cref="DistanceTo(in Point3d)"/>, <see cref="ClosestPoint(in Point3d)"/>,
/// <see cref="Project(in Vector3d)"/>, <see cref="Flip"/>, <see cref="To2d(in Point3d)"/>,
/// <see cref="To3d(in Point2d)"/>, <see cref="Contains(in Point3d, in Tolerance)"/> and
/// <see cref="IsCoplanar(in Plane, in Tolerance)"/>. Equality, hashing, formatting and
/// <see cref="IsValid"/> work on any value, including <c>default</c>, because their job is
/// to describe the value rather than to answer a question about a plane.
/// </para>
/// </remarks>
public readonly struct Plane : IEquatable<Plane>
{
    /// <summary>
    /// Creates a plane through a point with a given normal. The in-plane axes are chosen
    /// arbitrarily but deterministically, so the same normal always yields the same frame.
    /// </summary>
    /// <param name="origin">The plane's origin, which becomes the zero of its 2d coordinates.</param>
    /// <param name="normal">The plane's normal. Need not be normalised.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="normal"/> is zero-length or non-finite, or when
    /// <paramref name="origin"/> is not finite.
    /// </exception>
    public Plane(in Point3d origin, in Vector3d normal)
    {
        if (!origin.IsValid)
        {
            throw new ArgumentException("A plane's origin must be finite.", nameof(origin));
        }

        if (!normal.TryNormalise(out Vector3d unitNormal))
        {
            throw new ArgumentException(
                "A plane's normal must have non-zero length and finite components.",
                nameof(normal));
        }

        // Seed the frame from the world Z axis, swapping to Y when the normal is too close to
        // Z for that cross product to be well conditioned. The switch at 0.9 keeps the shorter
        // of the two cross products above 0.43 in length in every case. Choosing Y rather than
        // X for the polar case is what makes a normal of +Z reproduce the world XY frame
        // exactly, which is the single most common call this constructor gets.
        Vector3d seed = Math.Abs(unitNormal.Z) > 0.9 ? Vector3d.YAxis : Vector3d.ZAxis;

        Origin = origin;
        Normal = unitNormal;
        XAxis = seed.Cross(unitNormal).Normalised();
        YAxis = unitNormal.Cross(XAxis);
    }

    private Plane(in Point3d origin, in Vector3d xAxis, in Vector3d yAxis, in Vector3d normal)
    {
        Origin = origin;
        XAxis = xAxis;
        YAxis = yAxis;
        Normal = normal;
    }

    /// <summary>
    /// The plane's origin: the point that <see cref="To2d(in Point3d)"/> maps to
    /// <c>(0, 0)</c>.
    /// </summary>
    public Point3d Origin { get; }

    /// <summary>
    /// The in-plane direction that <see cref="To2d(in Point3d)"/> measures its first
    /// coordinate along. Always a unit vector for a valid plane.
    /// </summary>
    public Vector3d XAxis { get; }

    /// <summary>
    /// The in-plane direction that <see cref="To2d(in Point3d)"/> measures its second
    /// coordinate along. Always a unit vector perpendicular to <see cref="XAxis"/> for a
    /// valid plane.
    /// </summary>
    public Vector3d YAxis { get; }

    /// <summary>
    /// The plane's unit normal, equal to <see cref="XAxis"/> crossed with
    /// <see cref="YAxis"/>. It defines the positive side of the plane, which is the side
    /// <see cref="DistanceTo(in Point3d)"/> reports as positive.
    /// </summary>
    public Vector3d Normal { get; }

    /// <summary>
    /// The world XY plane: origin at <c>(0, 0, 0)</c>, X along the world X axis, Y along the
    /// world Y axis, normal along <c>+Z</c>.
    /// </summary>
    public static Plane WorldXY => new(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);

    /// <summary>
    /// The world XZ plane: origin at <c>(0, 0, 0)</c>, X along the world X axis, Y along the
    /// world Z axis. Because the frame stays right-handed, its normal is
    /// <c>-Y</c>, not <c>+Y</c>.
    /// </summary>
    public static Plane WorldXZ => new(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis, -Vector3d.YAxis);

    /// <summary>
    /// The world YZ plane: origin at <c>(0, 0, 0)</c>, X along the world Y axis, Y along the
    /// world Z axis, normal along <c>+X</c>.
    /// </summary>
    public static Plane WorldYZ => new(Point3d.Origin, Vector3d.YAxis, Vector3d.ZAxis, Vector3d.XAxis);

    /// <summary>
    /// <see langword="true"/> when the frame is a usable orthonormal basis — which it always
    /// is for a plane produced by a constructor or factory, and never is for
    /// <c>default(Plane)</c>, whose vectors are all zero.
    /// </summary>
    public bool IsValid =>
        Origin.IsValid
        && Normal.IsValid
        && XAxis.IsValid
        && YAxis.IsValid
        && Normal.LengthSquared > 0.0;

    /// <summary>
    /// Creates a plane through a point with a given normal. The factory form of the
    /// equivalent constructor.
    /// </summary>
    /// <param name="origin">The plane's origin.</param>
    /// <param name="normal">The plane's normal. Need not be normalised.</param>
    /// <returns>The plane.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="normal"/> is zero-length or non-finite.
    /// </exception>
    public static Plane ByOriginNormal(in Point3d origin, in Vector3d normal) => new(origin, normal);

    /// <summary>
    /// Creates a plane from an origin and two in-plane directions.
    /// </summary>
    /// <param name="origin">The plane's origin.</param>
    /// <param name="xAxis">
    /// The direction the plane's first in-plane coordinate is measured along. Used as given
    /// once normalised.
    /// </param>
    /// <param name="yAxis">
    /// A second in-plane direction. It does <b>not</b> have to be perpendicular to
    /// <paramref name="xAxis"/>: only the component of it perpendicular to
    /// <paramref name="xAxis"/> is kept, so the resulting frame is orthonormal and the
    /// plane's <see cref="YAxis"/> may differ from what was passed in.
    /// </param>
    /// <returns>The plane.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when either direction is zero-length or non-finite, or when the two are
    /// parallel and therefore span no plane.
    /// </exception>
    public static Plane ByOriginXAxisYAxis(in Point3d origin, in Vector3d xAxis, in Vector3d yAxis)
    {
        if (!origin.IsValid)
        {
            throw new ArgumentException("A plane's origin must be finite.", nameof(origin));
        }

        if (!xAxis.TryNormalise(out Vector3d x))
        {
            throw new ArgumentException(
                "The X axis must have non-zero length and finite components.",
                nameof(xAxis));
        }

        if (!x.Cross(yAxis).TryNormalise(out Vector3d normal))
        {
            throw new ArgumentException(
                "The two axes are parallel or degenerate, so they span no plane.",
                nameof(yAxis));
        }

        return new Plane(origin, x, normal.Cross(x), normal);
    }

    /// <summary>
    /// Creates the plane through three points.
    /// </summary>
    /// <param name="first">
    /// The first point, which becomes the plane's <see cref="Origin"/>.
    /// </param>
    /// <param name="second">
    /// The second point. The plane's <see cref="XAxis"/> points from
    /// <paramref name="first"/> towards it.
    /// </param>
    /// <param name="third">
    /// The third point, which fixes which side of the first two the plane's
    /// <see cref="YAxis"/> lies on.
    /// </param>
    /// <returns>
    /// The plane through the three points. Its normal follows the right-hand rule for the
    /// points taken in the order given, so reversing any two of them flips the normal.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the three points are collinear or coincident, and therefore define no
    /// unique plane, or when any of them is not finite. The <c>ParamName</c> names the
    /// offending point from <i>this</i> method's signature: an earlier version forwarded to
    /// <see cref="ByOriginXAxisYAxis(in Point3d, in Vector3d, in Vector3d)"/> and reported
    /// <c>yAxis</c>, a parameter no caller of this method has ever heard of.
    /// </exception>
    public static Plane ByThreePoints(in Point3d first, in Point3d second, in Point3d third)
    {
        if (!first.IsValid)
        {
            throw new ArgumentException("A plane's points must be finite.", nameof(first));
        }

        if (!second.IsValid)
        {
            throw new ArgumentException("A plane's points must be finite.", nameof(second));
        }

        if (!third.IsValid)
        {
            throw new ArgumentException("A plane's points must be finite.", nameof(third));
        }

        if (!(second - first).TryNormalise(out Vector3d x))
        {
            throw new ArgumentException(
                "The first two points are coincident, so they fix no direction in the plane.",
                nameof(second));
        }

        if (!x.Cross(third - first).TryNormalise(out Vector3d normal))
        {
            throw new ArgumentException(
                "The three points are collinear, so they define no unique plane.",
                nameof(third));
        }

        return new Plane(first, x, normal.Cross(x), normal);
    }

    /// <summary>
    /// The signed distance from the plane to a point.
    /// </summary>
    /// <param name="point">The point to measure to.</param>
    /// <returns>
    /// The distance along <see cref="Normal"/> from the plane to the point: positive on the
    /// side the normal points towards, negative on the other side, and zero on the plane.
    /// Take the absolute value for an unsigned distance.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public double DistanceTo(in Point3d point)
    {
        ThrowIfInvalid();

        return (point - Origin).Dot(Normal);
    }

    /// <summary>
    /// The point on the plane closest to a given point, which is its perpendicular projection
    /// onto the plane.
    /// </summary>
    /// <param name="point">The point to project.</param>
    /// <returns>The closest point on the plane.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Point3d ClosestPoint(in Point3d point)
    {
        ThrowIfInvalid();

        return point - (Normal * DistanceTo(point));
    }

    /// <summary>
    /// The part of a vector that lies in the plane.
    /// </summary>
    /// <param name="vector">The vector to project.</param>
    /// <returns>
    /// The vector with its component along <see cref="Normal"/> removed. A vector already
    /// parallel to the plane is returned unchanged, and a vector parallel to the normal
    /// projects to <see cref="Vector3d.Zero"/>. Note that this projects a <i>direction</i>;
    /// to project a <i>position</i>, use <see cref="ClosestPoint(in Point3d)"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Vector3d Project(in Vector3d vector)
    {
        ThrowIfInvalid();

        return vector - (Normal * vector.Dot(Normal));
    }

    /// <summary>
    /// Returns this plane with its normal reversed.
    /// </summary>
    /// <returns>
    /// A plane occupying the same positions with the opposite normal, so the sign of
    /// <see cref="DistanceTo(in Point3d)"/> is inverted. <see cref="XAxis"/> is kept and
    /// <see cref="YAxis"/> is negated, which is what keeps the frame right-handed; the
    /// consequence is that <see cref="To2d(in Point3d)"/> on the flipped plane negates the
    /// second coordinate.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Plane Flip()
    {
        ThrowIfInvalid();

        return new Plane(Origin, XAxis, -YAxis, -Normal);
    }

    /// <summary>
    /// Expresses a point in the plane's two-dimensional coordinates.
    /// </summary>
    /// <param name="point">The point to convert.</param>
    /// <returns>
    /// The point's coordinates along <see cref="XAxis"/> and <see cref="YAxis"/>, measured
    /// from <see cref="Origin"/>. A point off the plane is projected onto it first — the
    /// component along <see cref="Normal"/> is simply dropped, so
    /// <see cref="To3d(in Point2d)"/> of the result gives the projected point rather than the
    /// original one.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Point2d To2d(in Point3d point)
    {
        ThrowIfInvalid();

        Vector3d offset = point - Origin;

        return new Point2d(offset.Dot(XAxis), offset.Dot(YAxis));
    }

    /// <summary>
    /// Expresses a point given in the plane's two-dimensional coordinates as a world point.
    /// </summary>
    /// <param name="point">The in-plane coordinates.</param>
    /// <returns>
    /// The corresponding world point, which always lies on the plane. This inverts
    /// <see cref="To2d(in Point3d)"/> for points that already lie on the plane, <b>to within
    /// floating-point rounding rather than exactly</b>: the two conversions are dot and
    /// scale products against the frame, so the round trip loses a few units in the last
    /// place, and proportionally more the further the point is from the origin.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public Point3d To3d(in Point2d point)
    {
        ThrowIfInvalid();

        return Origin + (XAxis * point.X) + (YAxis * point.Y);
    }

    /// <summary>
    /// Tests whether a point lies on the plane.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="tolerance">
    /// The tolerance to use; only <see cref="Tolerance.Linear"/> is consulted. A
    /// default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the point's perpendicular distance from the plane is
    /// negligible at the scale of the plane and the point, by
    /// <see cref="Tolerance.IsNegligible(double, double)"/> — the same scale-aware rule
    /// <c>EqualsWithin</c> uses across the value layer. Returns <see langword="false"/> for
    /// an unset or non-finite point.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    public bool Contains(in Point3d point, in Tolerance tolerance = default)
    {
        ThrowIfInvalid();

        return tolerance.IsNegligible(
            DistanceTo(point),
            Math.Max(((Vector3d)Origin).Length, ((Vector3d)point).Length));
    }

    /// <summary>
    /// Tests whether this plane and another describe the same infinite plane.
    /// </summary>
    /// <param name="other">The plane to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. <see cref="Tolerance.Angular"/> governs the normals and
    /// <see cref="Tolerance.Linear"/> governs the separation. A default-constructed tolerance
    /// means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the two normals are parallel within the angular tolerance
    /// and the other plane's origin lies on this plane within the linear tolerance.
    /// <b>Direction is ignored</b>: a plane and its <see cref="Flip"/> are coplanar, and the
    /// in-plane axes play no part at all.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this plane is not valid, which for a <c>readonly struct</c> means a
    /// default-constructed one.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="other"/> is not valid.
    /// </exception>
    public bool IsCoplanar(in Plane other, in Tolerance tolerance = default)
    {
        ThrowIfInvalid();

        if (!other.IsValid)
        {
            throw new ArgumentException("The plane compared against must be valid.", nameof(other));
        }

        return Normal.IsParallelTo(other.Normal, tolerance) && Contains(other.Origin, tolerance);
    }

    /// <summary>
    /// Tests whether this plane and another have the same origin and frame within a
    /// tolerance.
    /// </summary>
    /// <param name="other">The plane to compare with.</param>
    /// <param name="tolerance">
    /// The tolerance to use. A default-constructed tolerance means
    /// <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the origins are coincident and all three axes agree within
    /// tolerance. This is stricter than <see cref="IsCoplanar(in Plane, in Tolerance)"/>,
    /// which ignores where the origin sits within the plane and how the frame is rotated in
    /// it.
    /// </returns>
    public bool EqualsWithin(in Plane other, in Tolerance tolerance = default) =>
        Origin.EqualsWithin(other.Origin, tolerance)
        && XAxis.EqualsWithin(other.XAxis, tolerance)
        && YAxis.EqualsWithin(other.YAxis, tolerance)
        && Normal.EqualsWithin(other.Normal, tolerance);

    /// <summary>
    /// Compares two planes for exact equality of origin and frame, following IEEE rules.
    /// </summary>
    /// <param name="left">The first plane.</param>
    /// <param name="right">The second plane.</param>
    /// <returns>
    /// <see langword="true"/> when the origin and all three axes are exactly equal. Use
    /// <see cref="EqualsWithin(in Plane, in Tolerance)"/> or
    /// <see cref="IsCoplanar(in Plane, in Tolerance)"/> for geometric comparison.
    /// </returns>
    public static bool operator ==(in Plane left, in Plane right) =>
        left.Origin == right.Origin
        && left.XAxis == right.XAxis
        && left.YAxis == right.YAxis
        && left.Normal == right.Normal;

    /// <summary>Compares two planes for exact inequality.</summary>
    /// <param name="left">The first plane.</param>
    /// <param name="right">The second plane.</param>
    /// <returns><see langword="true"/> when <c>operator ==</c> would return <see langword="false"/>.</returns>
    public static bool operator !=(in Plane left, in Plane right) => !(left == right);

    /// <summary>
    /// Tests exact equality of origin and frame, treating <see cref="double.NaN"/> as equal
    /// to itself so that planes remain usable as dictionary keys.
    /// </summary>
    /// <param name="other">The plane to compare with.</param>
    /// <returns><see langword="true"/> when the origin and all three axes are equal.</returns>
    public bool Equals(Plane other) =>
        Origin.Equals(other.Origin)
        && XAxis.Equals(other.XAxis)
        && YAxis.Equals(other.YAxis)
        && Normal.Equals(other.Normal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Plane other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Origin, XAxis, YAxis, Normal);

    /// <summary>
    /// Formats the origin and normal, using the invariant culture.
    /// </summary>
    /// <returns>A string of the form <c>Plane(Origin=(0, 0, 0), Normal=(0, 0, 1))</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Plane(Origin={Origin}, Normal={Normal})");

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                "A default-constructed Plane has no origin, no normal and no frame, so no "
                + "geometric question can be answered about it. Build one with a constructor "
                + "or a By* factory, and test IsValid if a plane may have reached you unset.");
        }
    }
}

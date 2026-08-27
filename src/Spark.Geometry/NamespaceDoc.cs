namespace Spark.Geometry;

/// <summary>
/// Documents the conventions that every type in the <c>Spark.Geometry</c> namespace obeys.
/// This type exists only to carry that documentation; it has no members and is never
/// instantiated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Handedness.</b> Spark uses a <b>right-handed</b> coordinate system throughout. The
/// cross product of the world X axis with the world Y axis is the world Z axis, and a
/// positive rotation about an axis is <b>counter-clockwise when viewed from the positive
/// end of that axis looking back towards the origin</b>. Every rotation, every signed
/// angle, every plane normal and every orientation predicate in this assembly follows
/// that rule with no exceptions.
/// </para>
/// <para>
/// <b>Units.</b> Coordinates are unitless. The kernel does not know and cannot know
/// whether a coordinate of <c>1.0</c> means a kilometre or a micron, which is why
/// <see cref="Tolerance.ForScale(double)"/> exists: numerical robustness is expressed in
/// terms of a characteristic length supplied by the caller rather than in terms of units.
/// Angles are the one exception — they are carried by <see cref="Angle"/>, which stores
/// radians internally and is constructed explicitly from degrees or radians.
/// </para>
/// <para>
/// <b>Tolerance is passed, never ambient.</b> There is no static, thread-local or
/// document-scoped default anywhere in this assembly. Predicates take
/// <c>in Tolerance tolerance = default</c>, and a default-constructed
/// <see cref="Tolerance"/> resolves to <see cref="Tolerance.Default"/>.
/// </para>
/// <para>
/// <b>Equality.</b> <c>operator ==</c> is <b>exact</b> on every value type here, and it
/// follows IEEE 754 semantics, so a value containing <c>NaN</c> is never equal to
/// anything, including itself. <c>Equals</c> follows <see cref="double.Equals(double)"/>
/// instead, treating <c>NaN</c> as equal to <c>NaN</c>, so that values stay usable as
/// dictionary keys. Geometric comparison is always a separate, explicit
/// <c>EqualsWithin(other, in Tolerance)</c> call. A fuzzy <c>operator ==</c> was
/// deliberately rejected: it breaks hashing, breaks transitivity, and surprises every
/// caller eventually.
/// </para>
/// <para>
/// <b>Immutability.</b> Every type in this namespace is a <c>readonly struct</c> or a
/// sealed immutable class. Nothing here carries identity, style, revision numbers or
/// screen awareness, and constructing geometry never mutates anything.
/// </para>
/// </remarks>
internal static class NamespaceDoc
{
}

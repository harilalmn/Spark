using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// One face of a <see cref="Mesh"/>: three or four vertex indices, wound anticlockwise.
/// </summary>
/// <remarks>
/// <para>
/// <b>Triangles and quads in one struct, not two types and not a list of indices.</b> A mesh is
/// mostly one or the other and a graph will mix them — a tessellated NURBS surface is naturally
/// quads with triangles at the poles — so a face type that could only be a triangle would force
/// every quad to be split at the point it enters the kernel, which loses the quad structure
/// permanently. Two types would double every loop. A four-index struct with a sentinel is the
/// smallest thing that carries both, and it stays a value type: a million-face mesh is a million
/// of these and not a million objects.
/// </para>
/// <para>
/// <b><see cref="D"/> is <c>-1</c> on a triangle</b>, and that is what <see cref="IsQuad"/> reads.
/// Repeating <see cref="C"/> in <see cref="D"/> is the other common convention and it is worse:
/// a degenerate quad and a triangle then look identical, and code that counts edges gets four
/// where there are three.
/// </para>
/// <para>
/// <b>Winding is anticlockwise seen from the front</b>, so the face normal follows the right-hand
/// rule around the indices. Stated on the type because it is the convention the whole kernel
/// depends on and nothing in the data records it: a mesh wound the other way looks correct until
/// it is shaded, or until a volume comes out negative.
/// </para>
/// </remarks>
public readonly struct MeshFace : IEquatable<MeshFace>
{
    /// <summary>What <see cref="D"/> holds on a triangle.</summary>
    public const int NoVertex = -1;

    /// <summary>Creates a triangular face.</summary>
    /// <param name="a">The first vertex index.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative.</exception>
    public MeshFace(int a, int b, int c) : this(a, b, c, NoVertex)
    {
    }

    /// <summary>Creates a triangular or quadrilateral face.</summary>
    /// <param name="a">The first vertex index.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <param name="d">The fourth, or <see cref="NoVertex"/> for a triangle.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative.</exception>
    public MeshFace(int a, int b, int c, int d)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(a);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfNegative(c);

        if (d < 0 && d != NoVertex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(d), d, "A fourth vertex index is either a real index or MeshFace.NoVertex.");
        }

        A = a;
        B = b;
        C = c;
        D = d;
    }

    /// <summary>The first vertex index.</summary>
    public int A { get; }

    /// <summary>The second vertex index.</summary>
    public int B { get; }

    /// <summary>The third vertex index.</summary>
    public int C { get; }

    /// <summary>The fourth vertex index, or <see cref="NoVertex"/>.</summary>
    public int D { get; }

    /// <summary>Whether this face has four corners.</summary>
    public bool IsQuad => D != NoVertex;

    /// <summary>How many corners this face has: three or four.</summary>
    public int Count => IsQuad ? 4 : 3;

    /// <summary>One corner by position.</summary>
    /// <param name="corner">Zero to <see cref="Count"/> − 1.</param>
    /// <returns>The vertex index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The corner is outside the face.</exception>
    public int this[int corner] => corner switch
    {
        0 => A,
        1 => B,
        2 => C,
        3 when IsQuad => D,
        _ => throw new ArgumentOutOfRangeException(
            nameof(corner), corner, "A face has three corners, or four when it is a quad."),
    };

    /// <summary>
    /// Whether the face names the same vertex twice, which makes it degenerate.
    /// </summary>
    /// <remarks>
    /// <b>Not refused at construction, and that is deliberate.</b> A degenerate face is a real
    /// thing a tessellator produces at a pole and a mesh reader finds in the wild, and a
    /// constructor that threw would make reading somebody's file impossible rather than merely
    /// awkward. What matters is that it can be *asked*, so a caller that cares can drop them.
    /// </remarks>
    public bool IsDegenerate =>
        A == B || B == C || C == A || (IsQuad && (D == A || D == B || D == C));

    /// <summary>The two triangles a quad splits into, or this face alone.</summary>
    /// <returns>One or two triangular faces.</returns>
    /// <remarks>
    /// Split across the <c>A–C</c> diagonal, always. Choosing the shorter diagonal gives better
    /// triangles on a warped quad and makes the result depend on the vertex positions — so two
    /// meshes with identical topology and slightly different coordinates would triangulate
    /// differently, and nothing downstream could explain why. The stable choice is worth more here
    /// than the prettier one.
    /// </remarks>
    public MeshFace[] Triangulated() =>
        IsQuad ? [new MeshFace(A, B, C), new MeshFace(A, C, D)] : [this];

    /// <summary>Whether two faces name the same corners in the same order.</summary>
    /// <param name="other">The other face.</param>
    /// <returns>True when every index matches.</returns>
    public bool Equals(MeshFace other) =>
        A == other.A && B == other.B && C == other.C && D == other.D;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MeshFace other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(A, B, C, D);

    /// <summary>Whether two faces are equal.</summary>
    /// <param name="left">One face.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they are equal.</returns>
    public static bool operator ==(MeshFace left, MeshFace right) => left.Equals(right);

    /// <summary>Whether two faces differ.</summary>
    /// <param name="left">One face.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they differ.</returns>
    public static bool operator !=(MeshFace left, MeshFace right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => IsQuad
        ? string.Create(CultureInfo.InvariantCulture, $"Quad({A}, {B}, {C}, {D})")
        : string.Create(CultureInfo.InvariantCulture, $"Triangle({A}, {B}, {C})");
}

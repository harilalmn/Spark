using System;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// Which side of a face's boundary a loop is.
/// </summary>
/// <remarks>
/// <b>Not derived from winding, and that is deliberate.</b> Whether a loop encloses material or
/// excludes it can be worked out from its winding in the face's parameter space — and doing so
/// needs the parameter-space curves, needs them to be consistent, and gets it wrong on a face whose
/// surface is closed in one direction. Recording it costs one byte per loop and is right on a
/// cylinder.
/// </remarks>
public enum BrepLoopKind
{
    /// <summary>The loop that bounds the face's material.</summary>
    Outer,

    /// <summary>A loop that cuts a hole out of it.</summary>
    Inner,
}

/// <summary>
/// A vertex of a <see cref="Brep"/>: a point where edges meet.
/// </summary>
/// <param name="Point">The index of its position in the BRep's point array.</param>
/// <remarks>
/// <b>An index rather than the point itself.</b> Two edges that meet share one vertex and therefore
/// one *index*, so moving that point moves both — which is the property a topological model exists
/// to have, and which a struct holding coordinates could not.
/// </remarks>
public readonly record struct BrepVertex(int Point);

/// <summary>
/// An edge of a <see cref="Brep"/>: a curve between two vertices, shared by the faces that meet
/// along it.
/// </summary>
/// <param name="Start">The index of the vertex the curve begins at.</param>
/// <param name="End">The index of the vertex it ends at.</param>
/// <param name="Curve">The index of its curve in the BRep's curve array.</param>
/// <remarks>
/// <b>The curve runs from <paramref name="Start"/> to <paramref name="End"/>, always.</b> A face on
/// the other side of the edge traverses it backwards, and that is recorded on the *trim* rather
/// than by storing the curve twice — which is the whole reason trims exist.
/// </remarks>
public readonly record struct BrepEdge(int Start, int End, int Curve);

/// <summary>
/// A trim: one use of an edge by one loop, and the direction that loop runs along it.
/// </summary>
/// <param name="Edge">The index of the edge being used.</param>
/// <param name="IsReversed">
/// Whether this loop runs from the edge's <see cref="BrepEdge.End"/> to its
/// <see cref="BrepEdge.Start"/>.
/// </param>
/// <remarks>
/// <para>
/// <b>A trim is the level at which orientation lives</b>, and that is the single idea the whole
/// model turns on. An edge has one curve and one direction; the two faces that meet along it
/// traverse it in *opposite* directions, and each records its own direction on its own trim. That
/// is what makes a shell's orientation checkable — every edge of a closed, consistently-oriented
/// shell is used exactly twice, once forwards and once backwards — and it is the same fact
/// <see cref="MeshTopology"/> uses on a mesh.
/// </para>
/// <para>
/// <b>What a trim does not carry yet, and it is a row rather than an omission.</b> In a complete
/// kernel a trim also holds a *pcurve*: the edge's path through the face's own parameter space,
/// which is what lets a face be trimmed by something other than its natural boundary. That needs
/// the planar layer's <c>Curve2d</c> (`E2-T13`), which does not exist — so a trim currently
/// references the edge and its direction, and a face's boundary is described in three dimensions.
/// The consequence is stated on <see cref="BrepFace"/>.
/// </para>
/// </remarks>
public readonly record struct BrepTrim(int Edge, bool IsReversed);

/// <summary>
/// A loop: a closed circuit of trims bounding part of a face.
/// </summary>
/// <param name="FirstTrim">The index of its first trim in the BRep's trim array.</param>
/// <param name="TrimCount">How many trims it has.</param>
/// <param name="Kind">Whether it bounds the face or cuts a hole in it.</param>
/// <remarks>
/// <b>Trims are contiguous, which is why a loop is an offset and a count.</b> The whole model is
/// laid out that way — trims within a loop, loops within a face, faces within a shell — and it is
/// not tidiness: a flat array of indices with contiguous ranges is what marshals across a C ABI in
/// one copy, which is the shape [ADR-0020] chose for the OCCT shim. A model of object references
/// would have to be walked and rebuilt at every crossing.
/// </remarks>
public readonly record struct BrepLoop(int FirstTrim, int TrimCount, BrepLoopKind Kind);

/// <summary>
/// A face: a piece of a surface, bounded by loops.
/// </summary>
/// <param name="Surface">The index of its surface in the BRep's surface array.</param>
/// <param name="FirstLoop">The index of its first loop.</param>
/// <param name="LoopCount">How many loops it has.</param>
/// <param name="IsReversed">
/// Whether the face's outward normal is the opposite of its surface's.
/// </param>
/// <remarks>
/// <para>
/// <b><paramref name="IsReversed"/> is what lets one surface serve two faces</b> — the inner and
/// outer walls of a shelled solid, for instance — and it is also how a face joins a shell whose
/// orientation disagrees with the surface it was built from. Without it, every such case would need
/// a second surface that is the first one flipped.
/// </para>
/// <para>
/// <b>A face with one loop whose trims run the surface's own boundary is *untrimmed*</b>, and that
/// is the only kind this build can tessellate: real trimming needs the parameter-space curves a
/// trim does not carry yet (see <see cref="BrepTrim"/>), and tessellating a genuinely trimmed face
/// is the work that moves behind the kernel seam as `E13-T11`. <see cref="Brep"/> says which kind a
/// face is rather than leaving it to be discovered.
/// </para>
/// </remarks>
public readonly record struct BrepFace(int Surface, int FirstLoop, int LoopCount, bool IsReversed);

/// <summary>
/// A shell: a connected set of faces.
/// </summary>
/// <param name="FirstFace">The index of its first face.</param>
/// <param name="FaceCount">How many faces it has.</param>
/// <remarks>
/// <b>A shell is not necessarily closed</b>, and a <see cref="Brep"/> holding one open shell is a
/// perfectly good surface model — which is what a sheet body is. Whether it *is* closed is asked
/// rather than asserted, because it is the question that decides whether a volume means anything.
/// </remarks>
public readonly record struct BrepShell(int FirstFace, int FaceCount)
{
    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Shell({FaceCount} faces from {FirstFace})");
}

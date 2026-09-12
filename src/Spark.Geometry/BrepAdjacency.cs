using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// The topology of one <see cref="Brep"/> read in the direction the model does not store it
/// (`E2-T65`): the edges at a vertex, the trims on an edge, the loop a trim belongs to and the face
/// a loop belongs to.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model stores each relationship exactly once and in one direction</b> — a face names its
/// loops, a loop names its trims, a trim names its edge, an edge names its vertices — and that is
/// deliberate (`E2-T22`). Storing the reverse alongside it would be a second description of the same
/// fact, which somebody then has to keep in step through every edit, every join and every
/// deserialization; the first time the two disagree the model is silently wrong and nothing says so.
/// </para>
/// <para>
/// <b>So the reverse is computed, kept by the caller, and never by the <see cref="Brep"/>.</b> This
/// type is that computation: one pass over the trims, loops and faces, and thereafter every lookup
/// here is an array read. The caller decides how long to keep it, which is a decision only the
/// caller is in a position to make — a graph node walking a model once wants the scan
/// <see cref="BrepEdgeView.AdjacentFaces"/> does, and a mesher walking it a hundred thousand times
/// wants this.
/// </para>
/// <para>
/// <b>It is a snapshot, and a <see cref="Brep"/> is immutable, so it cannot go stale</b> — but it
/// describes the model it was built from and no other. Building one from a <see cref="Brep"/> forces
/// that model to materialise, exactly as any other structural question does.
/// </para>
/// <para>
/// <b>Where a relationship is stored, the answer is a span into the index and costs nothing.</b>
/// Where it is derived — the faces at a vertex, the edges of a face — the answer is a fresh array,
/// because deriving it means visiting several stored relationships and removing the repeats.
/// </para>
/// </remarks>
public sealed class BrepAdjacency
{
    private readonly int[] _vertexEdgeOffsets;
    private readonly int[] _vertexEdges;
    private readonly int[] _edgeTrimOffsets;
    private readonly int[] _edgeTrims;
    private readonly int[] _trimLoop;
    private readonly int[] _loopFace;
    private readonly BrepFace[] _faces;
    private readonly BrepLoop[] _loops;
    private readonly BrepTrim[] _trims;
    private readonly BrepEdge[] _edges;

    private BrepAdjacency(Brep brep)
    {
        _faces = brep.RawFaces;
        _loops = brep.RawLoops;
        _trims = brep.RawTrims;
        _edges = brep.RawEdges;

        (_vertexEdgeOffsets, _vertexEdges) = BuildVertexEdges(_edges, brep.RawVertices.Length);
        (_edgeTrimOffsets, _edgeTrims) = BuildEdgeTrims(_trims, _edges.Length);

        _trimLoop = Owners(_loops.Length, _trims.Length, loop => (_loops[loop].FirstTrim, _loops[loop].TrimCount));
        _loopFace = Owners(_faces.Length, _loops.Length, face => (_faces[face].FirstLoop, _faces[face].LoopCount));
    }

    /// <summary>How many vertices the model this was built from has.</summary>
    public int VertexCount => _vertexEdgeOffsets.Length - 1;

    /// <summary>How many edges it has.</summary>
    public int EdgeCount => _edgeTrimOffsets.Length - 1;

    /// <summary>How many trims it has.</summary>
    public int TrimCount => _trims.Length;

    /// <summary>How many loops it has.</summary>
    public int LoopCount => _loopFace.Length;

    /// <summary>How many faces it has.</summary>
    public int FaceCount => _faces.Length;

    /// <summary>Builds the index for one model, in a single pass over its topology.</summary>
    /// <param name="brep">The model to index.</param>
    /// <returns>The index, which describes that model and no other.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="brep"/> is <see langword="null"/>.</exception>
    public static BrepAdjacency Of(Brep brep)
    {
        ArgumentNullException.ThrowIfNull(brep);

        return new BrepAdjacency(brep);
    }

    /// <summary>The edges that meet at one vertex.</summary>
    /// <param name="vertex">The vertex index.</param>
    /// <returns>
    /// Their indices, ascending, each once — an edge that starts and ends at this vertex appears
    /// once, not twice.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public ReadOnlySpan<int> EdgesAt(int vertex) => Slice(_vertexEdgeOffsets, _vertexEdges, vertex, nameof(vertex));

    /// <summary>The trims that use one edge.</summary>
    /// <param name="edge">The edge index.</param>
    /// <returns>
    /// Their indices, ascending. Two for an edge inside a closed shell, one for a naked edge, and
    /// more where several faces meet at a seam.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public ReadOnlySpan<int> TrimsOf(int edge) => Slice(_edgeTrimOffsets, _edgeTrims, edge, nameof(edge));

    /// <summary>The loop one trim belongs to.</summary>
    /// <param name="trim">The trim index.</param>
    /// <returns>The loop's index, or -1 for a trim no loop claims.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public int LoopOf(int trim) => _trimLoop[Check(trim, _trimLoop.Length, nameof(trim))];

    /// <summary>The face one loop belongs to.</summary>
    /// <param name="loop">The loop index.</param>
    /// <returns>
    /// The face's index, or -1 for a loop no face claims. An inner loop answers with its face just
    /// as an outer one does.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public int FaceOf(int loop) => _loopFace[Check(loop, _loopFace.Length, nameof(loop))];

    /// <summary>The trim on the other side of one trim's edge.</summary>
    /// <param name="trim">The trim index.</param>
    /// <returns>The partner's index, or -1 when the edge is not used by exactly two trims.</returns>
    /// <remarks>
    /// <b>-1 rather than an exception, because a naked edge is a legitimate model.</b> An open sheet
    /// is made of them, and a caller walking every trim of one would otherwise have to guard each
    /// call with the question this method already answers.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public int PartnerOf(int trim)
    {
        ReadOnlySpan<int> siblings = TrimsOf(_trims[Check(trim, _trims.Length, nameof(trim))].Edge);

        if (siblings.Length != 2)
        {
            return -1;
        }

        return siblings[0] == trim ? siblings[1] : siblings[0];
    }

    /// <summary>The faces that meet at one vertex.</summary>
    /// <param name="vertex">The vertex index.</param>
    /// <returns>Their indices, ascending, each once.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public int[] FacesAt(int vertex)
    {
        SortedSet<int> faces = [];

        foreach (int edge in EdgesAt(vertex))
        {
            foreach (int trim in TrimsOf(edge))
            {
                int loop = _trimLoop[trim];
                int face = loop < 0 ? -1 : _loopFace[loop];

                if (face >= 0)
                {
                    faces.Add(face);
                }
            }
        }

        return [.. faces];
    }

    /// <summary>The edges of one face, over all of its loops.</summary>
    /// <param name="face">The face index.</param>
    /// <returns>
    /// Their indices, in the order the face's loops traverse them, each once — the outer loop first
    /// and then each inner loop, because that is the order the face stores them in.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public int[] EdgesOf(int face) => Around(face, trim => _trims[trim].Edge);

    /// <summary>The vertices of one face, over all of its loops.</summary>
    /// <param name="face">The face index.</param>
    /// <returns>Their indices, in traversal order, each once.</returns>
    /// <remarks>
    /// <b>The direction is taken from each trim, not from its edge</b>, exactly as
    /// <see cref="BrepLoopView.VertexIndices"/> does it: an edge runs from its start vertex to its
    /// end, a loop may run the other way, and reading the edge's own direction gives a circuit that
    /// jumps.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public int[] VerticesOf(int face) =>
        Around(face, trim => _trims[trim].IsReversed ? _edges[_trims[trim].Edge].End : _edges[_trims[trim].Edge].Start);

    private static int Check(int index, int count, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count, name);

        return index;
    }

    private static ReadOnlySpan<int> Slice(int[] offsets, int[] values, int index, string name)
    {
        Check(index, offsets.Length - 1, name);

        return values.AsSpan(offsets[index], offsets[index + 1] - offsets[index]);
    }

    /// <summary>
    /// The owner of each item in a contiguous-span model: for every owner, the span it claims, and
    /// -1 for anything nobody claims.
    /// </summary>
    private static int[] Owners(int ownerCount, int itemCount, Func<int, (int First, int Count)> span)
    {
        int[] owners = new int[itemCount];

        Array.Fill(owners, -1);

        for (int owner = 0; owner < ownerCount; owner++)
        {
            (int first, int count) = span(owner);

            for (int offset = 0; offset < count; offset++)
            {
                owners[first + offset] = owner;
            }
        }

        return owners;
    }

    /// <summary>
    /// <b>Counted first, then filled, which is what keeps this one pass and no allocation per
    /// vertex.</b> The values land in ascending order for free because the source is walked in
    /// ascending order, and callers are told they may rely on that.
    /// </summary>
    private static (int[] Offsets, int[] Values) BuildVertexEdges(BrepEdge[] edges, int vertexCount)
    {
        int[] offsets = new int[vertexCount + 1];

        foreach (BrepEdge edge in edges)
        {
            offsets[edge.Start + 1]++;

            if (edge.End != edge.Start)
            {
                offsets[edge.End + 1]++;
            }
        }

        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            offsets[vertex + 1] += offsets[vertex];
        }

        int[] values = new int[offsets[vertexCount]];
        int[] next = [.. offsets.AsSpan(0, vertexCount)];

        for (int edge = 0; edge < edges.Length; edge++)
        {
            values[next[edges[edge].Start]++] = edge;

            if (edges[edge].End != edges[edge].Start)
            {
                values[next[edges[edge].End]++] = edge;
            }
        }

        return (offsets, values);
    }

    /// <inheritdoc cref="BuildVertexEdges"/>
    private static (int[] Offsets, int[] Values) BuildEdgeTrims(BrepTrim[] trims, int edgeCount)
    {
        int[] offsets = new int[edgeCount + 1];

        foreach (BrepTrim trim in trims)
        {
            offsets[trim.Edge + 1]++;
        }

        for (int edge = 0; edge < edgeCount; edge++)
        {
            offsets[edge + 1] += offsets[edge];
        }

        int[] values = new int[offsets[edgeCount]];
        int[] next = [.. offsets.AsSpan(0, edgeCount)];

        for (int trim = 0; trim < trims.Length; trim++)
        {
            values[next[trims[trim].Edge]++] = trim;
        }

        return (offsets, values);
    }

    private int[] Around(int face, Func<int, int> of)
    {
        BrepFace record = _faces[Check(face, _faces.Length, nameof(face))];

        List<int> found = [];
        HashSet<int> seen = [];

        for (int offset = 0; offset < record.LoopCount; offset++)
        {
            BrepLoop loop = _loops[record.FirstLoop + offset];

            for (int position = 0; position < loop.TrimCount; position++)
            {
                int item = of(loop.FirstTrim + position);

                if (seen.Add(item))
                {
                    found.Add(item);
                }
            }
        }

        return [.. found];
    }
}

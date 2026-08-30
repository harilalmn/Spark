using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// Builds a <see cref="Brep"/>, keeping the contiguity its layout depends on (`E2-T23`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The index model's one real cost is that its arrays have to be written in order</b> — trims
/// grouped by loop, loops by face, faces by shell — and getting that wrong produces a model whose
/// every index is in range and which describes a different shape. This type is what makes that
/// impossible: loops are added by naming their edges, faces by naming their loops, shells by naming
/// their faces, and the ordering is the builder's problem.
/// </para>
/// <para>
/// <b>It checks as it goes, where <see cref="Brep"/>'s constructor does not.</b> The constructor
/// takes any nine arrays, because reading a malformed BRep from a file in order to find out what is
/// wrong with it is a thing a repair tool has to do. A builder is used by code that is *making* one,
/// where an index out of range is a bug to be reported at the line that wrote it.
/// </para>
/// <para>
/// <b>Vertices and edges are shared by identity, not by position.</b> Two faces that meet along an
/// edge must name the *same* edge index or the model has a seam in it that no tolerance closes —
/// so the builder hands back an index and expects it to be reused. It does not weld coincident
/// points, because deciding that two points are the same is a tolerance question and a builder is
/// not the place to answer it.
/// </para>
/// </remarks>
public sealed class BrepBuilder
{
    private readonly List<Point3d> _points = [];
    private readonly List<Curve> _curves = [];
    private readonly List<Surface> _surfaces = [];
    private readonly List<BrepVertex> _vertices = [];
    private readonly List<BrepEdge> _edges = [];
    private readonly List<BrepTrim> _trims = [];
    private readonly List<BrepLoop> _loops = [];
    private readonly List<BrepFace> _faces = [];
    private readonly List<BrepShell> _shells = [];

    private readonly List<int> _facesAwaitingShell = [];

    /// <summary>Adds a vertex at a point.</summary>
    /// <param name="point">Where it is.</param>
    /// <returns>Its index, to be reused by every edge that meets there.</returns>
    public int AddVertex(in Point3d point)
    {
        _points.Add(point);
        _vertices.Add(new BrepVertex(_points.Count - 1));

        return _vertices.Count - 1;
    }

    /// <summary>Adds an edge between two vertices.</summary>
    /// <param name="start">The vertex it begins at.</param>
    /// <param name="end">The vertex it ends at.</param>
    /// <param name="curve">Its curve, running from <paramref name="start"/> to <paramref name="end"/>.</param>
    /// <returns>Its index, to be reused by both faces that meet along it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="curve"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A vertex index is not one this builder gave.</exception>
    public int AddEdge(int start, int end, Curve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        Check(start, _vertices.Count, nameof(start));
        Check(end, _vertices.Count, nameof(end));

        _curves.Add(curve);
        _edges.Add(new BrepEdge(start, end, _curves.Count - 1));

        return _edges.Count - 1;
    }

    /// <summary>Adds a straight edge between two vertices.</summary>
    /// <param name="start">The vertex it begins at.</param>
    /// <param name="end">The vertex it ends at.</param>
    /// <returns>Its index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A vertex index is not one this builder gave.</exception>
    public int AddLineEdge(int start, int end)
    {
        Check(start, _vertices.Count, nameof(start));
        Check(end, _vertices.Count, nameof(end));

        return AddEdge(start, end, new Line(_points[_vertices[start].Point], _points[_vertices[end].Point]));
    }

    /// <summary>Adds a loop over a circuit of edges.</summary>
    /// <param name="edges">
    /// The edges in the order the loop traverses them, each with whether the loop runs backwards
    /// along it.
    /// </param>
    /// <param name="kind">Whether the loop bounds the face or cuts a hole in it.</param>
    /// <returns>The loop's index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edges"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The circuit is empty, names an edge that does not exist, or does not close.
    /// </exception>
    /// <remarks>
    /// <b>Closure is checked here rather than at <see cref="Build"/>.</b> An open loop is the
    /// commonest mistake in a hand-built BRep — usually one edge listed in the wrong direction —
    /// and the message can name the position in the circuit only while the circuit is being added.
    /// </remarks>
    public int AddLoop(IReadOnlyList<(int Edge, bool IsReversed)> edges, BrepLoopKind kind = BrepLoopKind.Outer)
    {
        ArgumentNullException.ThrowIfNull(edges);

        if (edges.Count == 0)
        {
            throw new ArgumentException("A loop needs at least one edge.", nameof(edges));
        }

        int first = _trims.Count;
        int previousEnd = -1;
        int firstStart = -1;

        for (int position = 0; position < edges.Count; position++)
        {
            (int edge, bool reversed) = edges[position];

            Check(edge, _edges.Count, nameof(edges));

            BrepEdge record = _edges[edge];
            int start = reversed ? record.End : record.Start;
            int end = reversed ? record.Start : record.End;

            if (position == 0)
            {
                firstStart = start;
            }
            else if (start != previousEnd)
            {
                _trims.RemoveRange(first, _trims.Count - first);

                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The loop is broken at position {position}: the previous edge ends at vertex {previousEnd} and this one starts at {start}. An edge listed in the wrong direction is the usual cause."),
                    nameof(edges));
            }

            previousEnd = end;
            _trims.Add(new BrepTrim(edge, reversed));
        }

        if (previousEnd != firstStart)
        {
            _trims.RemoveRange(first, _trims.Count - first);

            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The loop does not close: it starts at vertex {firstStart} and ends at {previousEnd}."),
                nameof(edges));
        }

        _loops.Add(new BrepLoop(first, edges.Count, kind));

        return _loops.Count - 1;
    }

    /// <summary>Adds a face on a surface, bounded by loops the builder has already been given.</summary>
    /// <param name="surface">The surface it lies on.</param>
    /// <param name="loops">Its loops, exactly one of which must be outer.</param>
    /// <param name="isReversed">Whether the face's outward normal is the surface's reversed.</param>
    /// <returns>The face's index.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// The loops are empty, name one that does not exist, are not contiguous, or do not contain
    /// exactly one outer loop.
    /// </exception>
    /// <remarks>
    /// <b>The loops must be the ones just added, in order</b>, because a face is an offset and a
    /// count. Rather than silently reordering — which would move loops another face already
    /// referred to — the builder says so, and the fix is to add a face's loops immediately before
    /// the face.
    /// </remarks>
    public int AddFace(Surface surface, IReadOnlyList<int> loops, bool isReversed = false)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(loops);

        if (loops.Count == 0)
        {
            throw new ArgumentException("A face needs an outer loop.", nameof(loops));
        }

        int outer = 0;

        for (int position = 0; position < loops.Count; position++)
        {
            Check(loops[position], _loops.Count, nameof(loops));

            if (position > 0 && loops[position] != loops[position - 1] + 1)
            {
                throw new ArgumentException(
                    "A face's loops have to be contiguous, because a face is an offset and a count. "
                    + "Add a face's loops immediately before the face.",
                    nameof(loops));
            }

            if (_loops[loops[position]].Kind == BrepLoopKind.Outer)
            {
                outer++;
            }
        }

        if (outer != 1)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A face has exactly one outer loop and {outer} were given."),
                nameof(loops));
        }

        _surfaces.Add(surface);
        _faces.Add(new BrepFace(_surfaces.Count - 1, loops[0], loops.Count, isReversed));
        _facesAwaitingShell.Add(_faces.Count - 1);

        return _faces.Count - 1;
    }

    /// <summary>
    /// Closes a shell over every face added since the last one.
    /// </summary>
    /// <returns>The shell's index.</returns>
    /// <exception cref="InvalidOperationException">No faces have been added since the last shell.</exception>
    /// <remarks>
    /// <b>Implicit rather than named, and that is what keeps the faces contiguous.</b> A caller
    /// naming arbitrary faces would be asking the builder either to reorder them — moving faces
    /// another shell already refers to — or to give up contiguity, which is the property the whole
    /// layout rests on.
    /// </remarks>
    public int CloseShell()
    {
        if (_facesAwaitingShell.Count == 0)
        {
            throw new InvalidOperationException(
                "No faces have been added since the last shell was closed, and a shell with no "
                + "faces bounds nothing.");
        }

        _shells.Add(new BrepShell(_facesAwaitingShell[0], _facesAwaitingShell.Count));
        _facesAwaitingShell.Clear();

        return _shells.Count - 1;
    }

    /// <summary>Builds the BRep.</summary>
    /// <returns>The model.</returns>
    /// <exception cref="InvalidOperationException">
    /// Faces have been added that no shell contains.
    /// </exception>
    /// <remarks>
    /// <b>A face outside every shell is refused rather than swept into one.</b> Closing a shell is
    /// the caller saying *these faces belong together*, and inventing one for the leftovers would
    /// be guessing at exactly the fact the model exists to record.
    /// </remarks>
    public Brep Build()
    {
        if (_facesAwaitingShell.Count > 0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_facesAwaitingShell.Count} face(s) belong to no shell. Call CloseShell before Build."));
        }

        return new Brep(
            _points, _curves, _surfaces, _vertices, _edges, _trims, _loops, _faces, _shells);
    }

    private static void Check(int index, int count, string name)
    {
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(
                name,
                index,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"This builder has handed out {count} of these, so {index} is not one of them."));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// A boundary representation: faces on surfaces, bounded by loops of trims over shared edges.
/// </summary>
/// <remarks>
/// <para>
/// <b>Index-based, and that decision has now been paid for twice over.</b> Every relationship is an
/// <see cref="int"/> into a flat array — no object references anywhere in the model. It serialises
/// with no cycles to break, it is immutable without a graph walk, it is cache-friendly, and it is
/// **exactly the shape that marshals across a C ABI in one copy**, which is what
/// [ADR-0020](../../docs/adr/0020-occt-via-c-abi-shim.md) chose for the OCCT shim. A model of
/// object references would have to be walked and rebuilt at every crossing.
/// </para>
/// <para>
/// <b>Everything is contiguous.</b> Trims within a loop, loops within a face, faces within a shell:
/// each is an offset and a count rather than a list of indices. That is what makes a whole BRep
/// nine arrays and no indirection, and it is why <see cref="BrepBuilder"/> exists — writing one by
/// hand means keeping the ordering right, and the builder does it.
/// </para>
/// <para>
/// <b>Navigating is done through the views, not the arrays.</b> <see cref="Face(int)"/> and the
/// rest return <c>readonly ref struct</c> navigators that walk the model in the terms it is
/// actually about — *this face's loops*, *this loop's edges*, *the faces along this edge* — and
/// cost nothing, because a ref struct is a pair of registers and cannot escape to the heap. The
/// index model without them is correct and unusable; that is the whole of `E2-T23`.
/// </para>
/// <para>
/// <b>What this build's BRep cannot do, said here rather than discovered.</b> A trim carries no
/// parameter-space curve (`E2-T13`), so a face's boundary is described in three dimensions and only
/// an *untrimmed* face can be tessellated. Every operation that makes new topology — boolean,
/// fillet, extrude, sew, heal — is behind the kernel seam (`E2-T28`) and is not here. What is here
/// is the model, its construction, its validation, its measurement and its serialization, which is
/// exactly the half `E2-T28` says never crosses.
/// </para>
/// </remarks>
public sealed class Brep
{
    private readonly Point3d[] _points;
    private readonly Curve[] _curves;
    private readonly Surface[] _surfaces;
    private readonly BrepVertex[] _vertices;
    private readonly BrepEdge[] _edges;
    private readonly BrepTrim[] _trims;
    private readonly BrepLoop[] _loops;
    private readonly BrepFace[] _faces;
    private readonly BrepShell[] _shells;

    private BoundingBox _boundingBox;
    private bool _boundingBoxComputed;

    /// <summary>Creates a BRep from the nine arrays that describe it.</summary>
    /// <param name="points">Vertex positions.</param>
    /// <param name="curves">The curves edges lie on.</param>
    /// <param name="surfaces">The surfaces faces lie on.</param>
    /// <param name="vertices">The vertices.</param>
    /// <param name="edges">The edges.</param>
    /// <param name="trims">The trims, contiguous per loop.</param>
    /// <param name="loops">The loops, contiguous per face.</param>
    /// <param name="faces">The faces, contiguous per shell.</param>
    /// <param name="shells">The shells.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// <b>Nothing is checked here, and <see cref="Validate"/> is why.</b> A constructor that threw
    /// on a malformed BRep would make it impossible to *read* one from a file in order to find out
    /// what is wrong with it — and reading a malformed model is exactly what a repair tool does.
    /// Every caller that built one itself should have used <see cref="BrepBuilder"/>, which checks
    /// as it goes.
    /// </remarks>
    public Brep(
        IReadOnlyList<Point3d> points,
        IReadOnlyList<Curve> curves,
        IReadOnlyList<Surface> surfaces,
        IReadOnlyList<BrepVertex> vertices,
        IReadOnlyList<BrepEdge> edges,
        IReadOnlyList<BrepTrim> trims,
        IReadOnlyList<BrepLoop> loops,
        IReadOnlyList<BrepFace> faces,
        IReadOnlyList<BrepShell> shells)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(trims);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(shells);

        _points = [.. points];
        _curves = [.. curves];
        _surfaces = [.. surfaces];
        _vertices = [.. vertices];
        _edges = [.. edges];
        _trims = [.. trims];
        _loops = [.. loops];
        _faces = [.. faces];
        _shells = [.. shells];
    }

    /// <summary>How many vertices there are.</summary>
    public int VertexCount => _vertices.Length;

    /// <summary>How many edges there are.</summary>
    public int EdgeCount => _edges.Length;

    /// <summary>How many trims there are.</summary>
    public int TrimCount => _trims.Length;

    /// <summary>How many loops there are.</summary>
    public int LoopCount => _loops.Length;

    /// <summary>How many faces there are.</summary>
    public int FaceCount => _faces.Length;

    /// <summary>How many shells there are.</summary>
    public int ShellCount => _shells.Length;

    /// <summary>The box containing every vertex and every face's surface.</summary>
    /// <remarks>
    /// <b>The surfaces are included, not only the vertices.</b> A cylinder's two vertices are on its
    /// seam and a box around them alone would exclude most of the solid — which is the kind of
    /// bounding box that makes a spatial index quietly wrong rather than loudly.
    /// </remarks>
    public BoundingBox BoundingBox
    {
        get
        {
            if (!_boundingBoxComputed)
            {
                BoundingBox box = BoundingBox.Empty;

                foreach (Point3d point in _points)
                {
                    box = box.Union(point);
                }

                foreach (Surface surface in _surfaces)
                {
                    box = box.Union(surface.BoundingBox);
                }

                _boundingBox = box;
                _boundingBoxComputed = true;
            }

            return _boundingBox;
        }
    }

    /// <summary>A copy of the vertex positions.</summary>
    /// <returns>The points, in index order.</returns>
    public Point3d[] Points() => [.. _points];

    /// <summary>A copy of the edge curves.</summary>
    /// <returns>The curves, in index order.</returns>
    public Curve[] Curves() => [.. _curves];

    /// <summary>A copy of the face surfaces.</summary>
    /// <returns>The surfaces, in index order.</returns>
    public Surface[] Surfaces() => [.. _surfaces];

    /// <summary>A copy of the vertices.</summary>
    /// <returns>The vertices, in index order.</returns>
    public BrepVertex[] Vertices() => [.. _vertices];

    /// <summary>A copy of the edges.</summary>
    /// <returns>The edges, in index order.</returns>
    public BrepEdge[] Edges() => [.. _edges];

    /// <summary>A copy of the trims.</summary>
    /// <returns>The trims, in index order.</returns>
    public BrepTrim[] Trims() => [.. _trims];

    /// <summary>A copy of the loops.</summary>
    /// <returns>The loops, in index order.</returns>
    public BrepLoop[] Loops() => [.. _loops];

    /// <summary>A copy of the faces.</summary>
    /// <returns>The faces, in index order.</returns>
    public BrepFace[] Faces() => [.. _faces];

    /// <summary>A copy of the shells.</summary>
    /// <returns>The shells, in index order.</returns>
    public BrepShell[] Shells() => [.. _shells];

    /// <summary>A navigator over one face.</summary>
    /// <param name="index">The face index.</param>
    /// <returns>A view that can walk to its loops, trims, edges and surface.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public BrepFaceView Face(int index) => new(this, Check(index, _faces.Length, nameof(index)));

    /// <summary>A navigator over one loop.</summary>
    /// <param name="index">The loop index.</param>
    /// <returns>A view that can walk to its trims and its face.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public BrepLoopView Loop(int index) => new(this, Check(index, _loops.Length, nameof(index)));

    /// <summary>A navigator over one edge.</summary>
    /// <param name="index">The edge index.</param>
    /// <returns>A view that can walk to its vertices, its curve and the faces along it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public BrepEdgeView Edge(int index) => new(this, Check(index, _edges.Length, nameof(index)));

    /// <summary>A navigator over one shell.</summary>
    /// <param name="index">The shell index.</param>
    /// <returns>A view that can walk to its faces.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public BrepShellView Shell(int index) => new(this, Check(index, _shells.Length, nameof(index)));

    /// <summary>The position of one vertex.</summary>
    /// <param name="index">The vertex index.</param>
    /// <returns>Its point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the model.</exception>
    public Point3d VertexPoint(int index) =>
        _points[_vertices[Check(index, _vertices.Length, nameof(index))].Point];

    /// <summary>
    /// Every structural problem with this model, or nothing when it is sound.
    /// </summary>
    /// <returns>The problems, each a sentence naming the element it is about.</returns>
    /// <remarks>
    /// <para>
    /// <b>Validation is ours and stays ours</b> (`E2-T28` puts the data model and its validation in
    /// front of the kernel seam). What is *not* here is healing and sewing — those are
    /// `E13-T10`, behind the seam, because OCCT's <c>ShapeFix</c> does them and a second managed
    /// implementation would be a worse one.
    /// </para>
    /// <para>
    /// <b>It returns a list rather than throwing at the first problem.</b> A malformed BRep usually
    /// has several, and finding them one run at a time is how a repair session becomes an afternoon.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        for (int index = 0; index < _vertices.Length; index++)
        {
            if (_vertices[index].Point < 0 || _vertices[index].Point >= _points.Length)
            {
                problems.Add(Say($"Vertex {index} names point {_vertices[index].Point}, and there are {_points.Length}."));
            }
        }

        for (int index = 0; index < _edges.Length; index++)
        {
            BrepEdge edge = _edges[index];

            if (edge.Curve < 0 || edge.Curve >= _curves.Length)
            {
                problems.Add(Say($"Edge {index} names curve {edge.Curve}, and there are {_curves.Length}."));
            }

            if (edge.Start < 0 || edge.Start >= _vertices.Length || edge.End < 0 || edge.End >= _vertices.Length)
            {
                problems.Add(Say($"Edge {index} names vertices {edge.Start} and {edge.End}, and there are {_vertices.Length}."));
            }
        }

        for (int index = 0; index < _trims.Length; index++)
        {
            if (_trims[index].Edge < 0 || _trims[index].Edge >= _edges.Length)
            {
                problems.Add(Say($"Trim {index} names edge {_trims[index].Edge}, and there are {_edges.Length}."));
            }
        }

        for (int index = 0; index < _loops.Length; index++)
        {
            BrepLoop loop = _loops[index];

            if (loop.TrimCount < 1)
            {
                problems.Add(Say($"Loop {index} has {loop.TrimCount} trims; a loop needs at least one."));
            }

            if (loop.FirstTrim < 0 || loop.FirstTrim + loop.TrimCount > _trims.Length)
            {
                problems.Add(Say($"Loop {index} spans trims {loop.FirstTrim}..{loop.FirstTrim + loop.TrimCount - 1}, and there are {_trims.Length}."));
                continue;
            }

            ValidateLoopIsClosed(index, loop, problems);
        }

        for (int index = 0; index < _faces.Length; index++)
        {
            BrepFace face = _faces[index];

            if (face.Surface < 0 || face.Surface >= _surfaces.Length)
            {
                problems.Add(Say($"Face {index} names surface {face.Surface}, and there are {_surfaces.Length}."));
            }

            if (face.LoopCount < 1)
            {
                problems.Add(Say($"Face {index} has {face.LoopCount} loops; a face needs an outer one."));
            }

            if (face.FirstLoop < 0 || face.FirstLoop + face.LoopCount > _loops.Length)
            {
                problems.Add(Say($"Face {index} spans loops {face.FirstLoop}..{face.FirstLoop + face.LoopCount - 1}, and there are {_loops.Length}."));
                continue;
            }

            int outer = 0;

            for (int loop = face.FirstLoop; loop < face.FirstLoop + face.LoopCount; loop++)
            {
                if (_loops[loop].Kind == BrepLoopKind.Outer)
                {
                    outer++;
                }
            }

            if (outer != 1)
            {
                problems.Add(Say($"Face {index} has {outer} outer loops; a face has exactly one."));
            }
        }

        for (int index = 0; index < _shells.Length; index++)
        {
            BrepShell shell = _shells[index];

            if (shell.FaceCount < 1 || shell.FirstFace < 0 || shell.FirstFace + shell.FaceCount > _faces.Length)
            {
                problems.Add(Say($"Shell {index} spans faces {shell.FirstFace}..{shell.FirstFace + shell.FaceCount - 1}, and there are {_faces.Length}."));
            }
        }

        ValidateEdgeUse(problems);

        return problems;
    }

    /// <summary>
    /// Whether every edge is used exactly twice, once in each direction — the condition for a
    /// closed, consistently-oriented solid.
    /// </summary>
    /// <remarks>
    /// <b>The same question <see cref="MeshTopology.IsClosed"/> asks, at the topological level.</b>
    /// An edge used once is a boundary; an edge used twice in the *same* direction is two faces
    /// wound inconsistently, which is a solid that is inside-out in patches. Both are caught by
    /// counting.
    /// </remarks>
    public bool IsSolid
    {
        get
        {
            if (_edges.Length == 0)
            {
                return false;
            }

            int[] forwards = new int[_edges.Length];
            int[] backwards = new int[_edges.Length];

            foreach (BrepTrim trim in _trims)
            {
                if (trim.Edge < 0 || trim.Edge >= _edges.Length)
                {
                    return false;
                }

                if (trim.IsReversed)
                {
                    backwards[trim.Edge]++;
                }
                else
                {
                    forwards[trim.Edge]++;
                }
            }

            for (int index = 0; index < _edges.Length; index++)
            {
                if (forwards[index] != 1 || backwards[index] != 1)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Whether every face's boundary is its surface's own — the only kind this build can
    /// tessellate.
    /// </summary>
    /// <remarks>
    /// See the remarks on <see cref="BrepFace"/>: real trimming needs a trim to carry its
    /// parameter-space curve, which is `E2-T13`, and tessellating a genuinely trimmed face is
    /// `E13-T11`, behind the kernel seam.
    /// </remarks>
    public bool IsUntrimmed
    {
        get
        {
            foreach (BrepFace face in _faces)
            {
                if (face.LoopCount != 1)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"Brep({_shells.Length} shells, {_faces.Length} faces, {_edges.Length} edges, {_vertices.Length} vertices)");

    /// <summary>The arrays, for the views and the builder. Never handed to a caller.</summary>
    internal Point3d[] RawPoints => _points;

    /// <inheritdoc cref="RawPoints"/>
    internal Curve[] RawCurves => _curves;

    /// <inheritdoc cref="RawPoints"/>
    internal Surface[] RawSurfaces => _surfaces;

    /// <inheritdoc cref="RawPoints"/>
    internal BrepVertex[] RawVertices => _vertices;

    /// <inheritdoc cref="RawPoints"/>
    internal BrepEdge[] RawEdges => _edges;

    /// <inheritdoc cref="RawPoints"/>
    internal BrepTrim[] RawTrims => _trims;

    /// <inheritdoc cref="RawPoints"/>
    internal BrepLoop[] RawLoops => _loops;

    /// <inheritdoc cref="RawPoints"/>
    internal BrepFace[] RawFaces => _faces;

    /// <inheritdoc cref="RawPoints"/>
    internal BrepShell[] RawShells => _shells;

    /// <summary>
    /// Whether a loop's trims run end to end, each starting where the last one finished.
    /// </summary>
    /// <remarks>
    /// <b>This is the check that catches a loop assembled in the wrong order</b>, which is the
    /// commonest mistake in a hand-built BRep and which nothing else notices: every index is in
    /// range, every element exists, and the face simply describes a different shape from the one
    /// intended.
    /// </remarks>
    private void ValidateLoopIsClosed(int index, in BrepLoop loop, List<string> problems)
    {
        int previousEnd = -1;
        int firstStart = -1;

        for (int position = 0; position < loop.TrimCount; position++)
        {
            BrepTrim trim = _trims[loop.FirstTrim + position];

            if (trim.Edge < 0 || trim.Edge >= _edges.Length)
            {
                return;
            }

            BrepEdge edge = _edges[trim.Edge];
            int start = trim.IsReversed ? edge.End : edge.Start;
            int end = trim.IsReversed ? edge.Start : edge.End;

            if (position == 0)
            {
                firstStart = start;
            }
            else if (start != previousEnd)
            {
                problems.Add(Say(
                    $"Loop {index} is broken at trim {position}: the previous trim ends at vertex "
                    + $"{previousEnd} and this one starts at {start}."));

                return;
            }

            previousEnd = end;
        }

        if (previousEnd != firstStart)
        {
            problems.Add(Say(
                $"Loop {index} does not close: it starts at vertex {firstStart} and ends at {previousEnd}."));
        }
    }

    private void ValidateEdgeUse(List<string> problems)
    {
        int[] uses = new int[_edges.Length];

        foreach (BrepTrim trim in _trims)
        {
            if (trim.Edge >= 0 && trim.Edge < uses.Length)
            {
                uses[trim.Edge]++;
            }
        }

        for (int index = 0; index < uses.Length; index++)
        {
            if (uses[index] == 0)
            {
                problems.Add(Say($"Edge {index} is used by no loop, so it belongs to no face."));
            }
            else if (uses[index] > 2)
            {
                problems.Add(Say($"Edge {index} is used {uses[index]} times; an edge joins at most two faces."));
            }
        }
    }

    private static string Say(string message) => message;

    private static int Check(int index, int count, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count, name);

        return index;
    }
}

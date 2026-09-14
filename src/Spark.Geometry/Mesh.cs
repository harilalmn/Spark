using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// An indexed mesh: vertices, triangular and quadrilateral faces, and optional per-vertex normals,
/// texture coordinates and colours.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the type three different things meet at</b>, which is why its contract is worth
/// settling before any of them: tessellation writes into it (`E2-T26`), the viewport draws it, and
/// every mesh file format reads and writes it (`E2-T34`, `E2-T35`). A change here is a change to
/// all three.
/// </para>
/// <para>
/// <b>Immutable, and the arrays are copied in and out.</b> A mesh of a million vertices is
/// expensive to copy and far more expensive to debug when two nodes share one and one of them
/// welds it. The kernel's rule is that geometry is a value you can hold without a lock, and a mesh
/// is the type where that rule earns the most.
/// </para>
/// <para>
/// <b>The adjacency is built lazily and never eagerly.</b> Most meshes are produced, drawn and
/// discarded without anybody asking a topological question — a tessellated surface on its way to
/// the viewport is the common case — and building a halfedge structure for them would be pure
/// cost, roughly doubling the memory a mesh occupies. Ask <see cref="Topology"/> and it is built
/// once and kept.
/// </para>
/// <para>
/// <b>Colours are packed <c>uint</c>s, not <c>Rgba</c>, and that is a layering decision rather
/// than a preference.</b> <c>Rgba</c> lives in <c>Spark.Api</c> beside <c>Appearance</c> because
/// the kernel carries no styling (`E2-T1`), and <c>Spark.Api</c> references the kernel, so the
/// kernel cannot reference it back. **A scanned or baked vertex colour is data rather than
/// styling** — a PLY that carries them would otherwise be read lossily — so it is here, in the
/// packing every file format already uses: <c>0xRRGGBBAA</c>. Converting to <c>Rgba</c> at the
/// display layer is one line, and the alternative — a second colour type in the kernel that must
/// agree with the first — is worse than the shift.
/// </para>
/// </remarks>
public sealed class Mesh
{
    private readonly Point3d[] _vertices;
    private readonly MeshFace[] _faces;
    private readonly Vector3d[]? _normals;
    private readonly UV[]? _textureCoordinates;
    private readonly uint[]? _colours;

    private MeshTopology? _topology;
    private BoundingBox _boundingBox;
    private bool _boundingBoxComputed;
    private double _area = -1.0;

    /// <summary>Creates a mesh from vertices and faces.</summary>
    /// <param name="vertices">The vertex positions.</param>
    /// <param name="faces">The faces, indexing into <paramref name="vertices"/>.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">A face indexes a vertex that is not there.</exception>
    public Mesh(IReadOnlyList<Point3d> vertices, IReadOnlyList<MeshFace> faces)
        : this(vertices, faces, normals: null, textureCoordinates: null, colours: null)
    {
    }

    /// <summary>Creates a mesh with whichever per-vertex channels are known.</summary>
    /// <param name="vertices">The vertex positions.</param>
    /// <param name="faces">The faces, indexing into <paramref name="vertices"/>.</param>
    /// <param name="normals">One normal per vertex, or null.</param>
    /// <param name="textureCoordinates">One texture coordinate per vertex, or null.</param>
    /// <param name="colours">One packed <c>0xRRGGBBAA</c> colour per vertex, or null.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// A face indexes a vertex that is not there, or a channel is a different length from the
    /// vertices.
    /// </exception>
    /// <remarks>
    /// <b>Every index is checked here, once.</b> A face pointing past the end of the vertex array
    /// is the single most common thing wrong with a mesh built by hand or read from a file, and
    /// the failure it produces later — an <see cref="IndexOutOfRangeException"/> from inside a
    /// renderer or a tessellator — names nothing a user could act on. Checking costs one pass over
    /// the faces at construction and turns it into a sentence.
    /// </remarks>
    public Mesh(
        IReadOnlyList<Point3d> vertices,
        IReadOnlyList<MeshFace> faces,
        IReadOnlyList<Vector3d>? normals,
        IReadOnlyList<UV>? textureCoordinates,
        IReadOnlyList<uint>? colours)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(faces);

        _vertices = [.. vertices];
        _faces = [.. faces];

        foreach (MeshFace face in _faces)
        {
            for (int corner = 0; corner < face.Count; corner++)
            {
                if (face[corner] >= _vertices.Length)
                {
                    throw new ArgumentException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"A face indexes vertex {face[corner]} and the mesh has {_vertices.Length}."),
                        nameof(faces));
                }
            }
        }

        _normals = Channel(normals, nameof(normals));
        _textureCoordinates = Channel(textureCoordinates, nameof(textureCoordinates));
        _colours = Channel(colours, nameof(colours));
    }

    /// <summary>How many vertices there are.</summary>
    public int VertexCount => _vertices.Length;

    /// <summary>How many faces there are.</summary>
    public int FaceCount => _faces.Length;

    /// <summary>How many faces are quadrilateral.</summary>
    public int QuadCount
    {
        get
        {
            int quads = 0;

            foreach (MeshFace face in _faces)
            {
                if (face.IsQuad)
                {
                    quads++;
                }
            }

            return quads;
        }
    }

    /// <summary>Whether the mesh carries a normal per vertex.</summary>
    public bool HasNormals => _normals is not null;

    /// <summary>Whether the mesh carries a texture coordinate per vertex.</summary>
    public bool HasTextureCoordinates => _textureCoordinates is not null;

    /// <summary>Whether the mesh carries a colour per vertex.</summary>
    public bool HasColours => _colours is not null;

    /// <summary>One vertex.</summary>
    /// <param name="index">Its index.</param>
    /// <returns>The position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the mesh.</exception>
    public Point3d Vertex(int index) => _vertices[Check(index, _vertices.Length, nameof(index))];

    /// <summary>One face.</summary>
    /// <param name="index">Its index.</param>
    /// <returns>The face.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the mesh.</exception>
    public MeshFace Face(int index) => _faces[Check(index, _faces.Length, nameof(index))];

    /// <summary>A copy of the vertex positions.</summary>
    /// <returns>The positions, in index order.</returns>
    public Point3d[] Vertices() => [.. _vertices];

    /// <summary>A copy of the faces.</summary>
    /// <returns>The faces, in index order.</returns>
    public MeshFace[] Faces() => [.. _faces];

    /// <summary>
    /// The mesh with its vertices moved towards the average of their neighbours (`E2-T68`).
    /// </summary>
    /// <param name="strength">
    /// How far each vertex moves towards that average, from 0 (not at all) to 1 (all the way).
    /// </param>
    /// <param name="passes">How many times to repeat it. At least 1.</param>
    /// <returns>A mesh with the same faces and different vertex positions.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The strength is outside 0 to 1 or not finite, or the pass count is below 1.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Laplacian smoothing. <b>It moves points and nothing else</b> — every face, every index and
    /// the whole edge set survive, so the result's topology is the original's and a caller can
    /// still pair the two index for index.
    /// </para>
    /// <para>
    /// <b>Boundary vertices are pinned, and that is the decision in this member.</b> A vertex on an
    /// open edge has fewer neighbours, all of them on one side, so averaging pulls it inwards —
    /// smooth an open grid a few times without pinning and it shrinks away from its own boundary
    /// while looking perfectly smooth in the middle. The boundary is identified through
    /// <see cref="MeshTopology.NakedEdges"/> and held still.
    /// </para>
    /// <para>
    /// <b>Every pass reads the previous pass's positions</b>, not its own partial results, so the
    /// answer does not depend on the order the vertices happen to be stored in.
    /// </para>
    /// <para>
    /// The optional channels are carried across unchanged: smoothing moves vertices, and a normal
    /// that was supplied by the caller is theirs to recompute if they want it to follow.
    /// </para>
    /// </remarks>
    public Mesh Smoothed(double strength = 0.5, int passes = 1)
    {
        if (!double.IsFinite(strength) || strength < 0.0 || strength > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strength), strength, "A smoothing strength runs from 0 to 1.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(passes, 1);

        MeshTopology topology = Topology;

        bool[] pinned = new bool[_vertices.Length];
        foreach ((int from, int to) in topology.NakedEdges())
        {
            pinned[from] = true;
            pinned[to] = true;
        }

        // The neighbour sets do not change - only the positions do - so they are gathered once.
        int[][] neighbours = new int[_vertices.Length][];
        for (int index = 0; index < _vertices.Length; index++)
        {
            neighbours[index] = pinned[index] ? [] : topology.VerticesAroundVertex(index);
        }

        Point3d[] current = [.. _vertices];

        for (int pass = 0; pass < passes; pass++)
        {
            Point3d[] next = new Point3d[current.Length];

            for (int index = 0; index < current.Length; index++)
            {
                int[] around = neighbours[index];

                if (pinned[index] || around.Length == 0)
                {
                    next[index] = current[index];

                    continue;
                }

                double x = 0.0;
                double y = 0.0;
                double z = 0.0;

                foreach (int neighbour in around)
                {
                    x += current[neighbour].X;
                    y += current[neighbour].Y;
                    z += current[neighbour].Z;
                }

                Point3d average = new(x / around.Length, y / around.Length, z / around.Length);

                next[index] = current[index] + ((average - current[index]) * strength);
            }

            current = next;
        }

        return new Mesh(current, _faces, _normals, _textureCoordinates, _colours);
    }

    /// <summary>
    /// The mesh split into its connected pieces (`E2-T68`).
    /// </summary>
    /// <returns>
    /// One mesh per connected component, each carrying only the vertices its own faces use. A mesh
    /// that is already in one piece returns itself, and an empty mesh returns nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A connected-component walk over <see cref="MeshTopology.AdjacentFaces"/>, which is the
    /// whole algorithm — the topology had already answered the hard part.
    /// </para>
    /// <para>
    /// <b>Connected means sharing an edge, not sharing a vertex</b>, and the two differ: two boxes
    /// touching at a single corner come back as <b>two</b> pieces. That is what
    /// <see cref="MeshTopology.AdjacentFaces"/> answers and it is what a person means by a piece —
    /// something you could pick up on its own. It is stated here because the other reading is
    /// defensible and a caller should not have to find out by experiment.
    /// </para>
    /// <para>
    /// <b>Each piece is renumbered.</b> It carries only the vertices its own faces use, with the
    /// face indices rewritten to match, so a piece's <see cref="VertexCount"/> is about its own
    /// geometry. Keeping the original vertex array would have been simpler and would produce
    /// pieces that render identically, round-trip, and report a vertex count belonging to a mesh
    /// they are no longer part of.
    /// </para>
    /// <para>
    /// <b>The optional channels travel with their vertices.</b> Normals, texture coordinates and
    /// colours are carried across by the same renumbering, so a piece keeps whatever the original
    /// had rather than losing it at the split.
    /// </para>
    /// </remarks>
    public Mesh[] Explode()
    {
        if (_faces.Length == 0)
        {
            return [];
        }

        MeshTopology topology = Topology;
        int[] component = new int[_faces.Length];
        Array.Fill(component, -1);
        int components = 0;

        for (int seed = 0; seed < _faces.Length; seed++)
        {
            if (component[seed] >= 0)
            {
                continue;
            }

            // Breadth-first from this face, claiming everything edge-connected to it.
            Queue<int> pending = new();
            pending.Enqueue(seed);
            component[seed] = components;

            while (pending.Count > 0)
            {
                foreach (int neighbour in topology.AdjacentFaces(pending.Dequeue()))
                {
                    if (component[neighbour] < 0)
                    {
                        component[neighbour] = components;
                        pending.Enqueue(neighbour);
                    }
                }
            }

            components++;
        }

        if (components == 1)
        {
            return [this];
        }

        Mesh[] pieces = new Mesh[components];

        for (int index = 0; index < components; index++)
        {
            pieces[index] = Piece(index, component);
        }

        return pieces;
    }

    /// <summary>One connected piece, with its vertices renumbered to just the ones it uses.</summary>
    /// <param name="which">The component index.</param>
    /// <param name="component">Which component each face belongs to.</param>
    /// <returns>The piece.</returns>
    private Mesh Piece(int which, int[] component)
    {
        Dictionary<int, int> renumbered = [];
        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        for (int index = 0; index < _faces.Length; index++)
        {
            if (component[index] != which)
            {
                continue;
            }

            MeshFace face = _faces[index];
            int[] corners = new int[4];

            for (int corner = 0; corner < face.Count; corner++)
            {
                int original = face[corner];

                if (!renumbered.TryGetValue(original, out int moved))
                {
                    moved = vertices.Count;
                    renumbered[original] = moved;
                    vertices.Add(_vertices[original]);
                }

                corners[corner] = moved;
            }

            faces.Add(face.IsQuad
                ? new MeshFace(corners[0], corners[1], corners[2], corners[3])
                : new MeshFace(corners[0], corners[1], corners[2]));
        }

        return new Mesh(
            vertices,
            faces,
            Carry(_normals, renumbered, vertices.Count),
            Carry(_textureCoordinates, renumbered, vertices.Count),
            Carry(_colours, renumbered, vertices.Count));
    }

    /// <summary>Carries one optional per-vertex channel across a renumbering, or nothing if absent.</summary>
    /// <typeparam name="T">The channel's element type.</typeparam>
    /// <param name="channel">The original channel, which may be absent.</param>
    /// <param name="renumbered">Original vertex index to new index.</param>
    /// <param name="count">How many vertices the piece has.</param>
    /// <returns>The channel for the piece, or null when the original had none.</returns>
    private static T[]? Carry<T>(T[]? channel, Dictionary<int, int> renumbered, int count)
    {
        if (channel is null)
        {
            return null;
        }

        T[] moved = new T[count];

        foreach ((int original, int index) in renumbered)
        {
            moved[index] = channel[original];
        }

        return moved;
    }

    /// <summary>
    /// The centroid of every face, in face order (`E2-T69`).
    /// </summary>
    /// <returns>One point per face: the average of the corners that face actually has.</returns>
    /// <remarks>
    /// <para>
    /// <b>Dynamo calls this <c>TriangleCentroids</c> and Spark's faces are not all triangles</b>,
    /// which is a decision this member has to make rather than inherit. A <see cref="MeshFace"/>
    /// may have four corners, and the answer here is the average of the corners the face has — so
    /// this is the centroid of each <i>face</i>, and the name is kept for the caller who is looking
    /// for it.
    /// </para>
    /// <para>
    /// <b>Splitting quads into triangles first was the alternative and it is worse</b>: it would
    /// return more points than there are faces and destroy the one property a caller actually uses,
    /// which is that the result pairs index for index with <see cref="Faces"/>.
    /// </para>
    /// <para>
    /// For a triangle this is the true centroid — the intersection of the medians. For a quad it is
    /// the average of the four corners, which is the centroid of a planar parallelogram and is near
    /// but not equal to the centroid of a general quadrilateral's area.
    /// </para>
    /// </remarks>
    public Point3d[] TriangleCentroids()
    {
        Point3d[] centroids = new Point3d[_faces.Length];

        for (int index = 0; index < _faces.Length; index++)
        {
            MeshFace face = _faces[index];
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;

            for (int corner = 0; corner < face.Count; corner++)
            {
                Point3d point = _vertices[face[corner]];

                x += point.X;
                y += point.Y;
                z += point.Z;
            }

            centroids[index] = new Point3d(x / face.Count, y / face.Count, z / face.Count);
        }

        return centroids;
    }

    /// <summary>
    /// The mesh with its rubbish removed: degenerate faces, duplicate faces and orphaned vertices
    /// (`E2-T68`).
    /// </summary>
    /// <returns>
    /// The repaired mesh. A mesh that was already clean comes back unchanged rather than rebuilt.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Three passes, and the order matters because each one creates work for the next.</b>
    /// Degenerate faces go first — a face naming the same vertex twice, or whose corners are
    /// collinear and so enclose no area. Then duplicates, because removing degenerates can leave
    /// two identical faces where there were three. Then the vertices nothing indexes any more,
    /// which only exist once the first two passes have run.
    /// </para>
    /// <para>
    /// <b>Two faces are duplicates when they use the same <i>set</i> of vertices</b>, which is the
    /// definition written down rather than left to be inferred. It catches a face repeated with the
    /// opposite winding — the commonest way a mesh ends up with two — and it means a mesh whose
    /// front and back are separate coincident faces loses one of them. That is what repair is for;
    /// a caller who wanted both had a two-sided surface and not a mesh.
    /// </para>
    /// <para>
    /// <b>The renumbering is the part that fails quietly.</b> Dropping a vertex from the middle of
    /// the list shifts every index above it, so a face that is not renumbered points at a
    /// <i>different</i> vertex — a valid index, a mesh that loads, and geometry that is wrong. The
    /// renumbering here reuses <c>Carry</c>, the same helper <see cref="Explode"/> uses, so the
    /// normals, texture coordinates and colours move with the vertices rather than being left
    /// behind.
    /// </para>
    /// <para>
    /// <b>This does not weld and it does not fill holes.</b> Coincident-but-separate vertices are
    /// <see cref="Welded(double)"/>'s question, because closing them needs a tolerance and this
    /// member takes none; holes are <c>MakeWatertight</c>'s. Repair is the pass that needs no
    /// judgement, which is why it needs no parameters.
    /// </para>
    /// </remarks>
    public Mesh Repair()
    {
        List<MeshFace> kept = [];
        HashSet<string> seen = [];

        foreach (MeshFace face in _faces)
        {
            if (IsDegenerate(face))
            {
                continue;
            }

            if (!seen.Add(FaceKey(face)))
            {
                continue;
            }

            kept.Add(face);
        }

        // WHICH VERTICES ANYTHING STILL INDEXES, NUMBERED IN ASCENDING ORIGINAL ORDER. Numbering
        // them in the order the faces happen to reach them would renumber a mesh that needs no
        // repair at all - a sphere's faces do not visit its vertices in index order - so the
        // "nothing changed" case would quietly permute the mesh and the early return below would
        // never fire. Ascending order makes an untouched mesh map to itself.
        bool[] indexed = new bool[_vertices.Length];
        foreach (MeshFace face in kept)
        {
            for (int corner = 0; corner < face.Count; corner++)
            {
                indexed[face[corner]] = true;
            }
        }

        Dictionary<int, int> renumbered = [];
        for (int vertex = 0; vertex < indexed.Length; vertex++)
        {
            if (indexed[vertex])
            {
                renumbered[vertex] = renumbered.Count;
            }
        }

        if (kept.Count == _faces.Length && renumbered.Count == _vertices.Length)
        {
            return this;
        }

        Point3d[] vertices = new Point3d[renumbered.Count];
        foreach ((int original, int index) in renumbered)
        {
            vertices[index] = _vertices[original];
        }

        MeshFace[] faces = new MeshFace[kept.Count];
        for (int index = 0; index < kept.Count; index++)
        {
            MeshFace face = kept[index];

            faces[index] = face.Count == 3
                ? new MeshFace(
                    renumbered[face[0]], renumbered[face[1]], renumbered[face[2]])
                : new MeshFace(
                    renumbered[face[0]], renumbered[face[1]], renumbered[face[2]], renumbered[face[3]]);
        }

        return new Mesh(
            vertices,
            faces,
            Carry(_normals, renumbered, renumbered.Count),
            Carry(_textureCoordinates, renumbered, renumbered.Count),
            Carry(_colours, renumbered, renumbered.Count));
    }

    /// <summary>Whether a face encloses no area, either by repeating a vertex or by being flat.</summary>
    /// <param name="face">The face.</param>
    /// <returns>Whether it is degenerate.</returns>
    /// <remarks>
    /// <b>A repeated index and a collinear triple are the same defect seen twice</b> — one in the
    /// indices and one in the coordinates — so both are tested here rather than one being left for
    /// a caller to notice. A quad is degenerate only when <i>both</i> of its triangles are, because
    /// a quad with three collinear corners is still a triangle and still a surface.
    /// </remarks>
    private bool IsDegenerate(MeshFace face)
    {
        for (int corner = 0; corner < face.Count; corner++)
        {
            for (int other = corner + 1; other < face.Count; other++)
            {
                if (face[corner] == face[other])
                {
                    return true;
                }
            }
        }

        for (int corner = 2; corner < face.Count; corner++)
        {
            Vector3d first = _vertices[face[corner - 1]] - _vertices[face[0]];
            Vector3d second = _vertices[face[corner]] - _vertices[face[0]];

            if (first.Cross(second).LengthSquared > 0.0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A face's identity for duplicate detection: its vertices, sorted.</summary>
    /// <param name="face">The face.</param>
    /// <returns>A key equal for two faces using the same vertices in any order.</returns>
    private static string FaceKey(MeshFace face)
    {
        int[] corners = new int[face.Count];
        for (int corner = 0; corner < face.Count; corner++)
        {
            corners[corner] = face[corner];
        }

        Array.Sort(corners);

        return string.Join(',', corners);
    }

    /// <summary>
    /// The point on the mesh's surface nearest a given point (`E2-T69`).
    /// </summary>
    /// <param name="point">The point to measure from. It need not be near the mesh.</param>
    /// <returns>The nearest point <i>on the surface</i>, which is generally not a vertex.</returns>
    /// <exception cref="InvalidOperationException">The mesh has no faces to be near.</exception>
    /// <remarks>
    /// <para>
    /// <b>Nothing in Spark could answer this before, and two things that look as though they could
    /// cannot.</b> <c>Spark.Viewport</c>'s picker does ray-triangle work over a bounding-volume
    /// hierarchy, but it is a <i>renderer</i> and not the kernel; and
    /// <see cref="Point3d"/>'s duplicate pruning uses a k-d tree that answers
    /// point-to-<b>point</b>, where this is point-to-<b>surface</b>. The nearest point on a mesh is
    /// almost never one of its vertices.
    /// </para>
    /// <para>
    /// <b>The work is the closest point on a triangle, and it is not a projection onto its
    /// plane.</b> Projecting and clamping gets the interior of a face right and everything else
    /// wrong — and everything else is the common case, because a point outside a mesh is usually
    /// nearest an <b>edge</b> or a <b>vertex</b>. A triangle divides space into <b>seven</b> Voronoi
    /// regions: one over the face, three beyond the edges and three beyond the vertices, and which
    /// region the point falls in decides the answer. The tests that matter are the ones outside the
    /// face region.
    /// </para>
    /// <para>
    /// <b>A quad is answered as its two triangles</b>, fanned from its first corner. That matters
    /// only for a quad whose four corners are not coplanar, where the two triangles are a real
    /// surface and the quad is not — and answering against the triangles is answering against the
    /// shape that actually exists.
    /// </para>
    /// <para>
    /// <b>Every face is visited, and the cost is stated rather than apologised for.</b> This is
    /// O(faces) per query, which is right for the meshes a person inspects and wrong for a
    /// hundred-thousand-triangle scan. A bounding-volume hierarchy is the answer above some size,
    /// that size is <b>measurable</b>, and the day somebody measures it this implementation is what
    /// the replacement has to agree with.
    /// </para>
    /// </remarks>
    public Point3d ClosestPoint(in Point3d point)
    {
        if (_faces.Length == 0)
        {
            throw new InvalidOperationException(
                "A mesh with no faces has no surface for a point to be near.");
        }

        Point3d best = _vertices.Length > 0 ? _vertices[0] : point;
        double bestSquared = double.MaxValue;

        foreach (MeshFace face in _faces)
        {
            // Fanned from the first corner: a triangle runs this loop once, a quad twice.
            for (int corner = 2; corner < face.Count; corner++)
            {
                Point3d candidate = ClosestOnTriangle(
                    point, _vertices[face[0]], _vertices[face[corner - 1]], _vertices[face[corner]]);

                double squared = (candidate - point).LengthSquared;

                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Where a ray from a point in a direction first meets the mesh (`E2-T69`).
    /// </summary>
    /// <param name="point">Where the ray starts.</param>
    /// <param name="direction">Which way it travels. Need not be unit length.</param>
    /// <returns>
    /// The nearest hit in front of the start, or <see langword="null"/> when the ray misses.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="direction"/> has no length.</exception>
    /// <exception cref="InvalidOperationException">The mesh has no faces to hit.</exception>
    /// <remarks>
    /// <para>
    /// <b>The return type is <see cref="Point3d"/>? because a miss has no point, and saying so is
    /// the only honest option.</b> Returning the query point, or the nearest point on the mesh, or
    /// a sentinel far away, are each a wrong answer wearing the shape of a right one — a caller who
    /// forgets to check gets geometry rather than an error, and it looks plausible. This is the
    /// shape <see cref="Curve.PlaneOf(in Tolerance)"/> already uses for a question whose honest
    /// answer is sometimes <i>there isn't one</i>.
    /// </para>
    /// <para>
    /// <b>Behind the start is not a hit.</b> Projecting <i>along</i> a direction means forwards, so
    /// a negative ray parameter is rejected — and a caller who wants the line rather than the ray
    /// casts twice, once each way. The distinction matters most where it is least visible: a ray
    /// fired at a closed mesh from outside meets it twice, and taking the smaller <i>magnitude</i>
    /// rather than the smallest non-negative value lands on the far side or behind the caller, both
    /// of which are perfectly good-looking surface points.
    /// </para>
    /// <para>
    /// <b>There is one guard for that and not two.</b> The rejection happens where the hit is
    /// found, so every parameter reaching the comparison is already non-negative and the nearest is
    /// simply the smallest — writing that comparison over magnitudes instead would change nothing,
    /// which was checked by mutating it and watching every test stay green. The alternatives
    /// described above are what happens when the rejection is <i>absent</i>, not a second decision
    /// taken elsewhere.
    /// </para>
    /// <para>
    /// <b>A ray parallel to a triangle's plane is not a hit, even when it lies in that plane.</b> A
    /// grazing pass has no single meeting point, and returning one of infinitely many would be a
    /// coin flip the caller cannot see. The determinant of the Möller–Trumbore system is what
    /// detects it, and it is compared against zero rather than against a tolerance: an
    /// <i>almost</i> parallel ray does have a single answer, far away and correct, and rejecting it
    /// would be refusing a question that has one.
    /// </para>
    /// <para>
    /// <b>That check is for clarity rather than for correctness, and saying so is more useful than
    /// implying otherwise.</b> Removing it leaves every test green: a zero determinant makes the
    /// barycentric coordinates infinite or NaN, and both fail the range tests that follow, so the
    /// graze is already reported as a miss by IEEE arithmetic alone. It is kept because a reader
    /// should not have to reason about NaN comparisons to see why a parallel ray misses.
    /// </para>
    /// <para>
    /// <b>Möller–Trumbore, which is the ray-triangle test written as one 3×3 solve</b>: the two
    /// barycentric coordinates and the ray parameter come out together, and the triangle is missed
    /// when either coordinate leaves [0, 1] or their sum exceeds one. No plane equation is formed
    /// and no intersection point is computed for a triangle that is not hit.
    /// </para>
    /// <para>
    /// <b>Every face is visited, as in <see cref="ClosestPoint"/>, and the same note applies</b>: a
    /// bounding-volume hierarchy is the answer above a size that is measurable, and this is what
    /// the replacement will have to agree with.
    /// </para>
    /// </remarks>
    public Point3d? Project(in Point3d point, in Vector3d direction)
    {
        if (!direction.TryNormalise(out Vector3d along))
        {
            throw new ArgumentException(
                "A projection needs a direction with some length.", nameof(direction));
        }

        if (_faces.Length == 0)
        {
            throw new InvalidOperationException("A mesh with no faces has nothing for a ray to hit.");
        }

        double nearest = double.MaxValue;
        bool hit = false;

        foreach (MeshFace face in _faces)
        {
            for (int corner = 2; corner < face.Count; corner++)
            {
                if (TryHitTriangle(
                        point,
                        along,
                        _vertices[face[0]],
                        _vertices[face[corner - 1]],
                        _vertices[face[corner]],
                        out double travelled)
                    && travelled < nearest)
                {
                    nearest = travelled;
                    hit = true;
                }
            }
        }

        return hit ? point + (along * nearest) : null;
    }

    /// <summary>Möller–Trumbore: where a ray meets a triangle, if it does.</summary>
    /// <param name="from">Where the ray starts.</param>
    /// <param name="along">Which way it travels. Unit length.</param>
    /// <param name="a">The triangle's first corner.</param>
    /// <param name="b">Its second.</param>
    /// <param name="c">Its third.</param>
    /// <param name="travelled">How far along the ray the hit is.</param>
    /// <returns><see langword="false"/> when the ray misses, grazes, or hits behind its start.</returns>
    private static bool TryHitTriangle(
        in Point3d from,
        in Vector3d along,
        in Point3d a,
        in Point3d b,
        in Point3d c,
        out double travelled)
    {
        travelled = 0.0;

        Vector3d ab = b - a;
        Vector3d ac = c - a;
        Vector3d across = along.Cross(ac);
        double determinant = ab.Dot(across);

        if (determinant == 0.0 || !double.IsFinite(determinant))
        {
            // Parallel to the triangle's plane, the case of lying in it included.
            return false;
        }

        double inverse = 1.0 / determinant;
        Vector3d toStart = from - a;
        double u = toStart.Dot(across) * inverse;

        if (u < 0.0 || u > 1.0)
        {
            return false;
        }

        Vector3d other = toStart.Cross(ab);
        double v = along.Dot(other) * inverse;

        if (v < 0.0 || u + v > 1.0)
        {
            return false;
        }

        travelled = ac.Dot(other) * inverse;

        // BEHIND THE START IS NOT A HIT. Projecting along a direction means forwards, and the
        // smallest NON-NEGATIVE parameter is the answer rather than the smallest magnitude.
        return travelled >= 0.0;
    }

    /// <summary>The point on a triangle nearest a given point.</summary>
    /// <param name="point">The point to measure from.</param>
    /// <param name="a">The triangle's first corner.</param>
    /// <param name="b">Its second.</param>
    /// <param name="c">Its third.</param>
    /// <returns>The nearest point on the triangle, edges and corners included.</returns>
    /// <remarks>
    /// <para>
    /// <b>Ericson's construction: seven regions, tested in order, each ruled out by the signs of a
    /// few dot products.</b> The three vertex regions come first because they are the cheapest to
    /// decide, then the three edges, and what is left is over the face. No square roots and no
    /// division until the region is known, which is why this is worth writing out rather than
    /// solving as a small optimisation problem.
    /// </para>
    /// <para>
    /// <b>A degenerate triangle falls through to the face case and its division by a zero
    /// denominator</b> — which cannot happen here, because a triangle with no area has all three
    /// of its vertex or edge regions covering the plane, so one of the earlier tests always
    /// catches it first. That is a property of the ordering rather than luck, and it is the reason
    /// the ordering is not an implementation detail.
    /// </para>
    /// </remarks>
    private static Point3d ClosestOnTriangle(in Point3d point, in Point3d a, in Point3d b, in Point3d c)
    {
        Vector3d ab = b - a;
        Vector3d ac = c - a;
        Vector3d ap = point - a;

        double d1 = ab.Dot(ap);
        double d2 = ac.Dot(ap);

        if (d1 <= 0.0 && d2 <= 0.0)
        {
            return a;
        }

        Vector3d bp = point - b;
        double d3 = ab.Dot(bp);
        double d4 = ac.Dot(bp);

        if (d3 >= 0.0 && d4 <= d3)
        {
            return b;
        }

        double faceAB = (d1 * d4) - (d3 * d2);

        if (faceAB <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
        {
            return a + (ab * (d1 / (d1 - d3)));
        }

        Vector3d cp = point - c;
        double d5 = ab.Dot(cp);
        double d6 = ac.Dot(cp);

        if (d6 >= 0.0 && d5 <= d6)
        {
            return c;
        }

        double faceAC = (d5 * d2) - (d1 * d6);

        if (faceAC <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
        {
            return a + (ac * (d2 / (d2 - d6)));
        }

        double faceBC = (d3 * d6) - (d5 * d4);

        if (faceBC <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
        {
            return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        }

        double total = 1.0 / (faceAB + faceAC + faceBC);

        return a + (ab * (faceAC * total)) + (ac * (faceAB * total));
    }

    /// <summary>A copy of the per-vertex normals, or null when there are none.</summary>
    /// <returns>The normals, or null.</returns>
    public Vector3d[]? Normals() => _normals is null ? null : [.. _normals];

    /// <summary>A copy of the per-vertex texture coordinates, or null.</summary>
    /// <returns>The coordinates, or null.</returns>
    public UV[]? TextureCoordinates() => _textureCoordinates is null ? null : [.. _textureCoordinates];

    /// <summary>A copy of the per-vertex colours, packed <c>0xRRGGBBAA</c>, or null.</summary>
    /// <returns>The colours, or null.</returns>
    public uint[]? Colours() => _colours is null ? null : [.. _colours];

    /// <summary>The box containing every vertex.</summary>
    public BoundingBox BoundingBox
    {
        get
        {
            if (!_boundingBoxComputed)
            {
                BoundingBox box = BoundingBox.Empty;

                foreach (Point3d vertex in _vertices)
                {
                    box = box.Union(vertex);
                }

                _boundingBox = box;
                _boundingBoxComputed = true;
            }

            return _boundingBox;
        }
    }

    /// <summary>The total area of every face.</summary>
    /// <remarks>
    /// A quad is measured as its two triangles across the <c>A–C</c> diagonal, which is what makes
    /// this agree with the same mesh after <see cref="Triangulated"/> — a warped quad has no single
    /// area, and picking the same diagonal everywhere is what stops the answer depending on which
    /// form the mesh happens to be in.
    /// </remarks>
    public double Area
    {
        get
        {
            if (_area < 0.0)
            {
                double total = 0.0;

                foreach (MeshFace face in _faces)
                {
                    total += TriangleArea(face.A, face.B, face.C);

                    if (face.IsQuad)
                    {
                        total += TriangleArea(face.A, face.C, face.D);
                    }
                }

                _area = total;
            }

            return _area;
        }
    }

    /// <summary>
    /// The signed volume the mesh encloses, meaningful only when it is closed.
    /// </summary>
    /// <returns>The volume, positive when the faces are wound outwards.</returns>
    /// <remarks>
    /// <para>
    /// The divergence theorem, as a sum of signed tetrahedron volumes from the origin. It costs one
    /// pass and needs no adjacency, which is why it is not on <see cref="MeshTopology"/>.
    /// </para>
    /// <para>
    /// <b>It is signed, and the sign is the useful part.</b> A closed mesh wound inwards gives a
    /// negative volume, which is the cheapest reliable way to detect a mesh that will shade
    /// inside-out — and it is why the answer is not wrapped in an <see cref="Math.Abs(double)"/>.
    /// On a mesh that is not closed the number means nothing; ask
    /// <see cref="MeshTopology.IsClosed"/> first.
    /// </para>
    /// </remarks>
    public double Volume()
    {
        double total = 0.0;

        foreach (MeshFace face in _faces)
        {
            total += TetrahedronVolume(face.A, face.B, face.C);

            if (face.IsQuad)
            {
                total += TetrahedronVolume(face.A, face.C, face.D);
            }
        }

        return total;
    }

    /// <summary>
    /// The adjacency structure, built the first time it is asked for.
    /// </summary>
    /// <remarks>
    /// See the remarks on <see cref="Mesh"/> for why this is lazy. It is built once and kept, so
    /// the second caller pays nothing.
    /// </remarks>
    public MeshTopology Topology => _topology ??= new MeshTopology(this);

    /// <summary>The normal of one face, from its winding.</summary>
    /// <param name="index">The face index.</param>
    /// <returns>A unit vector, or the zero vector on a degenerate face.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the mesh.</exception>
    /// <remarks>
    /// <b>A quad's normal is Newell's, not the first triangle's.</b> A warped quad has no single
    /// plane, and taking the cross product of the first three corners gives a normal that flips
    /// when the vertices are listed from a different corner. Newell's method averages over the
    /// whole boundary and is invariant to where the winding starts, which is the property a
    /// renderer and a volume calculation both need.
    /// </remarks>
    public Vector3d FaceNormal(int index)
    {
        MeshFace face = Face(index);
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;

        for (int corner = 0; corner < face.Count; corner++)
        {
            Point3d current = _vertices[face[corner]];
            Point3d next = _vertices[face[(corner + 1) % face.Count]];

            x += (current.Y - next.Y) * (current.Z + next.Z);
            y += (current.Z - next.Z) * (current.X + next.X);
            z += (current.X - next.X) * (current.Y + next.Y);
        }

        Vector3d normal = new(x, y, z);

        return normal.TryNormalise(out Vector3d unit) ? unit : Vector3d.Zero;
    }

    /// <summary>The same mesh with every quad split into two triangles.</summary>
    /// <returns>A triangle-only mesh, or this one when it already is.</returns>
    /// <remarks>
    /// The per-vertex channels come across unchanged, because splitting a face adds no vertices —
    /// which is the reason to split on a diagonal rather than at a centroid.
    /// </remarks>
    public Mesh Triangulated()
    {
        if (QuadCount == 0)
        {
            return this;
        }

        List<MeshFace> triangles = new(_faces.Length + QuadCount);

        foreach (MeshFace face in _faces)
        {
            triangles.AddRange(face.Triangulated());
        }

        return new Mesh(_vertices, triangles, _normals, _textureCoordinates, _colours);
    }

    /// <summary>The same mesh moved by a transform.</summary>
    /// <param name="transform">The transform.</param>
    /// <returns>A new mesh.</returns>
    /// <remarks>
    /// <para>
    /// <b>Normals are transformed as directions and re-normalised, not as points.</b> Under a
    /// non-uniform scale a normal transformed like a position stops being perpendicular to the
    /// surface, which is the classic lighting bug; the exact answer is the inverse transpose, and
    /// where the transform is not invertible there is no correct normal to give, so the direction
    /// is carried across and the caller may recompute.
    /// </para>
    /// <para>
    /// <b>A transform that reverses handedness reverses every face's winding too.</b> Under a
    /// mirror — any transform whose <see cref="Transform.Determinant"/> is negative — moving the
    /// vertices alone leaves each face wound the other way round, so its geometric normal points
    /// into the solid while the stored vertex normals, which were carried across correctly, point
    /// out. The two then disagree: the mesh renders black or inside out, <see cref="FaceNormal"/>
    /// answers backwards, and <see cref="Volume"/> comes out negative. Reversing the corner order
    /// costs nothing and is the only way the two stay consistent.
    /// </para>
    /// </remarks>
    public Mesh TransformedBy(in Transform transform)
    {
        Point3d[] moved = new Point3d[_vertices.Length];

        for (int i = 0; i < _vertices.Length; i++)
        {
            moved[i] = transform.OfPoint(_vertices[i]);
        }

        Vector3d[]? normals = null;

        if (_normals is not null)
        {
            normals = new Vector3d[_normals.Length];

            // The inverse transpose, spelt out. `Transform.OfVector` uses only the upper 3x3, so
            // transposing that block is the whole of it — and where the transform is not
            // invertible there is no correct normal to give, so the direction is carried across
            // and the caller may recompute.
            Transform forNormals = transform.TryGetInverse(out Transform inverse)
                ? new Transform(
                    inverse.M00, inverse.M10, inverse.M20, 0.0,
                    inverse.M01, inverse.M11, inverse.M21, 0.0,
                    inverse.M02, inverse.M12, inverse.M22, 0.0,
                    0.0, 0.0, 0.0, 1.0)
                : transform;

            for (int i = 0; i < _normals.Length; i++)
            {
                normals[i] = forNormals.OfVector(_normals[i]).TryNormalise(out Vector3d unit)
                    ? unit
                    : _normals[i];
            }
        }

        MeshFace[] faces = _faces;

        if (transform.Determinant < 0.0)
        {
            faces = new MeshFace[_faces.Length];

            for (int i = 0; i < faces.Length; i++)
            {
                faces[i] = _faces[i].Reversed();
            }
        }

        return new Mesh(moved, faces, normals, _textureCoordinates, _colours);
    }

    /// <summary>
    /// Merges vertices that occupy the same place, so the mesh closes.
    /// </summary>
    /// <param name="tolerance">
    /// How far apart two vertices may be and still be the same one. Zero or unset uses a
    /// hundred-thousandth of the mesh's own size, which is the scale at which two copies of one
    /// point produced by two different faces differ.
    /// </param>
    /// <returns>The welded mesh, or this one when nothing merged.</returns>
    /// <remarks>
    /// <para>
    /// <b>A mesh of a solid is geometrically closed and topologically split, and this is what
    /// closes the topology.</b> Tessellating a BRep face by face — which is what every kernel does,
    /// ours and OpenCascade's alike — produces two copies of every vertex on a shared edge, one per
    /// face. Nothing leaks through the seam: the two copies are at the same place. But
    /// <see cref="MeshTopology.IsClosed"/> counts <i>edges</i>, and two coincident vertices are two
    /// edges, so a perfectly sound box reports twenty-four naked edges.
    /// </para>
    /// <para>
    /// <b>The split is deliberate and welding costs something real.</b> A vertex carries one
    /// normal, so a shared corner has one normal, so a welded box shades like a ball. That is why
    /// this is an operation and not what tessellation does: <b>ask for it when you need the
    /// topology</b> — a volume, an STL for a printer, a watertightness check — and not when you
    /// need the shading.
    /// </para>
    /// <para>
    /// <b>Normals, texture coordinates and colours are taken from the first vertex of each merged
    /// group</b> rather than averaged. Averaging two normals that disagree by ninety degrees
    /// produces a direction that is neither, and a caller who wants smooth shading should ask for
    /// <see cref="WithVertexNormals"/> afterwards, which computes them from the welded topology.
    /// </para>
    /// </remarks>
    public Mesh Welded(double tolerance = 0.0)
    {
        double epsilon = tolerance > 0.0 && double.IsFinite(tolerance)
            ? tolerance
            : Math.Max(BoundingBox.Diagonal.Length * 1e-5, 1e-12);

        double cell = epsilon * 2.0;
        Dictionary<(long X, long Y, long Z), int> lookup = new(_vertices.Length);
        int[] remap = new int[_vertices.Length];
        List<Point3d> kept = new(_vertices.Length);
        List<int> keptFrom = new(_vertices.Length);

        for (int i = 0; i < _vertices.Length; i++)
        {
            Point3d point = _vertices[i];
            (long X, long Y, long Z) key = (
                (long)Math.Round(point.X / cell),
                (long)Math.Round(point.Y / cell),
                (long)Math.Round(point.Z / cell));

            // A grid is not a metric: two points a hair apart can land in adjacent cells. The
            // twenty-seven neighbours are checked, which makes the merge symmetric — otherwise
            // whether two vertices weld would depend on which side of a cell boundary they fell,
            // and the same mesh translated by half a cell would weld differently.
            int found = -1;

            for (long dx = -1; dx <= 1 && found < 0; dx++)
            {
                for (long dy = -1; dy <= 1 && found < 0; dy++)
                {
                    for (long dz = -1; dz <= 1 && found < 0; dz++)
                    {
                        if (lookup.TryGetValue((key.X + dx, key.Y + dy, key.Z + dz), out int candidate)
                            && kept[candidate].DistanceTo(point) <= epsilon)
                        {
                            found = candidate;
                        }
                    }
                }
            }

            if (found >= 0)
            {
                remap[i] = found;
                continue;
            }

            int index = kept.Count;
            kept.Add(point);
            keptFrom.Add(i);
            lookup[key] = index;
            remap[i] = index;
        }

        if (kept.Count == _vertices.Length)
        {
            return this;
        }

        MeshFace[] faces = new MeshFace[_faces.Length];

        for (int i = 0; i < _faces.Length; i++)
        {
            MeshFace face = _faces[i];

            faces[i] = face.IsQuad
                ? new MeshFace(remap[face.A], remap[face.B], remap[face.C], remap[face.D])
                : new MeshFace(remap[face.A], remap[face.B], remap[face.C]);
        }

        Vector3d[]? normals = null;
        UV[]? textureCoordinates = null;
        uint[]? colours = null;

        if (_normals is not null)
        {
            normals = new Vector3d[kept.Count];

            for (int i = 0; i < kept.Count; i++)
            {
                normals[i] = _normals[keptFrom[i]];
            }
        }

        if (_textureCoordinates is not null)
        {
            textureCoordinates = new UV[kept.Count];

            for (int i = 0; i < kept.Count; i++)
            {
                textureCoordinates[i] = _textureCoordinates[keptFrom[i]];
            }
        }

        if (_colours is not null)
        {
            colours = new uint[kept.Count];

            for (int i = 0; i < kept.Count; i++)
            {
                colours[i] = _colours[keptFrom[i]];
            }
        }

        return new Mesh(kept, faces, normals, textureCoordinates, colours);
    }

    /// <summary>
    /// The same mesh with a normal per vertex, averaged from the faces around it.
    /// </summary>
    /// <returns>A mesh with normals, or this one when it already has them.</returns>
    /// <remarks>
    /// <b>Area-weighted, because the alternative is worse in the case it matters.</b> Averaging
    /// face normals equally makes a vertex where one huge face meets three slivers point almost
    /// entirely at the slivers. Weighting by area is one multiplication and gives the answer a
    /// renderer expects — and it falls out for free, because an unnormalised Newell normal already
    /// has twice the face's area as its length.
    /// </remarks>
    public Mesh WithVertexNormals()
    {
        if (_normals is not null)
        {
            return this;
        }

        Vector3d[] normals = new Vector3d[_vertices.Length];

        for (int index = 0; index < _faces.Length; index++)
        {
            MeshFace face = _faces[index];
            Vector3d weighted = FaceNormal(index) * FaceArea(face);

            for (int corner = 0; corner < face.Count; corner++)
            {
                normals[face[corner]] += weighted;
            }
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].TryNormalise(out Vector3d unit) ? unit : Vector3d.ZAxis;
        }

        return new Mesh(_vertices, _faces, normals, _textureCoordinates, _colours);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"Mesh({_vertices.Length} vertices, {_faces.Length} faces, {QuadCount} quads)");

    /// <summary>The area of one face, for the normal weighting.</summary>
    private double FaceArea(in MeshFace face) =>
        TriangleArea(face.A, face.B, face.C) + (face.IsQuad ? TriangleArea(face.A, face.C, face.D) : 0.0);

    private double TriangleArea(int a, int b, int c) =>
        (_vertices[b] - _vertices[a]).Cross(_vertices[c] - _vertices[a]).Length * 0.5;

    private double TetrahedronVolume(int a, int b, int c) =>
        ((Vector3d)_vertices[a]).TripleProduct((Vector3d)_vertices[b], (Vector3d)_vertices[c]) / 6.0;

    private T[]? Channel<T>(IReadOnlyList<T>? values, string name)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count != _vertices.Length)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A per-vertex channel needs one entry per vertex: {values.Count} given, {_vertices.Length} wanted."),
                name);
        }

        return [.. values];
    }

    private static int Check(int index, int count, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count, name);

        return index;
    }
}

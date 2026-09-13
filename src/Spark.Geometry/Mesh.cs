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

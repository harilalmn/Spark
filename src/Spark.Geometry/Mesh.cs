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
    /// <b>Normals are transformed as directions and re-normalised, not as points.</b> Under a
    /// non-uniform scale a normal transformed like a position stops being perpendicular to the
    /// surface, which is the classic lighting bug; the exact answer is the inverse transpose, and
    /// where the transform is not invertible there is no correct normal to give, so the direction
    /// is carried across and the caller may recompute.
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

        return new Mesh(moved, _faces, normals, _textureCoordinates, _colours);
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

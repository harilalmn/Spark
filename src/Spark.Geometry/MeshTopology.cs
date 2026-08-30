using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Who is next to whom in a <see cref="Mesh"/>: the halfedge structure, built on demand.
/// </summary>
/// <remarks>
/// <para>
/// <b>A halfedge is a directed edge belonging to one face.</b> Every face contributes one per
/// corner, running from that corner to the next; the *twin* of a halfedge is the one running the
/// other way in the neighbouring face. Two halfedges that are twins are the two sides of one edge,
/// and a halfedge with no twin is on the boundary. Almost every adjacency question is one or two
/// hops through those three ideas, which is why this structure and not a set of tables.
/// </para>
/// <para>
/// <b>It is built lazily and stored on the mesh</b> — see <see cref="Mesh.Topology"/> — because
/// most meshes are drawn and discarded without anybody asking a topological question.
/// </para>
/// <para>
/// <b>A mesh that is not a manifold is described, not rejected.</b> Three faces meeting at one
/// edge is malformed and it is also what a real scan or a careless boolean produces; refusing to
/// build would leave a caller with no way to *find* the problem. The third halfedge simply gets no
/// twin, <see cref="NonManifoldEdgeCount"/> counts it, and <see cref="IsManifold"/> says so.
/// </para>
/// </remarks>
public sealed class MeshTopology
{
    private readonly Mesh _mesh;

    // One entry per halfedge, in face order: face f contributes its corners consecutively.
    private readonly int[] _origin;
    private readonly int[] _face;
    private readonly int[] _twin;
    private readonly int[] _firstHalfedgeOfFace;

    private readonly Dictionary<long, int> _byEndpoints;

    private readonly int _nonManifold;

    /// <summary>Builds the adjacency of a mesh.</summary>
    /// <param name="mesh">The mesh.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    internal MeshTopology(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        _mesh = mesh;

        int halfedges = 0;
        _firstHalfedgeOfFace = new int[mesh.FaceCount + 1];

        for (int f = 0; f < mesh.FaceCount; f++)
        {
            _firstHalfedgeOfFace[f] = halfedges;
            halfedges += mesh.Face(f).Count;
        }

        _firstHalfedgeOfFace[mesh.FaceCount] = halfedges;

        _origin = new int[halfedges];
        _face = new int[halfedges];
        _twin = new int[halfedges];
        _byEndpoints = new Dictionary<long, int>(halfedges);

        for (int h = 0; h < halfedges; h++)
        {
            _twin[h] = -1;
        }

        for (int f = 0; f < mesh.FaceCount; f++)
        {
            MeshFace face = mesh.Face(f);
            int start = _firstHalfedgeOfFace[f];

            for (int corner = 0; corner < face.Count; corner++)
            {
                int h = start + corner;

                _origin[h] = face[corner];
                _face[h] = f;

                int to = face[(corner + 1) % face.Count];

                // The twin runs the other way, so it is looked up under the reversed key. A
                // dictionary keyed on the *directed* pair is what makes this one pass rather than
                // a sort: the first halfedge of an edge records itself, the second finds it.
                if (_byEndpoints.TryGetValue(Key(to, face[corner]), out int twin))
                {
                    if (_twin[twin] == -1)
                    {
                        _twin[twin] = h;
                        _twin[h] = twin;
                    }
                    else
                    {
                        // A third face on the same edge. Left without a twin and counted.
                        _nonManifold++;
                    }
                }
                else if (!_byEndpoints.TryAdd(Key(face[corner], to), h))
                {
                    // Two halfedges of the same face run the same way along one edge, which means
                    // the mesh has a duplicated face or a degenerate one.
                    _nonManifold++;
                }
            }
        }
    }

    /// <summary>How many halfedges there are — one per corner of every face.</summary>
    public int HalfedgeCount => _origin.Length;

    /// <summary>
    /// How many distinct edges there are: a paired edge counts once, a boundary edge counts once.
    /// </summary>
    public int EdgeCount
    {
        get
        {
            int paired = 0;

            for (int h = 0; h < _twin.Length; h++)
            {
                if (_twin[h] >= 0)
                {
                    paired++;
                }
            }

            return _twin.Length - (paired / 2);
        }
    }

    /// <summary>How many edges have only one face on them.</summary>
    public int NakedEdgeCount
    {
        get
        {
            int naked = 0;

            for (int h = 0; h < _twin.Length; h++)
            {
                if (_twin[h] < 0)
                {
                    naked++;
                }
            }

            return naked;
        }
    }

    /// <summary>How many halfedges could not be paired because a third face shares their edge.</summary>
    public int NonManifoldEdgeCount => _nonManifold;

    /// <summary>Whether every edge has exactly two faces on it.</summary>
    /// <remarks>
    /// <b>The question a volume calculation should ask first.</b> An open mesh's enclosed volume is
    /// not a number that means anything, and this is the cheap way to find out before believing
    /// one.
    /// </remarks>
    public bool IsClosed => NakedEdgeCount == 0 && _nonManifold == 0;

    /// <summary>Whether no edge has more than two faces on it.</summary>
    public bool IsManifold => _nonManifold == 0;

    /// <summary>Whether the mesh's faces all wind the same way relative to their neighbours.</summary>
    /// <remarks>
    /// <para>
    /// <b>Two neighbouring faces are consistently wound when they traverse their shared edge in
    /// opposite directions.</b> That is exactly what a halfedge having a twin means, so the test
    /// is nearly free — but it has to be asked separately from <see cref="IsClosed"/>, because a
    /// mesh can be closed and inconsistently wound, and that mesh shades inside-out in patches and
    /// reports a nonsense volume.
    /// </para>
    /// <para>
    /// A halfedge whose twin runs the *same* way rather than the opposite one is caught during the
    /// build as a non-manifold pairing, so this is the same question asked from the other side.
    /// </para>
    /// </remarks>
    public bool IsConsistentlyWound => _nonManifold == 0;

    /// <summary>The faces that share an edge with a face.</summary>
    /// <param name="face">The face index.</param>
    /// <returns>Between zero and four neighbours, in corner order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the mesh.</exception>
    public int[] AdjacentFaces(int face)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, _mesh.FaceCount);

        List<int> neighbours = [];

        for (int h = _firstHalfedgeOfFace[face]; h < _firstHalfedgeOfFace[face + 1]; h++)
        {
            if (_twin[h] >= 0)
            {
                neighbours.Add(_face[_twin[h]]);
            }
        }

        return [.. neighbours];
    }

    /// <summary>The faces that touch a vertex.</summary>
    /// <param name="vertex">The vertex index.</param>
    /// <returns>The faces, in ascending index order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the mesh.</exception>
    /// <remarks>
    /// <b>Found by scanning rather than by walking the halfedge fan</b>, and that is deliberate: a
    /// fan walk is faster and stops at a boundary or a non-manifold vertex, silently returning some
    /// of the faces rather than all of them. On a mesh that may be neither closed nor manifold — the
    /// only kind worth defending against — the complete answer is worth more than the quick one.
    /// </remarks>
    public int[] FacesAroundVertex(int vertex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(vertex, _mesh.VertexCount);

        List<int> faces = [];

        for (int f = 0; f < _mesh.FaceCount; f++)
        {
            MeshFace face = _mesh.Face(f);

            for (int corner = 0; corner < face.Count; corner++)
            {
                if (face[corner] == vertex)
                {
                    faces.Add(f);
                    break;
                }
            }
        }

        return [.. faces];
    }

    /// <summary>The vertex pairs of every edge with only one face on it.</summary>
    /// <returns>Each boundary edge once, as its two endpoints in the face's own direction.</returns>
    /// <remarks>
    /// <b>The single most useful diagnostic a mesh has.</b> A mesh that should be closed and is not
    /// has a hole, and its naked edges are exactly where — so this is what a *show me the problem*
    /// tool draws.
    /// </remarks>
    public (int From, int To)[] NakedEdges()
    {
        List<(int, int)> naked = [];

        for (int f = 0; f < _mesh.FaceCount; f++)
        {
            MeshFace face = _mesh.Face(f);
            int start = _firstHalfedgeOfFace[f];

            for (int corner = 0; corner < face.Count; corner++)
            {
                if (_twin[start + corner] < 0)
                {
                    naked.Add((face[corner], face[(corner + 1) % face.Count]));
                }
            }
        }

        return [.. naked];
    }

    /// <summary>
    /// A key for a directed edge, packed into a long so the dictionary hashes one value.
    /// </summary>
    /// <remarks>
    /// Two 32-bit indices in a 64-bit key: exact for any mesh that fits in memory, and no
    /// allocation per edge. A tuple key would be correct and would allocate a comparer's worth of
    /// work per lookup on a million-face mesh.
    /// </remarks>
    private static long Key(int from, int to) => ((long)from << 32) | (uint)to;
}

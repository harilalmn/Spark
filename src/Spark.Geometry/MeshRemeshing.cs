using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// Rebuilds a mesh's triangles at a uniform edge length, keeping its shape (<c>E2-T68</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Remeshing is not reducing, and the two answer different complaints.</b>
/// <see cref="MeshDecimation"/> is asked *make this smaller*, and it removes the triangles that
/// cost least to remove — leaving the rest exactly where they were, at whatever sizes they already
/// had. Remeshing is asked *make these triangles regular*: it will happily add triangles to a
/// coarse region while removing them from a fine one, and the count it lands on is whatever a
/// uniform edge length implies. **A mesh out of a boolean or an importer is the case for it**:
/// slivers a hundred times longer than they are wide, next to faces nobody subdivided, all of
/// which draw badly, simulate worse and offset unpredictably.
/// </para>
/// <para>
/// <b>The Botsch–Kobbelt loop, which is four passes over the same data, repeated.</b> Split every
/// edge that is too long; collapse every edge that is too short; flip edges towards vertex degree
/// six; move each vertex towards the average of its neighbours. Nothing in it is clever on its
/// own — the result comes from the four correcting each other's damage over a handful of
/// iterations, which is why it is a loop rather than a pipeline.
/// </para>
/// <para>
/// <b>The four-thirds and four-fifths are the paper's and they are not arbitrary.</b> Splitting at
/// exactly the target and collapsing at exactly the target makes the two passes fight: a split
/// produces two half-length edges, which the collapse pass then removes, which the split pass then
/// re-adds. A dead band either side gives the loop somewhere to settle, and 4/3 and 4/5 are the
/// widest band that still converges towards the target rather than towards the band's edges.
/// </para>
/// <para>
/// <b>Boundaries are left exactly where they are, and that is a limitation as well as a
/// promise.</b> A vertex on a naked edge is never moved and its edges are never collapsed, because
/// an open mesh's outline is usually the thing the caller cares most about — remeshing a panel must
/// not shrink it away from its own border, and <see cref="Mesh.Smoothed"/> makes the same choice for
/// the same reason. **The cost is that the border keeps whatever edge lengths it arrived with**: a
/// sheet whose outline is chopped into very short segments comes back uniform inside and ragged
/// around the edge. Collapsing along a straight run of boundary would be safe and is not done here,
/// because telling a straight run from a corner needs a tolerance this member does not take.
/// </para>
/// </remarks>
public static class MeshRemeshing
{
    /// <summary>How many times the four passes run.</summary>
    /// <remarks>
    /// Five is the paper's suggestion and matches what the tests measure: the edge-length spread
    /// stops improving materially after about four on every fixture tried. It is a constant rather
    /// than a parameter because a caller has no way to choose it well, and the member's job is to
    /// produce a uniform mesh rather than to expose a knob.
    /// </remarks>
    private const int Iterations = 5;

    /// <summary>
    /// Rebuilds a mesh so that its edges are all about <paramref name="targetEdgeLength"/> long.
    /// </summary>
    /// <param name="mesh">The mesh to remesh. Quads are triangulated first.</param>
    /// <param name="targetEdgeLength">The edge length to aim for. Must be finite and positive.</param>
    /// <returns>The remeshed mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="targetEdgeLength"/> is not finite and positive.
    /// </exception>
    /// <remarks>
    /// <b>The length is an aim, like <see cref="MeshDecimation.Reduced"/>'s count.</b> Curvature,
    /// boundaries and the refusals below all stop individual edges reaching it, and a caller who
    /// needs to know measures the result. **What is promised is that the spread narrows**, not that
    /// every edge lands on the number.
    /// </remarks>
    public static Mesh Remeshed(Mesh mesh, double targetEdgeLength)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (!double.IsFinite(targetEdgeLength) || targetEdgeLength <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetEdgeLength),
                targetEdgeLength,
                "A target edge length must be finite and greater than zero. Remeshing to zero is "
                + "asking for infinitely many triangles.");
        }

        double tooLong = targetEdgeLength * 4.0 / 3.0;
        double tooShort = targetEdgeLength * 4.0 / 5.0;

        Working working = Working.From(mesh.Triangulated());

        for (int pass = 0; pass < Iterations; pass++)
        {
            working.SplitLongEdges(tooLong);
            working.CollapseShortEdges(tooShort, tooLong);
            working.FlipTowardsSixNeighbours();
            working.RelaxTangentially();
        }

        return working.ToMesh();
    }


    /// <summary>
    /// The mesh being rebuilt: vertices, triangles, and the boundary flags the passes consult.
    /// </summary>
    /// <remarks>
    /// <b>A plain vertex-and-face pair rather than a halfedge structure</b>, rebuilt between
    /// iterations. A halfedge mesh is the right answer for a remesher that has to run inside a
    /// frame; this one runs when a user presses a button on an imported file, and the arrays keep
    /// the four passes readable — each is a loop anybody can check against the paper.
    /// </remarks>
    private sealed class Working
    {
        private readonly List<Point3d> _vertices;
        private readonly List<MeshFace> _faces;

        private Working(List<Point3d> vertices, List<MeshFace> faces)
        {
            _vertices = vertices;
            _faces = faces;
        }

        internal static Working From(Mesh mesh) => new([.. mesh.Vertices()], [.. mesh.Faces()]);

        internal Mesh ToMesh()
        {
            List<MeshFace> live = [];

            foreach (MeshFace face in _faces)
            {
                if (face.A != face.B && face.B != face.C && face.C != face.A)
                {
                    live.Add(face);
                }
            }

            return Compacted(_vertices, live);
        }

        /// <summary>Puts a new vertex at the midpoint of every edge that is too long.</summary>
        /// <remarks>
        /// <para>
        /// <b>EDGE-DRIVEN, NOT FACE-DRIVEN, AND THE DIFFERENCE IS A TORN MESH.</b> The first
        /// version of this pass walked the faces, split each one at its own longest edge, and
        /// reasoned that the neighbour across that edge would reach the same conclusion on its own
        /// turn. It does not: the shared edge is only the neighbour's *longest* edge if the
        /// neighbour has no longer one. Where it does, one side of the edge gains a midpoint and
        /// the other does not, which is a T-junction — a vertex sitting in the middle of another
        /// triangle's edge, joined to nothing. A closed sphere came back with **283 naked edges**.
        /// </para>
        /// <para>
        /// <b>So every long edge is chosen first and every face is then rebuilt around whichever of
        /// its three edges were chosen.</b> Zero, one, two or three, with a template for each, and
        /// both faces on an edge see the same midpoint because they read it from the same
        /// dictionary. Conforming by construction rather than by argument.
        /// </para>
        /// <para>
        /// <b>One split per edge per pass.</b> An edge twenty times the target needs five splits
        /// and gets one here; the loop supplies the rest over its iterations. Splitting to
        /// convergence inside this pass would build the whole subdivision before a single collapse
        /// or flip could improve it.
        /// </para>
        /// </remarks>
        internal void SplitLongEdges(double tooLong)
        {
            double limit = tooLong * tooLong;
            Dictionary<(int, int), int> midpoints = [];

            foreach (MeshFace face in _faces)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = face[corner];
                    int b = face[(corner + 1) % 3];

                    if ((_vertices[b] - _vertices[a]).LengthSquared > limit)
                    {
                        Midpoint(midpoints, a, b);
                    }
                }
            }

            if (midpoints.Count == 0)
            {
                return;
            }

            List<MeshFace> rebuilt = [];

            foreach (MeshFace face in _faces)
            {
                // Rotated so that the split edges are in a known place: `count` of them, starting
                // at the first corner. Three templates instead of nine.
                for (int rotation = 0; rotation < 3; rotation++)
                {
                    int a = face[rotation];
                    int b = face[(rotation + 1) % 3];
                    int c = face[(rotation + 2) % 3];

                    int ab = Existing(midpoints, a, b);
                    int bc = Existing(midpoints, b, c);
                    int ca = Existing(midpoints, c, a);

                    if (ab >= 0 && bc >= 0 && ca >= 0)
                    {
                        rebuilt.Add(new MeshFace(a, ab, ca));
                        rebuilt.Add(new MeshFace(ab, b, bc));
                        rebuilt.Add(new MeshFace(ca, bc, c));
                        rebuilt.Add(new MeshFace(ab, bc, ca));
                        break;
                    }

                    if (ab >= 0 && bc >= 0)
                    {
                        rebuilt.Add(new MeshFace(ab, b, bc));
                        rebuilt.Add(new MeshFace(a, ab, bc));
                        rebuilt.Add(new MeshFace(a, bc, c));
                        break;
                    }

                    if (ab >= 0 && bc < 0 && ca < 0)
                    {
                        rebuilt.Add(new MeshFace(a, ab, c));
                        rebuilt.Add(new MeshFace(ab, b, c));
                        break;
                    }

                    if (rotation == 2)
                    {
                        rebuilt.Add(face);
                    }
                }
            }

            _faces.Clear();
            _faces.AddRange(rebuilt);
        }

        private static int Existing(Dictionary<(int, int), int> midpoints, int a, int b) =>
            midpoints.TryGetValue(a < b ? (a, b) : (b, a), out int index) ? index : -1;

        /// <summary>Collapses every edge that is too short, when doing so is safe.</summary>
        /// <remarks>
        /// <b>Three refusals, and one special case.</b> **An edge between two boundary vertices is
        /// left alone**, because collapsing it would move the outline. **An edge with one endpoint
        /// on the boundary collapses onto that endpoint**, which removes the interior vertex and
        /// leaves the border untouched to the bit — freezing those too was the difference between
        /// *the outline never moves* and *nothing near the outline ever changes*, and it left a
        /// ring of stubs around every finely chopped border. A collapse that would make some *other* edge too long is refused, which is the
        /// pass's own damage: pulling a vertex across a short edge lengthens everything else
        /// attached to it, and without this the collapse and split passes trade the same triangles
        /// back and forth forever. And a collapse that would fold a triangle over is refused, as in
        /// <see cref="MeshDecimation"/>.
        /// </remarks>
        internal void CollapseShortEdges(double tooShort, double tooLong)
        {
            double lower = tooShort * tooShort;
            double upper = tooLong * tooLong;
            bool[] boundary = BoundaryVertices();
            int[] merged = new int[_vertices.Count];

            for (int i = 0; i < merged.Length; i++)
            {
                merged[i] = i;
            }

            foreach (MeshFace face in _faces)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = Root(merged, face[corner]);
                    int b = Root(merged, face[(corner + 1) % 3]);

                    if (a == b || (boundary[a] && boundary[b]))
                    {
                        continue;
                    }

                    if ((_vertices[b] - _vertices[a]).LengthSquared >= lower)
                    {
                        continue;
                    }

                    // AN EDGE WITH ONE ENDPOINT ON THE BORDER COLLAPSES ONTO THE BORDER, and the
                    // survivor does not move. This is the difference between "the outline never
                    // moves" and "nothing near the outline ever changes", and freezing both was
                    // costing far more than the rule requires: a sheet whose border is chopped at
                    // a twentieth of the target keeps a ring of stubs that length, because every
                    // edge reaching in from it has a boundary endpoint. Removing the INTERIOR
                    // vertex leaves the border exactly where it was, to the bit.
                    int survivor = boundary[b] ? b : a;
                    int removed = survivor == b ? a : b;

                    Point3d target = boundary[a] || boundary[b]
                        ? _vertices[survivor]
                        : new Point3d(
                            (_vertices[a].X + _vertices[b].X) / 2.0,
                            (_vertices[a].Y + _vertices[b].Y) / 2.0,
                            (_vertices[a].Z + _vertices[b].Z) / 2.0);

                    if (!IsCollapseSafe(merged, removed, survivor, target, upper))
                    {
                        continue;
                    }

                    _vertices[survivor] = target;
                    merged[removed] = survivor;
                }
            }

            Remap(merged);
        }

        /// <summary>
        /// Swaps the shared edge of two triangles when it brings their four vertices nearer to six
        /// neighbours each.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Six is the degree of a vertex in a regular triangulation of the plane</b>, and the
        /// deviation from it is the cheapest usable measure of how irregular a mesh is. A flip
        /// moves one degree off each of the two vertices on the edge and one onto each of the two
        /// across from it, so the scoring is four integers before against four after.
        /// </para>
        /// <para>
        /// <b>THIS PASS TORE A CLOSED SPHERE OPEN, TWICE OVER, AND BOTH CAUSES ARE GUARDED HERE.</b>
        /// The probe that found it is worth restating: split and collapse both left the sphere at
        /// zero naked and zero non-manifold edges, and the flip pass turned that into 24 and 12 in
        /// one go.
        /// </para>
        /// <para>
        /// <b>The first cause: a flip whose new edge already exists.</b> If <c>c</c> and <c>d</c>
        /// are already joined — which happens constantly on a coarse or a cone-like region — then
        /// creating <c>c-d</c> again gives that edge three or four faces. The mesh is non-manifold
        /// immediately and no later pass repairs it.
        /// </para>
        /// <para>
        /// <b>The second: face indices that went stale inside the loop.</b> The edge map is built
        /// once and holds face indices; the moment one flip rewrites <c>_faces[first]</c>, every
        /// other edge whose entry points at that face is describing a triangle that no longer has
        /// those corners. Reading it produces a flip computed from one triangle and applied to
        /// another. **Each face may be flipped at most once per pass** — which also bounds the
        /// pass, and costs nothing, because the loop runs five times.
        /// </para>
        /// </remarks>
        internal void FlipTowardsSixNeighbours()
        {
            Dictionary<(int, int), List<int>> edges = EdgeFaces();
            bool[] boundary = BoundaryVertices();
            int[] valence = Valences();
            bool[] touched = new bool[_faces.Count];
            HashSet<(int, int)> present = [.. edges.Keys];

            foreach (KeyValuePair<(int, int), List<int>> edge in edges)
            {
                if (edge.Value.Count != 2)
                {
                    continue;
                }

                (int a, int b) = edge.Key;

                if (boundary[a] || boundary[b])
                {
                    continue;
                }

                // WHICH OF THE TWO FACES TRAVERSES a -> b DECIDES THE WINDING OF BOTH NEW ONES,
                // and the edge key is sorted, so it says nothing about direction. Taking
                // edge.Value[0] as the forward face is right half the time, and the other half
                // emits two triangles wound against their neighbours - which is a hole and a
                // non-manifold edge, not a visibly wrong flip. This is the third distinct cause of
                // the torn sphere and the one that survived the first two fixes.
                int first = HasDirected(_faces[edge.Value[0]], a, b) ? edge.Value[0] : edge.Value[1];
                int second = first == edge.Value[0] ? edge.Value[1] : edge.Value[0];

                if (touched[first] || touched[second] || !HasDirected(_faces[first], a, b) || !HasDirected(_faces[second], b, a))
                {
                    continue;
                }

                int c = Opposite(_faces[first], a, b);
                int d = Opposite(_faces[second], a, b);

                if (c < 0 || d < 0 || c == d)
                {
                    continue;
                }

                if (present.Contains(c < d ? (c, d) : (d, c)))
                {
                    continue;
                }

                int before = Deviation(valence[a]) + Deviation(valence[b]) + Deviation(valence[c]) + Deviation(valence[d]);
                int after = Deviation(valence[a] - 1) + Deviation(valence[b] - 1) + Deviation(valence[c] + 1) + Deviation(valence[d] + 1);

                if (after >= before)
                {
                    continue;
                }

                // A FLIP THAT TURNS EITHER TRIANGLE OVER IS REFUSED. The valence test is
                // combinatorial and knows nothing about where the four points are; on a fold or a
                // sliver the flipped pair can be inside out while scoring better.
                if (Turns(a, b, c, d) || Turns(b, a, d, c))
                {
                    continue;
                }

                _faces[first] = new MeshFace(c, a, d);
                _faces[second] = new MeshFace(d, b, c);
                touched[first] = true;
                touched[second] = true;
                present.Add(c < d ? (c, d) : (d, c));
                present.Remove(edge.Key);

                valence[a]--;
                valence[b]--;
                valence[c]++;
                valence[d]++;
            }
        }

        private static int Deviation(int valence) => Math.Abs(valence - 6);

        /// <summary>The squared length of a triangle's longest edge.</summary>
        private static double Longest(in Point3d a, in Point3d b, in Point3d c) =>
            Math.Max(
                (b - a).LengthSquared,
                Math.Max((c - b).LengthSquared, (a - c).LengthSquared));

        private static int Root(int[] merged, int vertex)
        {
            while (merged[vertex] != vertex)
            {
                vertex = merged[vertex];
            }

            return vertex;
        }

        /// <summary>Whether the face traverses the edge in the direction given.</summary>
        private static bool HasDirected(in MeshFace face, int a, int b)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                if (face[corner] == a && face[(corner + 1) % 3] == b)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Opposite(in MeshFace face, int a, int b)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = face[corner];

                if (vertex != a && vertex != b)
                {
                    return vertex;
                }
            }

            return -1;
        }

        private static Mesh Compacted(List<Point3d> vertices, List<MeshFace> faces)
        {
            Dictionary<int, int> renumbered = [];
            List<Point3d> kept = [];

            int Number(int vertex)
            {
                if (!renumbered.TryGetValue(vertex, out int index))
                {
                    index = kept.Count;
                    renumbered[vertex] = index;
                    kept.Add(vertices[vertex]);
                }

                return index;
            }

            List<MeshFace> renumberedFaces = [];

            foreach (MeshFace face in faces)
            {
                renumberedFaces.Add(new MeshFace(Number(face.A), Number(face.B), Number(face.C)));
            }

            return renumberedFaces.Count > 0
                ? new Mesh(kept, renumberedFaces)
                : throw new InvalidOperationException(
                    "Remeshing removed every triangle, which should be impossible: a collapse that "
                    + "would empty the mesh is refused. "
                    + faces.Count.ToString(CultureInfo.InvariantCulture)
                    + " faces were present.");
        }

        /// <summary>
        /// Moves each interior vertex towards the average of its neighbours, <b>along the surface
        /// only</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>TANGENTIAL, AND THE WORD IS THE WHOLE POINT.</b> Plain Laplacian smoothing — which is
        /// what <see cref="Mesh.Smoothed"/> does, correctly, for its own purpose — moves each vertex
        /// towards its neighbours' centroid, and on a closed convex surface every one of those
        /// centroids lies *inside* the surface. Five rounds of it deflate a sphere:
        /// <b>this loop returned 75% of the volume it was given</b> until the displacement was
        /// projected onto the tangent plane.
        /// </para>
        /// <para>
        /// <b>Projecting keeps the vertex on the surface and still evens out the triangles</b>,
        /// which is the only thing relaxation is here for — the shape is the split and collapse
        /// passes' business, and it is not relaxation's job to change it. The normal is the
        /// area-weighted average of the incident face normals, which is the standard estimate and
        /// costs one cross product per face.
        /// </para>
        /// <para>
        /// <b>Every vertex reads the previous positions, not its own partial results</b>, so the
        /// answer does not depend on the order the vertices are stored in.
        /// </para>
        /// </remarks>
        internal void RelaxTangentially()
        {
            bool[] boundary = BoundaryVertices();
            Point3d[] sums = new Point3d[_vertices.Count];
            int[] counts = new int[_vertices.Count];
            Vector3d[] normals = new Vector3d[_vertices.Count];

            foreach (MeshFace face in _faces)
            {
                if (face.A == face.B || face.B == face.C || face.C == face.A)
                {
                    continue;
                }

                // Not normalised: the cross product's length is twice the triangle's area, so
                // summing them area-weights the vertex normal for free.
                Vector3d normal = (_vertices[face.B] - _vertices[face.A]).Cross(_vertices[face.C] - _vertices[face.A]);

                for (int corner = 0; corner < 3; corner++)
                {
                    int here = face[corner];
                    int next = face[(corner + 1) % 3];

                    normals[here] += normal;
                    sums[here] += _vertices[next] - Point3d.Origin;
                    counts[here]++;
                }
            }

            Point3d[] moved = new Point3d[_vertices.Count];

            for (int v = 0; v < _vertices.Count; v++)
            {
                moved[v] = _vertices[v];

                if (boundary[v] || counts[v] == 0)
                {
                    continue;
                }

                Point3d centroid = Point3d.Origin + ((sums[v] - Point3d.Origin) / counts[v]);
                Vector3d towards = centroid - _vertices[v];

                if (normals[v].TryNormalise(out Vector3d unit))
                {
                    towards -= unit * towards.Dot(unit);
                }

                moved[v] = _vertices[v] + towards;
            }

            for (int v = 0; v < _vertices.Count; v++)
            {
                _vertices[v] = moved[v];
            }
        }

        /// <summary>Whether flipping edge <c>a-b</c> to <c>c-d</c> turns triangle <c>c a d</c> over.</summary>
        private bool Turns(int a, int b, int c, int d)
        {
            Vector3d before = (_vertices[b] - _vertices[a]).Cross(_vertices[c] - _vertices[a]);
            Vector3d after = (_vertices[a] - _vertices[c]).Cross(_vertices[d] - _vertices[c]);

            return before.LengthSquared <= 0.0 || after.LengthSquared <= 0.0 || before.Dot(after) <= 0.0;
        }

        private bool IsCollapseSafe(int[] merged, int from, int to, in Point3d target, double upper)
        {
            foreach (MeshFace face in _faces)
            {
                int a = Root(merged, face.A);
                int b = Root(merged, face.B);
                int c = Root(merged, face.C);

                if (a == b || b == c || c == a)
                {
                    continue;
                }

                bool hasFrom = a == from || b == from || c == from;
                bool hasTo = a == to || b == to || c == to;

                if (hasFrom && hasTo)
                {
                    continue;
                }

                if (!hasFrom && !hasTo)
                {
                    continue;
                }

                int moved = hasFrom ? from : to;

                Point3d first = a == moved ? target : _vertices[a];
                Point3d second = b == moved ? target : _vertices[b];
                Point3d third = c == moved ? target : _vertices[c];

                // THE PASS'S OWN DAMAGE, AND THE THRESHOLD IS RELATIVE RATHER THAN ABSOLUTE.
                // A collapse drags everything attached to the survivor, and an edge it lengthens
                // past the split threshold will simply be split again next iteration - so refusing
                // that is what stops the collapse and split passes trading the same triangles back
                // and forth. But refusing on the ABSOLUTE threshold deadlocks: in a region whose
                // edges are ALREADY too long, every collapse touches one and every collapse is
                // refused, so a 20:1 stretched grid never loses its 0.1-long edges however many
                // iterations it is given. What is forbidden is MAKING a triangle worse, not being
                // near one that already is.
                double was = Longest(_vertices[a], _vertices[b], _vertices[c]);
                double becomes = Longest(first, second, third);

                if (becomes > upper && becomes > was)
                {
                    return false;
                }

                Vector3d before = (_vertices[b] - _vertices[a]).Cross(_vertices[c] - _vertices[a]);
                Vector3d after = (second - first).Cross(third - first);

                if (before.LengthSquared > 0.0 && after.LengthSquared > 0.0 && before.Dot(after) <= 0.0)
                {
                    return false;
                }
            }

            return true;
        }

        private void Remap(int[] merged)
        {
            for (int f = 0; f < _faces.Count; f++)
            {
                MeshFace face = _faces[f];
                _faces[f] = new MeshFace(Root(merged, face.A), Root(merged, face.B), Root(merged, face.C));
            }

            _faces.RemoveAll(face => face.A == face.B || face.B == face.C || face.C == face.A);
        }

        private int Midpoint(Dictionary<(int, int), int> midpoints, int a, int b)
        {
            (int, int) key = a < b ? (a, b) : (b, a);

            if (!midpoints.TryGetValue(key, out int index))
            {
                index = _vertices.Count;
                midpoints[key] = index;
                _vertices.Add(new Point3d(
                    (_vertices[a].X + _vertices[b].X) / 2.0,
                    (_vertices[a].Y + _vertices[b].Y) / 2.0,
                    (_vertices[a].Z + _vertices[b].Z) / 2.0));
            }

            return index;
        }

        private Dictionary<(int, int), List<int>> EdgeFaces()
        {
            Dictionary<(int, int), List<int>> edges = [];

            for (int f = 0; f < _faces.Count; f++)
            {
                MeshFace face = _faces[f];

                for (int corner = 0; corner < 3; corner++)
                {
                    int a = face[corner];
                    int b = face[(corner + 1) % 3];
                    (int, int) key = a < b ? (a, b) : (b, a);

                    if (!edges.TryGetValue(key, out List<int>? faces))
                    {
                        faces = [];
                        edges[key] = faces;
                    }

                    faces.Add(f);
                }
            }

            return edges;
        }

        private bool[] BoundaryVertices()
        {
            bool[] boundary = new bool[_vertices.Count];

            foreach (KeyValuePair<(int, int), List<int>> edge in EdgeFaces())
            {
                if (edge.Value.Count == 1)
                {
                    boundary[edge.Key.Item1] = true;
                    boundary[edge.Key.Item2] = true;
                }
            }

            return boundary;
        }

        private int[] Valences()
        {
            int[] valence = new int[_vertices.Count];

            foreach ((int a, int b) in EdgeFaces().Keys)
            {
                valence[a]++;
                valence[b]++;
            }

            return valence;
        }
    }
}

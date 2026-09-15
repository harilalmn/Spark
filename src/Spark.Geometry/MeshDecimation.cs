using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spark.Geometry;

/// <summary>
/// Reduces a mesh's triangle count while keeping its shape, by quadric error metrics
/// (<c>E2-T68</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The method, in one paragraph, because the code below is unreadable without it.</b> Garland
/// and Heckbert's observation is that the squared distance from a point to a plane is a quadratic
/// form, so the <i>sum</i> of squared distances to a set of planes is one 4×4 symmetric matrix
/// added up. Give every vertex the quadric of the planes of the faces around it; the cost of
/// collapsing an edge onto a point is that point evaluated against the two endpoints' quadrics
/// added together. Collapse the cheapest edge, add its quadric into the survivor, repeat. **The
/// whole algorithm is: a matrix per vertex, a number per edge, and a queue.**
/// </para>
/// <para>
/// <b>Why a mesh needs this at all.</b> A scanned or tessellated mesh carries the triangle count of
/// whatever produced it, which is a fact about the scanner and not about the shape — a cylinder
/// meshed at a fine tolerance is a million triangles that a thousand would draw indistinguishably.
/// `Reduce` is the member a user reaches for after importing an STL, and it is one of two things
/// `E2-T45` found missing from the mesh layer.
/// </para>
/// <para>
/// <b>The collapse target is the midpoint, not the quadric minimum, and that is a decision.</b>
/// Minimising the quadric exactly means inverting a 4×4 matrix that is singular on any flat or
/// symmetric neighbourhood — which is most of a well-behaved mesh — so the textbook implementation
/// spends its complexity on detecting and falling back from that case. Choosing between the two
/// endpoints and their midpoint costs three evaluations of a form we already have, never fails,
/// and keeps every surviving vertex **on or between the original ones**. A vertex that can only
/// move to somewhere its neighbours already were cannot invent geometry, which is worth more here
/// than the last few per cent of fidelity.
/// </para>
/// <para>
/// <b>Two collapses are refused however cheap they are.</b> One that would **flip a face** — turn a
/// triangle inside out — because the cost function measures distance and is blind to orientation,
/// and a folded-over mesh scores well while looking wrong. And one that would make the mesh
/// **non-manifold**: collapsing an edge whose endpoints share more than the two neighbours the edge
/// itself provides pinches the surface, and a pinched mesh has no volume and no consistent normals.
/// Both are cheap local tests and both are the difference between a reduction and a wreck.
/// </para>
/// </remarks>
public static class MeshDecimation
{
    /// <summary>
    /// Reduces a mesh towards a target triangle count.
    /// </summary>
    /// <param name="mesh">The mesh to reduce. Quads are triangulated first.</param>
    /// <param name="targetFaceCount">
    /// How many triangles to aim for. The result has this many or as near as the mesh allows —
    /// reduction stops early when every remaining collapse is refused.
    /// </param>
    /// <returns>The reduced mesh, or the original when it is already at or below the target.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="targetFaceCount"/> is less than one.
    /// </exception>
    /// <remarks>
    /// <b>The target is an aim and the member says so.</b> A mesh that cannot reach it — because
    /// every remaining collapse would flip a face or pinch the surface — comes back larger than
    /// asked for rather than wrecked, and a caller who needs to know compares
    /// <see cref="Mesh.FaceCount"/>. Silently returning something that is not a mesh of the same
    /// shape would be the worse answer.
    /// </remarks>
    public static Mesh Reduced(Mesh mesh, int targetFaceCount)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (targetFaceCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetFaceCount),
                targetFaceCount,
                "A reduced mesh must keep at least one triangle. Reducing to nothing is deleting "
                + "the mesh, and a caller who wants that does not need this member.");
        }

        Mesh triangles = mesh.Triangulated();

        // UNCHANGED MEANS UNCHANGED, including the quads. Returning the triangulated copy here
        // would make a defensive `Reduced(mesh, plenty)` silently convert a quad mesh, and a
        // pipeline that called it on every pass would find its quads gone with nothing in the
        // history to say when.
        if (triangles.FaceCount <= targetFaceCount)
        {
            return mesh;
        }

        Point3d[] positions = triangles.Vertices();
        MeshFace[] faces = triangles.Faces();
        bool[] faceRemoved = new bool[faces.Length];
        int[] merged = new int[positions.Length];

        for (int i = 0; i < merged.Length; i++)
        {
            merged[i] = i;
        }

        Quadric[] quadrics = new Quadric[positions.Length];
        List<int>[] incident = new List<int>[positions.Length];

        for (int i = 0; i < incident.Length; i++)
        {
            incident[i] = [];
        }

        for (int f = 0; f < faces.Length; f++)
        {
            Quadric plane = Quadric.OfTriangle(positions[faces[f].A], positions[faces[f].B], positions[faces[f].C]);

            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = faces[f][corner];
                quadrics[vertex] = quadrics[vertex].Add(plane);
                incident[vertex].Add(f);
            }
        }

        int live = faces.Length;

        // A REBUILT QUEUE RATHER THAN A DECREASE-KEY HEAP. A collapse changes the cost of every
        // edge around the survivor, and keeping a heap correct through that needs handles into it.
        // Rebuilding the candidate list each pass is O(edges) against a collapse that is already
        // O(neighbourhood), and it costs a constant factor on an operation a user runs once - where
        // a stale-priority bug costs a mesh that is quietly the wrong shape.
        while (live > targetFaceCount)
        {
            if (!TryFindCheapest(faces, faceRemoved, merged, quadrics, positions, out int fromVertex, out int toVertex, out Point3d target))
            {
                break;
            }

            positions[toVertex] = target;
            quadrics[toVertex] = quadrics[toVertex].Add(quadrics[fromVertex]);
            merged[fromVertex] = toVertex;

            foreach (int f in incident[fromVertex])
            {
                if (!faceRemoved[f])
                {
                    incident[toVertex].Add(f);
                }
            }

            for (int f = 0; f < faces.Length; f++)
            {
                if (faceRemoved[f])
                {
                    continue;
                }

                int a = Root(merged, faces[f].A);
                int b = Root(merged, faces[f].B);
                int c = Root(merged, faces[f].C);

                if (a == b || b == c || c == a)
                {
                    faceRemoved[f] = true;
                    live--;
                }
            }
        }

        return Rebuild(positions, faces, faceRemoved, merged);
    }

    /// <summary>Follows the merge chain to the vertex that survived.</summary>
    private static int Root(int[] merged, int vertex)
    {
        while (merged[vertex] != vertex)
        {
            vertex = merged[vertex];
        }

        return vertex;
    }

    /// <summary>The cheapest legal collapse over every surviving edge, or none.</summary>
    private static bool TryFindCheapest(
        MeshFace[] faces,
        bool[] faceRemoved,
        int[] merged,
        Quadric[] quadrics,
        Point3d[] positions,
        out int fromVertex,
        out int toVertex,
        out Point3d target)
    {
        double best = double.PositiveInfinity;
        fromVertex = -1;
        toVertex = -1;
        target = Point3d.Origin;

        for (int f = 0; f < faces.Length; f++)
        {
            if (faceRemoved[f])
            {
                continue;
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int first = Root(merged, faces[f][corner]);
                int second = Root(merged, faces[f][(corner + 1) % 3]);

                if (first == second)
                {
                    continue;
                }

                Quadric combined = quadrics[first].Add(quadrics[second]);
                Point3d candidate = Cheapest(combined, positions[first], positions[second], out double cost);

                if (cost >= best)
                {
                    continue;
                }

                // Cost first, legality second: the legality tests walk the neighbourhood and the
                // cost is three multiplications, so testing the cheap thing first is what keeps
                // this loop affordable.
                if (!IsCollapseSafe(faces, faceRemoved, merged, positions, first, second, candidate))
                {
                    continue;
                }

                best = cost;
                fromVertex = first;
                toVertex = second;
                target = candidate;
            }
        }

        return fromVertex >= 0;
    }

    /// <summary>The cheaper of the two endpoints and their midpoint, with its cost.</summary>
    private static Point3d Cheapest(in Quadric quadric, in Point3d first, in Point3d second, out double cost)
    {
        Point3d middle = new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0, (first.Z + second.Z) / 2.0);

        double atFirst = quadric.Evaluate(first);
        double atSecond = quadric.Evaluate(second);
        double atMiddle = quadric.Evaluate(middle);

        if (atMiddle <= atFirst && atMiddle <= atSecond)
        {
            cost = atMiddle;
            return middle;
        }

        if (atFirst <= atSecond)
        {
            cost = atFirst;
            return first;
        }

        cost = atSecond;
        return second;
    }

    /// <summary>
    /// Whether collapsing <paramref name="from"/> onto <paramref name="to"/> keeps the mesh sane.
    /// </summary>
    /// <remarks>
    /// Two refusals, and neither is visible to the cost function. **A flipped face** — the quadric
    /// measures distance and a triangle turned inside out sits in the same plane, so it scores as
    /// free. **A pinch** — two vertices joined by an edge that share a neighbour other than the two
    /// the edge itself provides form a tetrahedral fin, and collapsing it welds two sheets of
    /// surface together at a point.
    /// </remarks>
    private static bool IsCollapseSafe(
        MeshFace[] faces,
        bool[] faceRemoved,
        int[] merged,
        Point3d[] positions,
        int from,
        int to,
        in Point3d target)
    {
        HashSet<int> aroundFrom = [];
        HashSet<int> aroundTo = [];

        // WHAT STOPS A TETRAHEDRON COLLAPSING INTO NOTHING, which is the defect the test named
        // ATargetThatCannotBeReachedStopsRatherThanWrecksTheMesh found on the first run. Collapsing
        // any edge of a tetrahedron leaves its two far faces with identical corners: a doubled
        // sheet with no volume, which passes the flip test and the link condition and is not a
        // solid. Collapsing again removes both and the mesh is gone. Refusing a collapse that would
        // produce two identical faces stops it at the tetrahedron, which is the smallest closed
        // mesh there is - and it is a local test rather than a special case about counts.
        HashSet<(int, int, int)> survivors = [];
        int shared = 0;

        for (int f = 0; f < faces.Length; f++)
        {
            if (faceRemoved[f])
            {
                continue;
            }

            int a = Root(merged, faces[f].A);
            int b = Root(merged, faces[f].B);
            int c = Root(merged, faces[f].C);

            bool hasFrom = a == from || b == from || c == from;
            bool hasTo = a == to || b == to || c == to;

            if (hasFrom && hasTo)
            {
                shared++;
                continue;
            }

            if (hasFrom || hasTo)
            {
                // THE FACE AS IT WILL BE AFTER THE COLLAPSE, keyed so that two faces with the same
                // three corners in any order collide. A duplicate here means the collapse has
                // folded two sheets of surface onto each other - see below.
                int[] corners = [a == from ? to : a, b == from ? to : b, c == from ? to : c];
                Array.Sort(corners);

                if (!survivors.Add((corners[0], corners[1], corners[2])))
                {
                    return false;
                }
            }

            if (hasFrom)
            {
                aroundFrom.Add(a);
                aroundFrom.Add(b);
                aroundFrom.Add(c);

                // A face that keeps `from` but not `to` survives the collapse with `from` moved to
                // the target. If that turns it over, the collapse is refused however cheap it was.
                if (WouldFlip(positions, a, b, c, from, target))
                {
                    return false;
                }
            }
            else if (hasTo)
            {
                aroundTo.Add(a);
                aroundTo.Add(b);
                aroundTo.Add(c);

                if (WouldFlip(positions, a, b, c, to, target))
                {
                    return false;
                }
            }
        }

        // A manifold interior edge has exactly two faces and a boundary edge one. More than two is
        // already non-manifold, and collapsing it makes that worse rather than better.
        if (shared > 2)
        {
            return false;
        }

        aroundFrom.Remove(from);
        aroundFrom.Remove(to);
        aroundTo.Remove(from);
        aroundTo.Remove(to);
        aroundFrom.IntersectWith(aroundTo);

        // The link condition: the two endpoints may share exactly as many neighbours as the edge
        // has faces. Any more and the collapse pinches two parts of the surface together.
        return aroundFrom.Count <= shared;
    }

    /// <summary>Whether moving one corner of a triangle to a new point reverses its normal.</summary>
    /// <remarks>
    /// <para>
    /// <b>NO FIXTURE IN THE SUITE MAKES THIS RETURN TRUE, and that is recorded rather than hidden.</b>
    /// Disabling it entirely leaves every test green — a sphere, a subdivided box and a corrugated
    /// height field built specifically to fold all reduce identically without it. **The reason is
    /// the placement rule two paragraphs up**: the survivor goes to an endpoint or the midpoint, so
    /// it lands on or between vertices that were already there, and the flip that classic quadric
    /// decimation has to guard against comes from placing the survivor at the quadric's *minimum*,
    /// which can be far outside the neighbourhood.
    /// </para>
    /// <para>
    /// <b>It is kept anyway, and the argument is not symmetry.</b> Midpoint placement makes a flip
    /// rare rather than impossible — a sufficiently thin sliver in a non-convex neighbourhood can
    /// still fold when a corner moves inside the edge it shared — and the cost of the check is a
    /// cross product against a consequence that is a mesh silently inside out. **What must not
    /// happen is somebody reading this as covered.** `NoFaceIsTurnedInsideOutByAReduction` asserts
    /// the property and passes either way; it is a guard on the *result*, not on this method.
    /// </para>
    /// </remarks>
    private static bool WouldFlip(Point3d[] positions, int a, int b, int c, int moved, in Point3d target)
    {
        Point3d first = a == moved ? target : positions[a];
        Point3d second = b == moved ? target : positions[b];
        Point3d third = c == moved ? target : positions[c];

        Vector3d before = (positions[b] - positions[a]).Cross(positions[c] - positions[a]);
        Vector3d after = (second - first).Cross(third - first);

        // A triangle that was already degenerate has no normal to reverse, and one that BECOMES
        // degenerate is about to be removed by the collapse rather than kept inside out.
        return before.LengthSquared > 0.0 && after.LengthSquared > 0.0 && before.Dot(after) <= 0.0;
    }

    /// <summary>Collects the surviving faces and renumbers the vertices they still use.</summary>
    private static Mesh Rebuild(Point3d[] positions, MeshFace[] faces, bool[] faceRemoved, int[] merged)
    {
        Dictionary<int, int> renumbered = [];
        List<Point3d> vertices = [];
        List<MeshFace> kept = [];

        int Number(int vertex)
        {
            int root = Root(merged, vertex);

            if (!renumbered.TryGetValue(root, out int index))
            {
                index = vertices.Count;
                renumbered[root] = index;
                vertices.Add(positions[root]);
            }

            return index;
        }

        for (int f = 0; f < faces.Length; f++)
        {
            if (faceRemoved[f])
            {
                continue;
            }

            kept.Add(new MeshFace(Number(faces[f].A), Number(faces[f].B), Number(faces[f].C)));
        }

        return kept.Count > 0
            ? new Mesh(vertices, kept)
            : throw new InvalidOperationException(
                "Every face was removed by the reduction, which should be impossible: the loop "
                + "stops at the target count and the target is at least one. "
                + faces.Length.ToString(CultureInfo.InvariantCulture)
                + " faces went in.");
    }

    /// <summary>
    /// The sum of squared distances to a set of planes, as the 4×4 symmetric matrix it is.
    /// </summary>
    /// <remarks>
    /// Ten numbers rather than sixteen, because the matrix is symmetric — and a struct rather than
    /// an array, because there is one of these per vertex and a million-triangle mesh has half a
    /// million vertices.
    /// </remarks>
    private readonly struct Quadric(
        double xx, double xy, double xz, double xw,
        double yy, double yz, double yw,
        double zz, double zw,
        double ww)
    {
        /// <summary>The quadric of the plane through a triangle, or zero for a degenerate one.</summary>
        internal static Quadric OfTriangle(in Point3d a, in Point3d b, in Point3d c)
        {
            if (!(b - a).Cross(c - a).TryNormalise(out Vector3d normal))
            {
                return default;
            }

            double d = -(normal.X * a.X + normal.Y * a.Y + normal.Z * a.Z);

            return new Quadric(
                normal.X * normal.X, normal.X * normal.Y, normal.X * normal.Z, normal.X * d,
                normal.Y * normal.Y, normal.Y * normal.Z, normal.Y * d,
                normal.Z * normal.Z, normal.Z * d,
                d * d);
        }

        internal Quadric Add(in Quadric other) =>
            new(xx + other.Xx, xy + other.Xy, xz + other.Xz, xw + other.Xw,
                yy + other.Yy, yz + other.Yz, yw + other.Yw,
                zz + other.Zz, zw + other.Zw,
                ww + other.Ww);

        /// <summary>The sum of squared distances from a point to every plane in this quadric.</summary>
        internal double Evaluate(in Point3d p) =>
            (xx * p.X * p.X) + (2.0 * xy * p.X * p.Y) + (2.0 * xz * p.X * p.Z) + (2.0 * xw * p.X)
            + (yy * p.Y * p.Y) + (2.0 * yz * p.Y * p.Z) + (2.0 * yw * p.Y)
            + (zz * p.Z * p.Z) + (2.0 * zw * p.Z)
            + ww;

        private double Xx => xx;

        private double Xy => xy;

        private double Xz => xz;

        private double Xw => xw;

        private double Yy => yy;

        private double Yz => yz;

        private double Yw => yw;

        private double Zz => zz;

        private double Zw => zw;

        private double Ww => ww;
    }
}

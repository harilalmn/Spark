using System;
using System.Linq;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="MeshDecimation.Reduced"/>: fewer triangles, the same shape (<c>E2-T68</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A decimator has two ways to look right and be wrong</b>, and each of these tests exists for
/// one of them. It can hit the triangle count by destroying the shape — which a face-count
/// assertion alone cannot see, so every reduction here is also measured against the volume and the
/// bounding box it started with. And it can keep the shape by doing nothing — which a shape
/// assertion alone cannot see, so every one also asserts the count actually fell.
/// </para>
/// <para>
/// <b>The fixtures are exact-arithmetic where the assertion is exact.</b> A cube's corners are
/// integers and its volume is an integer, so *the corners survive* is a claim with no tolerance in
/// it at all. The sphere is where the tolerance lives, and its assertions are proportional rather
/// than absolute for that reason.
/// </para>
/// </remarks>
public sealed class MeshDecimationTests
{
    /// <summary>
    /// <b>A reduction reduces.</b> The first thing to get wrong is returning the input, and it
    /// looks perfect against every shape metric there is.
    /// </summary>
    [Fact]
    public void ASphereLosesTrianglesAndKeepsItsShape()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 32, 16);
        double before = sphere.Volume();

        Mesh reduced = MeshDecimation.Reduced(sphere, sphere.FaceCount / 4);

        Assert.True(
            reduced.FaceCount < sphere.FaceCount,
            $"the mesh did not shrink: {sphere.FaceCount} faces in, {reduced.FaceCount} out.");

        // A quarter of the triangles of a sphere still encloses very nearly the same space. The
        // bound is generous because the target is an aim, and tight enough that a decimator which
        // collapsed the sphere towards a point would blow straight through it.
        double after = reduced.Volume();
        Assert.InRange(after / before, 0.85, 1.05);
    }

    /// <summary>
    /// <b>The result is still a mesh, not a bag of triangles.</b> The collapse that pinches a
    /// surface is invisible in a face count and in a volume, and shows up here: a closed sphere
    /// that stops being closed has been torn.
    /// </summary>
    [Fact]
    public void AClosedMeshStaysClosedAndManifold()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 24, 12);

        Assert.True(sphere.Topology.IsClosed, "the fixture is not closed, so this test proves nothing.");

        Mesh reduced = MeshDecimation.Reduced(sphere, sphere.FaceCount / 2);

        Assert.Equal(0, reduced.Topology.NonManifoldEdgeCount);
        Assert.Equal(0, reduced.Topology.NakedEdgeCount);
        Assert.True(reduced.Topology.IsClosed, "the reduction opened a closed mesh.");
    }

    /// <summary>
    /// <b>The corners of a cube survive, exactly.</b> This is the sharpest statement the method
    /// makes: a quadric is the sum of squared distances to the planes around a vertex, and a corner
    /// where three perpendicular planes meet costs a great deal to move anywhere at all. If a
    /// decimator rounds off a box, its cost function is wrong — and the test needs no tolerance,
    /// because the corners are integers and a surviving corner is at its original coordinates.
    /// </summary>
    [Fact]
    public void TheCornersOfABoxSurviveBecauseTheyAreExpensiveToMove()
    {
        // A cube subdivided so that there is something to remove: the flat middle of each face is
        // free to collapse and the eight corners are not.
        Mesh box = MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 2.0, 2.0).Welded(1e-9);
        Mesh dense = Subdivided(Subdivided(box));

        Mesh reduced = MeshDecimation.Reduced(dense, dense.FaceCount / 4);

        Assert.True(reduced.FaceCount < dense.FaceCount);

        Point3d[] corners = [.. reduced.Vertices().Where(IsCorner)];

        Assert.Equal(8, corners.Length);
        Assert.All(corners, corner => Assert.True(
            Math.Abs(Math.Abs(corner.X) - 1.0) < 1e-9
            && Math.Abs(Math.Abs(corner.Y) - 1.0) < 1e-9
            && Math.Abs(Math.Abs(corner.Z) - 1.0) < 1e-9,
            corner.ToString()));

        // And the box is still the size of a box.
        Assert.True(reduced.BoundingBox.Min.EqualsWithin(dense.BoundingBox.Min, new Tolerance(1e-9, Angle.Zero, 1e-12)));
        Assert.True(reduced.BoundingBox.Max.EqualsWithin(dense.BoundingBox.Max, new Tolerance(1e-9, Angle.Zero, 1e-12)));
    }

    /// <summary>
    /// <b>A mesh already at or below the target comes back unchanged.</b> Reducing a shape that
    /// needs no reduction must not cost it a triangle, or every pipeline that calls this
    /// defensively erodes its geometry a little on each pass.
    /// </summary>
    [Fact]
    public void AMeshAlreadySmallEnoughIsReturnedUntouched()
    {
        Mesh cube = MeshPrimitives.Cuboid(Plane.WorldXY, 1.0, 1.0, 1.0);

        // The target is compared against the TRIANGLE count, because that is what a reduction
        // works in - six quads are twelve triangles, and asking for six would be asking for a
        // halving rather than for nothing.
        Mesh same = MeshDecimation.Reduced(cube, cube.Triangulated().FaceCount);
        Mesh alsoSame = MeshDecimation.Reduced(cube, cube.FaceCount * 10);

        Assert.Equal(cube.FaceCount, same.FaceCount);
        Assert.Equal(cube.FaceCount, alsoSame.FaceCount);
        Assert.Equal(cube.Area, same.Area, 9);

        // And the quads survive: "unchanged" has to mean the mesh that arrived, not a
        // triangulated copy of it, or a defensive call quietly rewrites the caller's topology.
        Assert.Equal(cube.QuadCount, same.QuadCount);
    }

    /// <summary>
    /// <b>The target is an aim, and a mesh that cannot reach it comes back larger rather than
    /// wrecked.</b> Asking a cube for one triangle is asking for something that is not a cube: the
    /// collapses that would get there all flip a face or pinch the surface, they are refused, and
    /// the loop stops. **Returning a wreck that happens to have one triangle would satisfy the
    /// argument and destroy the caller's model.**
    /// </summary>
    [Fact]
    public void ATargetThatCannotBeReachedStopsRatherThanWrecksTheMesh()
    {
        Mesh cube = MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 2.0, 2.0).Welded(1e-9);

        Mesh reduced = MeshDecimation.Reduced(cube, 1);

        Assert.True(reduced.FaceCount >= 4, $"a closed shape needs four triangles; got {reduced.FaceCount}.");
        Assert.Equal(0, reduced.Topology.NonManifoldEdgeCount);
    }

    /// <summary>
    /// <b>No triangle ends up facing backwards.</b> The cost function measures distance and is
    /// blind to orientation, so a collapse that folds a triangle over scores as free — the folded
    /// triangle sits in very nearly the same plane it did before. On a height field every face
    /// must keep pointing upwards, and one that does not has been turned inside out.
    /// </summary>
    /// <remarks>
    /// <b>The fixture is corrugated on purpose.</b> A sphere and a box are convex and
    /// well-conditioned, and a decimator with no flip test at all reduces both of them perfectly —
    /// which is what the first version of this suite found, and why this test exists. Alternating
    /// spikes give the collapse somewhere to fold to.
    /// </remarks>
    [Fact]
    public void NoFaceIsTurnedInsideOutByAReduction()
    {
        Mesh corrugated = Corrugated(12, 12);

        Assert.All(
            Enumerable.Range(0, corrugated.FaceCount),
            f => Assert.True(corrugated.FaceNormal(f).Z > 0.0, "the fixture already has a downward face."));

        Mesh reduced = MeshDecimation.Reduced(corrugated, corrugated.FaceCount / 3);

        Assert.True(reduced.FaceCount < corrugated.FaceCount);

        int[] backwards =
        [
            .. Enumerable.Range(0, reduced.FaceCount).Where(f => reduced.FaceNormal(f).Z <= 0.0),
        ];

        Assert.True(
            backwards.Length == 0,
            $"{backwards.Length} of {reduced.FaceCount} faces were turned inside out by the reduction.");
    }

    /// <summary>Reducing to nothing is deleting the mesh, and is refused by name.</summary>
    [Fact]
    public void ATargetBelowOneIsRefused()
    {
        Mesh cube = MeshPrimitives.Cuboid(Plane.WorldXY, 1.0, 1.0, 1.0);

        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => MeshDecimation.Reduced(cube, 0));

        Assert.Equal("targetFaceCount", refused.ParamName);
    }

    /// <summary>
    /// <b>Quads are triangulated rather than refused.</b> <c>MeshPrimitives.Cuboid</c> makes quads
    /// and an imported mesh often does, so a decimator that only spoke triangles would refuse the
    /// most ordinary input there is.
    /// </summary>
    [Fact]
    public void AQuadMeshIsTriangulatedRatherThanRefused()
    {
        Mesh quads = MeshPrimitives.Plane(Plane.WorldXY, 4.0, 4.0, 8, 8);

        Assert.True(quads.QuadCount > 0, "the fixture has no quads, so this test proves nothing.");

        Mesh reduced = MeshDecimation.Reduced(quads, 16);

        Assert.Equal(0, reduced.QuadCount);
        Assert.True(reduced.FaceCount <= quads.Triangulated().FaceCount);
    }

    private static bool IsCorner(Point3d p) =>
        Math.Abs(Math.Abs(p.X) - 1.0) < 1e-9
        && Math.Abs(Math.Abs(p.Y) - 1.0) < 1e-9
        && Math.Abs(Math.Abs(p.Z) - 1.0) < 1e-9;

    /// <summary>Splits every triangle into four, so there is interior detail to remove.</summary>
    /// <remarks>
    /// Written here rather than taken from the kernel because the kernel has no subdivision — which
    /// is `Remesh`, the other member `E2-T68` still owes. Midpoints of exact coordinates are exact,
    /// so this adds vertices without adding arithmetic the corner assertion would have to tolerate.
    /// </remarks>
    private static Mesh Subdivided(Mesh mesh)
    {
        Mesh triangles = mesh.Triangulated();
        System.Collections.Generic.List<Point3d> vertices = [.. triangles.Vertices()];
        System.Collections.Generic.List<MeshFace> faces = [];
        System.Collections.Generic.Dictionary<(int, int), int> midpoints = [];

        int Midpoint(int a, int b)
        {
            (int, int) key = a < b ? (a, b) : (b, a);

            if (!midpoints.TryGetValue(key, out int index))
            {
                Point3d first = vertices[a];
                Point3d second = vertices[b];
                index = vertices.Count;
                midpoints[key] = index;
                vertices.Add(new Point3d(
                    (first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0, (first.Z + second.Z) / 2.0));
            }

            return index;
        }

        for (int f = 0; f < triangles.FaceCount; f++)
        {
            MeshFace face = triangles.Face(f);
            int ab = Midpoint(face.A, face.B);
            int bc = Midpoint(face.B, face.C);
            int ca = Midpoint(face.C, face.A);

            faces.Add(new MeshFace(face.A, ab, ca));
            faces.Add(new MeshFace(ab, face.B, bc));
            faces.Add(new MeshFace(ca, bc, face.C));
            faces.Add(new MeshFace(ab, bc, ca));
        }

        return new Mesh(vertices, faces);
    }

    /// <summary>A height field with alternating spikes: everything faces up, and nothing is flat.</summary>
    private static Mesh Corrugated(int columns, int rows)
    {
        System.Collections.Generic.List<Point3d> vertices = [];
        System.Collections.Generic.List<MeshFace> faces = [];

        for (int y = 0; y <= rows; y++)
        {
            for (int x = 0; x <= columns; x++)
            {
                // Exactly representable heights, so the fixture is bit-stable: a quarter up on
                // every other lattice point and zero elsewhere.
                double height = ((x + y) % 2 == 0) ? 0.0 : 0.25;
                vertices.Add(new Point3d(x, y, height));
            }
        }

        int Index(int x, int y) => (y * (columns + 1)) + x;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                faces.Add(new MeshFace(Index(x, y), Index(x + 1, y), Index(x + 1, y + 1)));
                faces.Add(new MeshFace(Index(x, y), Index(x + 1, y + 1), Index(x, y + 1)));
            }
        }

        return new Mesh(vertices, faces);
    }
}

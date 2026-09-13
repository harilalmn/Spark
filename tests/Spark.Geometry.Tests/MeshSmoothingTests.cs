using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh.Smoothed"/> and <see cref="MeshTopology.VerticesAroundVertex"/> — `E2-T68`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the pinned boundary.</b> A smoother that moves boundary vertices still smooths,
/// still terminates, and still looks right in the middle — what it does is pull an open mesh
/// inwards from its own edges, a little more with every pass. No test of the interior can see it,
/// so the assertions are that the boundary vertices have not moved at all and that the bounding box
/// has not shrunk.
/// </para>
/// </remarks>
public sealed class MeshSmoothingTests
{
    private const double Tight = 1e-12;

    /// <summary><b>The branch.</b> A grid's boundary vertices are exactly where they were.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    public void TheBoundaryDoesNotMove(int passes)
    {
        Mesh grid = Bumpy();
        HashSet<int> boundary = [];

        foreach ((int from, int to) in grid.Topology.NakedEdges())
        {
            boundary.Add(from);
            boundary.Add(to);
        }

        Assert.NotEmpty(boundary);

        Mesh smoothed = grid.Smoothed(0.5, passes);

        foreach (int index in boundary)
        {
            Assert.True(
                smoothed.Vertex(index).DistanceTo(grid.Vertex(index)) < Tight,
                $"after {passes} passes, boundary vertex {index} moved.");
        }
    }

    /// <summary>
    /// And the mesh does not shrink: the bounding box of a smoothed open grid is the original's,
    /// because its extremes are all on the boundary.
    /// </summary>
    [Fact]
    public void AnOpenMeshDoesNotShrink()
    {
        Mesh grid = MeshPrimitives.Plane(Plane.WorldXY, 4.0, 6.0, 5, 5);

        BoundingBox before = grid.BoundingBox;
        BoundingBox after = grid.Smoothed(1.0, 20).BoundingBox;

        Assert.Equal(before.Min.X, after.Min.X, 1e-9);
        Assert.Equal(before.Max.X, after.Max.X, 1e-9);
        Assert.Equal(before.Min.Y, after.Min.Y, 1e-9);
        Assert.Equal(before.Max.Y, after.Max.Y, 1e-9);
    }

    /// <summary>
    /// <b>It actually smooths.</b> One vertex pulled out of plane comes back towards it, which is
    /// the property the member is named for.
    /// </summary>
    [Fact]
    public void ABumpIsFlattened()
    {
        Mesh bumpy = Bumpy();
        int bump = BumpIndex(bumpy);

        double before = Math.Abs(bumpy.Vertex(bump).Z);
        double after = Math.Abs(bumpy.Smoothed(0.5, 1).Vertex(bump).Z);

        Assert.True(before > 0.5, "the test mesh should have a bump to flatten.");
        Assert.True(after < before, $"the bump grew: {before} then {after}.");
    }

    /// <summary>
    /// <b>The strength has to matter.</b> Zero changes nothing, and more moves further — which an
    /// implementation ignoring its argument would fail.
    /// </summary>
    [Fact]
    public void TheStrengthMatters()
    {
        Mesh bumpy = Bumpy();
        int bump = BumpIndex(bumpy);
        double original = bumpy.Vertex(bump).Z;

        double none = bumpy.Smoothed(0.0, 5).Vertex(bump).Z;
        double gentle = bumpy.Smoothed(0.25, 1).Vertex(bump).Z;
        double firm = bumpy.Smoothed(1.0, 1).Vertex(bump).Z;

        Assert.Equal(original, none, Tight);
        Assert.True(Math.Abs(firm) < Math.Abs(gentle), $"a firmer smoothing should move further: {gentle} then {firm}.");
        Assert.True(Math.Abs(gentle) < Math.Abs(original));
    }

    /// <summary>More passes move further than fewer, at the same strength.</summary>
    [Fact]
    public void MorePassesMoveFurther()
    {
        Mesh bumpy = Bumpy();
        int bump = BumpIndex(bumpy);

        double once = Math.Abs(bumpy.Smoothed(0.4, 1).Vertex(bump).Z);
        double often = Math.Abs(bumpy.Smoothed(0.4, 6).Vertex(bump).Z);

        Assert.True(often < once, $"more passes should smooth further: {once} then {often}.");
    }

    /// <summary>
    /// <b>Smoothing moves points and nothing else.</b> The counts and the edge set are the
    /// original's, so anything pairing the two index for index still can.
    /// </summary>
    [Theory]
    [MemberData(nameof(Meshes))]
    public void TheTopologyIsUnchanged(Mesh mesh)
    {
        Mesh smoothed = mesh.Smoothed(0.5, 3);

        Assert.Equal(mesh.VertexCount, smoothed.VertexCount);
        Assert.Equal(mesh.FaceCount, smoothed.FaceCount);
        Assert.Equal(mesh.Topology.EdgeCount, smoothed.Topology.EdgeCount);
        Assert.Equal(mesh.Topology.IsClosed, smoothed.Topology.IsClosed);

        for (int index = 0; index < mesh.FaceCount; index++)
        {
            MeshFace before = mesh.Face(index);
            MeshFace after = smoothed.Face(index);

            Assert.Equal(before.Count, after.Count);

            for (int corner = 0; corner < before.Count; corner++)
            {
                Assert.Equal(before[corner], after[corner]);
            }
        }
    }

    /// <summary>
    /// <b>Every pass reads the previous pass's positions, not its own partial results.</b> At full
    /// strength one pass puts each free vertex exactly at the average of its neighbours' <i>original</i>
    /// positions — an implementation updating in place would use neighbours that had already moved,
    /// and would give a different answer that depends on the order the vertices are stored in.
    /// </summary>
    [Fact]
    public void APassReadsThePreviousPassRatherThanItself()
    {
        Mesh grid = Bumpy();
        Mesh smoothed = grid.Smoothed(1.0, 1);

        HashSet<int> boundary = [];
        foreach ((int from, int to) in grid.Topology.NakedEdges())
        {
            boundary.Add(from);
            boundary.Add(to);
        }

        int checkedVertices = 0;

        for (int index = 0; index < grid.VertexCount; index++)
        {
            if (boundary.Contains(index))
            {
                continue;
            }

            int[] around = grid.Topology.VerticesAroundVertex(index);
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;

            foreach (int neighbour in around)
            {
                x += grid.Vertex(neighbour).X;
                y += grid.Vertex(neighbour).Y;
                z += grid.Vertex(neighbour).Z;
            }

            Point3d expected = new(x / around.Length, y / around.Length, z / around.Length);

            Assert.True(
                smoothed.Vertex(index).DistanceTo(expected) < 1e-12,
                $"vertex {index} is at {smoothed.Vertex(index)}, not the average of its original neighbours {expected}.");

            checkedVertices++;
        }

        Assert.True(checkedVertices > 0, "the grid should have interior vertices to check.");
    }

    /// <summary>A closed mesh has no boundary to pin, so every vertex is free to move.</summary>
    [Fact]
    public void AClosedMeshHasEveryVertexFree()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 10, 5);

        Assert.True(sphere.Topology.IsClosed);

        Mesh smoothed = sphere.Smoothed(1.0, 1);

        // Laplacian smoothing shrinks a closed mesh, which is the known behaviour rather than a
        // defect: every vertex moves towards the average of its neighbours, which is inside.
        Assert.True(
            smoothed.BoundingBox.Diagonal.Length < sphere.BoundingBox.Diagonal.Length,
            "a closed mesh has no pinned boundary, so smoothing shrinks it.");
    }

    /// <summary>A vertex's neighbours are the ones an edge joins it to, each once.</summary>
    [Fact]
    public void TheNeighboursOfAVertexAreItsEdgeNeighbours()
    {
        // A quad and a triangle sharing the edge 1-2.
        Mesh mesh = new(
            [
                new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0),
                new Point3d(0, 1, 0), new Point3d(2, 0.5, 0),
            ],
            [new MeshFace(0, 1, 2, 3), new MeshFace(1, 4, 2)]);

        int[] around = mesh.Topology.VerticesAroundVertex(1);

        Assert.Equal(around.Length, around.Distinct().Count());
        Assert.Equal([0, 2, 4], [.. around.OrderBy(index => index)]);
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Mesh grid = Bumpy();

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Smoothed(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Smoothed(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Smoothed(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Smoothed(0.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Topology.VerticesAroundVertex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Topology.VerticesAroundVertex(grid.VertexCount));
    }

    /// <summary>A flat grid with one interior vertex pushed up out of plane.</summary>
    private static Mesh Bumpy()
    {
        Mesh flat = MeshPrimitives.Plane(Plane.WorldXY, 4.0, 4.0, 4, 4);
        Point3d[] vertices = flat.Vertices();

        vertices[BumpIndexOf(vertices)] = vertices[BumpIndexOf(vertices)] + (Vector3d.ZAxis * 1.0);

        return new Mesh(vertices, flat.Faces());
    }

    private static int BumpIndex(Mesh mesh) => BumpIndexOf(mesh.Vertices());

    /// <summary>The vertex nearest the centre of the grid, which is the one the bump is on.</summary>
    private static int BumpIndexOf(Point3d[] vertices)
    {
        int best = 0;
        double nearest = double.PositiveInfinity;

        for (int index = 0; index < vertices.Length; index++)
        {
            double distance = new Point3d(vertices[index].X, vertices[index].Y, 0.0).DistanceTo(Point3d.Origin);

            if (distance < nearest)
            {
                nearest = distance;
                best = index;
            }
        }

        return best;
    }

    public static TheoryData<Mesh> Meshes() =>
    [
        MeshPrimitives.Plane(Plane.WorldXY, 3.0, 3.0, 4, 4),
        MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 10, 5),
        MeshPrimitives.Cone(Plane.WorldXY, 1.0, 0.3, 2.0, 8),
    ];
}

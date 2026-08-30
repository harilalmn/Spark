using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh"/>, <see cref="MeshFace"/> and <see cref="MeshTopology"/> — `E2-T20`.
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed cube is the fixture almost everything here uses</b>, and it is the right one: it is
/// the smallest shape whose volume, area, edge count and closure are all known exactly, so an error
/// anywhere shows up as a wrong number rather than as a plausible one. Its quad form and its
/// triangulated form must agree about every one of those, which is a second independent statement.
/// </para>
/// <para>
/// <b>The topology tests are written around the malformed cases</b>, because that is where a
/// halfedge structure earns its keep: an open mesh, a mesh with a face wound backwards, and a mesh
/// with three faces on one edge. Each of those is something a boolean or a scan produces, and each
/// one has a question that answers it.
/// </para>
/// </remarks>
public sealed class MeshTests
{
    /// <summary>The unit cube, as six quads, wound outwards.</summary>
    private static Mesh Cube()
    {
        Point3d[] vertices =
        [
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        ];

        MeshFace[] faces =
        [
            new(0, 3, 2, 1),  // bottom, seen from below
            new(4, 5, 6, 7),  // top
            new(0, 1, 5, 4),  // front
            new(1, 2, 6, 5),  // right
            new(2, 3, 7, 6),  // back
            new(3, 0, 4, 7),  // left
        ];

        return new Mesh(vertices, faces);
    }

    /// <summary>One triangle, which is the smallest open mesh.</summary>
    private static Mesh Triangle() =>
        new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0)],
            [new MeshFace(0, 1, 2)]);

    // -- Faces --------------------------------------------------------------------------------

    /// <summary>A triangle has three corners and a quad has four.</summary>
    [Fact]
    public void AFaceKnowsHowManyCornersItHas()
    {
        MeshFace triangle = new(0, 1, 2);
        MeshFace quad = new(0, 1, 2, 3);

        Assert.False(triangle.IsQuad);
        Assert.Equal(3, triangle.Count);
        Assert.True(quad.IsQuad);
        Assert.Equal(4, quad.Count);
        Assert.Equal(MeshFace.NoVertex, triangle.D);
    }

    /// <summary>Asking a triangle for a fourth corner is an error rather than a repeat.</summary>
    [Fact]
    public void ATriangleHasNoFourthCorner() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeshFace(0, 1, 2)[3]);

    /// <summary>
    /// <b>A quad splits on the <c>A–C</c> diagonal, always.</b> Choosing the shorter diagonal
    /// would make the result depend on the vertex positions, so two meshes with identical topology
    /// and slightly different coordinates would triangulate differently.
    /// </summary>
    [Fact]
    public void AQuadSplitsOnAStableDiagonal()
    {
        MeshFace[] halves = new MeshFace(4, 5, 6, 7).Triangulated();

        Assert.Equal(2, halves.Length);
        Assert.Equal(new MeshFace(4, 5, 6), halves[0]);
        Assert.Equal(new MeshFace(4, 6, 7), halves[1]);
    }

    /// <summary>A degenerate face can be asked about rather than being refused at construction.</summary>
    [Fact]
    public void ADegenerateFaceIsDescribedRatherThanRefused()
    {
        Assert.True(new MeshFace(1, 1, 2).IsDegenerate);
        Assert.True(new MeshFace(0, 1, 2, 0).IsDegenerate);
        Assert.False(new MeshFace(0, 1, 2, 3).IsDegenerate);
    }

    // -- Construction --------------------------------------------------------------------------

    /// <summary>
    /// <b>A face pointing past the end of the vertex array is caught at construction</b>, with the
    /// arithmetic. Left to be found later it becomes an index-out-of-range from inside a renderer,
    /// naming nothing a user could act on.
    /// </summary>
    [Fact]
    public void AFaceIndexingAMissingVertexIsRefused()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new Mesh([new Point3d(0, 0, 0)], [new MeshFace(0, 1, 2)]));

        Assert.Contains("indexes vertex", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A per-vertex channel of the wrong length is refused, with both counts.</summary>
    [Fact]
    public void AChannelOfTheWrongLengthIsRefused()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new Mesh(
                [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0)],
                [new MeshFace(0, 1, 2)],
                [Vector3d.ZAxis],
                null,
                null));

        Assert.Contains("one entry per vertex", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The arrays are copied in, so a caller cannot mutate a mesh behind its back.</summary>
    [Fact]
    public void TheArraysAreCopiedIn()
    {
        Point3d[] vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
        Mesh mesh = new(vertices, [new MeshFace(0, 1, 2)]);

        vertices[0] = new Point3d(99, 99, 99);

        Assert.Equal(new Point3d(0, 0, 0), mesh.Vertex(0));
    }

    /// <summary>And copied out, for the same reason.</summary>
    [Fact]
    public void TheArraysAreCopiedOut()
    {
        Mesh mesh = Triangle();
        Point3d[] copy = mesh.Vertices();

        copy[0] = new Point3d(99, 99, 99);

        Assert.Equal(new Point3d(0, 0, 0), mesh.Vertex(0));
    }

    // -- Measurement ---------------------------------------------------------------------------

    /// <summary>A unit cube's surface area is six.</summary>
    [Fact]
    public void ACubesAreaIsSix() => Assert.Equal(6.0, Cube().Area, 1e-12);

    /// <summary>A unit cube encloses a volume of one.</summary>
    [Fact]
    public void ACubesVolumeIsOne() => Assert.Equal(1.0, Cube().Volume(), 1e-12);

    /// <summary>
    /// <b>The volume is signed, and the sign is the useful part.</b> A cube wound inwards reports
    /// −1, which is the cheapest reliable way to notice a mesh that will shade inside-out.
    /// </summary>
    [Fact]
    public void AnInsideOutCubeHasANegativeVolume()
    {
        Mesh flipped = new(
            Cube().Vertices(),
            [.. Cube().Faces().Select(face => new MeshFace(face.D, face.C, face.B, face.A))]);

        Assert.Equal(-1.0, flipped.Volume(), 1e-12);
    }

    /// <summary>
    /// <b>Triangulating changes nothing that is measurable.</b> Same area, same volume, same
    /// vertices — which is only true because a quad is measured across the same diagonal it splits
    /// on.
    /// </summary>
    [Fact]
    public void TriangulatingPreservesEveryMeasurement()
    {
        Mesh cube = Cube();
        Mesh triangles = cube.Triangulated();

        Assert.Equal(12, triangles.FaceCount);
        Assert.Equal(0, triangles.QuadCount);
        Assert.Equal(cube.VertexCount, triangles.VertexCount);
        Assert.Equal(cube.Area, triangles.Area, 1e-12);
        Assert.Equal(cube.Volume(), triangles.Volume(), 1e-12);
    }

    /// <summary>A mesh with no quads triangulates to itself, without copying.</summary>
    [Fact]
    public void ATriangleMeshTriangulatesToItself()
    {
        Mesh mesh = Triangle();

        Assert.Same(mesh, mesh.Triangulated());
    }

    /// <summary>The bounding box is the box around the vertices.</summary>
    [Fact]
    public void TheBoundingBoxHoldsEveryVertex()
    {
        BoundingBox box = Cube().BoundingBox;

        Assert.Equal(0.0, box.Min.X, 1e-12);
        Assert.Equal(1.0, box.Max.Z, 1e-12);
    }

    // -- Normals -------------------------------------------------------------------------------

    /// <summary>A cube's face normals point outwards, which is what its winding says.</summary>
    [Fact]
    public void ACubesFaceNormalsPointOutwards()
    {
        Mesh cube = Cube();

        Assert.Equal(-Vector3d.ZAxis, cube.FaceNormal(0));
        Assert.Equal(Vector3d.ZAxis, cube.FaceNormal(1));
        Assert.Equal(-Vector3d.YAxis, cube.FaceNormal(2));
    }

    /// <summary>
    /// <b>A quad's normal is Newell's, so it does not depend on which corner the winding starts
    /// from.</b> The first-three-corners cross product does, and on a warped quad it can flip.
    /// </summary>
    [Fact]
    public void AWarpedQuadsNormalDoesNotDependOnWhereItStarts()
    {
        Point3d[] vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0.5), new(0, 1, 0)];

        Mesh one = new(vertices, [new MeshFace(0, 1, 2, 3)]);
        Mesh other = new(vertices, [new MeshFace(1, 2, 3, 0)]);

        Assert.Equal(one.FaceNormal(0).X, other.FaceNormal(0).X, 1e-12);
        Assert.Equal(one.FaceNormal(0).Y, other.FaceNormal(0).Y, 1e-12);
        Assert.Equal(one.FaceNormal(0).Z, other.FaceNormal(0).Z, 1e-12);
    }

    /// <summary>A degenerate face has no normal, and says so with a zero vector.</summary>
    [Fact]
    public void ADegenerateFaceHasNoNormal()
    {
        Mesh mesh = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(2, 0, 0)],
            [new MeshFace(0, 1, 2)]);

        Assert.Equal(Vector3d.Zero, mesh.FaceNormal(0));
    }

    /// <summary>Vertex normals are averaged from the faces around each vertex.</summary>
    [Fact]
    public void VertexNormalsAreAveragedFromTheFaces()
    {
        Mesh cube = Cube().WithVertexNormals();

        Assert.True(cube.HasNormals);

        // The corner at (1,1,1) is shared by three faces, so its normal is the diagonal.
        Vector3d corner = cube.Normals()![6];
        Vector3d diagonal = new Vector3d(1, 1, 1).Normalised();

        Assert.Equal(1.0, corner.Dot(diagonal), 1e-9);
    }

    /// <summary>A mesh that already has normals keeps them rather than recomputing.</summary>
    [Fact]
    public void AMeshWithNormalsKeepsThem()
    {
        Mesh mesh = Cube().WithVertexNormals();

        Assert.Same(mesh, mesh.WithVertexNormals());
    }

    /// <summary>
    /// <b>Normals survive a non-uniform scale perpendicular to the surface</b>, which they do not
    /// if they are transformed like positions. Squash a cube flat in z and its top face's normal is
    /// still straight up — transformed as a position it would tilt.
    /// </summary>
    [Fact]
    public void NormalsAreTransformedByTheInverseTranspose()
    {
        Mesh mesh = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 1)],
            [new MeshFace(0, 1, 2)],
            [new Vector3d(0, -1, 1).Normalised(), new Vector3d(0, -1, 1).Normalised(), new Vector3d(0, -1, 1).Normalised()],
            null,
            null);

        Mesh squashed = mesh.TransformedBy(Transform.Scale(1.0, 1.0, 0.25));

        // The face's own normal, recomputed from the squashed geometry, is what the per-vertex
        // normal should still agree with.
        Assert.Equal(1.0, squashed.Normals()![0].Dot(squashed.FaceNormal(0)), 1e-9);
    }

    // -- Topology ------------------------------------------------------------------------------

    /// <summary>A cube is closed, manifold and consistently wound.</summary>
    [Fact]
    public void ACubeIsClosedAndManifold()
    {
        MeshTopology topology = Cube().Topology;

        Assert.True(topology.IsClosed);
        Assert.True(topology.IsManifold);
        Assert.True(topology.IsConsistentlyWound);
        Assert.Equal(12, topology.EdgeCount);
        Assert.Equal(24, topology.HalfedgeCount);
        Assert.Empty(topology.NakedEdges());
    }

    /// <summary>The adjacency is built once and kept, which is what makes it worth being lazy.</summary>
    [Fact]
    public void TheTopologyIsBuiltOnceAndKept()
    {
        Mesh cube = Cube();

        Assert.Same(cube.Topology, cube.Topology);
    }

    /// <summary>A single triangle is open, and every one of its edges is naked.</summary>
    [Fact]
    public void ALoneTriangleIsOpen()
    {
        MeshTopology topology = Triangle().Topology;

        Assert.False(topology.IsClosed);
        Assert.Equal(3, topology.NakedEdgeCount);
        Assert.Equal(3, topology.EdgeCount);
        Assert.Equal(3, topology.NakedEdges().Length);
    }

    /// <summary>
    /// <b>A cube with a face removed has exactly four naked edges, and they are the hole.</b> This
    /// is the diagnostic the structure exists for.
    /// </summary>
    [Fact]
    public void AHoleShowsUpAsNakedEdges()
    {
        Mesh holed = new(Cube().Vertices(), [.. Cube().Faces().Skip(1)]);
        MeshTopology topology = holed.Topology;

        Assert.False(topology.IsClosed);
        Assert.Equal(4, topology.NakedEdgeCount);

        // And they form the boundary of the face that was taken away.
        int[] onTheHole = [.. topology.NakedEdges().SelectMany(edge => new[] { edge.From, edge.To }).Distinct().Order()];

        Assert.Equal([0, 1, 2, 3], onTheHole);
    }

    /// <summary>
    /// <b>A face wound the wrong way is caught</b>, because its halfedges run the same way as its
    /// neighbours' rather than the opposite way. A closed mesh can still be inconsistently wound,
    /// which is why this is a separate question from being closed.
    /// </summary>
    [Fact]
    public void AFaceWoundBackwardsIsCaught()
    {
        MeshFace[] faces = Cube().Faces();
        faces[1] = new MeshFace(faces[1].D, faces[1].C, faces[1].B, faces[1].A);

        MeshTopology topology = new Mesh(Cube().Vertices(), faces).Topology;

        Assert.False(topology.IsConsistentlyWound);
        Assert.False(topology.IsClosed);
    }

    /// <summary>
    /// <b>Three faces on one edge is described rather than refused.</b> It is malformed, and it is
    /// also what a careless boolean produces — a kernel that would not build the adjacency would
    /// leave the caller no way to find it.
    /// </summary>
    [Fact]
    public void ThreeFacesOnOneEdgeAreCounted()
    {
        Mesh fin = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0), new Point3d(0, 0, 1), new Point3d(0, -1, 0)],
            [new MeshFace(0, 1, 2), new MeshFace(1, 0, 3), new MeshFace(0, 1, 4)]);

        MeshTopology topology = fin.Topology;

        Assert.False(topology.IsManifold);
        Assert.Equal(1, topology.NonManifoldEdgeCount);
    }

    /// <summary>Each face of a cube has four neighbours.</summary>
    [Fact]
    public void EachCubeFaceHasFourNeighbours()
    {
        MeshTopology topology = Cube().Topology;

        for (int face = 0; face < 6; face++)
        {
            Assert.Equal(4, topology.AdjacentFaces(face).Length);
        }
    }

    /// <summary>Each corner of a cube is shared by three faces.</summary>
    [Fact]
    public void EachCubeVertexHasThreeFaces()
    {
        MeshTopology topology = Cube().Topology;

        for (int vertex = 0; vertex < 8; vertex++)
        {
            Assert.Equal(3, topology.FacesAroundVertex(vertex).Length);
        }
    }

    /// <summary>Triangulating a closed mesh keeps it closed, with more edges and the same volume.</summary>
    [Fact]
    public void ATriangulatedCubeIsStillClosed()
    {
        MeshTopology topology = Cube().Triangulated().Topology;

        Assert.True(topology.IsClosed);
        Assert.Equal(18, topology.EdgeCount);
    }
}

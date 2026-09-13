using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="MeshTopology.Edges"/> and <see cref="Mesh.TriangleCentroids"/> — `E2-T69`'s queries.
/// </summary>
/// <remarks>
/// <para>
/// <b>For <c>Edges</c> the branch is the count, not the contents.</b> An implementation returning
/// every halfedge hands back real edges, with the right endpoints, in a sensible order — and each
/// interior one <i>twice</i>. Every test asking <i>are these edges</i> passes on it, so the claims
/// here are that the count agrees with <see cref="MeshTopology.EdgeCount"/>, computed a different
/// way, and that no pair appears again in either direction.
/// </para>
/// <para>
/// <b>For <c>TriangleCentroids</c> the branch is the quad</b>, since Spark's faces are not all
/// triangles: an implementation ignoring the fourth corner returns a point that is plausible,
/// inside the face, and wrong. The quad used here has its fourth corner nowhere near the other
/// three, so the value separates the two.
/// </para>
/// </remarks>
public sealed class MeshQueryTests
{
    private const double Tight = 1e-9;

    /// <summary>
    /// <b>The branch.</b> On a closed mesh the edge count agrees with <c>EdgeCount</c> — which
    /// counts twins rather than walking — so the two agreeing is evidence and not a tautology.
    /// </summary>
    [Fact]
    public void EveryEdgeAppearsOnceOnAClosedMesh()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 12, 6);

        MeshTopology topology = sphere.Topology;
        (int From, int To)[] edges = topology.Edges();

        Assert.True(topology.IsClosed, "this test needs a closed mesh to be worth anything.");
        Assert.Equal(topology.EdgeCount, edges.Length);
        Assert.True(
            edges.Length < topology.HalfedgeCount,
            $"a closed mesh has fewer edges ({edges.Length}) than halfedges ({topology.HalfedgeCount}).");
    }

    /// <summary>No edge is returned twice, in either direction.</summary>
    [Theory]
    [MemberData(nameof(Meshes))]
    public void NoEdgeIsReturnedTwice(Mesh mesh)
    {
        HashSet<(int, int)> seen = [];

        foreach ((int from, int to) in mesh.Topology.Edges())
        {
            Assert.True(seen.Add((Math.Min(from, to), Math.Max(from, to))), $"the edge {from}-{to} came back twice.");
        }
    }

    /// <summary>The count agrees with <c>EdgeCount</c> on open meshes too, where twins are scarcer.</summary>
    [Theory]
    [MemberData(nameof(Meshes))]
    public void TheCountAgreesWithEdgeCount(Mesh mesh)
    {
        Assert.Equal(mesh.Topology.EdgeCount, mesh.Topology.Edges().Length);
    }

    /// <summary>Every edge names two different, real vertices.</summary>
    [Theory]
    [MemberData(nameof(Meshes))]
    public void EveryEdgeNamesTwoRealVertices(Mesh mesh)
    {
        foreach ((int from, int to) in mesh.Topology.Edges())
        {
            Assert.InRange(from, 0, mesh.VertexCount - 1);
            Assert.InRange(to, 0, mesh.VertexCount - 1);
            Assert.NotEqual(from, to);
        }
    }

    /// <summary>Every boundary edge is among the edges, which a walk that skipped unpaired ones would miss.</summary>
    [Fact]
    public void TheBoundaryEdgesAreAmongThem()
    {
        Mesh grid = MeshPrimitives.Plane(Plane.WorldXY, 2.0, 2.0, 3, 3);

        HashSet<(int, int)> edges =
            [.. grid.Topology.Edges().Select(edge => (Math.Min(edge.From, edge.To), Math.Max(edge.From, edge.To)))];

        foreach ((int from, int to) in grid.Topology.NakedEdges())
        {
            Assert.Contains((Math.Min(from, to), Math.Max(from, to)), edges);
        }
    }

    /// <summary>One centroid per face, in face order.</summary>
    [Theory]
    [MemberData(nameof(Meshes))]
    public void ThereIsOneCentroidPerFaceInFaceOrder(Mesh mesh)
    {
        Point3d[] centroids = mesh.TriangleCentroids();

        Assert.Equal(mesh.FaceCount, centroids.Length);

        for (int index = 0; index < mesh.FaceCount; index++)
        {
            MeshFace face = mesh.Face(index);
            Point3d expected = Average(mesh, face);

            Assert.True(centroids[index].DistanceTo(expected) < Tight, $"face {index}'s centroid is wrong.");
        }
    }

    /// <summary>A triangle's centroid is the average of its three corners, computed by hand.</summary>
    [Fact]
    public void ATrianglesCentroidIsTheAverageOfItsCorners()
    {
        Mesh triangle = new(
            [new Point3d(0, 0, 0), new Point3d(3, 0, 0), new Point3d(0, 6, 3)],
            [new MeshFace(0, 1, 2)]);

        Point3d centroid = Assert.Single(triangle.TriangleCentroids());

        Assert.Equal(1.0, centroid.X, Tight);
        Assert.Equal(2.0, centroid.Y, Tight);
        Assert.Equal(1.0, centroid.Z, Tight);
    }

    /// <summary>
    /// <b>The quad branch.</b> The fourth corner is far from the other three, so an implementation
    /// that ignored it would return (1, 2, 0) — a plausible point inside the face, and the wrong
    /// answer.
    /// </summary>
    [Fact]
    public void AQuadsCentroidUsesAllFourCorners()
    {
        Mesh quad = new(
            [new Point3d(0, 0, 0), new Point3d(3, 0, 0), new Point3d(0, 6, 0), new Point3d(9, 10, 20)],
            [new MeshFace(0, 1, 2, 3)]);

        Point3d centroid = Assert.Single(quad.TriangleCentroids());

        Assert.Equal(3.0, centroid.X, Tight);
        Assert.Equal(4.0, centroid.Y, Tight);
        Assert.Equal(5.0, centroid.Z, Tight);

        // What ignoring the fourth corner would have given.
        Assert.True(centroid.DistanceTo(new Point3d(1.0, 2.0, 0.0)) > 1.0);
    }

    /// <summary>Every centroid lies inside its face's bounding box, on every mesh in the set.</summary>
    [Theory]
    [MemberData(nameof(Meshes))]
    public void EveryCentroidIsInsideItsFace(Mesh mesh)
    {
        Point3d[] centroids = mesh.TriangleCentroids();

        for (int index = 0; index < mesh.FaceCount; index++)
        {
            MeshFace face = mesh.Face(index);
            List<Point3d> corners = [];

            for (int corner = 0; corner < face.Count; corner++)
            {
                corners.Add(mesh.Vertex(face[corner]));
            }

            Assert.InRange(centroids[index].X, corners.Min(p => p.X) - Tight, corners.Max(p => p.X) + Tight);
            Assert.InRange(centroids[index].Y, corners.Min(p => p.Y) - Tight, corners.Max(p => p.Y) + Tight);
            Assert.InRange(centroids[index].Z, corners.Min(p => p.Z) - Tight, corners.Max(p => p.Z) + Tight);
        }
    }

    private static Point3d Average(Mesh mesh, MeshFace face)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;

        for (int corner = 0; corner < face.Count; corner++)
        {
            Point3d point = mesh.Vertex(face[corner]);

            x += point.X;
            y += point.Y;
            z += point.Z;
        }

        return new Point3d(x / face.Count, y / face.Count, z / face.Count);
    }

    /// <summary>Closed, open, quad-only and mixed — the four shapes these members behave differently on.</summary>
    public static TheoryData<Mesh> Meshes() =>
    [
        MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 10, 5),
        MeshPrimitives.Plane(Plane.WorldXY, 2.0, 3.0, 3, 2),
        MeshPrimitives.Cuboid(Plane.WorldXY, 1.0, 2.0, 3.0, 2, 2, 2),
        MeshPrimitives.Cone(Plane.WorldXY, 1.0, 0.4, 2.0, 8),
    ];
}

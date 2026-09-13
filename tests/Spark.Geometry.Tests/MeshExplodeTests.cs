using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh.Explode"/> — `E2-T68`'s first member.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the renumbering, not the split.</b> A piece that kept the original vertex array
/// would have the right faces, the right geometry, and would render identically — the only thing
/// that sees it is the vertex count, so that is what these tests assert.
/// </para>
/// <para>
/// <b>And the definition of <i>connected</i> is pinned rather than discovered</b>: two pieces
/// meeting at a single vertex stay two, because connectedness here means sharing an edge.
/// </para>
/// </remarks>
public sealed class MeshExplodeTests
{
    /// <summary>A mesh already in one piece comes back as itself, without being rebuilt.</summary>
    [Fact]
    public void AMeshInOnePieceComesBackAsItself()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 8, 4);

        Mesh piece = Assert.Single(sphere.Explode());

        Assert.Same(sphere, piece);
    }

    /// <summary>An empty mesh explodes to nothing rather than to one empty piece.</summary>
    [Fact]
    public void AnEmptyMeshExplodesToNothing()
    {
        Assert.Empty(new Mesh([], []).Explode());
    }

    /// <summary>
    /// <b>The count.</b> Two separated grids give two pieces — an implementation returning one piece
    /// per face would pass any test that only checked the geometry.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void SeparatedPiecesAreCounted(int count)
    {
        Mesh scattered = Scattered(count);

        Assert.Equal(count, scattered.Explode().Length);
    }

    /// <summary>
    /// <b>The branch.</b> Each piece carries only the vertices its own faces use — the one number a
    /// piece that kept the original array would get wrong.
    /// </summary>
    [Fact]
    public void EachPieceCarriesOnlyItsOwnVertices()
    {
        Mesh scattered = Scattered(4);

        Assert.Equal(16, scattered.VertexCount);

        foreach (Mesh piece in scattered.Explode())
        {
            HashSet<int> used = [];

            foreach (MeshFace face in piece.Faces())
            {
                for (int corner = 0; corner < face.Count; corner++)
                {
                    used.Add(face[corner]);
                }
            }

            Assert.Equal(used.Count, piece.VertexCount);
            Assert.Equal(4, piece.VertexCount);
        }
    }

    /// <summary>Nothing is lost: the pieces' faces sum to the original's, and the geometry survives.</summary>
    [Fact]
    public void NothingIsLostInTheSplit()
    {
        Mesh scattered = Scattered(3);

        Mesh[] pieces = scattered.Explode();

        Assert.Equal(scattered.FaceCount, pieces.Sum(piece => piece.FaceCount));
        Assert.Equal(scattered.VertexCount, pieces.Sum(piece => piece.VertexCount));

        // Every original face's centroid appears among the pieces' centroids.
        List<Point3d> original = [.. scattered.TriangleCentroids()];
        List<Point3d> split = [.. pieces.SelectMany(piece => piece.TriangleCentroids())];

        foreach (Point3d centroid in original)
        {
            Assert.Contains(split, other => other.DistanceTo(centroid) < 1e-9);
        }
    }

    /// <summary>
    /// <b>The decision, pinned.</b> Two grids meeting at a single shared vertex are two pieces,
    /// because connected means sharing an edge.
    /// </summary>
    [Fact]
    public void TwoPiecesMeetingAtOneVertexStayTwo()
    {
        // Two triangles that share vertex 2 and nothing else.
        Mesh pinched = new(
            [
                new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0),
                new Point3d(2, 1, 0), new Point3d(2, 2, 0),
            ],
            [new MeshFace(0, 1, 2), new MeshFace(2, 3, 4)]);

        Assert.Equal(2, pinched.Explode().Length);
    }

    /// <summary>Two faces sharing an edge are one piece, which is the other half of that decision.</summary>
    [Fact]
    public void TwoFacesSharingAnEdgeAreOnePiece()
    {
        Mesh joined = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0), new Point3d(0, 1, 0)],
            [new MeshFace(0, 1, 2), new MeshFace(0, 2, 3)]);

        Assert.Single(joined.Explode());
    }

    /// <summary>A piece's faces are still valid: every index is inside its own vertex array.</summary>
    [Fact]
    public void EveryPiecesFacesIndexItsOwnVertices()
    {
        foreach (Mesh piece in Scattered(3).Explode())
        {
            foreach (MeshFace face in piece.Faces())
            {
                for (int corner = 0; corner < face.Count; corner++)
                {
                    Assert.InRange(face[corner], 0, piece.VertexCount - 1);
                }
            }
        }
    }

    /// <summary>The optional channels travel with their vertices rather than being dropped.</summary>
    [Fact]
    public void TheChannelsTravelWithTheirVertices()
    {
        Mesh coloured = new(
            [
                new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0),
                new Point3d(5, 5, 0), new Point3d(6, 5, 0), new Point3d(6, 6, 0),
            ],
            [new MeshFace(0, 1, 2), new MeshFace(3, 4, 5)],
            [
                Vector3d.ZAxis, Vector3d.ZAxis, Vector3d.ZAxis,
                -Vector3d.ZAxis, -Vector3d.ZAxis, -Vector3d.ZAxis,
            ],
            null,
            [0xFF0000FFu, 0xFF0000FFu, 0xFF0000FFu, 0x00FF00FFu, 0x00FF00FFu, 0x00FF00FFu]);

        Mesh[] pieces = coloured.Explode();

        Assert.Equal(2, pieces.Length);

        foreach (Mesh piece in pieces)
        {
            Assert.True(piece.HasNormals);
            Assert.True(piece.HasColours);
            Assert.False(piece.HasTextureCoordinates);
            Vector3d[] normals = piece.Normals() ?? [];
            uint[] colours = piece.Colours() ?? [];

            Assert.Equal(3, normals.Length);
            Assert.Equal(3, colours.Length);

            // Each piece's three vertices had one normal and one colour between them.
            Assert.Single(normals.Distinct());
            Assert.Single(colours.Distinct());
        }
    }

    /// <summary>Several separated unit grids, each four vertices and one quad, spaced well apart.</summary>
    private static Mesh Scattered(int count)
    {
        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        for (int index = 0; index < count; index++)
        {
            double offset = index * 10.0;
            int first = vertices.Count;

            vertices.Add(new Point3d(offset, 0, 0));
            vertices.Add(new Point3d(offset + 1, 0, 0));
            vertices.Add(new Point3d(offset + 1, 1, 0));
            vertices.Add(new Point3d(offset, 1, 0));

            faces.Add(new MeshFace(first, first + 1, first + 2, first + 3));
        }

        return new Mesh(vertices, faces);
    }
}

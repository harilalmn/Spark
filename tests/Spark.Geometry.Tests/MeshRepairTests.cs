using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh.Repair"/> — `E2-T68`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the renumbering, and it is the one that fails quietly.</b> Dropping a vertex
/// from the middle of the list shifts every index above it: faces that are not renumbered point at
/// a <i>different</i> vertex, which is a valid index, a mesh that loads, and geometry that is
/// wrong. So the assertions compare the surviving faces by <b>coordinate</b> rather than by index —
/// an index-based test cannot see this defect at all.
/// </para>
/// <para>
/// <b>Each kind of rubbish gets its own test</b>, so that a failure names which pass broke rather
/// than reporting that repair is broken.
/// </para>
/// </remarks>
public sealed class MeshRepairTests
{
    /// <summary>A clean mesh comes back unchanged, which is what stops repair being a rebuild.</summary>
    [Fact]
    public void ACleanMeshIsUnchanged()
    {
        Mesh clean = MeshPrimitives.Sphere(Plane.WorldXY, 3.0, 12, 6);

        Mesh repaired = clean.Repair();

        Assert.Same(clean, repaired);
    }

    /// <summary>A face naming the same vertex twice encloses nothing and goes.</summary>
    [Fact]
    public void AFaceWithARepeatedVertexIsDropped()
    {
        Mesh mesh = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0)],
            [new MeshFace(0, 1, 2), new MeshFace(0, 1, 1)],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(1, repaired.FaceCount);
    }

    /// <summary>A face whose corners are collinear has no area and goes too.</summary>
    [Fact]
    public void AFaceWithNoAreaIsDropped()
    {
        Mesh mesh = new(
            [
                new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0),
                new Point3d(5, 0, 0), new Point3d(6, 0, 0), new Point3d(7, 0, 0),
            ],
            [new MeshFace(0, 1, 2), new MeshFace(3, 4, 5)],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(1, repaired.FaceCount);
        Assert.Equal(3, repaired.VertexCount);
    }

    /// <summary>Two faces on the same vertices are one face, whichever way round they are wound.</summary>
    [Fact]
    public void ADuplicateFaceIsDroppedWhicheverWayItIsWound()
    {
        Mesh mesh = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0)],
            [new MeshFace(0, 1, 2), new MeshFace(0, 2, 1), new MeshFace(1, 2, 0)],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(1, repaired.FaceCount);
    }

    /// <summary>
    /// <b>The branch.</b> An unused vertex in the <i>middle</i> of the list is dropped and
    /// everything above it renumbered — so the surviving face still names the same three points.
    /// </summary>
    [Fact]
    public void AnUnusedVertexInTheMiddleIsDroppedAndTheRestRenumbered()
    {
        // Vertex 1 is used by nothing. The face uses 0, 2 and 3 — which after the drop must become
        // 0, 1 and 2 while still meaning the same coordinates.
        Point3d[] wanted = [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0)];

        Mesh mesh = new(
            [wanted[0], new Point3d(99, 99, 99), wanted[1], wanted[2]],
            [new MeshFace(0, 2, 3)],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(3, repaired.VertexCount);
        Assert.Equal(1, repaired.FaceCount);

        Point3d[] vertices = repaired.Vertices();
        MeshFace face = repaired.Faces()[0];

        for (int corner = 0; corner < 3; corner++)
        {
            Assert.True(
                vertices[face[corner]].DistanceTo(wanted[corner]) < 1e-12,
                $"corner {corner} is at {vertices[face[corner]]}, not at {wanted[corner]} — the "
                + "face was not renumbered when the unused vertex was dropped.");
        }
    }

    /// <summary>
    /// And with several holes in the vertex list at once, so a one-off shift cannot fake it.
    /// </summary>
    [Fact]
    public void SeveralUnusedVerticesAreDroppedTogether()
    {
        Point3d[] used = [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0)];

        Mesh mesh = new(
            [
                new Point3d(50, 50, 50),
                used[0],
                new Point3d(60, 60, 60),
                used[1],
                new Point3d(70, 70, 70),
                used[2],
                new Point3d(80, 80, 80),
            ],
            [new MeshFace(1, 3, 5)],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(3, repaired.VertexCount);

        Point3d[] vertices = repaired.Vertices();
        MeshFace face = repaired.Faces()[0];

        for (int corner = 0; corner < 3; corner++)
        {
            Assert.True(
                vertices[face[corner]].DistanceTo(used[corner]) < 1e-12,
                $"corner {corner} is at {vertices[face[corner]]}, not at {used[corner]}.");
        }
    }

    /// <summary>
    /// <b>The same bug one level down.</b> The channels have to move with the vertices, or a repair
    /// that renumbers the faces correctly still hands back the wrong normal at every corner.
    /// </summary>
    [Fact]
    public void TheChannelsMoveWithTheVertices()
    {
        Vector3d[] normals =
        [
            new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(0, 0, 1),
        ];
        uint[] colours = [111u, 222u, 333u, 444u];

        Mesh mesh = new(
            [
                new Point3d(0, 0, 0), new Point3d(99, 99, 99),
                new Point3d(4, 0, 0), new Point3d(0, 4, 0),
            ],
            [new MeshFace(0, 2, 3)],
            normals,
            null,
            colours);

        Mesh repaired = mesh.Repair();

        Vector3d[] moved = repaired.Normals()!;
        uint[] recoloured = repaired.Colours()!;

        Assert.Equal(3, moved.Length);
        Assert.Equal(3, recoloured.Length);

        // The kept vertices were 0, 2 and 3, so their channels are the first, third and fourth.
        Assert.True((moved[0] - normals[0]).Length < 1e-12);
        Assert.True((moved[1] - normals[2]).Length < 1e-12);
        Assert.True((moved[2] - normals[3]).Length < 1e-12);
        Assert.Equal(111u, recoloured[0]);
        Assert.Equal(333u, recoloured[1]);
        Assert.Equal(444u, recoloured[2]);
    }

    /// <summary>The passes run in an order where each one's leavings feed the next.</summary>
    [Fact]
    public void AMeshWithEveryKindOfRubbishComesBackClean()
    {
        Point3d[] wanted = [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0)];

        Mesh mesh = new(
            [
                wanted[0], wanted[1], wanted[2],
                new Point3d(9, 0, 0),      // used only by a degenerate face
                new Point3d(10, 0, 0),
                new Point3d(11, 0, 0),
                new Point3d(77, 77, 77),   // used by nothing at all
            ],
            [
                new MeshFace(0, 1, 2),
                new MeshFace(0, 2, 1),     // duplicate, other winding
                new MeshFace(0, 1, 1),     // repeated vertex
                new MeshFace(3, 4, 5),     // collinear
            ],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(1, repaired.FaceCount);
        Assert.Equal(3, repaired.VertexCount);

        Point3d[] vertices = repaired.Vertices();
        MeshFace face = repaired.Faces()[0];

        for (int corner = 0; corner < 3; corner++)
        {
            Assert.True(
                vertices[face[corner]].DistanceTo(wanted[corner]) < 1e-12,
                $"corner {corner} is at {vertices[face[corner]]}, not at {wanted[corner]}.");
        }
    }

    /// <summary>A quad with three collinear corners is still a triangle and still a surface.</summary>
    [Fact]
    public void AQuadIsOnlyDegenerateWhenBothOfItsTrianglesAre()
    {
        Mesh mesh = new(
            [
                new Point3d(0, 0, 0), new Point3d(2, 0, 0), new Point3d(4, 0, 0),
                new Point3d(0, 4, 0),
            ],
            [new MeshFace(0, 1, 2, 3)],
            null,
            null,
            null);

        Assert.Equal(1, mesh.Repair().FaceCount);
    }

    /// <summary>A mesh of nothing but rubbish comes back with no faces rather than throwing.</summary>
    [Fact]
    public void AMeshOfNothingButRubbishComesBackEmpty()
    {
        Mesh mesh = new(
            [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(2, 0, 0)],
            [new MeshFace(0, 1, 2), new MeshFace(0, 0, 1)],
            null,
            null,
            null);

        Mesh repaired = mesh.Repair();

        Assert.Equal(0, repaired.FaceCount);
        Assert.Equal(0, repaired.VertexCount);
    }
}

using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// The four mesh primitives — `E2-T69`'s first family.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the seam, not the shape.</b> A sphere whose last column of vertices duplicates
/// its first has every vertex in the right place, the right face count, and renders identically —
/// and is two sheets that meet nowhere. <see cref="MeshTopology.IsClosed"/> is the only thing that
/// notices, so it is the claim these tests are built on; no assertion about positions can stand in
/// for it.
/// </para>
/// <para>
/// <b>And every primitive is checked against its own analytic truth</b>: every sphere vertex at the
/// radius, every cuboid vertex on the box, the cone's apex a single point. Those are answers the
/// construction does not supply.
/// </para>
/// </remarks>
public sealed class MeshPrimitiveTests
{
    private const double Tight = 1e-9;

    /// <summary><b>The branch.</b> A sphere is closed, which a duplicated seam would break.</summary>
    [Theory]
    [InlineData(3, 2)]
    [InlineData(8, 4)]
    [InlineData(16, 8)]
    [InlineData(32, 17)]
    public void ASphereIsClosed(int divisions, int stacks)
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 2.0, divisions, stacks);

        MeshTopology topology = sphere.Topology;

        Assert.True(topology.IsManifold, $"{divisions}x{stacks}: the sphere is not manifold.");
        Assert.True(
            topology.IsClosed,
            $"{divisions}x{stacks}: the sphere has {topology.NakedEdgeCount} naked edges, so its seam or its poles are split.");
    }

    /// <summary>Every vertex of a sphere is at the radius from its centre.</summary>
    [Fact]
    public void EverySphereVertexIsAtTheRadius()
    {
        Point3d centre = new(1.0, -2.0, 3.0);
        Mesh sphere = MeshPrimitives.Sphere(Plane.FromOriginNormal(centre, new Vector3d(1, 1, 1)), 2.5, 12, 6);

        foreach (Point3d vertex in sphere.Vertices())
        {
            Assert.Equal(2.5, vertex.DistanceTo(centre), 1e-9);
        }
    }

    /// <summary>
    /// <b>The poles are triangles and everything between them is quads</b>, which is the topology the
    /// geometry actually has — a count of the total would not see it.
    /// </summary>
    [Fact]
    public void ASphereIsTrianglesAtThePolesAndQuadsBetween()
    {
        const int divisions = 10;
        const int stacks = 5;

        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, divisions, stacks);

        int triangles = sphere.Faces().Count(face => !face.IsQuad);
        int quads = sphere.Faces().Count(face => face.IsQuad);

        Assert.Equal(2 * divisions, triangles);
        Assert.Equal((stacks - 2) * divisions, quads);
    }

    /// <summary>
    /// <b>The subdivision counts have to matter.</b> More divisions gives more faces, in the ratio
    /// the grid implies — which an implementation ignoring its arguments would fail.
    /// </summary>
    [Fact]
    public void MoreDivisionsGivesMoreFaces()
    {
        int coarse = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 8, 4).FaceCount;
        int fine = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 16, 8).FaceCount;

        Assert.Equal(8 * 4, coarse);
        Assert.Equal(16 * 8, fine);
    }

    /// <summary>A plane grid has the faces and vertices its divisions imply, and lies in its plane.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(7, 5)]
    public void APlaneGridHasTheFacesItsDivisionsImply(int x, int y)
    {
        Mesh grid = MeshPrimitives.Plane(Plane.WorldXY, 4.0, 6.0, x, y);

        Assert.Equal(x * y, grid.FaceCount);
        Assert.Equal((x + 1) * (y + 1), grid.VertexCount);
        Assert.All(grid.Vertices(), vertex => Assert.Equal(0.0, vertex.Z, Tight));
        Assert.All(grid.Faces(), face => Assert.True(face.IsQuad, "a plane grid is quads."));
    }

    /// <summary>The plane grid spans exactly the width and length asked for, centred on the origin.</summary>
    [Fact]
    public void APlaneGridSpansTheSizeAskedFor()
    {
        Mesh grid = MeshPrimitives.Plane(Plane.WorldXY, 4.0, 6.0, 3, 3);

        BoundingBox box = grid.BoundingBox;

        Assert.Equal(-2.0, box.Min.X, Tight);
        Assert.Equal(2.0, box.Max.X, Tight);
        Assert.Equal(-3.0, box.Min.Y, Tight);
        Assert.Equal(3.0, box.Max.Y, Tight);
    }

    /// <summary>A cuboid has six grids' worth of faces and spans the box asked for.</summary>
    [Fact]
    public void ACuboidHasSixGridsAndSpansItsBox()
    {
        Mesh box = MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 4.0, 6.0, 2, 3, 4);

        // Two faces per pair of axes: (x,y) twice, (z,x) twice, (y,z) twice.
        Assert.Equal((2 * 2 * 3) + (2 * 4 * 2) + (2 * 3 * 4), box.FaceCount);

        BoundingBox bounds = box.BoundingBox;
        Assert.Equal(-1.0, bounds.Min.X, Tight);
        Assert.Equal(1.0, bounds.Max.X, Tight);
        Assert.Equal(-2.0, bounds.Min.Y, Tight);
        Assert.Equal(2.0, bounds.Max.Y, Tight);
        Assert.Equal(-3.0, bounds.Min.Z, Tight);
        Assert.Equal(3.0, bounds.Max.Z, Tight);
    }

    /// <summary>
    /// Every cuboid vertex is on the surface of the box: one of its three coordinates is at an
    /// extreme. A face placed at the wrong offset would put vertices inside.
    /// </summary>
    [Fact]
    public void EveryCuboidVertexIsOnTheBox()
    {
        Mesh box = MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 4.0, 6.0, 2, 2, 2);

        foreach (Point3d vertex in box.Vertices())
        {
            bool onSurface =
                Math.Abs(Math.Abs(vertex.X) - 1.0) < Tight
                || Math.Abs(Math.Abs(vertex.Y) - 2.0) < Tight
                || Math.Abs(Math.Abs(vertex.Z) - 3.0) < Tight;

            Assert.True(onSurface, $"{vertex} is not on the box's surface.");
        }
    }

    /// <summary>A capped cone's apex is a single vertex, and its wall is triangles because of it.</summary>
    [Fact]
    public void AConeWithAPointHasOneApexVertex()
    {
        const int divisions = 12;

        Mesh cone = MeshPrimitives.Cone(Plane.WorldXY, 1.0, 0.0, 2.0, divisions);

        Point3d apex = new(0.0, 0.0, 2.0);
        Assert.Single(cone.Vertices(), vertex => vertex.DistanceTo(apex) < Tight);

        // The wall is `divisions` triangles; the base cap adds `divisions` more.
        Assert.Equal(2 * divisions, cone.FaceCount);
        Assert.All(cone.Faces(), face => Assert.False(face.IsQuad, "a pointed cone has no quads."));
    }

    /// <summary>A truncated cone's wall is quads, and both ends are capped.</summary>
    [Fact]
    public void ATruncatedConeHasAQuadWallAndTwoCaps()
    {
        const int divisions = 10;

        Mesh frustum = MeshPrimitives.Cone(Plane.WorldXY, 2.0, 1.0, 3.0, divisions);

        Assert.Equal(divisions, frustum.Faces().Count(face => face.IsQuad));
        Assert.Equal(2 * divisions, frustum.Faces().Count(face => !face.IsQuad));

        BoundingBox bounds = frustum.BoundingBox;
        Assert.Equal(0.0, bounds.Min.Z, Tight);
        Assert.Equal(3.0, bounds.Max.Z, Tight);
        Assert.Equal(2.0, bounds.Max.X, 1e-9);
    }

    /// <summary>An uncapped cone is open, and has only its wall.</summary>
    [Fact]
    public void AnUncappedConeIsOpen()
    {
        const int divisions = 8;

        Mesh open = MeshPrimitives.Cone(Plane.WorldXY, 1.0, 0.5, 2.0, divisions, capped: false);

        Assert.Equal(divisions, open.FaceCount);
        Assert.False(open.Topology.IsClosed, "an uncapped cone has two open rings.");
    }

    /// <summary>A capped cone is closed, which its rings sharing their vertices is what achieves.</summary>
    [Fact]
    public void ACappedConeIsClosed()
    {
        Mesh cone = MeshPrimitives.Cone(Plane.WorldXY, 1.0, 0.0, 2.0, 12);

        Assert.True(cone.Topology.IsClosed, "a capped cone should be closed.");
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Plane(Plane.WorldXY, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Plane(Plane.WorldXY, 1.0, 1.0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Cuboid(Plane.WorldXY, 1.0, 1.0, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 8, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Sphere(Plane.WorldXY, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Cone(Plane.WorldXY, 0.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Cone(Plane.WorldXY, -1.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPrimitives.Cone(Plane.WorldXY, 1.0, 0.0, 1.0, 2));
    }
}

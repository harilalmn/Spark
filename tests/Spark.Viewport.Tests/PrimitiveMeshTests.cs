using System;
using System.Collections.Generic;
using System.Numerics;
using Spark.Viewport.Meshes;

namespace Spark.Viewport.Tests;

/// <summary>
/// Watertightness and winding, asserted on the viewport's own primitives.
/// </summary>
/// <remarks>
/// The viewport's contract is that a closed solid arrives watertight — every edge shared by exactly
/// two triangles — and that it reports a violation rather than papering over it, because a hole in
/// a mesh is a kernel defect and hiding it in the renderer means it resurfaces in someone's 3D
/// print instead. A renderer whose own primitives fail that test has no standing to report anyone
/// else's, which is what these tests are for.
/// </remarks>
public sealed class PrimitiveMeshTests
{
    [Fact]
    public void TheBoxIsWatertight() =>
        AssertWatertight(PrimitiveMeshes.Box(new Vector3(-1, -2, -3), new Vector3(4, 5, 6)));

    [Fact]
    public void TheSphereIsWatertight() =>
        AssertWatertight(PrimitiveMeshes.Sphere(Vector3.Zero, 2f, segments: 16, rings: 10));

    [Fact]
    public void EveryBoxTriangleWindsAntiClockwiseSeenFromOutside() =>
        AssertOutwardWinding(PrimitiveMeshes.Box(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)), Vector3.Zero);

    [Fact]
    public void EverySphereTriangleWindsAntiClockwiseSeenFromOutside() =>
        AssertOutwardWinding(
            PrimitiveMeshes.Sphere(new Vector3(2, -1, 0.5f), 3f, segments: 24, rings: 12),
            new Vector3(2, -1, 0.5f));

    [Fact]
    public void BoxNormalsPointAwayFromTheMaterial()
    {
        Mesh mesh = PrimitiveMeshes.Box(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

        for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
        {
            Vector3 position = PositionAt(mesh, vertex);
            Vector3 normal = NormalAt(mesh, vertex);

            Assert.True(
                Vector3.Dot(normal, position) > 0,
                $"Vertex {vertex} at {position} has an inward normal {normal}.");
        }
    }

    [Fact]
    public void SphereNormalsAreUnitLengthAndRadial()
    {
        Vector3 centre = new(1, 2, 3);
        Mesh mesh = PrimitiveMeshes.Sphere(centre, 2.5f, segments: 20, rings: 12);

        for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
        {
            Vector3 normal = NormalAt(mesh, vertex);
            Vector3 radial = Vector3.Normalize(PositionAt(mesh, vertex) - centre);

            Assert.Equal(1.0, normal.Length(), 4);
            Assert.Equal(1.0, Vector3.Dot(normal, radial), 4);
        }
    }

    [Fact]
    public void TheBoxHasTwelveTrianglesAndFlatShadedCorners()
    {
        Mesh mesh = PrimitiveMeshes.Box(Vector3.Zero, Vector3.One);

        Assert.Equal(12, mesh.TriangleCount);

        // Twenty-four vertices, not eight: a box with shared corner vertices has one normal where
        // it needs three, and shades as a rounded lump instead of a box.
        Assert.Equal(24, mesh.VertexCount);
    }

    [Fact]
    public void ASphereRefusesANonPositiveRadius() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveMeshes.Sphere(Vector3.Zero, 0f));

    [Fact]
    public void TheGroundGridLeavesTheCentreLinesToTheAxes()
    {
        LineBatch grid = GroundGrid.Build(halfExtent: 4, spacing: 1f, majorEvery: 2);

        // 4 divisions each side, minus the two centre lines the axes replace, in two directions,
        // plus three axis segments.
        Assert.Equal(((8 * 2) + 3) * 2, grid.VertexCount);
        Assert.Equal(grid.VertexCount * 4, grid.Colours.Length);
    }

    [Fact]
    public void TheGroundGridUsesTheAxisColoursFromTheDesignLanguage()
    {
        LineBatch grid = GroundGrid.Build(halfExtent: 2, spacing: 1f);
        HashSet<ViewportColor> colours = [];

        for (int i = 0; i + 3 < grid.Colours.Length; i += 4)
        {
            colours.Add(new ViewportColor(
                grid.Colours[i], grid.Colours[i + 1], grid.Colours[i + 2], grid.Colours[i + 3]));
        }

        Assert.Contains(ViewportPalette.AxisX, colours);
        Assert.Contains(ViewportPalette.AxisY, colours);
        Assert.Contains(ViewportPalette.AxisZ, colours);
        Assert.Contains(ViewportPalette.GridMinor, colours);
    }

    [Fact]
    public void TheGroundGridRefusesANonPositiveSpacing() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundGrid.Build(10, 0f));

    /// <summary>
    /// Asserts that every edge of a mesh is shared by exactly two triangles, welding vertices by
    /// position first.
    /// </summary>
    /// <param name="mesh">The mesh to check.</param>
    /// <remarks>
    /// Welding by position rather than by index is the whole point. A flat-shaded box duplicates
    /// its corners so each face can carry its own normal, so an index-based edge count reports
    /// twenty-four boundary edges on a solid that is in fact closed. What watertightness means is a
    /// statement about the surface, not about the buffer layout.
    /// </remarks>
    private static void AssertWatertight(Mesh mesh)
    {
        Dictionary<Vector3, int> weld = [];
        int[] welded = new int[mesh.VertexCount];

        for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
        {
            Vector3 position = Round(PositionAt(mesh, vertex));
            if (!weld.TryGetValue(position, out int id))
            {
                id = weld.Count;
                weld[position] = id;
            }

            welded[vertex] = id;
        }

        Dictionary<(int Low, int High), int> edges = [];

        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            int a = welded[mesh.Indices[triangle * 3]];
            int b = welded[mesh.Indices[(triangle * 3) + 1]];
            int c = welded[mesh.Indices[(triangle * 3) + 2]];

            // A pole triangle on a latitude-longitude sphere is degenerate by construction: two of
            // its corners weld to the same point. It contributes no surface and no edge.
            if (a == b || b == c || a == c)
            {
                continue;
            }

            Count(edges, a, b);
            Count(edges, b, c);
            Count(edges, c, a);
        }

        foreach (((int low, int high), int count) in edges)
        {
            Assert.True(
                count == 2,
                $"Edge {low}-{high} is shared by {count} triangles, not 2. The mesh is not watertight.");
        }
    }

    private static void AssertOutwardWinding(Mesh mesh, Vector3 interiorPoint)
    {
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            Vector3 a = PositionAt(mesh, mesh.Indices[triangle * 3]);
            Vector3 b = PositionAt(mesh, mesh.Indices[(triangle * 3) + 1]);
            Vector3 c = PositionAt(mesh, mesh.Indices[(triangle * 3) + 2]);

            Vector3 face = Vector3.Cross(b - a, c - a);
            if (face.LengthSquared() < 1e-10f)
            {
                continue;   // A degenerate pole triangle has no face normal to check.
            }

            Vector3 outward = ((a + b + c) / 3f) - interiorPoint;

            Assert.True(
                Vector3.Dot(face, outward) > 0,
                $"Triangle {triangle} winds inward: face normal {Vector3.Normalize(face)} against {outward}.");
        }
    }

    private static void Count(Dictionary<(int, int), int> edges, int a, int b)
    {
        (int, int) key = a < b ? (a, b) : (b, a);
        edges[key] = edges.TryGetValue(key, out int existing) ? existing + 1 : 1;
    }

    private static Vector3 Round(Vector3 value) => new(
        MathF.Round(value.X, 4), MathF.Round(value.Y, 4), MathF.Round(value.Z, 4));

    private static Vector3 PositionAt(Mesh mesh, int vertex) => new(
        mesh.Positions[vertex * 3], mesh.Positions[(vertex * 3) + 1], mesh.Positions[(vertex * 3) + 2]);

    private static Vector3 NormalAt(Mesh mesh, int vertex) => new(
        mesh.Normals[vertex * 3], mesh.Normals[(vertex * 3) + 1], mesh.Normals[(vertex * 3) + 2]);
}

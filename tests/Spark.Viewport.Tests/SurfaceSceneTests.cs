using System;
using System.Linq;
using Spark.Geometry;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// Surfaces and meshes reaching the viewport — `E2-T26`, `E9`.
/// </summary>
/// <remarks>
/// <b>The seam this covers is the one the walking skeleton exists to prove.</b> A tessellator that
/// produces a correct mesh and a scene builder that never asks it for one is two green test files
/// and a black viewport; these assert that a surface handed to the builder becomes triangles with
/// normals in a render package.
/// </remarks>
public sealed class SurfaceSceneTests
{
    private static readonly GeometryKey Key = new("node", 0);

    /// <summary>A surface becomes a shaded package rather than being counted unrenderable.</summary>
    [Fact]
    public void ASurfaceBecomesTriangles()
    {
        SceneBuilder builder = new();

        builder.Add(Key, new SphericalSurface(Plane.WorldXY, 2.0));

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(0, builder.UnrenderableCount);
        Assert.True(package.TriangleCount > 100, $"only {package.TriangleCount} triangles");
        Assert.Equal(package.VertexCount * 3, package.Normals.Length);
    }

    /// <summary>A mesh becomes triangles too, without going through a surface first.</summary>
    [Fact]
    public void AMeshBecomesTriangles()
    {
        SceneBuilder builder = new();

        builder.Add(
            Key,
            new Mesh(
                [new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0), new Point3d(0, 1, 0)],
                [new MeshFace(0, 1, 2, 3)]));

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(0, builder.UnrenderableCount);
        Assert.Equal(2, package.TriangleCount);
    }

    /// <summary>
    /// <b>A sphere's normals point outwards in the render package</b>, which is what makes it shade
    /// like a sphere rather than like a hole. A mesh drawn with face normals would still round-trip
    /// through here; the assertion is that the *surface's* normals survived.
    /// </summary>
    [Fact]
    public void TheSurfacesNormalsReachThePackage()
    {
        SceneBuilder builder = new();

        builder.Add(Key, new SphericalSurface(Plane.WorldXY, 2.0));

        RenderPackage package = Assert.Single(builder.Build());

        for (int vertex = 0; vertex < package.VertexCount; vertex++)
        {
            float x = package.Positions[vertex * 3];
            float y = package.Positions[(vertex * 3) + 1];
            float z = package.Positions[(vertex * 3) + 2];

            float nx = package.Normals[vertex * 3];
            float ny = package.Normals[(vertex * 3) + 1];
            float nz = package.Normals[(vertex * 3) + 2];

            double length = Math.Sqrt((x * x) + (y * y) + (z * z));

            if (length < 1e-6)
            {
                continue;
            }

            double outwards = ((x * nx) + (y * ny) + (z * nz)) / length;

            Assert.True(outwards > 0.9, $"vertex {vertex}'s normal points inwards ({outwards})");
        }
    }

    /// <summary>
    /// <b>A shaded surface draws no wireframe over itself.</b> Emitting each facet as a quad would
    /// also emit its four edges, and the tessellation grid would be drawn on top of the shading.
    /// </summary>
    [Fact]
    public void AShadedSurfaceHasNoEdges()
    {
        SceneBuilder builder = new();

        builder.Add(Key, new SphericalSurface(Plane.WorldXY, 2.0));

        Assert.Equal(0, Assert.Single(builder.Build()).EdgeCount);
    }

    /// <summary>
    /// The display tolerance scales with the geometry, so a large surface does not get a
    /// hundred-thousand-facet mesh and a small one does not get a lumpy four.
    /// </summary>
    [Fact]
    public void TheFacetCountDoesNotDependOnScale()
    {
        SceneBuilder small = new();
        SceneBuilder large = new();

        small.Add(Key, new SphericalSurface(Plane.WorldXY, 0.01));
        large.Add(Key, new SphericalSurface(Plane.WorldXY, 1000.0));

        int smallCount = Assert.Single(small.Build()).TriangleCount;
        int largeCount = Assert.Single(large.Build()).TriangleCount;

        Assert.Equal(smallCount, largeCount);
    }
}

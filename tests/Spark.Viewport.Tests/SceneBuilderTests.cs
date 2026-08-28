using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Spark.Api;
using Spark.Geometry;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// Turning graph values into render packages: what gets drawn, what one buffer set means, and what
/// happens to geometry a node stops producing.
/// </summary>
public sealed class SceneBuilderTests
{
    private static readonly GeometryKey Key = new("node-a", 0);

    /// <summary>
    /// One package per key however many values arrived, because the scene is keyed by
    /// <c>(NodeId, PortIndex)</c> and that tuple <i>is</i> the geometry's identity.
    /// </summary>
    [Fact]
    public void AHundredPointsBecomeOneBufferSet()
    {
        SceneBuilder builder = new();
        builder.Add(Key, Grid(10, 10));

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(Key, package.Key);
        Assert.Equal(100, builder.RenderableCount);

        // Eight flat-shaded faces per marker, three vertices each.
        Assert.Equal(100 * 8, package.TriangleCount);
    }

    /// <summary>Lists are walked to any depth, which is what a Cross Product result is.</summary>
    [Fact]
    public void NestedListsAreWalkedToAnyDepth()
    {
        SceneBuilder builder = new();
        builder.Add(Key, new SparkList([Grid(3, 3), Grid(2, 2)], 3));

        Assert.Equal(13, builder.RenderableCount);
    }

    /// <summary>
    /// A value nothing knows how to draw contributes nothing and is counted, so a graph full of
    /// arithmetic gives an empty viewport rather than an error.
    /// </summary>
    [Fact]
    public void UnrenderableValuesAreCountedRatherThanThrown()
    {
        SceneBuilder builder = new();
        builder.Add(Key, SparkList.Of(1.0, "text", null, new Point3d(1, 2, 3)));

        Assert.Equal(1, builder.RenderableCount);
        Assert.Equal(2, builder.UnrenderableCount);
        Assert.Single(builder.Build());
    }

    /// <summary>A key that produced nothing produces no package at all.</summary>
    [Fact]
    public void AKeyWithNoRenderableValuesProducesNoPackage()
    {
        SceneBuilder builder = new();
        builder.Add(Key, SparkList.Of(1.0, 2.0));

        Assert.Empty(builder.Build());
        Assert.Empty(builder.Keys());
    }

    /// <summary>
    /// A <see cref="Displayable"/> is unwrapped and its colour reaches the package's appearance,
    /// which is the whole of Spark's styling model.
    /// </summary>
    [Fact]
    public void ADisplayableAppliesItsColour()
    {
        SceneBuilder builder = new();
        builder.Add(
            Key,
            new Displayable(new Point3d(0, 0, 0), new Spark.Api.Appearance(new Rgba(90, 200, 255))));

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(90 / 255f, package.Appearance.Surface.R, 3);
        Assert.Equal(200 / 255f, package.Appearance.Surface.G, 3);
        Assert.Equal(255 / 255f, package.Appearance.Surface.B, 3);
    }

    /// <summary>Unwrapped geometry keeps the viewport's own default, so nothing has to be styled.</summary>
    [Fact]
    public void UnwrappedGeometryKeepsTheDefaultAppearance()
    {
        SceneBuilder builder = new();
        builder.Add(Key, new Point3d(0, 0, 0));

        Assert.Equal(Appearance.Default, Assert.Single(builder.Build()).Appearance);
    }

    /// <summary>
    /// Marker size is derived from the whole scene's extent, so the same code draws a visible dot
    /// on a building and on a bolt.
    /// </summary>
    /// <remarks>
    /// This measures one marker, not the scene. Asserting on the scene's bounds instead passes
    /// whatever the marker size is, because the point positions dominate them — which is exactly
    /// how a test that cannot fail gets written.
    /// </remarks>
    [Fact]
    public void MarkerSizeFollowsTheSceneExtent()
    {
        float small = MarkerSpan(1f);
        float large = MarkerSpan(1000f);

        Assert.True(
            large > small * 100,
            $"A scene a thousand times larger drew a marker {large:F4} across against {small:F4}.");
    }

    /// <summary>A single point still has a size, because its extent is zero and cannot scale one.</summary>
    [Fact]
    public void ASinglePointStillHasAVisibleMarker()
    {
        RenderPackage package = Build(new Point3d(5, 5, 5));
        Bounds3 bounds = package.ComputeBounds();

        Assert.Equal(8, package.TriangleCount);
        Assert.Equal(0.1f, bounds.Max.X - bounds.Min.X, 4);
    }

    /// <summary>
    /// The width of the marker drawn for a lone point in a scene whose extent is set by a second,
    /// distant point filed under another key.
    /// </summary>
    private static float MarkerSpan(float sceneWidth)
    {
        SceneBuilder builder = new();
        builder.Add(Key, new Point3d(0, 0, 0));
        builder.Add(new GeometryKey("far", 0), new Point3d(sceneWidth, 0, 0));

        Bounds3 marker = builder.Build().Single(package => package.Key == Key).ComputeBounds();
        return marker.Max.X - marker.Min.X;
    }

    /// <summary>A vector is drawn from the origin, so a direction is visible as a direction.</summary>
    [Fact]
    public void AVectorIsDrawnFromTheOrigin()
    {
        SceneBuilder builder = new();
        builder.Add(Key, new Vector3d(0, 0, 4));

        RenderPackage package = Assert.Single(builder.Build());

        Assert.True(package.EdgeCount >= 1);
        Assert.Contains(0f, package.Positions.ToArray());
        Assert.Contains(4f, package.Positions.ToArray());
    }

    /// <summary>
    /// A curve arrives as the polyline its own tessellator produces: edges, no triangles, and every
    /// vertex on the curve.
    /// </summary>
    [Fact]
    public void ACurveIsDrawnAsItsOwnTessellation()
    {
        SceneBuilder builder = new();
        Circle circle = Circle.ByCentreRadius(Point3d.Origin, 10.0);
        builder.Add(Key, circle);

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(0, package.TriangleCount);
        Assert.True(package.EdgeCount > 8, $"A circle produced {package.EdgeCount} edges.");

        // Every vertex is on the circle to within the display sag, which is what makes this a test
        // of the curve being drawn rather than of some geometry having been emitted.
        float[] positions = package.Positions.ToArray();
        for (int index = 0; index + 2 < positions.Length; index += 3)
        {
            double radius = Math.Sqrt(
                (positions[index] * positions[index]) + (positions[index + 1] * positions[index + 1]));
            Assert.True(Math.Abs(radius - 10.0) < 0.05, $"A vertex sits at radius {radius}.");
            Assert.Equal(0f, positions[index + 2]);
        }
    }

    /// <summary>
    /// The display tolerance is derived from the curve's size rather than taken from the kernel
    /// default, so a curve a thousand units across costs about as many segments as one a unit
    /// across. At the kernel's own default of 1e-6 the large one would cost tens of thousands.
    /// </summary>
    [Fact]
    public void ALargeCurveDoesNotCostMoreSegmentsThanASmallOne()
    {
        SceneBuilder small = new();
        small.Add(Key, Circle.ByCentreRadius(Point3d.Origin, 1.0));

        SceneBuilder large = new();
        large.Add(Key, Circle.ByCentreRadius(Point3d.Origin, 1000.0));

        int smallEdges = Assert.Single(small.Build()).EdgeCount;
        int largeEdges = Assert.Single(large.Build()).EdgeCount;

        Assert.Equal(smallEdges, largeEdges);
        Assert.True(largeEdges < 512, $"A large circle cost {largeEdges} segments.");
    }

    /// <summary>
    /// A curve inside a list, wrapped for display, is both walked and coloured — the path the curve
    /// demo actually takes from the graph to the screen.
    /// </summary>
    [Fact]
    public void ListsOfDisplayedCurvesAreWalkedAndColoured()
    {
        SceneBuilder builder = new();
        SparkList curves = new(
        [
            new Displayable(Circle.ByCentreRadius(Point3d.Origin, 1.0), new Spark.Api.Appearance(new Rgba(10, 20, 30))),
            new Displayable(new Line(Point3d.Origin, new Point3d(5, 0, 0)), new Spark.Api.Appearance(new Rgba(10, 20, 30))),
        ],
            1);
        builder.Add(Key, curves);

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(2, builder.RenderableCount);
        Assert.Equal(0, builder.UnrenderableCount);
        Assert.Equal(10 / 255f, package.Appearance.Edge.R, 3);
    }

    /// <summary>
    /// A bounding box arrives as six shaded faces, each drawing its own four-edge outline — so
    /// twenty-four edge segments, not twelve. The faces do not share vertices, because a box with
    /// shared corner vertices has one normal where it needs three and shades as a rounded lump.
    /// </summary>
    [Fact]
    public void ABoundingBoxIsDrawnAsASolid()
    {
        SceneBuilder builder = new();
        builder.Add(Key, new Spark.Geometry.BoundingBox(new Point3d(0, 0, 0), new Point3d(2, 3, 4)));

        RenderPackage package = Assert.Single(builder.Build());

        Assert.Equal(12, package.TriangleCount);
        Assert.Equal(24, package.EdgeCount);
    }

    /// <summary>
    /// Publishing replaces the keys it produced and retires the ones it did not, so a node that
    /// stops producing geometry stops showing it.
    /// </summary>
    [Fact]
    public void PublishingRetiresKeysThatProducedNothing()
    {
        ViewportScene scene = new();
        GeometryKey stale = new("node-b", 0);

        SceneBuilder first = new();
        first.Add(Key, new Point3d(0, 0, 0));
        first.Add(stale, new Point3d(1, 1, 1));
        first.PublishTo(scene);

        Assert.Equal(2, scene.Count);

        // The second run's node-b produced nothing at all.
        SceneBuilder second = new();
        second.Add(Key, new Point3d(0, 0, 0));
        second.PublishTo(scene, [Key, stale]);

        Assert.Equal(1, scene.Count);
        Assert.Equal(Key, Assert.Single(scene.Snapshot()).Key);
    }

    /// <summary>
    /// Re-publishing one key replaces one buffer set and leaves the others alone, which is the
    /// property that makes editing one node cheap.
    /// </summary>
    [Fact]
    public void RepublishingOneKeyReplacesOnlyThatBufferSet()
    {
        ViewportScene scene = new();
        GeometryKey other = new("node-b", 0);

        SceneBuilder first = new();
        first.Add(Key, new Point3d(0, 0, 0));
        first.Add(other, new Point3d(9, 9, 9));
        first.PublishTo(scene);

        RenderPackage untouched = scene.Snapshot().Single(package => package.Key == other);

        SceneBuilder second = new();
        second.Add(Key, new Point3d(4, 4, 4));
        second.PublishTo(scene);

        Assert.Equal(2, scene.Count);
        Assert.Same(untouched, scene.Snapshot().Single(package => package.Key == other));
        Assert.NotSame(untouched, scene.Snapshot().Single(package => package.Key == Key));
    }

    /// <summary>Every triangle carries a normal, or the shaded surface has nothing to light.</summary>
    [Fact]
    public void EveryVertexCarriesANormal()
    {
        RenderPackage package = Build(new Point3d(0, 0, 0), new Point3d(1, 1, 1));

        Assert.Equal(package.Positions.Length, package.Normals.Length);

        float[] normals = package.Normals.ToArray();
        for (int i = 0; i + 2 < normals.Length; i += 3)
        {
            float length = new Vector3(normals[i], normals[i + 1], normals[i + 2]).Length();
            Assert.Equal(1f, length, 3);
        }
    }

    private static RenderPackage Build(params Point3d[] points)
    {
        SceneBuilder builder = new();
        foreach (Point3d point in points)
        {
            builder.Add(Key, point);
        }

        return builder.Build().Single();
    }

    private static SparkList Grid(int columns, int rows)
    {
        List<object?> outer = [];
        for (int column = 0; column < columns; column++)
        {
            List<object?> inner = [];
            for (int row = 0; row < rows; row++)
            {
                inner.Add(new Point3d(column, row, 0));
            }

            outer.Add(new SparkList(inner, 1));
        }

        return new SparkList(outer, 2);
    }
}

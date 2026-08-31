using System;
using System.Numerics;
using Spark.Viewport;
using Spark.Viewport.Software;

namespace Spark.Viewport.Tests;

/// <summary>
/// Rendering a scene to pixels with no window and no graphics device — the mechanism behind
/// headless thumbnails, <c>spark render</c> and the CI visual regression.
/// </summary>
public sealed class ThumbnailRendererTests
{
    private static readonly GeometryKey Key = new("node-a", 0);

    /// <summary>The buffer is exactly the size asked for, in RGBA.</summary>
    [Fact]
    public void TheImageIsTheRequestedSizeInFourChannels()
    {
        byte[] pixels = ThumbnailRenderer.Render(SceneWithABox(), 64, 48);

        Assert.Equal(64 * 48 * 4, pixels.Length);
    }

    /// <summary>
    /// Automatic framing puts the geometry on screen. Without it the default camera sits twelve
    /// units out and a small model renders as a handful of pixels, or a large one fills nothing
    /// but the near plane.
    /// </summary>
    [Fact]
    public void AutomaticFramingPutsTheGeometryInTheMiddleOfTheImage()
    {
        // A box a thousand units across: nothing about the default camera would frame this.
        byte[] pixels = ThumbnailRenderer.Render(SceneWithABox(halfSize: 500f), 80, 80);

        Assert.True(IsLit(pixels, 80, 40, 40), "the centre of an auto-framed render should be geometry");
    }

    /// <summary>An empty scene still renders — the background, not a crash and not a black square.</summary>
    [Fact]
    public void AnEmptySceneRendersTheBackgroundRatherThanFailing()
    {
        byte[] pixels = ThumbnailRenderer.Render(new ViewportScene(), 32, 32);

        Assert.Equal(32 * 32 * 4, pixels.Length);

        // #1B1F26 / #14171D: dark, but not the zero a cleared-and-never-drawn buffer would hold.
        int i = (((16 * 32) + 16) * 4) + 2;
        Assert.InRange(pixels[i], 20, 60);
    }

    /// <summary>
    /// Two renders of the same scene are byte-identical. This is the property the CI visual
    /// regression is built on, asserted at the level the CI job actually calls.
    /// </summary>
    [Fact]
    public void TheSameSceneRendersByteIdenticallyThroughTheThumbnailPath()
    {
        ViewportScene scene = SceneWithABox();

        Assert.Equal(
            ThumbnailRenderer.Render(scene, 96, 72, drawGroundGrid: true),
            ThumbnailRenderer.Render(scene, 96, 72, drawGroundGrid: true));
    }

    /// <summary>The grid is off by default for a thumbnail and on when asked for.</summary>
    [Fact]
    public void TheGroundGridIsOffByDefaultAndCanBeTurnedOn()
    {
        ViewportScene scene = new();

        byte[] withoutGrid = ThumbnailRenderer.Render(scene, 120, 120);
        byte[] withGrid = ThumbnailRenderer.Render(scene, 120, 120, drawGroundGrid: true);

        Assert.NotEqual(withoutGrid, withGrid);
    }

    /// <summary>
    /// A caller-supplied camera has its viewport size corrected to the requested image. A camera
    /// framed for one aspect ratio and rendered at another crops silently, which is the kind of
    /// defect that only shows up in the one thumbnail nobody looked at.
    /// </summary>
    [Fact]
    public void ACallerSuppliedCameraIsResizedToTheRequestedImage()
    {
        Camera camera = new();
        camera.SetViewportSize(10, 10);

        ThumbnailRenderer.Render(SceneWithABox(), camera, 200, 100);

        Assert.Equal(200, camera.ViewportWidth);
        Assert.Equal(100, camera.ViewportHeight);
    }

    /// <summary>Null arguments are refused rather than producing an empty image.</summary>
    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ThumbnailRenderer.Render(null!, 8, 8));
        Assert.Throws<ArgumentNullException>(() => ThumbnailRenderer.Render(SceneWithABox(), null!, 8, 8));
    }

    /// <summary>A degenerate size is clamped rather than throwing or allocating nothing.</summary>
    [Fact]
    public void ZeroAndNegativeSizesAreClampedToOnePixel()
    {
        Assert.Equal(4, ThumbnailRenderer.Render(new ViewportScene(), 0, 0).Length);
        Assert.Equal(4, ThumbnailRenderer.Render(new ViewportScene(), -8, -8).Length);
    }

    private static bool IsLit(byte[] pixels, int width, int x, int y)
    {
        int i = (((y * width) + x) * 4);

        // The background is #1B1F26 at its lightest: red 27. Lit geometry.surface is far above it.
        return pixels[i] > 70;
    }

    private static ViewportScene SceneWithABox(float halfSize = 1f)
    {
        ViewportScene scene = new();
        scene.Set(Quad(Key, halfSize));
        return scene;
    }

    /// <summary>A flat quad on the world XY plane, facing +Z.</summary>
    private static RenderPackage Quad(GeometryKey key, float halfSize)
    {
        float[] positions =
        [
            -halfSize, -halfSize, 0f,
            halfSize, -halfSize, 0f,
            halfSize, halfSize, 0f,
            -halfSize, halfSize, 0f,
        ];
        float[] normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f];
        int[] indices = [0, 1, 2, 0, 2, 3];
        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0];

        return new RenderPackage(key, "0", positions, normals, indices, edges, Appearance.Default);
    }
}

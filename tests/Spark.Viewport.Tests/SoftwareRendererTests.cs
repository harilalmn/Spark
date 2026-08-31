using System;
using System.Numerics;
using Spark.Viewport;
using Spark.Viewport.Software;

namespace Spark.Viewport.Tests;

/// <summary>
/// The software rasteriser, asserted on the only thing that matters about it: the pixels. Every
/// test here reads the framebuffer back, because a renderer that compiles, initialises and draws
/// nothing passes every test that does not.
/// </summary>
public sealed class SoftwareRendererTests
{
    private static readonly GeometryKey Key = new("node-a", 0);

    /// <summary>
    /// The backend this project exists to provide a fallback <i>to</i> must not have a failure
    /// mode of its own.
    /// </summary>
    [Fact]
    public void InitialisationAlwaysSucceedsAndSaysSoInTheDiagnostic()
    {
        using SoftwareViewportRenderer renderer = new(64, 48);

        Assert.True(renderer.Initialise());
        Assert.True(renderer.IsInitialised);
        Assert.Equal("Software", renderer.Name);
        Assert.NotNull(renderer.Diagnostic);
    }

    /// <summary>An empty scene is the background gradient, and the gradient runs the right way up.</summary>
    [Fact]
    public void AnEmptySceneIsTheBackgroundGradientDarkestAtTheBottom()
    {
        using SoftwareViewportRenderer renderer = Ready(120, 90);
        renderer.DrawGroundGrid = false;

        renderer.Render(new ViewportScene(), new Camera());

        // The dither is ±0.75% of a unit, so compare rows far enough apart to clear it.
        ViewportColor top = renderer.Framebuffer.GetPixel(60, 2);
        ViewportColor bottom = renderer.Framebuffer.GetPixel(60, 87);

        Assert.True(top.R > bottom.R, $"top {top.R} should be lighter than bottom {bottom.R}");
        Assert.True(top.G > bottom.G);
        Assert.True(top.B > bottom.B);

        // Nothing was drawn, so nothing wrote depth.
        Assert.Equal(SoftwareFramebuffer.Far, renderer.Framebuffer.GetDepth(60, 45));
    }

    /// <summary>
    /// A triangle facing the camera covers the middle of the viewport and is lit — brighter than
    /// the background it replaced, and carrying a real depth.
    /// </summary>
    [Fact]
    public void ATriangleIsRasterisedLitAndWritesDepth()
    {
        using SoftwareViewportRenderer renderer = Ready(200, 200);
        renderer.DrawGroundGrid = false;

        ViewportScene scene = new();
        scene.Set(TriangleFacingUp(Key, z: 0f));

        renderer.Render(scene, LookingDown(6f));

        ViewportColor centre = renderer.Framebuffer.GetPixel(100, 100);
        float depth = renderer.Framebuffer.GetDepth(100, 100);

        Assert.True(depth < SoftwareFramebuffer.Far, "the centre pixel should carry a written depth");
        Assert.InRange(depth, 0f, 1f);

        // geometry.surface is #AEB7C6; lit, it is far above the #1B1F26 background.
        Assert.True(centre.R > 0.3f, $"centre red was {centre.R}, which is background-dark");

        // A corner is outside the triangle and must still be background.
        Assert.Equal(SoftwareFramebuffer.Far, renderer.Framebuffer.GetDepth(3, 3));
    }

    /// <summary>
    /// The depth buffer actually arbitrates. The nearer of two overlapping triangles wins
    /// regardless of the order they were submitted in, which is the property a painter's
    /// algorithm silently does not have.
    /// </summary>
    [Fact]
    public void TheNearerTriangleOccludesTheFartherOneInEitherSubmissionOrder()
    {
        float farDepth = RenderPairAndReadCentreDepth(nearFirst: false);
        float nearDepth = RenderPairAndReadCentreDepth(nearFirst: true);

        Assert.Equal(nearDepth, farDepth, 6);
    }

    /// <summary>
    /// The same scene rendered twice produces the same bytes. This is the entire premise of
    /// <c>E9-T12</c>: if this is not true, no visual regression test can exist.
    /// </summary>
    [Fact]
    public void TheSameSceneRendersByteIdenticallyTwice()
    {
        ViewportScene scene = new();
        scene.Set(TriangleFacingUp(Key, z: 0f));

        byte[] first = RenderToBytes(scene);
        byte[] second = RenderToBytes(scene);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Two renderers built independently agree. Determinism within one instance could come from a
    /// cache; this is the property CI actually depends on.
    /// </summary>
    [Fact]
    public void TwoSeparateRenderersAgreeOnTheSameScene()
    {
        ViewportScene scene = new();
        scene.Set(TriangleFacingUp(Key, z: 0.25f));

        Assert.Equal(RenderToBytes(scene), RenderToBytes(scene));
    }

    /// <summary>
    /// Ghosted geometry is drawn edges-only and unshaded — a rendering mode rather than a contrast
    /// ratio, which is how the design language's §8.4 exception is discharged.
    /// </summary>
    [Fact]
    public void GhostedGeometryDrawsNoShadedSurface()
    {
        using SoftwareViewportRenderer renderer = Ready(200, 200);
        renderer.DrawGroundGrid = false;

        RenderPackage solid = TriangleFacingUp(Key, z: 0f);
        ViewportScene scene = new();
        scene.Set(solid.WithAppearance(new Appearance(
            ViewportPalette.GeometrySurface, ViewportPalette.GeometryEdge, IsSelected: false, IsGhosted: true)));

        renderer.Render(scene, LookingDown(6f));

        // The centre of the face is interior to the triangle and away from any edge, so a ghosted
        // package must leave it untouched.
        Assert.Equal(SoftwareFramebuffer.Far, renderer.Framebuffer.GetDepth(100, 100));
    }

    /// <summary>Resizing reallocates and the next frame fills the new size.</summary>
    [Fact]
    public void ResizingChangesTheBufferAndTheNextFrameFillsIt()
    {
        using SoftwareViewportRenderer renderer = Ready(32, 32);
        renderer.Resize(80, 40);

        Assert.Equal(80, renderer.Framebuffer.Width);
        Assert.Equal(40, renderer.Framebuffer.Height);

        renderer.Render(new ViewportScene(), new Camera());

        Assert.Equal(80 * 40 * 4, renderer.Framebuffer.Pixels.Length);
        Assert.Equal(255, renderer.Framebuffer.GetPixel(79, 39).A * 255f, 3);
    }

    /// <summary>
    /// Geometry entirely behind the eye is clipped rather than mirrored onto the screen. A near
    /// plane that is not clipped geometrically produces a triangle that is not merely off-screen
    /// but inside out, and it is the single most visible rasteriser defect there is.
    /// </summary>
    [Fact]
    public void GeometryBehindTheEyeDrawsNothing()
    {
        using SoftwareViewportRenderer renderer = Ready(120, 120);
        renderer.DrawGroundGrid = false;

        ViewportScene scene = new();
        scene.Set(TriangleFacingUp(Key, z: 0f));

        Camera camera = new() { Target = new Vector3(0f, 0f, -400f), Distance = 5f };
        camera.SetViewportSize(120, 120);
        renderer.Render(scene, camera);

        for (int y = 0; y < 120; y++)
        {
            for (int x = 0; x < 120; x++)
            {
                Assert.Equal(SoftwareFramebuffer.Far, renderer.Framebuffer.GetDepth(x, y));
            }
        }
    }

    /// <summary>
    /// A renderer that has not been initialised draws nothing rather than throwing, because the
    /// caller is a paint handler and a throw there takes the window down.
    /// </summary>
    [Fact]
    public void AnUninitialisedRendererDrawsNothingAndDoesNotThrow()
    {
        using SoftwareViewportRenderer renderer = new(16, 16);

        renderer.Render(new ViewportScene(), new Camera());

        Assert.Equal(SoftwareFramebuffer.Far, renderer.Framebuffer.GetDepth(8, 8));
    }

    /// <summary>The ground grid is furniture and can be turned off for a geometry-only thumbnail.</summary>
    [Fact]
    public void TheGroundGridCanBeTurnedOff()
    {
        using SoftwareViewportRenderer withGrid = Ready(160, 160);
        using SoftwareViewportRenderer withoutGrid = Ready(160, 160);
        withoutGrid.DrawGroundGrid = false;

        ViewportScene scene = new();
        withGrid.Render(scene, LookingDown(20f));
        withoutGrid.Render(scene, LookingDown(20f));

        Assert.False(withGrid.Framebuffer.Pixels.SequenceEqual(withoutGrid.Framebuffer.Pixels));
    }

    /// <summary>
    /// The lighting model's shape, which is what can be held without a GPU to compare against.
    /// A lit fragment never falls below the ambient term and never exceeds the sum of the
    /// coefficients plus the specular. Both bounds are the shader's own constants —
    /// <c>0.26 + 0.60 + 0.16</c> and <c>0.22</c> — so a coefficient edited on one side of the
    /// pair and not the other moves a pixel outside them.
    /// </summary>
    [Fact]
    public void EveryLitPixelLiesBetweenTheAmbientFloorAndTheFullyLitCeiling()
    {
        using SoftwareViewportRenderer renderer = Ready(160, 160);
        renderer.DrawGroundGrid = false;

        ViewportScene scene = new();
        scene.Set(TriangleFacingUp(Key, z: 0f));

        renderer.Render(scene, LookingDown(6f));

        const float Surface = 0.68235296f;              // #AEB7C6's red channel.
        const float Floor = Surface * 0.26f;
        const float Ceiling = (Surface * (0.26f + 0.60f + 0.16f)) + 0.22f;

        int lit = 0;
        for (int y = 0; y < 160; y++)
        {
            for (int x = 0; x < 160; x++)
            {
                if (renderer.Framebuffer.GetDepth(x, y) >= SoftwareFramebuffer.Far)
                {
                    continue;
                }

                float red = renderer.Framebuffer.GetPixel(x, y).R;

                // The edge pass paints over the silhouette in geometry.edge, which is not a
                // shaded value and is not what this test is about.
                if (red > 0.88f)
                {
                    continue;
                }

                lit++;
                Assert.InRange(red, Floor - 0.01f, MathF.Min(Ceiling, 1f) + 0.01f);
            }
        }

        Assert.True(lit > 1000, $"only {lit} shaded pixels were found; the triangle did not draw");
    }

    /// <summary>
    /// A face turned towards the key light is brighter than the same face turned away from it.
    /// Bounds alone would pass on a constant, and this is what makes the model directional.
    /// </summary>
    [Fact]
    public void TurningAFaceTowardsTheKeyLightBrightensIt()
    {
        float facingLight = CentreBrightnessWithNormal(new Vector3(-0.6f, -0.6f, 0.53f));
        float facingAway = CentreBrightnessWithNormal(new Vector3(0.6f, 0.6f, 0.53f));

        Assert.True(
            facingLight > facingAway,
            $"towards the key light gave {facingLight}, away gave {facingAway}");
    }

    private static float CentreBrightnessWithNormal(Vector3 normal)
    {
        using SoftwareViewportRenderer renderer = Ready(200, 200);
        renderer.DrawGroundGrid = false;

        Vector3 n = Vector3.Normalize(normal);
        float[] positions = [-2f, -2f, 0f, 2f, -2f, 0f, 0f, 2f, 0f];
        float[] normals = [n.X, n.Y, n.Z, n.X, n.Y, n.Z, n.X, n.Y, n.Z];

        ViewportScene scene = new();
        scene.Set(new RenderPackage(Key, "0", positions, normals, [0, 1, 2], [], Appearance.Default));

        renderer.Render(scene, LookingDown(6f));
        return renderer.Framebuffer.GetPixel(100, 100).R;
    }

    private static SoftwareViewportRenderer Ready(int width, int height)
    {
        SoftwareViewportRenderer renderer = new(width, height);
        renderer.Initialise();
        return renderer;
    }

    private static Camera LookingDown(float distance)
    {
        Camera camera = new() { Target = Vector3.Zero, Distance = distance, Elevation = 1.4f, Azimuth = 0f };
        camera.SetViewportSize(200, 200);
        return camera;
    }

    private static byte[] RenderToBytes(ViewportScene scene)
    {
        using SoftwareViewportRenderer renderer = Ready(96, 96);
        renderer.Render(scene, LookingDown(6f));

        byte[] bytes = new byte[renderer.Framebuffer.Pixels.Length];
        renderer.Framebuffer.CopyPixels(bytes);
        return bytes;
    }

    private static float RenderPairAndReadCentreDepth(bool nearFirst)
    {
        using SoftwareViewportRenderer renderer = Ready(200, 200);
        renderer.DrawGroundGrid = false;

        RenderPackage near = TriangleFacingUp(new GeometryKey("near", 0), z: 1f);
        RenderPackage far = TriangleFacingUp(new GeometryKey("far", 0), z: -1f);

        ViewportScene scene = new();
        if (nearFirst)
        {
            scene.Set(near);
            scene.Set(far);
        }
        else
        {
            scene.Set(far);
            scene.Set(near);
        }

        renderer.Render(scene, LookingDown(8f));
        return renderer.Framebuffer.GetDepth(100, 100);
    }

    /// <summary>A large upward-facing triangle centred on the origin at a given height.</summary>
    private static RenderPackage TriangleFacingUp(GeometryKey key, float z)
    {
        float[] positions = [-2f, -2f, z, 2f, -2f, z, 0f, 2f, z];
        float[] normals = [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f];
        int[] indices = [0, 1, 2];
        int[] edges = [0, 1, 1, 2, 2, 0];

        return new RenderPackage(key, "0", positions, normals, indices, edges, Appearance.Default);
    }
}

using System;
using System.Numerics;

namespace Spark.Viewport.Software;

/// <summary>
/// Renders a scene to pixels with no window, no graphics device and no display connection: the
/// headless half of <c>E9-T11</c>, and the mechanism <c>spark render</c> and the CI visual
/// regression in <c>E9-T12</c> are both built on.
/// </summary>
/// <remarks>
/// <para>
/// This is a convenience over <see cref="SoftwareViewportRenderer"/> rather than a second
/// renderer. It exists because the three callers that want a picture of a scene — a thumbnail, a
/// command-line render, a regression test — all want the same four things: a sensible camera when
/// none was supplied, a fixed size, the furniture turned off, and bytes back. Writing that four
/// times is how the four slowly stop agreeing.
/// </para>
/// <para>
/// Output is 8-bit RGBA, <b>top row first</b>, which is the row order every image format Spark
/// writes uses and the opposite of what <c>glReadPixels</c> returns. The GL capture path flips;
/// this one has nothing to flip.
/// </para>
/// </remarks>
public static class ThumbnailRenderer
{
    /// <summary>
    /// Renders a scene, framing it automatically.
    /// </summary>
    /// <param name="scene">The geometry to draw.</param>
    /// <param name="width">Image width in pixels. Values below one are treated as one.</param>
    /// <param name="height">Image height in pixels. Values below one are treated as one.</param>
    /// <param name="drawGroundGrid">
    /// Whether to draw the ground grid and world axes. A thumbnail of the geometry alone usually
    /// wants them off; a picture standing in for the viewport wants them on.
    /// </param>
    /// <returns>8-bit RGBA, top row first, <c>width * height * 4</c> bytes long.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public static byte[] Render(ViewportScene scene, int width, int height, bool drawGroundGrid = false)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Camera camera = new();
        camera.SetViewportSize(Math.Max(1, width), Math.Max(1, height));

        Bounds3 bounds = scene.ComputeBounds();
        if (bounds.IsEmpty)
        {
            // Nothing to frame. Leave the camera at its default three-quarter view rather than
            // inventing a distance, so an empty render is the empty viewport and not a black
            // rectangle that could mean anything.
            camera.Target = Vector3.Zero;
        }
        else
        {
            camera.ZoomToFit(bounds);
        }

        return Render(scene, camera, width, height, drawGroundGrid);
    }

    /// <summary>
    /// Renders a scene through a camera the caller supplies.
    /// </summary>
    /// <param name="scene">The geometry to draw.</param>
    /// <param name="camera">
    /// The camera. Its viewport size is set to match the requested image, because a camera framed
    /// for one aspect ratio and rendered at another crops silently.
    /// </param>
    /// <param name="width">Image width in pixels. Values below one are treated as one.</param>
    /// <param name="height">Image height in pixels. Values below one are treated as one.</param>
    /// <param name="drawGroundGrid">Whether to draw the ground grid and world axes.</param>
    /// <returns>8-bit RGBA, top row first, <c>width * height * 4</c> bytes long.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="camera"/> is null.</exception>
    public static byte[] Render(
        ViewportScene scene,
        Camera camera,
        int width,
        int height,
        bool drawGroundGrid = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        using SoftwareViewportRenderer renderer = new(width, height);
        renderer.Initialise();
        renderer.DrawGroundGrid = drawGroundGrid;
        renderer.Render(scene, camera);

        byte[] pixels = new byte[width * height * 4];
        renderer.Framebuffer.CopyPixels(pixels);
        return pixels;
    }
}

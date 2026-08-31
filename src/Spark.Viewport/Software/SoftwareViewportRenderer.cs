using System;
using System.Numerics;
using Spark.Viewport.Meshes;

namespace Spark.Viewport.Software;

/// <summary>
/// A CPU rasteriser implementing <see cref="IViewportRenderer"/> without a graphics driver of any
/// kind. It exists for three reasons, all of them named on <c>E9-T5</c>: GL initialisation fails
/// on virtual machines and over remote desktop and the viewport still has to show something;
/// thumbnails have to render with no window; and <b>GPU output is not comparable between
/// machines, so nothing a GPU draws can be asserted on</b>. This backend's output is comparable,
/// which is what makes viewport regression testing possible at all
/// (<c>ADR-0014</c>, <c>E9-T11</c>, <c>E9-T12</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a match for the GL path, not an approximation of it.</b> The draw order, the two-sided
/// lighting model in <c>GlShaders.MeshFragment</c>, the camera-relative key light, the selection
/// tint and the <c>0.0006</c> edge depth bias are all reproduced deliberately, because a fallback
/// that renders a recognisably different picture is a fallback nobody trusts. Interpolation is
/// perspective-correct for the same reason: affine interpolation is cheaper and shades a large
/// triangle visibly wrongly.
/// </para>
/// <para>
/// <b>Three deliberate divergences</b>, each because bit-for-bit reproducibility matters more here
/// than matching the GPU. <i>One:</i> the background dither uses an integer hash rather than the
/// shader's <c>fract(sin(...))</c> — the sine of a large argument is exactly where two conforming
/// IEEE 754 implementations may still differ, and this backend exists to produce the same bytes
/// everywhere. <i>Two:</i> lines are drawn by a DDA walk rather than by GL's diamond-exit rule, so
/// a diagonal line may differ from the GPU's by a pixel. <i>Three:</i> there is no multisampling.
/// None of the three affects a software-against-software comparison, which is the only comparison
/// that is ever made.
/// </para>
/// <para>
/// Not thread-safe, in common with every <see cref="IViewportRenderer"/>. Render from one thread.
/// </para>
/// </remarks>
public sealed class SoftwareViewportRenderer : IViewportRenderer
{
    /// <summary>
    /// How far towards the eye edges are pulled in normalised device depth, so an edge drawn on
    /// the silhouette of its own surface is not half-swallowed by it. The same constant the GL
    /// line shader receives.
    /// </summary>
    private const float EdgeDepthBias = 0.0006f;

    private bool _disposed;
    private LineBatch? _grid;

    /// <summary>Creates a renderer at a starting size.</summary>
    /// <param name="widthPixels">Width in pixels. Values below one are treated as one.</param>
    /// <param name="heightPixels">Height in pixels. Values below one are treated as one.</param>
    public SoftwareViewportRenderer(int widthPixels = 1, int heightPixels = 1)
    {
        Framebuffer = new SoftwareFramebuffer(widthPixels, heightPixels);
    }

    /// <inheritdoc/>
    public string Name => "Software";

    /// <inheritdoc/>
    public bool IsInitialised { get; private set; }

    /// <inheritdoc/>
    public string? Diagnostic { get; private set; }

    /// <summary>
    /// The colour and depth targets. Reading pixels from here is how a thumbnail is captured and
    /// how a regression test asserts on a frame; there is no read-back step because there is no
    /// device to read back from.
    /// </summary>
    public SoftwareFramebuffer Framebuffer { get; }

    /// <summary>
    /// Whether the ground grid and world axes are drawn. On for an interactive viewport; a caller
    /// rendering a thumbnail of the geometry alone turns it off.
    /// </summary>
    public bool DrawGroundGrid { get; set; } = true;

    /// <summary>
    /// Whether the background gradient is painted. When false the colour target is left as it was,
    /// which is what a caller compositing over its own background wants.
    /// </summary>
    public bool DrawBackground { get; set; } = true;

    /// <inheritdoc/>
    /// <remarks>
    /// Always succeeds. That is the point of this backend: it is what the viewport falls back
    /// <i>to</i>, so it must not have a failure mode of its own to fall back from.
    /// </remarks>
    public bool Initialise()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _grid = GroundGrid.Build();
        IsInitialised = true;
        Diagnostic = "Software rasteriser: no graphics device in use.";
        return true;
    }

    /// <inheritdoc/>
    public void Resize(int widthPixels, int heightPixels) => Framebuffer.Resize(widthPixels, heightPixels);

    /// <inheritdoc/>
    public void Render(ViewportScene scene, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        if (!IsInitialised || _disposed)
        {
            return;
        }

        camera.SetViewportSize(Framebuffer.Width, Framebuffer.Height);

        if (DrawBackground)
        {
            PaintBackground();
        }

        Framebuffer.ClearDepth();

        Matrix4x4 viewProjection = camera.ViewProjection;
        RenderPackage[] packages = scene.Snapshot();

        if (DrawGroundGrid && _grid is not null)
        {
            DrawLineBatch(_grid, viewProjection);
        }

        DrawGeometry(packages, camera, viewProjection);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        IsInitialised = false;
        _grid = null;
    }

    /// <summary>
    /// The vertical gradient, dithered. The gradient runs from <c>viewport.bottom</c> at the
    /// bottom of the image to <c>viewport.top</c> at the top, matching the background shader.
    /// </summary>
    private void PaintBackground()
    {
        ViewportColor top = ViewportPalette.BackgroundTop;
        ViewportColor bottom = ViewportPalette.BackgroundBottom;
        int width = Framebuffer.Width;
        int height = Framebuffer.Height;

        for (int y = 0; y < height; y++)
        {
            // Row 0 is the top of the image, and the shader's vHeight is 1 at the top.
            float h = 1f - ((y + 0.5f) / height);
            float r = bottom.R + ((top.R - bottom.R) * h);
            float g = bottom.G + ((top.G - bottom.G) * h);
            float b = bottom.B + ((top.B - bottom.B) * h);

            for (int x = 0; x < width; x++)
            {
                float noise = Dither(x, y) * 0.015f;
                Framebuffer.SetPixel(x, y, r + noise, g + noise, b + noise);
            }
        }
    }

    /// <summary>
    /// A deterministic value in −0.5..0.5 from a pixel coordinate, standing in for the shader's
    /// <c>fract(sin(dot(gl_FragCoord.xy, ...)))</c>.
    /// </summary>
    private static float Dither(int x, int y)
    {
        uint h = (uint)((x * 73856093) ^ (y * 19349663));
        h ^= h >> 13;
        h *= 0x85EBCA6B;
        h ^= h >> 16;
        return (h / (float)uint.MaxValue) - 0.5f;
    }

    private void DrawGeometry(RenderPackage[] packages, Camera camera, in Matrix4x4 viewProjection)
    {
        Vector3 eye = camera.Position;

        // Fixed relative to the camera, at the top-left, exactly as DrawGeometry does on the GL
        // path. A world-fixed light leaves a face unlit however the user orbits, which reads as
        // a hole rather than as a shadow.
        Vector3 forward = Vector3.Normalize(camera.Target - eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Camera.WorldUp));
        Vector3 up = Vector3.Cross(right, forward);
        Vector3 keyLight = Vector3.Normalize((-forward * 0.55f) + (right * -0.55f) + (up * 0.62f));

        foreach (RenderPackage package in packages)
        {
            if (package.Appearance.IsGhosted || package.TriangleCount == 0)
            {
                continue;
            }

            ViewportColor surface = package.Appearance.Surface;
            if (package.Appearance.IsSelected)
            {
                surface = Blend(surface, ViewportPalette.Accent, 0.15f);
            }

            DrawTriangles(package, viewProjection, eye, keyLight, surface);
        }

        foreach (RenderPackage package in packages)
        {
            if (package.EdgeCount == 0)
            {
                continue;
            }

            ViewportColor edge = package.Appearance.IsSelected
                ? ViewportPalette.Accent
                : package.Appearance.IsGhosted
                    ? ViewportPalette.GeometryGhost
                    : package.Appearance.Edge;

            DrawEdges(package, viewProjection, edge);
        }
    }

    private void DrawTriangles(
        RenderPackage package,
        in Matrix4x4 viewProjection,
        in Vector3 eye,
        in Vector3 keyLight,
        in ViewportColor surface)
    {
        ReadOnlySpan<float> positions = package.Positions;
        ReadOnlySpan<float> normals = package.Normals;
        ReadOnlySpan<int> indices = package.Indices;
        int vertexCount = package.VertexCount;

        Span<ShadedVertex> polygon = stackalloc ShadedVertex[8];
        Span<ShadedVertex> clipped = stackalloc ShadedVertex[8];

        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int i0 = indices[t];
            int i1 = indices[t + 1];
            int i2 = indices[t + 2];

            if ((uint)i0 >= (uint)vertexCount || (uint)i1 >= (uint)vertexCount || (uint)i2 >= (uint)vertexCount)
            {
                // A malformed index is dropped rather than thrown on: this runs inside a paint
                // path, and a throw there takes a window down over one bad triangle.
                continue;
            }

            polygon[0] = MakeVertex(positions, normals, i0, viewProjection);
            polygon[1] = MakeVertex(positions, normals, i1, viewProjection);
            polygon[2] = MakeVertex(positions, normals, i2, viewProjection);

            int count = ClipToNearPlane(polygon, 3, clipped);
            for (int f = 2; f < count; f++)
            {
                RasteriseTriangle(clipped[0], clipped[f - 1], clipped[f], eye, keyLight, surface);
            }
        }
    }

    private static ShadedVertex MakeVertex(
        ReadOnlySpan<float> positions,
        ReadOnlySpan<float> normals,
        int index,
        in Matrix4x4 viewProjection)
    {
        int p = index * 3;
        Vector3 world = new(positions[p], positions[p + 1], positions[p + 2]);
        Vector3 normal = normals.Length == positions.Length
            ? new Vector3(normals[p], normals[p + 1], normals[p + 2])
            : Vector3.UnitZ;

        return new ShadedVertex(Vector4.Transform(new Vector4(world, 1f), viewProjection), world, normal);
    }

    /// <summary>
    /// Clips a convex polygon against the near plane, which in this projection's convention is
    /// <c>z >= 0</c> rather than <c>z >= -w</c>.
    /// </summary>
    /// <remarks>
    /// This is the only plane clipped geometrically. The four side planes are handled by clamping
    /// the raster bounding box, which produces the same pixels for less code, and the far plane by
    /// rejecting fragments whose depth exceeds one. The near plane cannot be treated that way,
    /// because a vertex behind the eye has a negative <c>w</c> and projects to a position that is
    /// not merely off-screen but mirrored.
    /// </remarks>
    private static int ClipToNearPlane(ReadOnlySpan<ShadedVertex> input, int count, Span<ShadedVertex> output)
    {
        int produced = 0;
        for (int i = 0; i < count; i++)
        {
            ShadedVertex current = input[i];
            ShadedVertex next = input[(i + 1) % count];
            bool currentInside = current.Clip.Z >= 0f;
            bool nextInside = next.Clip.Z >= 0f;

            if (currentInside && produced < output.Length)
            {
                output[produced++] = current;
            }

            if (currentInside != nextInside && produced < output.Length)
            {
                float denominator = current.Clip.Z - next.Clip.Z;
                if (MathF.Abs(denominator) > float.Epsilon)
                {
                    output[produced++] = ShadedVertex.Lerp(current, next, current.Clip.Z / denominator);
                }
            }
        }

        return produced;
    }

    private void RasteriseTriangle(
        in ShadedVertex a,
        in ShadedVertex b,
        in ShadedVertex c,
        in Vector3 eye,
        in Vector3 keyLight,
        in ViewportColor surface)
    {
        int width = Framebuffer.Width;
        int height = Framebuffer.Height;

        Project(a.Clip, width, height, out Vector2 p0, out float d0, out float invW0);
        Project(b.Clip, width, height, out Vector2 p1, out float d1, out float invW1);
        Project(c.Clip, width, height, out Vector2 p2, out float d2, out float invW2);

        float area = EdgeFunction(p0, p1, p2.X, p2.Y);
        if (MathF.Abs(area) < 1e-9f || !float.IsFinite(area))
        {
            return;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));

        float inverseArea = 1f / area;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;

                // Dividing by the signed area rather than its magnitude makes the barycentrics
                // positive inside the triangle for either winding, which is what keeps this
                // rasteriser two-sided in the same way the fragment shader is.
                float w0 = EdgeFunction(p1, p2, px, py) * inverseArea;
                float w1 = EdgeFunction(p2, p0, px, py) * inverseArea;
                float w2 = EdgeFunction(p0, p1, px, py) * inverseArea;

                if (w0 < 0f || w1 < 0f || w2 < 0f)
                {
                    continue;
                }

                float depth = (w0 * d0) + (w1 * d1) + (w2 * d2);

                // Perspective correction. Interpolating an attribute linearly in screen space is
                // wrong for anything but depth, and the error grows with the triangle.
                float weight0 = w0 * invW0;
                float weight1 = w1 * invW1;
                float weight2 = w2 * invW2;
                float weightSum = weight0 + weight1 + weight2;
                if (weightSum <= 0f || !float.IsFinite(weightSum))
                {
                    continue;
                }

                float inverseWeight = 1f / weightSum;
                Vector3 world = ((a.World * weight0) + (b.World * weight1) + (c.World * weight2)) * inverseWeight;
                Vector3 normal = ((a.Normal * weight0) + (b.Normal * weight1) + (c.Normal * weight2)) * inverseWeight;

                Shade(normal, world, eye, keyLight, surface, out float r, out float g, out float bl);
                Framebuffer.TestAndSet(x, y, depth, r, g, bl);
            }
        }
    }

    /// <summary>
    /// The lighting model, transcribed term for term from <c>GlShaders.MeshFragment</c>.
    /// </summary>
    /// <remarks>
    /// <b>Nothing enforces that these two stay equal, and it is worth being plain about that
    /// rather than implying a gate that does not exist.</b> Comparing them directly would need a
    /// GPU, which is the very thing this backend exists to do without. What the tests can and do
    /// hold is the model's shape — that the result never falls below the ambient term, never
    /// exceeds the sum of the coefficients plus the specular, and rises as a face turns towards
    /// the key light. A change to the shader that preserved all three would still go unnoticed
    /// here, so a change to either file is a change to both.
    /// </remarks>
    private static void Shade(
        Vector3 normal,
        Vector3 world,
        in Vector3 eye,
        in Vector3 keyLight,
        in ViewportColor surface,
        out float r,
        out float g,
        out float b)
    {
        Vector3 n = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        Vector3 toEye = eye - world;
        Vector3 v = toEye.LengthSquared() > 0f ? Vector3.Normalize(toEye) : Vector3.UnitZ;

        // Two-sided: a fragment facing away is lit with the negated normal rather than culled, so
        // a winding defect shows as odd shading rather than as missing geometry.
        if (Vector3.Dot(n, v) < 0f)
        {
            n = -n;
        }

        Vector3 key = Vector3.Normalize(keyLight);
        Vector3 fill = Vector3.Normalize(new Vector3(-0.45f, 0.35f, 0.30f));

        float lambert = 0.26f
            + (0.60f * MathF.Max(Vector3.Dot(n, key), 0f))
            + (0.16f * MathF.Max(Vector3.Dot(n, fill), 0f));

        Vector3 halfway = Vector3.Normalize(key + v);
        float specular = MathF.Pow(MathF.Max(Vector3.Dot(n, halfway), 0f), 40f) * 0.22f;

        r = MathF.Min((surface.R * lambert) + specular, 1f);
        g = MathF.Min((surface.G * lambert) + specular, 1f);
        b = MathF.Min((surface.B * lambert) + specular, 1f);
    }

    private void DrawEdges(RenderPackage package, in Matrix4x4 viewProjection, ViewportColor colour)
    {
        ReadOnlySpan<float> positions = package.Positions;
        ReadOnlySpan<int> edges = package.EdgeIndices;
        int vertexCount = package.VertexCount;

        for (int e = 0; e + 1 < edges.Length; e += 2)
        {
            int i0 = edges[e];
            int i1 = edges[e + 1];
            if ((uint)i0 >= (uint)vertexCount || (uint)i1 >= (uint)vertexCount)
            {
                continue;
            }

            Vector4 c0 = Vector4.Transform(
                new Vector4(positions[i0 * 3], positions[(i0 * 3) + 1], positions[(i0 * 3) + 2], 1f), viewProjection);
            Vector4 c1 = Vector4.Transform(
                new Vector4(positions[i1 * 3], positions[(i1 * 3) + 1], positions[(i1 * 3) + 2], 1f), viewProjection);

            DrawClipLine(c0, c1, colour, colour, EdgeDepthBias);
        }
    }

    private void DrawLineBatch(LineBatch batch, in Matrix4x4 viewProjection)
    {
        ReadOnlySpan<float> positions = batch.Positions;
        ReadOnlySpan<float> colours = batch.Colours;

        for (int v = 0; v + 1 < batch.VertexCount; v += 2)
        {
            Vector4 c0 = Vector4.Transform(
                new Vector4(positions[v * 3], positions[(v * 3) + 1], positions[(v * 3) + 2], 1f), viewProjection);
            Vector4 c1 = Vector4.Transform(
                new Vector4(positions[(v + 1) * 3], positions[((v + 1) * 3) + 1], positions[((v + 1) * 3) + 2], 1f),
                viewProjection);

            ViewportColor col0 = new(colours[v * 4], colours[(v * 4) + 1], colours[(v * 4) + 2], colours[(v * 4) + 3]);
            ViewportColor col1 = new(
                colours[(v + 1) * 4], colours[((v + 1) * 4) + 1], colours[((v + 1) * 4) + 2], colours[((v + 1) * 4) + 3]);

            DrawClipLine(c0, c1, col0, col1, 0f);
        }
    }

    private void DrawClipLine(Vector4 c0, Vector4 c1, ViewportColor col0, ViewportColor col1, float depthBias)
    {
        // The bias is applied in clip space, before the divide, exactly as the line vertex shader
        // does it. Applying it after the divide would make it depend on distance.
        c0.Z -= depthBias * c0.W;
        c1.Z -= depthBias * c1.W;

        bool inside0 = c0.Z >= 0f;
        bool inside1 = c1.Z >= 0f;

        if (!inside0 && !inside1)
        {
            return;
        }

        if (inside0 != inside1)
        {
            float denominator = c0.Z - c1.Z;
            if (MathF.Abs(denominator) <= float.Epsilon)
            {
                return;
            }

            float t = c0.Z / denominator;
            Vector4 crossing = Vector4.Lerp(c0, c1, t);
            ViewportColor crossingColour = LerpColour(col0, col1, t);

            if (inside0)
            {
                c1 = crossing;
                col1 = crossingColour;
            }
            else
            {
                c0 = crossing;
                col0 = crossingColour;
            }
        }

        Project(c0, Framebuffer.Width, Framebuffer.Height, out Vector2 p0, out float d0, out _);
        Project(c1, Framebuffer.Width, Framebuffer.Height, out Vector2 p1, out float d1, out _);

        if (!float.IsFinite(p0.X) || !float.IsFinite(p0.Y) || !float.IsFinite(p1.X) || !float.IsFinite(p1.Y))
        {
            return;
        }

        // Clip to the viewport rectangle before walking it. Without this, a line that runs a
        // million pixels off-screen costs a million iterations to draw nothing.
        if (!ClipToViewport(ref p0, ref p1, ref d0, ref d1, ref col0, ref col1))
        {
            return;
        }

        float dx = p1.X - p0.X;
        float dy = p1.Y - p0.Y;
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)));

        if (steps <= 0)
        {
            Framebuffer.TestAndSet((int)p0.X, (int)p0.Y, d0, col0.R, col0.G, col0.B);
            return;
        }

        float inverseSteps = 1f / steps;
        for (int i = 0; i <= steps; i++)
        {
            float t = i * inverseSteps;
            int x = (int)MathF.Floor(p0.X + (dx * t));
            int y = (int)MathF.Floor(p0.Y + (dy * t));
            float depth = d0 + ((d1 - d0) * t);
            ViewportColor colour = LerpColour(col0, col1, t);
            Framebuffer.TestAndSet(x, y, depth, colour.R, colour.G, colour.B);
        }
    }

    /// <summary>
    /// Liang–Barsky clip of a screen-space segment to the viewport, carrying depth and colour
    /// along with it.
    /// </summary>
    /// <returns>False when the segment is entirely outside and nothing should be drawn.</returns>
    private bool ClipToViewport(
        ref Vector2 p0,
        ref Vector2 p1,
        ref float d0,
        ref float d1,
        ref ViewportColor col0,
        ref ViewportColor col1)
    {
        float xMax = Framebuffer.Width - 0.001f;
        float yMax = Framebuffer.Height - 0.001f;

        float dx = p1.X - p0.X;
        float dy = p1.Y - p0.Y;
        float enter = 0f;
        float exit = 1f;

        ReadOnlySpan<float> p = [-dx, dx, -dy, dy];
        ReadOnlySpan<float> q = [p0.X, xMax - p0.X, p0.Y, yMax - p0.Y];

        for (int i = 0; i < 4; i++)
        {
            if (MathF.Abs(p[i]) < 1e-9f)
            {
                if (q[i] < 0f)
                {
                    return false;
                }

                continue;
            }

            float r = q[i] / p[i];
            if (p[i] < 0f)
            {
                if (r > exit)
                {
                    return false;
                }

                enter = MathF.Max(enter, r);
            }
            else
            {
                if (r < enter)
                {
                    return false;
                }

                exit = MathF.Min(exit, r);
            }
        }

        if (enter > exit)
        {
            return false;
        }

        Vector2 originalP0 = p0;
        float originalD0 = d0;
        ViewportColor originalCol0 = col0;

        p0 = new Vector2(originalP0.X + (dx * enter), originalP0.Y + (dy * enter));
        p1 = new Vector2(originalP0.X + (dx * exit), originalP0.Y + (dy * exit));
        d0 = originalD0 + ((d1 - originalD0) * enter);
        d1 = originalD0 + ((d1 - originalD0) * exit);
        col0 = LerpColour(originalCol0, col1, enter);
        col1 = LerpColour(originalCol0, col1, exit);
        return true;
    }

    private static void Project(in Vector4 clip, int width, int height, out Vector2 screen, out float depth, out float inverseW)
    {
        inverseW = clip.W > 1e-9f ? 1f / clip.W : 0f;
        screen = new Vector2(
            ((clip.X * inverseW) + 1f) * 0.5f * width,
            (1f - (clip.Y * inverseW)) * 0.5f * height);
        depth = clip.Z * inverseW;
    }

    private static float EdgeFunction(in Vector2 a, in Vector2 b, float x, float y) =>
        ((b.X - a.X) * (y - a.Y)) - ((b.Y - a.Y) * (x - a.X));

    private static ViewportColor LerpColour(in ViewportColor a, in ViewportColor b, float t) => new(
        a.R + ((b.R - a.R) * t),
        a.G + ((b.G - a.G) * t),
        a.B + ((b.B - a.B) * t),
        a.A + ((b.A - a.A) * t));

    private static ViewportColor Blend(in ViewportColor a, in ViewportColor b, float t) => LerpColour(a, b, t);

    /// <summary>A vertex after transformation, carrying what the fragment stage interpolates.</summary>
    private readonly struct ShadedVertex(Vector4 clip, Vector3 world, Vector3 normal)
    {
        internal readonly Vector4 Clip = clip;
        internal readonly Vector3 World = world;
        internal readonly Vector3 Normal = normal;

        internal static ShadedVertex Lerp(in ShadedVertex a, in ShadedVertex b, float t) => new(
            Vector4.Lerp(a.Clip, b.Clip, t),
            Vector3.Lerp(a.World, b.World, t),
            Vector3.Lerp(a.Normal, b.Normal, t));
    }
}

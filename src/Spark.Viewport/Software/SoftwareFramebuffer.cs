using System;

namespace Spark.Viewport.Software;

/// <summary>
/// The colour and depth targets the software rasteriser draws into: a tightly packed RGBA byte
/// buffer and a parallel array of normalised device depths.
/// </summary>
/// <remarks>
/// <para>
/// <b>Row order is top-down</b> — row 0 is the top of the image — which is the opposite of what
/// <c>glReadPixels</c> hands back and the same as every image format Spark writes. The GL path
/// flips in <c>MainWindow</c>; this one never needs to, and stating the convention here is
/// cheaper than rediscovering it from an upside-down thumbnail.
/// </para>
/// <para>
/// Depth is stored in the projection's own normalised range. <see cref="System.Numerics"/>
/// builds a Direct3D-convention perspective matrix, so that range is <c>0..1</c> and not the
/// <c>-1..1</c> an OpenGL reflex expects. <see cref="Far"/> is the cleared value and anything
/// outside <c>0..1</c> is rejected before it reaches the buffer.
/// </para>
/// </remarks>
public sealed class SoftwareFramebuffer
{
    /// <summary>The depth a cleared pixel carries: further away than any drawable fragment.</summary>
    public const float Far = float.PositiveInfinity;

    private byte[] _colour;
    private float[] _depth;

    /// <summary>Creates a framebuffer.</summary>
    /// <param name="width">Width in pixels. Values below one are treated as one.</param>
    /// <param name="height">Height in pixels. Values below one are treated as one.</param>
    public SoftwareFramebuffer(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _colour = new byte[Width * Height * 4];
        _depth = new float[Width * Height];

        // A freshly allocated float array is all zeroes, and zero is the *nearest* representable
        // depth, not the furthest. Left alone, a buffer that has never been cleared rejects every
        // fragment offered to it and renders an empty frame that looks exactly like a scene with
        // nothing in it. Clearing on construction and on resize means the invariant holds from
        // the first instant rather than from the first Render call.
        ClearDepth();
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// The colour target as consecutive R, G, B, A bytes, row 0 first and each row left to right.
    /// </summary>
    public ReadOnlySpan<byte> Pixels => _colour;

    /// <summary>The depth target, one normalised device depth per pixel, in the same order.</summary>
    public ReadOnlySpan<float> Depths => _depth;

    /// <summary>
    /// Resizes the buffers, reallocating only when the pixel count actually changes. The contents
    /// are undefined afterwards; every frame clears before it draws.
    /// </summary>
    /// <param name="width">Width in pixels. Values below one are treated as one.</param>
    /// <param name="height">Height in pixels. Values below one are treated as one.</param>
    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        _colour = new byte[Width * Height * 4];
        _depth = new float[Width * Height];
        ClearDepth();
    }

    /// <summary>Sets every depth to <see cref="Far"/>.</summary>
    public void ClearDepth() => Array.Fill(_depth, Far);

    /// <summary>Reads one pixel as an opaque colour, for tests and for thumbnail sampling.</summary>
    /// <param name="x">Column, 0 at the left.</param>
    /// <param name="y">Row, 0 at the top.</param>
    /// <returns>The pixel's colour, alpha included.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates are outside the buffer.</exception>
    public ViewportColor GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        int i = ((y * Width) + x) * 4;
        const float Scale = 1f / 255f;
        return new ViewportColor(_colour[i] * Scale, _colour[i + 1] * Scale, _colour[i + 2] * Scale, _colour[i + 3] * Scale);
    }

    /// <summary>The depth at one pixel, or <see cref="Far"/> where nothing was drawn.</summary>
    /// <param name="x">Column, 0 at the left.</param>
    /// <param name="y">Row, 0 at the top.</param>
    /// <returns>The normalised device depth.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates are outside the buffer.</exception>
    public float GetDepth(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return _depth[(y * Width) + x];
    }

    /// <summary>Copies the colour target into a caller-supplied span.</summary>
    /// <param name="destination">A span of at least <c>Width * Height * 4</c> bytes.</param>
    /// <exception cref="ArgumentException">The destination is too small.</exception>
    public void CopyPixels(Span<byte> destination)
    {
        if (destination.Length < _colour.Length)
        {
            throw new ArgumentException(
                $"The destination needs {_colour.Length} bytes and has {destination.Length}.",
                nameof(destination));
        }

        _colour.AsSpan().CopyTo(destination);
    }

    /// <summary>
    /// Writes one pixel unconditionally, without a depth test. Used by the background pass, which
    /// runs with depth testing off.
    /// </summary>
    /// <param name="x">Column, 0 at the left.</param>
    /// <param name="y">Row, 0 at the top.</param>
    /// <param name="r">Red, 0..1; clamped.</param>
    /// <param name="g">Green, 0..1; clamped.</param>
    /// <param name="b">Blue, 0..1; clamped.</param>
    internal void SetPixel(int x, int y, float r, float g, float b)
    {
        int i = ((y * Width) + x) * 4;
        _colour[i] = ToByte(r);
        _colour[i + 1] = ToByte(g);
        _colour[i + 2] = ToByte(b);
        _colour[i + 3] = 255;
    }

    /// <summary>
    /// Depth-tests a fragment with <c>GL_LEQUAL</c> and, when it passes, writes both the colour
    /// and the depth. The depth mask is always on, matching the GL path, which enables depth
    /// writes for every pass that is depth-tested at all.
    /// </summary>
    /// <param name="x">Column, 0 at the left.</param>
    /// <param name="y">Row, 0 at the top.</param>
    /// <param name="depth">Normalised device depth. Values outside 0..1 are rejected.</param>
    /// <param name="r">Red, 0..1; clamped.</param>
    /// <param name="g">Green, 0..1; clamped.</param>
    /// <param name="b">Blue, 0..1; clamped.</param>
    /// <returns>True when the fragment passed and was written.</returns>
    internal bool TestAndSet(int x, int y, float depth, float r, float g, float b)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return false;
        }

        // The far test is what stands in for a far clip plane: clipping polygons against it would
        // change the silhouette for no visible gain, where rejecting the fragment costs nothing.
        if (!(depth >= 0f) || depth > 1f)
        {
            return false;
        }

        int p = (y * Width) + x;
        if (depth > _depth[p])
        {
            return false;
        }

        _depth[p] = depth;
        SetPixel(x, y, r, g, b);
        return true;
    }

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
}

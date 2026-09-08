using System;

namespace Spark.UI.Canvas;

/// <summary>
/// The arithmetic behind exporting a picture at a chosen resolution (`E8-T69`).
/// </summary>
/// <remarks>
/// <para>
/// Pure by design, and it lives beside <see cref="CanvasLayout"/> and <see cref="CanvasAlignment"/>
/// for the reason those do: there is no Avalonia type in the signature, so every case is a unit
/// test rather than a window and a gesture. What the dialog above adds is two text boxes and a
/// checkbox, and none of that is arithmetic.
/// </para>
/// <para>
/// <b>The aspect lock is the only rule here that anybody could get wrong</b>, and the way to get it
/// wrong is to derive both numbers from each other: typing in the width sets the height, which
/// re-derives the width, which drifts by a rounding error on every keystroke. So the ratio is taken
/// once, from the size the dialog opened at, and each derivation reads that constant rather than
/// the other box.
/// </para>
/// </remarks>
public static class CanvasExport
{
    /// <summary>
    /// The smallest image the export will make.
    /// </summary>
    /// <remarks>
    /// Small enough to be a thumbnail and large enough that the fit has something to fit into.
    /// A one-pixel export is not a picture of anything and the renderer refuses it anyway.
    /// </remarks>
    public const int MinimumPixels = 16;

    /// <summary>
    /// The largest image the export will make, on either axis.
    /// </summary>
    /// <remarks>
    /// <b>A ceiling rather than no ceiling, because the cost is quadratic and paid in one
    /// allocation.</b> 16,384 square is a gigabyte of 32-bit pixels; the next power of two is four,
    /// and a typo in a text box should not be an out-of-memory. It is also the largest texture
    /// dimension most GPUs will accept, so it is the number the platform would have imposed
    /// anyway.
    /// </remarks>
    public const int MaximumPixels = 16384;

    /// <summary>Brings a requested size inside the permitted range.</summary>
    /// <param name="pixels">The requested size on one axis.</param>
    /// <returns>The size, clamped to <see cref="MinimumPixels"/>..<see cref="MaximumPixels"/>.</returns>
    public static int Clamp(int pixels) => Math.Clamp(pixels, MinimumPixels, MaximumPixels);

    /// <summary>The aspect ratio of a size, as width over height.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The ratio, or 1 when either side is not a usable number.</returns>
    /// <remarks>
    /// <b>One is the answer for a degenerate size rather than an exception</b>, because this is
    /// read while a text box is being typed into: a box holding <c>4</c> on the way to
    /// <c>4000</c> is a moment, not a mistake.
    /// </remarks>
    public static double Aspect(double width, double height) =>
        width > 0 && height > 0 && double.IsFinite(width) && double.IsFinite(height)
            ? width / height
            : 1;

    /// <summary>The height that keeps a given width at a given aspect ratio.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="aspect">The ratio to hold, as width over height.</param>
    /// <returns>The height, clamped to the permitted range.</returns>
    public static int HeightFor(int width, double aspect) =>
        Clamp((int)Math.Round(Clamp(width) / (aspect > 0 && double.IsFinite(aspect) ? aspect : 1)));

    /// <summary>The width that keeps a given height at a given aspect ratio.</summary>
    /// <param name="height">The height in pixels.</param>
    /// <param name="aspect">The ratio to hold, as width over height.</param>
    /// <returns>The width, clamped to the permitted range.</returns>
    public static int WidthFor(int height, double aspect) =>
        Clamp((int)Math.Round(Clamp(height) * (aspect > 0 && double.IsFinite(aspect) ? aspect : 1)));
}

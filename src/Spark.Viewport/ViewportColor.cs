using System;
using System.Globalization;

namespace Spark.Viewport;

/// <summary>
/// A linear-agnostic RGBA colour carried as four floats in the range 0..1. Values are sRGB
/// component values, not linear light: they are the numbers written in the design language
/// document and they are handed to GL unchanged, so a colour named in the specification is the
/// colour that appears on screen.
/// </summary>
/// <param name="R">Red, 0..1.</param>
/// <param name="G">Green, 0..1.</param>
/// <param name="B">Blue, 0..1.</param>
/// <param name="A">Alpha, 0..1, where 1 is opaque.</param>
public readonly record struct ViewportColor(float R, float G, float B, float A)
{
    private const float ByteScale = 1f / 255f;

    /// <summary>Fully transparent black.</summary>
    public static ViewportColor Transparent => new(0f, 0f, 0f, 0f);

    /// <summary>Builds an opaque colour from three 0..255 components.</summary>
    /// <param name="r">Red, 0..255.</param>
    /// <param name="g">Green, 0..255.</param>
    /// <param name="b">Blue, 0..255.</param>
    /// <returns>The colour, with an alpha of 1.</returns>
    public static ViewportColor FromRgb(byte r, byte g, byte b) =>
        new(r * ByteScale, g * ByteScale, b * ByteScale, 1f);

    /// <summary>
    /// Parses a <c>#RRGGBB</c> or <c>RRGGBB</c> string. Provided so viewport colours can be
    /// written as the same hex literals the design language document prints, which is what makes
    /// a mismatch findable by grepping for the hex value.
    /// </summary>
    /// <param name="hex">The hex string, with or without a leading <c>#</c>.</param>
    /// <returns>The parsed opaque colour.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hex"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="hex"/> is not six hex digits.</exception>
    public static ViewportColor FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        ReadOnlySpan<char> digits = hex.AsSpan();
        if (digits.Length > 0 && digits[0] == '#')
        {
            digits = digits[1..];
        }

        if (digits.Length != 6)
        {
            throw new FormatException($"Expected six hex digits, got '{hex}'.");
        }

        byte r = byte.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(digits[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return FromRgb(r, g, b);
    }

    /// <summary>This colour with a different alpha.</summary>
    /// <param name="alpha">The replacement alpha, 0..1.</param>
    /// <returns>A copy carrying <paramref name="alpha"/>.</returns>
    public ViewportColor WithAlpha(float alpha) => this with { A = alpha };
}

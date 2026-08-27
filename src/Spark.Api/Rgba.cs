using System;
using System.Globalization;

namespace Spark.Api;

/// <summary>
/// A straight (non-premultiplied) 8-bit-per-channel colour with alpha.
/// </summary>
/// <remarks>
/// This lives in <c>Spark.Api</c> rather than in the geometry kernel on purpose. Geometry in Spark
/// has no identity, no style and no screen awareness; colour is a display concern, so it sits
/// beside <see cref="Appearance"/> where display concerns belong. That is what keeps
/// <c>Spark.Geometry</c> usable entirely on its own, with no notion of colour at all.
/// </remarks>
public readonly struct Rgba : IEquatable<Rgba>
{
    /// <summary>Creates a colour from its four channels.</summary>
    /// <param name="red">The red channel, 0 to 255.</param>
    /// <param name="green">The green channel, 0 to 255.</param>
    /// <param name="blue">The blue channel, 0 to 255.</param>
    /// <param name="alpha">The alpha channel, 0 transparent to 255 opaque.</param>
    public Rgba(byte red, byte green, byte blue, byte alpha = 255)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    /// <summary>The red channel.</summary>
    public byte Red { get; }

    /// <summary>The green channel.</summary>
    public byte Green { get; }

    /// <summary>The blue channel.</summary>
    public byte Blue { get; }

    /// <summary>The alpha channel: 0 transparent, 255 opaque.</summary>
    public byte Alpha { get; }

    /// <summary>Whether two colours have identical channels.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> when every channel matches.</returns>
    public static bool operator ==(Rgba left, Rgba right) => left.Equals(right);

    /// <summary>Whether two colours differ in any channel.</summary>
    /// <param name="left">The first colour.</param>
    /// <param name="right">The second colour.</param>
    /// <returns><see langword="true"/> when any channel differs.</returns>
    public static bool operator !=(Rgba left, Rgba right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(Rgba other) =>
        Red == other.Red && Green == other.Green && Blue == other.Blue && Alpha == other.Alpha;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Rgba other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Red, Green, Blue, Alpha);

    /// <summary>Renders the colour as <c>#RRGGBBAA</c>.</summary>
    /// <returns>The rendered colour.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"#{Red:X2}{Green:X2}{Blue:X2}{Alpha:X2}");
}

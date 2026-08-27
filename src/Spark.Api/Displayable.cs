using System;

namespace Spark.Api;

/// <summary>
/// A piece of geometry paired with the <see cref="Appearance"/> it should be drawn with.
/// </summary>
/// <remarks>
/// <para>
/// This wrapper is the whole of Spark's styling model, and its existence is what keeps styling out
/// of the geometry kernel. A styling node produces one of these; the viewport unwraps it. Geometry
/// that is never wrapped renders with the viewport's defaults, so <c>Spark.Geometry</c> remains a
/// library with no notion of colour, no screen awareness and no reference to anything above it.
/// </para>
/// <para>
/// The anti-pattern being designed out is a geometry type with a colour field on it, or worse an
/// auto-registering shape that puts itself on a display list when constructed. Both make geometry
/// carry identity, and identity in Spark comes from the graph — the
/// <c>(NodeId, PortIndex, ElementPath)</c> triple — never from the value.
/// </para>
/// </remarks>
public sealed class Displayable
{
    /// <summary>Wraps geometry with an appearance.</summary>
    /// <param name="geometry">
    /// The geometry, which is any kernel value. It is stored by reference and never mutated —
    /// kernel values are immutable — so wrapping is free.
    /// </param>
    /// <param name="appearance">The appearance to draw it with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> or <paramref name="appearance"/> is <see langword="null"/>.</exception>
    public Displayable(object geometry, Appearance appearance)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(appearance);

        Geometry = geometry;
        Appearance = appearance;
    }

    /// <summary>The wrapped geometry.</summary>
    public object Geometry { get; }

    /// <summary>How to draw it.</summary>
    public Appearance Appearance { get; }

    /// <summary>
    /// Returns the geometry inside <paramref name="value"/> if it is a <see cref="Displayable"/>,
    /// and <paramref name="value"/> itself otherwise. Wrapping is not required, so every consumer
    /// of geometry has to cope with both.
    /// </summary>
    /// <param name="value">A graph value that may or may not be wrapped.</param>
    /// <returns>The underlying geometry.</returns>
    public static object? Unwrap(object? value) => value is Displayable displayable ? displayable.Geometry : value;

    /// <summary>Renders the wrapper as the geometry's own text followed by its colour.</summary>
    /// <returns>The rendered value.</returns>
    public override string ToString() => $"{Geometry} ({Appearance.Colour})";
}

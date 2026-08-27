using System;

namespace Spark.Api;

/// <summary>
/// How a piece of geometry should be drawn. Immutable, and entirely separate from the geometry
/// itself.
/// </summary>
/// <remarks>
/// Geometry in Spark carries no colour, no layer, no line weight and no visibility flag, because a
/// value that knows how it is drawn cannot be shared, compared or cached as a value. Style is
/// applied by wrapping geometry in a <see cref="Displayable"/>, which is an explicit node on the
/// canvas rather than a hidden property. Unwrapped geometry renders with the viewport's defaults,
/// so nothing has to be styled for anything to be visible.
/// </remarks>
public sealed class Appearance
{
    /// <summary>Creates an appearance.</summary>
    /// <param name="colour">The colour to draw with.</param>
    /// <param name="lineWeight">The line weight in device pixels. Must be positive and finite.</param>
    /// <param name="visible">Whether the geometry is drawn at all.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineWeight"/> is not positive and finite.</exception>
    public Appearance(Rgba colour, double lineWeight = 1.0, bool visible = true)
    {
        if (!double.IsFinite(lineWeight) || lineWeight <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineWeight), lineWeight, "Line weight must be positive and finite.");
        }

        Colour = colour;
        LineWeight = lineWeight;
        Visible = visible;
    }

    /// <summary>
    /// The appearance geometry gets when nobody has said otherwise: mid grey, one pixel, visible.
    /// </summary>
    public static Appearance Default { get; } = new(new Rgba(128, 128, 128), 1.0, true);

    /// <summary>The colour to draw with.</summary>
    public Rgba Colour { get; }

    /// <summary>The line weight in device pixels.</summary>
    public double LineWeight { get; }

    /// <summary>Whether the geometry is drawn at all.</summary>
    public bool Visible { get; }

    /// <summary>Returns a copy with a different colour.</summary>
    /// <param name="colour">The new colour.</param>
    /// <returns>A new appearance; this one is unchanged.</returns>
    public Appearance WithColour(Rgba colour) => new(colour, LineWeight, Visible);

    /// <summary>Returns a copy with a different line weight.</summary>
    /// <param name="lineWeight">The new line weight, positive and finite.</param>
    /// <returns>A new appearance; this one is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineWeight"/> is not positive and finite.</exception>
    public Appearance WithLineWeight(double lineWeight) => new(Colour, lineWeight, Visible);

    /// <summary>Returns a copy with a different visibility.</summary>
    /// <param name="visible">Whether the geometry is drawn.</param>
    /// <returns>A new appearance; this one is unchanged.</returns>
    public Appearance WithVisible(bool visible) => new(Colour, LineWeight, visible);
}

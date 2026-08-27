using System;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that say how geometry should be drawn.
/// </summary>
/// <remarks>
/// Styling in Spark is a wrapper, not a property: geometry carries no colour, so a styling node
/// produces a <see cref="Displayable"/> and the viewport unwraps it. That is what keeps
/// <c>Spark.Geometry</c> free of any notion of colour and keeps a geometric value comparable and
/// cacheable as a value.
/// </remarks>
[SparkNode(Category = NodeCategories.Display)]
public static class Display
{
    /// <summary>Wraps geometry so the viewport draws it in a given colour.</summary>
    /// <remarks>
    /// The geometry port is declared <c>object</c>, which is rank 0, so feeding it a list of points
    /// replicates the node once per point rather than handing it the list. The colour port is
    /// <see cref="NoReplicationAttribute"/> because fanning a display node out over a list of
    /// colours is never what anyone meant.
    /// </remarks>
    /// <param name="geometry">The geometry to style.</param>
    /// <param name="colour">The colour to draw it in.</param>
    /// <param name="lineWeight">The line weight in device pixels. Must be positive and finite.</param>
    /// <returns>The geometry paired with its appearance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineWeight"/> is not positive and finite.</exception>
    [return: NodePort("displayable")]
    public static Displayable ByGeometryColour(
        object geometry,
        [NoReplication] Rgba colour = default,
        [NoReplication] double lineWeight = 1.0)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        Rgba resolved = colour.Alpha == 0 ? Appearance.Default.Colour : colour;
        return new Displayable(geometry, new Appearance(resolved, lineWeight));
    }
}

/// <summary>
/// Nodes that make colours.
/// </summary>
[SparkNode(Category = NodeCategories.Display)]
public static class Colour
{
    /// <summary>Makes an opaque colour from three 0–255 channels.</summary>
    /// <param name="red">The red channel. Clamped to 0–255.</param>
    /// <param name="green">The green channel. Clamped to 0–255.</param>
    /// <param name="blue">The blue channel. Clamped to 0–255.</param>
    /// <returns>The colour.</returns>
    [return: NodePort("colour")]
    public static Rgba ByRgb(double red = 255, double green = 255, double blue = 255) =>
        new(Channel(red), Channel(green), Channel(blue));

    private static byte Channel(double value) =>
        (byte)System.Math.Clamp(System.Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}

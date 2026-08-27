using System;
using System.Collections.Generic;

namespace Spark.Viewport.Meshes;

/// <summary>
/// Builds the ground plane grid and the world axis lines as a single coloured line batch, so the
/// whole of the viewport's furniture is one draw call rather than four.
/// </summary>
/// <remarks>
/// The grid lies on the world XY plane and the axes point along world X, Y and Z, matching the
/// kernel's <c>Plane.WorldXY</c>. Colours come from
/// <c>docs/help/concepts/design-language.md</c> §8.1 and §8.2: minor grid at 1.26:1 against the
/// ground and major at 1.60:1, both deliberately low and both covered by the scene-element
/// exemption in §4.2.
/// </remarks>
public static class GroundGrid
{
    /// <summary>
    /// Builds a square grid centred on the origin.
    /// </summary>
    /// <param name="halfExtent">
    /// How many minor divisions the grid runs in each direction from the origin. Clamped to at
    /// least one.
    /// </param>
    /// <param name="spacing">The world distance between minor lines. Must be positive.</param>
    /// <param name="majorEvery">
    /// How many minor divisions make a major one. Clamped to at least one; the design language
    /// specifies ten.
    /// </param>
    /// <returns>A line batch holding the grid and the three axis lines.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="spacing"/> is not positive.</exception>
    public static LineBatch Build(int halfExtent = 50, float spacing = 1f, int majorEvery = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);

        halfExtent = Math.Max(1, halfExtent);
        majorEvery = Math.Max(1, majorEvery);

        float limit = halfExtent * spacing;
        List<float> positions = new((halfExtent * 4 + 8) * 6);
        List<float> colours = new((halfExtent * 4 + 8) * 8);

        for (int i = -halfExtent; i <= halfExtent; i++)
        {
            // The axis lines replace the two centre lines, so skip them here rather than
            // drawing a grey line underneath a coloured one and hoping the depth test picks
            // the right one.
            if (i == 0)
            {
                continue;
            }

            ViewportColor colour = i % majorEvery == 0 ? ViewportPalette.GridMajor : ViewportPalette.GridMinor;
            float offset = i * spacing;

            AddSegment(positions, colours, -limit, offset, 0f, limit, offset, 0f, colour);
            AddSegment(positions, colours, offset, -limit, 0f, offset, limit, 0f, colour);
        }

        // Axes. Each runs the full extent in both directions so the ground plane reads as
        // divided into quadrants rather than as a corner.
        AddSegment(positions, colours, -limit, 0f, 0f, limit, 0f, 0f, ViewportPalette.AxisX);
        AddSegment(positions, colours, 0f, -limit, 0f, 0f, limit, 0f, ViewportPalette.AxisY);
        AddSegment(positions, colours, 0f, 0f, 0f, 0f, 0f, limit * 0.25f, ViewportPalette.AxisZ);

        return new LineBatch([.. positions], [.. colours]);
    }

    private static void AddSegment(
        List<float> positions,
        List<float> colours,
        float x0,
        float y0,
        float z0,
        float x1,
        float y1,
        float z1,
        ViewportColor colour)
    {
        positions.Add(x0);
        positions.Add(y0);
        positions.Add(z0);
        positions.Add(x1);
        positions.Add(y1);
        positions.Add(z1);

        for (int i = 0; i < 2; i++)
        {
            colours.Add(colour.R);
            colours.Add(colour.G);
            colours.Add(colour.B);
            colours.Add(colour.A);
        }
    }
}

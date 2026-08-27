using System;

namespace Spark.Viewport.Meshes;

/// <summary>
/// A set of independent line segments with a colour per vertex, drawn as one <c>GL_LINES</c> call.
/// The grid and the three axes share one batch because they change together — never, in practice —
/// and because three extra draw calls per frame for three lines is not a trade worth making.
/// </summary>
public sealed class LineBatch
{
    /// <summary>Creates a batch from arrays the caller must not mutate afterwards.</summary>
    /// <param name="positions">Positions as consecutive x, y, z triples, two per segment.</param>
    /// <param name="colours">Colours as consecutive r, g, b, a quadruples, one per vertex.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The two arrays describe different vertex counts.</exception>
    public LineBatch(float[] positions, float[] colours)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(colours);

        if (positions.Length / 3 != colours.Length / 4)
        {
            throw new ArgumentException("Positions and colours must describe the same vertex count.", nameof(colours));
        }

        PositionData = positions;
        ColourData = colours;
    }

    /// <summary>Positions as consecutive x, y, z triples.</summary>
    public ReadOnlySpan<float> Positions => PositionData;

    /// <summary>Colours as consecutive r, g, b, a quadruples.</summary>
    public ReadOnlySpan<float> Colours => ColourData;

    /// <summary>The number of vertices, which is twice the number of segments.</summary>
    public int VertexCount => PositionData.Length / 3;

    internal float[] PositionData { get; }

    internal float[] ColourData { get; }
}

namespace Spark.UI.Canvas;

/// <summary>
/// An axis-aligned rectangle in canvas world coordinates. Deliberately not
/// <c>Avalonia.Rect</c>: <see cref="SceneIndex"/> and the level-of-detail rules are pure logic
/// and are unit-tested without a UI, and a framework type in their signatures would end that.
/// </summary>
/// <param name="MinX">The left edge.</param>
/// <param name="MinY">The top edge. Canvas y increases downwards, as in every screen coordinate system.</param>
/// <param name="MaxX">The right edge.</param>
/// <param name="MaxY">The bottom edge.</param>
public readonly record struct CanvasBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>The width.</summary>
    public double Width => MaxX - MinX;

    /// <summary>The height.</summary>
    public double Height => MaxY - MinY;

    /// <summary>Builds a rectangle from a corner and a size.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The rectangle.</returns>
    public static CanvasBounds FromSize(double x, double y, double width, double height) =>
        new(x, y, x + width, y + height);

    /// <summary>Whether this rectangle contains a point, edges included.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>True when the point is inside or on the boundary.</returns>
    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>Whether this rectangle wholly contains another, edges included.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>True when every point of <paramref name="other"/> is inside or on the boundary.</returns>
    /// <remarks>
    /// The window half of the box-select pair: <see cref="Intersects"/> is the crossing rule and
    /// this is the stricter one. Edges are included in both, so a node exactly on the boundary is
    /// caught by either — a box dragged flush to a node's edge selecting it in one mode and not the
    /// other would look like a bug whichever way it fell.
    /// </remarks>
    public bool Contains(CanvasBounds other) =>
        other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;

    /// <summary>Whether this rectangle overlaps another, edges included.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>True when the two rectangles share any area or edge.</returns>
    public bool Intersects(CanvasBounds other) =>
        MinX <= other.MaxX && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;
}

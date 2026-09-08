using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make and query axis-aligned bounding boxes.
/// </summary>
/// <remarks>
/// As with <see cref="Plane"/>, the type name shadows the kernel type of the same name so that the
/// generated node reads <c>BoundingBox.FromCorners</c>; the kernel type is written out in full.
/// </remarks>
[SparkNode(Category = NodeCategories.Solid)]
public static class BoundingBox
{
    /// <summary>Makes a box from two opposite corners, in either order.</summary>
    /// <param name="corner">One corner.</param>
    /// <param name="oppositeCorner">The corner diagonally opposite it.</param>
    /// <returns>The box.</returns>
    [return: NodePort("box")]
    [SparkNodeAlias("BoundingBox.ByCorners")]
    public static Spark.Geometry.BoundingBox FromCorners(Point3d corner, Point3d oppositeCorner) =>
        new(corner, oppositeCorner);

    /// <summary>The center of a box.</summary>
    /// <param name="box">The box.</param>
    /// <returns>The center point.</returns>
    [return: NodePort("point")]
    [SparkNodeAlias("BoundingBox.Centre")]
    public static Point3d Center(Spark.Geometry.BoundingBox box) => box.Center;
}

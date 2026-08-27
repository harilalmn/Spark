using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make and combine direction vectors.
/// </summary>
[SparkNode(Category = NodeCategories.Point)]
public static class Vector
{
    /// <summary>Makes a vector from its three components.</summary>
    /// <param name="x">The x component.</param>
    /// <param name="y">The y component.</param>
    /// <param name="z">The z component.</param>
    /// <returns>The vector.</returns>
    [return: NodePort("vector")]
    public static Vector3d ByCoordinates(double x = 0, double y = 0, double z = 0) => new(x, y, z);

    /// <summary>The world x axis, (1, 0, 0).</summary>
    /// <returns>The unit vector.</returns>
    [return: NodePort("vector")]
    public static Vector3d XAxis() => new(1, 0, 0);

    /// <summary>The world y axis, (0, 1, 0).</summary>
    /// <returns>The unit vector.</returns>
    [return: NodePort("vector")]
    public static Vector3d YAxis() => new(0, 1, 0);

    /// <summary>The world z axis, (0, 0, 1). Spark is z-up.</summary>
    /// <returns>The unit vector.</returns>
    [return: NodePort("vector")]
    public static Vector3d ZAxis() => new(0, 0, 1);

    /// <summary>The vector from one point to another.</summary>
    /// <param name="start">The tail.</param>
    /// <param name="end">The head.</param>
    /// <returns>The vector.</returns>
    [return: NodePort("vector")]
    public static Vector3d ByTwoPoints(Point3d start, Point3d end) =>
        new(end.X - start.X, end.Y - start.Y, end.Z - start.Z);

    /// <summary>Multiplies a vector's length by a factor.</summary>
    /// <param name="vector">The vector.</param>
    /// <param name="factor">The factor.</param>
    /// <returns>The scaled vector.</returns>
    [return: NodePort("vector")]
    public static Vector3d Scale(Vector3d vector, double factor) =>
        new(vector.X * factor, vector.Y * factor, vector.Z * factor);

    /// <summary>The length of a vector.</summary>
    /// <param name="vector">The vector.</param>
    /// <returns>The length.</returns>
    [return: NodePort("length")]
    public static double Length(Vector3d vector) => vector.Length;
}

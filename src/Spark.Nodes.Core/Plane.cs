using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make planes.
/// </summary>
/// <remarks>
/// The type name deliberately shadows <see cref="Spark.Geometry.Plane"/> inside this namespace, so
/// the kernel type is written out in full below. The node has to be called <c>Plane.ByOriginNormal</c>
/// and the importer takes that name from the declaring type.
/// </remarks>
[SparkNode(Category = NodeCategories.Solid)]
public static class Plane
{
    /// <summary>Makes a plane from a point on it and its normal.</summary>
    /// <param name="origin">A point on the plane.</param>
    /// <param name="normal">The plane's normal. Need not be a unit vector.</param>
    /// <returns>The plane.</returns>
    [return: NodePort("plane")]
    public static Spark.Geometry.Plane ByOriginNormal(Point3d origin, Vector3d normal) => new(origin, normal);

    /// <summary>The world xy plane: origin at (0, 0, 0), normal along +z.</summary>
    /// <returns>The plane.</returns>
    [return: NodePort("plane")]
    public static Spark.Geometry.Plane XY() => new(new Point3d(0, 0, 0), new Vector3d(0, 0, 1));
}

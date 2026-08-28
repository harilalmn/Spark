using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make circles.
/// </summary>
[SparkNode(Category = NodeCategories.Curve)]
public static class Circle
{
    /// <summary>Makes a circle lying flat in the world xy plane.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <returns>The circle.</returns>
    [return: NodePort("circle")]
    public static Spark.Geometry.Circle ByCentreRadius(Point3d centre, double radius = 1.0) =>
        Spark.Geometry.Circle.ByCentreRadius(centre, radius);

    /// <summary>Makes a circle in a plane, centred on the plane's origin.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <returns>The circle.</returns>
    [return: NodePort("circle")]
    public static Spark.Geometry.Circle ByPlaneRadius(
        Spark.Geometry.Plane plane, double radius = 1.0) =>
        Spark.Geometry.Circle.ByPlaneRadius(plane, radius);

    /// <summary>Makes a circle about an axis.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="normal">The circle's axis. Need not be a unit vector.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <returns>The circle.</returns>
    [return: NodePort("circle")]
    public static Spark.Geometry.Circle ByCentreNormalRadius(
        Point3d centre, Vector3d normal, double radius = 1.0) =>
        Spark.Geometry.Circle.ByCentreNormalRadius(centre, normal, radius);

    /// <summary>Makes the circle that passes through three points.</summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <param name="third">The third point.</param>
    /// <returns>The circle. The three points must not be collinear.</returns>
    [return: NodePort("circle")]
    public static Spark.Geometry.Circle ByThreePoints(
        Point3d first, Point3d second, Point3d third) =>
        Spark.Geometry.Circle.ByThreePoints(first, second, third);
}

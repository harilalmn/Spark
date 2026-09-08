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
    [SparkNodeAlias("Circle.ByCentreRadius")]
    public static Spark.Geometry.Circle FromCentreRadius(Point3d centre, double radius = 1.0) =>
        Spark.Geometry.Circle.FromCentreRadius(centre, radius);

    /// <summary>Makes a circle in a plane, centred on the plane's origin.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <returns>The circle.</returns>
    [return: NodePort("circle")]
    [SparkNodeAlias("Circle.ByPlaneRadius")]
    public static Spark.Geometry.Circle FromPlaneRadius(
        Spark.Geometry.Plane plane, double radius = 1.0) =>
        Spark.Geometry.Circle.FromPlaneRadius(plane, radius);

    /// <summary>Makes a circle about an axis.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="normal">The circle's axis. Need not be a unit vector.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <returns>The circle.</returns>
    [return: NodePort("circle")]
    [SparkNodeAlias("Circle.ByCentreNormalRadius")]
    public static Spark.Geometry.Circle FromCentreNormalRadius(
        Point3d centre, Vector3d normal, double radius = 1.0) =>
        Spark.Geometry.Circle.FromCentreNormalRadius(centre, normal, radius);

    /// <summary>Makes the circle that passes through three points.</summary>
    /// <param name="first">The first point.</param>
    /// <param name="second">The second point.</param>
    /// <param name="third">The third point.</param>
    /// <returns>The circle. The three points must not be collinear.</returns>
    [return: NodePort("circle")]
    [SparkNodeAlias("Circle.ByThreePoints")]
    public static Spark.Geometry.Circle FromThreePoints(
        Point3d first, Point3d second, Point3d third) =>
        Spark.Geometry.Circle.FromThreePoints(first, second, third);
}

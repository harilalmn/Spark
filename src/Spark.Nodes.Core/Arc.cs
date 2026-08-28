using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make circular arcs.
/// </summary>
[SparkNode(Category = NodeCategories.Curve)]
public static class Arc
{
    /// <summary>Makes the arc that runs from the first point through the second to the third.</summary>
    /// <param name="first">The start point.</param>
    /// <param name="second">A point on the arc between the other two.</param>
    /// <param name="third">The end point.</param>
    /// <returns>The arc. The three points must not be collinear.</returns>
    [return: NodePort("arc")]
    public static Spark.Geometry.Arc ByThreePoints(
        Point3d first, Point3d second, Point3d third) =>
        Spark.Geometry.Arc.ByThreePoints(first, second, third);

    /// <summary>Makes an arc from a plane, a radius and two angles.</summary>
    /// <param name="plane">The plane. Its origin is the centre of the arc's circle.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <param name="startAngle">Where the arc starts, measured from the plane's x axis, in degrees.</param>
    /// <param name="sweepAngle">
    /// How far it sweeps, in degrees. Negative sweeps the other way. There is no default: an
    /// unstated sweep would have to be substituted for silently, and a quarter turn nobody asked
    /// for is a worse answer than a port asking to be filled in.
    /// </param>
    /// <returns>The arc.</returns>
    [return: NodePort("arc")]
    public static Spark.Geometry.Arc ByPlaneRadiusAngles(
        Spark.Geometry.Plane plane,
        double radius,
        Angle startAngle,
        Angle sweepAngle) =>
        Spark.Geometry.Arc.ByPlaneRadiusAngles(plane, radius, startAngle, sweepAngle);

    /// <summary>Makes an arc from its centre, its start point and how far to sweep.</summary>
    /// <param name="centre">The centre of the arc's circle.</param>
    /// <param name="startPoint">Where the arc begins. Its distance from the centre is the radius.</param>
    /// <param name="normal">The axis to turn about, by the right-hand rule.</param>
    /// <param name="sweepAngle">How far to sweep, in degrees. Negative sweeps the other way.</param>
    /// <returns>The arc.</returns>
    [return: NodePort("arc")]
    public static Spark.Geometry.Arc ByCentreStartPointSweepAngle(
        Point3d centre,
        Point3d startPoint,
        Vector3d normal,
        Angle sweepAngle) =>
        Spark.Geometry.Arc.ByCentreStartPointSweepAngle(centre, startPoint, normal, sweepAngle);
}

using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make chains of straight segments, including rectangles and regular polygons.
/// </summary>
/// <remarks>
/// Dynamo has a <c>Polygon</c> type and a <c>Rectangle</c> type. Spark has neither: both are closed
/// polylines, and both arrive here as factories rather than as types nobody would otherwise need.
/// See <c>docs/DYNAMO-COVERAGE.md</c> §3.2.
/// </remarks>
[SparkNode(Category = NodeCategories.Curve)]
public static class PolyLine
{
    /// <summary>Makes a polyline through a list of points.</summary>
    /// <param name="points">
    /// At least two points, no two consecutive ones coinciding. This port takes a list rather than
    /// a point, so feeding it a list of points makes one polyline rather than a polyline each.
    /// </param>
    /// <returns>The polyline.</returns>
    [return: NodePort("polyline")]
    [SparkNodeAlias("PolyLine.ByPoints")]
    public static Spark.Geometry.PolyLine FromPoints(IReadOnlyList<Point3d> points) =>
        Spark.Geometry.PolyLine.FromPoints(points);

    /// <summary>Makes a closed polyline through a list of points, joining the last back to the first.</summary>
    /// <param name="points">At least three points, no two consecutive ones coinciding.</param>
    /// <returns>The closed polyline.</returns>
    [return: NodePort("polyline")]
    [SparkNodeAlias("PolyLine.ByClosedPoints")]
    public static Spark.Geometry.PolyLine FromClosedPoints(IReadOnlyList<Point3d> points) =>
        Spark.Geometry.PolyLine.FromClosedPoints(points);

    /// <summary>Where a polyline crosses itself.</summary>
    /// <param name="polyline">The polyline to check.</param>
    /// <returns>One point per crossing. Empty when the outline is clean.</returns>
    /// <remarks>
    /// <b>Meeting at a shared corner is not a crossing</b>, so a well-drawn closed outline returns
    /// nothing. This is what to check an imported or traced boundary with before using it as one.
    /// </remarks>
    [return: NodePort("points")]
    [SparkNodeAlias("Polygon.SelfIntersections")]
    public static IReadOnlyList<Point3d> SelfIntersections(Spark.Geometry.PolyLine polyline)
    {
        CurveIntersections found = polyline.SelfIntersections();
        Point3d[] points = new Point3d[found.Points.Count];

        for (int index = 0; index < points.Length; index++)
        {
            points[index] = found.Points[index].Point;
        }

        return points;
    }

    /// <summary>Makes a closed rectangle centerd on a plane's origin.</summary>
    /// <param name="plane">The plane the rectangle lies in.</param>
    /// <param name="width">The size along the plane's x axis.</param>
    /// <param name="length">The size along the plane's y axis.</param>
    /// <returns>A closed polyline of four segments.</returns>
    [return: NodePort("rectangle")]
    [SparkNodeAlias("PolyLine.ByRectangle")]
    public static Spark.Geometry.PolyLine FromRectangle(
        Spark.Geometry.Plane plane, double width = 1.0, double length = 1.0) =>
        Spark.Geometry.PolyLine.FromRectangle(plane, width, length);

    /// <summary>Makes a closed regular polygon inscribed in a circle.</summary>
    /// <param name="plane">The plane the polygon lies in, centerd on its origin.</param>
    /// <param name="radius">The distance from the center to each corner.</param>
    /// <param name="sides">How many sides. At least three.</param>
    /// <returns>A closed polyline.</returns>
    [return: NodePort("polygon")]
    [SparkNodeAlias("PolyLine.ByRegularPolygon")]
    public static Spark.Geometry.PolyLine FromRegularPolygon(
        Spark.Geometry.Plane plane, double radius = 1.0, int sides = 6) =>
        Spark.Geometry.PolyLine.FromRegularPolygon(plane, radius, sides);
}

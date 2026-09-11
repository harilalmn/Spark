using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make and measure points.
/// </summary>
/// <remarks>
/// Every member here is a plain public static method. There is no registration call, no partial
/// class and no reference to <c>Spark.Engine</c> anywhere in this assembly: the engine's reflection
/// importer discovers these exactly as it discovers a third-party package's, which is what stops
/// the importer from quietly special-casing the first-party library (ADR-0005, rule 2).
/// </remarks>
[SparkNode(Category = NodeCategories.Point)]
public static class Point
{
    /// <summary>Makes a point from its three world coordinates.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="z">The z coordinate.</param>
    /// <returns>The point.</returns>
    [return: NodePort("point")]
    [SparkNodeAlias("Point.ByCoordinates")]
    public static Point3d FromCoordinates(double x = 0, double y = 0, double z = 0) => new(x, y, z);

    /// <summary>
    /// The points with near duplicates removed, keeping the first of each cluster (<c>E2-T16</c>).
    /// </summary>
    /// <param name="points">
    /// The points, in order. This port takes a list, so the node sees the whole list rather than
    /// running once per point.
    /// </param>
    /// <param name="tolerance">How close two points must be to count as one.</param>
    /// <returns>The first point of each cluster, in the order the points came.</returns>
    /// <remarks>
    /// A point is removed only when one already kept is within the tolerance, so a chain of points
    /// each slightly closer than the tolerance keeps every other one rather than collapsing to its
    /// first.
    /// </remarks>
    [return: NodePort("points")]
    public static IReadOnlyList<Point3d> PruneDuplicates(IReadOnlyList<Point3d> points, double tolerance = 0.001) =>
        Point3d.PruneDuplicates(points, tolerance);

    /// <summary>
    /// Makes a point from cylindrical coordinates: a distance from the world z axis, an angle
    /// round it, and a height.
    /// </summary>
    /// <param name="radius">The distance from the z axis, measured in the xy plane. Negative reflects through the axis.</param>
    /// <param name="azimuth">The angle in the xy plane, measured from the x axis towards y, in degrees.</param>
    /// <param name="height">The z coordinate.</param>
    /// <returns>The point.</returns>
    [return: NodePort("point")]
    [SparkNodeAlias("Point.ByCylindricalCoordinates")]
    public static Point3d FromCylindrical(double radius = 1.0, Angle azimuth = default, double height = 0.0) =>
        Point3d.FromCylindrical(radius, azimuth, height);

    /// <summary>
    /// Makes a point from spherical coordinates. <b>The polar angle is measured from the z axis</b>,
    /// not up from the xy plane: zero is straight up and ninety degrees is the xy plane.
    /// </summary>
    /// <param name="radius">The distance from the origin. Negative reflects through the origin.</param>
    /// <param name="azimuth">The angle in the xy plane, measured from the x axis towards y, in degrees.</param>
    /// <param name="polar">The angle away from the z axis, in degrees. Zero is the north pole.</param>
    /// <returns>The point.</returns>
    [return: NodePort("point")]
    [SparkNodeAlias("Point.BySphericalCoordinates")]
    public static Point3d FromSpherical(double radius = 1.0, Angle azimuth = default, Angle polar = default) =>
        Point3d.FromSpherical(radius, azimuth, polar);

    /// <summary>The world origin.</summary>
    /// <returns>The point at (0, 0, 0).</returns>
    [return: NodePort("point")]
    public static Point3d Origin() => new(0, 0, 0);

    /// <summary>Moves a point along a direction.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="direction">The direction to move along. It is normalised first, so its length is ignored.</param>
    /// <param name="distance">How far to move.</param>
    /// <returns>The moved point.</returns>
    [return: NodePort("point")]
    public static Point3d Translate(Point3d point, Vector3d direction, double distance = 1.0)
    {
        if (!direction.TryNormalise(out Vector3d unit))
        {
            return point;
        }

        return new Point3d(
            point.X + (unit.X * distance),
            point.Y + (unit.Y * distance),
            point.Z + (unit.Z * distance));
    }

    /// <summary>The straight-line distance between two points.</summary>
    /// <param name="start">The first point.</param>
    /// <param name="end">The second point.</param>
    /// <returns>The distance.</returns>
    [return: NodePort("distance")]
    public static double Distance(Point3d start, Point3d end) => start.DistanceTo(end);

    /// <summary>
    /// Splits a point into its three coordinates. A multi-output node: the importer turns each
    /// <c>out</c> parameter into an output port of its own.
    /// </summary>
    /// <param name="point">The point to split.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="z">The z coordinate.</param>
    public static void Coordinates(
        Point3d point,
        [NodePort("x")] out double x,
        [NodePort("y")] out double y,
        [NodePort("z")] out double z)
    {
        x = point.X;
        y = point.Y;
        z = point.Z;
    }
}

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
    public static Point3d ByCoordinates(double x = 0, double y = 0, double z = 0) => new(x, y, z);

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

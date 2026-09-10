using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make planes.
/// </summary>
/// <remarks>
/// The type name deliberately shadows <see cref="Spark.Geometry.Plane"/> inside this namespace, so
/// the kernel type is written out in full below. The node has to be called <c>Plane.FromOriginNormal</c>
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
    [SparkNodeAlias("Plane.ByOriginNormal")]
    public static Spark.Geometry.Plane FromOriginNormal(Point3d origin, Vector3d normal) => new(origin, normal);

    /// <summary>
    /// Makes the plane that best fits a list of points, in the least-squares sense. The points do
    /// not have to lie on it; three or more, and not all on one line.
    /// </summary>
    /// <param name="points">
    /// The points to fit through, in order. The order matters only to which way the normal
    /// points: it follows the winding of the points the way a polygon's normal does.
    /// </param>
    /// <returns>The plane, with its origin at the points' centroid.</returns>
    /// <remarks>
    /// <b>Collinear or nearly collinear points are an error rather than an answer.</b> A plane
    /// through points that are almost on a line has a normal decided by rounding, and a
    /// plausible-looking wrong plane is worse than a message.
    /// </remarks>
    [return: NodePort("plane")]
    [SparkNodeAlias("Plane.ByBestFitThroughPoints")]
    public static Spark.Geometry.Plane FromBestFit(IReadOnlyList<Point3d> points) =>
        Spark.Geometry.Plane.FromBestFit(points);

    /// <summary>Makes the plane containing a line and a point that is not on it.</summary>
    /// <param name="line">The line — <see cref="Spark.Geometry.Line"/>, written out because the node type <c>Line</c> shadows it here — whose start becomes the plane's origin and whose direction becomes its x axis.</param>
    /// <param name="point">A point off the line, which picks which plane through the line is meant.</param>
    /// <returns>The plane.</returns>
    /// <remarks>
    /// A point <i>on</i> the line is an error: a line and a point on it lie in infinitely many
    /// planes and none of them is the answer.
    /// </remarks>
    [return: NodePort("plane")]
    [SparkNodeAlias("Plane.ByLineAndPoint")]
    public static Spark.Geometry.Plane FromLineAndPoint(Spark.Geometry.Line line, Point3d point) =>
        Spark.Geometry.Plane.FromLineAndPoint(line, point);

    /// <summary>The world xy plane: origin at (0, 0, 0), normal along +z.</summary>
    /// <returns>The plane.</returns>
    [return: NodePort("plane")]
    public static Spark.Geometry.Plane XY() => new(new Point3d(0, 0, 0), new Vector3d(0, 0, 1));
}

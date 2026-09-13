using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make straight segments.
/// </summary>
/// <remarks>
/// The type name shadows <see cref="Spark.Geometry.Line"/> inside this namespace, so the kernel type
/// is written out in full below — the same arrangement <see cref="Plane"/> uses, and for the same
/// reason: the node has to be called <c>Line.FromStartPointEndPoint</c>, and the importer takes that
/// name from the declaring type.
/// </remarks>
[SparkNode(Category = NodeCategories.Curve)]
public static class Line
{
    /// <summary>Makes a straight segment between two points.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="end">The end point. Must differ from the start.</param>
    /// <returns>The line.</returns>
    [return: NodePort("line")]
    [SparkNodeAlias("Line.ByStartPointEndPoint")]
    public static Spark.Geometry.Line FromStartPointEndPoint(Point3d start, Point3d end) =>
        new(start, end);

    /// <summary>Makes a straight segment from a point, a direction and a length.</summary>
    /// <param name="start">The start point.</param>
    /// <param name="direction">The direction. Normalised first, so its length is ignored.</param>
    /// <param name="length">How long the line is. A negative length runs it the other way.</param>
    /// <returns>The line.</returns>
    [return: NodePort("line")]
    [SparkNodeAlias("Line.ByStartPointDirectionLength")]
    public static Spark.Geometry.Line FromStartPointDirectionLength(
        Point3d start, Vector3d direction, double length = 1.0) =>
        Spark.Geometry.Line.FromStartPointDirectionLength(start, direction, length);

    /// <summary>Makes the line that best fits a list of points.</summary>
    /// <param name="points">
    /// At least two points. They must have a preferred direction: points spread evenly in a ring
    /// or a ball are an error rather than an answer, because every line through their middle fits
    /// as well as every other.
    /// </param>
    /// <returns>The line, trimmed to the span of the points.</returns>
    [return: NodePort("line")]
    [SparkNodeAlias("Line.ByBestFitThroughPoints")]
    public static Spark.Geometry.Line FromBestFit(IReadOnlyList<Point3d> points) =>
        Spark.Geometry.Line.FromBestFit(points);
}

using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make ellipses and elliptical arcs.
/// </summary>
/// <remarks>
/// Dynamo splits these across <c>Ellipse</c> and <c>EllipseArc</c>. Spark has one kernel type,
/// <see cref="EllipseCurve"/>, and the difference is how far it sweeps.
/// </remarks>
[SparkNode(Category = NodeCategories.Curve)]
public static class Ellipse
{
    /// <summary>Makes a full ellipse in a plane, centred on the plane's origin.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="xRadius">The radius along the plane's x axis.</param>
    /// <param name="yRadius">The radius along the plane's y axis.</param>
    /// <returns>The ellipse.</returns>
    [return: NodePort("ellipse")]
    public static EllipseCurve ByPlaneRadii(
        Spark.Geometry.Plane plane, double xRadius = 2.0, double yRadius = 1.0) =>
        EllipseCurve.ByPlaneRadii(plane, xRadius, yRadius);

    /// <summary>Makes part of an ellipse in a plane.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="xRadius">The radius along the plane's x axis.</param>
    /// <param name="yRadius">The radius along the plane's y axis.</param>
    /// <param name="startAngle">Where the curve starts, in degrees.</param>
    /// <param name="sweepAngle">How far it sweeps, in degrees. Negative sweeps the other way.</param>
    /// <returns>The elliptical arc.</returns>
    [return: NodePort("ellipse")]
    public static EllipseCurve ByPlaneRadiiAngles(
        Spark.Geometry.Plane plane,
        double xRadius,
        double yRadius,
        Angle startAngle,
        Angle sweepAngle) =>
        EllipseCurve.ByPlaneRadiiAngles(plane, xRadius, yRadius, startAngle, sweepAngle);
}

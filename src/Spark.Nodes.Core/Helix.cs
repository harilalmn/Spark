using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make helices.
/// </summary>
[SparkNode(Category = NodeCategories.Curve)]
public static class Helix
{
    /// <summary>Makes a helix turning about an axis.</summary>
    /// <param name="origin">
    /// A point on the axis. Any point on the same line gives the same helix: it is
    /// <paramref name="startPoint"/> that fixes the height the rise is measured from.
    /// </param>
    /// <param name="direction">The axis to turn about, by the right-hand rule.</param>
    /// <param name="startPoint">
    /// Where the helix begins. Its distance from the axis is the radius, so it must lie off the
    /// axis.
    /// </param>
    /// <param name="pitch">
    /// How far the helix rises along the axis each full turn. A negative pitch is a left-handed
    /// helix. It may not be zero — a helix that does not rise is an arc.
    /// </param>
    /// <param name="sweepAngle">
    /// How far to turn in total, in degrees, with no upper bound: three turns is 1080. There is no
    /// default, for the reason <c>Arc.FromPlaneRadiusAngles</c> has none — a turn nobody asked for
    /// is a worse answer than a port asking to be filled in. Negative turns the other way.
    /// </param>
    /// <returns>The helix.</returns>
    [return: NodePort("helix")]
    [SparkNodeAlias("Helix.ByAxis")]
    public static Spark.Geometry.Helix FromAxis(
        Point3d origin,
        Vector3d direction,
        Point3d startPoint,
        double pitch,
        Angle sweepAngle) =>
        Spark.Geometry.Helix.FromAxis(origin, direction, startPoint, pitch, sweepAngle);
}

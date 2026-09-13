using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make a smooth curve through points.
/// </summary>
/// <remarks>
/// <para>
/// A NURBS curve made <i>through</i> points is interpolated: the curve passes through every one,
/// and its control points are solved for rather than given. That is what a graph user means by
/// <i>a smooth curve through these</i>, and it is different from a polyline through them, which
/// <see cref="PolyLine.FromPoints"/> makes, and from a curve <i>controlled</i> by them, which the
/// kernel's constructor makes and which touches only the first and last.
/// </para>
/// <para>
/// The type name shadows <see cref="Spark.Geometry.NurbsCurve"/> inside this namespace, so the
/// kernel type is written out in full below.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.Curve)]
public static class NurbsCurve
{
    /// <summary>Makes a smooth curve that passes through every point in a list.</summary>
    /// <param name="points">
    /// At least two points, no two consecutive ones coinciding. This port takes a list rather than
    /// a point, so feeding it a list of points makes one curve rather than a curve each.
    /// </param>
    /// <param name="degree">The degree, from 1 upwards and less than the number of points.</param>
    /// <returns>The curve.</returns>
    [return: NodePort("curve")]
    [SparkNodeAlias("NurbsCurve.ByPoints")]
    public static Spark.Geometry.NurbsCurve InterpolatePoints(IReadOnlyList<Point3d> points, int degree = 3) =>
        Spark.Geometry.NurbsCurve.InterpolatePoints(points, degree);

    /// <summary>
    /// Makes a smooth curve through every point in a list that sets off in one direction and
    /// arrives in another.
    /// </summary>
    /// <param name="points">At least two points, no two consecutive ones coinciding.</param>
    /// <param name="startTangent">The direction the curve leaves the first point in. Its length is ignored.</param>
    /// <param name="endTangent">The direction the curve arrives at the last point in. Its length is ignored.</param>
    /// <param name="degree">The degree, from 2 upwards and at most one more than the number of points.</param>
    /// <returns>The curve.</returns>
    [return: NodePort("curve")]
    [SparkNodeAlias("NurbsCurve.ByPointsTangents")]
    public static Spark.Geometry.NurbsCurve InterpolatePointsWithTangents(
        IReadOnlyList<Point3d> points, Vector3d startTangent, Vector3d endTangent, int degree = 3) =>
        Spark.Geometry.NurbsCurve.InterpolatePointsWithTangents(points, startTangent, endTangent, degree);
}

using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that ask questions of any curve, and that make one curve out of others.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parameters here run from 0 to 1, and the kernel's do not.</b> Every kernel curve carries its
/// own domain — a circle's runs over 2π in radians, a polyline's over one unit per segment — and
/// the node layer converts, because a graph user expects <i>halfway along</i> to be 0.5 whatever
/// the curve is. Anything measured in real distance goes through the <c>AtLength</c> family
/// instead, and on anything but a line and a circle those two answers are different points.
/// </para>
/// <para>
/// The type name shadows <see cref="Spark.Geometry.Curve"/> inside this namespace, so the kernel
/// type is written out in full below.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.Curve)]
public static class Curve
{
    /// <summary>The point a given fraction of the way along a curve's parameter space.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="parameter">
    /// Where to look, from 0 at the start to 1 at the end. This is a fraction of the curve's
    /// parameter range, not of its length: on an ellipse the two are different places.
    /// </param>
    /// <returns>The point.</returns>
    [return: NodePort("point")]
    public static Point3d PointAtParameter(Spark.Geometry.Curve curve, double parameter = 0.5) =>
        curve.PointAt(curve.Domain.Denormalise(parameter));

    /// <summary>The point a given distance along a curve, measured from its start.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="distance">The distance along the curve. Clamped to the curve's length.</param>
    /// <returns>The point.</returns>
    [return: NodePort("point")]
    public static Point3d PointAtLength(Spark.Geometry.Curve curve, double distance = 0.0) =>
        curve.PointAtLength(distance);

    /// <summary>The unit tangent a given fraction of the way along a curve.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="parameter">Where to look, from 0 at the start to 1 at the end.</param>
    /// <returns>A unit vector pointing along the curve.</returns>
    [return: NodePort("tangent")]
    public static Vector3d TangentAtParameter(
        Spark.Geometry.Curve curve, double parameter = 0.5) =>
        curve.TangentAt(curve.Domain.Denormalise(parameter));

    /// <summary>The frame at a point on a curve: x along the tangent, y along the turn.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="parameter">Where to look, from 0 at the start to 1 at the end.</param>
    /// <returns>A right-handed coordinate system sitting on the curve.</returns>
    [return: NodePort("frame")]
    public static CoordinateSystem CoordinateSystemAtParameter(
        Spark.Geometry.Curve curve, double parameter = 0.5) =>
        curve.CoordinateSystemAt(curve.Domain.Denormalise(parameter));

    /// <summary>Divides a curve into pieces of equal length and returns the points between them.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="divisions">How many equal pieces. At least one.</param>
    /// <returns>
    /// One more point than there are divisions, including both ends. The spacing is measured along
    /// the curve, so the points are equally spaced on an ellipse as well as on a circle.
    /// </returns>
    [return: NodePort("points")]
    public static IReadOnlyList<Point3d> DivideEqually(
        Spark.Geometry.Curve curve, int divisions = 10) =>
        curve.DivideEqually(divisions);

    /// <summary>Places points along a curve at a fixed spacing measured along it.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="length">The spacing. Must be positive.</param>
    /// <returns>The points, starting at the curve's start. A trailing remainder is dropped.</returns>
    [return: NodePort("points")]
    public static IReadOnlyList<Point3d> DivideByLength(
        Spark.Geometry.Curve curve, double length = 1.0) =>
        curve.DivideByLength(length);

    /// <summary>How long a curve is, measured along it.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The arc length.</returns>
    [return: NodePort("length")]
    public static double Length(Spark.Geometry.Curve curve) => curve.Length;

    /// <summary>Where a curve starts.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The start point.</returns>
    [return: NodePort("point")]
    public static Point3d StartPoint(Spark.Geometry.Curve curve) => curve.StartPoint;

    /// <summary>Where a curve ends.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The end point.</returns>
    [return: NodePort("point")]
    public static Point3d EndPoint(Spark.Geometry.Curve curve) => curve.EndPoint;

    /// <summary>Whether a curve returns to where it started.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns><see langword="true"/> when the curve is closed.</returns>
    [return: NodePort("closed")]
    public static bool IsClosed(Spark.Geometry.Curve curve) => curve.IsClosed;

    /// <summary>The same curve, traversed the other way.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>A new curve. The original is unchanged.</returns>
    [return: NodePort("curve")]
    public static Spark.Geometry.Curve Reverse(Spark.Geometry.Curve curve) => curve.Reversed();

    /// <summary>The part of a curve between two fractions of the way along it.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="start">Where to start, from 0 to 1.</param>
    /// <param name="end">Where to end, from 0 to 1. May be before the start, which reverses the result.</param>
    /// <returns>A new curve. The original is unchanged.</returns>
    [return: NodePort("curve")]
    public static Spark.Geometry.Curve TrimByParameter(
        Spark.Geometry.Curve curve, double start = 0.0, double end = 1.0) =>
        curve.Trimmed(
            new Interval(curve.Domain.Denormalise(start), curve.Domain.Denormalise(end)));

    /// <summary>Moves a curve by an offset.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="direction">The direction to move along. Normalised first.</param>
    /// <param name="distance">How far to move.</param>
    /// <returns>A new curve. The original is unchanged.</returns>
    [return: NodePort("curve")]
    public static Spark.Geometry.Curve Translate(
        Spark.Geometry.Curve curve, Vector3d direction, double distance = 1.0)
    {
        if (!direction.TryNormalise(out Vector3d unit))
        {
            return curve;
        }

        return curve.TransformedBy(Transform.Translation(unit * distance));
    }

    /// <summary>The points a curve is drawn with, at a given accuracy.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="tolerance">
    /// The furthest the straight pieces may stray from the true curve. Smaller means more points.
    /// </param>
    /// <returns>The points of the polyline approximation, starting and ending on the curve.</returns>
    [return: NodePort("points")]
    public static IReadOnlyList<Point3d> Tessellate(
        Spark.Geometry.Curve curve, double tolerance = 0.001) =>
        curve.Tessellate(new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));
}

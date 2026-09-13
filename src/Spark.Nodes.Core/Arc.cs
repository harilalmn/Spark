using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make circular arcs.
/// </summary>
[SparkNode(Category = NodeCategories.Curve)]
public static class Arc
{
    /// <summary>Makes an arc about a centre, from a start point towards an end point.</summary>
    /// <param name="center">The centre.</param>
    /// <param name="startPoint">Where it begins. Its distance from the centre is the radius.</param>
    /// <param name="endPoint">Which way it ends. See the remarks.</param>
    /// <returns>The arc, sweeping the shorter way round.</returns>
    /// <remarks>
    /// <b>The end point gives a direction, not a distance.</b> Three points do not generally lie on
    /// a circle about the first, so the radius comes from the start and the arc finishes on the ray
    /// towards the end point. Feed it three measured points and the arc will miss the third.
    /// </remarks>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByCenterPointStartPointEndPoint")]
    public static Spark.Geometry.Arc FromCenterStartEnd(Point3d center, Point3d startPoint, Point3d endPoint) =>
        Spark.Geometry.Arc.FromCenterStartEnd(center, startPoint, endPoint);

    /// <summary>Makes an arc between two points that sets off in a given direction.</summary>
    /// <param name="startPoint">Where it begins.</param>
    /// <param name="endPoint">Where it ends. It passes through this exactly.</param>
    /// <param name="startTangent">The direction it leaves the start in.</param>
    /// <returns>The arc.</returns>
    /// <remarks>
    /// <b>Two points and a direction make exactly one arc</b>, so unlike the centre form this one
    /// passes through both points. A direction pointing straight at the other point describes a
    /// line rather than an arc, and is an error.
    /// </remarks>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByStartPointEndPointStartTangent")]
    public static Spark.Geometry.Arc FromStartEndStartTangent(
        Point3d startPoint, Point3d endPoint, Vector3d startTangent) =>
        Spark.Geometry.Arc.FromStartEndStartTangent(startPoint, endPoint, startTangent);

    /// <summary>Makes the arc that runs from the first point through the second to the third.</summary>
    /// <param name="first">The start point.</param>
    /// <param name="second">A point on the arc between the other two.</param>
    /// <param name="third">The end point.</param>
    /// <returns>The arc. The three points must not be collinear.</returns>
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByThreePoints")]
    public static Spark.Geometry.Arc FromThreePoints(
        Point3d first, Point3d second, Point3d third) =>
        Spark.Geometry.Arc.FromThreePoints(first, second, third);

    /// <summary>Makes an arc from a plane, a radius and two angles.</summary>
    /// <param name="plane">The plane. Its origin is the center of the arc's circle.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <param name="startAngle">Where the arc starts, measured from the plane's x axis, in degrees.</param>
    /// <param name="sweepAngle">
    /// How far it sweeps, in degrees. Negative sweeps the other way. There is no default: an
    /// unstated sweep would have to be substituted for silently, and a quarter turn nobody asked
    /// for is a worse answer than a port asking to be filled in.
    /// </param>
    /// <returns>The arc.</returns>
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByPlaneRadiusAngles")]
    public static Spark.Geometry.Arc FromPlaneRadiusAngles(
        Spark.Geometry.Plane plane,
        double radius,
        Angle startAngle,
        Angle sweepAngle) =>
        Spark.Geometry.Arc.FromPlaneRadiusAngles(plane, radius, startAngle, sweepAngle);

    /// <summary>Makes an arc from its center, its start point and how far to sweep.</summary>
    /// <param name="center">The center of the arc's circle.</param>
    /// <param name="startPoint">Where the arc begins. Its distance from the center is the radius.</param>
    /// <param name="normal">The axis to turn about, by the right-hand rule.</param>
    /// <param name="sweepAngle">How far to sweep, in degrees. Negative sweeps the other way.</param>
    /// <returns>The arc.</returns>
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByCentreStartPointSweepAngle")]
    [SparkNodeAlias("Arc.FromCentreStartPointSweepAngle")]
    public static Spark.Geometry.Arc FromCenterStartPointSweepAngle(
        Point3d center,
        Point3d startPoint,
        Vector3d normal,
        Angle sweepAngle) =>
        Spark.Geometry.Arc.FromCenterStartPointSweepAngle(center, startPoint, normal, sweepAngle);

    /// <summary>Makes the arc that best fits a list of points.</summary>
    /// <param name="points">
    /// At least three points, <b>in order along the arc</b>. Points that double back are an error:
    /// they still have a circle through them, so only the sweep would be wrong, which is worse
    /// than a message.
    /// </param>
    /// <returns>The arc, spanning the points from the first to the last.</returns>
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByBestFitThroughPoints")]
    public static Spark.Geometry.Arc FromBestFit(IReadOnlyList<Point3d> points) =>
        Spark.Geometry.Arc.FromBestFit(points);
}

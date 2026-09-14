using System;
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

    /// <summary>The points where two curves cross or touch (<c>E2-T11</c>).</summary>
    /// <param name="curve">The first curve.</param>
    /// <param name="other">The second curve.</param>
    /// <returns>
    /// The points, in order along the first curve. A stretch where the two run together is not a
    /// point; that is what <see cref="OverlapWith"/> returns.
    /// </returns>
    /// <remarks>
    /// Lines, circles and arcs are answered exactly; any other pair by sampling and refining, which
    /// reports an overlap too short to sample as a point.
    /// </remarks>
    [return: NodePort("points")]
    public static IReadOnlyList<Point3d> IntersectWith(Spark.Geometry.Curve curve, Spark.Geometry.Curve other)
    {
        CurveIntersections found = curve.IntersectWith(other);
        List<Point3d> points = new(found.Points.Count);

        foreach (CurveIntersectionPoint point in found.Points)
        {
            points.Add(point.Point);
        }

        return points;
    }

    /// <summary>
    /// The points where a curve crosses a surface (<c>E2-T70</c>).
    /// </summary>
    /// <param name="curve">The curve.</param>
    /// <param name="surface">The surface to cut it with.</param>
    /// <returns>The points, in order along the curve; empty when they do not meet.</returns>
    /// <remarks>
    /// <para>
    /// The kernel's answer carries the parameter on the curve and the <c>UV</c> on the surface beside
    /// each point; this node gives the points, because a node that returns three parallel lists is a
    /// node nobody can wire. A graph that needs the parameters uses a code block over
    /// <c>Curve.IntersectWith(Surface)</c>.
    /// </para>
    /// <para>
    /// A curve lying <i>in</i> the surface gives no points, because it is a shared curve rather than a
    /// set of crossings.
    /// </para>
    /// </remarks>
    [return: NodePort("points")]
    public static IReadOnlyList<Point3d> IntersectWithSurface(Spark.Geometry.Curve curve, Spark.Geometry.Surface surface)
    {
        CurveSurfaceIntersections found = curve.IntersectWith(surface);
        List<Point3d> points = new(found.Points.Count);

        foreach (CurveSurfaceIntersectionPoint point in found.Points)
        {
            points.Add(point.Point);
        }

        return points;
    }

    /// <summary>
    /// The stretches where two curves run together, as pieces of the first (<c>E2-T11</c>).
    /// </summary>
    /// <param name="curve">The first curve, which the pieces are cut from.</param>
    /// <param name="other">The second curve.</param>
    /// <returns>One curve per shared stretch, each a piece of <paramref name="curve"/>.</returns>
    [return: NodePort("curves")]
    public static IReadOnlyList<Spark.Geometry.Curve> OverlapWith(Spark.Geometry.Curve curve, Spark.Geometry.Curve other)
    {
        CurveIntersections found = curve.IntersectWith(other);
        List<Spark.Geometry.Curve> pieces = new(found.Overlaps.Count);

        foreach (CurveOverlap overlap in found.Overlaps)
        {
            pieces.Add(curve.Trimmed(overlap.OnA));
        }

        return pieces;
    }

    /// <summary>Rounds the corner where two curves cross, with an arc of a given radius.</summary>
    /// <param name="first">The curve the fillet leaves.</param>
    /// <param name="second">The curve it arrives at.</param>
    /// <param name="radius">The fillet radius. Positive, and small enough to fit the corner.</param>
    /// <param name="normal">
    /// The normal of the plane the two curves lie in. It is asked for rather than worked out,
    /// because two straight curves have no plane of their own to read.
    /// </param>
    /// <returns>The fillet arc, then the two curves trimmed back to meet it — three curves that join.</returns>
    /// <remarks>
    /// <b>The arc is tangent to both curves, not merely touching them at the right places.</b> A
    /// radius too large for the corner is an error rather than an arc that overshoots.
    /// </remarks>
    [return: NodePort("curves")]
    [SparkNodeAlias("Arc.ByFillet")]
    public static IReadOnlyList<Spark.Geometry.Curve> Fillet(
        Spark.Geometry.Curve first,
        Spark.Geometry.Curve second,
        double radius = 1.0,
        Vector3d normal = default)
    {
        (Spark.Geometry.Arc fillet, Spark.Geometry.Curve trimmedFirst, Spark.Geometry.Curve trimmedSecond) =
            CurveOffset.Fillet(
                first, second, radius, normal.LengthSquared > 0.0 ? normal : Vector3d.ZAxis);

        return [trimmedFirst, fillet, trimmedSecond];
    }

    /// <summary>Rounds a corner with an arc whose radius is decided by a third curve.</summary>
    /// <param name="first">The curve the arc leaves.</param>
    /// <param name="second">The curve it arrives at.</param>
    /// <param name="tangentTo">The curve that sets the radius by being tangent to it as well.</param>
    /// <param name="normal">The normal of the plane all three curves lie in.</param>
    /// <returns>The arc alone. Nothing is trimmed.</returns>
    /// <remarks>
    /// <b>Where <c>Fillet</c> is given the radius, this one works it out.</b> Three curves have
    /// several circles tangent to all of them — a triangle has four — and the one nearest the
    /// corner they enclose is the answer. For the three sides of a triangle that is its incircle.
    /// </remarks>
    [return: NodePort("arc")]
    [SparkNodeAlias("Arc.ByFilletTangentToCurve")]
    public static Spark.Geometry.Arc FilletTangentTo(
        Spark.Geometry.Curve first,
        Spark.Geometry.Curve second,
        Spark.Geometry.Curve tangentTo,
        Vector3d normal = default) =>
        CurveOffset.FilletTangentTo(
            first, second, tangentTo, normal.LengthSquared > 0.0 ? normal : Vector3d.ZAxis);

    /// <summary>Places a point every so far along the curve, measured in a straight line.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="chordLength">
    /// The straight-line spacing. <b>Not the same as the distance along the curve</b> — see the
    /// remarks.
    /// </param>
    /// <returns>The points, starting at the curve's start. The remainder at the end is dropped.</returns>
    /// <remarks>
    /// <b>Use this when the pieces between the points have to be straight and equal</b> — equal
    /// struts, equal panels, a chain of equal members. Use <c>DivideByLength</c> when what matters
    /// is the distance walked along the curve. On a line the two agree; on anything else they do
    /// not.
    /// </remarks>
    [return: NodePort("points")]
    [SparkNodeAlias("Curve.PointsAtChordLength")]
    public static IReadOnlyList<Point3d> DivideByChordLength(
        Spark.Geometry.Curve curve, double chordLength = 1.0) =>
        curve.DivideByChordLength(chordLength);

    /// <summary>Divides the curve into pieces whose straight-line separations are all equal.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="divisions">How many pieces. At least one.</param>
    /// <returns>One more point than divisions, including both ends.</returns>
    /// <remarks>
    /// <b>Equal <i>chords</i>, not equal arc lengths.</b> Around a circle this gives the inscribed
    /// polygon. <c>DivideEqually</c> gives the other answer, and on a curve that is not a line the
    /// two are different points.
    /// </remarks>
    [return: NodePort("points")]
    [SparkNodeAlias("Curve.PointsAtEqualChordLength")]
    public static IReadOnlyList<Point3d> DivideEquallyByChord(
        Spark.Geometry.Curve curve, int divisions = 4) =>
        curve.DivideEquallyByChord(divisions);

    /// <summary>Divides the curve by distance along it, starting from a parameter.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="distance">The spacing, measured along the curve.</param>
    /// <param name="fromParameter">
    /// Where to start, from 0 to 1 through the curve's own parameter space. Outside that it is
    /// clamped rather than refused.
    /// </param>
    /// <returns>The points from there onwards, including the starting point.</returns>
    [return: NodePort("points")]
    [SparkNodeAlias("Curve.DivideByDistanceFromParameter")]
    public static IReadOnlyList<Point3d> DivideByLengthFromParameter(
        Spark.Geometry.Curve curve, double distance = 1.0, double fromParameter = 0.0) =>
        curve.DivideByLength(distance, curve.Domain.Denormalise(System.Math.Clamp(fromParameter, 0.0, 1.0)));

    /// <summary>Lengthens a curve past its own ends.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="atStart">How far to add before the start, measured along the curve.</param>
    /// <param name="atEnd">How far to add after the end.</param>
    /// <returns>The longer curve.</returns>
    /// <remarks>
    /// <b>A curve that can continue itself does</b> — a line stays a line, an arc becomes a wider
    /// arc, a helix gains turns. Anything else is extended along its own end tangent, so the result
    /// is the curve with straight tails on it. A <b>closed</b> curve has no ends and is an error.
    /// </remarks>
    [return: NodePort("curve")]
    [SparkNodeAlias("Curve.Extend")]
    [SparkNodeAlias("Curve.ExtendStart")]
    [SparkNodeAlias("Curve.ExtendEnd")]
    public static Spark.Geometry.Curve Extended(
        Spark.Geometry.Curve curve, double atStart = 0.0, double atEnd = 1.0) =>
        curve.Extended(atStart, atEnd);

    /// <summary>Pulls a curve flat onto a plane.</summary>
    /// <param name="curve">The curve.</param>
    /// <param name="plane">The plane to pull onto.</param>
    /// <param name="tolerance">The tolerance for converting the curve first.</param>
    /// <returns>The pulled curve, lying wholly in the plane.</returns>
    /// <remarks>
    /// <b>Pulling is not projecting along a direction.</b> Every point moves to the NEAREST point
    /// of the plane, which is along the plane's own normal. A pulled line is still straight and a
    /// pulled circle is generally an ellipse.
    /// </remarks>
    [return: NodePort("curve")]
    [SparkNodeAlias("Curve.PullOntoPlane")]
    public static Spark.Geometry.Curve PulledOntoPlane(
        Spark.Geometry.Curve curve, Spark.Geometry.Plane plane, double tolerance = 1e-6) =>
        curve.PulledOntoPlane(plane, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));
}

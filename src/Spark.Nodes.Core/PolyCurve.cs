using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that join curves into a single chain.
/// </summary>
[SparkNode(Category = NodeCategories.Curve)]
public static class PolyCurve
{
    /// <summary>Joins curves end to end into one curve.</summary>
    /// <param name="curves">
    /// The curves in order. Each one has to start where the previous one ended, to within the
    /// tolerance. A polycurve among them is flattened into its own pieces.
    /// </param>
    /// <param name="tolerance">
    /// How far apart consecutive ends may be before the join is refused. A gap accepted silently
    /// would give a curve whose length is not the length of the path it draws.
    /// </param>
    /// <returns>The joined curve.</returns>
    [return: NodePort("polycurve")]
    [SparkNodeAlias("PolyCurve.ByJoinedCurves")]
    public static Spark.Geometry.PolyCurve FromJoinedCurves(
        IReadOnlyList<Spark.Geometry.Curve> curves, double tolerance = 1e-6) =>
        Spark.Geometry.PolyCurve.FromJoinedCurves(
            curves, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));

    /// <summary>The closed outline of a curve given a thickness, spread sideways in a plane.</summary>
    /// <param name="curve">The centre line. Must be open.</param>
    /// <param name="thickness">How wide the result is. Half of it goes to each side.</param>
    /// <param name="planeNormal">The normal of the plane the thickening happens in.</param>
    /// <param name="tolerance">The tolerance for the offsets and the join.</param>
    /// <returns>The closed outline: one side, a cap, the other side, a cap.</returns>
    /// <remarks>
    /// <b>The curve is the centre line and the ends are capped with straight lines.</b> A closed
    /// curve is refused, because thickened it is an annulus — two loops, not one. Thickening a
    /// wiggly curve by more than twice its smallest radius of curvature gives an outline that
    /// crosses itself, which is not repaired here.
    /// </remarks>
    [return: NodePort("polycurve")]
    [SparkNodeAlias("PolyCurve.ByThickeningCurve")]
    public static Spark.Geometry.PolyCurve FromThickenedCurve(
        Spark.Geometry.Curve curve,
        double thickness,
        Vector3d planeNormal,
        double tolerance = 1e-6) =>
        Spark.Geometry.PolyCurve.FromThickenedCurve(
            curve, thickness, planeNormal, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));

    /// <summary>The closed outline of a curve given a thickness, spread along a direction.</summary>
    /// <param name="curve">The centre line. Must be open.</param>
    /// <param name="thickness">How wide the result is. Half of it goes to each side.</param>
    /// <param name="direction">The direction to thicken along.</param>
    /// <param name="tolerance">The tolerance for the join.</param>
    /// <returns>The closed outline.</returns>
    /// <remarks>
    /// <b>This is a translation rather than an offset, so it is exact for every curve type.</b>
    /// Where <c>FromThickenedCurve</c> spreads the ribbon sideways within a plane, this one moves
    /// it bodily along the direction given — flat against standing up, from the same arguments.
    /// </remarks>
    [return: NodePort("polycurve")]
    [SparkNodeAlias("PolyCurve.ByThickeningCurveNormal")]
    public static Spark.Geometry.PolyCurve FromThickenedCurveAlong(
        Spark.Geometry.Curve curve,
        double thickness,
        Vector3d direction,
        double tolerance = 1e-6) =>
        Spark.Geometry.PolyCurve.FromThickenedCurveAlong(
            curve, thickness, direction, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));

    /// <summary>Sorts a heap of curves into as many chains as it holds.</summary>
    /// <param name="curves">The curves, in any order and drawn in any direction.</param>
    /// <param name="tolerance">How near two ends must be to count as joined.</param>
    /// <returns>One polycurve per chain, in the order their first curve appeared.</returns>
    /// <remarks>
    /// <b>This is the join that does not refuse a gap.</b> <c>FromJoinedCurves</c> builds one chain
    /// and refuses a gap; this builds several and uses a gap as the boundary between them. A curve
    /// that touches nothing comes back as a chain of one, and a link drawn the other way round is
    /// turned rather than left out.
    /// </remarks>
    [return: NodePort("polycurves")]
    [SparkNodeAlias("PolyCurve.ByGroupedCurves")]
    public static Spark.Geometry.PolyCurve[] FromGroupedCurves(
        IReadOnlyList<Spark.Geometry.Curve> curves, double tolerance = 1e-6) =>
        Spark.Geometry.PolyCurve.FromGroupedCurves(
            curves, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));

    /// <summary>Rounds every corner of a chain to a fillet of the same radius.</summary>
    /// <param name="polycurve">The chain.</param>
    /// <param name="radius">The fillet radius. Positive.</param>
    /// <param name="tolerance">The tolerance for the plane, the corners and the offsets.</param>
    /// <returns>The rounded chain.</returns>
    /// <remarks>
    /// <b>A corner too tight for the radius is left sharp rather than losing the whole chain</b>, so
    /// one bad corner in twenty comes back with nineteen rounded. The chain has to be planar; a
    /// closed one has its wrap corner rounded too.
    /// </remarks>
    [return: NodePort("polycurve")]
    [SparkNodeAlias("PolyCurve.Fillet")]
    public static Spark.Geometry.PolyCurve Filleted(
        Spark.Geometry.PolyCurve polycurve, double radius, double tolerance = 1e-6) =>
        polycurve.Filleted(radius, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));

    /// <summary>Closes an open chain with an arc, a straight run and a second arc, all tangent.</summary>
    /// <param name="polycurve">The open chain.</param>
    /// <param name="startRadius">The radius of the arc arriving at the chain's start. Positive.</param>
    /// <param name="endRadius">The radius of the arc leaving the chain's end. Positive.</param>
    /// <param name="tolerance">The tolerance for the plane and for the crossing test.</param>
    /// <returns>The closed chain.</returns>
    /// <remarks>
    /// <b>Of the four closures that exist, the one that does not cut through the shape is taken</b>,
    /// and the shortest of those — because the shortest closure overall is free to cross the outline
    /// it is closing. The chain has to be planar and has to be open.
    /// </remarks>
    [return: NodePort("polycurve")]
    [SparkNodeAlias("PolyCurve.CloseWithLineAndTangentArcs")]
    public static Spark.Geometry.PolyCurve ClosedWithLineAndTangentArcs(
        Spark.Geometry.PolyCurve polycurve,
        double startRadius,
        double endRadius,
        double tolerance = 1e-6) =>
        polycurve.ClosedWithLineAndTangentArcs(
            startRadius, endRadius, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));
}

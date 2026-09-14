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
}

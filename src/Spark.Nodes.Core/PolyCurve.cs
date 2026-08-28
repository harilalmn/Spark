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
    public static Spark.Geometry.PolyCurve ByJoinedCurves(
        IReadOnlyList<Spark.Geometry.Curve> curves, double tolerance = 1e-6) =>
        Spark.Geometry.PolyCurve.ByJoinedCurves(
            curves, new Tolerance(tolerance, Angle.FromDegrees(0.001), 1e-12));
}

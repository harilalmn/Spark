using System.Collections.Generic;

namespace Spark.Engine;

/// <summary>
/// One node of a run has finished and produced output (<c>E9-T7</c>).
/// </summary>
/// <param name="Node">The node.</param>
/// <param name="Outputs">
/// Its outputs, one per output port - the same values the run's <see cref="EvaluationResult"/> will
/// hold for it.
/// </param>
/// <param name="FromCache">Whether they came from the evaluation cache rather than a fresh call.</param>
/// <remarks>
/// What <see cref="GraphEvaluator"/> reports as a run goes, so a viewport can draw a node's geometry
/// without waiting for the slowest node in the graph.
/// </remarks>
public readonly record struct NodeCompleted(NodeId Node, IReadOnlyList<object?> Outputs, bool FromCache);

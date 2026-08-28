using System;
using Spark.Engine;

namespace Spark.Benchmarks;

/// <summary>
/// Graphs built for measurement, and built to evaluate cleanly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>DemoGraphs.Synthetic</c>, and the difference is the point.</b> That graph exists to
/// put two thousand nodes in front of the renderer and is deliberately never evaluated — it wires
/// whatever ports will accept each other, so a good fraction of its nodes error on their default
/// literals. Benchmarking it measured exception throwing rather than evaluation, and produced the
/// tell that gave it away: fifty nodes cold were *slower* than five hundred.
/// </para>
/// <para>
/// What is here instead is a chain of replicating nodes over one list. Every node in it runs, none
/// of them error, and every link exercises the path the engine actually spends its time on —
/// replication, then marshalling a list in and out of the CLR types a node signature asks for.
/// </para>
/// </remarks>
internal static class BenchmarkGraphs
{
    /// <summary>
    /// A range of numbers fed through a chain of maths nodes, each replicating over the whole list.
    /// </summary>
    /// <param name="library">The imported node library.</param>
    /// <param name="length">How many nodes are in the chain, including the range.</param>
    /// <param name="elements">How many numbers the range produces.</param>
    /// <returns>The graph, ready to evaluate.</returns>
    internal static Graph Chain(NodeLibrary library, int length, int elements)
    {
        ArgumentNullException.ThrowIfNull(library);

        Graph graph = new();

        NodeInstance range = graph.AddNode(library.ByName("Number.Range"));
        graph.SetLiteral(range.Id, 0, 0.0);
        graph.SetLiteral(range.Id, 1, (double)Math.Max(1, elements - 1));
        graph.SetLiteral(range.Id, 2, 1.0);

        NodeInstance previous = range;

        for (int index = 1; index < Math.Max(1, length); index++)
        {
            // Sin, then Add 1, then Multiply by 2, repeating. All three take and return one
            // number, so each replicates elementwise over the list arriving on its input — which
            // is the engine's hot path rather than a decorative chain.
            NodeInstance node = (index % 3) switch
            {
                0 => graph.AddNode(library.ByName("Math.Sin")),
                1 => graph.AddNode(library.ByName("Math.Add")),
                _ => graph.AddNode(library.ByName("Math.Multiply")),
            };

            if (index % 3 != 0)
            {
                graph.SetLiteral(node.Id, 1, index % 3 == 1 ? 1.0 : 2.0);
            }

            graph.TryConnect(previous.Id, 0, node.Id, 0);
            previous = node;
        }

        return graph;
    }
}

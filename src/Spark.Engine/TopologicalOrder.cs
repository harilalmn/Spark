using System;
using System.Collections.Generic;
using System.Linq;

namespace Spark.Engine;

/// <summary>
/// A graph sorted into evaluation levels, plus whatever could not be sorted because it sits in a
/// cycle.
/// </summary>
/// <remarks>
/// <para>
/// Everything in one level is independent of everything else in the same level, which is what makes
/// a level the unit of parallelism: the scheduler runs a level's nodes in any order, on any threads,
/// and moves on.
/// </para>
/// <para>
/// <b>This never hangs and never recurses.</b> Kahn's algorithm terminates on any input, cyclic or
/// not, and what it cannot order is reported rather than looped over. That matters because a cycle
/// reaches the evaluator only through a loaded file, which is exactly the situation where hanging
/// would look like the product being broken rather than the file being wrong.
/// </para>
/// </remarks>
public sealed class TopologicalOrder
{
    private TopologicalOrder(
        IReadOnlyList<IReadOnlyList<NodeId>> levels,
        IReadOnlyList<NodeId> cyclicNodes,
        IReadOnlyList<NodeId> downstreamOfCycles)
    {
        Levels = levels;
        CyclicNodes = cyclicNodes;
        DownstreamOfCycles = downstreamOfCycles;
    }

    /// <summary>The evaluation levels, outermost dependency first.</summary>
    public IReadOnlyList<IReadOnlyList<NodeId>> Levels { get; }

    /// <summary>
    /// The nodes that lie on a cycle, or on a path between two cycles. Every one of them errors, and
    /// the rest of the graph still evaluates.
    /// </summary>
    public IReadOnlyList<NodeId> CyclicNodes { get; }

    /// <summary>
    /// The nodes that are downstream of a cycle without being part of one. They have nothing wrong
    /// with them and get no diagnostic — they are simply never evaluated.
    /// </summary>
    public IReadOnlyList<NodeId> DownstreamOfCycles { get; }

    /// <summary>Whether the graph contains a cycle.</summary>
    public bool HasCycle => CyclicNodes.Count > 0;

    /// <summary>Sorts a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static TopologicalOrder Of(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        IReadOnlyList<NodeInstance> nodes = graph.Nodes();

        Dictionary<NodeId, HashSet<NodeId>> successors = [];
        Dictionary<NodeId, HashSet<NodeId>> predecessors = [];

        foreach (NodeInstance node in nodes)
        {
            successors[node.Id] = [];
            predecessors[node.Id] = [];
        }

        foreach (Wire wire in graph.Wires())
        {
            // A node may feed another through several wires; that is one dependency, not several.
            successors[wire.Source].Add(wire.Target);
            predecessors[wire.Target].Add(wire.Source);
        }

        Dictionary<NodeId, int> remainingDependencies = [];
        foreach (NodeInstance node in nodes)
        {
            remainingDependencies[node.Id] = predecessors[node.Id].Count;
        }

        List<IReadOnlyList<NodeId>> levels = [];
        List<NodeId> current = [];

        foreach (NodeInstance node in nodes)
        {
            if (remainingDependencies[node.Id] == 0)
            {
                current.Add(node.Id);
            }
        }

        HashSet<NodeId> ordered = [];

        while (current.Count > 0)
        {
            current.Sort(CompareById);
            levels.Add(current);

            List<NodeId> next = [];
            foreach (NodeId id in current)
            {
                ordered.Add(id);

                foreach (NodeId successor in successors[id])
                {
                    if (--remainingDependencies[successor] == 0)
                    {
                        next.Add(successor);
                    }
                }
            }

            current = next;
        }

        HashSet<NodeId> unordered = [];
        foreach (NodeInstance node in nodes)
        {
            if (!ordered.Contains(node.Id))
            {
                unordered.Add(node.Id);
            }
        }

        (List<NodeId> cyclic, List<NodeId> downstream) = SeparateCyclesFromTails(unordered, successors);

        return new TopologicalOrder(levels, cyclic, downstream);
    }

    /// <summary>
    /// Peels the tails off the unordered remainder. What is left has both a predecessor and a
    /// successor inside the remainder, which is the definition of being caught in a cycle; what came
    /// off is downstream of one and blameless.
    /// </summary>
    private static (List<NodeId> Cyclic, List<NodeId> Downstream) SeparateCyclesFromTails(
        HashSet<NodeId> unordered,
        Dictionary<NodeId, HashSet<NodeId>> successors)
    {
        HashSet<NodeId> remaining = [.. unordered];
        List<NodeId> downstream = [];

        bool peeled = true;
        while (peeled)
        {
            peeled = false;

            foreach (NodeId id in remaining.ToArray())
            {
                bool hasSuccessorInside = false;
                foreach (NodeId successor in successors[id])
                {
                    if (remaining.Contains(successor))
                    {
                        hasSuccessorInside = true;
                        break;
                    }
                }

                if (!hasSuccessorInside)
                {
                    remaining.Remove(id);
                    downstream.Add(id);
                    peeled = true;
                }
            }
        }

        List<NodeId> cyclic = [.. remaining];
        cyclic.Sort(CompareById);
        downstream.Sort(CompareById);

        return (cyclic, downstream);
    }

    private static int CompareById(NodeId left, NodeId right) =>
        left.Value.CompareTo(right.Value);
}

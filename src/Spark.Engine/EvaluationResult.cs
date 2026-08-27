using System;
using System.Collections.Generic;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// What happened to one node during a run.
/// </summary>
public enum NodeState
{
    /// <summary>
    /// The node was never run, because something it depends on produced no output. It is greyed on
    /// the canvas and carries no diagnostic of its own: there is nothing wrong with it.
    /// </summary>
    NotEvaluated = 0,

    /// <summary>The node produced output and said nothing.</summary>
    Evaluated = 1,

    /// <summary>
    /// The node produced output and raised a warning. Downstream still evaluates — a warning means
    /// output with caveats.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// The node produced no output. Downstream is <see cref="NotEvaluated"/>, never given errors of
    /// its own: cascading turns a one-node problem into a fifty-error wall that hides the cause.
    /// </summary>
    Error = 3,

    /// <summary>
    /// The node lies on a cycle found when the graph was loaded. Every node in the cycle carries
    /// this, and the rest of the graph still evaluates.
    /// </summary>
    Cycle = 4,
}

/// <summary>
/// The outcome of evaluating a graph.
/// </summary>
public sealed class EvaluationResult
{
    private readonly Dictionary<NodeId, IReadOnlyList<object?>> _outputs;
    private readonly Dictionary<NodeId, NodeState> _states;
    private readonly Dictionary<NodeId, List<SparkDiagnostic>> _byNode;

    internal EvaluationResult(
        Dictionary<NodeId, IReadOnlyList<object?>> outputs,
        Dictionary<NodeId, NodeState> states,
        List<SparkDiagnostic> diagnostics,
        int nodesEvaluated,
        int cacheHits)
    {
        _outputs = outputs;
        _states = states;
        Diagnostics = diagnostics;
        NodesEvaluated = nodesEvaluated;
        CacheHits = cacheHits;

        _byNode = [];
        foreach (SparkDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.NodeId is not { } id)
            {
                continue;
            }

            NodeId nodeId = new(id);
            if (!_byNode.TryGetValue(nodeId, out List<SparkDiagnostic>? list))
            {
                list = [];
                _byNode[nodeId] = list;
            }

            list.Add(diagnostic);
        }
    }

    /// <summary>Every diagnostic raised during the run, in evaluation order.</summary>
    public IReadOnlyList<SparkDiagnostic> Diagnostics { get; }

    /// <summary>How many nodes actually ran, as opposed to being served from the cache.</summary>
    public int NodesEvaluated { get; }

    /// <summary>How many nodes were served from the cache.</summary>
    public int CacheHits { get; }

    /// <summary>Whether any node errored or sits in a cycle.</summary>
    public bool HasErrors
    {
        get
        {
            foreach (NodeState state in _states.Values)
            {
                if (state is NodeState.Error or NodeState.Cycle)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>What happened to a node.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns>The state, or <see cref="NodeState.NotEvaluated"/> for a node not in the run.</returns>
    public NodeState StateOf(NodeId id) => _states.TryGetValue(id, out NodeState state) ? state : NodeState.NotEvaluated;

    /// <summary>The value on one of a node's output ports.</summary>
    /// <param name="id">The node identity.</param>
    /// <param name="portIndex">The output port index.</param>
    /// <returns>The value, or <see langword="null"/> when the node produced no output.</returns>
    public object? Value(NodeId id, int portIndex = 0)
    {
        if (!_outputs.TryGetValue(id, out IReadOnlyList<object?>? outputs) || portIndex >= outputs.Count)
        {
            return null;
        }

        return outputs[portIndex];
    }

    /// <summary>Whether a node produced output at all.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns><see langword="true"/> when it did.</returns>
    public bool HasOutput(NodeId id) => _outputs.ContainsKey(id);

    /// <summary>The diagnostics raised by one node.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns>The diagnostics, possibly empty.</returns>
    public IReadOnlyList<SparkDiagnostic> DiagnosticsFor(NodeId id) =>
        _byNode.TryGetValue(id, out List<SparkDiagnostic>? list) ? list : [];
}

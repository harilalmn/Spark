using System;
using System.Collections.Generic;
using System.Threading;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// Runs a graph: topologically sorted into levels, parallel within a level, cached by provenance,
/// cancellable between nodes and between replication elements, and incapable of hanging.
/// </summary>
/// <remarks>
/// <para>
/// <b>Error handling is the part worth reading.</b> A node that errors produces no output, and every
/// node downstream of it is marked <see cref="NodeState.NotEvaluated"/> and given no diagnostic at
/// all. Cascading would turn one broken node into a wall of fifty errors, and the wall is what hides
/// the cause. A warning is different: it means output with caveats, and downstream evaluates
/// normally.
/// </para>
/// <para>
/// <b>Cycles do not stop the run.</b> They cannot be created by drawing a wire, so one can only
/// arrive through a loaded file. Every node caught in the cycle errors, everything downstream of it
/// is not evaluated, and the rest of the graph runs.
/// </para>
/// <para>
/// <b>The cache is consulted for every node, not only dirty ones.</b> A key has to be computed for
/// every node anyway, because a node's key is built from its upstream keys; once it is computed the
/// lookup is one dictionary probe. That is why undo, redo and slider reverts are instant, and it is
/// why the dirty set is a hint rather than the mechanism.
/// </para>
/// </remarks>
public static class GraphEvaluator
{
    /// <summary>Evaluates a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="context">The tolerance, scheduler, cache and run epoch for this run.</param>
    /// <param name="cancellationToken">
    /// Checked between nodes and between replication elements. Cancelling leaves everything already
    /// computed in the cache, so resuming is cheap.
    /// </param>
    /// <returns>The outputs, states and diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static EvaluationResult Evaluate(Graph graph, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);

        TopologicalOrder order = TopologicalOrder.Of(graph);

        Run run = new(graph, context, cancellationToken);

        foreach (NodeId id in order.CyclicNodes)
        {
            NodeInstance node = graph.Node(id);
            run.Fail(id, NodeState.Cycle, DiagnosticCodes.Create(
                DiagnosticSeverity.Error,
                DiagnosticCodes.NodeInCycle,
                $"'{node.Definition.DisplayName}' is part of a cycle, so it has no order to evaluate in. Break the loop by removing one of the wires in it; the rest of the graph has still been evaluated.").WithNode(id.Value));
        }

        foreach (NodeId id in order.DownstreamOfCycles)
        {
            run.MarkNotEvaluated(id);
        }

        foreach (IReadOnlyList<NodeId> level in order.Levels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<Action> work = new(level.Count);
            foreach (NodeId id in level)
            {
                NodeId captured = id;
                work.Add(() => run.EvaluateNode(captured));
            }

            context.Scheduler.Run(work, cancellationToken);
        }

        graph.MarkAllClean();
        return run.Build();
    }

    private sealed class Run
    {
        private readonly Dictionary<NodeId, IReadOnlyList<object?>> _outputs = [];
        private readonly Dictionary<NodeId, NodeState> _states = [];
        private readonly Dictionary<NodeId, CacheKey> _keys = [];
        private readonly List<SparkDiagnostic> _diagnostics = [];
        private readonly Lock _gate = new();
        private readonly Graph _graph;
        private readonly EvaluationContext _context;
        private readonly CancellationToken _cancellationToken;
        private int _nodesEvaluated;
        private int _cacheHits;

        internal Run(Graph graph, EvaluationContext context, CancellationToken cancellationToken)
        {
            _graph = graph;
            _context = context;
            _cancellationToken = cancellationToken;
        }

        internal void EvaluateNode(NodeId id)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            NodeInstance node = _graph.Node(id);
            int inputCount = node.Definition.Inputs.Count;

            object?[] arguments = new object?[inputCount];
            CacheKeyInput[] keyInputs = new CacheKeyInput[inputCount];
            bool[] wired = new bool[inputCount];

            foreach (Wire wire in _graph.IncomingWires(id))
            {
                if (wire.TargetPort >= inputCount)
                {
                    continue;
                }

                // An upstream node that produced nothing means this node cannot run. It is greyed,
                // not blamed: the error is one node back, and saying so fifty times would bury it.
                if (!TryReadUpstream(wire, out object? value, out CacheKey upstreamKey))
                {
                    MarkNotEvaluated(id);
                    return;
                }

                arguments[wire.TargetPort] = value;
                keyInputs[wire.TargetPort] = CacheKeyInput.Wired(upstreamKey, wire.SourcePort);
                wired[wire.TargetPort] = true;
            }

            for (int index = 0; index < inputCount; index++)
            {
                if (wired[index])
                {
                    continue;
                }

                object? literal = node.Literal(index);
                arguments[index] = literal;
                keyInputs[index] = CacheKeyInput.Unwired(literal);
            }

            LacingMode effective = node.EffectiveLacing;
            CacheKey key = CacheKey.For(
                node.Definition, effective, _context.Tolerance, _context.RunEpoch, keyInputs);

            if (!node.Definition.IsSideEffect
                && _context.Cache.TryGet(key, out CachedResult? cached)
                && cached is not null)
            {
                Complete(id, key, cached.Outputs, cached.Diagnostics, fromCache: true);
                return;
            }

            ReplicationResult result = Replicator.Replicate(
                node.Definition, node.Lacing, arguments, _cancellationToken);

            if (!result.HasOutput)
            {
                SparkDiagnostic error = result.Diagnostics.Count > 0
                    ? result.Diagnostics[0].WithNode(id.Value)
                    : DiagnosticCodes.Create(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.NodeThrewAtDepthZero,
                        $"'{node.Definition.DisplayName}' produced no output.").WithNode(id.Value);

                Fail(id, NodeState.Error, error);
                return;
            }

            if (!node.Definition.IsSideEffect)
            {
                _context.Cache.Set(key, new CachedResult(result.Outputs, result.Diagnostics));
            }

            Complete(id, key, result.Outputs, result.Diagnostics, fromCache: false);
        }

        internal void Fail(NodeId id, NodeState state, SparkDiagnostic diagnostic)
        {
            lock (_gate)
            {
                _states[id] = state;
                _diagnostics.Add(diagnostic);
            }
        }

        internal void MarkNotEvaluated(NodeId id)
        {
            lock (_gate)
            {
                _states[id] = NodeState.NotEvaluated;
            }
        }

        internal EvaluationResult Build()
        {
            lock (_gate)
            {
                return new EvaluationResult(_outputs, _states, _diagnostics, _nodesEvaluated, _cacheHits);
            }
        }

        private bool TryReadUpstream(Wire wire, out object? value, out CacheKey upstreamKey)
        {
            lock (_gate)
            {
                if (_outputs.TryGetValue(wire.Source, out IReadOnlyList<object?>? outputs)
                    && wire.SourcePort < outputs.Count
                    && _keys.TryGetValue(wire.Source, out upstreamKey))
                {
                    value = outputs[wire.SourcePort];
                    return true;
                }
            }

            value = null;
            upstreamKey = CacheKey.None;
            return false;
        }

        private void Complete(
            NodeId id,
            CacheKey key,
            IReadOnlyList<object?> outputs,
            IReadOnlyList<SparkDiagnostic> diagnostics,
            bool fromCache)
        {
            NodeState state = NodeState.Evaluated;

            lock (_gate)
            {
                _outputs[id] = outputs;
                _keys[id] = key;

                foreach (SparkDiagnostic diagnostic in diagnostics)
                {
                    _diagnostics.Add(diagnostic.WithNode(id.Value));
                    if (diagnostic.Severity == DiagnosticSeverity.Warning)
                    {
                        state = NodeState.Warning;
                    }
                }

                _states[id] = state;

                if (fromCache)
                {
                    _cacheHits++;
                }
                else
                {
                    _nodesEvaluated++;
                }
            }
        }
    }
}

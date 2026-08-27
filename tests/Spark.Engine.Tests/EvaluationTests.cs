using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// Evaluation: ordering, error containment, cycles, provenance caching and cancellation.
/// </summary>
public sealed class EvaluationTests
{
    /// <summary>A chain evaluates in dependency order and the value reaches the end of it.</summary>
    [Fact]
    public void AChainEvaluatesInDependencyOrder()
    {
        Graph graph = new();
        NodeInstance first = graph.AddNode(LacingNodes.Add);
        NodeInstance second = graph.AddNode(LacingNodes.Add);

        graph.SetLiteral(first.Id, 0, 3.0);
        graph.SetLiteral(first.Id, 1, 4.0);
        graph.TryConnect(first.Id, 0, second.Id, 0);
        graph.SetLiteral(second.Id, 1, 10.0);

        EvaluationResult result = Evaluate(graph, new EvaluationContext());

        Assert.Equal(NodeState.Evaluated, result.StateOf(first.Id));
        Assert.Equal(7.0, result.Value(first.Id));
        Assert.Equal(17.0, result.Value(second.Id));
    }

    /// <summary>
    /// An error produces no output and is <b>not</b> cascaded: everything downstream is greyed as
    /// not evaluated and carries no diagnostic of its own.
    /// </summary>
    /// <remarks>
    /// This is the behaviour that keeps a one-node problem from becoming a fifty-error wall. The
    /// assertion that matters is the one about the downstream node's diagnostics being empty — a
    /// cascading implementation passes every other assertion here.
    /// </remarks>
    [Fact]
    public void AnErrorDoesNotCascadeAndDownstreamIsGreyedRatherThanBlamed()
    {
        Graph graph = new();
        NodeInstance failing = graph.AddNode(LacingNodes.Invert);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);
        NodeInstance further = graph.AddNode(LacingNodes.Add);
        NodeInstance unrelated = graph.AddNode(LacingNodes.Add);

        graph.SetLiteral(failing.Id, 0, 0.0);
        graph.TryConnect(failing.Id, 0, downstream.Id, 0);
        graph.TryConnect(downstream.Id, 0, further.Id, 0);
        graph.SetLiteral(unrelated.Id, 0, 1.0);
        graph.SetLiteral(unrelated.Id, 1, 2.0);

        EvaluationResult result = Evaluate(graph, new EvaluationContext());

        Assert.Equal(NodeState.Error, result.StateOf(failing.Id));
        Assert.Equal(DiagnosticCodes.NodeThrewAtDepthZero, Assert.Single(result.DiagnosticsFor(failing.Id)).Code);

        Assert.Equal(NodeState.NotEvaluated, result.StateOf(downstream.Id));
        Assert.Empty(result.DiagnosticsFor(downstream.Id));

        Assert.Equal(NodeState.NotEvaluated, result.StateOf(further.Id));
        Assert.Empty(result.DiagnosticsFor(further.Id));

        // The rest of the graph still runs.
        Assert.Equal(NodeState.Evaluated, result.StateOf(unrelated.Id));
        Assert.Equal(3.0, result.Value(unrelated.Id));

        Assert.Single(result.Diagnostics);
    }

    /// <summary>A warning means output with caveats, so downstream evaluates normally.</summary>
    [Fact]
    public void AWarningStillProducesOutputAndDownstreamStillEvaluates()
    {
        Graph graph = new();
        NodeInstance warning = graph.AddNode(LacingNodes.Invert);
        NodeInstance downstream = graph.AddNode(LacingNodes.ListCount);

        graph.SetLiteral(warning.Id, 0, SparkList.Of(1.0, 0.0, 4.0));
        graph.TryConnect(warning.Id, 0, downstream.Id, 0);

        EvaluationResult result = Evaluate(graph, new EvaluationContext());

        Assert.Equal(NodeState.Warning, result.StateOf(warning.Id));
        Assert.Equal(DiagnosticCodes.ElementsFailed, Assert.Single(result.DiagnosticsFor(warning.Id)).Code);
        Assert.Equal(NodeState.Evaluated, result.StateOf(downstream.Id));
        Assert.Equal(3, result.Value(downstream.Id));
    }

    /// <summary>
    /// A cycle that arrives through a loaded file errors every node in it, leaves the rest of the
    /// graph evaluating, and <b>terminates</b>.
    /// </summary>
    /// <remarks>
    /// The hard timeout is the point of the test. "Never hangs" is not a property an assertion about
    /// the result can express, because a hanging implementation never reaches one.
    /// </remarks>
    [Fact]
    public async Task ACycleFoundAtLoadErrorsItsOwnNodesTerminatesAndLeavesTheRestEvaluating()
    {
        Graph graph = new();
        NodeInstance first = graph.AddNode(LacingNodes.Add);
        NodeInstance second = graph.AddNode(LacingNodes.Add);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);
        NodeInstance unrelated = graph.AddNode(LacingNodes.Add);

        // The load path does not validate: a file can contain a cycle, and refusing to open the
        // document would be the wrong answer.
        graph.LoadWire(first.Id, 0, second.Id, 0);
        graph.LoadWire(second.Id, 0, first.Id, 0);
        graph.LoadWire(second.Id, 0, downstream.Id, 0);

        graph.SetLiteral(unrelated.Id, 0, 1.0);
        graph.SetLiteral(unrelated.Id, 1, 2.0);

        CancellationToken token = TestContext.Current.CancellationToken;
        Task<EvaluationResult> run = Task.Run(() => Evaluate(graph, new EvaluationContext()), token);
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10), token));

        Assert.Same(run, finished);

        EvaluationResult result = await run;

        Assert.Equal(NodeState.Cycle, result.StateOf(first.Id));
        Assert.Equal(NodeState.Cycle, result.StateOf(second.Id));
        Assert.Equal(DiagnosticCodes.NodeInCycle, Assert.Single(result.DiagnosticsFor(first.Id)).Code);

        // Downstream of the cycle is blameless and gets no diagnostic.
        Assert.Equal(NodeState.NotEvaluated, result.StateOf(downstream.Id));
        Assert.Empty(result.DiagnosticsFor(downstream.Id));

        Assert.Equal(NodeState.Evaluated, result.StateOf(unrelated.Id));
        Assert.Equal(3.0, result.Value(unrelated.Id));
    }

    /// <summary>Re-running an unchanged graph runs nothing: every node is served from its provenance key.</summary>
    [Fact]
    public void ReRunningAnUnchangedGraphServesEveryNodeFromTheCache()
    {
        Graph graph = BuildChain(out NodeId first, out NodeId second);
        EvaluationContext context = new();

        EvaluationResult firstRun = Evaluate(graph, context);
        Assert.Equal(2, firstRun.NodesEvaluated);
        Assert.Equal(0, firstRun.CacheHits);

        EvaluationResult secondRun = Evaluate(graph, context.NextRun());
        Assert.Equal(0, secondRun.NodesEvaluated);
        Assert.Equal(2, secondRun.CacheHits);
        Assert.Equal(17.0, secondRun.Value(second));
        Assert.NotEqual(NodeId.None, first);
    }

    /// <summary>
    /// Changing a literal invalidates exactly the node that changed and everything downstream of it,
    /// and nothing else.
    /// </summary>
    [Fact]
    public void ChangingALiteralReEvaluatesOnlyTheAffectedSubgraph()
    {
        Graph graph = BuildChain(out NodeId first, out NodeId second);
        EvaluationContext context = new();

        Evaluate(graph, context);
        graph.SetLiteral(second, 1, 100.0);

        EvaluationResult result = Evaluate(graph, context.NextRun());

        Assert.Equal(1, result.NodesEvaluated);
        Assert.Equal(1, result.CacheHits);
        Assert.Equal(107.0, result.Value(second));
        Assert.Equal(7.0, result.Value(first));
    }

    /// <summary>
    /// Setting a literal back to its old value is instant, because the old provenance key is still
    /// resident. This is what makes undo, redo and a slider revert free rather than a re-run.
    /// </summary>
    [Fact]
    public void RevertingALiteralIsServedFromTheCacheRatherThanRecomputed()
    {
        Graph graph = BuildChain(out NodeId first, out NodeId second);
        EvaluationContext context = new();

        Evaluate(graph, context);
        graph.SetLiteral(second, 1, 100.0);
        Evaluate(graph, context);

        graph.SetLiteral(second, 1, 10.0);
        EvaluationResult reverted = Evaluate(graph, context);

        Assert.Equal(0, reverted.NodesEvaluated);
        Assert.Equal(2, reverted.CacheHits);
        Assert.Equal(17.0, reverted.Value(second));
        Assert.NotEqual(NodeId.None, first);
    }

    /// <summary>
    /// The document tolerance is part of every cache key, so changing it invalidates the graph.
    /// </summary>
    /// <remarks>
    /// This is the decisive argument against an ambient tolerance: an ambient one would be invisible
    /// to the key, so nothing would invalidate and the graph would go on serving geometry computed at
    /// the old value, silently and with no way for a user to tell.
    /// </remarks>
    [Fact]
    public void ChangingTheDocumentToleranceInvalidatesEveryCachedResult()
    {
        Graph graph = BuildChain(out NodeId first, out NodeId second);
        EvaluationContext context = new();

        Evaluate(graph, context);

        EvaluationResult result = Evaluate(graph, context.WithTolerance(Tolerance.ForScale(1000.0)));

        Assert.Equal(2, result.NodesEvaluated);
        Assert.Equal(0, result.CacheHits);
        Assert.NotEqual(NodeId.None, first);
        Assert.NotEqual(NodeId.None, second);
    }

    /// <summary>
    /// An impure node re-runs every time and poisons the keys of everything downstream of it, so the
    /// subgraph below it re-runs too.
    /// </summary>
    /// <remarks>
    /// An undeclared impure node is the worst failure available in a provenance cache: it poisons
    /// nothing, so it serves a stale result forever and never looks wrong. The pure control in this
    /// test is what makes the impure assertion mean something.
    /// </remarks>
    [Fact]
    public void AnImpureNodeReRunsEveryTimeAndPoisonsEverythingDownstream()
    {
        int calls = 0;
        NodeDefinition ticking = new(
            new NodeKey("Test", "Clock"),
            "Clock",
            [],
            [new PortDefinition("tick", typeof(double), 0)],
            _ => [(double)Interlocked.Increment(ref calls)],
            LacingMode.Longest,
            version: 1,
            isSideEffect: true);

        Graph graph = new();
        NodeInstance clock = graph.AddNode(ticking);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);
        graph.TryConnect(clock.Id, 0, downstream.Id, 0);
        graph.SetLiteral(downstream.Id, 1, 10.0);

        EvaluationContext context = new();

        EvaluationResult firstRun = Evaluate(graph, context);
        EvaluationResult secondRun = Evaluate(graph, context.NextRun());

        Assert.Equal(11.0, firstRun.Value(downstream.Id));
        Assert.Equal(12.0, secondRun.Value(downstream.Id));
        Assert.Equal(2, calls);

        // Nothing was served from the cache: the impure node re-ran, and its new key poisoned the
        // downstream node's key too.
        Assert.Equal(0, secondRun.CacheHits);
        Assert.Equal(2, secondRun.NodesEvaluated);
    }

    /// <summary>
    /// A pure node with identical provenance is served from the cache, which is the control the
    /// impure test above needs to mean anything.
    /// </summary>
    [Fact]
    public void APureNodeWithTheSameProvenanceIsNotReRun()
    {
        int calls = 0;
        NodeDefinition counting = new(
            new NodeKey("Test", "Counted"),
            "Counted",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [(double)Interlocked.Increment(ref calls)]);

        Graph graph = new();
        NodeInstance node = graph.AddNode(counting);
        EvaluationContext context = new();

        Evaluate(graph, context);
        Evaluate(graph, context.NextRun());

        Assert.Equal(1, calls);
        Assert.NotEqual(NodeId.None, node.Id);
    }

    /// <summary>
    /// Cancelling stops the run and leaves everything already computed in the cache, so resuming is
    /// cheap rather than a fresh start.
    /// </summary>
    [Fact]
    public void CancellingLeavesCompletedNodesInTheCache()
    {
        using CancellationTokenSource cancellation = new();

        NodeDefinition canceller = new(
            new NodeKey("Test", "Canceller"),
            "Canceller",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("value", typeof(double), 0)],
            arguments =>
            {
                cancellation.Cancel();
                return [arguments[0]];
            });

        Graph graph = new();
        NodeInstance start = graph.AddNode(LacingNodes.Add);
        NodeInstance middle = graph.AddNode(canceller);
        NodeInstance end = graph.AddNode(LacingNodes.Add);

        graph.SetLiteral(start.Id, 0, 3.0);
        graph.SetLiteral(start.Id, 1, 4.0);
        graph.TryConnect(start.Id, 0, middle.Id, 0);
        graph.TryConnect(middle.Id, 0, end.Id, 0);
        graph.SetLiteral(end.Id, 1, 1.0);

        EvaluationContext context = new();

        Assert.ThrowsAny<OperationCanceledException>(
            () => GraphEvaluator.Evaluate(graph, context, cancellation.Token));

        // The work that finished before the cancellation is still cached.
        EvaluationResult resumed = Evaluate(graph, context);
        Assert.True(resumed.CacheHits >= 1, "Cancelling discarded work that had already completed.");
        Assert.Equal(8.0, resumed.Value(end.Id));
    }

    /// <summary>Cancellation is checked between replication elements, so a runaway replication stops.</summary>
    [Fact]
    public void CancellationIsCheckedBetweenReplicationElements()
    {
        using CancellationTokenSource cancellation = new();
        int invocations = 0;

        NodeDefinition counting = new(
            new NodeKey("Test", "CountingIdentity"),
            "CountingIdentity",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("value", typeof(double), 0)],
            arguments =>
            {
                if (Interlocked.Increment(ref invocations) == 10)
                {
                    cancellation.Cancel();
                }

                return [arguments[0]];
            });

        object?[] values = new object?[100_000];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (double)index;
        }

        Assert.ThrowsAny<OperationCanceledException>(() => Replicator.Replicate(
            counting, LacingMode.Longest, [new SparkList(values, 1)], cancellation.Token));

        Assert.True(invocations < 1_000, $"Replication ran {invocations} elements after cancellation was requested.");
    }

    /// <summary>The parallel scheduler produces exactly the results the sequential one does.</summary>
    [Fact]
    public void TheParallelSchedulerAgreesWithTheSequentialOne()
    {
        Graph graph = BuildWideGraph(out List<NodeId> leaves);

        EvaluationResult sequential = Evaluate(graph, new EvaluationContext(scheduler: new SequentialEvaluationScheduler()));
        EvaluationResult parallel = Evaluate(graph, new EvaluationContext(scheduler: new ParallelEvaluationScheduler()));

        foreach (NodeId leaf in leaves)
        {
            GraphValues.AssertEqual(sequential.Value(leaf), parallel.Value(leaf));
        }
    }

    /// <summary>
    /// The topological sort produces levels, and everything in one level is independent of everything
    /// else in it — that is what makes a level the unit of parallelism.
    /// </summary>
    [Fact]
    public void TheSortProducesLevelsWithNoDependenciesInsideALevel()
    {
        Graph graph = new();
        NodeInstance source = graph.AddNode(LacingNodes.Add);
        NodeInstance left = graph.AddNode(LacingNodes.Add);
        NodeInstance right = graph.AddNode(LacingNodes.Add);
        NodeInstance join = graph.AddNode(LacingNodes.Add);

        graph.TryConnect(source.Id, 0, left.Id, 0);
        graph.TryConnect(source.Id, 0, right.Id, 0);
        graph.TryConnect(left.Id, 0, join.Id, 0);
        graph.TryConnect(right.Id, 0, join.Id, 1);

        TopologicalOrder order = TopologicalOrder.Of(graph);

        Assert.False(order.HasCycle);
        Assert.Equal(3, order.Levels.Count);
        Assert.Equal([source.Id], order.Levels[0]);
        Assert.Equal(2, order.Levels[1].Count);
        Assert.Equal([join.Id], order.Levels[2]);
    }

    private static EvaluationResult Evaluate(Graph graph, EvaluationContext context) =>
        GraphEvaluator.Evaluate(graph, context, TestContext.Current.CancellationToken);

    private static Graph BuildChain(out NodeId first, out NodeId second)
    {
        Graph graph = new();
        NodeInstance start = graph.AddNode(LacingNodes.Add);
        NodeInstance end = graph.AddNode(LacingNodes.Add);

        graph.SetLiteral(start.Id, 0, 3.0);
        graph.SetLiteral(start.Id, 1, 4.0);
        graph.TryConnect(start.Id, 0, end.Id, 0);
        graph.SetLiteral(end.Id, 1, 10.0);

        first = start.Id;
        second = end.Id;
        return graph;
    }

    private static Graph BuildWideGraph(out List<NodeId> leaves)
    {
        Graph graph = new();
        NodeInstance source = graph.AddNode(LacingNodes.Range);
        graph.SetLiteral(source.Id, 0, 20.0);

        leaves = [];
        for (int index = 0; index < 16; index++)
        {
            NodeInstance leaf = graph.AddNode(LacingNodes.Add);
            graph.TryConnect(source.Id, 0, leaf.Id, 0);
            graph.SetLiteral(leaf.Id, 1, index);
            leaves.Add(leaf.Id);
        }

        return graph;
    }
}

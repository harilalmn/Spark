using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// A run reports each node as it finishes (`E9-T7`, the streamed half).
/// </summary>
/// <remarks>
/// <b>What a viewport needs from the engine to draw before the run ends</b>: every node that produced
/// output, once, no earlier than the nodes it depends on, with the values the run's result will hold.
/// The recorder here is synchronous on purpose - <see cref="Progress{T}"/> posts to a
/// synchronisation context, and a test that read its reports would be racing them.
/// </remarks>
public sealed class NodeCompletedReportTests
{
    /// <summary>Records every report, from whichever thread it arrives on.</summary>
    private sealed class Recorder : IProgress<NodeCompleted>
    {
        private readonly Lock _gate = new();
        private readonly List<NodeCompleted> _reports = [];

        public IReadOnlyList<NodeCompleted> Reports
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reports];
                }
            }
        }

        public void Report(NodeCompleted value)
        {
            lock (_gate)
            {
                _reports.Add(value);
            }
        }
    }

    /// <summary>Three additions in a row: 3 + 4, then + 10, then + 100.</summary>
    private static Graph Chain(out NodeId first, out NodeId second, out NodeId third)
    {
        Graph graph = new();
        NodeInstance a = graph.AddNode(LacingNodes.Add);
        NodeInstance b = graph.AddNode(LacingNodes.Add);
        NodeInstance c = graph.AddNode(LacingNodes.Add);

        graph.SetLiteral(a.Id, 0, 3.0);
        graph.SetLiteral(a.Id, 1, 4.0);
        graph.TryConnect(a.Id, 0, b.Id, 0);
        graph.SetLiteral(b.Id, 1, 10.0);
        graph.TryConnect(b.Id, 0, c.Id, 0);
        graph.SetLiteral(c.Id, 1, 100.0);

        first = a.Id;
        second = b.Id;
        third = c.Id;

        return graph;
    }

    /// <summary>
    /// <b>The row.</b> Each node of a chain is reported once, in the order it depends on, carrying the
    /// outputs the finished result holds for it.
    /// </summary>
    [Fact]
    public void EachNodeIsReportedOnceInDependencyOrderWithItsOutputs()
    {
        Graph graph = Chain(out NodeId first, out NodeId second, out NodeId third);
        Recorder recorder = new();

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(), recorder, TestContext.Current.CancellationToken);

        Assert.Equal([first, second, third], recorder.Reports.Select(report => report.Node));

        foreach (NodeCompleted report in recorder.Reports)
        {
            Assert.False(report.FromCache);
            Assert.Equal(result.Value(report.Node), report.Outputs[0]);
        }

        Assert.Equal(117.0, recorder.Reports[2].Outputs[0]);
    }

    /// <summary>
    /// A node served from the cache is reported too, because a viewport redrawing after an edit needs
    /// the unchanged nodes' geometry as much as the changed ones'.
    /// </summary>
    [Fact]
    public void ANodeServedFromTheCacheIsReportedToo()
    {
        Graph graph = Chain(out _, out _, out _);
        EvaluationContext context = new();

        GraphEvaluator.Evaluate(graph, context, TestContext.Current.CancellationToken);

        Recorder recorder = new();
        GraphEvaluator.Evaluate(graph, context, recorder, TestContext.Current.CancellationToken);

        Assert.Equal(3, recorder.Reports.Count);
        Assert.All(recorder.Reports, report => Assert.True(report.FromCache));
    }

    /// <summary>
    /// A node that failed, and the node it left without an input, produced nothing to show and are not
    /// reported; an unrelated node in the same run still is.
    /// </summary>
    [Fact]
    public void ANodeThatProducedNothingIsNotReported()
    {
        Graph graph = new();
        NodeInstance failing = graph.AddNode(LacingNodes.Invert);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);
        NodeInstance unrelated = graph.AddNode(LacingNodes.Add);

        graph.SetLiteral(failing.Id, 0, 0.0);
        graph.TryConnect(failing.Id, 0, downstream.Id, 0);
        graph.SetLiteral(unrelated.Id, 0, 1.0);
        graph.SetLiteral(unrelated.Id, 1, 2.0);

        Recorder recorder = new();
        GraphEvaluator.Evaluate(graph, new EvaluationContext(), recorder, TestContext.Current.CancellationToken);

        Assert.Equal([unrelated.Id], recorder.Reports.Select(report => report.Node));
    }
}

using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Spark.Engine;
using Spark.Host;

namespace Spark.Benchmarks;

/// <summary>
/// One node replicating over a long list, which is the engine's defining operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <see cref="MarshallingBenchmarks"/>.</b> Marshalling is one leg of
/// replication measured with the evaluator taken away — it converts a list to CLR arguments and
/// back, and says nothing about the cost of deciding how many times to call, of pairing two inputs
/// of different lengths, or of assembling the result. Replication over 100 000 items was covered
/// only through that path, which is the gap this file closes (<b>E11-T16</b>).
/// </para>
/// <para>
/// <b>Two shapes, because they fail differently.</b> <see cref="Scalar"/> fans a list against a
/// literal, which is the common case and is dominated by marshalling and by the per-element call.
/// <see cref="Zipped"/> pairs two lists, which is where the replication engine does work that has
/// no analogue in the scalar case — and where an accidental quadratic would hide, because pairing
/// is the step with two sequences in scope at once.
/// </para>
/// <para>
/// <b>The ratio is the guard, not the ceiling.</b> Both sizes are measured seconds apart in one
/// run, so 100 000 against 1 000 cancels the machine out entirely; a wall-clock ceiling on a shared
/// runner cannot distinguish a throttled core from a changed algorithm and must never be quoted as
/// though it could (<b>N29</b>). A hundred times the data for roughly a hundred times the work is
/// what linear looks like; an O(n²) moves that figure by two orders of magnitude anywhere.
/// </para>
/// <para>
/// Evaluation is <b>cold</b> on purpose. A warm run measures a dictionary lookup — the cache is
/// keyed by provenance and both nodes are unchanged between iterations, so there is nothing left of
/// replication in the number. <see cref="EvaluationBenchmarks"/> owns the warm-against-cold claim;
/// this file owns the cost of doing the work once.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ReplicationBenchmarks
{
    private readonly SparkSession _session = new(scheduler: new SequentialEvaluationScheduler());
    private Graph _scalar = null!;
    private Graph _zipped = null!;

    /// <summary>How many numbers the range produces, and so how many times the node replicates.</summary>
    [Params(1_000, 100_000)]
    public int Elements { get; set; }

    /// <summary>Builds both graphs and proves each of them runs clean, once per size.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _scalar = Build(zip: false);
        _zipped = Build(zip: true);
    }

    /// <summary>Releases the session's library after the last size.</summary>
    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    /// <summary>
    /// A list of <see cref="Elements"/> numbers replicated against a single literal.
    /// </summary>
    /// <returns>The result, returned so nothing is optimised away.</returns>
    [Benchmark(Description = "Scalar: a list of N against one literal")]
    public EvaluationResult Scalar() => GraphEvaluator.Evaluate(
        _scalar,
        new EvaluationContext(default, new SequentialEvaluationScheduler()),
        CancellationToken.None);

    /// <summary>
    /// Two lists of <see cref="Elements"/> numbers paired elementwise.
    /// </summary>
    /// <returns>The result, returned so nothing is optimised away.</returns>
    [Benchmark(Description = "Zipped: two lists of N paired elementwise")]
    public EvaluationResult Zipped() => GraphEvaluator.Evaluate(
        _zipped,
        new EvaluationContext(default, new SequentialEvaluationScheduler()),
        CancellationToken.None);

    /// <summary>
    /// A range feeding one <c>Math.Add</c>, with the second input either a literal or the range
    /// again.
    /// </summary>
    /// <param name="zip">
    /// <see langword="true"/> to wire the range into both inputs, so the node pairs two lists.
    /// </param>
    /// <returns>The graph, checked to evaluate cleanly.</returns>
    private Graph Build(bool zip)
    {
        Graph graph = new();

        NodeInstance range = graph.AddNode(_session.Library.ByName("Number.Range"));
        graph.SetLiteral(range.Id, 0, 0.0);
        graph.SetLiteral(range.Id, 1, (double)(Elements - 1));
        graph.SetLiteral(range.Id, 2, 1.0);

        NodeInstance add = graph.AddNode(_session.Library.ByName("Math.Add"));
        Connect(graph, range, add, 0);

        if (zip)
        {
            Connect(graph, range, add, 1);
        }
        else
        {
            graph.SetLiteral(add.Id, 1, 1.0);
        }

        // Checked rather than assumed, for the reason EvaluationBenchmarks records: a node that
        // errors is caught by the evaluator and turned into a diagnostic, which costs a thrown
        // exception — so a graph that does not run clean measures `throw` and reports the figure
        // under the evaluator's name. At 100 000 elements it would report it a hundred thousand
        // times over.
        EvaluationResult probe = GraphEvaluator.Evaluate(
            graph,
            new EvaluationContext(default, new SequentialEvaluationScheduler()),
            CancellationToken.None);

        if (probe.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException(
                $"The {(zip ? "zipped" : "scalar")} graph produced {probe.Diagnostics.Count} "
                + $"diagnostics; the first is '{probe.Diagnostics[0].Message}'. "
                + "A graph that does not run clean measures exception handling.");
        }

        if (probe.NodesEvaluated != 2)
        {
            throw new InvalidOperationException(
                $"The {(zip ? "zipped" : "scalar")} graph evaluated {probe.NodesEvaluated} of 2 "
                + "nodes. An unwired graph measures less than it claims to.");
        }

        return graph;
    }

    /// <summary>Wires one port and refuses to continue if the wire was not made.</summary>
    /// <remarks>
    /// <b>The zipped case is indistinguishable from the scalar one by its numbers</b> — both shapes
    /// measure within a microsecond of each other and allocate within 50 bytes, because the second
    /// input is the <i>same</i> node output referenced twice and the evaluator hands out the list it
    /// already has. That is the correct answer and it is also exactly what a silently dropped wire
    /// would look like, so the wire is checked rather than believed. A rejected connection returns a
    /// null <see cref="Wire"/> and no exception.
    /// </remarks>
    /// <param name="graph">The graph being built.</param>
    /// <param name="source">The node supplying the list.</param>
    /// <param name="target">The node replicating over it.</param>
    /// <param name="targetPort">Which of the target's inputs to wire.</param>
    private static void Connect(Graph graph, NodeInstance source, NodeInstance target, int targetPort)
    {
        ConnectionResult result = graph.TryConnect(source.Id, 0, target.Id, targetPort);

        if (result.Wire is null)
        {
            throw new InvalidOperationException(
                $"Wiring {source.Definition.Key} into port {targetPort} of {target.Definition.Key} "
                + $"was refused as {result.Compatibility}. The benchmark would then measure a "
                + "different graph from the one it names.");
        }
    }
}

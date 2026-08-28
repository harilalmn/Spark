using System;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Spark.Engine;
using Spark.Geometry;
using Spark.Host;

namespace Spark.Benchmarks;

/// <summary>
/// A whole graph, evaluated cold and evaluated again.
/// </summary>
/// <remarks>
/// <para>
/// The second measurement is the one to watch. Spark's cache is keyed by provenance rather than by
/// value, and the claim resting on it — that undo, redo and toggling a wire back and forth are
/// free — is only true while a fully cached run stays close to free itself. A warm run drifting
/// towards the cold one is the cache quietly ceasing to be a cache, and no test would fail.
/// </para>
/// <para>
/// <b>The node library is imported once, in setup, and never inside a measurement.</b> Importing it
/// reflects over an assembly and costs tens of milliseconds — put it inside the timed region and
/// the benchmark reports the importer's cost under the evaluator's name. That is not hypothetical:
/// the first version of this file constructed a <see cref="SparkSession"/> per iteration and
/// reported fifty nodes as slower than five hundred.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class EvaluationBenchmarks
{
    private readonly SparkSession _session = new(scheduler: new SequentialEvaluationScheduler());
    private Graph _graph = null!;
    private EvaluationContext _warm = null!;

    /// <summary>How many nodes are in the chain.</summary>
    [Params(50, 500)]
    public int Nodes { get; set; }

    /// <summary>How many elements each node replicates over.</summary>
    [Params(100)]
    public int Elements { get; set; }

    /// <summary>Builds the graph and warms a cache against it, once per size.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _graph = BenchmarkGraphs.Chain(_session.Library, Nodes, Elements);

        _warm = new EvaluationContext(default, new SequentialEvaluationScheduler());
        EvaluationResult warmed = GraphEvaluator.Evaluate(_graph, _warm, CancellationToken.None);

        // The graph must run clean, and the benchmark checks rather than assumes it. A node that
        // errors is caught by the evaluator and turned into a diagnostic, which costs a thrown
        // exception — so a graph with erroring nodes in it measures `throw` and reports the number
        // under the evaluator's name. That is exactly what the first version of this file did.
        if (warmed.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException(
                $"The benchmark graph produced {warmed.Diagnostics.Count} diagnostics; "
                + $"the first is '{warmed.Diagnostics[0].Message}'. "
                + "A graph that does not run clean measures exception handling.");
        }

        if (warmed.NodesEvaluated != Nodes)
        {
            throw new InvalidOperationException(
                $"The benchmark graph evaluated {warmed.NodesEvaluated} of {Nodes} nodes. "
                + "An unwired chain measures less than it claims to.");
        }
    }

    /// <summary>Releases the session's library after the last size.</summary>
    [GlobalCleanup]
    public void Cleanup() => _session.Dispose();

    /// <summary>
    /// A run against a cache that has never seen this graph.
    /// </summary>
    /// <remarks>
    /// Coldness comes from a fresh <see cref="EvaluationContext"/>, which brings a fresh cache with
    /// it, rather than from a fresh session. The graph and the library are the same objects the
    /// warm run uses, so the only difference between the two numbers is the cache.
    /// </remarks>
    /// <returns>The result, returned so nothing is optimised away.</returns>
    [Benchmark(Description = "Cold: a cache that has never seen this graph")]
    public EvaluationResult Cold() => GraphEvaluator.Evaluate(
        _graph,
        new EvaluationContext(default, new SequentialEvaluationScheduler()),
        CancellationToken.None);

    /// <summary>A run where every node's key is already resident.</summary>
    /// <returns>The result, returned so nothing is optimised away.</returns>
    [Benchmark(Description = "Warm: every key already resident")]
    public EvaluationResult Warm() => GraphEvaluator.Evaluate(_graph, _warm, CancellationToken.None);
}

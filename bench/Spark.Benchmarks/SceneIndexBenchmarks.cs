using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Spark.UI.Canvas;

namespace Spark.Benchmarks;

/// <summary>
/// The canvas's retained spatial index: rebuild, cull and hit-test at two thousand nodes.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0013 chose one immediate-mode control over one control per node, and the whole bet is that
/// culling and hit-testing a few thousand rectangles by hand is cheaper than the framework's
/// per-visual costs. `SceneIndex` is where that bet is settled, and it is pure managed
/// data-structure code with no Avalonia in it — so unlike the frame time itself, it can be
/// measured here rather than by driving a window.
/// </para>
/// <para>
/// <b>This does not replace the canvas benchmark and must not be read as doing so.</b> The
/// application's own `--canvas-benchmark` measures the render pass through the real compositor,
/// which is the number ADR-0013 is actually judged on (`E8-T15`). This measures the half of it
/// that is ours.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SceneIndexBenchmarks
{
    private readonly SceneIndex _index = new();
    private List<CanvasBounds> _bounds = [];

    /// <summary>How many nodes are in the index.</summary>
    [Params(2_000)]
    public int Nodes { get; set; }

    /// <summary>Lays the nodes out in a grid and builds the index once.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _bounds = new List<CanvasBounds>(Nodes);

        int columns = (int)Math.Ceiling(Math.Sqrt(Nodes));
        for (int node = 0; node < Nodes; node++)
        {
            double x = (node % columns) * 220.0;
            double y = (node / columns) * 120.0;
            _bounds.Add(CanvasBounds.FromSize(x, y, 168, 76));
        }

        _index.Rebuild(_bounds);
    }

    /// <summary>Building the index from scratch, which is what loading a graph costs.</summary>
    [Benchmark(Description = "Rebuild the whole index")]
    public void Rebuild() => _index.Rebuild(_bounds);

    /// <summary>
    /// A cull over roughly a screenful, which is what every frame costs.
    /// </summary>
    /// <returns>The visible count, returned so nothing is optimised away.</returns>
    [Benchmark(Description = "Cull to a screenful")]
    public int Cull()
    {
        _index.Query(0, 0, 1920, 1080);
        return _index.VisibleCount;
    }

    /// <summary>
    /// A hit test, which is what every pointer move costs.
    /// </summary>
    /// <returns>The slot found, returned so nothing is optimised away.</returns>
    [Benchmark(Description = "Hit-test a point")]
    public int HitTest()
    {
        _index.Query(0, 0, 1920, 1080);
        return _index.HitTest(900, 500);
    }
}

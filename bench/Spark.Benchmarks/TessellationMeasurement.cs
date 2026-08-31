using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Viewport;

namespace Spark.Benchmarks;

/// <summary>
/// Measures what the viewport asks the kernel to tessellate to (<c>E12-T19</c>, <c>E12-T12</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because nothing measured tessellation, and an eighteen-second stall hid in the
/// gap.</b> The solids demo took 18.2 s to open against 2.1 s for the points demo, and every other
/// number in <c>bench/</c> was green throughout: the budgets cover evaluation, marshalling, the
/// scene index and the canvas frame, and not one of them touches the path between an evaluated
/// solid and a mesh.
/// </para>
/// <para>
/// <b>It is a verb printing a line rather than a BenchmarkDotNet case, for the same reason the
/// canvas is.</b> A `Brep` cannot be tessellated without a provider, and the ubuntu leg has none
/// (<b>D18</b>). A BenchmarkDotNet case would run there anyway, measure a failed operation, and
/// report an excellent time " — " which is the vacuously-green failure the nightly is built to
/// avoid. As a verb it either produces a measurement or the run says <c>--no-tessellation</c> and
/// declares that it did not.
/// </para>
/// <para>
/// <b>The budget is a triangle count, not a time.</b> Allocation and counts are the
/// machine-independent half of a benchmark (<b>N29</b>), and the count is what actually moved:
/// 1,110,772 triangles at the old tolerance against 11,636 at the new one. A time ceiling on a
/// hosted runner would catch the same regression and would also fail on a busy afternoon.
/// </para>
/// <para>
/// <b>A surface case would not have caught it</b>, which was measured before this was written:
/// <c>Surface.ToMesh</c> returns 2,048 triangles for a sphere at 0.5, 2, 6, 15 and 30 degrees
/// alike, because the surface path is governed by sag alone. Only the <c>Brep</c> path answers to
/// the angular deflection, so only the <c>Brep</c> path is worth measuring.
/// </para>
/// </remarks>
public static class TessellationMeasurement
{
    /// <summary>Tessellates every solid the solids example produces, and prints one line.</summary>
    /// <param name="args">
    /// Optionally <c>--graph PATH</c>. Defaults to <c>docs/examples/solids.spark</c>, which is the
    /// graph the stall was found in.
    /// </param>
    /// <returns>Zero when it measured something, one when it could not.</returns>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string graphPath = Path.Combine(RepositoryRoot(), "docs", "examples", "solids.spark");

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--graph" && index + 1 < args.Length)
            {
                graphPath = args[++index];
            }
            else
            {
                Console.Error.WriteLine($"::error::Unrecognised argument '{args[index]}'.");
                return 1;
            }
        }

        if (!Spark.Geometry.Occt.OcctKernel.TryInstall(out string? why))
        {
            // Loudly, and with a non-zero exit. A run that meant to skip this says so on the check
            // side with --no-tessellation; one that did not mean to needs to find out here.
            Console.Error.WriteLine(
                $"::error::No solid-modelling kernel, so tessellation cannot be measured. {why}");
            return 1;
        }

        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"::error::No graph at '{graphPath}'.");
            return 1;
        }

        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));

        Graph graph = SparkFile.Read(File.ReadAllText(graphPath)).Restore(library);
        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(default, new SequentialEvaluationScheduler()), CancellationToken.None);

        List<Brep> solids =
        [
            .. graph.Nodes()
                .SelectMany(node => Enumerable.Range(0, node.Definition.Outputs.Count)
                    .Select(port => result.Value(node.Id, port)))
                .OfType<Brep>(),
        ];

        // Each solid through its own builder, so the count is the sum of what the viewport would
        // hold rather than whatever one shared builder happened to merge.
        Stopwatch clock = Stopwatch.StartNew();
        long triangles = 0;
        int packages = 0;

        foreach (Brep solid in solids)
        {
            SceneBuilder builder = new();
            builder.Add(new GeometryKey(Guid.NewGuid().ToString(), 0), solid);

            foreach (RenderPackage package in builder.Build())
            {
                triangles += package.Indices.Length / 3;
                packages++;
            }
        }

        clock.Stop();

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"tessellation solids={solids.Count} packages={packages} triangles={triangles} " +
            $"ms={clock.ElapsedMilliseconds}"));

        Console.WriteLine(
            "  The triangle count is what is budgeted; the time is printed for a reader and is not.");
        Console.WriteLine(
            "  At the angular deflection this regressed to, the same graph gave 1,110,772 triangles.");

        return 0;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);

        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "Spark.slnx")))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        throw new InvalidOperationException("the repository root was not found above " + AppContext.BaseDirectory);
    }
}

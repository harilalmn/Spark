using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Viewport;

namespace Spark.UI.Tests;

/// <summary>
/// What the viewport asks the kernel to tessellate to, and what that costs (<c>E12-T19</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The display tolerance had an angular deflection of 0.5 degrees, and opening the solids demo
/// took eighteen seconds.</b> Half a degree reads like a sensible smoothness figure and is not: it
/// is around fifty-seven times finer than the half a <i>radian</i> a mesher of this kind
/// conventionally defaults to, and cost against it is nowhere near linear. Six degrees is 286 times
/// faster on the same nine solids and gives a cylinder sixty segments, which is smooth at any zoom
/// this viewport reaches.
/// </para>
/// <para>
/// <b>These assert the triangle count, not the time.</b> A wall-clock ceiling on a shared machine
/// is the flakiest kind of test there is, and [N29] already argues that; the count is deterministic
/// and machine-independent, and it is what actually moved — 1,110,772 triangles against 11,636.
/// The ceiling below is four times the real figure, so it catches a regression of that shape
/// without ever failing for a rounding difference.
/// </para>
/// </remarks>
public sealed class DisplayTessellationTests
{
    /// <summary>The ceiling. Four times the measured 11,636, and a fortieth of the old 1,110,772.</summary>
    private const int TriangleCeiling = 50_000;

    /// <summary>
    /// <b>The solids demo tessellates to a mesh a viewport can hold.</b> This is the test that
    /// would have caught <c>E12-T19</c>, and nothing like it existed: no budget in <c>bench/</c>
    /// touches tessellation, so the one demo that exercises it was fifteen seconds slower than its
    /// neighbours and no test said so.
    /// </summary>
    [Fact]
    public void TheSolidsDemoTessellatesToASaneTriangleCount()
    {
        if (!Spark.Geometry.Occt.OcctKernel.TryInstall(out _))
        {
            // The no-provider configuration is supported (ADR-0021) and has no solids to measure.
            return;
        }

        IReadOnlyList<Brep> solids = SolidsFromTheDemo();

        Assert.True(solids.Count > 0, "the solids demo produced no solids, so this test measured nothing");

        long triangles = 0;

        foreach (Brep solid in solids)
        {
            SceneBuilder builder = new();
            builder.Add(new GeometryKey(Guid.NewGuid().ToString(), 0), solid);

            foreach (RenderPackage package in builder.Build())
            {
                triangles += package.Indices.Length / 3;
            }
        }

        Assert.True(
            triangles <= TriangleCeiling,
            $"the solids demo tessellates to {triangles:N0} triangles, over the {TriangleCeiling:N0} "
            + "ceiling. Check the angular deflection in SceneBuilder.DisplayTolerance: at 0.5 degrees "
            + "this was 1,110,772 triangles and eighteen seconds.");
    }

    /// <summary>
    /// <b>The angular deflection is the thing that regressed, so it is asserted directly.</b> The
    /// count above would also catch it, but a failure naming the number is a failure somebody has
    /// to go and diagnose; this one names the cause.
    /// </summary>
    [Fact]
    public void TheDisplayAngularDeflectionIsNotFinerThanADegree()
    {
        string source = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "Spark.Viewport", "SceneBuilder.cs"));

        System.Text.RegularExpressions.Match found = System.Text.RegularExpressions.Regex.Match(
            source, @"private static Tolerance DisplayTolerance.*?Angle\.FromDegrees\(([0-9.]+)\)",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        Assert.True(found.Success, "DisplayTolerance no longer names an angular deflection in degrees.");

        double degrees = double.Parse(found.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(
            degrees >= 1.0,
            $"the display angular deflection is {degrees} degrees. Below one degree the mesher's cost "
            + "runs away: 0.5 degrees cost 17,440 ms and 1,110,772 triangles on the solids demo, "
            + "against 61 ms and 11,636 at six degrees.");
    }

    private static IReadOnlyList<Brep> SolidsFromTheDemo()
    {
        string text = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "examples", "solids.spark"));

        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));

        Spark.Engine.Graph graph = SparkFile.Read(text).Restore(library);
        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(default, new ParallelEvaluationScheduler()), CancellationToken.None);

        return
        [
            .. graph.Nodes()
                .SelectMany(node => Enumerable.Range(0, node.Definition.Outputs.Count)
                    .Select(port => result.Value(node.Id, port)))
                .OfType<Brep>(),
        ];
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Host;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Opens and evaluates every committed example graph (<c>E11-T3</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the check that compiling a sample cannot be.</b> <c>E11-T2</c> proves a C# snippet is
/// well formed against the current API; it says nothing about whether a graph still produces an
/// answer. These files are the worked examples the help topics point at, and a worked example that
/// errors when opened is worse than no example: a reader assumes they have done something wrong.
/// </para>
/// <para>
/// <b>An executed graph is the strongest anti-rot mechanism available to a node-graph tool.</b> A
/// screenshot of a graph rots silently — rename a node and the picture is still a picture. A graph
/// that is opened, evaluated and asserted on goes red the same day.
/// </para>
/// <para>
/// It runs headless, with no window and no GPU, because the graph and the evaluator have no
/// dependency on either.
/// </para>
/// </remarks>
public sealed class ExampleGraphTests
{
    /// <summary>Every committed example opens without losing anything.</summary>
    [Fact]
    public void EveryExampleGraphOpens()
    {
        IReadOnlyList<string> files = ExampleFiles();

        Assert.True(files.Count >= 3, $"expected the committed example graphs, found {files.Count}");

        using SparkSession session = new();
        List<string> failures = [];

        foreach (string file in files)
        {
            try
            {
                CanvasGraph graph = CanvasDocument.Open(File.ReadAllText(file), session.Library, session.Scripts);
                if (graph.Nodes.Count == 0)
                {
                    failures.Add($"{Path.GetFileName(file)}: opened with no nodes at all.");
                }
            }
            catch (Exception error) when (error is SparkFileException or InvalidOperationException)
            {
                failures.Add($"{Path.GetFileName(file)}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, "These example graphs do not open:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// <b>Every committed example evaluates with no errors.</b> The heart of the row: these are the
    /// files a reader is told to open, and one that reports an error teaches them the wrong thing.
    /// </summary>
    /// <remarks>
    /// <b>The solid examples need the OpenCascade provider, and running without one is a supported
    /// configuration rather than a fault</b> (ADR-0021, D18). So the check has two modes: with the
    /// provider installed every example must be error-free, and without it every <i>other</i>
    /// example must be, while the solid ones are counted as <b>unchecked</b> rather than passed.
    /// <para>
    /// The failure this arrangement must not have is a green run that checked nothing, so the
    /// number fully evaluated is asserted too. That is the same shape the native test project
    /// already uses, where a green managed run proves nothing about the provider unless the skip
    /// count is zero.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryExampleGraphEvaluatesWithoutErrors()
    {
        bool kernelInstalled = Spark.Geometry.Occt.OcctKernel.TryInstall(out _);

        using SparkSession session = new();
        List<string> failures = [];
        List<string> needingKernel = [];
        int fullyEvaluated = 0;

        foreach (string file in ExampleFiles())
        {
            CanvasGraph graph = CanvasDocument.Open(File.ReadAllText(file), session.Library, session.Scripts);

            EvaluationContext context = new(
                Tolerance.Default, new SequentialEvaluationScheduler(), new EvaluationCache(), 0);

            EvaluationResult result = GraphEvaluator.Evaluate(
                graph.Engine, context, TestContext.Current.CancellationToken);

            bool complete = true;

            foreach (SparkDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                if (!kernelInstalled && IsMissingKernel(diagnostic))
                {
                    complete = false;
                    continue;
                }

                failures.Add($"{Path.GetFileName(file)}: [{diagnostic.Code}] {diagnostic.Message}");
            }

            if (complete)
            {
                fullyEvaluated++;
            }
            else
            {
                needingKernel.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            failures.Count == 0,
            "These example graphs report errors when evaluated:\n" + string.Join("\n", failures));

        if (kernelInstalled)
        {
            // With a provider there is no excuse: every example must evaluate completely. Asserting
            // only "more than none" here would let the solid examples quietly stop being checked on
            // the one machine configuration where they can be.
            Assert.True(
                needingKernel.Count == 0,
                "The OpenCascade provider is installed, so every example should evaluate completely. "
                + "These did not: " + string.Join(", ", needingKernel));

            Assert.Equal(ExampleFiles().Count, fullyEvaluated);
            return;
        }

        Assert.True(
            fullyEvaluated > 0,
            "No example graph was fully evaluated. Every one needs the OpenCascade provider, which "
            + "is not installed: " + string.Join(", ", needingKernel));
    }

    /// <summary>
    /// Whether a diagnostic is the kernel reporting that it is absent, rather than a real modelling
    /// failure.
    /// </summary>
    /// <remarks>
    /// The kernel-unavailable code is matched exactly. A node that threw is matched on its code
    /// <i>and</i> the sentence the unavailable kernel raises, because a node failure and a missing
    /// kernel share <c>SPK1046</c> — and tolerating every <c>SPK1046</c> would tolerate the real
    /// failures this test exists to find.
    /// </remarks>
    private static bool IsMissingKernel(SparkDiagnostic diagnostic) =>
        string.Equals(diagnostic.Code, KernelDiagnostics.Unavailable, StringComparison.Ordinal)
        || (string.Equals(diagnostic.Code, DiagnosticCodes.NodeThrewAtDepthZero, StringComparison.Ordinal)
            && diagnostic.Message.Contains("no solid-modelling kernel", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every example actually produces something. A graph that evaluates cleanly and computes
    /// nothing would pass the test above and illustrate nothing, which is the shape of a check
    /// that cannot fail.
    /// </summary>
    [Fact]
    public void EveryExampleGraphProducesOutput()
    {
        using SparkSession session = new();
        List<string> barren = [];

        foreach (string file in ExampleFiles())
        {
            CanvasGraph graph = CanvasDocument.Open(File.ReadAllText(file), session.Library, session.Scripts);

            EvaluationContext context = new(
                Tolerance.Default, new SequentialEvaluationScheduler(), new EvaluationCache(), 0);

            EvaluationResult result = GraphEvaluator.Evaluate(
                graph.Engine, context, TestContext.Current.CancellationToken);

            int produced = graph.Engine.Nodes().Count(node => result.HasOutput(node.Id));
            if (produced == 0)
            {
                barren.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            barren.Count == 0,
            "These example graphs evaluated but produced nothing: " + string.Join(", ", barren));
    }

    /// <summary>
    /// <b>Every example re-saves byte-identically.</b> The same promise the file format makes, held
    /// against the files a reader is most likely to open and accidentally re-save.
    /// </summary>
    [Fact]
    public void EveryExampleGraphReSavesByteIdentically()
    {
        using SparkSession session = new();
        List<string> changed = [];

        foreach (string file in ExampleFiles())
        {
            string original = File.ReadAllText(file).ReplaceLineEndings("\n");
            CanvasGraph graph = CanvasDocument.Open(original, session.Library, session.Scripts);

            if (CanvasDocument.Save(graph).ReplaceLineEndings("\n") != original)
            {
                changed.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            changed.Count == 0,
            "These example graphs do not survive a read and a write unchanged: " + string.Join(", ", changed));
    }

    private static IReadOnlyList<string> ExampleFiles()
    {
        string directory = Path.Combine(RepositoryRoot(), "docs", "examples");
        return Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, "*.spark").OrderBy(f => f, StringComparer.Ordinal)]
            : [];
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}

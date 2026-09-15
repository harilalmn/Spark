using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Spark.Api;
using Spark.Engine;
using Spark.Scripting;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>E6-T14</c>'s last acceptance criterion: <b>a graph containing no script nodes never loads
/// <c>Spark.Scripting</c></b>, and with it never pays for Roslyn.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one claim in the epic that cannot be settled by reading the code</b>, which is
/// why the box stayed unticked while the design was already right.
/// <c>SparkSession.EnableScripting</c> is the only door to the factory and it is called only where
/// a code block is present — so the claim looked true, and it was false.
/// <c>SparkSession.Dispose</c> touched a field <i>declared</i> as <c>ScriptCompletion?</c>, the JIT
/// resolved the field's type when it compiled the method, and every <c>spark run</c> in the product
/// loaded Roslyn on its way out. The value was always null and the branch was never taken. No
/// amount of reading finds that; one run of this test does.
/// </para>
/// <para>
/// <b>It must be a child process, and this is the one place in this project where that is worth
/// its cost.</b> The project comment argues against shelling out, and it is right about exit
/// codes — but the subject here is <i>a process's loaded-assembly list</i>, and this test assembly
/// references <c>Spark.Cli</c>, which means <c>Spark.Scripting</c> is in its own closure and sits
/// loaded in the test host before the first assertion runs. An in-process check would pass on a
/// tree where the product was broken, which is worse than no check.
/// </para>
/// <para>
/// <b>Both directions are asserted.</b> A guard that has never been seen to fail is a guard nobody
/// has tested, so <see cref="AGraphWithACodeBlockDoesLoadSparkScripting"/> runs the same probe over
/// a graph that does hold a code block and requires the assembly to appear. Without it, a probe
/// that silently observed nothing at all — a hook that never ran, a variable never read — would
/// report a clean run forever.
/// </para>
/// <para>
/// <b>And one of the three is a capability rather than a cost.</b>
/// <see cref="GraphDescribesACodeBlockWithoutLoadingSparkScripting"/> points <c>spark graph</c> at
/// the graph the test above uses to prove Roslyn <i>does</i> load, and requires that it does not —
/// which is what makes that verb usable on a file this build cannot open (<c>E12-T5</c>).
/// </para>
/// </remarks>
public sealed class ScriptingResidencyTests
{
    /// <summary>
    /// <b>The row.</b> A graph with no code block in it runs to completion without
    /// <c>Spark.Scripting</c> ever being loaded.
    /// </summary>
    [Fact]
    public void AGraphWithNoScriptNodesNeverLoadsSparkScripting()
    {
        string path = WriteGraph(graph => graph.SetLiteral(
            graph.AddNode(Library.Get(new NodeKey("Spark.Nodes.Core", "Number.Value"))).Id, 0, 3.0));

        (int exitCode, IReadOnlyCollection<string> loaded) = RunCli(path);

        Assert.Equal(0, exitCode);

        // The one line the criterion asks for. `Spark.Scripting` is what loads Roslyn; naming the
        // whole prefix catches a satellite or a resource assembly arriving beside it.
        Assert.DoesNotContain(loaded, name =>
            name.StartsWith("Spark.Scripting", StringComparison.Ordinal));

        // Roslyn itself, asserted separately: `Spark.Scripting` is the door, and this is what is
        // behind it. If the door is ever renamed or split, the cost is still measured here.
        Assert.DoesNotContain(loaded, name =>
            name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));

        // Proof the probe watched the right process rather than an empty one.
        Assert.Contains("Spark.Engine", loaded);
    }

    /// <summary>
    /// The same probe over a graph that <i>does</i> hold a code block loads <c>Spark.Scripting</c>,
    /// which is what makes the assertion above mean something.
    /// </summary>
    [Fact]
    public void AGraphWithACodeBlockDoesLoadSparkScripting()
    {
        const string script = "return new Line(new Point3d(0, 0, 0), new Point3d(1, 0, 0));";

        string path = WriteGraph(graph => graph.AddNode(
            NodeDefinition.FromScript(new ScriptNodeFactory(new ReferenceCatalog()).Create(script), script)));

        (int exitCode, IReadOnlyCollection<string> loaded) = RunCli(path);

        Assert.Equal(0, exitCode);

        Assert.Contains(loaded, name =>
            name.StartsWith("Spark.Scripting", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b><c>spark graph</c> describes a graph that <i>does</i> hold a code block and still loads
    /// no Roslyn</b> — which is the whole of what that verb is for (<c>E12-T5</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two tests above measure a cost the product should not pay. This one measures a
    /// capability: <c>graph</c> never calls <see cref="GraphDocument.Restore"/>, so the file it is
    /// pointed at can hold anything at all and the description still arrives. The negative is the
    /// evidence for it, because a verb that quietly built a factory would produce the same output
    /// and be useless for the case it exists to serve — a graph this build cannot open.
    /// </para>
    /// <para>
    /// <b>It is the same graph <see cref="AGraphWithACodeBlockDoesLoadSparkScripting"/> runs</b>,
    /// deliberately: one file, two verbs, and the difference between them is the claim. A test
    /// using a graph with no block in it would prove nothing this file does not already prove.
    /// </para>
    /// </remarks>
    [Fact]
    public void GraphDescribesACodeBlockWithoutLoadingSparkScripting()
    {
        const string script = "return new Line(new Point3d(0, 0, 0), new Point3d(1, 0, 0));";

        string path = WriteGraph(graph => graph.AddNode(
            NodeDefinition.FromScript(new ScriptNodeFactory(new ReferenceCatalog()).Create(script), script)));

        (int exitCode, IReadOnlyCollection<string> loaded) = RunCli(path, "graph");

        Assert.Equal(0, exitCode);

        Assert.DoesNotContain(loaded, name =>
            name.StartsWith("Spark.Scripting", StringComparison.Ordinal));

        Assert.DoesNotContain(loaded, name =>
            name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));

        // The library is what `graph` reconciles against, so its absence would mean the verb had
        // returned before doing the only thing that could have loaded anything.
        Assert.Contains("Spark.Nodes.Core", loaded);
    }

    /// <summary>
    /// Runs <c>spark run --all PATH</c> in a child process under the startup hook and returns what
    /// it exited with and what it loaded.
    /// </summary>
    /// <param name="path">The graph to run.</param>
    /// <param name="verb">The verb to run it under. <c>run</c> unless a caller says otherwise.</param>
    /// <returns>The exit code and the child's loaded-assembly names.</returns>
    /// <remarks>
    /// <c>--all</c> rather than a bare run so that a graph with no watch node in it is still a
    /// success; what is under test is what the process loaded, and a verb reporting nothing to
    /// print would make the exit-code assertion say something else. It is passed only to
    /// <c>run</c>, which is the only verb that has it.
    /// </remarks>
    private static (int ExitCode, IReadOnlyCollection<string> Loaded) RunCli(
        string path, string verb = "run")
    {
        string log = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".assemblies");

        ProcessStartInfo start = new(Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(verb);

        if (verb == "run")
        {
            start.ArgumentList.Add("--all");
        }

        start.ArgumentList.Add(path);

        start.Environment["SPARK_LOADED_ASSEMBLIES"] = log;
        start.Environment["DOTNET_STARTUP_HOOKS"] = typeof(StartupHook).Assembly.Location;

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("spark did not start");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        Assert.True(
            process.WaitForExit(TimeSpan.FromMinutes(2)),
            "spark did not exit within two minutes");

        Assert.True(
            File.Exists(log),
            $"the startup hook wrote nothing, so nothing was observed.{Environment.NewLine}{output}{error}");

        try
        {
            return (process.ExitCode, File.ReadAllLines(log));
        }
        finally
        {
            File.Delete(log);
        }
    }

    /// <summary>
    /// The built <c>spark</c> apphost, beside this test's own output under the same configuration.
    /// </summary>
    /// <remarks>
    /// Found by walking up to the file that marks the repository root rather than by counting
    /// <c>..</c> segments, which is the version of this that breaks when a project moves. The
    /// configuration and target framework are read off this assembly's own path, because the two
    /// projects are always built together and always into the same pair of folders.
    /// </remarks>
    private static string Cli { get; } = LocateCli();

    private static string LocateCli()
    {
        DirectoryInfo output = new(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        string framework = output.Name;
        string configuration = output.Parent?.Name
            ?? throw new InvalidOperationException($"no configuration folder above {output.FullName}");

        DirectoryInfo? root = output;

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Spark.slnx")))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            throw new InvalidOperationException($"no Spark.slnx above {AppContext.BaseDirectory}");
        }

        string name = OperatingSystem.IsWindows() ? "spark.exe" : "spark";
        string cli = Path.Combine(root.FullName, "src", "Spark.Cli", "bin", configuration, framework, name);

        return File.Exists(cli)
            ? cli
            : throw new FileNotFoundException($"the CLI is not built at {cli}", cli);
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }

    private static string WriteGraph(Action<Graph> build)
    {
        Graph graph = new();
        build(graph);

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".spark");
        File.WriteAllText(path, SparkFile.Write(GraphDocument.Capture(graph)));

        return path;
    }
}

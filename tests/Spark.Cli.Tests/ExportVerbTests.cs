using System;
using System.IO;
using Spark.Cli;
using Spark.Engine;
using Spark.Scripting;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark export</c> and code blocks (`E12-T24`).
/// </summary>
/// <remarks>
/// <b>It used to refuse every graph holding a code block</b>, because it restored the document with
/// no script factory at all — as though <c>--no-script</c> had been given, whether or not anybody
/// gave it, and whether or not the geometry came from the block. It now follows <c>run</c>'s rules,
/// and these tests hold it to them. The package-folder refusal is in <see cref="PackageFolderTests"/>,
/// beside the same refusal for the other two verbs.
/// </remarks>
public sealed class ExportVerbTests : IDisposable
{
    private readonly string _root;

    public ExportVerbTests()
    {
        // Geometry has to be loaded before a catalogue is built, or its prelude line does not
        // resolve and every block fails for a reason that has nothing to do with exporting.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        _root = Path.Combine(Path.GetTempPath(), "spark-cli-export", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Left for the operating system; the next run makes its own folder.
        }
    }

    /// <summary>
    /// <b>The row</b>: a curve a code block made is written to the file like any other.
    /// </summary>
    [Fact]
    public void ACodeBlocksCurveIsExported()
    {
        string graph = SaveGraph(graph =>
        {
            const string script = "return new Line(new Point3d(0, 0, 0), new Point3d(1, 0, 0));";
            _ = graph.AddNode(NodeDefinition.FromScript(new ScriptNodeFactory(new ReferenceCatalog()).Create(script), script));
        });

        string obj = Path.Combine(_root, "line.obj");
        StringWriter report = new();
        StringWriter error = new();

        Assert.Equal(0, Program.Export(["--open", graph, "--out", obj], report, error));

        Assert.Contains("wrote 1 object(s)", report.ToString(), StringComparison.Ordinal);
        Assert.Contains(File.ReadAllLines(obj), line => line.StartsWith("v ", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>--no-script</c> refuses a graph holding a code block, names the flag, and writes nothing —
    /// a file that silently lacked the block's geometry would be a wrong answer delivered quietly.
    /// </summary>
    [Fact]
    public void NoScriptRefusesAGraphWithACodeBlockAndWritesNothing()
    {
        string graph = SaveGraph(graph =>
        {
            const string script = "return new Line(new Point3d(0, 0, 0), new Point3d(1, 0, 0));";
            _ = graph.AddNode(NodeDefinition.FromScript(new ScriptNodeFactory(new ReferenceCatalog()).Create(script), script));
        });

        string obj = Path.Combine(_root, "line.obj");
        StringWriter error = new();

        Assert.Equal(1, Program.Export(["--open", graph, "--out", obj, "--no-script"], new StringWriter(), error));

        Assert.Contains("--no-script", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(obj), "a refused export wrote a file");
    }

    /// <summary>
    /// A graph with no code block exports exactly as it always did, and the line saying what was
    /// written goes to the report stream rather than the error stream.
    /// </summary>
    [Fact]
    public void AGraphWithoutACodeBlockExportsAsItAlwaysDid()
    {
        string graph = SaveGraph(graph =>
        {
            NodeId circle = graph.AddNode(Library.Get(new NodeKey("Spark.Nodes.Core", "Circle.FromCenterRadius"))).Id;
            graph.SetLiteral(circle, 1, 2.0);
        });

        string obj = Path.Combine(_root, "circle.obj");
        StringWriter report = new();
        StringWriter error = new();

        Assert.Equal(0, Program.Export(["--open", graph, "--out", obj], report, error));

        Assert.Contains("wrote 1 object(s)", report.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(File.Exists(obj));
    }

    private string SaveGraph(Action<Graph> build)
    {
        Graph graph = new();
        build(graph);

        string path = Path.Combine(_root, Guid.NewGuid().ToString("n") + ".spark");
        File.WriteAllText(path, SparkFile.Write(GraphDocument.Capture(graph)));

        return path;
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }
}

using System;
using System.IO;
using System.Linq;
using Spark.Cli;
using Spark.Engine;
using Spark.Packages;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark pack</c>, and a <c>.sparkz</c> accepted wherever a graph is (`E3-T20` step B).
/// </summary>
/// <remarks>
/// ADR-0017 says the bundle is <i>produced by <c>spark pack</c></i>. The other verbs open one into a
/// temporary folder and read the graph found there, so the package gate sees the package folder
/// beside it exactly as it would beside a <c>.spark</c>.
/// </remarks>
public sealed class PackVerbTests : IDisposable
{
    private static readonly string BundleFolders = Path.Combine(Path.GetTempPath(), "spark-bundles");

    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public PackVerbTests() => Directory.CreateDirectory(_root);

    private static NodeLibrary Library { get; } = BuildLibrary();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is not a test failure.
        }
    }

    /// <summary><b>The row.</b> A bundle runs as the graph it holds, value for value.</summary>
    [Fact]
    public void ABundleRunsAsTheGraphItHolds()
    {
        string graph = WriteGraph();
        string bundle = Path.Combine(_root, "shared.sparkz");
        StringWriter packed = new();

        Assert.Equal(0, Program.Pack([graph, "--out", bundle], packed, new StringWriter()));
        Assert.Contains("packed tower.spark into", packed.ToString(), StringComparison.Ordinal);

        StringWriter fromGraph = new();
        StringWriter fromBundle = new();

        Assert.Equal(0, Program.Run([graph, "--all"], fromGraph, new StringWriter()));
        Assert.Equal(0, Program.Run([bundle, "--all"], fromBundle, new StringWriter()));
        Assert.NotEqual(string.Empty, fromBundle.ToString());
        Assert.Equal(fromGraph.ToString(), fromBundle.ToString());
    }

    /// <summary>With no <c>--out</c>, the bundle is named after the graph and put beside it.</summary>
    [Fact]
    public void TheBundleDefaultsToTheGraphsNameBesideIt()
    {
        Assert.Equal(0, Program.Pack([WriteGraph()], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(Path.Combine(_root, "tower.sparkz")));
    }

    /// <summary>A build gate takes a bundle as it takes a graph, and is as silent about a clean one.</summary>
    [Fact]
    public void CheckTakesABundle()
    {
        string graph = WriteGraph();
        Program.Pack([graph], new StringWriter(), new StringWriter());

        StringWriter error = new();

        Assert.Equal(0, Program.Check([Path.Combine(_root, "tower.sparkz")], error));
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>The temporary folder a bundle is opened into is gone when the verb is done.</summary>
    [Fact]
    public void TheTemporaryFolderIsRemovedAfterwards()
    {
        string graph = WriteGraph();
        Program.Pack([graph], new StringWriter(), new StringWriter());

        string[] before = Folders();
        Assert.Equal(0, Program.Check([Path.Combine(_root, "tower.sparkz")], new StringWriter()));

        Assert.Empty(Folders().Except(before, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A refused bundle is an <see cref="IOException"/>, the kind <c>Main</c> reports in one line, so
    /// its reason reaches the person who typed the command.
    /// </summary>
    [Fact]
    public void ARefusedBundleIsTheKindMainReports()
    {
        string bundle = Path.Combine(_root, "broken.sparkz");
        File.WriteAllText(bundle, "not a zip");

        SparkBundleException refused = Assert.Throws<SparkBundleException>(() => Program.Check([bundle], new StringWriter()));
        Assert.IsAssignableFrom<IOException>(refused);
    }

    /// <summary>Naming no graph is an error, and the message says what to type instead.</summary>
    [Fact]
    public void PackWithNoGraphSaysWhatToType()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Pack([], new StringWriter(), error));
        Assert.Contains("spark pack graph.spark", error.ToString(), StringComparison.Ordinal);
    }

    private static string[] Folders() =>
        Directory.Exists(BundleFolders) ? Directory.GetDirectories(BundleFolders) : [];

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }

    private string WriteGraph()
    {
        Graph graph = new();
        NodeId number = graph.AddNode(Library.Get(new NodeKey("Spark.Nodes.Core", "Number.Value"))).Id;
        graph.SetLiteral(number, 0, 3.0);

        string path = Path.Combine(_root, "tower.spark");
        File.WriteAllText(path, SparkFile.Write(GraphDocument.Capture(graph)));
        return path;
    }
}

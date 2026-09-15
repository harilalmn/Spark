using System;
using System.IO;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark graph</c> (<c>E12-T5</c>): describing a <c>.spark</c> file without binding it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim under test is what the verb does <i>not</i> do.</b> Every other verb calls
/// <see cref="GraphDocument.Restore"/>, so every other verb fails on a graph naming a definition
/// this build does not hold. <see cref="ADefinitionTheLibraryLacksIsNamedAndExitsOne"/> is
/// therefore the row rather than a corner: it builds exactly the file the other verbs cannot open
/// and requires this one to describe it anyway.
/// </para>
/// <para>
/// <b>The documents are built by constructing <see cref="GraphDocument"/> directly</b>, not by
/// capturing a live graph. A capture can only name definitions that exist, which is precisely the
/// case this verb is for — so a test that went through <c>Capture</c> could not express its
/// subject.
/// </para>
/// </remarks>
public sealed class GraphVerbTests : IDisposable
{
    private readonly string _root;

    public GraphVerbTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-cli-graph", Guid.NewGuid().ToString("n"));
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
    /// <b>The row.</b> A graph naming a definition this build does not hold is described rather
    /// than refused, the definition is marked <c>missing</c> by name, and the exit code says so.
    /// </summary>
    /// <remarks>
    /// The same file through <c>spark check</c> is a placeholdered node and a diagnostic; the point
    /// of this verb is that the answer arrives as a description of the file instead.
    /// </remarks>
    [Fact]
    public void ADefinitionTheLibraryLacksIsNamedAndExitsOne()
    {
        string path = Save("stranger", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [
                Node("Acme.Nodes", "Panel.ByOutline"),
                Node("Acme.Nodes", "Panel.ByOutline"),
                Node("Spark.Nodes.Core", "Number.Value"),
            ],
            []));

        StringWriter output = new();

        Assert.Equal(1, Program.Graph([path], output, new StringWriter()));

        string said = output.ToString();

        Assert.Contains("missing  Acme.Nodes/Panel.ByOutline  x2", said, StringComparison.Ordinal);
        Assert.Contains("present  Spark.Nodes.Core/Number.Value  x1", said, StringComparison.Ordinal);
        Assert.Contains("1 thing(s) missing", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A graph whose every definition this build holds passes, and the summary says so in words
    /// rather than leaving the reader to infer it from an absence.
    /// </summary>
    [Fact]
    public void AGraphThisBuildHoldsEveryDefinitionForPasses()
    {
        string path = Save("known", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [Node("Spark.Nodes.Core", "Number.Value")],
            []));

        StringWriter output = new();

        Assert.Equal(0, Program.Graph([path], output, new StringWriter()));

        Assert.Contains("this build can open it", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A code block is counted and never reconciled.</b> Its definition is its source, so
    /// reporting it against the library would make every graph containing one read as broken.
    /// </summary>
    [Fact]
    public void ACodeBlockIsCountedApartAndIsNeverMissing()
    {
        string path = Save("block", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [Node("Spark.Scripting", "Code", "return 1 + 2;")],
            []));

        StringWriter output = new();

        Assert.Equal(0, Program.Graph([path], output, new StringWriter()));

        string said = output.ToString();

        Assert.Contains("code blocks  1", said, StringComparison.Ordinal);
        Assert.Contains("in file  (code block", said, StringComparison.Ordinal);
        Assert.DoesNotContain("missing", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The counts are the file's own, and the format line says both what this build writes and what
    /// the file needs — the two numbers somebody deciding whether to upgrade actually wants.
    /// </summary>
    [Fact]
    public void TheCountsAndTheFormatAreReported()
    {
        string path = Save("counted", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [
                Node("Spark.Nodes.Core", "Number.Value") with { Literals = [new GraphLiteral(0, 3.0)] },
                Node("Spark.Nodes.Core", "Number.Value"),
            ],
            [],
            notes: [new GraphDocumentNote(Guid.NewGuid(), 0, 0, 10, 10, "a note")]));

        StringWriter output = new();

        Assert.Equal(0, Program.Graph([path], output, new StringWriter()));

        string said = output.ToString();

        Assert.Contains("nodes        2", said, StringComparison.Ordinal);
        Assert.Contains("wires        0", said, StringComparison.Ordinal);
        Assert.Contains("literals     1", said, StringComparison.Ordinal);
        Assert.Contains("notes        1", said, StringComparison.Ordinal);
        Assert.Contains(
            $"format       {GraphDocument.CurrentFormatVersion}, readable by this build",
            said,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A recorded package the folder lacks fails the verb, in <c>pkg list</c>'s words — because
    /// <i>will this open here</i> has two halves and this is the commoner one.
    /// </summary>
    [Fact]
    public void ARecordedPackageTheFolderLacksIsNamedAndExitsOne()
    {
        string path = Save("packaged", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [Node("Spark.Nodes.Core", "Number.Value")],
            [],
            packages: [new GraphDocumentPackage("packaged.packages/acme.nodes.1.0.0")]));

        StringWriter output = new();

        Assert.Equal(1, Program.Graph([path], output, new StringWriter()));

        Assert.Contains(
            "missing  packaged.packages/acme.nodes.1.0.0", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The output is byte-identical over two runs, so it can be diffed.</summary>
    /// <remarks>
    /// The definitions are sorted ordinally rather than by count for exactly this. A dictionary's
    /// enumeration order is not contractual, and a listing that reordered between runs would make
    /// every diff of a build log noise.
    /// </remarks>
    [Fact]
    public void TheDescriptionIsStableAcrossRuns()
    {
        string path = Save("stable", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [
                Node("Spark.Nodes.Core", "Vector.ZAxis"),
                Node("Spark.Nodes.Core", "Number.Value"),
                Node("Spark.Nodes.Core", "Point.FromCoordinates"),
            ],
            []));

        StringWriter first = new();
        StringWriter second = new();

        Assert.Equal(0, Program.Graph([path], first, new StringWriter()));
        Assert.Equal(0, Program.Graph([path], second, new StringWriter()));

        Assert.Equal(first.ToString(), second.ToString());
    }

    /// <summary>Being given nothing to describe is a message, not a stack trace.</summary>
    [Fact]
    public void NoGraphIsRefusedWithAnExample()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Graph([], new StringWriter(), error));

        Assert.Contains("spark graph graph.spark", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An option this verb does not have is named rather than ignored.</summary>
    [Fact]
    public void AnUnknownOptionIsNamed()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Graph(["--all"], new StringWriter(), error));

        Assert.Contains("unrecognised option '--all'", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary><c>--open</c> takes the graph too, as it does everywhere else.</summary>
    [Fact]
    public void OpenNamesTheGraphAsWellAsAPositionalArgument()
    {
        string path = Save("opened", new GraphDocument(
            GraphDocument.CurrentFormatVersion,
            [Node("Spark.Nodes.Core", "Number.Value")],
            []));

        StringWriter positional = new();
        StringWriter named = new();

        Assert.Equal(0, Program.Graph([path], positional, new StringWriter()));
        Assert.Equal(0, Program.Graph(["--open", path], named, new StringWriter()));

        Assert.Equal(positional.ToString(), named.ToString());
    }

    private static GraphDocumentNode Node(string package, string name, string? script = null) =>
        new(
            new NodeId(Guid.NewGuid()),
            new NodeKey(package, name),
            LacingMode.Auto,
            0,
            0,
            [],
            Script: script);

    private string Save(string name, GraphDocument document)
    {
        string path = Path.Combine(_root, name + ".spark");
        File.WriteAllText(path, SparkFile.Write(document));

        return path;
    }
}

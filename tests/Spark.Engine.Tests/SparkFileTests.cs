using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The `.spark` format: canonical JSON in, the same graph out, and the same bytes back.
/// </summary>
/// <remarks>
/// The load-bearing test here is <see cref="ReadingAndRewritingAFileReproducesItByteForByte"/>.
/// [ADR-0017](../../docs/adr/0017-spark-file-is-plain-json.md) chose text over a container so that
/// graphs review like code, and that benefit disappears the moment an untouched graph re-saves with
/// a different byte in it. Everything else in this file exists to make that one true.
/// </remarks>
public sealed class SparkFileTests
{
    /// <summary>A line feed, spelled as a constant so no editing tool can eat the escape.</summary>
    private const char LineFeed = '\n';

    /// <summary>A carriage return, which a `.spark` file must never contain.</summary>
    private const char CarriageReturn = '\r';

    private static readonly NodeLibrary Library = BuildLibrary();

    [Fact]
    public void AGraphSurvivesBeingWrittenAndReadBack()
    {
        Graph original = BuildGraph(out NodeId points, out NodeId range);
        Dictionary<NodeId, (double, double)> layout = new()
        {
            [points] = (120.5, 40.0),
            [range] = (-30.0, 900.25),
        };

        string text = SparkFile.Write(GraphDocument.Capture(original, id => layout[id]));
        GraphDocument document = SparkFile.Read(text);
        Graph restored = document.Restore(Library);

        Assert.Equal(original.Nodes().Count, restored.Nodes().Count);
        Assert.Equal(original.Wires().Count, restored.Wires().Count);

        // Identity survives, which is what makes a diff of two saves meaningful and what lets a
        // cached result still match after a reload.
        NodeInstance restoredPoints = restored.Node(points);
        Assert.Equal(original.Node(points).Definition.Key, restoredPoints.Definition.Key);
        Assert.Equal(LacingMode.CrossProduct, restoredPoints.Lacing);

        GraphDocumentNode written = document.Nodes.Single(node => node.Id == points);
        Assert.Equal(120.5, written.X);
        Assert.Equal(40.0, written.Y);
    }

    [Fact]
    public void ReadingAndRewritingAFileReproducesItByteForByte()
    {
        Graph graph = BuildGraph(out NodeId points, out _);
        string first = SparkFile.Write(GraphDocument.Capture(graph, _ => (11.5, 22.25)));

        string second = SparkFile.Write(SparkFile.Read(first));
        string third = SparkFile.Write(SparkFile.Read(second));

        Assert.Equal(first, second);
        Assert.Equal(second, third);

        // And a round trip through a live graph, which is the path the application takes when a
        // user opens a file and saves it without touching anything.
        Graph reloaded = SparkFile.Read(first).Restore(Library);
        string fromGraph = SparkFile.Write(GraphDocument.Capture(reloaded, _ => (11.5, 22.25)));
        Assert.Equal(first, fromGraph);
        Assert.NotEqual(NodeId.None, points);
    }

    [Fact]
    public void TheFileDoesNotInheritTheOrderNodesWereAddedIn()
    {
        // Two graphs holding the same nodes, built in opposite orders. If the writer took the
        // in-memory order, these would differ and every save after a node was deleted and re-added
        // would produce a diff of the whole file.
        NodeDefinition value = Library.ByName("Number.Value");
        NodeId first = NodeId.New();
        NodeId second = NodeId.New();

        Graph forwards = new();
        forwards.AddNode(value, first);
        forwards.AddNode(value, second);

        Graph backwards = new();
        backwards.AddNode(value, second);
        backwards.AddNode(value, first);

        Assert.Equal(
            SparkFile.Write(GraphDocument.Capture(forwards)),
            SparkFile.Write(GraphDocument.Capture(backwards)));
    }

    [Fact]
    public void EveryLiteralKindComesBackAsTheTypeItWentInAs()
    {
        // JSON cannot tell 1 from 1.0, and Spark's ports are typed, so the kind is written beside
        // the value. Without it an integer port would come back holding a double and rebind wrongly.
        Graph graph = new();
        NodeDefinition definition = Library.ByName("Fixtures.Literals");
        NodeId id = NodeId.New();
        graph.AddNode(definition, id);
        graph.SetLiteral(id, 0, 42);
        graph.SetLiteral(id, 1, 1.5);
        graph.SetLiteral(id, 2, true);
        graph.SetLiteral(id, 3, "a string");
        graph.SetLiteral(id, 4, Angle.FromDegrees(30.0));

        Graph restored = SparkFile.Read(SparkFile.Write(GraphDocument.Capture(graph)))
            .Restore(Library);
        NodeInstance node = restored.Node(id);

        Assert.Equal(42, Assert.IsType<int>(node.Literal(0)));
        Assert.Equal(1.5, Assert.IsType<double>(node.Literal(1)));
        Assert.True(Assert.IsType<bool>(node.Literal(2)));
        Assert.Equal("a string", Assert.IsType<string>(node.Literal(3)));
        Assert.Equal(30.0, Assert.IsType<Angle>(node.Literal(4)).Degrees, 1e-12);
    }

    [Fact]
    public void ANumberKeepsItsFullPrecisionThroughTheFile()
    {
        // A shortest-round-trippable format, not a fixed number of decimals. One third written to
        // fifteen places and read back is not one third, and the difference propagates.
        Graph graph = new();
        NodeDefinition definition = Library.ByName("Fixtures.Literals");
        NodeId id = NodeId.New();
        graph.AddNode(definition, id);
        double awkward = 1.0 / 3.0;
        graph.SetLiteral(id, 1, awkward);

        Graph restored = SparkFile.Read(SparkFile.Write(GraphDocument.Capture(graph)))
            .Restore(Library);

        Assert.Equal(awkward, Assert.IsType<double>(restored.Node(id).Literal(1)));
    }

    [Fact]
    public void AValueTheFormatCannotHoldIsRefusedAtSaveTime()
    {
        // Refused while the user still has the value, rather than at load time when it is gone.
        Graph graph = new();
        NodeDefinition definition = Library.ByName("Fixtures.Literals");
        NodeId id = NodeId.New();
        graph.AddNode(definition, id);
        graph.SetLiteral(id, 3, new Point3d(1.0, 2.0, 3.0));

        SparkFileException error =
            Assert.Throws<SparkFileException>(() => GraphDocument.Capture(graph));

        Assert.Equal(DiagnosticCodes.UnwritableLiteral, error.Diagnostic.Code);
        Assert.Equal(3, error.Diagnostic.PortIndex);
        Assert.Contains("Point3d", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileFromANewerBuildIsRefusedWholeRatherThanPartlyRead()
    {
        string text = SparkFile.Write(GraphDocument.Capture(BuildGraph(out _, out _)))
            .Replace("\"formatVersion\": 1", "\"formatVersion\": 99", StringComparison.Ordinal);

        SparkFileException error = Assert.Throws<SparkFileException>(() => SparkFile.Read(text));

        Assert.Equal(DiagnosticCodes.UnreadableFormatVersion, error.Diagnostic.Code);
        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unknown node is never silently skipped. <b>What changed on 2026-08-31 is how that is
    /// achieved, not whether it holds</b>: the default is now a placeholder that keeps the node,
    /// its key and its wires (`E7-T6`), where it used to be a refusal. The original assertion has
    /// moved to the explicit strict policy below, and the property this test was written to
    /// protect — that the node does not vanish — is now checked directly.
    /// </summary>
    [Fact]
    public void AnUnknownNodeIsKeptRatherThanSkipped()
    {
        string text = SparkFile.Write(GraphDocument.Capture(BuildGraph(out _, out _)))
            .Replace("Point.ByCoordinates", "Point.BySomethingElse", StringComparison.Ordinal);

        Graph graph = SparkFile.Read(text).Restore(Library);

        Assert.Contains(graph.Nodes(), n => n.Definition.Key.Name == "Point.BySomethingElse");
        Assert.Contains(graph.Nodes(), n => PlaceholderNode.IsPlaceholder(n.Definition));
    }

    /// <summary>
    /// The strict policy still refuses and still names the node, for a headless check that must
    /// not proceed on an incomplete graph.
    /// </summary>
    [Fact]
    public void AnUnknownNodeIsNamedWhenTheStrictPolicyIsAsked()
    {
        string text = SparkFile.Write(GraphDocument.Capture(BuildGraph(out _, out _)))
            .Replace("Point.ByCoordinates", "Point.BySomethingElse", StringComparison.Ordinal);

        SparkFileException error = Assert.Throws<SparkFileException>(
            () => SparkFile.Read(text).Restore(Library, null, MissingNodePolicy.Refuse));

        Assert.Equal(DiagnosticCodes.UnknownNodeDefinition, error.Diagnostic.Code);
        Assert.Contains("Point.BySomethingElse", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"formatVersion\": 1, \"nodes\": [{\"key\": \"a/b\"}]}")]
    [InlineData("{\"formatVersion\": 1, \"nodes\": [{\"id\": \"not-a-guid\", \"key\": \"a/b\"}]}")]
    public void MalformedFilesAreRefusedWithAReason(string text)
    {
        SparkFileException error = Assert.Throws<SparkFileException>(() => SparkFile.Read(text));

        Assert.Equal(DiagnosticCodes.MalformedGraphFile, error.Diagnostic.Code);
        Assert.NotEmpty(error.Message);
    }

    [Fact]
    public void AFileContainingACycleOpensAndReportsIt()
    {
        // A file is not a gesture. A wire the canvas would refuse still has to load, or a graph
        // that acquired a cycle through a hand edit could never be opened to be repaired.
        Graph graph = new();
        NodeDefinition add = Library.ByName("Math.Add");
        NodeId first = NodeId.New();
        NodeId second = NodeId.New();
        graph.AddNode(add, first);
        graph.AddNode(add, second);

        GraphDocument document = new(
            GraphDocument.CurrentFormatVersion,
            GraphDocument.Capture(graph).Nodes,
            [
                new GraphDocumentWire(first, 0, second, 0),
                new GraphDocumentWire(second, 0, first, 0),
            ]);

        Graph restored = SparkFile.Read(SparkFile.Write(document)).Restore(Library);

        Assert.Equal(2, restored.Wires().Count);
        TopologicalOrder order = TopologicalOrder.Of(restored);
        Assert.True(order.HasCycle);
        Assert.Equal(2, order.CyclicNodes.Count);
    }

    [Fact]
    public void TheFileUsesLineFeedsOnEveryPlatform()
    {
        // Not Environment.NewLine. A format whose whole premise is a quiet diff cannot write CRLF
        // on Windows and LF on Linux: a graph saved on one and re-saved on the other would produce
        // a diff of every line while nothing about the graph had changed.
        string text = SparkFile.Write(GraphDocument.Capture(BuildGraph(out _, out _)));

        Assert.DoesNotContain(CarriageReturn, text);
        Assert.Contains(LineFeed, text);
    }

    [Fact]
    public void TheWrittenFileIsIndentedTwoSpacesAndEndsInANewline()
    {
        string text = SparkFile.Write(GraphDocument.Capture(BuildGraph(out _, out _)));

        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Contains("\n  \"nodes\": [", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\t", text, StringComparison.Ordinal);
    }

    private static Graph BuildGraph(out NodeId points, out NodeId range)
    {
        Graph graph = new();
        NodeDefinition pointDefinition = Library.ByName("Point.ByCoordinates");
        NodeDefinition rangeDefinition = Library.ByName("Number.Range");

        points = NodeId.New();
        range = NodeId.New();
        graph.AddNode(pointDefinition, points);
        graph.AddNode(rangeDefinition, range);

        graph.SetLacing(points, LacingMode.CrossProduct);
        graph.SetLiteral(range, 0, 0.0);
        graph.SetLiteral(range, 1, 9.0);
        graph.SetLiteral(range, 2, 1.0);
        graph.TryConnect(range, 0, points, 0);

        return graph;
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        library.Add(NodeImporter.Import([typeof(Fixtures)], "Fixtures"));
        return library;
    }
}

/// <summary>A node whose ports cover every literal kind the format supports.</summary>
public static class Fixtures
{
    /// <summary>Takes one of everything.</summary>
    /// <param name="count">An integer port.</param>
    /// <param name="size">A number port.</param>
    /// <param name="flag">A true/false port.</param>
    /// <param name="label">A text port.</param>
    /// <param name="turn">An angle port.</param>
    /// <returns>Nothing anybody uses; the ports are the point.</returns>
    public static double Literals(
        int count = 0, double size = 0.0, bool flag = false, string label = "", Angle turn = default) =>
        count + size + (flag ? 1.0 : 0.0) + label.Length + turn.Radians;
}

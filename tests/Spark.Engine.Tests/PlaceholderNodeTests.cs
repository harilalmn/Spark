using System;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Opening a graph on a machine without one of its packages (<c>E7-T6</c>, <c>E7-T7</c>).
/// </summary>
/// <remarks>
/// The promise being tested is narrow and absolute: <b>nobody's graph is ever damaged</b>. Not
/// "mostly preserved", not "recoverable" — the file that goes in is the file that comes back out,
/// byte for byte, having been through a session that could not understand part of it.
/// </remarks>
public sealed class PlaceholderNodeTests
{
    /// <summary>
    /// <b>The test that makes E7-T6 a fact rather than an intention.</b> A graph is saved, one of
    /// its node keys is renamed to something no library has, and the result is opened and re-saved.
    /// The bytes match.
    /// </summary>
    [Fact]
    public void AGraphWithAMissingPackageReSavesByteIdentically()
    {
        string original = GraphMissingOneNode();

        GraphDocument reopened = GraphDocument.Capture(SparkFile.Read(original).Restore(Library()));
        string rewritten = SparkFile.Write(reopened);

        Assert.Equal(original, rewritten);
    }

    /// <summary>The placeholder keeps the key it stood in for, which is what the re-save writes.</summary>
    [Fact]
    public void ThePlaceholderKeepsTheOriginalNodeKey()
    {
        Graph graph = SparkFile.Read(GraphMissingOneNode()).Restore(Library());

        NodeInstance placeholder = Assert.Single(
            graph.Nodes(), n => PlaceholderNode.IsPlaceholder(n.Definition));

        Assert.Equal("Acme.Nodes/Widget.ByThing", placeholder.Definition.Key.Value);
    }

    /// <summary>
    /// Every wire survives — on both sides. A placeholder that dropped an outgoing wire would
    /// still re-save the node, and the loss would show up only when the user next looked.
    /// </summary>
    [Fact]
    public void WiresIntoAndOutOfTheMissingNodeAreKept()
    {
        Graph graph = SparkFile.Read(GraphMissingOneNode()).Restore(Library());

        NodeInstance placeholder = graph.Nodes().Single(n => PlaceholderNode.IsPlaceholder(n.Definition));

        Assert.Contains(graph.Wires(), w => w.Target == placeholder.Id);
        Assert.Contains(graph.Wires(), w => w.Source == placeholder.Id);
    }

    /// <summary>
    /// The port count is inferred from what the file uses, which is the only evidence available.
    /// Too few ports and the wires cannot attach; too many and the node grows phantom inputs that
    /// the user can type into.
    /// </summary>
    [Fact]
    public void PortCountsAreInferredFromWhatTheFileActuallyUses()
    {
        Graph graph = SparkFile.Read(GraphMissingOneNode()).Restore(Library());

        NodeInstance placeholder = graph.Nodes().Single(n => PlaceholderNode.IsPlaceholder(n.Definition));

        // The staged graph wires into input 1 and takes a literal on input 0, and one wire leaves
        // output 0. Two in, one out — no more.
        Assert.Equal(2, placeholder.Definition.Inputs.Count);
        Assert.Single(placeholder.Definition.Outputs);
    }

    /// <summary>
    /// A placeholder refuses to evaluate and names the package. Returning null would let the graph
    /// compute a confident wrong answer downstream, which is worse than not computing at all.
    /// </summary>
    [Fact]
    public void EvaluatingAPlaceholderRefusesAndNamesThePackage()
    {
        NodeDefinition placeholder = PlaceholderNode.For(NodeKey.Parse("Acme.Nodes/Widget.ByThing"), 1, 1);

        MissingPackageException thrown = Assert.Throws<MissingPackageException>(
            () => placeholder.Invoke([null]));

        Assert.Contains("Acme.Nodes", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("not installed", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A node with no outgoing wire still gets an output port, because a definition without one
    /// cannot exist and a node that cannot be wired downstream cannot even report that it failed.
    /// </summary>
    [Fact]
    public void APlaceholderAlwaysHasAtLeastOneOutput()
    {
        NodeDefinition placeholder = PlaceholderNode.For(NodeKey.Parse("Acme.Nodes/Widget.ByThing"), 0, 0);

        Assert.Empty(placeholder.Inputs);
        Assert.Single(placeholder.Outputs);
    }

    /// <summary>
    /// The strict policy is still available and still names the node, because a headless check
    /// that proceeds on an incomplete graph is a different kind of wrong.
    /// </summary>
    [Fact]
    public void TheRefusePolicyStillRefusesAndNamesTheNode()
    {
        SparkFileException error = Assert.Throws<SparkFileException>(
            () => SparkFile.Read(GraphMissingOneNode()).Restore(Library(), null, MissingNodePolicy.Refuse));

        Assert.Equal(DiagnosticCodes.UnknownNodeDefinition, error.Diagnostic.Code);
        Assert.Contains("Widget.ByThing", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A definition that is present is never placeholdered. Obvious, and it is the assertion that
    /// stops a regression here from replacing the entire library with placeholders silently.
    /// </summary>
    [Fact]
    public void NodesThatAreInstalledAreNotPlaceholdered()
    {
        Graph graph = SparkFile.Read(GraphMissingOneNode()).Restore(Library());

        Assert.Equal(2, graph.Nodes().Count(n => !PlaceholderNode.IsPlaceholder(n.Definition)));
    }

    /// <summary>
    /// A graph with nothing missing is untouched by the placeholder path, and still re-saves
    /// byte-identically. The control for the headline test.
    /// </summary>
    [Fact]
    public void AGraphWithNothingMissingIsUnaffected()
    {
        string original = SparkFile.Write(GraphDocument.Capture(BuildGraph()));

        string rewritten = SparkFile.Write(GraphDocument.Capture(SparkFile.Read(original).Restore(Library())));

        Assert.Equal(original, rewritten);
        Assert.DoesNotContain(
            SparkFile.Read(rewritten).Nodes,
            n => n.Key.Package == PlaceholderNode.Category);
    }

    /// <summary>
    /// The staged file: a real graph whose middle node has been renamed to a package nothing has.
    /// Renaming the text rather than building a fake definition is deliberate — it is exactly what
    /// a user's file looks like on a machine without the package.
    /// </summary>
    private static string GraphMissingOneNode() =>
        SparkFile.Write(GraphDocument.Capture(BuildGraph()))
            .Replace("Spark.Core/Number.Add", "Acme.Nodes/Widget.ByThing", StringComparison.Ordinal);

    private static Graph BuildGraph()
    {
        NodeLibrary library = Library();
        Graph graph = new();

        NodeId source = graph.AddNode(library.Get(NodeKey.Parse("Spark.Core/Number.One"))).Id;
        NodeId middle = graph.AddNode(library.Get(NodeKey.Parse("Spark.Core/Number.Add"))).Id;
        NodeId sink = graph.AddNode(library.Get(NodeKey.Parse("Spark.Core/Number.Double"))).Id;

        graph.SetLiteral(middle, 0, 7.5);
        graph.LoadWire(source, 0, middle, 1);
        graph.LoadWire(middle, 0, sink, 0);
        return graph;
    }

    private static NodeLibrary Library()
    {
        NodeLibrary library = new();

        library.Add(new NodeDefinition(
            NodeKey.Parse("Spark.Core/Number.One"),
            "Number.One",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [1.0]));

        library.Add(new NodeDefinition(
            NodeKey.Parse("Spark.Core/Number.Add"),
            "Number.Add",
            [new PortDefinition("a", typeof(double), 0), new PortDefinition("b", typeof(double), 0)],
            [new PortDefinition("sum", typeof(double), 0)],
            args => [Convert.ToDouble(args[0]) + Convert.ToDouble(args[1])]));

        library.Add(new NodeDefinition(
            NodeKey.Parse("Spark.Core/Number.Double"),
            "Number.Double",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("doubled", typeof(double), 0)],
            args => [Convert.ToDouble(args[0]) * 2.0]));

        return library;
    }
}

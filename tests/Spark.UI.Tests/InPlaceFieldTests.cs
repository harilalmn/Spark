using System;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Nodes.Core;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Typing a value into a node on the canvas — `E8-T5`.
/// </summary>
/// <remarks>
/// <para>
/// <b>An input node whose value lives in a side panel is a node you cannot read.</b> Six numbers in
/// a graph are six identical boxes labelled <c>Number.Value</c>, and finding which one is the wall
/// height means clicking each in turn. Asked for directly, against Dynamo's input nodes.
/// </para>
/// <para>
/// <b>The editing itself is a real <c>TextBox</c> over the drawing</b>, which is the hybrid overlay
/// <c>GraphCanvas</c>'s remarks have described since it was written. What is asserted here is
/// everything either side of that: which nodes offer a field, what it shows, what it accepts, and
/// what it refuses.
/// </para>
/// </remarks>
public sealed class InPlaceFieldTests
{
    /// <summary>The two input nodes offer a field, and nothing else does.</summary>
    [Fact]
    public void TheInputNodesOfferAField()
    {
        Assert.True(Library.ByName("Number.Value").HasField);
        Assert.True(Library.ByName("String.Value").HasField);

        Assert.Equal(
            ["Number.Value", "String.Value"],
            Library.Definitions()
                .Where(definition => definition.HasField)
                .Select(definition => definition.DisplayName)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>A node with a field is taller, so the box is not drawn over its bottom edge.</summary>
    [Fact]
    public void AFieldNodeIsTallerThanItsPortsAlone()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Value"), 0, 0);
        CanvasNode node = graph.Nodes[slot];

        Assert.True(node.HasField);

        double portsOnly = CanvasNode.HeaderHeight
            + (System.Math.Max(node.Inputs.Count, node.Outputs.Count) * CanvasNode.PortPitch)
            + CanvasNode.BodyPadding;

        Assert.Equal(portsOnly + CanvasNode.FieldHeight, node.Height);
    }

    /// <summary>And the box sits inside the node.</summary>
    [Fact]
    public void TheFieldIsInsideTheNode()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Value"), 30, 40);
        CanvasNode node = graph.Nodes[slot];

        node.FieldBox(out double x, out double y, out double width, out double height);

        Assert.True(x > node.X);
        Assert.True(x + width < node.X + node.Width);
        Assert.True(y > node.Y + CanvasNode.HeaderHeight);
        Assert.True(y + height <= node.Y + node.Height);
    }

    /// <summary>The field shows the literal the node holds.</summary>
    [Fact]
    public void TheFieldShowsTheLiteral()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Value"), 0, 0);

        Assert.Equal("0", graph.FieldText(slot));

        Assert.True(graph.SetFieldText(slot, "42.5"));

        Assert.Equal("42.5", graph.FieldText(slot));
        Assert.Equal(42.5, Assert.IsType<double>(graph.Literal(slot, 0)));
    }

    /// <summary>Text goes into a text port as text, without being parsed at.</summary>
    [Fact]
    public void ATextFieldTakesText()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("String.Value"), 0, 0);

        Assert.True(graph.SetFieldText(slot, "Level 3"));

        Assert.Equal("Level 3", Assert.IsType<string>(graph.Literal(slot, 0)));
    }

    /// <summary>
    /// <b>Text that will not parse commits nothing, and is not an error.</b> Somebody half way
    /// through typing <c>-</c> or <c>1e</c> has not made a mistake yet, and a node that fell back
    /// to zero would discard what they were typing and re-run the graph on a value they never
    /// asked for.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("1e")]
    [InlineData("four")]
    public void UnparseableTextCommitsNothing(string text)
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Value"), 0, 0);

        graph.SetFieldText(slot, "7");

        Assert.False(graph.SetFieldText(slot, text));
        Assert.Equal(7.0, graph.Literal(slot, 0));
    }

    /// <summary>Setting the same value again reports no change, so nothing re-runs.</summary>
    [Fact]
    public void SettingTheSameTextChangesNothing()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Value"), 0, 0);

        Assert.True(graph.SetFieldText(slot, "12"));
        Assert.False(graph.SetFieldText(slot, "12"));
    }

    /// <summary>
    /// <b>The text is rendered invariantly</b>, because this is what gets parsed back — and the
    /// value is on its way into a document that gets opened on other people's machines.
    /// </summary>
    [Fact]
    public void TheTextRoundTripsThroughItself()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Value"), 0, 0);

        Assert.True(graph.SetFieldText(slot, "1234.5678"));

        string shown = Assert.IsType<string>(graph.FieldText(slot));

        Assert.Contains(".", shown, StringComparison.Ordinal);
        Assert.True(graph.SetFieldText(slot, "0"));
        Assert.True(graph.SetFieldText(slot, shown));
        Assert.Equal(1234.5678, graph.Literal(slot, 0));
    }

    /// <summary>A node with no field has no text to show, rather than an empty one.</summary>
    [Fact]
    public void ANodeWithoutAFieldHasNoFieldText()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Point.ByCoordinates"), 0, 0);

        Assert.False(graph.Nodes[slot].HasField);
        Assert.Null(graph.FieldText(slot));
        Assert.False(graph.SetFieldText(slot, "3"));
    }

    /// <summary>
    /// A node claiming a field with no input port to put it on does not get one — the same guard
    /// the slider has, for the same reason.
    /// </summary>
    [Fact]
    public void ANodeWithNoInputsIsNotGivenAField()
    {
        NodeDefinition malformed = new(
            new NodeKey("Test", "NoInputs"),
            "NoInputs",
            [],
            [new PortDefinition("out", typeof(double), 0)],
            _ => [0.0],
            hasField: true);

        CanvasGraph graph = new();
        int slot = graph.Add(malformed, 0, 0);

        Assert.True(malformed.HasField);
        Assert.False(graph.Nodes[slot].HasField);
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Number).Assembly));

        return library;
    }
}

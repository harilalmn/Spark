using Spark.UI.Canvas;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The temporary node model the canvas drives until the graph engine lands. These tests cover the
/// parts the canvas actually depends on — port geometry, connection rules and bounds — so that
/// replacing the model with the real one has something to be checked against.
/// </summary>
public sealed class PlaceholderGraphTests
{
    [Fact]
    public void ANodesHeightFollowsItsPortCount()
    {
        PlaceholderNode one = Node("a", ["x"], ["out"]);
        PlaceholderNode three = Node("b", ["x", "y", "z"], ["out"]);

        Assert.True(three.Height > one.Height);
        Assert.Equal(PlaceholderNode.PortPitch * 2, three.Height - one.Height);
    }

    [Fact]
    public void InputPortsSitOnTheLeftEdgeAndOutputsOnTheRight()
    {
        PlaceholderNode node = Node("a", ["x", "y"], ["out"]);
        node.X = 100;
        node.Y = 50;

        node.InputPortCentre(0, out double inputX, out double inputY);
        node.OutputPortCentre(0, out double outputX, out double outputY);

        Assert.Equal(100, inputX);
        Assert.Equal(100 + node.Width, outputX);
        Assert.Equal(inputY, outputY);

        // Ports start below the header and step by one pitch each.
        Assert.Equal(50 + PlaceholderNode.HeaderHeight + (PlaceholderNode.PortPitch * 0.5), inputY);

        node.InputPortCentre(1, out _, out double secondY);
        Assert.Equal(inputY + PlaceholderNode.PortPitch, secondY);
    }

    [Fact]
    public void AWireMustRunFromAnOutputToAnInputOnADifferentNode()
    {
        PlaceholderGraph graph = new();
        int a = graph.Add(Node("a", [], ["out"]));
        int b = graph.Add(Node("b", ["in"], []));

        Assert.False(graph.AddWire(new PlaceholderWire(
            new PlaceholderPort(a, 0, IsOutput: false), new PlaceholderPort(b, 0, IsOutput: false))));

        Assert.False(graph.AddWire(new PlaceholderWire(
            new PlaceholderPort(a, 0, IsOutput: true), new PlaceholderPort(b, 0, IsOutput: true))));

        Assert.False(graph.AddWire(new PlaceholderWire(
            new PlaceholderPort(a, 0, IsOutput: true), new PlaceholderPort(a, 0, IsOutput: false))));

        Assert.True(graph.AddWire(new PlaceholderWire(
            new PlaceholderPort(a, 0, IsOutput: true), new PlaceholderPort(b, 0, IsOutput: false))));

        Assert.Single(graph.Wires);
    }

    [Fact]
    public void ASecondWireIntoOneInputReplacesTheFirst()
    {
        PlaceholderGraph graph = new();
        int a = graph.Add(Node("a", [], ["out"]));
        int b = graph.Add(Node("b", [], ["out"]));
        int c = graph.Add(Node("c", ["in"], []));

        graph.AddWire(new PlaceholderWire(
            new PlaceholderPort(a, 0, IsOutput: true), new PlaceholderPort(c, 0, IsOutput: false)));
        graph.AddWire(new PlaceholderWire(
            new PlaceholderPort(b, 0, IsOutput: true), new PlaceholderPort(c, 0, IsOutput: false)));

        // An input takes one wire. Replacing rather than refusing is what a user reconnecting an
        // input expects, and it is what every node editor does.
        Assert.Single(graph.Wires);
        Assert.Equal(b, graph.Wires[0].From.NodeIndex);
    }

    [Fact]
    public void ConnectednessIsReportedForBothEnds()
    {
        PlaceholderGraph graph = new();
        int a = graph.Add(Node("a", [], ["out"]));
        int b = graph.Add(Node("b", ["in"], []));

        PlaceholderPort output = new(a, 0, IsOutput: true);
        PlaceholderPort input = new(b, 0, IsOutput: false);

        Assert.False(graph.IsConnected(output));

        graph.AddWire(new PlaceholderWire(output, input));

        Assert.True(graph.IsConnected(output));
        Assert.True(graph.IsConnected(input));
    }

    [Fact]
    public void AnEmptyGraphHasNonDegenerateBounds()
    {
        CanvasBounds bounds = new PlaceholderGraph().ComputeBounds();

        // Zoom-to-fit divides by these, so a zero-area default would produce an infinite zoom.
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void GraphBoundsCoverEveryNode()
    {
        PlaceholderGraph graph = new();
        PlaceholderNode first = Node("a", [], ["out"]);
        PlaceholderNode second = Node("b", [], ["out"]);
        second.X = 900;
        second.Y = 700;

        graph.Add(first);
        graph.Add(second);

        CanvasBounds bounds = graph.ComputeBounds();

        Assert.True(bounds.Contains(first.X, first.Y));
        Assert.True(bounds.Contains(second.X + second.Width, second.Y + second.Height));
    }

    [Fact]
    public void TheDemoGraphExercisesEveryVisualState()
    {
        PlaceholderGraph graph = SampleGraphs.Demo();

        Assert.NotEmpty(graph.Nodes);
        Assert.NotEmpty(graph.Wires);

        PlaceholderNodeState seen = PlaceholderNodeState.None;
        foreach (PlaceholderNode node in graph.Nodes)
        {
            seen |= node.State;
        }

        Assert.True(seen.HasFlag(PlaceholderNodeState.Selected));
        Assert.True(seen.HasFlag(PlaceholderNodeState.Anchor));
        Assert.True(seen.HasFlag(PlaceholderNodeState.Error));
        Assert.True(seen.HasFlag(PlaceholderNodeState.Warning));
    }

    [Fact]
    public void TheSyntheticGraphIsTheSizeItWasAskedFor()
    {
        PlaceholderGraph graph = SampleGraphs.Synthetic(2000);

        Assert.Equal(2000, graph.Nodes.Count);
        Assert.True(graph.Wires.Count > 1000, $"Only {graph.Wires.Count} wires; the wire layer is under-exercised.");

        // The layout is a grid rather than a scatter because a grid is the worst case for a
        // uniform spatial index: every cell is evenly occupied, so no query gets to skip a sparse
        // region.
        CanvasBounds bounds = graph.ComputeBounds();
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
    }

    [Fact]
    public void ASyntheticGraphOfZeroNodesStillProducesOne() =>
        Assert.Single(SampleGraphs.Synthetic(0).Nodes);

    private static PlaceholderNode Node(string id, string[] inputs, string[] outputs) =>
        new(id, id, NodeCategory.Custom, 0, 0, inputs, outputs);
}

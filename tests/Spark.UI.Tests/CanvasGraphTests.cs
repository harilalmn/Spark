using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.UI.Canvas;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The canvas's view of a real engine graph: layout, the wire projection, the compatibility
/// preview, and the mapping from a run's node states onto the design language's visual states.
/// </summary>
public sealed class CanvasGraphTests
{
    [Fact]
    public void ANodesHeightFollowsItsPortCount()
    {
        CanvasGraph graph = new();
        int one = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);
        int three = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);

        Assert.True(graph.Nodes[three].Height > graph.Nodes[one].Height);
        Assert.Equal(CanvasNode.PortPitch * 2, graph.Nodes[three].Height - graph.Nodes[one].Height);
    }

    [Fact]
    public void InputPortsSitOnTheLeftEdgeAndOutputsOnTheRight()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Math.Add"), 100, 50);
        CanvasNode node = graph.Nodes[slot];

        node.InputPortCentre(0, out double inputX, out double inputY);
        node.OutputPortCentre(0, out double outputX, out double outputY);

        Assert.Equal(100, inputX);
        Assert.Equal(100 + node.Width, outputX);
        Assert.Equal(inputY, outputY);
        Assert.Equal(50 + CanvasNode.HeaderHeight + (CanvasNode.PortPitch * 0.5), inputY);

        node.InputPortCentre(1, out _, out double secondY);
        Assert.Equal(inputY + CanvasNode.PortPitch, secondY);
    }

    /// <summary>
    /// The category on a definition decides the header colour, and an unknown one still resolves to
    /// something legible rather than throwing.
    /// </summary>
    [Fact]
    public void CategoriesFromTheEngineReachTheRenderer()
    {
        CanvasGraph graph = new();
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);
        int math = graph.Add(TestGraphs.Library.ByName("Math.Add"), 0, 0);
        int input = graph.Add(TestGraphs.Library.ByName("Number.Range"), 0, 0);

        Assert.Equal(NodeCategory.Point, graph.Nodes[point].Category);
        Assert.Equal(NodeCategory.Math, graph.Nodes[math].Category);
        Assert.Equal(NodeCategory.Input, graph.Nodes[input].Category);
        Assert.Equal(NodeCategory.Custom, NodeCategoryNames.Parse("Something.A.Package.Invented"));
    }

    /// <summary>
    /// Declared rank reaches the port shape, which is the only thing on the canvas that says a
    /// lacing is about to happen before the graph runs (§7.6).
    /// </summary>
    [Fact]
    public void DeclaredRankReachesThePortShape()
    {
        CanvasGraph graph = new();
        int range = graph.Add(TestGraphs.Library.ByName("Number.Range"), 0, 0);
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);

        Assert.Equal(1, graph.Nodes[range].Outputs[0].DeclaredRank);
        Assert.Equal(0, graph.Nodes[point].Inputs[0].DeclaredRank);
    }

    /// <summary>
    /// The wire preview is the engine's own type check, not a UI guess. A number into a number is
    /// accepted; a point into a number is refused.
    /// </summary>
    [Fact]
    public void ThePreviewReportsTheEnginesAnswerRatherThanAlwaysAccepting()
    {
        CanvasGraph graph = new();
        int value = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int sin = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 600, 0);

        Assert.Equal(WireOutcome.Accepted, graph.Preview(Output(value, 0), Input(sin, 0)));

        // Point3d into a double port: no rule in the compatibility order matches.
        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(point, 0), Input(sin, 0)));

        // Output to output, and a node to itself, are refused before types are consulted.
        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(value, 0), Output(sin, 0)));
        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(sin, 0), Input(sin, 0)));
    }

    /// <summary>A wire that would close a cycle is refused, and the preview says so first.</summary>
    [Fact]
    public void AWireThatWouldCloseACycleIsRefused()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 300, 0);

        Assert.True(graph.TryConnect(Output(a, 0), Input(b, 0)));

        Assert.Equal(WireOutcome.Refused, graph.Preview(Output(b, 0), Input(a, 0)));
        Assert.False(graph.TryConnect(Output(b, 0), Input(a, 0)));
        Assert.Single(graph.Wires);
    }

    [Fact]
    public void ASecondWireIntoOneInputReplacesTheFirst()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 200);
        int c = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);

        Assert.True(graph.TryConnect(Output(a, 0), Input(c, 0)));
        Assert.True(graph.TryConnect(Output(b, 0), Input(c, 0)));

        CanvasWire wire = Assert.Single(graph.Wires);
        Assert.Equal(b, wire.From.NodeIndex);
    }

    [Fact]
    public void DisconnectingAWireRemovesItFromTheEngineToo()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);

        graph.TryConnect(Output(a, 0), Input(b, 0));
        CanvasWire wire = Assert.Single(graph.Wires);

        Assert.True(graph.Disconnect(wire));
        Assert.Empty(graph.Wires);
        Assert.Empty(graph.Engine.Wires());
    }

    /// <summary>
    /// Removing a node renumbers every slot after it, and the wire projection has to follow or the
    /// canvas draws wires between the wrong nodes.
    /// </summary>
    [Fact]
    public void RemovingANodeRenumbersTheSlotsAfterIt()
    {
        CanvasGraph graph = new();
        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        int c = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 600, 0);

        graph.TryConnect(Output(b, 0), Input(c, 0));
        graph.Remove(a);

        Assert.Equal(2, graph.Nodes.Count);

        CanvasWire wire = Assert.Single(graph.Wires);
        Assert.Equal(0, wire.From.NodeIndex);
        Assert.Equal(1, wire.To.NodeIndex);
    }

    /// <summary>
    /// The non-cascading rule, as a test. One node errors; the node below it is greyed as
    /// <i>not evaluated</i> and carries no diagnostic of its own.
    /// </summary>
    [Fact]
    public void AnErrorDoesNotCascadeIntoDownstreamErrors()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);
        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        graph.ApplyResult(result);

        CanvasNode divide = Node(graph, "Math.Divide");
        CanvasNode translate = Node(graph, "Point.Translate");

        Assert.True(divide.State.HasFlag(CanvasNodeState.Error));
        Assert.NotNull(divide.Message);

        Assert.True(translate.State.HasFlag(CanvasNodeState.NotEvaluated));
        Assert.False(translate.State.HasFlag(CanvasNodeState.Error));
        Assert.Null(translate.Message);

        // Exactly one node is blamed, however many are downstream of it.
        Assert.Equal(1, graph.Nodes.Count(node => node.State.HasFlag(CanvasNodeState.Error)));
    }

    /// <summary>
    /// The seeded demo is the whole point of the slice: two ranges of ten crossed into one hundred
    /// points, arranged as a grid rather than a diagonal.
    /// </summary>
    [Fact]
    public void TheDemoGraphProducesAHundredPointsInAGrid()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);
        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);

        CanvasNode points = Node(graph, "Point.ByCoordinates");
        SparkList grid = Assert.IsType<SparkList>(result.Value(points.Id));

        // Cross Product raises rank by k, not by one: ten crossed with ten is a 10 x 10 nested
        // list, never a flat hundred.
        Assert.Equal(2, grid.Rank);
        Assert.Equal(DemoGraphs.GridSide, grid.Count);

        SparkList row = Assert.IsType<SparkList>(grid[0]);
        Assert.Equal(DemoGraphs.GridSide, row.Count);

        // And the values really are a grid: the first row varies in y at a fixed x.
        Spark.Geometry.Point3d first = Assert.IsType<Spark.Geometry.Point3d>(row[0]);
        Spark.Geometry.Point3d second = Assert.IsType<Spark.Geometry.Point3d>(row[1]);
        Assert.Equal(first.X, second.X);
        Assert.NotEqual(first.Y, second.Y);
    }

    /// <summary>
    /// The same graph under Longest laces to a ten-point diagonal. This is the comparison the demo
    /// exists to make, and it fails if lacing is ever quietly ignored.
    /// </summary>
    [Fact]
    public void TheSameGraphUnderLongestProducesATenPointDiagonal()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);
        CanvasNode points = Node(graph, "Point.ByCoordinates");

        graph.Engine.SetLacing(points.Id, LacingMode.Longest);

        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        SparkList line = Assert.IsType<SparkList>(result.Value(points.Id));

        Assert.Equal(1, line.Rank);
        Assert.Equal(DemoGraphs.GridSide, line.Count);
    }

    /// <summary>
    /// Only ports nothing consumes are previewed, or the display node's hundred coloured points
    /// would be drawn coincident with the point node's hundred grey ones.
    /// </summary>
    [Fact]
    public void OnlyTerminalPortsArePreviewed()
    {
        CanvasGraph graph = DemoGraphs.Demo(TestGraphs.Library);

        HashSet<string> previewed =
        [
            .. graph.PreviewPorts().Select(port => graph.Nodes[port.Slot].Title),
        ];

        Assert.Contains("Display.ByGeometryColour", previewed);
        Assert.Contains("Point.Translate", previewed);
        Assert.DoesNotContain("Point.ByCoordinates", previewed);
        Assert.DoesNotContain("Number.Range", previewed);
    }

    /// <summary>
    /// Editing a literal marks the node and everything downstream dirty, which is what makes the
    /// next run recompute rather than serve the cache.
    /// </summary>
    [Fact]
    public void SettingALiteralMarksTheSubgraphDirty()
    {
        CanvasGraph graph = new();
        int value = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int sin = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        graph.TryConnect(Output(value, 0), Input(sin, 0));

        graph.Engine.MarkAllClean();
        graph.SetLiteral(value, 0, 90.0);

        IReadOnlySet<NodeId> dirty = graph.Engine.DirtyNodes();
        Assert.Contains(graph.Nodes[value].Id, dirty);
        Assert.Contains(graph.Nodes[sin].Id, dirty);

        EvaluationResult result = GraphEvaluator.Evaluate(graph.Engine, new EvaluationContext(), TestContext.Current.CancellationToken);
        Assert.Equal(1.0, Assert.IsType<double>(result.Value(graph.Nodes[sin].Id)), 9);
    }

    /// <summary>
    /// Every mutation runs inside the edit scope when one is set, which is what serialises canvas
    /// edits against an evaluation running on a worker thread.
    /// </summary>
    [Fact]
    public void EveryMutationRunsInsideTheEditScope()
    {
        CanvasGraph graph = new();
        int entered = 0;
        graph.EditScope = edit =>
        {
            entered++;
            edit();
        };

        int a = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        int b = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);
        graph.TryConnect(Output(a, 0), Input(b, 0));
        graph.SetLiteral(a, 0, 1.0);
        graph.Disconnect(graph.Wires[0]);
        graph.Remove(b);

        Assert.Equal(6, entered);
    }

    [Fact]
    public void AnEmptyGraphHasNonDegenerateBounds()
    {
        CanvasBounds bounds = new CanvasGraph().ComputeBounds();

        // Zoom-to-fit divides by these, so a zero-area default would produce an infinite zoom.
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void GraphBoundsCoverEveryNode()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Math.Sin"), 0, 0);
        int far = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 900, 700);

        CanvasBounds bounds = graph.ComputeBounds();
        CanvasNode node = graph.Nodes[far];

        Assert.True(bounds.Contains(0, 0));
        Assert.True(bounds.Contains(node.X + node.Width, node.Y + node.Height));
    }

    [Fact]
    public void TheSyntheticGraphIsTheSizeItWasAskedForAndIsWiredThroughout()
    {
        CanvasGraph graph = DemoGraphs.Synthetic(TestGraphs.Library, 2000);

        Assert.Equal(2000, graph.Nodes.Count);
        Assert.True(
            graph.Wires.Count > 1500,
            $"Only {graph.Wires.Count} wires; the wire layer is under-exercised by the benchmark.");
    }

    [Fact]
    public void ASyntheticGraphOfZeroNodesStillProducesOne() =>
        Assert.Single(DemoGraphs.Synthetic(TestGraphs.Library, 0).Nodes);

    private static CanvasNode Node(CanvasGraph graph, string title) =>
        graph.Nodes.First(node => string.Equals(node.Title, title, StringComparison.Ordinal));

    private static CanvasPort Output(int slot, int portIndex) => new(slot, portIndex, IsOutput: true);

    private static CanvasPort Input(int slot, int portIndex) => new(slot, portIndex, IsOutput: false);
}

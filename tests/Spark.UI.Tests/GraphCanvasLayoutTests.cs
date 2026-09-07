using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.UI.Canvas;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Cleaning up as the canvas actually performs it: over a real graph, with a real selection, and
/// with the edit reported the way the shell expects.
/// </summary>
/// <remarks>
/// <see cref="CanvasLayoutTests"/> owns the arithmetic. What is left to prove here is the four
/// things the arithmetic cannot know about — which nodes are in scope, that the wires reach it in
/// the right terms, that the spatial index is told, and that the edit is announced as one the
/// evaluator does not have to see.
/// </remarks>
public sealed class GraphCanvasLayoutTests
{
    [Fact]
    public void CleaningUpPutsEveryNodeToTheRightOfWhatFeedsIt() => WithCanvas((_, canvas) =>
    {
        Assert.Empty(canvas.Selection);
        Assert.True(canvas.CleanUpLayout());

        IReadOnlyList<CanvasNode> nodes = canvas.Graph.Nodes;
        Assert.True(nodes[1].X > nodes[0].X);
        Assert.True(nodes[2].X > nodes[1].X);
    });

    /// <summary>
    /// Nothing selected means the whole graph, and that is the common case: a user presses the key
    /// to tidy what they are looking at, having selected nothing at all.
    /// </summary>
    [Fact]
    public void WithNothingSelectedTheWholeGraphIsArranged() => WithCanvas((_, canvas) =>
    {
        List<(double X, double Y)> before = Positions(canvas);

        Assert.True(canvas.CleanUpLayout());

        Assert.All(Positions(canvas).Zip(before), pair => Assert.NotEqual(pair.Second, pair.First));
    });

    /// <summary>
    /// <b>A selection of two or more is a scope</b>, and a node outside it is not touched — the
    /// same promise <c>AlignSelection</c> makes.
    /// </summary>
    [Fact]
    public void ASelectionOfTwoArrangesOnlyThoseTwo() => WithCanvas((window, canvas) =>
    {
        ClickNode(window, canvas, 0);
        ClickNode(window, canvas, 1, RawInputModifiers.Control);
        Assert.Equal(2, canvas.Selection.Count);

        (double x, double y) = (canvas.Graph.Nodes[2].X, canvas.Graph.Nodes[2].Y);

        Assert.True(canvas.CleanUpLayout());

        Assert.Equal(x, canvas.Graph.Nodes[2].X);
        Assert.Equal(y, canvas.Graph.Nodes[2].Y);
    });

    /// <summary>
    /// <b>One selected node is not a scope.</b> Clicking a node to look at it and then pressing the
    /// key means <i>tidy this graph</i>; arranging a single node on its own would move nothing and
    /// look like a dead key.
    /// </summary>
    [Fact]
    public void ASingleSelectedNodeStillArrangesTheWholeGraph() => WithCanvas((window, canvas) =>
    {
        ClickNode(window, canvas, 0);
        Assert.Single(canvas.Selection);

        (double x, double y) = (canvas.Graph.Nodes[2].X, canvas.Graph.Nodes[2].Y);

        Assert.True(canvas.CleanUpLayout());

        Assert.NotEqual((x, y), (canvas.Graph.Nodes[2].X, canvas.Graph.Nodes[2].Y));
    });

    /// <summary>
    /// A move is a document edit that changes no value, so it must not start a run. A layout is the
    /// same claim: a position is not in a node's provenance.
    /// </summary>
    [Fact]
    public void CleaningUpIsAnEditThatDoesNotRequireARun() => WithCanvas((_, canvas) =>
    {
        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        canvas.CleanUpLayout();

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.False(edit.AffectsEvaluation);
        Assert.Equal("Clean up layout", edit.Label);
    });

    /// <summary>
    /// <b>The undo-stack guard.</b> Pressing the key on an already-tidy graph is how a user checks
    /// it is tidy, and it must not leave a step behind whose undo moves nothing — the same lesson
    /// the drag gesture learned as N19 and the alignments learned again.
    /// </summary>
    [Fact]
    public void CleaningUpTwiceRecordsOneEdit() => WithCanvas((_, canvas) =>
    {
        int edits = 0;
        canvas.GraphChanged += (_, _) => edits++;

        Assert.True(canvas.CleanUpLayout());
        Assert.False(canvas.CleanUpLayout());

        Assert.Equal(1, edits);
    });

    /// <summary>
    /// The spatial index is rebuilt inside <c>Render</c>, so a node moved by a command rather than
    /// by a gesture has to tell the index itself. If it does not, the node stays clickable where it
    /// used to be.
    /// </summary>
    [Fact]
    public void ACleanedUpNodeIsHitTestableWhereItNowIs() => WithCanvas((window, canvas) =>
    {
        Assert.True(canvas.CleanUpLayout());
        Assert.Empty(canvas.Selection);

        ClickNode(window, canvas, 1);

        Assert.Equal([1], canvas.Selection);
    });

    /// <summary>
    /// The menu asks this to decide whether to offer the command, and it counts what would actually
    /// be arranged rather than what is selected.
    /// </summary>
    [Fact]
    public void AGraphOfOneNodeCannotBeCleanedUp() => WithCanvas((_, canvas) =>
    {
        Assert.True(canvas.CanCleanUpLayout());

        canvas.Graph = OneNode();

        Assert.False(canvas.CanCleanUpLayout());
        Assert.False(canvas.CleanUpLayout());
    });

    /// <summary>
    /// A chain of three, wired 0 → 1 → 2 and placed <b>backwards</b>: the source is furthest right
    /// and lowest, so no assertion about flow order can pass by accident.
    /// </summary>
    private static CanvasGraph BackwardsChain()
    {
        CanvasGraph graph = new();
        int value = graph.Add(TestGraphs.Library.ByName("Number.Value"), 520, 300);
        int sin = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 260, 120);
        int cos = graph.Add(TestGraphs.Library.ByName("Math.Cos"), 40, 10);

        Assert.True(graph.TryConnect(
            new CanvasPort(value, 0, IsOutput: true), new CanvasPort(sin, 0, IsOutput: false)));
        Assert.True(graph.TryConnect(
            new CanvasPort(sin, 0, IsOutput: true), new CanvasPort(cos, 0, IsOutput: false)));

        return graph;
    }

    private static CanvasGraph OneNode()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 40, 40);
        return graph;
    }

    private static List<(double X, double Y)> Positions(GraphCanvas canvas)
    {
        List<(double X, double Y)> positions = [];
        foreach (CanvasNode node in canvas.Graph.Nodes)
        {
            positions.Add((node.X, node.Y));
        }

        return positions;
    }

    /// <summary>Clicks a node on its header, which is the one part of it that is never a port.</summary>
    private static void ClickNode(
        Window window, GraphCanvas canvas, int slot, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        CanvasNode node = canvas.Graph.Nodes[slot];
        Point point = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + (CanvasNode.HeaderHeight / 2)));

        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    /// <summary>
    /// Opens the chain in a headless window, runs a body against it, and <b>closes the window
    /// again</b> — which is not tidiness: a window still shown when the dispatch ends has its
    /// pending render drained during teardown, by which point the font manager is disposed
    /// (<see cref="GraphCanvasAlignmentTests"/> records the failure that costs).
    /// </summary>
    private static void WithCanvas(Action<Window, GraphCanvas> body) => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = BackwardsChain() };
        Window window = new()
        {
            Width = 800,
            Height = 600,
            Content = canvas,
        };

        window.Show();

        try
        {
            body(window, canvas);
        }
        finally
        {
            window.Close();
        }
    });
}

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.Engine;
using Spark.UI.Controls;
using Spark.UI.Graph;
using Spark.UI.Theming;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Control+drag copies what it drags, and File → New empties the document — `E8-T37`.
/// </summary>
/// <remarks>
/// <b>Both asked for directly.</b> Control used to be a second way to spell Shift on the canvas; it
/// is worth more as the copy modifier, which is what Dynamo, Grasshopper and every drawing
/// application use, and Shift on its own still adds to a selection.
/// </remarks>
public sealed class DuplicateAndNewTests
{
    /// <summary>
    /// <b>A copy is the same node with the same settings</b>, not a fresh one of the same kind.
    /// </summary>
    [Fact]
    public void ACopyCarriesTheSettingsOfWhatItCopied()
    {
        CanvasGraph graph = TestGraphs.Demo();
        CanvasNode source = graph.Nodes[0];

        source.CustomTitle = "Profile";
        source.ColourOverride = NodeCategory.Math;

        int copy = graph.Duplicate([0], 40, 0).Single();
        CanvasNode made = graph.Nodes[copy];

        Assert.NotEqual(source.Id, made.Id);
        Assert.Equal(source.Title, made.Title);
        Assert.Equal("Profile", made.CustomTitle);
        Assert.Equal(NodeCategory.Math, made.ColourOverride);
        Assert.Equal(source.X + 40, made.X, 3);
        Assert.Equal(
            graph.Engine.Node(source.Id).Lacing,
            graph.Engine.Node(made.Id).Lacing);
    }

    /// <summary>
    /// <b>The wires between copied nodes are copied too.</b> Duplicating a chain and getting
    /// unconnected nodes is the behaviour that makes people stop using the gesture.
    /// </summary>
    [Fact]
    public void WiresBetweenCopiedNodesAreCopied()
    {
        CanvasGraph graph = TestGraphs.Demo();

        int wiresBefore = graph.Wires.Count;
        Assert.True(wiresBefore > 0, "the demo graph should have wires to copy");

        IReadOnlyList<int> copies = graph.Duplicate(
            [.. Enumerable.Range(0, graph.Nodes.Count)], 400, 400);

        Assert.Equal(graph.Nodes.Count, copies.Count * 2);
        Assert.Equal(wiresBefore * 2, graph.Wires.Count);
    }

    /// <summary>A wire to a node outside the set is not copied: the copy is of what was selected.</summary>
    [Fact]
    public void AWireToSomethingUnselectedIsNotCopied()
    {
        CanvasGraph graph = TestGraphs.Demo();

        CanvasWire wire = graph.Wires[0];
        int wiresBefore = graph.Wires.Count;

        graph.Duplicate([wire.To.NodeIndex], 40, 40);

        Assert.Equal(wiresBefore, graph.Wires.Count);
    }

    /// <summary>
    /// <b>Control+drag leaves the original and takes a copy with the pointer.</b>
    /// </summary>
    [Fact]
    public void ControlDragCopiesTheNode() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas) = Open();

        int before = canvas.Graph.Nodes.Count;
        CanvasNode node = canvas.Graph.Nodes[0];

        double startX = node.X;
        Point from = Screen(canvas, node.X + 30, node.Y + 8);
        Point to = Screen(canvas, node.X + 130, node.Y + 8);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(to, RawInputModifiers.Control);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(before + 1, canvas.Graph.Nodes.Count);

        // The original stayed exactly where it was; the copy is the one that moved.
        Assert.Equal(startX, canvas.Graph.Nodes[0].X, 3);
        Assert.Equal(startX + 100, canvas.Graph.Nodes[^1].X, 3);
        Assert.Equal([canvas.Graph.Nodes.Count - 1], canvas.Selection.Order());
    });

    /// <summary>
    /// <b>Copies chain: Control+drag the copy and you get another.</b> Reported by the client after
    /// the first version — a copy lands selected, Control+click used to toggle it straight back out
    /// of the selection, and the drag then had nothing to copy. One extra click per node, in the
    /// gesture that exists to save clicks.
    /// </summary>
    [Fact]
    public void ControlDraggingACopyMakesAnotherCopy() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas) = Open();

        int before = canvas.Graph.Nodes.Count;
        CanvasNode node = canvas.Graph.Nodes[0];

        double x = node.X + 30;
        double y = node.Y + 8;

        // Three copies in a row, each one dragged out of the one before it, with no click in
        // between and nothing deselected.
        for (int copy = 0; copy < 3; copy++)
        {
            Point from = Screen(canvas, x, y);
            Point to = Screen(canvas, x + 120, y);

            window.MouseDown(from, MouseButton.Left, RawInputModifiers.Control);
            window.MouseMove(to, RawInputModifiers.Control);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.Control);

            x += 120;
        }

        Assert.Equal(before + 3, canvas.Graph.Nodes.Count);
        Assert.Single(canvas.Selection);
    });

    /// <summary>
    /// Control+click still takes a node out of a selection — the toggle waits for the release
    /// rather than going away.
    /// </summary>
    [Fact]
    public void ControlClickStillDeselectsASelectedNode() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas) = Open();

        int before = canvas.Graph.Nodes.Count;
        CanvasNode first = canvas.Graph.Nodes[0];
        CanvasNode second = canvas.Graph.Nodes[1];

        Point at = Screen(canvas, first.X + 30, first.Y + 8);
        Point other = Screen(canvas, second.X + 30, second.Y + 8);

        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);

        window.MouseDown(other, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(other, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(2, canvas.Selection.Count);

        window.MouseDown(other, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(other, MouseButton.Left, RawInputModifiers.Control);

        Assert.Single(canvas.Selection);

        // And neither click copied anything: a deselection is not a duplicate.
        Assert.Equal(before, canvas.Graph.Nodes.Count);
    });

    /// <summary>
    /// <b>Control+click on its own copies nothing.</b> The duplicate happens on the first movement,
    /// or a canvas would fill with copies made by people selecting things.
    /// </summary>
    [Fact]
    public void ControlClickWithoutADragCopiesNothing() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas) = Open();

        int before = canvas.Graph.Nodes.Count;
        CanvasNode node = canvas.Graph.Nodes[0];

        Point at = Screen(canvas, node.X + 30, node.Y + 8);

        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(before, canvas.Graph.Nodes.Count);
    });

    /// <summary>Shift still adds to the selection rather than copying.</summary>
    [Fact]
    public void ShiftStillAddsToTheSelection() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas) = Open();

        int before = canvas.Graph.Nodes.Count;

        for (int slot = 0; slot < 2; slot++)
        {
            CanvasNode node = canvas.Graph.Nodes[slot];
            Point at = Screen(canvas, node.X + 30, node.Y + 8);

            window.MouseDown(at, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.Shift);
        }

        Assert.Equal(before, canvas.Graph.Nodes.Count);
        Assert.Equal(2, canvas.Selection.Count);
    });

    /// <summary>File → New empties the document and starts a fresh history.</summary>
    [Fact]
    public void NewEmptiesTheDocument()
    {
        using MainWindowViewModel model = new();

        Assert.NotEmpty(model.Graph.Nodes);

        model.NewGraph();

        Assert.Empty(model.Graph.Nodes);
        Assert.Empty(model.Graph.Wires);

        // Undo must not bring the closed document back: that is a different operation from the one
        // Ctrl+Z promises.
        Assert.False(model.CanUndo);
    }

    private static (Window Window, GraphCanvas Canvas) Open()
    {
        GraphCanvas canvas = new() { Graph = TestGraphs.Demo() };
        Window window = new() { Width = 1200, Height = 800, Content = canvas };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        return (window, canvas);
    }

    private static Point Screen(GraphCanvas canvas, double worldX, double worldY) =>
        new(canvas.Transform.ToScreenX(worldX), canvas.Transform.ToScreenY(worldY));
}

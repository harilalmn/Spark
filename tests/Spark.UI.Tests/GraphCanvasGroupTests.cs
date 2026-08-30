using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.UI.Canvas;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Groups as a gesture reaches them: grabbed by the title strip, dragged with their members, and
/// removed without taking anything with them.
/// </summary>
/// <remarks>
/// <c>CanvasGroupTests</c> owns the model — membership, the derived frame, the file. What only a
/// pointer can prove is the part that decides whether a group is usable: that its frame does not
/// swallow the clicks that land inside it, since the inside of a group is where its own nodes are.
/// </remarks>
public sealed class GraphCanvasGroupTests
{
    [Fact]
    public void GroupingTheSelectionSelectsTheGroupAndRecordsAnEdit() => WithCanvas((_, canvas) =>
    {
        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        canvas.SelectOnly(0);
        CanvasGroup group = Assert.IsType<CanvasGroup>(canvas.GroupSelection("Radii"));

        Assert.Same(group, canvas.SelectedGroup);
        Assert.Empty(canvas.Selection);

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.Equal("Group nodes", edit.Label);
        Assert.False(edit.AffectsEvaluation);
    });

    [Fact]
    public void GroupingNothingDoesNothing() => WithCanvas((_, canvas) =>
    {
        int edits = 0;
        canvas.GraphChanged += (_, _) => edits++;

        Assert.False(canvas.CanGroupSelection());
        Assert.Null(canvas.GroupSelection());
        Assert.Equal(0, edits);
    });

    /// <summary>
    /// <b>The gesture that decides whether groups are usable.</b> A group's rectangle is mostly the
    /// gap between its own nodes. If the whole rectangle took clicks, a node inside a group could
    /// not be clicked and a marquee could not be started inside one — so only the title strip does.
    /// </summary>
    [Fact]
    public void OnlyTheTitleStripTakesTheClick() => WithCanvas((window, canvas) =>
    {
        canvas.SelectOnly(0);
        CanvasGroup group = Assert.IsType<CanvasGroup>(canvas.GroupSelection("Radii"));
        CanvasBounds frame = Assert.IsType<CanvasBounds>(canvas.Graph.GroupBounds(group));

        // Inside the frame, below the title strip, and not on a node: empty canvas as far as the
        // user is concerned, and a marquee is what should start there.
        ClickWorld(window, canvas, frame.MinX + 4, frame.MaxY - 4);
        Assert.Null(canvas.SelectedGroup);

        ClickWorld(window, canvas, frame.MinX + 20, frame.MinY + (CanvasGroup.TitleHeight / 2));
        Assert.Same(group, canvas.SelectedGroup);
    });

    [Fact]
    public void ANodeInsideAGroupIsStillClickable() => WithCanvas((window, canvas) =>
    {
        canvas.SelectOnly(0);
        canvas.GroupSelection("Radii");

        CanvasNode node = canvas.Graph.Nodes[0];
        ClickWorld(window, canvas, node.X + (node.Width / 2), node.Y + (CanvasNode.HeaderHeight / 2));

        Assert.Equal([0], canvas.Selection);
        Assert.Null(canvas.SelectedGroup);
    });

    [Fact]
    public void DraggingAGroupMovesItsMembers() => WithCanvas((window, canvas) =>
    {
        canvas.SelectOnly(0);
        CanvasGroup group = Assert.IsType<CanvasGroup>(canvas.GroupSelection("Radii"));
        CanvasBounds frame = Assert.IsType<CanvasBounds>(canvas.Graph.GroupBounds(group));

        double before = canvas.Graph.Nodes[0].X;
        double untouched = canvas.Graph.Nodes[1].X;

        double grabX = frame.MinX + 20;
        double grabY = frame.MinY + (CanvasGroup.TitleHeight / 2);
        DragWorld(window, canvas, grabX, grabY, grabX + 130, grabY);

        Assert.Equal(before + 130, canvas.Graph.Nodes[0].X, 6);

        // Only the members move. A group is a frame around some nodes, not around a region.
        Assert.Equal(untouched, canvas.Graph.Nodes[1].X, 6);
    });

    [Fact]
    public void DraggingAGroupBackToWhereItStartedRecordsNothing() => WithCanvas((window, canvas) =>
    {
        canvas.SelectOnly(0);
        CanvasGroup group = Assert.IsType<CanvasGroup>(canvas.GroupSelection("Radii"));
        CanvasBounds frame = Assert.IsType<CanvasBounds>(canvas.Graph.GroupBounds(group));

        int edits = 0;
        canvas.GraphChanged += (_, _) => edits++;

        Point start = Screen(canvas, frame.MinX + 20, frame.MinY + (CanvasGroup.TitleHeight / 2));
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Screen(canvas, frame.MinX + 220, frame.MinY + 200), RawInputModifiers.None);
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseUp(start, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, edits);
    });

    /// <summary>
    /// <b>Deleting a group keeps its nodes.</b> The most expensive surprise an editor can spring is
    /// taking the contents with the container, and it is the one users arrive expecting.
    /// </summary>
    [Fact]
    public void DeletingASelectedGroupLeavesEveryNodeWhereItWas() => WithCanvas((_, canvas) =>
    {
        int nodes = canvas.Graph.Nodes.Count;
        canvas.SelectOnly(0);
        canvas.GroupSelection("Radii");

        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        Assert.True(canvas.DeleteSelection());

        Assert.Empty(canvas.Graph.Groups);
        Assert.Equal(nodes, canvas.Graph.Nodes.Count);

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.Equal("Ungroup", edit.Label);
        Assert.False(edit.AffectsEvaluation);
    });

    [Fact]
    public void SelectingANodeClearsTheGroupSelection() => WithCanvas((window, canvas) =>
    {
        canvas.SelectOnly(0);
        canvas.GroupSelection("Radii");
        Assert.NotNull(canvas.SelectedGroup);

        CanvasNode node = canvas.Graph.Nodes[1];
        ClickWorld(window, canvas, node.X + (node.Width / 2), node.Y + (CanvasNode.HeaderHeight / 2));

        Assert.Null(canvas.SelectedGroup);
    });

    private static void ClickWorld(Window window, GraphCanvas canvas, double worldX, double worldY)
    {
        Point point = Screen(canvas, worldX, worldY);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void DragWorld(
        Window window, GraphCanvas canvas, double fromX, double fromY, double toX, double toY)
    {
        window.MouseDown(Screen(canvas, fromX, fromY), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Screen(canvas, toX, toY), RawInputModifiers.None);
        window.MouseUp(Screen(canvas, toX, toY), MouseButton.Left, RawInputModifiers.None);
    }

    private static Point Screen(GraphCanvas canvas, double worldX, double worldY) =>
        new(canvas.Transform.ToScreenX(worldX), canvas.Transform.ToScreenY(worldY));

    /// <remarks>The window is closed in a <c>finally</c> — N37.</remarks>
    private static void WithCanvas(Action<Window, GraphCanvas> body) => HeadlessSession.Run(() =>
    {
        // Placed well inside the window, so a group's frame — which extends beyond its members by
        // its padding and its title strip — is still on screen and clickable.
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 120, 160);
        graph.Add(TestGraphs.Library.ByName("Math.Sin"), 460, 400);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new()
        {
            Width = 900,
            Height = 700,
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

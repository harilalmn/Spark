using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Notes as a gesture reaches them: selected, dragged, deleted, and never mistaken for a node.
/// </summary>
/// <remarks>
/// <para>
/// <c>CanvasNoteTests</c> owns the model and the file. What is asserted here is the part only a
/// pointer can reach — that a note is behind everything, that selecting one is a third kind of
/// selection rather than a node with a strange slot, and that every note gesture is an edit the
/// evaluator does not have to see.
/// </para>
/// <para>
/// Every window is closed in a <c>finally</c>. A headless window left open renders during the
/// session's teardown, after the fonts are disposed — N37, and it presents as flakiness somewhere
/// else entirely.
/// </para>
/// </remarks>
public sealed class GraphCanvasNoteTests
{
    [Fact]
    public void ClickingANoteSelectsItAndNotANode() => WithCanvas((window, canvas) =>
    {
        CanvasNote note = canvas.Graph.AddNote(600, 400);

        ClickWorld(window, canvas, note.X + 20, note.Y + 20);

        Assert.Same(note, canvas.SelectedNote);
        Assert.Empty(canvas.Selection);
    });

    /// <summary>
    /// A note is drawn behind the nodes, so it has to lose to them on the way in as well. A note
    /// stretched under a graph must not steal the clicks of every node sitting on it.
    /// </summary>
    [Fact]
    public void ANodeOnTopOfANoteStillWinsTheClick() => WithCanvas((window, canvas) =>
    {
        CanvasNode node = canvas.Graph.Nodes[0];
        canvas.Graph.AddNote(node.X - 40, node.Y - 40).Width = 600;

        ClickWorld(window, canvas, node.X + (node.Width / 2), node.Y + (CanvasNode.HeaderHeight / 2));

        Assert.Null(canvas.SelectedNote);
        Assert.Equal([0], canvas.Selection);
    });

    /// <summary>
    /// Selecting a note clears the node selection. The two cannot be dragged or deleted together,
    /// and a selection spanning both would have to answer what Delete means.
    /// </summary>
    [Fact]
    public void SelectingANoteClearsTheNodeSelection() => WithCanvas((window, canvas) =>
    {
        CanvasNote note = canvas.Graph.AddNote(600, 400);

        canvas.SelectOnly(0);
        Assert.NotEmpty(canvas.Selection);

        ClickWorld(window, canvas, note.X + 20, note.Y + 20);

        Assert.Empty(canvas.Selection);
        Assert.Same(note, canvas.SelectedNote);
    });

    [Fact]
    public void SelectingANodeClearsTheNoteSelection() => WithCanvas((window, canvas) =>
    {
        CanvasNote note = canvas.Graph.AddNote(600, 400);
        ClickWorld(window, canvas, note.X + 20, note.Y + 20);
        Assert.NotNull(canvas.SelectedNote);

        CanvasNode node = canvas.Graph.Nodes[0];
        ClickWorld(window, canvas, node.X + (node.Width / 2), node.Y + (CanvasNode.HeaderHeight / 2));

        Assert.Null(canvas.SelectedNote);
    });

    [Fact]
    public void ClickingEmptyCanvasClearsTheNoteSelection() => WithCanvas((window, canvas) =>
    {
        CanvasNote note = canvas.Graph.AddNote(600, 400);
        ClickWorld(window, canvas, note.X + 20, note.Y + 20);
        Assert.NotNull(canvas.SelectedNote);

        // On screen and empty: below the two nodes, left of the note. A point outside the window
        // is not "empty canvas", it is no pointer event at all, and the selection would simply
        // stay as it was.
        ClickWorld(window, canvas, 20, 620);

        Assert.Null(canvas.SelectedNote);
    });

    [Fact]
    public void ANoteCanBeDraggedAndTheMoveIsRecordedOnce() => WithCanvas((window, canvas) =>
    {
        CanvasNote note = canvas.Graph.AddNote(600, 400);
        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        DragWorld(window, canvas, note.X + 20, note.Y + 20, note.X + 120, note.Y + 60);

        Assert.Equal(700, note.X, 6);
        Assert.Equal(440, note.Y, 6);

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.Equal("Move note", edit.Label);
        Assert.False(edit.AffectsEvaluation);
    });

    /// <summary>
    /// The net-displacement rule, for the third time: a drag out and back records nothing, because
    /// an undo step that moves nothing reads as undo being broken.
    /// </summary>
    [Fact]
    public void DraggingANoteBackToWhereItStartedRecordsNothing() => WithCanvas((window, canvas) =>
    {
        CanvasNote note = canvas.Graph.AddNote(600, 400);
        int edits = 0;
        canvas.GraphChanged += (_, _) => edits++;

        Point start = Screen(canvas, note.X + 20, note.Y + 20);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Screen(canvas, note.X + 220, note.Y + 220), RawInputModifiers.None);
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseUp(start, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, edits);
        Assert.Equal(600, note.X, 6);
    });

    [Fact]
    public void DeletingASelectedNoteRemovesItAndLeavesTheNodesAlone() => WithCanvas((window, canvas) =>
    {
        int nodes = canvas.Graph.Nodes.Count;
        CanvasNote note = canvas.Graph.AddNote(600, 400);
        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        ClickWorld(window, canvas, note.X + 20, note.Y + 20);

        Assert.True(canvas.DeleteSelection());

        Assert.Empty(canvas.Graph.Notes);
        Assert.Equal(nodes, canvas.Graph.Nodes.Count);
        Assert.Null(canvas.SelectedNote);

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.Equal("Delete note", edit.Label);
        Assert.False(edit.AffectsEvaluation);
    });

    [Fact]
    public void AddingANoteSelectsItAndRecordsAnEdit() => WithCanvas((window, canvas) =>
    {
        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        CanvasNote note = canvas.AddNote(120, 240);

        Assert.Same(note, canvas.SelectedNote);
        Assert.Same(note, Assert.Single(canvas.Graph.Notes));

        // Created empty. Placeholder text has to be deleted before the note can be written, and a
        // user who forgets is left with a note that says "New note" in the middle of their graph.
        Assert.Equal(string.Empty, note.Text);

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.Equal("Add note", edit.Label);
        Assert.False(edit.AffectsEvaluation);
    });

    /// <summary>
    /// Notes are hit-tested topmost first, so the one drawn last — on top — is the one a click
    /// lands on. Two overlapping notes must not both answer.
    /// </summary>
    [Fact]
    public void TheTopmostOfTwoOverlappingNotesTakesTheClick() => WithCanvas((window, canvas) =>
    {
        canvas.Graph.AddNote(600, 400);
        CanvasNote above = canvas.Graph.AddNote(620, 420);

        ClickWorld(window, canvas, 640, 440);

        Assert.Same(above, canvas.SelectedNote);
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

    private static void WithCanvas(Action<Window, GraphCanvas> body) => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = TestGraphs.SourceAndSink() };
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

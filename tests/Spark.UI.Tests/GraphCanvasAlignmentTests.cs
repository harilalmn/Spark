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
/// Aligning as the canvas actually performs it: over a real selection, against real nodes, with
/// the edit reported the way the shell expects.
/// </summary>
/// <remarks>
/// <see cref="CanvasAlignmentTests"/> owns the arithmetic. What is left to prove here is the three
/// things the arithmetic cannot know about — that the selection is what gets moved, that the
/// spatial index is told, and that the edit is announced as one the evaluator does not have to
/// see.
/// </remarks>
public sealed class GraphCanvasAlignmentTests
{
    [Fact]
    public void AligningMovesEverySelectedNodeAndNothingElse() => WithCanvas((window, canvas) =>
    {
        SelectAll(window, canvas);
        Assert.Equal(3, canvas.Selection.Count);

        Assert.True(canvas.AlignSelection(CanvasAlign.Left));

        Assert.All(canvas.Graph.Nodes, node => Assert.Equal(40, node.X));
    });

    /// <summary>
    /// A node that is not selected is not touched, which is the difference between an align
    /// command and a layout pass.
    /// </summary>
    [Fact]
    public void AnUnselectedNodeStaysWhereItIs() => WithCanvas((window, canvas) =>
    {
        ClickNode(window, canvas, 0);
        ClickNode(window, canvas, 1, RawInputModifiers.Control);
        Assert.Equal(2, canvas.Selection.Count);

        double untouched = canvas.Graph.Nodes[2].X;

        Assert.True(canvas.AlignSelection(CanvasAlign.Left));

        Assert.Equal(untouched, canvas.Graph.Nodes[2].X);
    });

    /// <summary>
    /// A move is a document edit that changes no value, so it must not start a run. Aligning is
    /// the same claim: a position is not in a node's provenance.
    /// </summary>
    [Fact]
    public void AligningIsAnEditThatDoesNotRequireARun() => WithCanvas((window, canvas) =>
    {
        List<GraphEditedEventArgs> edits = [];
        canvas.GraphChanged += (_, e) => edits.Add(e);

        SelectAll(window, canvas);
        canvas.AlignSelection(CanvasAlign.Top);

        GraphEditedEventArgs edit = Assert.Single(edits);
        Assert.False(edit.AffectsEvaluation);
        Assert.Equal("Align top", edit.Label);
    });

    /// <summary>
    /// <b>The undo-stack guard.</b> Aligning an already-aligned set is what a user does to check
    /// it is aligned, and it must not leave a step behind whose undo moves nothing — the same
    /// lesson the drag gesture learned as N19.
    /// </summary>
    [Fact]
    public void AligningTwiceRecordsOneEdit() => WithCanvas((window, canvas) =>
    {
        int edits = 0;
        canvas.GraphChanged += (_, _) => edits++;

        SelectAll(window, canvas);

        Assert.True(canvas.AlignSelection(CanvasAlign.Left));
        Assert.False(canvas.AlignSelection(CanvasAlign.Left));

        Assert.Equal(1, edits);
    });

    /// <summary>
    /// The spatial index is rebuilt inside <c>Render</c>, so a node moved by a command rather than
    /// by a gesture has to tell the index itself. If it does not, the node stays clickable where it
    /// used to be — which is the failure this repository has already met once from the other
    /// direction.
    /// </summary>
    [Fact]
    public void AnAlignedNodeIsHitTestableWhereItNowIs() => WithCanvas((window, canvas) =>
    {
        SelectAll(window, canvas);
        canvas.AlignSelection(CanvasAlign.Left);
        canvas.SelectOnly(-1);
        Assert.Empty(canvas.Selection);

        // Click where node 1 now is, which is 220 units left of where it was placed.
        ClickNode(window, canvas, 1);

        Assert.Equal([1], canvas.Selection);
    });

    [Fact]
    public void ASelectionOfOneCannotBeAlignedAndTwoCannotBeDistributed() =>
        WithCanvas((window, canvas) =>
        {
            ClickNode(window, canvas, 0);

            Assert.False(canvas.CanAlignSelection(CanvasAlign.Left));
            Assert.False(canvas.AlignSelection(CanvasAlign.Left));

            ClickNode(window, canvas, 1, RawInputModifiers.Control);

            Assert.True(canvas.CanAlignSelection(CanvasAlign.Left));
            Assert.False(canvas.CanAlignSelection(CanvasAlign.DistributeHorizontally));
        });

    [Fact]
    public void DistributingEqualisesTheGapsBetweenTheSelectedNodes() =>
        WithCanvas((window, canvas) =>
        {
            SelectAll(window, canvas);

            Assert.True(canvas.AlignSelection(CanvasAlign.DistributeHorizontally));

            List<(double Start, double End)> run = [.. canvas.Graph.Nodes
                .Select(n => (Start: n.X, End: n.X + n.Width))
                .OrderBy(r => r.Start)];

            Assert.Equal(run[0].End - run[1].Start, run[1].End - run[2].Start, 9);
        });

    /// <summary>
    /// Three nodes at three different x and three different y, none of them already lined up on
    /// either axis, so no assertion above can pass by accident.
    /// </summary>
    private static CanvasGraph ThreeScatteredNodes()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 40, 10);
        graph.Add(TestGraphs.Library.ByName("Math.Sin"), 260, 120);
        graph.Add(TestGraphs.Library.ByName("Math.Cos"), 520, 60);
        return graph;
    }

    private static void SelectAll(Window window, GraphCanvas canvas)
    {
        for (int slot = 0; slot < canvas.Graph.Nodes.Count; slot++)
        {
            ClickNode(
                window, canvas, slot, slot == 0 ? RawInputModifiers.None : RawInputModifiers.Control);
        }
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
    /// Opens three scattered nodes in a headless window, runs a body against them, and
    /// <b>closes the window again</b>.
    /// </summary>
    /// <remarks>
    /// The close is not tidiness. An alignment invalidates the visual, and a window still shown
    /// when the dispatch ends has that render job drained during the session's teardown — by which
    /// point the font manager is disposed, and the test fails with an
    /// <c>ObjectDisposedException</c> thrown inside <c>DrawText</c> whose stack names nothing in
    /// this file. Closing the window retires the pending frame with it.
    /// </remarks>
    private static void WithCanvas(Action<Window, GraphCanvas> body) => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = ThreeScatteredNodes() };
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

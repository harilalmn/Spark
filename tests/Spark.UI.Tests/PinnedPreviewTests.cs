using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T72` — Dynamo's preview bubble: a collapsed strip, a toggle, and a pin that keeps it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client with two screenshots of Dynamo.</b> The bubble used to be one thing
/// that appeared whole and vanished whole, so there were two states and nothing between them: a
/// value that went away the moment you looked elsewhere, or a <c>Watch</c> node wired into the
/// graph. The pin is the thing in between, and it changes no wire.
/// </para>
/// <para>
/// <b>The geometry is asserted without a window and the gestures with one.</b> Every rectangle
/// comes from <see cref="CanvasNode"/> rather than from measured text, which is the whole reason
/// the toggle and the pin are findable at all — a hit test that had to ask the last frame where
/// something was drawn is the defect this canvas already fixed once.
/// </para>
/// </remarks>
public sealed class PinnedPreviewTests
{
    /// <summary>A node that has run and has something to preview.</summary>
    private static (CanvasGraph Graph, int Slot) Ran(string value = "#5AC8FFFF")
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Colour.FromRgb"), 0, 0);

        graph.Nodes[slot].ResultSummary = value;

        return (graph, slot);
    }

    /// <summary>A node that has not run has no bubble at all, open or closed.</summary>
    [Fact]
    public void ANodeThatHasNotRunHasNoBubble()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Colour.FromRgb"), 0, 0);

        Assert.False(graph.Nodes[slot].HasPreview);
    }

    /// <summary>
    /// <b>The bubble is the node's own width, under it.</b> That is what makes every target inside
    /// it arithmetic rather than a measurement only the renderer knows.
    /// </summary>
    [Fact]
    public void TheBubbleIsTheNodesWidthUnderTheNode()
    {
        (CanvasGraph graph, int slot) = Ran();
        CanvasNode node = graph.Nodes[slot];

        node.PreviewBox(out double x, out double y, out double width, out double height);

        Assert.Equal(node.X, x, 6);
        Assert.Equal(node.Y + node.Height + CanvasNode.PreviewGap, y, 6);
        Assert.Equal(node.Width, width, 6);
        Assert.Equal(CanvasNode.PreviewRowHeight, height, 6);
    }

    /// <summary>Opening it makes it taller and nothing else: it stays where it was and as wide.</summary>
    [Fact]
    public void OpeningItOnlyMakesItTaller()
    {
        (CanvasGraph graph, int slot) = Ran();
        CanvasNode node = graph.Nodes[slot];

        node.PreviewBox(out double x, out double y, out double width, out double closed);

        node.PreviewExpanded = true;
        node.PreviewBox(out double openX, out double openY, out double openWidth, out double open);

        Assert.Equal(x, openX, 6);
        Assert.Equal(y, openY, 6);
        Assert.Equal(width, openWidth, 6);
        Assert.True(open > closed, $"opening it did not make it taller: {closed} to {open}");
    }

    /// <summary>
    /// <b>The two controls are inside the strip and clear of each other.</b> Two 16 px targets
    /// eight pixels apart in a 20 px row is the whole of the layout, and getting either of the
    /// two offsets wrong puts one of them under the other or outside the bubble.
    /// </summary>
    [Fact]
    public void TheToggleAndThePinAreInsideTheStripAndClearOfEachOther()
    {
        (CanvasGraph graph, int slot) = Ran();
        CanvasNode node = graph.Nodes[slot];
        node.PreviewExpanded = true;

        node.PreviewBox(out double boxX, out double boxY, out double boxWidth, out _);
        node.PreviewToggleBox(out double tx, out double ty, out double tw, out double th);
        node.PreviewPinBox(out double px, out double py, out double pw, out _);

        Assert.True(tx + tw <= boxX + boxWidth, "the toggle runs off the right of the bubble");
        Assert.True(ty >= boxY, "the toggle is above the bubble");
        Assert.True(ty + th <= boxY + CanvasNode.PreviewRowHeight, "the toggle is below the strip");
        Assert.True(px + pw <= tx, $"the pin overlaps the toggle: {px + pw} > {tx}");
        Assert.True(px > boxX, "the pin is off the left of the bubble");
        Assert.Equal(ty, py, 6);
    }

    /// <summary>A point on each control is reported as that control, and the rest as the body.</summary>
    [Fact]
    public void EachPartOfTheBubbleIsHitAsItself() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();
        GraphCanvas canvas = new() { Graph = graph };
        canvas.SelectOnly(slot);

        CanvasNode node = graph.Nodes[slot];
        node.PreviewExpanded = true;

        Assert.Equal(CanvasPreviewPart.Toggle, canvas.HitTestPreview(Center(node, Part.Toggle), out int hit));
        Assert.Equal(slot, hit);

        Assert.Equal(CanvasPreviewPart.Pin, canvas.HitTestPreview(Center(node, Part.Pin), out _));
        Assert.Equal(CanvasPreviewPart.Body, canvas.HitTestPreview(Center(node, Part.Body), out _));

        // Above the bubble is the node, and the node is not the bubble.
        Assert.Equal(
            CanvasPreviewPart.None,
            canvas.HitTestPreview(new Point(node.X + 10, node.Y + 4), out int missed));

        Assert.Equal(-1, missed);
    });

    /// <summary>
    /// <b>The pin is not a target while the bubble is closed</b>, because it is not drawn there.
    /// A control you cannot see and can still press is worse than no control.
    /// </summary>
    [Fact]
    public void ThePinIsNotHitWhileTheBubbleIsClosed() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();
        GraphCanvas canvas = new() { Graph = graph };
        canvas.SelectOnly(slot);

        Assert.Equal(
            CanvasPreviewPart.Body,
            canvas.HitTestPreview(Center(graph.Nodes[slot], Part.Pin), out _));
    });

    /// <summary>Clicking the toggle opens it, and clicking it again closes it.</summary>
    [Fact]
    public void ClickingTheToggleOpensAndClosesIt() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();
        canvas.SelectOnly(slot);

        CanvasNode node = graph.Nodes[slot];

        Click(window, canvas, Center(node, Part.Toggle));
        Assert.True(node.PreviewExpanded, "the toggle did not open the bubble");

        Click(window, canvas, Center(node, Part.Toggle));
        Assert.False(node.PreviewExpanded, "the toggle did not close the bubble");

        window.Close();
    });

    /// <summary>
    /// <b>The claim the client asked for.</b> Pin an open bubble, deselect the node, and the
    /// bubble is still there — where an unpinned one is not.
    /// </summary>
    [Fact]
    public void APinnedBubbleSurvivesDeselectionAndAnUnpinnedOneDoesNot() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();
        canvas.SelectOnly(slot);

        CanvasNode node = graph.Nodes[slot];

        Click(window, canvas, Center(node, Part.Toggle));

        // Selected and open, but not yet pinned: deselecting takes it away.
        canvas.SelectOnly(-1);
        Assert.False(canvas.ShowsPreview(slot), "an unpinned bubble survived deselection");

        canvas.SelectOnly(slot);
        Click(window, canvas, Center(node, Part.Pin));

        Assert.True(node.PreviewPinned, "the pin did not take");

        canvas.SelectOnly(-1);
        Assert.True(canvas.ShowsPreview(slot), "a pinned bubble did not survive deselection");
        Assert.True(node.PreviewExpanded, "a pinned bubble closed itself");

        window.Close();
    });

    /// <summary>
    /// <b>Closing a pinned bubble unpins it.</b> A pin keeps an *open* bubble, so a pinned closed
    /// one would be invisible state making the node behave differently for no visible reason.
    /// </summary>
    [Fact]
    public void ClosingAPinnedBubbleUnpinsIt()
    {
        (CanvasGraph graph, int slot) = Ran();
        GraphCanvas canvas = new() { Graph = graph };

        canvas.PinPreview(slot);

        Assert.True(graph.Nodes[slot].PreviewExpanded, "pinning did not open it");
        Assert.True(graph.Nodes[slot].PreviewPinned);

        canvas.TogglePreview(slot);

        Assert.False(graph.Nodes[slot].PreviewExpanded);
        Assert.False(graph.Nodes[slot].PreviewPinned, "closing it left it pinned");
    }

    /// <summary>
    /// <b>A press inside the bubble is swallowed even where it does nothing.</b> Falling through
    /// would start a marquee across the graph behind it and clear the selection the bubble belongs
    /// to.
    /// </summary>
    [Fact]
    public void APressOnTheBubbleBodyDoesNotClearTheSelection() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();
        canvas.SelectOnly(slot);

        Click(window, canvas, Center(graph.Nodes[slot], Part.Body));

        Assert.True(canvas.ShowsPreview(slot), "pressing the bubble cleared the selection under it");

        window.Close();
    });

    /// <summary>
    /// <b>The hover survives the pointer leaving the node for its own bubble.</b> Without it the
    /// strip disappears a frame before the click arrives, and the toggle is unreachable on any
    /// node that is not also selected.
    /// </summary>
    [Fact]
    public void MovingOntoTheBubbleKeepsTheNodeHovered() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        CanvasNode node = graph.Nodes[slot];

        // Onto the node first, which is what makes it the hovered one.
        window.MouseMove(Screen(canvas, new Point(node.X + 20, node.Y + 6)), RawInputModifiers.None);
        Assert.True(canvas.ShowsPreview(slot), "hovering the node showed no bubble");

        // Then down onto its strip, which is outside the node entirely.
        window.MouseMove(Screen(canvas, Center(node, Part.Body)), RawInputModifiers.None);
        Assert.True(canvas.ShowsPreview(slot), "moving onto the bubble took it away");

        // And away from both, which does take it away.
        window.MouseMove(Screen(canvas, new Point(node.X + 20, node.Y - 60)), RawInputModifiers.None);
        Assert.False(canvas.ShowsPreview(slot), "the bubble stayed after the pointer left");

        window.Close();
    });

    /// <summary>
    /// `E8-T73` — <b>the hover survives the six units between the node and its bubble.</b>
    /// </summary>
    /// <remarks>
    /// <b>The defect the client found on the first try:</b> <i>pane disappears while moving the
    /// mouse down to pin it.</i> `E8-T72` kept the hover over the bubble and not over the
    /// <see cref="CanvasNode.PreviewGap"/> above it — which is exactly the ground a pointer crosses
    /// on its way from the node to the toggle, so the bubble went away before the pointer arrived.
    /// The three points below are the journey, and the middle one is the one that failed.
    /// </remarks>
    [Fact]
    public void TheHoverSurvivesTheGapBetweenTheNodeAndItsBubble() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        CanvasNode node = graph.Nodes[slot];

        window.MouseMove(Screen(canvas, new Point(node.X + 20, node.Y + 6)), RawInputModifiers.None);
        Assert.True(canvas.ShowsPreview(slot), "hovering the node showed no bubble");

        // Halfway down the gap: not on the node any more, not on the bubble yet.
        window.MouseMove(
            Screen(canvas, new Point(node.X + 20, node.Y + node.Height + (CanvasNode.PreviewGap / 2))),
            RawInputModifiers.None);

        Assert.True(canvas.ShowsPreview(slot), "the bubble went away in the gap above itself");

        window.MouseMove(Screen(canvas, Center(node, Part.Toggle)), RawInputModifiers.None);
        Assert.True(canvas.ShowsPreview(slot), "the bubble went away on its own toggle");

        window.Close();
    });

    /// <summary>
    /// <b>The reach is generous for the hover and not for the press.</b> Widening what a click
    /// lands on would swallow presses in the gap, which belong to the canvas.
    /// </summary>
    [Fact]
    public void TheGapIsNotPartOfTheBubbleForAPress() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Ran();
        GraphCanvas canvas = new() { Graph = graph };
        canvas.SelectOnly(slot);

        CanvasNode node = graph.Nodes[slot];
        Point gap = new(node.X + 20, node.Y + node.Height + (CanvasNode.PreviewGap / 2));

        Assert.True(node.IsInPreviewReach(gap.X, gap.Y), "the gap is not within reach");
        Assert.False(node.IsInPreview(gap.X, gap.Y), "the gap counts as the bubble");
        Assert.Equal(CanvasPreviewPart.None, canvas.HitTestPreview(gap, out _));
    });

    /// <summary>
    /// The strip says what came out of the node in one word. A node whose output declares no type
    /// falls back to its shape, because an empty strip answers nothing.
    /// </summary>
    [Fact]
    public void TheStripNamesTheTypeOrFallsBackToTheShape()
    {
        (CanvasGraph graph, int slot) = Ran();

        Assert.Equal("Rgba", graph.Nodes[slot].PreviewLabel);

        CanvasGraph other = new();
        int untyped = other.Add(TestGraphs.Library.ByName("Number.Range"), 0, 0);

        other.Nodes[untyped].ResultRank = 1;
        other.Nodes[untyped].ResultCount = 10;
        other.Nodes[untyped].ResultSummary = "[0, 1, 2]";

        Assert.Equal(CanvasGraph.RankLine(other.Nodes[untyped]), other.Nodes[untyped].PreviewLabel);
    }

    /// <summary>
    /// A value long enough to wrap makes the bubble taller, and never taller than the cap — a
    /// bubble that grew with a list of a thousand points would cover the graph it annotates.
    /// </summary>
    [Fact]
    public void TheValueWrapsAndTheHeightIsCapped()
    {
        (CanvasGraph one, int shortSlot) = Ran("42");
        (CanvasGraph many, int longSlot) = Ran(new string('x', 20_000));

        Assert.Equal(1, one.Nodes[shortSlot].PreviewValueLines);
        Assert.Equal(CanvasNode.PreviewMaximumLines, many.Nodes[longSlot].PreviewValueLines);
    }

    private enum Part
    {
        Toggle,
        Pin,
        Body,
    }

    private static Point Center(CanvasNode node, Part part)
    {
        switch (part)
        {
            case Part.Toggle:
                node.PreviewToggleBox(out double tx, out double ty, out double tw, out double th);
                return new Point(tx + (tw / 2), ty + (th / 2));

            case Part.Pin:
                node.PreviewPinBox(out double px, out double py, out double pw, out double ph);
                return new Point(px + (pw / 2), py + (ph / 2));

            default:
                node.PreviewBox(out double bx, out double by, out _, out _);
                return new Point(bx + 6, by + (CanvasNode.PreviewRowHeight / 2));
        }
    }

    private static Point Screen(GraphCanvas canvas, Point world) =>
        new(canvas.Transform.ToScreenX(world.X), canvas.Transform.ToScreenY(world.Y));

    private static void Click(Window window, GraphCanvas canvas, Point world)
    {
        Point screen = Screen(canvas, world);

        window.MouseDown(screen, MouseButton.Left);
        window.MouseUp(screen, MouseButton.Left);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.Engine;
using Spark.Scripting;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T39` — the code block's source on the node, and the editor that opens over it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The canvas hosts no controls, so this is two halves.</b> Every block on the canvas is a
/// <i>drawing</i> of its source, which is what keeps a graph of a hundred of them the same cost as
/// a graph of a hundred anything else ([ADR-0013](../../docs/adr/0013-immediate-mode-node-canvas.md));
/// the one being typed into gets a real editor put over that drawing by the pane above. What is
/// tested here is the geometry both halves share and the gesture that asks for the second — the
/// editor itself is `E6-T11`'s and already has its own tests.
/// </para>
/// <para>
/// The gesture tests live here rather than on the pane for the reason
/// <see cref="CanvasWidgetGestureTests"/> records: a pane containing a wrapping
/// <c>TextBlock</c> cannot be shown in the headless session at all ([N90](NOTES.md)), and the
/// canvas draws its own text and shows normally.
/// </para>
/// </remarks>
public sealed class CodeBlockOnCanvasTests
{
    private const string TwoLines = "var doubled = a * 2;\nvar tripled = a * 3;\n";

    /// <summary>
    /// <b>The source travels onto the canvas node.</b> The renderer never calls an engine API, so a
    /// block that did not carry its own text could not be drawn without breaking that rule.
    /// </summary>
    [Fact]
    public void ACodeBlockCarriesItsSourceOntoTheCanvas()
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        Assert.Equal(TwoLines, graph.Nodes[slot].Script);
    }

    /// <summary>
    /// A trailing newline is not a line anybody typed on, and counting it would leave a blank row
    /// at the bottom of every block — every starter script ends in one.
    /// </summary>
    [Fact]
    public void ATrailingNewlineIsNotALine()
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        Assert.Equal(2, graph.Nodes[slot].ScriptLineCount);
    }

    /// <summary>An ordinary node has no source, and nothing on it is drawn as though it had.</summary>
    [Fact]
    public void AnOrdinaryNodeHasNoSource()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        Assert.Null(graph.Nodes[slot].Script);
    }

    /// <summary>
    /// <b>The source box is clear of both columns of port tabs.</b> The node is wide enough for the
    /// text <i>and</i> the tabs, which is what stops a block with an eleven-character port name
    /// drawing its code underneath the lozenge.
    /// </summary>
    [Fact]
    public void TheSourceBoxIsClearOfBothPortTabs()
    {
        (CanvasGraph graph, int slot) = Block("var perimeter = radius * 6;\nvar circumference = radius * 7;\n");

        CanvasNode node = graph.Nodes[slot];

        node.ScriptBox(out double x, out double y, out double width, out double height);
        node.PortTab(0, isOutput: false, out _, out _, out double inputRight, out _);
        node.PortTab(0, isOutput: true, out double outputLeft, out _, out _, out _);

        Assert.True(x >= inputRight, $"the source starts at {x} and the input tab ends at {inputRight}");
        Assert.True(
            x + width <= outputLeft,
            $"the source ends at {x + width} and the output tab starts at {outputLeft}");

        Assert.True(width > 0);
        Assert.True(y >= node.Y + CanvasNode.HeaderHeight);
        Assert.True(y + height <= node.Y + node.Height);
    }

    /// <summary>
    /// <b>A block is as tall as its source when its source is the taller half.</b> Ports and lines
    /// share the band below the header rather than stacking, which is what makes a block whose every
    /// line is a port come out the height of its ports.
    /// </summary>
    [Fact]
    public void ABlockIsTallEnoughForItsSource()
    {
        (CanvasGraph few, int fewSlot) = Block("var only = a * 2;\n");
        (CanvasGraph many, int manySlot) = Block(
            "var a1 = 1.0;\nvar a2 = 2.0;\nvar a3 = 3.0;\nvar a4 = 4.0;\nvar a5 = 5.0;\n"
            + "var a6 = 6.0;\nvar a7 = 7.0;\nvar a8 = 8.0;\nvar a9 = 9.0;\nvar a10 = 10.0;\n"
            + "return a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10;\n");

        // The second block has *one* output port and eleven lines, so its height can only have come
        // from the source - which is the case that would fail if the two were not compared.
        Assert.Equal("result", Assert.Single(many.Nodes[manySlot].Outputs).Name);
        Assert.True(
            many.Nodes[manySlot].Height > few.Nodes[fewSlot].Height,
            $"eleven lines came out {many.Nodes[manySlot].Height} and one came out {few.Nodes[fewSlot].Height}");
    }

    /// <summary>
    /// <b>Double-clicking a code block asks for an editor over its source.</b> The gesture, through
    /// the control, with real pointer events — the canvas is immediate-mode, so nothing in the
    /// framework routes a click to a node and the routing is ours to prove.
    /// </summary>
    [Fact]
    public void DoubleClickingACodeBlockAsksForAnEditorOverItsSource() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        CanvasFieldEditEventArgs? asked = null;
        canvas.ScriptEditRequested += (_, e) => asked = e;

        graph.Nodes[slot].ScriptBox(out double x, out double y, out double width, out double height);

        Point center = new(
            canvas.Transform.ToScreenX(x + (width / 2)),
            canvas.Transform.ToScreenY(y + (height / 2)));

        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        Assert.NotNull(asked);
        Assert.Equal(slot, asked!.Slot);
        Assert.Equal(TwoLines, asked.Text);

        // The rectangle is the one the source was drawn in, through the pan and the zoom — so the
        // editor opens over the words rather than near them.
        Assert.Equal(canvas.Transform.ToScreenX(x), asked.ScreenX, 3);
        Assert.Equal(canvas.Transform.ToScreenY(y), asked.ScreenY, 3);

        window.Close();
    });

    /// <summary>
    /// And double-clicking an ordinary node asks for nothing, rather than opening an editor over a
    /// node that has no source.
    /// </summary>
    [Fact]
    public void DoubleClickingAnOrdinaryNodeAsksForNothing() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        bool asked = false;
        canvas.ScriptEditRequested += (_, _) => asked = true;

        CanvasNode node = graph.Nodes[slot];

        Point center = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + CanvasNode.HeaderHeight + 4));

        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        Assert.False(asked, "an ordinary node was offered a code editor");

        window.Close();
    });

    /// <summary>A graph holding one code block, with the source given.</summary>
    private static (CanvasGraph Graph, int Slot) Block(string source)
    {
        ScriptNodeFactory scripts = new();
        CanvasGraph graph = new() { Scripts = scripts };

        return (graph, graph.Add(NodeDefinition.FromScript(scripts.Create(source), source), 0, 0));
    }

    /// <summary>
    /// <b>`E8-T40`: making room for the editor keeps it clear of the port tabs.</b> The reported
    /// defect, and the reason it is a reservation rather than a floor on the editor's size — the
    /// editor is drawn at a fixed legible size while the source rectangle is scaled by the zoom, so
    /// on a short block the second is smaller than the first and the difference used to land on the
    /// lozenges either side.
    /// </summary>
    [Fact]
    public void MakingRoomForTheEditorKeepsItClearOfThePortTabs() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block("var a1 = 1.0;\nvar a2 = 2.0;\n");

        GraphCanvas canvas = new() { Graph = graph };
        CanvasNode node = graph.Nodes[slot];

        double before = node.Width;

        Assert.True(
            canvas.ScriptEditorSpace(slot, 400, 200, out double x, out double y, out double width, out double height),
            "a code block was not given room");

        Assert.True(width >= 400, $"asked for 400 and was given {width}");
        Assert.True(height >= 200, $"asked for 200 and was given {height}");
        Assert.True(node.Width > before, $"the block did not grow: {before} to {node.Width}");

        // The tabs are outside the rectangle the editor is placed in, which is the whole of the
        // fix - at zoom 1 the screen rectangle is the world one.
        node.PortTab(0, isOutput: true, out double outputLeft, out _, out _, out _);

        Assert.True(
            x + width <= outputLeft,
            $"the editor ends at {x + width} and the output tab starts at {outputLeft}");

        Assert.True(y + height <= node.Y + node.Height);
    });

    /// <summary>
    /// <b>And the block goes back to its own size when the editor closes.</b> The reservation is
    /// not a resize: a node's width is derived from its content every time it is measured, so
    /// nothing here can reach the file — but a reservation left behind would leave a block sitting
    /// at editor width for the rest of the session.
    /// </summary>
    [Fact]
    public void ClosingTheEditorGivesTheRoomBack() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block("var a1 = 1.0;\nvar a2 = 2.0;\n");

        GraphCanvas canvas = new() { Graph = graph };

        double before = graph.Nodes[slot].Width;
        double heightBefore = graph.Nodes[slot].Height;

        canvas.ScriptEditorSpace(slot, 400, 200, out _, out _, out _, out _);
        canvas.EndScriptEdit(slot);

        Assert.Equal(before, graph.Nodes[slot].Width);
        Assert.Equal(heightBefore, graph.Nodes[slot].Height);
    });

    /// <summary>
    /// <b>The room asked for is in screen pixels and the reservation is in world units.</b> That
    /// division by the zoom is what makes zooming out grow the block rather than shrink the editor
    /// into it — the editor is the one thing on the canvas that is not scaled, because 6 px text is
    /// not text anybody can edit.
    /// </summary>
    [Fact]
    public void TheRoomIsAskedForInScreenPixelsWhateverTheZoom() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block("var a1 = 1.0;\n");

        GraphCanvas canvas = new() { Graph = graph };
        canvas.Transform.Zoom = 0.5;

        Assert.True(canvas.ScriptEditorSpace(slot, 400, 100, out _, out _, out double width, out _));

        // Half a world unit per pixel, so the block had to grow twice as far in world units to
        // answer with the same number of pixels.
        Assert.True(width >= 400, $"asked for 400 screen pixels at 50% and was given {width}");
    });

    /// <summary>An ordinary node has no source, so there is nothing to make room for.</summary>
    [Fact]
    public void AnOrdinaryNodeIsGivenNoRoom() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        double before = graph.Nodes[slot].Width;

        Assert.False(canvas.ScriptEditorSpace(slot, 400, 200, out _, out _, out _, out _));
        Assert.Equal(before, graph.Nodes[slot].Width);
    });

    /// <summary>
    /// <b>`E8-T43`: the wheel moves the view, and the view says so.</b> Everything the canvas draws
    /// follows the pan and the zoom for free; the one real control the hybrid overlay holds does
    /// not, and it can only follow if it is told.
    /// </summary>
    [Fact]
    public void MovingTheViewIsAnnounced() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        int moves = 0;
        canvas.ContentMoved += (_, _) => moves++;

        window.MouseWheel(new Point(400, 300), new Vector(0, 1));

        Assert.True(moves > 0, "zooming the canvas announced nothing");
        Assert.True(slot >= 0);

        window.Close();
    });

    /// <summary>
    /// <b>And the editor's rectangle comes back the same size at a different zoom.</b> The
    /// reservation is the screen size divided by the zoom, so asking again after the view moves is
    /// what keeps the editor legible and constant while the block grows around it — which is what
    /// lets it follow instead of closing.
    /// </summary>
    [Fact]
    public void TheEditorsRectangleIsTheSameSizeAtEveryZoom() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };

        Assert.True(canvas.ScriptEditorSpace(slot, 400, 200, out _, out _, out double wide, out double tall));

        double worldWidth = graph.Nodes[slot].Width;

        canvas.Transform.Zoom = 0.5;

        Assert.True(canvas.ScriptEditorSpace(slot, 400, 200, out _, out _, out double zoomedWide, out double zoomedTall));

        // The same rectangle on screen at half the zoom, which can only be true if the block took
        // twice the world units to hold it.
        Assert.Equal(wide, zoomedWide, 3);
        Assert.Equal(tall, zoomedTall, 3);
        Assert.True(
            graph.Nodes[slot].Width > worldWidth,
            $"the block should have grown in world units: {worldWidth} to {graph.Nodes[slot].Width}");
    });

    /// <summary>
    /// <b>`E8-T52`: dragging the block moves it out from under the editor, so dragging the block
    /// has to be announced too.</b> Found by the client, who dragged a block by its title with the
    /// editor open and watched the block leave without it.
    /// </summary>
    /// <remarks>
    /// The canvas redrew and told nobody: the pan and the wheel went through the announcing funnel
    /// and the two node drags each called <c>InvalidateVisual</c> directly. That is why the event
    /// is named for <i>a node moved</i> rather than for <i>the view moved</i> — the overlay cannot
    /// tell the two apart and does not need to.
    /// </remarks>
    [Fact]
    public void DraggingABlockIsAnnounced() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        int moves = 0;
        canvas.ContentMoved += (_, _) => moves++;

        // The title bar, which is what the client dragged and the one part of a block that is not
        // its source.
        CanvasNode node = graph.Nodes[slot];
        Point title = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + (CanvasNode.HeaderHeight / 2)));

        window.MouseDown(title, MouseButton.Left);
        window.MouseMove(title + new Vector(120, 60), RawInputModifiers.LeftMouseButton);
        window.MouseUp(title + new Vector(120, 60), MouseButton.Left);

        Assert.True(moves > 0, "dragging a code block announced nothing, so an open editor stays put");

        window.Close();
    });

    /// <summary>
    /// And the announcement is worth making: the rectangle the editor is placed in really has
    /// moved with the block, by the distance the pointer travelled.
    /// </summary>
    /// <remarks>
    /// The event alone would be satisfied by raising it and answering the same rectangle, which is
    /// the version of this fix that looks right and does nothing. This asks the canvas the question
    /// the pane asks it — <c>ScriptEditorSpace</c>, whose answer becomes <c>Canvas.Left</c> and
    /// <c>Canvas.Top</c> — before and after.
    /// </remarks>
    [Fact]
    public void TheEditorsRectangleFollowsADraggedBlock() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        Assert.True(canvas.ScriptEditorSpace(slot, 400, 120, out double x, out double y, out _, out _));

        CanvasNode node = graph.Nodes[slot];
        Point title = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + (CanvasNode.HeaderHeight / 2)));

        window.MouseDown(title, MouseButton.Left);
        window.MouseMove(title + new Vector(120, 60), RawInputModifiers.LeftMouseButton);
        window.MouseUp(title + new Vector(120, 60), MouseButton.Left);

        Assert.True(canvas.ScriptEditorSpace(slot, 400, 120, out double movedX, out double movedY, out _, out _));

        Assert.Equal(x + 120, movedX, 3);
        Assert.Equal(y + 60, movedY, 3);

        window.Close();
    });

    /// <summary>
    /// <b>`E8-T53`: one click on a code block opens its editor.</b> Asked for by the client, who
    /// found the double-click a keystroke too many on the node they type into most.
    /// </summary>
    [Fact]
    public void ASingleClickOnACodeBlockOpensTheEditor() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        CanvasFieldEditEventArgs? asked = null;
        canvas.ScriptEditRequested += (_, e) => asked = e;

        graph.Nodes[slot].ScriptBox(out double x, out double y, out double width, out double height);

        Point center = new(
            canvas.Transform.ToScreenX(x + (width / 2)),
            canvas.Transform.ToScreenY(y + (height / 2)));

        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        Assert.NotNull(asked);
        Assert.Equal(slot, asked!.Slot);
        Assert.Equal(TwoLines, asked.Text);

        window.Close();
    });

    /// <summary>
    /// <b>The gesture this could break, and the reason the rule is slop rather than "did it
    /// move".</b> A block is dragged by pressing on it, so a drag must open nothing — and a
    /// tremor of a pixel or two must still be a click, or the click-to-edit is a gesture users
    /// learn not to trust.
    /// </summary>
    [Fact]
    public void DraggingABlockOpensNothingButATremorStillClicks() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        int asked = 0;
        canvas.ScriptEditRequested += (_, _) => asked++;

        CanvasNode node = graph.Nodes[slot];
        Point title = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + (CanvasNode.HeaderHeight / 2)));

        // A SLOW DRAG, IN STEPS SMALLER THAN THE SLOP, AND THAT IS THE WHOLE POINT.
        //
        // Sixty steps of two pixels is a hand moving a node carefully, and it is the only shape of
        // drag that can tell a correct implementation from one measuring the slop against
        // `_dragStartWorld` — which a node drag advances on every move, so it holds the *previous*
        // position and each step reads as two pixels for ever. Under that mistake this drag is
        // sixty consecutive clicks. A drag that jumps 120 pixels in one move passes either way.
        window.MouseDown(title, MouseButton.Left);

        for (int step = 1; step <= 60; step++)
        {
            window.MouseMove(title + new Vector(step * 2, step), RawInputModifiers.LeftMouseButton);
        }

        window.MouseUp(title + new Vector(120, 60), MouseButton.Left);

        Assert.Equal(0, asked);

        // Two pixels, which is inside the slop: a hand that shakes has still clicked.
        Point again = new(
            canvas.Transform.ToScreenX(graph.Nodes[slot].X + (graph.Nodes[slot].Width / 2)),
            canvas.Transform.ToScreenY(graph.Nodes[slot].Y + (CanvasNode.HeaderHeight / 2)));

        window.MouseDown(again, MouseButton.Left);
        window.MouseMove(again + new Vector(2, 1), RawInputModifiers.LeftMouseButton);
        window.MouseUp(again + new Vector(2, 1), MouseButton.Left);

        Assert.Equal(1, asked);

        window.Close();
    });

    /// <summary>
    /// A click on an ordinary node opens nothing — the same claim the double-click test makes, and
    /// it has to be re-made because the gesture is now the one every node receives.
    /// </summary>
    [Fact]
    public void ASingleClickOnAnOrdinaryNodeOpensNothing() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        bool asked = false;
        canvas.ScriptEditRequested += (_, _) => asked = true;

        CanvasNode node = graph.Nodes[slot];
        Point center = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + CanvasNode.HeaderHeight + 4));

        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        Assert.False(asked, "an ordinary node was offered a code editor");

        window.Close();
    });

    /// <summary>
    /// <b>Control and Shift are about the selection, not about typing.</b> Control+click arms a
    /// copy (`E8-T37`) and Shift+click extends the selection; neither should put an editor over the
    /// block, which would take the keyboard away mid-selection.
    /// </summary>
    /// <remarks>
    /// <b>One modified click per test, and that is not fussiness.</b> The first version did both in
    /// one body, at one point, and failed — two clicks in the same place are a <i>double</i> click,
    /// which has opened the editor since `E8-T39` and pays no attention to modifiers. The test was
    /// measuring the double-click path while claiming to measure the single one.
    /// </remarks>
    [Theory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Shift)]
    public void AModifiedClickOpensNothing(RawInputModifiers held) => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        int asked = 0;
        canvas.ScriptEditRequested += (_, _) => asked++;

        graph.Nodes[slot].ScriptBox(out double x, out double y, out double width, out double height);

        Point center = new(
            canvas.Transform.ToScreenX(x + (width / 2)),
            canvas.Transform.ToScreenY(y + (height / 2)));

        window.MouseDown(center, MouseButton.Left, held);
        window.MouseUp(center, MouseButton.Left, held);

        Assert.Equal(0, asked);

        window.Close();
    });

    /// <summary>
    /// <b>`E8-T54`: deleting a block is a move, and it is the move that matters most.</b> Reported
    /// by the client, whose editor stayed on the canvas after the block under it was deleted — and
    /// cleared itself on the next zoom, which is the tell.
    /// </summary>
    /// <remarks>
    /// The pane already hides an editor whose block has gone; that check lives in the handler for
    /// this event, and the delete was the one node-moving site `E8-T52` did not route into the
    /// funnel. So the zoom worked and nothing else did.
    /// </remarks>
    [Fact]
    public void DeletingABlockIsAnnounced() => HeadlessSession.Run(() =>
    {
        (CanvasGraph graph, int slot) = Block(TwoLines);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        canvas.SelectOnly(slot);

        int moves = 0;
        canvas.ContentMoved += (_, _) => moves++;

        Assert.True(canvas.DeleteSelection());
        Assert.True(moves > 0, "deleting a block announced nothing, so its editor stays on the canvas");

        // And the pane's question now answers no, which is what hides it.
        Assert.False(canvas.ScriptEditorSpace(slot, 400, 120, out _, out _, out _, out _));

        window.Close();
    });

    /// <summary>
    /// <b>Why the open editor is held by identity and not by slot.</b> A slot is an index into an
    /// array that renumbers: delete a node and every node after it moves down one.
    /// </summary>
    /// <remarks>
    /// This is the hazard the second half of `E8-T54` exists for, and it is worse than the ghost
    /// editor because it is silent. An editor open on the second block while the first is deleted
    /// would have been left pointing at a slot that is now a *different* block — and if that block
    /// is also a code block, the placement succeeds, the editor keeps its text, and the commit
    /// lands on the wrong node. <c>CanvasGraph.SlotOf</c> is what the pane asks instead.
    /// </remarks>
    [Fact]
    public void DeletingALowerBlockRenumbersTheOnesAfterIt()
    {
        ScriptNodeFactory scripts = new();
        CanvasGraph graph = new() { Scripts = scripts };

        int first = graph.Add(NodeDefinition.FromScript(scripts.Create("1;"), "1;"), 0, 0);
        int second = graph.Add(NodeDefinition.FromScript(scripts.Create(TwoLines), TwoLines), 300, 0);

        CanvasNodeHandle edited = graph.HandleOf(second);

        Assert.Equal(second, graph.SlotOf(edited));

        graph.Remove(first);

        // The slot the editor opened on now names a different node - or none. The handle still
        // names the block, which is the whole reason the pane holds one.
        Assert.NotEqual(second, graph.SlotOf(edited));
        Assert.Equal(edited, graph.HandleOf(graph.SlotOf(edited)));

        // And a handle to a node that has gone answers -1 rather than a wrong slot.
        Assert.Equal(-1, graph.SlotOf(graph.HandleOf(99)));
    }
}

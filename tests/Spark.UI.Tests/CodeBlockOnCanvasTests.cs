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

        Point centre = new(
            canvas.Transform.ToScreenX(x + (width / 2)),
            canvas.Transform.ToScreenY(y + (height / 2)));

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

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

        Point centre = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + CanvasNode.HeaderHeight + 4));

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

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
        canvas.ViewChanged += (_, _) => moves++;

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
}

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
/// `E8-T68` — double-clicking a node's title edits it in place.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>let the user double click and edit the node title there
/// only. When entering edit mode, keep the entire title text selected.</i> Renaming lived in the
/// properties pane and nowhere else, which is a panel away from the thing being renamed.
/// </para>
/// <para>
/// <b>The gestures are tested through the control with real pointer events</b>, because the canvas
/// is immediate-mode: nothing in the framework routes a click to a node, so the routing is ours
/// and is exactly what could be wrong. The selection-on-open half belongs to the pane's
/// <c>TextBox</c> and is a property of Avalonia rather than of this repository; what is asserted
/// here is everything on the canvas side of that seam.
/// </para>
/// </remarks>
public sealed class NodeTitleEditTests
{
    private const string TwoLines = "var doubled = a * 2;\nvar tripled = a * 3;\n";

    /// <summary>The header is the node's own width, at its top, and exactly one band tall.</summary>
    [Fact]
    public void TheHeaderBoxIsTheBandTheTitleIsDrawnIn()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 40, 60);

        CanvasNode node = graph.Nodes[slot];
        node.HeaderBox(out double x, out double y, out double width, out double height);

        Assert.Equal(node.X, x, 6);
        Assert.Equal(node.Y, y, 6);
        Assert.Equal(node.Width, width, 6);
        Assert.Equal(CanvasNode.HeaderHeight, height, 6);

        Assert.True(node.IsInHeader(node.X + 4, node.Y + 4));
        Assert.False(node.IsInHeader(node.X + 4, node.Y + CanvasNode.HeaderHeight + 4));
        Assert.False(node.IsInHeader(node.X - 4, node.Y + 4));
    }

    /// <summary>
    /// <b>The gesture itself.</b> Double-clicking the header asks for an editor carrying the
    /// node's displayed title, over the rectangle the title was drawn in.
    /// </summary>
    [Fact]
    public void DoubleClickingATitleAsksForAnEditorOverIt() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        CanvasFieldEditEventArgs? asked = null;
        canvas.TitleEditRequested += (_, e) => asked = e;

        CanvasNode node = graph.Nodes[slot];
        Point title = TitlePoint(canvas, node);

        window.MouseDown(title, MouseButton.Left);
        window.MouseUp(title, MouseButton.Left);
        window.MouseDown(title, MouseButton.Left);
        window.MouseUp(title, MouseButton.Left);

        Assert.NotNull(asked);
        Assert.Equal(slot, asked!.Slot);
        Assert.Equal(node.DisplayTitle, asked.Text);

        node.HeaderBox(out double x, out double y, out double width, out double height);

        Assert.Equal(canvas.Transform.ToScreenX(x), asked.ScreenX, 3);
        Assert.Equal(canvas.Transform.ToScreenY(y), asked.ScreenY, 3);
        Assert.Equal(width * canvas.Transform.Zoom, asked.ScreenWidth, 3);
        Assert.Equal(height * canvas.Transform.Zoom, asked.ScreenHeight, 3);

        window.Close();
    });

    /// <summary>
    /// <b>Below the header it is the old gesture and nothing else.</b> The body of an ordinary node
    /// is not a rename target — a double-click there used to do nothing, and still does.
    /// </summary>
    [Fact]
    public void DoubleClickingTheBodyAsksForNothing() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        bool asked = false;
        canvas.TitleEditRequested += (_, _) => asked = true;

        CanvasNode node = graph.Nodes[slot];
        Point body = new(
            canvas.Transform.ToScreenX(node.X + (node.Width / 2)),
            canvas.Transform.ToScreenY(node.Y + CanvasNode.HeaderHeight + 6));

        window.MouseDown(body, MouseButton.Left);
        window.MouseUp(body, MouseButton.Left);
        window.MouseDown(body, MouseButton.Left);
        window.MouseUp(body, MouseButton.Left);

        Assert.False(asked, "the body of a node offered a rename");

        window.Close();
    });

    /// <summary>
    /// <b>A code block's header renames it rather than opening its source</b> (`E8-T68` narrowing
    /// `E8-T39` and `E8-T53`). A rule with an exception for one node kind is a rule nobody can
    /// learn, and the cost is the 22 px of a block that is not source.
    /// </summary>
    [Fact]
    public void ACodeBlocksHeaderRenamesRatherThanOpeningTheEditor() => HeadlessSession.Run(() =>
    {
        ScriptNodeFactory scripts = new();
        CanvasGraph graph = new() { Scripts = scripts };
        int slot = graph.Add(NodeDefinition.FromScript(scripts.Create(TwoLines), TwoLines), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        bool renamed = false;
        bool edited = false;

        canvas.TitleEditRequested += (_, _) => renamed = true;
        canvas.ScriptEditRequested += (_, _) => edited = true;

        Point title = TitlePoint(canvas, graph.Nodes[slot]);

        window.MouseDown(title, MouseButton.Left);
        window.MouseUp(title, MouseButton.Left);

        // The single click alone must open nothing, or the first half of the double-click would
        // put a source editor over the block before the second half could ask for a rename.
        Assert.False(edited, "a click on the header opened the source editor");

        window.MouseDown(title, MouseButton.Left);
        window.MouseUp(title, MouseButton.Left);

        Assert.True(renamed, "the header did not offer a rename");
        Assert.False(edited, "the header opened the source editor");

        window.Close();
    });

    /// <summary>
    /// <b>And a click anywhere else on a block still opens it</b>, which is all of a block but
    /// one band. `E8-T53` is narrowed here, not withdrawn.
    /// </summary>
    [Fact]
    public void ACodeBlocksBodyStillOpensOnASingleClick() => HeadlessSession.Run(() =>
    {
        ScriptNodeFactory scripts = new();
        CanvasGraph graph = new() { Scripts = scripts };
        int slot = graph.Add(NodeDefinition.FromScript(scripts.Create(TwoLines), TwoLines), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        bool edited = false;
        canvas.ScriptEditRequested += (_, _) => edited = true;

        graph.Nodes[slot].ScriptBox(out double x, out double y, out double width, out double height);

        Point source = new(
            canvas.Transform.ToScreenX(x + (width / 2)),
            canvas.Transform.ToScreenY(y + (height / 2)));

        window.MouseDown(source, MouseButton.Left);
        window.MouseUp(source, MouseButton.Left);

        Assert.True(edited, "a click on a block's source opened nothing");

        window.Close();
    });

    /// <summary>Committing a name puts it on the node and records exactly one undo step.</summary>
    [Fact]
    public void CommittingANameRenamesTheNodeOnce()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };

        int edits = 0;
        string? label = null;
        bool affects = true;

        canvas.GraphChanged += (_, e) =>
        {
            edits++;
            label = e.Label;
            affects = e.AffectsEvaluation;
        };

        canvas.CommitNodeTitle(slot, "radius");

        Assert.Equal("radius", graph.Nodes[slot].CustomTitle);
        Assert.Equal("radius", graph.Nodes[slot].DisplayTitle);
        Assert.Equal(1, edits);
        Assert.Equal("Rename node", label);

        // A name is a label. Renaming a node cannot change what the graph computes, so a run is
        // not owed - which is the same claim an alignment makes.
        Assert.False(affects, "a rename asked for a re-run");
    }

    /// <summary>
    /// <b>Committing the same name again is not an edit.</b> Opening the editor and pressing Enter
    /// is the commonest way to leave it, and an undo step that undoes nothing visible is the
    /// failure `CanvasLayout.Moves` exists to avoid.
    /// </summary>
    [Fact]
    public void CommittingAnUnchangedNameRecordsNothing()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };

        int edits = 0;
        canvas.GraphChanged += (_, _) => edits++;

        canvas.CommitNodeTitle(slot, graph.Nodes[slot].DisplayTitle);

        Assert.Null(graph.Nodes[slot].CustomTitle);
        Assert.Equal(0, edits);
    }

    /// <summary>
    /// <b>Typing the definition's own name back is a reset, not a custom title.</b> The two look
    /// identical and behave differently: a custom title is written into the document and survives
    /// the definition being renamed underneath it, and the user who typed it meant *put it back*.
    /// </summary>
    [Fact]
    public void TypingTheDefinitionsOwnNameBackClearsTheCustomTitle()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        CanvasNode node = graph.Nodes[slot];

        canvas.CommitNodeTitle(slot, "radius");
        Assert.Equal("radius", node.CustomTitle);

        string? label = null;
        canvas.GraphChanged += (_, e) => label = e.Label;

        canvas.CommitNodeTitle(slot, node.Title);

        Assert.Null(node.CustomTitle);
        Assert.Equal(node.Title, node.DisplayTitle);
        Assert.Equal("Reset node name", label);
    }

    /// <summary>Blank is a reset too, which is what clearing the box and leaving means.</summary>
    [Fact]
    public void ABlankNameClearsTheCustomTitle()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };

        canvas.CommitNodeTitle(slot, "radius");
        canvas.CommitNodeTitle(slot, "   ");

        Assert.Null(graph.Nodes[slot].CustomTitle);
    }

    /// <summary>
    /// <b>A longer name is a wider node</b> (`E8-T35`), and the rename has to say so — the spatial
    /// index the canvas hit-tests against was built from the old width.
    /// </summary>
    [Fact]
    public void ALongerNameWidensTheNode()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };

        double before = graph.Nodes[slot].Width;

        canvas.CommitNodeTitle(slot, "a name long enough to widen the node it is on");

        Assert.True(
            graph.Nodes[slot].Width > before,
            $"the node did not grow: {before} to {graph.Nodes[slot].Width}");
    }

    private static Point TitlePoint(GraphCanvas canvas, CanvasNode node)
    {
        node.HeaderBox(out double x, out double y, out double width, out double height);

        return new Point(
            canvas.Transform.ToScreenX(x + (width / 2)),
            canvas.Transform.ToScreenY(y + (height / 2)));
    }
}

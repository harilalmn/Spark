using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.Engine;
using Spark.Scripting;
using Spark.UI.Canvas;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Selection around a code block whose editor is open — `E8-T40`, reported by the client.
/// </summary>
/// <remarks>
/// <para>
/// <b>The report was "node selection has some issues when codeblocks are present", with two
/// screenshots.</b> The cause is that opening a block's in-place editor <i>reserves</i> room on
/// the node so it can host the editor — and the reservation went into the node's bounds, which is
/// what the spatial index is built from. A block 287×70 became 584×305, and the extra was
/// **invisible**: an empty stretch of canvas below and to the right of the block answered every
/// click with the block.
/// </para>
/// <para>
/// <b>The reservation is right; putting it in the hit rectangle was not.</b> The node has to grow
/// so the editor is not drawn over the port tabs either side, and while the editor is open it is a
/// real control sitting on top of the canvas — so clicks inside it never reach the canvas at all.
/// Extending the canvas's own target bought nothing and cost a phantom.
/// </para>
/// </remarks>
public sealed class CodeBlockSelectionTests
{
    /// <summary>
    /// <b>An open editor does not make the block's click target grow.</b> The assertion the report
    /// reduces to, written against the geometry rather than against a click so that a failure says
    /// which rectangle is wrong.
    /// </summary>
    [Fact]
    public void AnOpenEditorDoesNotEnlargeTheBlocksClickTarget() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int block, int slider) = Open();
        _ = window;
        _ = slider;

        CanvasBounds before = graph.Nodes[block].SelectionBounds;

        canvas.ScriptEditorSpace(block, 520, 260, out _, out _, out _, out _);

        CanvasBounds after = graph.Nodes[block].SelectionBounds;

        Assert.Equal(before.MinX, after.MinX, 1e-9);
        Assert.Equal(before.MinY, after.MinY, 1e-9);
        Assert.Equal(before.MaxX, after.MaxX, 1e-9);
        Assert.Equal(before.MaxY, after.MaxY, 1e-9);

        // And the node really did grow, or the test above is asserting nothing.
        Assert.True(
            graph.Nodes[block].Bounds.MaxY > after.MaxY,
            "the node should still grow to host the editor");
    });

    /// <summary>
    /// <b>The click the client actually made.</b> Empty canvas well clear of the block, with the
    /// block's editor open, must not select the block.
    /// </summary>
    [Fact]
    public void ClickingEmptyCanvasBesideAnOpenEditorSelectsNothing() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int block, int slider) = Open();

        // Measured BEFORE the editor opens, or the point moves with the phantom it is testing.
        CanvasNode node = graph.Nodes[block];
        double beyond = node.X + node.Width + 120;
        double below = node.Y + node.Height + 120;

        canvas.ScriptEditorSpace(block, 520, 260, out _, out _, out _, out _);

        // Clear of the block as drawn, and squarely inside what the reservation used to claim.
        Click(window, canvas, beyond, below);

        Assert.Empty(canvas.Selection);
        _ = slider;
    });

    /// <summary>
    /// And the block itself is still selectable, so the fix did not simply make it unclickable.
    /// </summary>
    [Fact]
    public void TheBlockIsStillSelectableWhileItsEditorIsOpen() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int block, int _) = Open();

        canvas.ScriptEditorSpace(block, 520, 260, out _, out _, out _, out _);

        CanvasNode node = graph.Nodes[block];
        Click(window, canvas, node.X + 20, node.Y + 6);

        Assert.Contains(block, canvas.Selection);
    });

    /// <summary>
    /// <b>A neighbour under the reservation keeps its own clicks.</b> This is the shape the report
    /// described: a node that sits where the phantom rectangle used to be.
    /// </summary>
    [Fact]
    public void ANeighbourUnderTheReservationIsStillSelectable() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int block, int slider) = Open();

        canvas.ScriptEditorSpace(block, 520, 260, out _, out _, out _, out _);

        CanvasNode neighbour = graph.Nodes[slider];
        Click(window, canvas, neighbour.X + (neighbour.Width / 2), neighbour.Y + 6);

        Assert.Contains(slider, canvas.Selection);
        Assert.DoesNotContain(block, canvas.Selection);
    });

    private static void Click(Window window, GraphCanvas canvas, double worldX, double worldY)
    {
        Point at = new(canvas.Transform.ToScreenX(worldX), canvas.Transform.ToScreenY(worldY));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    /// <summary>A canvas holding a code block with a slider inside the reservation's reach.</summary>
    private static (Window Window, GraphCanvas Canvas, CanvasGraph Graph, int Block, int Slider) Open()
    {
        CanvasGraph graph = new();

        // Placed where the reservation used to reach, which is the whole point of it being here.
        int slider = graph.Add(Library.ByName("Number.Slider"), 900, 220);

        ReferenceCatalog catalog = new();
        catalog.Add(
        [
            typeof(Spark.Geometry.Point3d).Assembly.Location,
            typeof(Spark.Api.SparkList).Assembly.Location,
            typeof(NodeKey).Assembly.Location,
            typeof(Spark.Nodes.Core.Number).Assembly.Location,
        ]);

        ScriptNodeFactory scripts = new(catalog);
        graph.Scripts = scripts;

        const string Source = "Point2d p = new Point2d(x, y);";
        int block = graph.Add(NodeDefinition.FromScript(scripts.Create(Source), Source), 760, 60);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 1400, Height = 900, Content = canvas };

        window.Show();

        return (window, canvas, graph, block, slider);
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Number).Assembly));

        return library;
    }
}

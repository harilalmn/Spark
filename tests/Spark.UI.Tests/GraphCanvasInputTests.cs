using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.UI.Controls;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Tests;

/// <summary>
/// The canvas's input routing, driven through a headless Avalonia window.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0013's negative consequence is that the canvas re-implements by hand what the framework
/// would otherwise provide: hit-testing, selection, focus and drag. None of that is exercised by a
/// compile, and a control that builds may still be unclickable — so every gesture the canvas claims
/// to support is performed here as a real sequence of pointer events.
/// </para>
/// <para>
/// What these tests do <b>not</b> cover is what the canvas looks like. Headless drawing is a stub,
/// so <c>Render</c> runs without producing pixels; that it draws the right thing was checked by
/// capturing the running application, not here.
/// </para>
/// <para>
/// The gestures run through <see cref="HeadlessUnitTestSession"/> directly rather than through
/// <c>Avalonia.Headless.XUnit</c>'s <c>[AvaloniaFact]</c>. That package's 12.1.1 build is compiled
/// against xunit.v3 3.2.2 and calls <c>TestIntrospectionHelper.GetTestCaseDetails</c> with a
/// signature that no longer exists in the 4.0.0 this repository pins, so every
/// <c>[AvaloniaFact]</c> fails at <i>discovery</i> with a <c>MissingMethodException</c>.
/// Dispatching onto the session by hand uses only <c>Avalonia.Headless</c>, which has no xunit
/// dependency at all, and costs one helper method.
/// </para>
/// </remarks>
public sealed class GraphCanvasInputTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));

    [Fact]
    public void ClickingANodeSelectsIt() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        Click(window, 60, 30);

        Assert.Equal([0], canvas.Selection);
        Assert.Equal(0, canvas.FocusedSlot);
    });

    [Fact]
    public void ClickingEmptyCanvasClearsTheSelection() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        Click(window, 60, 30);
        Assert.NotEmpty(canvas.Selection);

        Click(window, 700, 500);
        Assert.Empty(canvas.Selection);
    });

    [Fact]
    public void ShiftClickingExtendsTheSelectionAndClickingAgainRemovesIt() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        Click(window, 60, 30);
        Click(window, 360, 30, RawInputModifiers.Shift);
        Assert.Equal(2, canvas.Selection.Count);

        Click(window, 360, 30, RawInputModifiers.Shift);
        Assert.Equal([0], canvas.Selection);
    });

    [Fact]
    public void DraggingANodeMovesItAndTheIndexFollows() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        PlaceholderNode node = canvas.Graph.Nodes[0];

        window.MouseDown(new Point(60, 30), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(160, 80), RawInputModifiers.None);
        window.MouseUp(new Point(160, 80), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(100, node.X, 3);
        Assert.Equal(50, node.Y, 3);

        // The spatial index has to have followed, or the node becomes unclickable where it now is.
        Click(window, 160, 80);
        Assert.Equal([0], canvas.Selection);
    });

    [Fact]
    public void DraggingANodeMovesEveryMemberOfTheSelection() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        Click(window, 60, 30);
        Click(window, 360, 30, RawInputModifiers.Shift);

        double firstX = canvas.Graph.Nodes[0].X;
        double secondX = canvas.Graph.Nodes[1].X;

        window.MouseDown(new Point(360, 30), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(400, 30), RawInputModifiers.None);
        window.MouseUp(new Point(400, 30), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(firstX + 40, canvas.Graph.Nodes[0].X, 3);
        Assert.Equal(secondX + 40, canvas.Graph.Nodes[1].X, 3);
    });

    [Fact]
    public void AMarqueeOverEmptyCanvasSelectsEverythingItCovers() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        window.MouseDown(new Point(700, 500), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(20, 20), RawInputModifiers.None);
        window.MouseUp(new Point(20, 20), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(2, canvas.Selection.Count);
    });

    [Fact]
    public void MiddleDraggingPansTheViewAndMovesNoNode() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        double offsetX = canvas.Transform.OffsetX;

        window.MouseDown(new Point(300, 300), MouseButton.Middle, RawInputModifiers.None);
        window.MouseMove(new Point(200, 300), RawInputModifiers.None);
        window.MouseUp(new Point(200, 300), MouseButton.Middle, RawInputModifiers.None);

        // Pan is the canvas transform and nothing else — never per-node layout.
        Assert.Equal(offsetX + 100, canvas.Transform.OffsetX, 3);
        Assert.Equal(0, canvas.Graph.Nodes[0].X, 3);
    });

    [Fact]
    public void TheWheelZoomsAboutThePointerAndKeepsThatWorldPointFixed() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        Point anchor = new(500, 300);
        double worldXBefore = canvas.Transform.ToWorldX(anchor.X);
        double worldYBefore = canvas.Transform.ToWorldY(anchor.Y);
        double zoomBefore = canvas.Transform.Zoom;

        window.MouseWheel(anchor, new Vector(0, 1), RawInputModifiers.None);

        Assert.True(canvas.Transform.Zoom > zoomBefore);
        Assert.Equal(worldXBefore, canvas.Transform.ToWorldX(anchor.X), 6);
        Assert.Equal(worldYBefore, canvas.Transform.ToWorldY(anchor.Y), 6);
    });

    [Fact]
    public void DraggingFromAnOutputPortToAnInputPortCreatesAWire() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        canvas.Graph.Nodes[0].OutputPortCentre(0, out double fromX, out double fromY);
        canvas.Graph.Nodes[1].InputPortCentre(0, out double toX, out double toY);

        window.MouseDown(Screen(canvas, fromX, fromY), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Screen(canvas, (fromX + toX) / 2, (fromY + toY) / 2), RawInputModifiers.None);
        window.MouseMove(Screen(canvas, toX, toY), RawInputModifiers.None);
        window.MouseUp(Screen(canvas, toX, toY), MouseButton.Left, RawInputModifiers.None);

        Assert.Single(canvas.Graph.Wires);
        Assert.Equal(0, canvas.Graph.Wires[0].From.NodeIndex);
        Assert.True(canvas.Graph.Wires[0].From.IsOutput);
        Assert.Equal(1, canvas.Graph.Wires[0].To.NodeIndex);
        Assert.False(canvas.Graph.Wires[0].To.IsOutput);
    });

    [Fact]
    public void DraggingFromAPortToEmptyCanvasCreatesNothing() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        canvas.Graph.Nodes[0].OutputPortCentre(0, out double fromX, out double fromY);

        window.MouseDown(Screen(canvas, fromX, fromY), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(700, 520), RawInputModifiers.None);
        window.MouseUp(new Point(700, 520), MouseButton.Left, RawInputModifiers.None);

        Assert.Empty(canvas.Graph.Wires);

        // And the node must not have been dragged instead, which is what happens when the port hit
        // test loses to the node hit test.
        Assert.Equal(0, canvas.Graph.Nodes[0].X, 3);
    });

    [Fact]
    public void PressingAPortDoesNotAlsoSelectItsNode() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        canvas.Graph.Nodes[0].OutputPortCentre(0, out double x, out double y);

        Point port = Screen(canvas, x, y);
        window.MouseDown(port, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(port, MouseButton.Left, RawInputModifiers.None);

        Assert.Empty(canvas.Selection);
    });

    [Fact]
    public void HoveringAPortReportsIt() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        canvas.Graph.Nodes[1].InputPortCentre(0, out double x, out double y);

        window.MouseMove(Screen(canvas, x, y), RawInputModifiers.None);

        Assert.NotNull(canvas.HoveredPort);
        Assert.Equal(1, canvas.HoveredPort!.Value.NodeIndex);
        Assert.False(canvas.HoveredPort.Value.IsOutput);
    });

    [Fact]
    public void HomeFitsTheGraphAndEscapeClearsTheSelection() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(SampleGraphs.Demo());

        Click(window, 400, 300);
        canvas.Transform.Zoom = 4;
        canvas.Transform.OffsetX = 100_000;

        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);

        Assert.True(canvas.Transform.Zoom < 4);
        Assert.True(canvas.Transform.OffsetX < 100_000);

        window.MouseDown(new Point(20, 20), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(780, 560), RawInputModifiers.None);
        window.MouseUp(new Point(780, 560), MouseButton.Left, RawInputModifiers.None);
        Assert.NotEmpty(canvas.Selection);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Empty(canvas.Selection);
    });

    [Fact]
    public void TwoThousandNodesSurvivePanningAndZoomingThroughEveryDetailLevel() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(SampleGraphs.Synthetic(2000));
        canvas.ZoomToFit();

        for (int i = 0; i < 12; i++)
        {
            window.MouseWheel(new Point(400, 300), new Vector(0, i % 2 == 0 ? 1 : -1), RawInputModifiers.None);
            window.MouseDown(new Point(400, 300), MouseButton.Middle, RawInputModifiers.None);
            window.MouseMove(new Point(400 + (i * 7), 300 + (i * 3)), RawInputModifiers.None);
            window.MouseUp(new Point(400 + (i * 7), 300 + (i * 3)), MouseButton.Middle, RawInputModifiers.None);
        }

        // Headless drawing is a stub, so this asserts that the whole render path survives every
        // level-of-detail branch — not that it produced the right pixels.
        Assert.Equal(2000, canvas.Graph.Nodes.Count);
        Assert.True(canvas.LastVisibleNodeCount > 0);
        Assert.True(canvas.LastConsideredNodeCount < 2000);
    });

    /// <summary>Runs a test body on the headless UI thread, rethrowing anything it threw.</summary>
    /// <param name="body">The gesture sequence and its assertions.</param>
    private static void OnUiThread(Action body) =>
        Session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();

    private static void Click(Window window, double x, double y, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Point point = new(x, y);
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    private static Point Screen(GraphCanvas canvas, double worldX, double worldY) => new(
        canvas.Transform.ToScreenX(worldX), canvas.Transform.ToScreenY(worldY));

    private static PlaceholderGraph TwoNodes()
    {
        PlaceholderGraph graph = new();
        graph.Add(new PlaceholderNode("a", "Left", NodeCategory.Input, 0, 0, [], ["out"]));
        graph.Add(new PlaceholderNode("b", "Right", NodeCategory.Math, 300, 0, ["in"], ["out"]));
        return graph;
    }

    private static (Window Window, GraphCanvas Canvas) Open(PlaceholderGraph graph)
    {
        GraphCanvas canvas = new() { Graph = graph };
        Window window = new()
        {
            Width = 800,
            Height = 600,
            Content = canvas,
        };

        window.Show();
        return (window, canvas);
    }
}

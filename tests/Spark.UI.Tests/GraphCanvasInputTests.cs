using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.Engine;
using Spark.UI.Controls;
using Spark.UI.Graph;

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
        CanvasNode node = canvas.Graph.Nodes[0];

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

    /// <summary>
    /// A box dragged to the right selects only the nodes it wholly contains.
    /// </summary>
    /// <remarks>
    /// <b>Direction, as every CAD application has meant it for forty years</b> (`E8-T26`). Users
    /// arrive already knowing this pair, which is the only reason to spend a gesture on it.
    /// </remarks>
    [Fact]
    public void ABoxDraggedRightSelectsOnlyWhatItWhollyContains() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        // Nudged off the corner so a box can start OUTSIDE the first node. At the identity
        // transform that node's top-left corner is the canvas's own, and nothing is outside it.
        canvas.Transform.OffsetX = -50;
        canvas.Transform.OffsetY = -50;

        (Point from, Point to) = WindowOverTheFirstNode(canvas);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(to, RawInputModifiers.None);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal([0], canvas.Selection);
    });

    /// <summary>
    /// The same box dragged the other way selects everything it touches.
    /// </summary>
    [Fact]
    public void ABoxDraggedLeftSelectsEverythingItTouches() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        canvas.Transform.OffsetX = -50;
        canvas.Transform.OffsetY = -50;

        (Point to, Point from) = WindowOverTheFirstNode(canvas);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(to, RawInputModifiers.None);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(2, canvas.Selection.Count);
    });

    /// <summary>
    /// The box being dragged has a positive width and height whichever way the drag went.
    /// </summary>
    /// <remarks>
    /// <b>This is the regression test for a marquee that selected nodes and drew nothing.</b>
    /// <c>DrawMarquee</c> built its rectangle with <c>new Rect(start, end)</c>, and Avalonia's
    /// two-point constructor subtracts rather than ordering — so every right-to-left drag, which is
    /// to say every crossing selection, had a negative width and a rectangle with a negative width
    /// draws nothing at all. Headless rendering produces no pixels to assert on, so the ordered
    /// rectangle is exposed and asserted instead.
    /// </remarks>
    [Fact]
    public void TheMarqueeRectangleIsOrderedWhicheverWayTheDragWent() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        window.MouseDown(new Point(700, 500), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(20, 20), RawInputModifiers.None);

        Assert.True(canvas.MarqueeIsCrossing);
        Assert.True(canvas.MarqueeRectangle.Width > 0, "a right-to-left drag drew a negative width");
        Assert.True(canvas.MarqueeRectangle.Height > 0, "an upward drag drew a negative height");

        window.MouseUp(new Point(20, 20), MouseButton.Left, RawInputModifiers.None);

        // And nothing is left behind once the gesture is over.
        Assert.Equal(default, canvas.MarqueeRectangle);
    });

    /// <summary>
    /// A left-to-right box that covers the first node whole and clips the second, as screen points.
    /// </summary>
    /// <param name="canvas">The canvas, already transformed.</param>
    /// <returns>The press point and the release point, in that order.</returns>
    private static (Point From, Point To) WindowOverTheFirstNode(GraphCanvas canvas)
    {
        Spark.UI.Canvas.CanvasBounds first = canvas.Graph.Nodes[0].Bounds;
        Spark.UI.Canvas.CanvasBounds second = canvas.Graph.Nodes[1].Bounds;

        return (
            Screen(canvas, first.MinX - 20, Math.Min(first.MinY, second.MinY) - 20),
            Screen(
                canvas,
                second.MinX + (second.Width / 2),
                Math.Max(first.MaxY, second.MaxY) + 20));
    }

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

    /// <summary>
    /// <b>Two clicks connect, without holding the button down</b> — `E8-T34`, asked for directly:
    /// dragging between two small targets is precise work, and worse on a trackpad.
    /// </summary>
    [Fact]
    public void ClickingAPortAndThenAnotherCreatesAWire() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        canvas.Graph.Nodes[0].OutputPortCentre(0, out double fromX, out double fromY);
        canvas.Graph.Nodes[1].InputPortCentre(0, out double toX, out double toY);

        Point from = Screen(canvas, fromX, fromY);
        Point to = Screen(canvas, toX, toY);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(from, MouseButton.Left, RawInputModifiers.None);

        // No button held: the wire follows the pointer between the two clicks.
        window.MouseMove(to, RawInputModifiers.None);

        window.MouseDown(to, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);

        Assert.Single(canvas.Graph.Wires);
        Assert.True(canvas.Graph.Wires[0].From.IsOutput);
        Assert.Equal(1, canvas.Graph.Wires[0].To.NodeIndex);
    });

    /// <summary>
    /// <b>The port's name is part of the port</b> — `E8-T36`, which is the whole reason for drawing
    /// a port as a lozenge: clicking the word <c>x</c> starts the wire that <c>x</c> wants, from
    /// twenty pixels inside the node rather than from a disc on its edge.
    /// </summary>
    [Fact]
    public void ClickingAPortsNameStartsItsWire() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        CanvasNode node = canvas.Graph.Nodes[1];
        node.PortTab(0, isOutput: false, out double left, out double top, out double right, out double bottom);

        Point inside = Screen(canvas, (left + right) / 2, (top + bottom) / 2);

        window.MouseMove(inside, RawInputModifiers.None);

        Assert.NotNull(canvas.HoveredPort);
        Assert.Equal(1, canvas.HoveredPort!.Value.NodeIndex);
        Assert.False(canvas.HoveredPort.Value.IsOutput);

        // And it connects from there, without ever touching the disc on the node's edge.
        canvas.Graph.Nodes[0].OutputPortCentre(0, out double fromX, out double fromY);
        Point from = Screen(canvas, fromX, fromY);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(inside, RawInputModifiers.None);
        window.MouseDown(inside, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None);

        Assert.Single(canvas.Graph.Wires);
    });

    /// <summary>A click on empty canvas abandons a wire a click armed, and selects nothing.</summary>
    [Fact]
    public void ClickingAwayAbandonsAPendingWire() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        canvas.Graph.Nodes[0].OutputPortCentre(0, out double fromX, out double fromY);
        Point from = Screen(canvas, fromX, fromY);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(from, MouseButton.Left, RawInputModifiers.None);

        window.MouseDown(new Point(700, 520), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(new Point(700, 520), MouseButton.Left, RawInputModifiers.None);

        canvas.Graph.Nodes[1].InputPortCentre(0, out double toX, out double toY);
        Point to = Screen(canvas, toX, toY);

        window.MouseDown(to, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);

        // The click on the input port armed a new wire rather than completing the abandoned one.
        Assert.Empty(canvas.Graph.Wires);
    });

    /// <summary>Escape abandons a pending wire, which is the other way out of one.</summary>
    [Fact]
    public void EscapeAbandonsAPendingWire() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        canvas.Graph.Nodes[0].OutputPortCentre(0, out double fromX, out double fromY);
        Point from = Screen(canvas, fromX, fromY);

        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(from, MouseButton.Left, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        canvas.Graph.Nodes[1].InputPortCentre(0, out double toX, out double toY);
        Point to = Screen(canvas, toX, toY);

        window.MouseDown(to, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);

        Assert.Empty(canvas.Graph.Wires);
    });

    /// <summary>
    /// <b>A port is easier to hit than it is to see.</b> The disc is 7 px and the target is 18, so
    /// a click 8 px off centre still lands on the port — which is what the size complaint was
    /// really about.
    /// </summary>
    [Fact]
    public void APortIsPickedFromEightPixelsAway() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());

        canvas.Graph.Nodes[0].OutputPortCentre(0, out double x, out double y);

        Point centre = Screen(canvas, x, y);

        window.MouseMove(new Point(centre.X + 8, centre.Y), RawInputModifiers.None);

        Assert.NotNull(canvas.HoveredPort);
        Assert.True(canvas.HoveredPort!.Value.IsOutput);
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
        (Window window, GraphCanvas canvas) = Open(TestGraphs.Demo());

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
        (Window window, GraphCanvas canvas) = Open(TestGraphs.Synthetic(2000));
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

    [Fact]
    public void DrawingAWireReportsAGraphChange() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        int changes = 0;
        GraphEditedEventArgs? edit = null;
        canvas.GraphChanged += (_, e) =>
        {
            changes++;
            edit = e;
        };

        DragWire(window, canvas, 0, 1);

        // The event is what starts an evaluation. Without it the wire is drawn and nothing runs,
        // which is the single most confusing failure this shell can have.
        Assert.Equal(1, changes);
        Assert.Single(canvas.Graph.Wires);
        Assert.NotNull(edit);
        Assert.True(edit.AffectsEvaluation);
    });

    /// <summary>
    /// A drag reports the move as an edit, and says it does not need a run.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The edit is what puts a move on the undo stack; the flag is what stops
    /// the shell evaluating a graph whose every answer it already has, because a position is not in
    /// a node's provenance and cannot change what the node produces.
    /// </remarks>
    [Fact]
    public void MovingNodesReportsAnEditThatDoesNotNeedARun() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        GraphEditedEventArgs? edit = null;
        canvas.GraphChanged += (_, e) => edit = e;

        window.MouseDown(new Point(60, 30), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(160, 80), RawInputModifiers.None);
        window.MouseUp(new Point(160, 80), MouseButton.Left, RawInputModifiers.None);

        Assert.NotNull(edit);
        Assert.Equal("Move node", edit.Label);
        Assert.False(edit.AffectsEvaluation);
    });

    /// <summary>
    /// Pressing a node and releasing without moving it is not an edit.
    /// </summary>
    /// <remarks>
    /// Selecting a node is a click, and every click ends in the drag branch. Recording one would put
    /// a step on the undo stack for every selection, and the user's first Ctrl+Z would do nothing
    /// visible.
    /// </remarks>
    [Fact]
    public void ClickingANodeWithoutMovingItIsNotAnEdit() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        int changes = 0;
        canvas.GraphChanged += (_, _) => changes++;

        Click(window, 60, 30);

        Assert.Equal(0, changes);
    });

    /// <summary>
    /// A drag that comes back to where it started is not an edit either.
    /// </summary>
    /// <remarks>
    /// This is the case that separates "the pointer moved" from "the node moved", and it is why the
    /// canvas accumulates a net displacement rather than setting a flag on the first move event. A
    /// flag would record a step whose undo puts every node back exactly where it already is.
    /// </remarks>
    [Fact]
    public void ADragThatEndsWhereItStartedIsNotAnEdit() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        int changes = 0;
        canvas.GraphChanged += (_, _) => changes++;

        window.MouseDown(new Point(60, 30), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(200, 140), RawInputModifiers.None);
        window.MouseMove(new Point(60, 30), RawInputModifiers.None);
        window.MouseUp(new Point(60, 30), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, canvas.Graph.Nodes[0].X, 6);
        Assert.Equal(0, changes);
    });

    /// <summary>
    /// A wire the engine refuses is not created, and the canvas said so under the cursor first.
    /// </summary>
    [Fact]
    public void ARefusedWireIsNotCreated() => OnUiThread(() =>
    {
        CanvasGraph graph = new();
        int point = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 0);
        int sin = graph.Add(TestGraphs.Library.ByName("Math.Sin"), 300, 0);

        (Window window, GraphCanvas canvas) = Open(graph);
        int changes = 0;
        canvas.GraphChanged += (_, _) => changes++;

        DragWire(window, canvas, point, sin);

        Assert.Empty(canvas.Graph.Wires);
        Assert.Equal(0, changes);
    });

    [Fact]
    public void ClickingAWireSelectsItAndDeleteRemovesIt() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        DragWire(window, canvas, 0, 1);
        Assert.Single(canvas.Graph.Wires);

        int changes = 0;
        canvas.GraphChanged += (_, _) => changes++;

        // The midpoint of the wire, which is empty canvas as far as node hit-testing is concerned.
        canvas.Graph.Nodes[0].OutputPortCentre(0, out double x0, out double y0);
        canvas.Graph.Nodes[1].InputPortCentre(0, out double x1, out double y1);
        Click(window, Screen(canvas, (x0 + x1) / 2, (y0 + y1) / 2));

        Assert.NotNull(canvas.SelectedWire);
        Assert.Empty(canvas.Selection);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.Empty(canvas.Graph.Wires);
        Assert.Null(canvas.SelectedWire);
        Assert.Equal(1, changes);
    });

    [Fact]
    public void DeleteRemovesTheSelectedNodesAndTheirWires() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        DragWire(window, canvas, 0, 1);

        int changes = 0;
        canvas.GraphChanged += (_, _) => changes++;

        Click(window, 60, 30);
        Assert.Single(canvas.Selection);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.Single(canvas.Graph.Nodes);
        Assert.Empty(canvas.Graph.Wires);
        Assert.Empty(canvas.Selection);
        Assert.Equal(1, changes);
    });

    /// <summary>
    /// Deleting with a wire selected removes the wire and not the nodes. A user who just clicked a
    /// wire means the wire, and taking their whole selection instead is the kind of surprise that
    /// costs trust in an editor permanently.
    /// </summary>
    [Fact]
    public void DeletingAWireDoesNotAlsoDeleteNodes() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        DragWire(window, canvas, 0, 1);

        canvas.Graph.Nodes[0].OutputPortCentre(0, out double x0, out double y0);
        canvas.Graph.Nodes[1].InputPortCentre(0, out double x1, out double y1);
        Click(window, Screen(canvas, (x0 + x1) / 2, (y0 + y1) / 2));

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.Equal(2, canvas.Graph.Nodes.Count);
        Assert.Empty(canvas.Graph.Wires);
    });

    [Fact]
    public void SelectingANodeReportsTheSelectionChange() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        int changes = 0;
        canvas.SelectionChanged += (_, _) => changes++;

        Click(window, 60, 30);

        // The inspector rebuilds from this, so a missed notification is a properties panel that
        // shows the previous node's values.
        Assert.True(changes >= 1);
    });

    /// <summary>
    /// Double-clicking empty canvas asks for a code block there, at the point that was clicked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A code block, not the search box.</b> That is Dynamo's gesture and `E8-T27` adopts it:
    /// double-click-then-type is how a Dynamo user writes a number, a formula or a list without
    /// hunting for a node, and a user arriving with that habit got a search dialog instead.
    /// </para>
    /// <para>
    /// The world coordinates are the assertion that matters. A gesture that creates something but
    /// puts it somewhere else is worse than no gesture, because the user has to find and move it
    /// every time.
    /// </para>
    /// </remarks>
    [Fact]
    public void DoubleClickingEmptyCanvasAsksForACodeBlockThere() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        CanvasCreateRequestedEventArgs? request = null;
        int searches = 0;
        canvas.CodeBlockRequested += (_, e) => request = e;
        canvas.CreateRequested += (_, _) => searches++;

        Point empty = new(640, 460);
        DoubleClick(window, empty);

        Assert.NotNull(request);
        Assert.Equal(canvas.Transform.ToWorldX(empty.X), request.WorldX, 3);
        Assert.Equal(canvas.Transform.ToWorldY(empty.Y), request.WorldY, 3);
        Assert.Equal(empty.X, request.ScreenX, 3);
        Assert.Equal(empty.Y, request.ScreenY, 3);

        // And the gesture it replaced must not fire as well, or the user gets a code block with a
        // search box sitting on top of it.
        Assert.Equal(0, searches);
    });

    /// <summary>
    /// Right-clicking empty canvas asks for the node search, at the point that was clicked.
    /// </summary>
    /// <remarks>
    /// The search box did not lose its gesture when the double-click went to code blocks
    /// (`E8-T27`) — it moved to the button that has no other job on the canvas.
    /// </remarks>
    [Fact]
    public void RightClickingEmptyCanvasAsksForTheNodeSearch() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        CanvasCreateRequestedEventArgs? request = null;
        int blocks = 0;
        canvas.CreateRequested += (_, e) => request = e;
        canvas.CodeBlockRequested += (_, _) => blocks++;

        Point empty = new(640, 460);
        window.MouseDown(empty, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(empty, MouseButton.Right, RawInputModifiers.None);

        Assert.NotNull(request);
        Assert.Equal(canvas.Transform.ToWorldX(empty.X), request.WorldX, 3);
        Assert.Equal(empty.X, request.ScreenX, 3);
        Assert.Equal(0, blocks);
    });

    /// <summary>
    /// Right-clicking a node asks for nothing, and does not disturb the selection.
    /// </summary>
    /// <remarks>
    /// A context menu on a node is a real feature with a real menu behind it. Opening the node
    /// search over the node the user pointed at would be answering a question they did not ask.
    /// </remarks>
    [Fact]
    public void RightClickingANodeAsksForNothing() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        int requests = 0;
        canvas.CreateRequested += (_, _) => requests++;

        window.MouseDown(new Point(60, 30), MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(new Point(60, 30), MouseButton.Right, RawInputModifiers.None);

        Assert.Equal(0, requests);
        Assert.Empty(canvas.Selection);
    });

    /// <summary>
    /// Double-clicking a node asks for nothing, because that gesture belongs to the node.
    /// </summary>
    /// <remarks>
    /// It is the in-place editor's gesture (`E8-T5`). Creating something on top of the node that
    /// was double-clicked would be a worse answer than doing nothing, and it would have to be
    /// untaught later.
    /// </remarks>
    [Fact]
    public void DoubleClickingANodeAsksForNothing() => OnUiThread(() =>
    {
        (Window window, GraphCanvas canvas) = Open(TwoNodes());
        int requests = 0;
        canvas.CreateRequested += (_, _) => requests++;
        canvas.CodeBlockRequested += (_, _) => requests++;

        DoubleClick(window, new Point(60, 30));

        Assert.Equal(0, requests);
    });

    private static void DoubleClick(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    /// <summary>Runs a test body on the headless UI thread, rethrowing anything it threw.</summary>
    /// <param name="body">The gesture sequence and its assertions.</param>
    private static void OnUiThread(Action body) => HeadlessSession.Run(body);

    private static void Click(Window window, double x, double y, RawInputModifiers modifiers = RawInputModifiers.None) =>
        Click(window, new Point(x, y), modifiers);

    private static void Click(Window window, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
    }

    /// <summary>Drags a wire from one node's output port 0 to another node's input port 0.</summary>
    private static void DragWire(Window window, GraphCanvas canvas, int fromSlot, int toSlot)
    {
        canvas.Graph.Nodes[fromSlot].OutputPortCentre(0, out double fromX, out double fromY);
        canvas.Graph.Nodes[toSlot].InputPortCentre(0, out double toX, out double toY);

        window.MouseDown(Screen(canvas, fromX, fromY), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Screen(canvas, (fromX + toX) / 2, (fromY + toY) / 2), RawInputModifiers.None);
        window.MouseMove(Screen(canvas, toX, toY), RawInputModifiers.None);
        window.MouseUp(Screen(canvas, toX, toY), MouseButton.Left, RawInputModifiers.None);
    }

    private static Point Screen(GraphCanvas canvas, double worldX, double worldY) => new(
        canvas.Transform.ToScreenX(worldX), canvas.Transform.ToScreenY(worldY));

    private static CanvasGraph TwoNodes() => TestGraphs.SourceAndSink();

    private static (Window Window, GraphCanvas Canvas) Open(CanvasGraph graph)
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

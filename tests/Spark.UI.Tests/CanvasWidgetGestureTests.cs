using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Spark.Engine;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The on-canvas widgets, driven through the control by actual pointer events (`E6-T20`).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the class of test whose absence let three defects reach a person in one session.</b>
/// The viewport had tests for its camera, its renderer, its read-back and its tessellation, and the
/// one thing none of them did was press a button — so a control that drew nothing, and was
/// therefore invisible to the pointer, passed everything ([N88](NOTES.md)). These press buttons.
/// </para>
/// <para>
/// <b>They exist on the canvas and not on the properties pane, and that is not a preference.</b>
/// <c>InspectorPane</c> cannot be shown in the headless session at all: a data-bound
/// <c>TextBlock</c> with <c>TextWrapping="Wrap"</c> inside a <c>Grid</c> hangs
/// <c>Window.Show()</c>, which is an Avalonia headless limitation rather than a Spark defect — the
/// real application renders those panes correctly. [N90](NOTES.md) has the bisection. The canvas
/// draws its own text with <c>DrawingContext</c> and wraps nothing, so it shows and captures
/// normally.
/// </para>
/// </remarks>
public sealed class CanvasWidgetGestureTests
{
    /// <summary>
    /// <b>The canvas is hit-testable where a widget was drawn.</b> The property everything below
    /// depends on, asserted on its own so a regression names itself rather than failing four
    /// gesture tests with no common message.
    /// </summary>
    [Fact]
    public void TheCanvasIsHitTestableOverASliderNode() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int slot) = Open("Number.Slider");

        graph.Nodes[slot].SliderTrack(out double left, out _, out double y);

        window.CaptureRenderedFrame();

        Assert.Same(canvas, window.InputHitTest(Screen(canvas, left + 4, y)));

        window.Close();
    });

    /// <summary>
    /// <b>Dragging the track moves the value.</b> The whole point of a slider, and the one thing a
    /// properties-panel text box cannot do.
    /// </summary>
    [Fact]
    public void DraggingTheTrackMovesTheValue() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int slot) = Open("Number.Slider");

        graph.Nodes[slot].SliderTrack(out double left, out double right, out double y);

        Assert.Equal(0.0, Convert.ToDouble(graph.Literal(slot, 0), Culture));

        Point middle = Screen(canvas, (left + right) / 2, y);

        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);

        // The track runs 0..100, so the middle is about fifty. Asserted as a band rather than a
        // number: the thumb lands where the pointer is, and the pointer is in device pixels.
        double value = Convert.ToDouble(graph.Literal(slot, 0), Culture);

        Assert.True(value is > 35 and < 65, $"the middle of the track gave {value}");

        window.Close();
    });

    /// <summary>And the far end gives the maximum, not something past it.</summary>
    [Fact]
    public void DraggingToTheEndClampsToTheMaximum() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int slot) = Open("Number.Slider");

        graph.Nodes[slot].SliderTrack(out double left, out double right, out double y);

        window.MouseDown(Screen(canvas, (left + right) / 2, y), MouseButton.Left);
        window.MouseMove(Screen(canvas, right + 200, y), RawInputModifiers.LeftMouseButton);
        window.MouseUp(Screen(canvas, right + 200, y), MouseButton.Left);

        Assert.Equal(100.0, Convert.ToDouble(graph.Literal(slot, 0), Culture));

        window.Close();
    });

    /// <summary>
    /// <b>Pressing the slider does not drag the node.</b> The slider lives inside the node's
    /// bounds, so the hit test has to run first — and if it ever stops doing so, every attempt to
    /// set a value will move the node instead.
    /// </summary>
    [Fact]
    public void DraggingTheTrackDoesNotMoveTheNode() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int slot) = Open("Number.Slider");

        CanvasNode node = graph.Nodes[slot];
        node.SliderTrack(out double left, out double right, out double y);

        double x = node.X;
        double top = node.Y;

        window.MouseDown(Screen(canvas, left + 4, y), MouseButton.Left);
        window.MouseMove(Screen(canvas, right - 4, y + 40), RawInputModifiers.LeftMouseButton);
        window.MouseUp(Screen(canvas, right - 4, y + 40), MouseButton.Left);

        Assert.Equal(x, graph.Nodes[slot].X);
        Assert.Equal(top, graph.Nodes[slot].Y);

        window.Close();
    });

    /// <summary>
    /// <b>Clicking a value field asks for an editor rather than dragging the node</b>, and names
    /// the rectangle to put it at (<c>E8-T5</c>).
    /// </summary>
    [Fact]
    public void ClickingAFieldRequestsAnEditor() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int slot) = Open("Number.Value");

        CanvasFieldEditEventArgs? asked = null;
        canvas.FieldEditRequested += (_, e) => asked = e;

        graph.Nodes[slot].FieldBox(out double x, out double y, out double width, out double height);

        Point centre = Screen(canvas, x + (width / 2), y + (height / 2));

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

        Assert.NotNull(asked);
        Assert.Equal(slot, asked!.Slot);
        Assert.Equal("0", asked.Text);
        Assert.True(asked.ScreenWidth > 0, "the editor was given no width to fill");
        Assert.True(asked.ScreenHeight > 0, "the editor was given no height");

        window.Close();
    });

    /// <summary>
    /// <b>The draw path runs over both widget kinds.</b>
    /// </summary>
    /// <remarks>
    /// <b>What this asserts is that rendering happens and the geometry is right, not that a
    /// picture came back.</b> <c>CaptureRenderedFrame</c> returns null in this backend — the
    /// hit-testing test above depends on it having rendered, and gets that, but there is no bitmap
    /// to inspect. So the frame is asked for to drive <c>DrawSlider</c> and <c>DrawField</c>, and
    /// the assertions are on the measurements those two draw from. Claiming more than that would
    /// be the same mistake as a view-model test claiming to prove a surface.
    /// </remarks>
    [Fact]
    public void TheWidgetsDraw() => HeadlessSession.Run(() =>
    {
        (Window window, GraphCanvas canvas, CanvasGraph graph, int slider) = Open("Number.Slider");

        int field = graph.Add(Library.ByName("Number.Value"), 0, 260);

        canvas.InvalidateVisual();
        window.CaptureRenderedFrame();

        CanvasNode sliderNode = graph.Nodes[slider];
        CanvasNode fieldNode = graph.Nodes[field];

        Assert.True(sliderNode.HasSlider);
        Assert.True(fieldNode.HasField);

        sliderNode.SliderTrack(out double left, out double right, out double trackY);

        Assert.True(right > left, "the slider track has no length to drag along");
        Assert.True(trackY > sliderNode.Y + CanvasNode.HeaderHeight, "the track is over the header");
        Assert.True(trackY < sliderNode.Y + sliderNode.Height, "the track is below the node");

        fieldNode.FieldBox(out double x, out double y, out double width, out double height);

        Assert.True(width > 0 && height > 0, "the value field has no area to draw in");
        Assert.True(x > fieldNode.X && x + width < fieldNode.X + fieldNode.Width);
        Assert.True(y + height <= fieldNode.Y + fieldNode.Height);

        window.Close();
    });

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Number).Assembly));

        return library;
    }

    /// <summary>A window holding a canvas with one node of the named kind at the origin.</summary>
    private static (Window Window, GraphCanvas Canvas, CanvasGraph Graph, int Slot) Open(string node)
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName(node), 0, 0);

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();

        return (window, canvas, graph, slot);
    }

    /// <summary>A world point in the canvas's own coordinates, through the pan and zoom.</summary>
    private static Point Screen(GraphCanvas canvas, double worldX, double worldY) =>
        new(canvas.Transform.ToScreenX(worldX), canvas.Transform.ToScreenY(worldY));
}

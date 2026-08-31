using System;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;
using Spark.Nodes.Core;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Number and integer sliders — `E8-T25`.
/// </summary>
/// <remarks>
/// <para>
/// <b>A slider is a node whose value you set by dragging it</b>, which is the one thing a text box
/// in a side panel cannot do however convenient it is: the point is sweeping a value and watching
/// the geometry answer. Asked for directly, against Dynamo's.
/// </para>
/// <para>
/// <b>The positional contract is the part most likely to rot.</b> <c>NodeSliderAttribute</c>
/// promises four inputs in order — value, minimum, maximum, step — and nothing in the type system
/// enforces it, so <see cref="EverySliderNodeHasTheShapeItPromises"/> does, over every node that
/// declares the attribute rather than over the two that exist today.
/// </para>
/// </remarks>
public sealed class SliderNodeTests
{
    /// <summary>
    /// <b>Every node declaring the attribute has the shape the attribute promises.</b> Written to
    /// find the node somebody adds later, not the two that exist now.
    /// </summary>
    [Fact]
    public void EverySliderNodeHasTheShapeItPromises()
    {
        NodeDefinition[] sliders =
            [.. Library.Definitions().Where(definition => definition.HasSlider)];

        Assert.NotEmpty(sliders);

        foreach (NodeDefinition slider in sliders)
        {
            Assert.Equal(
                ["value", "min", "max", "step"],
                slider.Inputs.Select(port => port.Name));

            // The value port's type is what the canvas writes back through, and the range has to
            // be readable as a number or the track cannot be drawn.
            foreach (PortDefinition port in slider.Inputs)
            {
                Assert.True(
                    port.ValueType == typeof(double) || port.ValueType == typeof(int),
                    slider.DisplayName + "." + port.Name + " is " + port.ValueType.Name
                        + ", which a slider track cannot be drawn from");
            }
        }
    }

    /// <summary>Both sliders imported, and only they claim to be sliders.</summary>
    [Fact]
    public void TheTwoSlidersAreTheSliders()
    {
        Assert.True(Library.ByName("Number.Slider").HasSlider);
        Assert.True(Library.ByName("Integer.Slider").HasSlider);

        Assert.Equal(
            ["Integer.Slider", "Number.Slider"],
            Library.Definitions()
                .Where(definition => definition.HasSlider)
                .Select(definition => definition.DisplayName)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// <b>The node clamps, not only the widget.</b> The value port is an ordinary input: it can be
    /// wired and it can be typed into, and a node that honoured its own range only when dragged
    /// would produce out-of-range values by every other route.
    /// </summary>
    [Theory]
    [InlineData(-50.0, 0.0)]
    [InlineData(500.0, 100.0)]
    [InlineData(40.0, 40.0)]
    public void TheNodeClampsWhateverRouteTheValueArrivedBy(double value, double expected) =>
        Assert.Equal(expected, Number.Slider(value, min: 0, max: 100));

    /// <summary>A step snaps the value to a notch.</summary>
    [Theory]
    [InlineData(43.0, 45.0)]
    [InlineData(41.0, 40.0)]
    [InlineData(0.0, 0.0)]
    public void AStepSnapsTheValue(double value, double expected) =>
        Assert.Equal(expected, Number.Slider(value, min: 0, max: 100, step: 5));

    /// <summary>A step of zero does not snap, which is the continuous slider.</summary>
    [Fact]
    public void NoStepMeansNoSnapping() =>
        Assert.Equal(43.7, Number.Slider(43.7, min: 0, max: 100, step: 0));

    /// <summary>
    /// <b>An inverted range is not an error.</b> Dragging <c>max</c> below <c>min</c> while setting
    /// a slider up is an ordinary thing to do half way through, and a node that threw would fill
    /// the diagnostics pane during a gesture the user is still making.
    /// </summary>
    [Fact]
    public void AnInvertedRangeIsSwappedRatherThanRefused() =>
        Assert.Equal(40.0, Number.Slider(40, min: 100, max: 0));

    /// <summary>The integer slider returns whole numbers, which is why it is a separate node.</summary>
    [Fact]
    public void TheIntegerSliderIsWhole()
    {
        Assert.Equal(4, Number.IntegerSlider(4, min: 0, max: 10));
        Assert.Equal(6, Number.IntegerSlider(7, min: 0, max: 10, step: 3));
        Assert.Equal(10, Number.IntegerSlider(99, min: 0, max: 10));
    }

    /// <summary>A step below one is treated as one, because a fraction of an integer is not a step.</summary>
    [Fact]
    public void AnIntegerStepBelowOneIsOne() =>
        Assert.Equal(7, Number.IntegerSlider(7, min: 0, max: 10, step: 0));

    /// <summary>
    /// <b>The canvas node is taller for the track.</b> Without this the slider is drawn over the
    /// node's bottom edge, which is how a widget added to a fixed-height node usually first looks.
    /// </summary>
    [Fact]
    public void ASliderNodeIsTallerThanTheSameNodeWithout()
    {
        CanvasGraph graph = new();

        int slider = graph.Add(Library.ByName("Number.Slider"), 0, 0);
        int plain = graph.Add(Library.ByName("Number.Value"), 0, 200);

        Assert.True(graph.Nodes[slider].HasSlider);
        Assert.False(graph.Nodes[plain].HasSlider);

        // Four ports against one, so compare against what the ports alone would give.
        double portsOnly = CanvasNode.HeaderHeight
            + (4 * CanvasNode.PortPitch)
            + CanvasNode.BodyPadding;

        Assert.Equal(portsOnly + CanvasNode.SliderHeight, graph.Nodes[slider].Height);
    }

    /// <summary>And the track sits inside the node it belongs to.</summary>
    [Fact]
    public void TheTrackIsInsideTheNode()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Slider"), 10, 20);
        CanvasNode node = graph.Nodes[slot];

        node.SliderTrack(out double left, out double right, out double y);

        Assert.True(left > node.X, "the track starts outside the node");
        Assert.True(right < node.X + node.Width, "the track ends outside the node");
        Assert.True(y > node.Y + CanvasNode.HeaderHeight, "the track is over the header");
        Assert.True(y < node.Y + node.Height, "the track is below the node");
    }

    /// <summary>The range is read from the literals the node was seeded with.</summary>
    [Fact]
    public void TheRangeComesFromTheLiterals()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Slider"), 0, 0);

        Assert.True(graph.SliderRange(slot, out double value, out double min, out double max, out double step));

        Assert.Equal(0.0, value);
        Assert.Equal(0.0, min);
        Assert.Equal(100.0, max);
        Assert.Equal(0.0, step);
    }

    /// <summary>
    /// <b>An impossible range is refused rather than divided by.</b> A track whose ends are equal
    /// has no fraction along it, and the drawing code would otherwise produce a NaN thumb position.
    /// </summary>
    [Fact]
    public void AnEmptyRangeIsRefused()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Slider"), 0, 0);

        graph.SetLiteral(slot, 2, 0.0);

        Assert.False(graph.SliderRange(slot, out _, out _, out _, out _));
    }

    /// <summary>Setting the value writes the literal, in the port's own type.</summary>
    [Fact]
    public void SettingTheValueWritesTheLiteralInThePortsType()
    {
        CanvasGraph graph = new();

        int number = graph.Add(Library.ByName("Number.Slider"), 0, 0);
        int integer = graph.Add(Library.ByName("Integer.Slider"), 0, 200);

        Assert.True(graph.SetSliderValue(number, 42.5));
        Assert.True(graph.SetSliderValue(integer, 42.5));

        Assert.Equal(42.5, Assert.IsType<double>(graph.Literal(number, 0)));

        // Rounded into the port's type, not truncated and not left as a double: a count of storeys
        // wired on from here must not arrive as 42.5.
        Assert.Equal(43, Assert.IsType<int>(graph.Literal(integer, 0)));
    }

    /// <summary>
    /// <b>Setting the same value twice reports no change</b>, which is what keeps dragging cheap:
    /// a pointer move is reported far more often than a slider crosses a notch, and each real
    /// change re-runs the graph.
    /// </summary>
    [Fact]
    public void SettingTheSameValueChangesNothing()
    {
        CanvasGraph graph = new();
        int slot = graph.Add(Library.ByName("Number.Slider"), 0, 0);

        Assert.True(graph.SetSliderValue(slot, 30.0));
        Assert.False(graph.SetSliderValue(slot, 30.0));
    }

    /// <summary>
    /// A node that claims to be a slider without the four ports draws none, rather than drawing a
    /// misleading one over whatever ports it does have.
    /// </summary>
    [Fact]
    public void ANodeOfTheWrongShapeIsNotASlider()
    {
        NodeDefinition malformed = new(
            new NodeKey("Test", "Malformed"),
            "Malformed",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("out", typeof(double), 0)],
            arguments => [arguments[0]],
            hasSlider: true);

        CanvasGraph graph = new();
        int slot = graph.Add(malformed, 0, 0);

        Assert.True(malformed.HasSlider, "the definition should carry what it was told");
        Assert.False(graph.Nodes[slot].HasSlider, "the canvas should refuse the wrong shape");
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Number).Assembly));

        return library;
    }
}

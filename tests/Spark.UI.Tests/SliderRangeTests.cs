using System;
using System.Globalization;
using Spark.Engine;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// A slider's value never sits outside the range it declares — `E8-T83`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported by the client</b>: <i>if min and max values are set for an integer/number slider, do
/// not let the user slide beyond those values; currently the slider slides beyond these values.</i>
/// </para>
/// <para>
/// <b>Dragging was already clamped, and a probe proved it before anything was changed</b> — a 10–20
/// slider dragged three hundred pixels past the right end came back 20. What was unguarded was
/// every other route to the same literal, and the one that reads as the reported bug is the third
/// test here: a value that was legal when it was set, stranded outside a range narrowed afterwards.
/// </para>
/// </remarks>
public sealed class SliderRangeTests
{
    /// <summary>
    /// <b>Typing past the end is the same as dragging past it.</b> The properties panel takes any
    /// number, and took it without an opinion until now.
    /// </summary>
    [Fact]
    public void TypingAValueBeyondTheRangeStoresTheEnd()
    {
        (CanvasGraph graph, int slot) = Slider("Number.Slider");

        graph.SetLiteral(slot, 0, 150.0);

        Assert.Equal(100.0, Number(graph, slot));
    }

    /// <summary>And below the bottom, which is the same rule pointing the other way.</summary>
    [Fact]
    public void TypingAValueBelowTheRangeStoresTheStart()
    {
        (CanvasGraph graph, int slot) = Slider("Number.Slider");

        graph.SetLiteral(slot, 1, 10.0);
        graph.SetLiteral(slot, 0, -5.0);

        Assert.Equal(10.0, Number(graph, slot));
    }

    /// <summary>
    /// <b>The case that reads as the reported bug.</b> The value was legal when it was set;
    /// narrowing the range afterwards left it stranded, and because the node clamps its
    /// <i>output</i> the graph quietly returned 10 while the panel still said 50 — the display and
    /// the result disagreeing, with the display being the half a user believes.
    /// </summary>
    [Fact]
    public void NarrowingTheRangeBringsTheValueWithIt()
    {
        (CanvasGraph graph, int slot) = Slider("Number.Slider");

        graph.SetLiteral(slot, 0, 50.0);
        Assert.Equal(50.0, Number(graph, slot));

        graph.SetLiteral(slot, 2, 10.0);

        Assert.Equal(10.0, Number(graph, slot));
    }

    /// <summary>And raising the floor pushes the value up in the same way.</summary>
    [Fact]
    public void RaisingTheMinimumBringsTheValueWithIt()
    {
        (CanvasGraph graph, int slot) = Slider("Number.Slider");

        graph.SetLiteral(slot, 0, 5.0);
        graph.SetLiteral(slot, 1, 40.0);

        Assert.Equal(40.0, Number(graph, slot));
    }

    /// <summary>
    /// <b>An integer slider stores an integer.</b> A clamp that wrote a double back would leave a
    /// <c>10.0</c> in a port the graph promised was an <c>int</c>.
    /// </summary>
    [Fact]
    public void AnIntegerSliderStaysAnInteger()
    {
        (CanvasGraph graph, int slot) = Slider("Integer.Slider");

        graph.SetLiteral(slot, 0, 500);
        graph.SetLiteral(slot, 2, 7);

        object? stored = graph.Literal(slot, 0);

        Assert.IsType<int>(stored);
        Assert.Equal(7, stored);
    }

    /// <summary>
    /// <b>An inverted range is a half-finished gesture, not an error</b> — the same rule the node
    /// itself follows. The ends are read swapped, and a value already inside them is left alone.
    /// </summary>
    [Fact]
    public void AnInvertedRangeIsReadSwapped()
    {
        (CanvasGraph graph, int slot) = Slider("Number.Slider");

        graph.SetLiteral(slot, 0, 50.0);
        graph.SetLiteral(slot, 2, 20.0);
        Assert.Equal(20.0, Number(graph, slot));

        // min above max: read as 20..80, and 20 is inside it, so nothing moves.
        graph.SetLiteral(slot, 1, 80.0);

        Assert.Equal(20.0, Number(graph, slot));
    }

    /// <summary>
    /// <b>The price of never storing an out-of-range value, asserted so it is a decision rather
    /// than a surprise.</b> Retyping a range in two steps clamps at each step, so a value can be
    /// moved by the intermediate state and not moved back by the final one.
    /// </summary>
    /// <remarks>
    /// <b>The alternative is worse.</b> Not clamping on a range change is exactly the state the
    /// client reported: a value stranded outside its own slider, with the node returning one number
    /// and the panel showing another. A value that follows the range is at least always the number
    /// the graph will use.
    /// </remarks>
    [Fact]
    public void RetypingARangeInTwoStepsClampsAtEachStep()
    {
        (CanvasGraph graph, int slot) = Slider("Number.Slider");

        graph.SetLiteral(slot, 0, 50.0);

        // Raising min to 80 while max is still 100 legitimately moves 50 up to 80 ...
        graph.SetLiteral(slot, 1, 80.0);
        Assert.Equal(80.0, Number(graph, slot));

        // ... and lowering max to 90 afterwards does not bring it back down to 50, because 80 is
        // inside 80..90 and nothing remembers what the value used to be.
        graph.SetLiteral(slot, 2, 90.0);
        Assert.Equal(80.0, Number(graph, slot));
    }

    /// <summary>
    /// <b>Nothing that is not a slider is touched.</b> Every literal in the graph goes through the
    /// same gate, and this is the assertion that stops one node's rule reaching all of them.
    /// </summary>
    [Fact]
    public void AnOrdinaryNodeKeepsWhateverItIsGiven()
    {
        CanvasGraph graph = new();
        using Spark.Host.SparkSession session = new();
        int slot = graph.Add(session.Library.ByName("Number.Value"), 0, 0);

        graph.SetLiteral(slot, 0, 9999.0);

        Assert.Equal(9999.0, Convert.ToDouble(graph.Literal(slot, 0), CultureInfo.InvariantCulture));
    }

    private static (CanvasGraph Graph, int Slot) Slider(string name)
    {
        CanvasGraph graph = new();
        using Spark.Host.SparkSession session = new();

        return (graph, graph.Add(session.Library.ByName(name), 0, 0));
    }

    private static double Number(CanvasGraph graph, int slot) =>
        Convert.ToDouble(graph.Literal(slot, 0), CultureInfo.InvariantCulture);
}

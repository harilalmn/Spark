using System.Collections.Generic;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that produce numbers, including the range that makes lacing visible.
/// </summary>
[SparkNode(Category = NodeCategories.Input)]
public static class Number
{
    /// <summary>The most a single <see cref="Range(double, double, double)"/> may produce.</summary>
    /// <remarks>
    /// A step of <c>1e-9</c> across a span of one is thirty years of allocation, and a user who
    /// typed it meant something else. The cap turns a hang into an exception the node reports.
    /// </remarks>
    public const int MaximumRangeCount = 1_000_000;

    /// <summary>Passes a literal number through, so a graph has somewhere to type one.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The same number.</returns>
    [return: NodePort("value")]
    public static double Value(double value = 0) => value;

    /// <summary>
    /// A number set by dragging a slider on the node itself (<c>E8-T25</c>).
    /// </summary>
    /// <param name="value">The number the slider is set to. Clamped into the range.</param>
    /// <param name="min">The left end of the track.</param>
    /// <param name="max">The right end of the track.</param>
    /// <param name="step">
    /// What the value snaps to. Zero or negative means no snapping, which is the continuous
    /// slider.
    /// </param>
    /// <returns>The value, clamped and snapped.</returns>
    /// <remarks>
    /// <para>
    /// <b>The clamping is done here rather than only in the widget</b>, because the value is an
    /// ordinary input port: it can be wired, and it can be typed into in the properties panel. A
    /// node that honoured its range only when dragged would produce a value outside its own
    /// declared bounds by any other route, which is the sort of thing that is discovered a long
    /// way downstream.
    /// </para>
    /// <para>
    /// <b>An inverted range is not an error.</b> Dragging <c>max</c> below <c>min</c> while
    /// setting up a slider is an ordinary thing to do half way through, and a node that threw
    /// would fill the diagnostics pane during a gesture the user is still making. The ends are
    /// swapped instead.
    /// </para>
    /// </remarks>
    [NodeSlider]
    [return: NodePort("value")]
    public static double Slider(double value = 0, double min = 0, double max = 100, double step = 0)
    {
        (double low, double high) = min <= max ? (min, max) : (max, min);

        double clamped = System.Math.Clamp(value, low, high);

        if (step > 0 && double.IsFinite(step))
        {
            clamped = low + (System.Math.Round((clamped - low) / step) * step);
            clamped = System.Math.Clamp(clamped, low, high);
        }

        return clamped;
    }

    /// <summary>
    /// A whole number set by dragging a slider on the node itself (<c>E8-T25</c>).
    /// </summary>
    /// <param name="value">The number the slider is set to. Clamped into the range.</param>
    /// <param name="min">The left end of the track.</param>
    /// <param name="max">The right end of the track.</param>
    /// <param name="step">How far one notch moves. Below one it is treated as one.</param>
    /// <returns>The value, clamped and snapped to a whole number of steps.</returns>
    /// <remarks>
    /// <b>Separate from <see cref="Slider"/> rather than a flag on it</b>, because the port's
    /// <i>type</i> is the difference and a type cannot be a runtime flag. A count of storeys wired
    /// into a node that wants an integer must not arrive as <c>4.000000001</c>, and the only way to
    /// promise that is for the port to say <c>int</c>.
    /// </remarks>
    [SparkNode(Name = "Integer.Slider")]
    [NodeSlider]
    [return: NodePort("value")]
    public static int IntegerSlider(int value = 0, int min = 0, int max = 100, int step = 1)
    {
        (int low, int high) = min <= max ? (min, max) : (max, min);

        int clamped = System.Math.Clamp(value, low, high);
        int notch = System.Math.Max(step, 1);

        if (notch > 1)
        {
            long snapped = low + ((long)System.Math.Round((clamped - (double)low) / notch) * notch);
            clamped = (int)System.Math.Clamp(snapped, low, high);
        }

        return clamped;
    }

    /// <summary>
    /// A list of numbers from <paramref name="start"/> up to <paramref name="end"/>, stepping by
    /// <paramref name="step"/>. <paramref name="end"/> is included when the step lands on it.
    /// </summary>
    /// <remarks>
    /// This is the node lacing is easiest to see through: feed one range into a point's x and
    /// another into its y, set the point node to Cross Product, and a grid appears. A single range
    /// into both under Longest gives a diagonal instead — the same two inputs, a different lacing,
    /// a visibly different result.
    /// </remarks>
    /// <param name="start">The first value.</param>
    /// <param name="end">The value not to pass.</param>
    /// <param name="step">
    /// The increment. Its sign is ignored; the range walks from <paramref name="start"/> towards
    /// <paramref name="end"/>.
    /// </param>
    /// <returns>The list, which is empty only when the step cannot reach the end.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="step"/> is zero or not finite, either bound is not finite, or the range
    /// would exceed <see cref="MaximumRangeCount"/> values.
    /// </exception>
    [return: NodePort("numbers")]
    public static IReadOnlyList<double> Range(double start = 0, double end = 10, double step = 1)
    {
        if (!double.IsFinite(start))
        {
            throw new System.ArgumentOutOfRangeException(nameof(start), start, "Range needs a finite start.");
        }

        if (!double.IsFinite(end))
        {
            throw new System.ArgumentOutOfRangeException(nameof(end), end, "Range needs a finite end.");
        }

        double magnitude = System.Math.Abs(step);
        if (!double.IsFinite(magnitude) || magnitude == 0)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(step), step, "Range needs a non-zero, finite step; a zero step never reaches its end.");
        }

        double span = System.Math.Abs(end - start);
        double exact = (span / magnitude) + 1;
        if (exact > MaximumRangeCount)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(step),
                step,
                $"That start, end and step describe about {exact:F0} values, and a single range is capped at {MaximumRangeCount}.");
        }

        int count = (int)System.Math.Floor(exact + 1e-9);
        double signed = end >= start ? magnitude : -magnitude;

        double[] values = new double[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = start + (signed * index);
        }

        return values;
    }
}

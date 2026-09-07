using System.Collections.Generic;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that produce numbers, including the range that makes lacing visible.
/// </summary>
[SparkNode(Category = NodeCategories.Input)]
public static class Number
{
    /// <summary>The most a single range node may produce.</summary>
    /// <remarks>
    /// <b>An alias, not a second opinion.</b> The number lives on <see cref="NumberRange.MaximumCount"/>
    /// with the arithmetic it guards; this stays because it is shipped public API, and a constant
    /// that disagreed with the check that enforces it would be worse than either.
    /// </remarks>
    public const int MaximumRangeCount = NumberRange.MaximumCount;

    /// <summary>Passes a literal number through, so a graph has somewhere to type one.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The same number.</returns>
    /// <remarks>
    /// <b>The value is typed on the node itself</b> (<c>E8-T5</c>), not only in the properties
    /// panel. Six of these in a graph are otherwise six identical boxes, and finding which one is
    /// the wall height means clicking each in turn.
    /// </remarks>
    [NodeField]
    [SparkNode(Kind = NodeMemberKind.Create)]
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
    [SparkNode(Kind = NodeMemberKind.Create)]
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
    [SparkNode(Name = "Integer.Slider", Kind = NodeMemberKind.Create)]
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
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("numbers")]
    public static IReadOnlyList<double> Range(double start = 0, double end = 10, double step = 1) =>
        NumberRange.ByStep(start, end, step);

    /// <summary>
    /// <paramref name="count"/> numbers evenly spaced from <paramref name="start"/> to
    /// <paramref name="end"/>, <b>including both ends</b>. Dynamo writes this
    /// <c>start..end..#count</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap is the span over <c>count - 1</c>, and getting that wrong is the usual
    /// mistake.</b> <c>#5</c> asks for five values including both bounds, so there are <i>four</i>
    /// gaps between them. Dividing by five gives five values that stop short of the end, which
    /// looks right until you read the last one. <c>E10-T15</c> wrote this down in
    /// <c>concepts/lists.md</c> when the only way to say it was LINQ; this is the node that row
    /// said did not exist.
    /// </para>
    /// <para>
    /// <b>The last value is assigned, not computed.</b> <c>start + (index * span / (count - 1))</c>
    /// at the final index is <i>arithmetically</i> <paramref name="end"/> and in binary floating
    /// point is often the next representable double along — so a range asked to stop at 5 ends at
    /// 4.999999999999999, and a user who compares against their own bound gets `false` for reasons
    /// invisible on screen. Writing the bound in outright costs one assignment and makes the
    /// promise in this summary true.
    /// </para>
    /// <para>
    /// <b><c>count</c> of one is <paramref name="start"/>, not a division by zero</b>, and zero is
    /// the empty list. Both are what a count of that many values means; neither is an error worth
    /// interrupting a graph for.
    /// </para>
    /// </remarks>
    /// <param name="start">The first value, produced exactly.</param>
    /// <param name="end">The last value, produced exactly.</param>
    /// <param name="count">How many values, including both ends.</param>
    /// <returns>The list, of exactly <paramref name="count"/> values.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Either bound is not finite, <paramref name="count"/> is negative, or it exceeds
    /// <see cref="MaximumRangeCount"/>.
    /// </exception>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("numbers")]
    public static IReadOnlyList<double> RangeByCount(double start = 0, double end = 10, int count = 11) =>
        NumberRange.ByCount(start, end, count);

    /// <summary>
    /// <paramref name="count"/> numbers from <paramref name="start"/>, each
    /// <paramref name="step"/> after the one before. Dynamo writes this
    /// <c>start..#count..step</c>.
    /// </summary>
    /// <remarks>
    /// <b>The one range form with no end bound</b>, and the reason it is a separate method rather
    /// than an overload: a count and a step decide where the list stops, so there is nothing to
    /// pass an end to. A negative <paramref name="step"/> counts downwards, which is the whole use
    /// of allowing one — unlike <see cref="Range(double, double, double)"/>, where the bounds
    /// already say which way to walk and a sign could only contradict them.
    /// </remarks>
    /// <param name="start">The first value, produced exactly.</param>
    /// <param name="count">How many values.</param>
    /// <param name="step">The increment, which may be negative.</param>
    /// <returns>The list, of exactly <paramref name="count"/> values.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// <paramref name="start"/> or <paramref name="step"/> is not finite,
    /// <paramref name="count"/> is negative, or it exceeds <see cref="MaximumRangeCount"/>.
    /// </exception>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("numbers")]
    public static IReadOnlyList<double> RangeByCountAndStep(double start = 0, int count = 11, double step = 1) =>
        NumberRange.ByCountAndStep(start, count, step);
}

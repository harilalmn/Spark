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

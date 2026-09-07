using System;
using System.Collections.Generic;

namespace Spark.Api;

/// <summary>
/// The arithmetic behind every form of numeric range Spark understands — the step form, and the
/// two count forms Dynamo writes with a <c>#</c> (<c>E10-T15</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is here and not beside the nodes.</b> Two callers need it: <c>Number.Range</c> and
/// its two siblings in <c>Spark.Nodes.Core</c>, and the code block's range sugar, which
/// <c>Spark.Scripting</c> lowers. <c>Spark.Scripting</c> has no project reference to
/// <c>Spark.Nodes.Core</c> — a code block's reference set is built from <i>whatever assemblies the
/// host process happens to have loaded</i>, so <c>Spark.Nodes.Core.Number.Range(3, 5, 1)</c>
/// compiles in the desktop app and does not in a host that never loaded the node library. A
/// lowering the compiler performs on the user's behalf cannot rest on that: it has to name
/// something every host references by construction. <c>Spark.Api</c> is that, for both callers, and
/// it is in every code block's default imports, so a lowered call needs no import of its own.
/// </para>
/// <para>
/// <b>One implementation, on purpose.</b> The divisor is <c>count - 1</c> and the last value is
/// assigned rather than computed; both are easy to get subtly wrong and neither is visible on
/// screen when it is. Two copies would be two answers to <c>0..1..#5</c> depending on whether it
/// was typed into a node or a code block — the drift <c>NodeLibraryCoverageTests</c> exists to tell
/// the story about.
/// </para>
/// </remarks>
public static class NumberRange
{
    /// <summary>The most values a single range may produce.</summary>
    /// <remarks>
    /// A step of <c>1e-9</c> across a span of one is thirty years of allocation, and a user who
    /// typed it meant something else. The cap turns a hang into an exception the node reports.
    /// </remarks>
    public const int MaximumCount = 1_000_000;

    /// <summary>
    /// Values from <paramref name="start"/> towards <paramref name="end"/>, stepping by
    /// <paramref name="step"/>. Dynamo writes this <c>start..end..step</c>.
    /// </summary>
    /// <param name="start">The first value.</param>
    /// <param name="end">The value not to pass.</param>
    /// <param name="step">
    /// The increment. Its sign is ignored; the range walks from <paramref name="start"/> towards
    /// <paramref name="end"/>, because the bounds already say which way that is.
    /// </param>
    /// <returns>The list, which is empty only when the step cannot reach the end.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="step"/> is zero or not finite, either bound is not finite, or the range
    /// would exceed <see cref="MaximumCount"/> values.
    /// </exception>
    public static IReadOnlyList<double> ByStep(double start, double end, double step)
    {
        RequireFinite(start, nameof(start), "Range needs a finite start.");
        RequireFinite(end, nameof(end), "Range needs a finite end.");

        double magnitude = Math.Abs(step);
        if (!double.IsFinite(magnitude) || magnitude == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step), step, "Range needs a non-zero, finite step; a zero step never reaches its end.");
        }

        double span = Math.Abs(end - start);
        double exact = (span / magnitude) + 1;
        if (exact > MaximumCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(step),
                step,
                $"That start, end and step describe about {exact:F0} values, and a single range is capped at {MaximumCount}.");
        }

        int count = (int)Math.Floor(exact + 1e-9);
        double signed = end >= start ? magnitude : -magnitude;

        double[] values = new double[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = start + (signed * index);
        }

        return values;
    }

    /// <summary>
    /// <paramref name="count"/> values evenly spaced from <paramref name="start"/> to
    /// <paramref name="end"/>, <b>including both ends</b>. Dynamo writes this
    /// <c>start..end..#count</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap is the span over <c>count - 1</c>, and getting that wrong is the usual
    /// mistake.</b> <c>#5</c> asks for five values including both bounds, so there are <i>four</i>
    /// gaps between them. Dividing by five gives five values that stop short of the end, which
    /// looks right until you read the last one.
    /// </para>
    /// <para>
    /// <b>The last value is assigned, not computed.</b> <c>start + (index * gap)</c> at the final
    /// index is <i>arithmetically</i> <paramref name="end"/> and in binary floating point is
    /// frequently the next representable double along — a range asked to walk from <c>0.7</c> to
    /// <c>0.1</c> ends at <c>0.09999999999999998</c>, which reads as <c>0.1</c> everywhere a user
    /// can see it and compares unequal to the bound they typed. Writing the bound in outright costs
    /// one assignment and makes the promise in this summary true.
    /// </para>
    /// <para>
    /// <b><c>count</c> of one is <paramref name="start"/>, and zero is empty.</b> Both would be a
    /// division by zero under the general formula; neither is a mistake worth stopping a graph for.
    /// </para>
    /// </remarks>
    /// <param name="start">The first value, produced exactly.</param>
    /// <param name="end">The last value, produced exactly.</param>
    /// <param name="count">How many values, including both ends.</param>
    /// <returns>The list, of exactly <paramref name="count"/> values.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either bound is not finite, <paramref name="count"/> is negative, or it exceeds
    /// <see cref="MaximumCount"/>.
    /// </exception>
    public static IReadOnlyList<double> ByCount(double start, double end, int count)
    {
        RequireFinite(start, nameof(start), "Range needs a finite start.");
        RequireFinite(end, nameof(end), "Range needs a finite end.");
        RequireCount(count);

        if (count == 0)
        {
            return Array.Empty<double>();
        }

        double[] values = new double[count];
        values[0] = start;

        if (count == 1)
        {
            return values;
        }

        double gap = (end - start) / (count - 1);
        for (int index = 1; index < count - 1; index++)
        {
            values[index] = start + (gap * index);
        }

        // Not computed: see the remarks. The bound the caller typed is the bound they get.
        values[count - 1] = end;

        return values;
    }

    /// <summary>
    /// <paramref name="count"/> values from <paramref name="start"/>, each <paramref name="step"/>
    /// after the one before. Dynamo writes this <c>start..#count..step</c>.
    /// </summary>
    /// <remarks>
    /// <b>The one range form with no end bound.</b> A count and a step decide where the list stops,
    /// so there is nothing to pass an end to — and a negative <paramref name="step"/> counts
    /// downwards, which is the whole use of allowing a sign here where <see cref="ByStep"/> ignores
    /// one. There the bounds already say which way to walk, and a sign could only contradict them.
    /// </remarks>
    /// <param name="start">The first value, produced exactly.</param>
    /// <param name="count">How many values.</param>
    /// <param name="step">The increment, which may be negative.</param>
    /// <returns>The list, of exactly <paramref name="count"/> values.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start"/> or <paramref name="step"/> is not finite,
    /// <paramref name="count"/> is negative, or it exceeds <see cref="MaximumCount"/>.
    /// </exception>
    public static IReadOnlyList<double> ByCountAndStep(double start, int count, double step)
    {
        RequireFinite(start, nameof(start), "Range needs a finite start.");
        RequireFinite(step, nameof(step), "Range needs a finite step.");
        RequireCount(count);

        double[] values = new double[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = start + (step * index);
        }

        return values;
    }

    private static void RequireFinite(double value, string name, string message)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, value, message);
        }
    }

    private static void RequireCount(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, "A range cannot have a negative count.");
        }

        if (count > MaximumCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"That count is {count} values, and a single range is capped at {MaximumCount}.");
        }
    }
}

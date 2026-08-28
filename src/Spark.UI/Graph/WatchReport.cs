using System;
using System.Collections.Generic;
using System.Globalization;
using Spark.Api;

namespace Spark.UI.Graph;

/// <summary>One line of a watch report: a nesting depth and the text at it.</summary>
/// <param name="Depth">How deep in the list structure the line sits. Zero is a port.</param>
/// <param name="Text">The line, already formatted and not clipped.</param>
public readonly record struct WatchLine(int Depth, string Text);

/// <summary>
/// Renders a node's whole output for the watch panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the deliberate opposite of the preview strip under a node, and the three ways it
/// differs are the three things the strip's own documentation says it will not do.</b> The strip
/// shows the first output port; this shows every one. The strip shows six elements and says how
/// many it left out; this shows all of them. The strip clips a long value with an ellipsis so
/// that one enormous string cannot lay a band across the graph; this does not clip at all,
/// because <i>if you need the whole value, that is what the watch panel is for</i> is a promise
/// the panel now has to keep.
/// </para>
/// <para>
/// <b>There is still a bound, and it is on lines rather than on characters.</b> A list of a
/// million points expanded in full is not a readout, it is a hang — and a hang is a worse answer
/// than a truncated one. The cap is high enough that no ordinary value reaches it and the report
/// says plainly how many lines it did not write, because silence would make a list of a million
/// read as a list of two thousand. That is the same argument the collapsed strip makes for
/// naming the count it left out.
/// </para>
/// <para>
/// <b>Rank is on every list line, at every depth.</b> A hundred points at rank 1 and a hundred at
/// rank 2 draw identically in the viewport and lace completely differently, and a panel that
/// showed the elements without the shape would answer the easy question while hiding the one
/// people actually get wrong.
/// </para>
/// </remarks>
public static class WatchReport
{
    /// <summary>
    /// The most lines a report contains before it stops and says what it left out.
    /// </summary>
    public const int MaximumLines = 2000;

    /// <summary>
    /// Renders every output port of one node.
    /// </summary>
    /// <param name="ports">The output ports, in order.</param>
    /// <param name="values">
    /// The value on each port, in the same order. A port with no value — a node that has not run,
    /// or that failed — is reported as such rather than omitted, because a missing row reads as a
    /// node with fewer outputs than it has.
    /// </param>
    /// <returns>The lines.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="ports"/> or <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<WatchLine> Describe(
        IReadOnlyList<CanvasPortInfo> ports,
        IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(values);

        List<WatchLine> lines = [];
        int omitted = 0;

        for (int index = 0; index < ports.Count; index++)
        {
            object? value = index < values.Count ? values[index] : null;

            Write(
                lines,
                0,
                string.Create(CultureInfo.InvariantCulture, $"{ports[index].Name} — {Headline(value)}"),
                ref omitted);

            // Only a list has anything below its headline. A scalar's text IS the headline, and
            // expanding it would write every single value in the panel twice.
            if (value is SparkList list)
            {
                Expand(lines, 1, list, ref omitted);
            }
        }

        if (omitted > 0)
        {
            lines.Add(new WatchLine(
                0,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"… {omitted:N0} more {(omitted == 1 ? "line" : "lines")} not shown.")));
        }

        return lines;
    }

    private static void Expand(List<WatchLine> lines, int depth, SparkList list, ref int omitted)
    {
        for (int index = 0; index < list.Count; index++)
        {
            object? item = list[index];
            string prefix = string.Create(CultureInfo.InvariantCulture, $"[{index}] ");

            if (item is SparkList nested)
            {
                Write(lines, depth, prefix + Headline(nested), ref omitted);
                Expand(lines, depth + 1, nested, ref omitted);
                continue;
            }

            Write(lines, depth, prefix + Render(item), ref omitted);
        }
    }

    private static void Write(List<WatchLine> lines, int depth, string text, ref int omitted)
    {
        if (lines.Count >= MaximumLines)
        {
            omitted++;
            return;
        }

        lines.Add(new WatchLine(depth, text));
    }

    private static string Headline(object? value) => value switch
    {
        null => "nothing yet",
        SparkList list => string.Create(
            CultureInfo.InvariantCulture,
            $"{list.Count:N0} {(list.Count == 1 ? "item" : "items")} · rank {list.Rank}"),
        _ => Render(value),
    };

    private static string Render(object? value) => value switch
    {
        null => "null",

        // The invariant culture, and no clipping. A watch panel that wrote 1,5 on one machine and
        // 1.5 on another would make two users reading the same graph disagree about the value.
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),

        _ => value.ToString() ?? string.Empty,
    };
}

using System;
using System.Globalization;

namespace Spark.Api;

/// <summary>
/// How a value is written down for a person to read: its shape, a glance at it, and the whole of
/// it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Here, rather than in the shell, so that there is exactly one rendering of a value.</b>
/// The canvas draws it in a preview bubble, the properties pane shows it in a watch panel, and
/// <c>spark run</c> prints it with no window anywhere. `E12-T5` requires that the command line
/// produce <i>output identical to the desktop app's</i>, and the only way to keep a requirement
/// like that true is to make it structural: one implementation both callers reach, in a layer
/// beneath both.
/// </para>
/// <para>
/// The three renderings are deliberately different lengths, because they answer different
/// questions. <see cref="Shape(object?)"/> says what kind of thing this is — the question users get wrong.
/// <see cref="Summary"/> is a glance, cut short enough to sit under a node. <see cref="Full"/> is
/// for reading, and is capped only where a text box would stop being a user interface.
/// </para>
/// </remarks>
public static class ValueText
{
    /// <summary>The longest a <see cref="Summary"/> can be, ellipsis included.</summary>
    /// <remarks>
    /// Sized for a preview bubble under a node rather than for a panel. Long enough that a point
    /// or a small list arrives whole; short enough that a bubble does not become the graph.
    /// </remarks>
    public const int SummaryLength = 60;

    /// <summary>How many characters <see cref="Full"/> renders before it cuts.</summary>
    /// <remarks>
    /// A cap rather than no cap, because a list of a hundred thousand points renders to several
    /// megabytes and a text box handed that stops being a user interface. Generous enough that
    /// anything a person is actually reading arrives whole.
    /// </remarks>
    public const int FullLength = 20_000;

    /// <summary>
    /// A value's shape: its rank, and its length when it has one.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>Text such as <c>rank 0 · one value</c> or <c>rank 2 · 4 items</c>.</returns>
    /// <remarks>
    /// <b>Rank 0 says <i>one value</i>, never <i>0 items</i>.</b> A single value and an empty list
    /// are precisely the two things this line exists to tell apart, and wording them alike would
    /// defeat it at the one moment it matters. Rank is what users get wrong: <c>[[1], [2]]</c> and
    /// <c>[1, 2]</c> read alike at a glance and behave completely differently under lacing.
    /// </remarks>
    public static string Shape(object? value) => Shape(
        SparkList.RankOf(value), value is SparkList list ? list.Count : 0);

    /// <summary>The same line, from a rank and a count that have already been worked out.</summary>
    /// <param name="rank">The rank: 0 for a single value, 1 for a list, 2 for a list of lists.</param>
    /// <param name="count">How many items, when the value is a list.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    public static string Shape(int rank, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (rank == 0)
        {
            return "rank 0 · one value";
        }

        string items = count == 1
            ? "1 item"
            : string.Create(CultureInfo.InvariantCulture, $"{count} items");

        return string.Create(CultureInfo.InvariantCulture, $"rank {rank} · {items}");
    }

    /// <summary>
    /// A one-line glance at a value, cut at <see cref="SummaryLength"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The rendering, or null when there is nothing to say.</returns>
    /// <remarks>
    /// The value and nothing else. Rank and length used to be prefixed here, and once
    /// <see cref="Shape(object?)"/> existed that made a preview bubble read <c>rank 1 · 8 items</c> above
    /// <c>8 items, rank 1  [...]</c> — the same fact twice, in two wordings, in adjacent lines.
    /// </remarks>
    public static string? Summary(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string text = value.ToString() ?? string.Empty;
        // Exactly SummaryLength when it cuts, ellipsis included, so the constant names the
        // width a caller has to lay out rather than a threshold that produces some other number.
        return text.Length > SummaryLength ? text[..(SummaryLength - 1)] + "…" : text;
    }

    /// <summary>
    /// The whole of a value, capped at <see cref="FullLength"/> with the cut announced.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The rendering, or an empty string when there is nothing to show.</returns>
    /// <remarks>
    /// The cut says how much was left out rather than trailing off, because <b>a truncation that
    /// trails off is one a reader mistakes for the end of their data</b> — and acting on a value
    /// you believe is complete and is not is a mistake that shows up much later, in geometry.
    /// </remarks>
    public static string Full(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        string text = value.ToString() ?? string.Empty;
        if (text.Length <= FullLength)
        {
            return text;
        }

        int hidden = text.Length - FullLength;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{text[..FullLength]}{Environment.NewLine}{Environment.NewLine}… {hidden} more characters not shown.");
    }
}

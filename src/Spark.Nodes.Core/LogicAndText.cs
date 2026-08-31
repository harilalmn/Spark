using System;
using System.Globalization;
using System.Linq;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Boolean logic and comparison.
/// </summary>
/// <remarks>
/// Comparison nodes take numbers and a tolerance-free equality is deliberately not
/// among them: <see cref="Equal"/> takes an explicit tolerance, because two doubles that came from
/// different arithmetic are almost never bitwise equal and a node that answered <c>false</c> to
/// <c>0.1 + 0.2 == 0.3</c> would be technically correct and useless.
/// </remarks>
[SparkNode(Category = NodeCategories.Logic)]
public static class Logic
{
    /// <summary>True when both inputs are true.</summary>
    /// <param name="a">The first input.</param>
    /// <param name="b">The second input.</param>
    /// <returns>The conjunction.</returns>
    [SparkNode(Name = "Logic.And")]
    [return: NodePort("result")]
    public static bool And(bool a = false, bool b = false) => a && b;

    /// <summary>True when either input is true.</summary>
    /// <param name="a">The first input.</param>
    /// <param name="b">The second input.</param>
    /// <returns>The disjunction.</returns>
    [SparkNode(Name = "Logic.Or")]
    [return: NodePort("result")]
    public static bool Or(bool a = false, bool b = false) => a || b;

    /// <summary>The opposite of the input.</summary>
    /// <param name="value">The input.</param>
    /// <returns>Its negation.</returns>
    [SparkNode(Name = "Logic.Not")]
    [return: NodePort("result")]
    public static bool Not(bool value = false) => !value;

    /// <summary>
    /// Chooses between two values on a condition.
    /// </summary>
    /// <param name="condition">Which to take.</param>
    /// <param name="whenTrue">The value when the condition holds.</param>
    /// <param name="whenFalse">The value otherwise.</param>
    /// <returns>The chosen value.</returns>
    /// <remarks>
    /// <b>Both branches are evaluated</b>, because a node's inputs are values by the time it runs —
    /// this is a graph, not a language, and there is no laziness to exploit. A caller who needs one
    /// branch not to run needs two graphs, not this node.
    /// </remarks>
    [SparkNode(Name = "Logic.If")]
    [return: NodePort("result")]
    public static object? If(
        bool condition = false,
        [KeepStructure] object? whenTrue = null,
        [KeepStructure] object? whenFalse = null) => condition ? whenTrue : whenFalse;

    /// <summary>Whether two numbers are equal to within a tolerance.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <param name="tolerance">How close counts as equal. Must not be negative.</param>
    /// <returns>True when they are within the tolerance of each other.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    [SparkNode(Name = "Logic.Equal")]
    [return: NodePort("result")]
    public static bool Equal(double a = 0, double b = 0, [NoReplication] double tolerance = 1e-9)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        return System.Math.Abs(a - b) <= tolerance;
    }

    /// <summary>Whether the first number is smaller than the second.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns>True when <paramref name="a"/> is less than <paramref name="b"/>.</returns>
    [SparkNode(Name = "Logic.LessThan")]
    [return: NodePort("result")]
    public static bool LessThan(double a = 0, double b = 0) => a < b;

    /// <summary>Whether the first number is larger than the second.</summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns>True when <paramref name="a"/> is greater than <paramref name="b"/>.</returns>
    [SparkNode(Name = "Logic.GreaterThan")]
    [return: NodePort("result")]
    public static bool GreaterThan(double a = 0, double b = 0) => a > b;
}

/// <summary>
/// Text.
/// </summary>
/// <remarks>
/// Small on purpose. Text is where a node library grows without limit and where almost none of the
/// growth earns its place in a geometry tool — what a graph actually needs is to build a label,
/// read a number out of one, and join a few together.
/// </remarks>
[SparkNode(Category = NodeCategories.Input)]
public static class Text
{
    /// <summary>Passes text through, so a graph has somewhere to type some.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The same text.</returns>
    /// <remarks>
    /// <b>The text is typed on the node itself</b> (<c>E8-T5</c>), for the reason
    /// <c>Number.Value</c>'s is: a label is what the node is *for*, and a graph full of boxes all
    /// reading <c>String.Value</c> tells you nothing about which is which.
    /// </remarks>
    [SparkNode(Name = "String.Value")]
    [NodeField]
    [return: NodePort("text")]
    public static string Value(string value = "") => value ?? string.Empty;

    /// <summary>Joins two pieces of text.</summary>
    /// <param name="first">The text that comes first.</param>
    /// <param name="second">The text that follows it.</param>
    /// <returns>The two joined.</returns>
    [SparkNode(Name = "String.Concat")]
    [return: NodePort("text")]
    public static string Concat(string first = "", string second = "") =>
        (first ?? string.Empty) + (second ?? string.Empty);

    /// <summary>How many characters the text has.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The length.</returns>
    [SparkNode(Name = "String.Length")]
    [return: NodePort("length")]
    public static int Length(string text = "") => (text ?? string.Empty).Length;

    /// <summary>
    /// Renders a number as text.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="decimals">How many decimal places. Between 0 and 15.</param>
    /// <returns>The rendered number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimals"/> is outside 0..15.</exception>
    /// <remarks>
    /// <b>Invariant culture, always.</b> A graph that renders <c>3.14</c> on one machine and
    /// <c>3,14</c> on another produces files that differ by locale, and a `.spark` file is meant to
    /// diff cleanly wherever it was written ([ADR-0017](../../docs/adr/0017-spark-file-is-plain-json.md)).
    /// </remarks>
    [SparkNode(Name = "String.FromNumber")]
    [return: NodePort("text")]
    public static string FromNumber(double value = 0, [NoReplication] int decimals = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 15);

        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a number out of text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The number.</returns>
    /// <exception cref="ArgumentException">The text is not a number.</exception>
    /// <remarks>
    /// Invariant culture again, and it throws rather than returning zero: a graph that silently
    /// turned a typo into zero would produce geometry at the origin and no explanation.
    /// </remarks>
    [SparkNode(Name = "String.ToNumber")]
    [return: NodePort("number")]
    public static double ToNumber(string text = "0")
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ArgumentException(
                $"'{text}' is not a number. Numbers are read in the invariant culture, so the "
                + "decimal separator is a point.",
                nameof(text));
        }

        return value;
    }

    /// <summary>Joins a list of text with a separator between each pair.</summary>
    /// <param name="items">The list of text.</param>
    /// <param name="separator">What to put between them.</param>
    /// <returns>The joined text.</returns>
    [SparkNode(Name = "String.JoinList")]
    [return: NodePort("text")]
    public static string JoinList([KeepStructure] object? items, [NoReplication] string separator = ", ")
    {
        if (items is not SparkList list)
        {
            return items?.ToString() ?? string.Empty;
        }

        return string.Join(
            separator ?? string.Empty,
            Enumerable.Range(0, list.Count).Select(i => list[i]?.ToString() ?? string.Empty));
    }
}

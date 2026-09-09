using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Writes text for a person to read, in the Console pane (<c>E8-T80</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: a console with <c>Write</c>, <c>WriteLine</c> and <c>Clear</c>,
/// callable from a code block and available as nodes. This is both — the same three methods the
/// node importer turns into three nodes and a code block calls directly.
/// </para>
/// <para>
/// <b>Every method returns something, and that is not decoration.</b> A <c>void</c> method is not
/// imported at all: the importer's own words are <i>it returns void and has no out parameter, so it
/// produces no value a graph can carry</i>. Returning what was written is what a graph wants
/// anyway — the node has an output to wire onward, so writing a value to the console can sit in the
/// middle of a chain instead of ending it.
/// </para>
/// <para>
/// <b><see cref="NodeSideEffectAttribute"/> is what makes it work twice.</b> Without it the
/// evaluation cache serves the first run's answer and the second run prints nothing, because the
/// inputs have not changed — which is correct for a node that computes and wrong for one whose
/// entire purpose is the thing it does on the way. The attribute mixes the run counter into the
/// cache key, so these re-evaluate every run and so does everything downstream of them.
/// </para>
/// <para>
/// <b>In a code block, <c>Console</c> means this type</b>, not <c>System.Console</c>. A windowed
/// application has no terminal, so <c>System.Console.WriteLine</c> writes where nobody can see it;
/// the prelude pins the name here, exactly as <c>E6-T30</c> pins <c>Circle</c> and the other eight.
/// <c>System.Console</c> is still reachable by its full name.
/// </para>
/// </remarks>
[NodeSideEffect("writing to the console is the point of the node, so it runs every time.")]
public static class Console
{
    /// <summary>Writes text to the console without ending the line.</summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The text, so the node can pass it on.</returns>
    /// <example>
    /// <code>
    /// Console.Write("a");
    /// Console.WriteLine("bc");   // one line: abc
    /// </code>
    /// </example>
    [SparkNode(Category = NodeCategories.Display)]
    public static string Write(string text)
    {
        SparkConsole.Write(text);
        return text;
    }

    /// <summary>Writes text to the console and ends the line.</summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The text, so the node can pass it on.</returns>
    /// <example>
    /// <code>
    /// var name = Console.WriteLine("wall height: 3.2m");
    /// </code>
    /// </example>
    [SparkNode(Category = NodeCategories.Display)]
    public static string WriteLine(string text)
    {
        SparkConsole.WriteLine(text);
        return text;
    }

    /// <summary>
    /// Empties the console.
    /// </summary>
    /// <returns>
    /// How many lines were removed, which is <b>why this returns a number rather than nothing</b>:
    /// a node with no output cannot exist, and the count is the one honest thing a clear knows.
    /// </returns>
    /// <example>
    /// <code>
    /// Console.Clear();
    /// Console.WriteLine("starting again");
    /// </code>
    /// </example>
    [SparkNode(Category = NodeCategories.Display)]
    public static int Clear()
    {
        int removed = SparkConsole.Lines().Count;
        SparkConsole.Clear();

        return removed;
    }
}

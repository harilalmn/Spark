using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Spark.Api;

/// <summary>
/// The text a graph writes for a person to read (<c>E8-T80</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <c>Console.Write</c>, <c>Console.WriteLine</c> and
/// <c>Console.Clear</c>, callable from a code block, with nodes for the same three and a dockable
/// pane to read them in. This type is the channel all of that writes to, and it knows nothing about
/// any of it.
/// </para>
/// <para>
/// <b>It lives in <c>Spark.Api</c> because that is the assembly both ends can name.</b> A code
/// block already imports <c>Spark.Api</c>, and a node library must be able to write here without
/// reaching for the user interface — <c>Spark.UI</c> is deliberately not a contract assembly, and
/// nothing a package legitimately does should require its types.
/// </para>
/// <para>
/// <b>Static, because a graph is not handed a console.</b> A user writing <c>Console.WriteLine("x")</c>
/// in a code block has nowhere to get an instance from, and threading one through every node
/// signature to avoid a static would be a worse answer to a smaller problem.
/// </para>
/// <para>
/// <b>Bounded, and honest about it.</b> A loop writing a line per iteration would otherwise grow
/// until the process died. The oldest lines go first and <see cref="Dropped"/> counts them, because
/// a console that quietly loses the beginning of the output is worse than one that says how much it
/// lost.
/// </para>
/// </remarks>
public static class SparkConsole
{
    /// <summary>How many completed lines are kept before the oldest are dropped.</summary>
    /// <remarks>
    /// <b>Large enough to be a log and small enough to be a buffer.</b> Ten thousand lines is more
    /// than anybody reads and a few megabytes at worst; the alternative is a setting nobody would
    /// know how to answer.
    /// </remarks>
    public const int Capacity = 10_000;

    private static readonly Lock Gate = new();
    private static readonly Queue<string> Completed = new();
    private static string _pending = string.Empty;
    private static int _dropped;

    /// <summary>
    /// Raised after anything is written or cleared, so a view can catch up.
    /// </summary>
    /// <remarks>
    /// <b>Raised outside the lock, always.</b> A handler is somebody else's code — it may write to
    /// the console itself, or marshal to a user interface thread that is at that moment blocked
    /// waiting for this very lock. Raising inside would deadlock on the first of those and it would
    /// happen in a graph the author could not debug.
    /// </remarks>
    public static event EventHandler? Changed;

    /// <summary>How many lines have been dropped because the buffer was full.</summary>
    public static int Dropped
    {
        get
        {
            lock (Gate)
            {
                return _dropped;
            }
        }
    }

    /// <summary>
    /// Writes text without ending the line.
    /// </summary>
    /// <param name="text">What to write. Null writes nothing at all.</param>
    /// <remarks>
    /// <b>It appends to a pending line that <see cref="WriteLine(string)"/> completes</b>, which is
    /// <c>System.Console</c>'s own contract and the one anybody typing this expects. <b>Several
    /// threads writing partial lines can interleave</b> — a graph evaluates in parallel — and that
    /// is also what the real console does. Whole lines never interleave.
    /// </remarks>
    public static void Write(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (Gate)
        {
            _pending += text;
        }

        Announce();
    }

    /// <summary>Writes a value without ending the line.</summary>
    /// <param name="value">The value, formatted invariantly. Null writes nothing.</param>
    public static void Write(object? value) => Write(Format(value));

    /// <summary>Ends the current line.</summary>
    public static void WriteLine() => WriteLine(string.Empty);

    /// <summary>Writes text and ends the line.</summary>
    /// <param name="text">What to write. Null writes an empty line, as <c>System.Console</c> does.</param>
    public static void WriteLine(string? text)
    {
        lock (Gate)
        {
            Completed.Enqueue(_pending + (text ?? string.Empty));
            _pending = string.Empty;

            while (Completed.Count > Capacity)
            {
                _ = Completed.Dequeue();
                _dropped++;
            }
        }

        Announce();
    }

    /// <summary>Writes a value and ends the line.</summary>
    /// <param name="value">The value, formatted invariantly.</param>
    public static void WriteLine(object? value) => WriteLine(Format(value));

    /// <summary>
    /// Empties the console, including anything written but not yet ended.
    /// </summary>
    /// <remarks>
    /// <b>The pending line goes too.</b> A <c>Clear</c> that left half a line behind, to appear at
    /// the front of whatever was written next, would be a puzzle rather than a clear.
    /// </remarks>
    public static void Clear()
    {
        lock (Gate)
        {
            Completed.Clear();
            _pending = string.Empty;
            _dropped = 0;
        }

        Announce();
    }

    /// <summary>
    /// Everything readable right now, oldest first, with any unfinished line last.
    /// </summary>
    /// <returns>A snapshot that does not change under the caller.</returns>
    /// <remarks>
    /// <b>The unfinished line is included</b>, so a <see cref="Write(string)"/> with no
    /// <see cref="WriteLine()"/> after it is visible rather than invisible until something ends it.
    /// A user debugging with a bare <c>Write</c> is the person most in need of seeing it.
    /// </remarks>
    public static IReadOnlyList<string> Lines()
    {
        lock (Gate)
        {
            List<string> snapshot = new(Completed.Count + 1);
            snapshot.AddRange(Completed);

            if (_pending.Length > 0)
            {
                snapshot.Add(_pending);
            }

            return snapshot;
        }
    }

    /// <summary>Everything readable right now as one block of text.</summary>
    /// <returns>The lines, newline separated.</returns>
    public static string Text() => string.Join(Environment.NewLine, Lines());

    /// <summary>
    /// A value as the console shows it.
    /// </summary>
    /// <remarks>
    /// <b>Invariant, because a console line is not a user-facing document.</b> It is read beside
    /// code and pasted into issues, and a number that changes its decimal separator with the
    /// machine's locale makes those two things disagree.
    /// </remarks>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static void Announce() => Changed?.Invoke(null, EventArgs.Empty);
}

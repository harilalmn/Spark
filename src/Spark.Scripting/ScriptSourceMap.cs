using System;
using System.Collections.Immutable;
using System.Globalization;

namespace Spark.Scripting;

/// <summary>
/// One run of generated lines that came from somewhere other than directly under the frame — a
/// type declaration the wrapper lifted out of the entry point (`E6-T34`).
/// </summary>
/// <param name="GeneratedStart">The one-based line in the generated source the run starts on.</param>
/// <param name="UserStart">The one-based line in the user's script the run started on.</param>
/// <param name="Lines">How many lines the run occupies, which is the same in both.</param>
public readonly record struct ScriptSourceSegment(int GeneratedStart, int UserStart, int Lines);

/// <summary>
/// Maps a position in the generated source back to the position in what the user typed
/// (`E6-T1`).
/// </summary>
/// <remarks>
/// <para>
/// <b>A code block's source is not what the compiler sees.</b> It is wrapped in a namespace, a
/// class and a method, preceded by the prelude's <c>using</c> lines, a cancellation check, a guard
/// budget and one declaration per input port. A diagnostic therefore arrives on a line that means
/// nothing to the person who wrote the code — <c>(14,9): ; expected</c> in a four-line script.
/// </para>
/// <para>
/// <b>The map is a subtraction for the block's statements, and it is only that because two other
/// decisions were made to keep it one.</b> The wrapper puts every line it adds <i>before</i> the
/// user's first, so the offset is constant rather than a table; and <see cref="GuardWeaver"/>
/// weaves statements with no trivia at all, so rewriting adds no lines either (`E6-T4`).
/// </para>
/// <para>
/// <b>`E6-T34` bought one exception to that with its eyes open, and it is the reason
/// <see cref="Hoisted"/> exists.</b> A type declared in a block cannot stay where it was written —
/// C# has no local class — so the wrapper blanks each declaration <i>to spaces</i> where it stood,
/// which keeps the statements' lengths and line numbers exactly as they were, and re-emits it after
/// the generated class. Those re-emitted lines are the one region where a subtraction gives the
/// wrong answer, so each carries a segment saying where it came from. The statements' half of the
/// map is untouched, and a block that declares no type has no segments at all.
/// </para>
/// <para>
/// <b>Columns are not mapped, deliberately.</b> Nothing the wrapper adds is on a user line, so a
/// column is already the user's column — and a map that adjusted them would be adjusting them by
/// zero, which is a claim with no meaning attached. A hoisted declaration keeps this true by being
/// re-emitted at the column it was written at, padding included.
/// </para>
/// </remarks>
/// <param name="PreludeLines">
/// How many lines the generated frame puts before the user's first line.
/// </param>
/// <param name="Hoisted">
/// The runs of generated lines that were moved, in the order they were emitted. Empty for the
/// overwhelming majority of blocks, which declare no types.
/// </param>
public readonly record struct ScriptSourceMap(
    int PreludeLines,
    ImmutableArray<ScriptSourceSegment> Hoisted = default)
{
    /// <summary>The user's line for a line in the generated source.</summary>
    /// <param name="generatedLine">A one-based line number in the generated source.</param>
    /// <returns>
    /// The one-based line in the user's script, or 0 when the position is inside the generated
    /// frame rather than inside the script.
    /// </returns>
    /// <remarks>
    /// <b>Zero rather than a clamp.</b> A diagnostic that really is on a generated line — a
    /// declaration for a port whose wired type cannot be assigned, say — must not be reported as
    /// though it were on the user's first line, which would send them to look at code that is
    /// correct.
    /// </remarks>
    public int UserLine(int generatedLine)
    {
        if (Segment(generatedLine) is { } segment)
        {
            return segment.UserStart + (generatedLine - segment.GeneratedStart);
        }

        int line = generatedLine - PreludeLines;

        return line > 0 ? line : 0;
    }

    /// <summary>
    /// Whether a generated line is inside a declaration the wrapper moved (`E6-T34`).
    /// </summary>
    /// <param name="generatedLine">A one-based line number in the generated source.</param>
    /// <returns>True when the line came from a hoisted type declaration.</returns>
    /// <remarks>
    /// <b>Asked by input inference, and for a reason worth stating.</b> An input port is an
    /// identifier the block expects from outside, found by compiling with nothing declared and
    /// reading the <c>CS0103</c>s. A type declared in the block is not inside the entry point and
    /// cannot see its locals, so an unresolved name in a class body is the user's own typo — and
    /// turning it into a port would answer a spelling mistake with a mysterious extra socket.
    /// </remarks>
    public bool IsHoisted(int generatedLine) => Segment(generatedLine) is not null;

    /// <summary>Prefixes a compiler message with the user's own line, when it has one.</summary>
    /// <param name="generatedLine">A one-based line number in the generated source.</param>
    /// <param name="column">The one-based column, which is already the user's.</param>
    /// <param name="message">The compiler's message.</param>
    /// <returns>The message, placed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public string Place(int generatedLine, int column, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        int line = UserLine(generatedLine);

        return line == 0
            ? message
            : string.Format(CultureInfo.InvariantCulture, "line {0}, column {1}: {2}", line, column, message);
    }

    private ScriptSourceSegment? Segment(int generatedLine)
    {
        if (Hoisted.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (ScriptSourceSegment segment in Hoisted)
        {
            if (generatedLine >= segment.GeneratedStart && generatedLine < segment.GeneratedStart + segment.Lines)
            {
                return segment;
            }
        }

        return null;
    }
}

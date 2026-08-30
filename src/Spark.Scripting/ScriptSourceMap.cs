using System;
using System.Globalization;

namespace Spark.Scripting;

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
/// <b>The map is a subtraction, and it is only that because two other decisions were made to keep
/// it one.</b> The wrapper puts every line it adds <i>before</i> the user's first, so the offset is
/// constant rather than a table; and <see cref="GuardWeaver"/> weaves statements with no trivia at
/// all, so rewriting adds no lines either (`E6-T4`). Give either of those up and this becomes a
/// list of ranges that has to be maintained.
/// </para>
/// <para>
/// <b>Columns are not mapped, deliberately.</b> Nothing the wrapper adds is on a user line, so a
/// column is already the user's column — and a map that adjusted them would be adjusting them by
/// zero, which is a claim with no meaning attached.
/// </para>
/// </remarks>
/// <param name="PreludeLines">
/// How many lines the generated frame puts before the user's first line.
/// </param>
public readonly record struct ScriptSourceMap(int PreludeLines)
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
        int line = generatedLine - PreludeLines;

        return line > 0 ? line : 0;
    }

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
}

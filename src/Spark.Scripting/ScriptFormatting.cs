using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spark.Scripting;

/// <summary>
/// Tidies a code block's text, so a block that was typed on one line reads as several
/// (<c>E8-T84</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>add line breaks automatically when one clicks out of a code
/// block, or perform an auto-formatting.</i> They had typed <c>3;20;</c> — two statements sharing a
/// line, which is legal C# and unreadable, and which the block's own message about ports does not
/// help with because it is answering a different question.
/// </para>
/// <para>
/// <b>This cannot change a block's ports, and that was checked before it was written.</b> A code
/// block's outputs come from its <c>LocalDeclarationStatementSyntax</c> nodes — the <c>var</c>
/// lines — and its inputs from the identifiers it uses without declaring. Neither is a fact about
/// line breaks, so reformatting moves text and nothing else. **A wire cannot break because somebody
/// tidied a block.**
/// </para>
/// <para>
/// <b>It parses as a compilation unit rather than as a list of statements.</b> A block may open
/// with <c>using</c> directives — <c>E6-T39</c> hoists them to namespace scope so a block can reach
/// a library whose namespace was refused — and a <c>using</c> directive is not a statement. Parsed
/// as a body it would be an error; parsed as a file it is exactly what it looks like.
/// </para>
/// <para>
/// <b>Text that does not parse is returned untouched, byte for byte.</b> Formatting is offered when
/// a block loses focus, which is a moment that arrives in the middle of half-finished work — an
/// editor that rearranges a broken block is fighting the person trying to fix it.
/// </para>
/// </remarks>
public static class ScriptFormatting
{
    /// <summary>Formats a code block's source.</summary>
    /// <param name="source">The block's text.</param>
    /// <returns>
    /// The formatted text, or <paramref name="source"/> unchanged when it is null, blank, or does
    /// not parse.
    /// </returns>
    /// <remarks>
    /// <b>Idempotent</b>: formatting formatted text returns it unchanged, so a block does not become
    /// modified merely by being clicked into and out of again.
    /// </remarks>
    public static string Format(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return source ?? string.Empty;
        }

        try
        {
            CompilationUnitSyntax parsed = SyntaxFactory.ParseCompilationUnit(source);

            // A parse error means the text is not C# yet. Anything from a half-typed expression to
            // a missing brace lands here, and every one of them is a reason to leave the text alone
            // rather than to guess at what it was going to be.
            foreach (Diagnostic diagnostic in parsed.GetDiagnostics())
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    return source;
                }
            }

            string formatted = parsed.NormalizeWhitespace(indentation: "    ", eol: "\n").ToFullString();

            // `NormalizeWhitespace` writes no trailing newline and Spark's editor does not want one;
            // the leading blank a compilation unit sometimes carries is not wanted either.
            return formatted.Trim('\n', '\r');
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException)
        {
            // Roslyn's own refusal to normalise something it parsed. Rare, and the answer is the
            // same as a parse error: the user's text is theirs, and a formatter that eats it is
            // worse than one that does nothing.
            return source;
        }
    }

    /// <summary>
    /// Tidies the completed lines above a caret, leaving the line the caret is on alone
    /// (<c>E8-T84</c>).
    /// </summary>
    /// <param name="source">The block's whole text.</param>
    /// <param name="caret">Where the caret is, as an offset into <paramref name="source"/>.</param>
    /// <param name="moved">Where that same caret is in the returned text.</param>
    /// <returns>The text, tidied above the caret's line, or <paramref name="source"/> unchanged.</returns>
    /// <remarks>
    /// <para>
    /// <b>This exists because formatting the whole document on <kbd>Enter</kbd> deletes the
    /// <kbd>Enter</kbd>.</b> <see cref="Format"/> trims trailing newlines — deliberately, because a
    /// block does not want one — so tidying the entire text at the instant a newline is typed at
    /// the end of it takes that newline straight back out. The feature would undo the keystroke
    /// that triggered it, which is the worst behaviour an editor can have: it fights the person
    /// using it, on every line.
    /// </para>
    /// <para>
    /// <b>So the caret's own line is the boundary and is never touched.</b> Everything above it is
    /// finished work and is fair game; the line the caret sits on is being typed and is not. The
    /// newline between the two halves is put back explicitly rather than left to survive, which is
    /// what makes the round trip exact.
    /// </para>
    /// <para>
    /// <b>An unfinished block above the caret is left alone for free.</b> A half-written
    /// <c>if (x) {</c> makes the text above the caret fail to parse, and <see cref="Format"/>
    /// already returns unparseable text untouched — so the guard that stops this mangling an open
    /// brace is one that was written for a different reason and needed no extension.
    /// </para>
    /// <para>
    /// <b>The caret moves by the length difference, not by being re-found.</b> Only the text above
    /// its line changed, so everything from the caret onwards is the same string at a new offset —
    /// which is exact, where searching for the caret's surroundings would guess.
    /// </para>
    /// </remarks>
    public static string FormatAbove(string? source, int caret, out int moved)
    {
        string text = source ?? string.Empty;

        moved = Math.Clamp(caret, 0, text.Length);

        int lineStart = LineStartAt(text, moved);

        if (lineStart <= 0)
        {
            // The caret is on the first line, so nothing above it is finished.
            return text;
        }

        // The newline itself is not part of what gets formatted, and is restored below.
        string above = text[..lineStart].TrimEnd('\n', '\r');
        string tidied = Format(above);

        if (string.Equals(above, tidied, StringComparison.Ordinal))
        {
            return text;
        }

        moved += tidied.Length - above.Length;

        return tidied + "\n" + text[lineStart..];
    }

    /// <summary>
    /// Tidies the whole block and reports where the caret ended up (<c>E8-T84</c>).
    /// </summary>
    /// <param name="source">The block's whole text.</param>
    /// <param name="caret">Where the caret is, as an offset into <paramref name="source"/>.</param>
    /// <param name="moved">Where that same caret is in the returned text.</param>
    /// <returns>The formatted text, or <paramref name="source"/> unchanged.</returns>
    /// <remarks>
    /// <para>
    /// <b>What <kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> runs</b> — VS Code's Format Document
    /// gesture, which is what anybody who opens this editor will try. Unlike
    /// <see cref="FormatAbove"/> it touches the line the caret is on, because the user asked for
    /// the whole block rather than for the editor to tidy up after them.
    /// </para>
    /// <para>
    /// <b>The caret is mapped by counting non-whitespace characters, and that mapping is exact
    /// rather than approximate.</b> Formatting rewrites whitespace and nothing else — Roslyn's
    /// <c>NormalizeWhitespace</c> preserves every token's text and their order — so the sequence
    /// of non-whitespace characters is the same string before and after. Counting how many of them
    /// precede the caret, then walking that many into the formatted text, therefore lands on the
    /// same character of the same token. Clamping the old offset into the new length, which is the
    /// obvious thing to do, moves the caret by however much the indentation changed above it.
    /// </para>
    /// </remarks>
    public static string FormatDocument(string? source, int caret, out int moved)
    {
        string text = source ?? string.Empty;
        int at = Math.Clamp(caret, 0, text.Length);

        string formatted = Format(text);

        if (string.Equals(text, formatted, StringComparison.Ordinal))
        {
            moved = at;

            return text;
        }

        int solid = 0;
        for (int i = 0; i < at; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                solid++;
            }
        }

        moved = OffsetAfter(formatted, solid);

        return formatted;
    }

    /// <summary>The offset just past the <i>n</i>th non-whitespace character.</summary>
    /// <param name="text">The text to walk.</param>
    /// <param name="count">How many non-whitespace characters to pass.</param>
    /// <returns>The offset, or the end of the text when there are fewer than that many.</returns>
    /// <remarks>
    /// <b>Zero means the start, not the first non-whitespace character.</b> A caret at the top of
    /// a block has passed nothing, and answering with the first token's offset would silently skip
    /// leading indentation the user may be about to type into.
    /// </remarks>
    private static int OffsetAfter(string text, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        int seen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                continue;
            }

            seen++;

            if (seen == count)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    /// <summary>The offset of the start of the line an offset falls on.</summary>
    /// <param name="text">The text.</param>
    /// <param name="offset">An offset into it.</param>
    /// <returns>The offset just after the previous line break, or zero.</returns>
    /// <remarks>
    /// Both line endings, because the document may hold either: a block typed here is <c>\n</c>
    /// and one pasted from elsewhere is often <c>\r\n</c>, and a search for only one of them puts
    /// the boundary one character out on the other.
    /// </remarks>
    private static int LineStartAt(string text, int offset)
    {
        for (int i = offset - 1; i >= 0; i--)
        {
            if (text[i] is '\n' or '\r')
            {
                return i + 1;
            }
        }

        return 0;
    }
}

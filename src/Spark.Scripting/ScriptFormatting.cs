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
}

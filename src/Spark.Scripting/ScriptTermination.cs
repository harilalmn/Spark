using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spark.Scripting;

/// <summary>
/// Puts the semicolon back on a code block's last statement when the user left it off (`E6-T33`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> A Dynamo code block is a list of expressions and the semicolon is
/// the only punctuation in it, so it is the one character people leave off — and the cost of
/// leaving it off is not a squiggle, it is a node whose ports vanish. `E6-T26` gives a block one
/// output port per variable it declares, and a source that does not parse declares nothing, so the
/// block loses its outputs and takes its wires with it. Typing the last line and clicking away is
/// the commonest gesture there is; it should not be the one that costs the wiring.
/// </para>
/// <para>
/// <b>It runs on commit, not on every keystroke.</b> An editor that inserted a semicolon while the
/// caret was still on the line would fight the person typing — they are mid-statement by
/// definition. Clicking out of the block is the moment the user has said they are finished with it,
/// which is the only moment the missing character is unambiguous.
/// </para>
/// <para>
/// <b>The parser decides, not a string test.</b> "Ends with <c>;</c> or <c>}</c>" is the obvious
/// rule and it is wrong in both directions: it appends to <c>a + b // note</c> after the comment,
/// where the semicolon has to go before it, and it appends to <c>if (x) {</c>, where nothing is
/// missing but a brace. Roslyn already knows which token is absent, so this asks it.
/// </para>
/// <para>
/// <b>And it refuses unless it helps.</b> A candidate that does not lower the error count is
/// discarded, which is what stops a half-typed block from being "corrected" into a differently
/// broken one. A tidy-up that can make things worse is not a tidy-up.
/// </para>
/// </remarks>
public static class ScriptTermination
{
    /// <summary>
    /// Adds the semicolon the last statement is missing, if one is missing and adding it helps.
    /// </summary>
    /// <param name="script">The source as the editor holds it. Null and blank are returned as they came.</param>
    /// <returns>
    /// The source, with one <c>;</c> inserted after the final statement's last token — or the very
    /// same string when nothing was missing, when the source is not a statement, or when the
    /// insertion would not have reduced the number of errors.
    /// </returns>
    /// <remarks>
    /// <b>Inserted before the trailing trivia rather than at the end of the text</b>, which is the
    /// whole difference between <c>a + b; // note</c> and <c>a + b // note;</c>. The last
    /// <i>token</i> of the statement is where the statement ends; everything after it is whitespace
    /// and comments, and a semicolon written past those is inside the comment.
    /// </remarks>
    public static string Terminate(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return script;
        }

        CompilationUnitSyntax root = SyntaxFactory.ParseCompilationUnit(script);

        if (root.Members.Count == 0
            || root.Members[^1] is not GlobalStatementSyntax global
            || !IsMissingSemicolon(global.Statement))
        {
            return script;
        }

        // Zero-width tokens are the missing ones the parser invented, and inserting after one of
        // those would be inserting at a position nothing was ever typed at.
        SyntaxToken last = global.Statement.GetLastToken(includeZeroWidth: false);

        if (last.IsKind(SyntaxKind.None) || last.IsMissing)
        {
            return script;
        }

        int at = last.Span.End;

        if (at <= 0 || at > script.Length)
        {
            return script;
        }

        string candidate = string.Concat(script.AsSpan(0, at), ";", script.AsSpan(at));

        return Errors(SyntaxFactory.ParseCompilationUnit(candidate)) < Errors(root)
            ? candidate
            : script;
    }

    /// <summary>Whether a statement is one that ends in a semicolon, and has lost it.</summary>
    /// <param name="statement">The last statement in the block.</param>
    /// <returns>True when its semicolon token exists in the grammar and is missing from the text.</returns>
    /// <remarks>
    /// <b>A closed list rather than a search for a semicolon-shaped child.</b> These are the
    /// statements a code block's last line is in practice — an expression, a declaration, and the
    /// two jumps somebody writes at the end of one. A block statement, an <c>if</c> or a
    /// <c>foreach</c> has no semicolon to be missing, and must not acquire one.
    /// </remarks>
    private static bool IsMissingSemicolon(StatementSyntax statement) => statement switch
    {
        ExpressionStatementSyntax expression => expression.SemicolonToken.IsMissing,
        LocalDeclarationStatementSyntax declaration => declaration.SemicolonToken.IsMissing,
        ReturnStatementSyntax returned => returned.SemicolonToken.IsMissing,
        ThrowStatementSyntax thrown => thrown.SemicolonToken.IsMissing,
        _ => false,
    };

    /// <summary>How many errors a parse produced.</summary>
    /// <param name="root">The parsed source.</param>
    /// <returns>The count of diagnostics at <see cref="DiagnosticSeverity.Error"/>.</returns>
    private static int Errors(CompilationUnitSyntax root)
    {
        int errors = 0;

        foreach (Diagnostic diagnostic in root.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                errors++;
            }
        }

        return errors;
    }
}

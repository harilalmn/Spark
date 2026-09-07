using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spark.Scripting;

/// <summary>
/// A script with its range markers found: Dynamo's <c>#</c> blanked to a space, and the offset it
/// stood at written down (<c>E10-T15</c>).
/// </summary>
/// <param name="Text">
/// The script, character for character the same length as what the user typed.
/// </param>
/// <param name="Markers">
/// Where a <c>#</c> was, ascending. Empty for the overwhelming majority of scripts, which contain
/// none.
/// </param>
public readonly record struct BlankedScript(string Text, ImmutableArray<int> Markers)
{
    /// <summary>Whether the script contained a range marker at all.</summary>
    public bool HasMarkers => !Markers.IsDefaultOrEmpty;
}

/// <summary>
/// Lets a code block write Dynamo's three range forms — <c>0..1..0.25</c>, <c>0..1..#5</c> and
/// <c>0..#5..1</c> — and lowers them onto <see cref="Spark.Api.NumberRange"/> (<c>E10-T15</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot be a syntax rewriter on its own, which is the whole shape of the file.</b>
/// <c>#</c> does not break a rule about expressions; it breaks the <i>lexer</i>. A code block
/// containing <c>0..1..#5</c> reports <i>"Preprocessor directives must appear as the first
/// non-whitespace character on a line"</i>, and there is no usable syntax tree to rewrite — so the
/// marker has to go before the parse, in text. The <i>lowering</i> is a tree rewrite, because that
/// is the only place the operands are real expressions rather than a regular expression's guess.
/// Two stages, and each is the only one that can do its half.
/// </para>
/// <para>
/// <b>The text stage is length-preserving, and that is not tidiness.</b> A <c>#</c> becomes a
/// single space, so every character after it keeps the offset it had. That matters most for
/// <see cref="ScriptCompletion"/>, which resolves the caret by a flat character offset and which
/// does <i>not</i> run the lowering below — so a marker anywhere in the block leaves every
/// completion offset exactly where it was.
/// </para>
/// <para>
/// <b>The tree stage does move columns, on the line it rewrites, and that is worth saying out
/// loud.</b> The lowered call is longer than the range it replaces, so an error further along the
/// <i>same</i> line is reported at a column that is not the user's. Lines are untouched — every
/// token this adds carries no trivia — which is the property <see cref="ScriptSourceMap"/> actually
/// maps. <see cref="GuardWeaver"/> has done exactly this since `E6-T4`: a <c>;</c> at column 37
/// after a <c>while</c> loop is already reported at column 87. One shared wart, one shared fix if
/// it is ever worth making, written down as <c>N122</c>.
/// </para>
/// <para>
/// <b>Three-part ranges are unambiguous, which is what makes this safe.</b> Two-part <c>0..1</c> is
/// legal C# — <c>arr[1..^1]</c> is real code and still compiles untouched — but a <c>Range</c> has
/// no <c>..</c> operator, so <c>a..b..c</c> can never bind in C# and cannot be anything else's.
/// Verified rather than assumed: <c>0..1..0.25</c> reaches the binder today and fails there with
/// <i>"Cannot implicitly convert type 'System.Range' to 'System.Index'"</i>.
/// </para>
/// <para>
/// <b>Strings and comments are left alone, and Roslyn is what decides which is which</b> rather
/// than a hand-written scanner that would have to know about verbatim, interpolated and raw string
/// literals. Blanking a <c>#</c> can never move where a literal or a comment begins or ends —
/// <c>#</c> is not a delimiter of either — so the blanked text can be parsed and asked, and the
/// answer holds for the original.
/// </para>
/// </remarks>
public static class ScriptRanges
{
    private const char Marker = '#';

    /// <summary>The type every lowered form is a call on.</summary>
    /// <remarks>
    /// Fully qualified with <c>global::</c> so that a user's own <c>NumberRange</c> — a local, a
    /// using alias, a type from a package — cannot capture the call the compiler was going to make
    /// on their behalf.
    /// </remarks>
    private const string Target = "global::Spark.Api.NumberRange.";

    /// <summary>
    /// Blanks every range marker in a script, leaving the ones inside strings and comments alone.
    /// </summary>
    /// <param name="script">The script, as the user typed it.</param>
    /// <returns>The blanked script and the offsets the markers stood at.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    public static BlankedScript Blank(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (script.IndexOf(Marker, StringComparison.Ordinal) < 0)
        {
            // The overwhelming majority of scripts, and they do not pay for a parse.
            return new BlankedScript(script, ImmutableArray<int>.Empty);
        }

        char[] candidate = script.ToCharArray();
        List<int> found = [];

        for (int index = 0; index < candidate.Length; index++)
        {
            if (candidate[index] == Marker)
            {
                candidate[index] = ' ';
                found.Add(index);
            }
        }

        // Every marker is blank in here, so no bad directive is left to swallow the rest of a line
        // and hide the marker after it. One parse answers for all of them.
        SyntaxNode probe = CSharpSyntaxTree.ParseText(new string(candidate)).GetRoot();

        ImmutableArray<int>.Builder markers = ImmutableArray.CreateBuilder<int>();

        foreach (int index in found)
        {
            if (IsInsideTextOrComment(probe, index))
            {
                candidate[index] = Marker;
                continue;
            }

            markers.Add(index);
        }

        return new BlankedScript(new string(candidate), markers.ToImmutable());
    }

    /// <summary>
    /// Lowers every three-part range in a tree onto <see cref="Spark.Api.NumberRange"/>.
    /// </summary>
    /// <param name="root">The parsed generated source.</param>
    /// <param name="markers">
    /// Marker offsets <b>in this tree's coordinates</b> — a script offset from
    /// <see cref="Blank"/> plus wherever the script was placed in the generated source.
    /// </param>
    /// <returns>The tree, with three-part ranges replaced by calls.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public static SyntaxNode Lower(SyntaxNode root, ImmutableArray<int> markers)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new Lowering(markers).Visit(root);
    }

    private static bool IsInsideTextOrComment(SyntaxNode root, int position)
    {
        SyntaxKind trivia = root.FindTrivia(position).Kind();

        if (trivia is SyntaxKind.SingleLineCommentTrivia
            or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia
            or SyntaxKind.MultiLineDocumentationCommentTrivia)
        {
            return true;
        }

        // A literal's *content*, not the expression: the text of a string, a character, the literal
        // halves of an interpolated string. Code inside an interpolation hole is code and is
        // rewritten like any other, which is why this asks about the token rather than the span of
        // the enclosing expression.
        return root.FindToken(position, findInsideTrivia: true).Kind()
            is SyntaxKind.StringLiteralToken
            or SyntaxKind.CharacterLiteralToken
            or SyntaxKind.InterpolatedStringTextToken
            or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.MultiLineRawStringLiteralToken
            or SyntaxKind.Utf8StringLiteralToken
            or SyntaxKind.Utf8SingleLineRawStringLiteralToken
            or SyntaxKind.Utf8MultiLineRawStringLiteralToken
            or SyntaxKind.XmlTextLiteralToken;
    }

    /// <summary>The tree half: <c>((a..b)..c)</c> becomes one call.</summary>
    /// <remarks>
    /// <b>Every token this builds carries no trivia</b>, and the operands keep their own, so the
    /// rewrite adds no lines — the same rule <see cref="GuardWeaver"/> follows and for the same
    /// reason. <see cref="ScriptSourceMap"/> stays a subtraction.
    /// </remarks>
    private sealed class Lowering(ImmutableArray<int> markers) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitRangeExpression(RangeExpressionSyntax node)
        {
            // Children first, so an inner range is settled before the outer one reads it. A plain
            // two-part range is left exactly as it was, which is what keeps `arr[1..^1]` working.
            SyntaxNode? visited = base.VisitRangeExpression(node);

            if (visited is not RangeExpressionSyntax outer
                || outer.LeftOperand is not RangeExpressionSyntax inner
                || outer.RightOperand is not { } third
                || inner.LeftOperand is not { } first
                || inner.RightOperand is not { } second)
            {
                return visited;
            }

            bool countIsSecond = MarkerBefore(inner.OperatorToken, second);
            bool countIsThird = MarkerBefore(outer.OperatorToken, third);

            if (countIsSecond && countIsThird)
            {
                // `0..#5..#5` means nothing. Left alone so the compiler reports it, rather than
                // silently picking one of the two readings.
                return outer;
            }

            string method = countIsSecond ? "ByCountAndStep" : countIsThird ? "ByCount" : "ByStep";

            return Call(method, first, second, third)
                .WithLeadingTrivia(outer.GetLeadingTrivia())
                .WithTrailingTrivia(outer.GetTrailingTrivia());
        }

        /// <summary>Whether a marker sits in the gap between a <c>..</c> and the operand after it.</summary>
        /// <remarks>
        /// The gap rather than the operand's own start, so <c>0..1..# 5</c> — a space after the
        /// marker — reads the same as <c>0..1..#5</c>. Nothing but whitespace can be in that gap:
        /// the marker was blanked to a space and the parser put everything else in a token.
        /// </remarks>
        private bool MarkerBefore(SyntaxToken dots, ExpressionSyntax operand)
        {
            foreach (int marker in markers)
            {
                if (marker >= dots.Span.End && marker < operand.SpanStart)
                {
                    return true;
                }
            }

            return false;
        }

        private static InvocationExpressionSyntax Call(
            string method, ExpressionSyntax first, ExpressionSyntax second, ExpressionSyntax third) =>
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression(Target + method),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList<ArgumentSyntax>(
                    [
                        SyntaxFactory.Argument(first.WithoutLeadingTrivia()),
                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                        SyntaxFactory.Argument(second),
                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                        SyntaxFactory.Argument(third.WithoutTrailingTrivia()),
                    ])));
    }
}

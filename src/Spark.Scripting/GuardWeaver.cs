using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Spark.Scripting;

/// <summary>
/// Works out where a <see cref="ScriptGuard"/> call has to go in a code block, and says so as a list
/// of <see cref="SourceInjection"/> the rewriter applies while copying.
/// </summary>
/// <remarks>
/// Two places, for the two ways a code block takes the process with it. Every loop body gets a
/// <see cref="ScriptGuard.Tick"/>, so a loop that never ends can be stopped. Everything the script
/// declares that can call itself gets a <see cref="ScriptGuard.Enter"/>, so recursion that never
/// bottoms out is usually caught while there is still stack to unwind — a real stack overflow cannot
/// be caught at all, it ends the process.
/// </remarks>
internal static class GuardWeaver
{
    /// <summary>Fully qualified and <c>global::</c>-rooted: a script may declare any name it likes.</summary>
    private const string Tick = "global::Spark.Scripting.ScriptGuard.Tick();";

    private const string Enter = "global::Spark.Scripting.ScriptGuard.Enter();";

    private const string OpenAndTick = "{" + Tick;
    private const string OpenAndEnter = "{" + Enter;
    private const string OpenAndEnterReturning = "{" + Enter + "return ";
    private const string Close = "}";

    /// <summary>What to weave into a file, and what to blank out of it first.</summary>
    /// <param name="Injections">The fragments to insert, in the order they must be applied.</param>
    /// <param name="Blanks">
    /// Spans to replace with spaces rather than remove, so that every offset and every line number in
    /// the copy still matches the file on screen. It is what lets an expression body become a block:
    /// the <c>=&gt;</c> has to go, and going quietly is the only option.
    /// </param>
    internal readonly record struct Plan(IReadOnlyList<SourceInjection> Injections, IReadOnlyList<TextSpan> Blanks);

    /// <summary>Every guard insertion for a parsed code block.</summary>
    /// <param name="root">The parsed user text.</param>
    /// <returns>The plan.</returns>
    internal static Plan For(SyntaxNode? root)
    {
        if (root is null)
        {
            return new Plan([], []);
        }

        List<SourceInjection> injections = [];
        List<TextSpan> blanks = [];

        foreach (SyntaxNode node in root.DescendantNodesAndSelf())
        {
            StatementSyntax? loopBody = LoopBodyOf(node);

            if (loopBody is not null)
            {
                Wrap(injections, node, loopBody, OpenAndTick, Tick);
                continue;
            }

            Callable(injections, blanks, node);
        }

        return new Plan(injections, blanks);
    }

    /// <summary>
    /// Puts a guard at the top of a body, giving it a block of its own if it has not got one.
    /// <c>while (x) DoThing();</c> is not guardable without that.
    /// </summary>
    private static void Wrap(
        List<SourceInjection> injections, SyntaxNode owner, SyntaxNode body, string open, string inside)
    {
        if (body is BlockSyntax block)
        {
            injections.Add(new SourceInjection(block.OpenBraceToken.Span.End, owner.SpanStart, inside));
            return;
        }

        injections.Add(new SourceInjection(body.SpanStart, owner.SpanStart, open));

        // Span, not FullSpan: a trailing `// comment` after the statement would otherwise swallow
        // the closing brace.
        injections.Add(new SourceInjection(body.Span.End, owner.SpanStart, Close));
    }

    /// <summary>
    /// Guards entry to anything a code block declares that can call itself.
    /// </summary>
    /// <remarks>
    /// A property or indexer written as <c>int P =&gt; ...</c> is left alone: turning that into a
    /// block means writing a <c>get</c> accessor around it, which is a great deal of surgery for a
    /// way of recursing nobody reaches for. Its accessors are guarded when it has them, which is the
    /// form anyone writing a recursive property would use anyway.
    /// </remarks>
    private static void Callable(List<SourceInjection> injections, List<TextSpan> blanks, SyntaxNode node)
    {
        switch (node)
        {
            case MethodDeclarationSyntax method:
                Body(injections, blanks, method, method.Body, method.ExpressionBody,
                    method.SemicolonToken, Returns(method.ReturnType));
                return;

            case LocalFunctionStatementSyntax local:
                Body(injections, blanks, local, local.Body, local.ExpressionBody,
                    local.SemicolonToken, Returns(local.ReturnType));
                return;

            case ConstructorDeclarationSyntax constructor:
                Body(injections, blanks, constructor, constructor.Body, constructor.ExpressionBody,
                    constructor.SemicolonToken, false);
                return;

            case AccessorDeclarationSyntax accessor:
                Body(injections, blanks, accessor, accessor.Body, accessor.ExpressionBody,
                    accessor.SemicolonToken, accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                return;

            // A lambda can recurse through the variable holding it. Only the ones already written as
            // a block are guarded; giving an expression lambda a block would mean deciding its
            // return type, which here is not written down anywhere.
            case AnonymousFunctionExpressionSyntax lambda when lambda.Block is not null:
                Wrap(injections, lambda, lambda.Block, OpenAndEnter, Enter);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// One body. A block takes the call after its brace; an expression body has to become a block,
    /// which means blanking the arrow and closing after the semicolon.
    /// </summary>
    private static void Body(
        List<SourceInjection> injections,
        List<TextSpan> blanks,
        SyntaxNode owner,
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? arrow,
        SyntaxToken semicolon,
        bool returns)
    {
        if (block is not null)
        {
            Wrap(injections, owner, block, OpenAndEnter, Enter);
            return;
        }

        if (arrow?.Expression is null || semicolon.IsKind(SyntaxKind.None))
        {
            return;
        }

        // `return throw new X();` is not C#; a throw expression stands on its own.
        bool value = returns && !arrow.Expression.IsKind(SyntaxKind.ThrowExpression);

        blanks.Add(arrow.ArrowToken.Span);

        injections.Add(new SourceInjection(
            arrow.ArrowToken.SpanStart, owner.SpanStart, value ? OpenAndEnterReturning : OpenAndEnter));

        injections.Add(new SourceInjection(semicolon.Span.End, owner.SpanStart, Close));
    }

    /// <summary>Whether a body has to produce a value, decided from the written return type.</summary>
    private static bool Returns(TypeSyntax returnType) =>
        returnType is not PredefinedTypeSyntax predefined || !predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    /// <summary>
    /// The body of anything that can run its body more than once. A backwards <c>goto</c> is a loop
    /// too, and is not covered — nothing marks where it begins.
    /// </summary>
    private static StatementSyntax? LoopBodyOf(SyntaxNode node) => node switch
    {
        WhileStatementSyntax loop => loop.Statement,
        DoStatementSyntax loop => loop.Statement,
        ForStatementSyntax loop => loop.Statement,
        ForEachStatementSyntax loop => loop.Statement,
        ForEachVariableStatementSyntax loop => loop.Statement,
        _ => null,
    };
}

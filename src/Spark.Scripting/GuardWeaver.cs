using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Spark.Scripting;

/// <summary>
/// Rewrites a generated script so that its loops can be cancelled and its recursion is bounded
/// (`E6-T4`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing the C# compiler emits checks a cancellation token on its own.</b> `E6-T17` cut the
/// channel — the generated entry point is
/// <c>Run(object[] __in, CancellationToken __token)</c> — and on its own that only stops a script
/// that has not started yet. A script already inside <c>while (true) { }</c> hangs the evaluation
/// thread, and there is nothing to interrupt it with: .NET has no safe thread abort. The only place
/// a check can go is inside the loop, and the only moment it can be put there is before the code is
/// compiled. That is what this does.
/// </para>
/// <para>
/// <b>Four rewrites, and each is the smallest that works.</b> Every <c>for</c>, <c>foreach</c>,
/// <c>while</c> and <c>do</c> body gains a leading <see cref="ScriptGuard.Tick"/>, which both tests
/// the token and counts; every <c>goto</c> gains one too, because a label and a jump are the one
/// other way to write an unbounded loop in C#; every local function is bracketed with
/// <see cref="ScriptGuard.Enter"/> and <see cref="ScriptGuard.Exit"/>, so recursion ends in a
/// diagnostic rather than in a <see cref="StackOverflowException"/> — which cannot be caught in
/// .NET and would take the application down with it; and every <c>static</c> modifier on a local
/// function or lambda is removed, because a woven check reads <c>__token</c> and a <c>static</c>
/// local function is precisely the thing that may not capture it.
/// </para>
/// <para>
/// <b>Every woven statement is written without trivia, on purpose.</b> A rewrite that inserted
/// lines would move every line of the user's script relative to the tree the compiler sees, and the
/// compiler's diagnostics are the only thing a user has to find their typo with. Keeping the line
/// count identical means a diagnostic's line number is still the user's line number plus a constant
/// prelude — exactly the property `E6-T1`'s source map needs, and far cheaper to preserve now than
/// to reconstruct later.
/// </para>
/// <para>
/// <b>What this deliberately does not bound, and why.</b> Recursion expressed through an
/// expression-bodied lambda — <c>Func&lt;int, int&gt; f = null; f = n =&gt; n &lt;= 0 ? 0 :
/// f(n - 1);</c> — is not guarded. Bracketing a body with <c>try</c>/<c>finally</c> means turning it
/// into a block, and that needs to know whether the lambda returns a value, which a lambda does not
/// say and only the semantic model knows. A local function does state its return type, which is why
/// it is covered and a lambda is not. Recursion through a method in a library the script calls is
/// not bounded either, and cannot be: it is not our code. Both are stated here rather than
/// discovered, and both leave <c>R11</c> exactly where the PRD puts it.
/// </para>
/// </remarks>
public sealed class GuardWeaver : CSharpSyntaxRewriter
{
    /// <summary>The parameter a woven cancellation check reads.</summary>
    /// <remarks>
    /// Named with a double underscore for the same reason the rest of the generated frame is: a
    /// user's identifier cannot collide with it without the compiler saying so.
    /// </remarks>
    public const string TokenParameterName = "__token";

    private const string GuardType = "global::Spark.Scripting.ScriptGuard";

    private readonly string _tick;
    private readonly string _enter;
    private readonly string _exit;
    private readonly string _begin;

    /// <summary>Creates a weaver with the default ceilings.</summary>
    public GuardWeaver() : this(ScriptGuard.DefaultIterationLimit, ScriptGuard.DefaultDepthLimit)
    {
    }

    /// <summary>Creates a weaver with explicit ceilings.</summary>
    /// <param name="iterationLimit">The ceiling on loop iterations in one invocation.</param>
    /// <param name="depthLimit">The ceiling on recursion depth in one invocation.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either limit is not positive.</exception>
    /// <remarks>
    /// <b>The limits are woven in as literals rather than read from a setting at run time.</b> A
    /// compiled script therefore carries the ceiling it was compiled with, which is what stops a
    /// cached assembly from silently keeping an old limit after the setting changed — and it is why
    /// the limits belong in the compile-cache key.
    /// </remarks>
    public GuardWeaver(long iterationLimit, int depthLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterationLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depthLimit);

        IterationLimit = iterationLimit;
        DepthLimit = depthLimit;

        _tick = GuardType + ".Tick(" + TokenParameterName + ");";
        _enter = GuardType + ".Enter();";
        _exit = GuardType + ".Exit();";
        _begin = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Begin({1}L, {2});",
            GuardType,
            iterationLimit,
            depthLimit);
    }

    /// <summary>The ceiling this weaver writes on loop iterations.</summary>
    public long IterationLimit { get; }

    /// <summary>The ceiling this weaver writes on recursion depth.</summary>
    public int DepthLimit { get; }

    /// <summary>The source of the call that opens one invocation and resets its counters.</summary>
    /// <returns>A single C# statement, for a generator that builds text.</returns>
    public string BeginSource() => _begin;

    /// <summary>Weaves the guards into a generated compilation unit.</summary>
    /// <param name="root">The parsed generated source.</param>
    /// <returns>The rewritten root, with the same number of lines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public SyntaxNode Weave(SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Visit(root)!;
    }

    /// <inheritdoc/>
    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) =>
        Guarded((WhileStatementSyntax)base.VisitWhileStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b));

    /// <inheritdoc/>
    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
        Guarded((DoStatementSyntax)base.VisitDoStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b));

    /// <inheritdoc/>
    public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
        Guarded((ForStatementSyntax)base.VisitForStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b));

    /// <inheritdoc/>
    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
        Guarded((ForEachStatementSyntax)base.VisitForEachStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b));

    /// <inheritdoc/>
    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
        Guarded((ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b));

    /// <summary>Guards a backward jump, which is the other way to write a loop.</summary>
    /// <param name="node">The <c>goto</c>.</param>
    /// <returns>The jump preceded by a guard.</returns>
    /// <remarks>
    /// A weaver that only looked at loop keywords would leave <c>again: … goto again;</c>
    /// completely unguarded, and that is not an exotic thing to write — it is what a script
    /// translated from another language often looks like. Whether the jump goes backwards is not
    /// decidable from the statement alone, so every jump is guarded; a <c>goto case</c> inside a
    /// <c>switch</c> costs one counter increment it did not need, which is the right side of that
    /// trade.
    /// </remarks>
    public override SyntaxNode? VisitGotoStatement(GotoStatementSyntax node) =>
        SyntaxFactory.Block(Statement(_tick), (GotoStatementSyntax)base.VisitGotoStatement(node)!);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The recursion guard goes on local functions and only on local functions.</b> A code
    /// block's body is the body of one generated method, so a user who writes a helper writes a
    /// local function — the idiomatic route to recursion here, and the one route whose return type
    /// is stated in the syntax, which is what makes a <c>try</c>/<c>finally</c> rewrite possible
    /// without a semantic model.
    /// </remarks>
    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        LocalFunctionStatementSyntax visited = WithoutStatic((LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!);

        // An iterator or an async method returns to its caller long before its body finishes, so a
        // depth counter around the body would be counting something other than stack frames. Left
        // alone deliberately, rather than guarded wrongly.
        if (visited.Modifiers.Any(SyntaxKind.AsyncKeyword) || ContainsYield(visited))
        {
            return visited;
        }

        BlockSyntax? body = visited.Body;

        if (body is null)
        {
            if (visited.ExpressionBody is not { } arrow)
            {
                // Neither a body nor an expression body means `extern` or a syntax error. Either
                // way there is nothing here to guard.
                return visited;
            }

            ExpressionSyntax expression = arrow.Expression
                .WithoutTrivia()
                .WithLeadingTrivia(SyntaxFactory.Space);

            bool returnsValue = visited.ReturnType is not PredefinedTypeSyntax
            {
                Keyword.RawKind: (int)SyntaxKind.VoidKeyword,
            };

            body = SyntaxFactory.Block(returnsValue
                ? SyntaxFactory.ReturnStatement(expression)
                : SyntaxFactory.ExpressionStatement(expression));

            visited = visited
                .WithExpressionBody(null)
                .WithSemicolonToken(default);
        }

        return visited.WithBody(Bracketed(body)).WithTriviaFrom(node);
    }

    /// <inheritdoc/>
    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
        WithoutStatic((SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!);

    /// <inheritdoc/>
    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        WithoutStatic((ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!);

    /// <inheritdoc/>
    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
        WithoutStatic((AnonymousMethodExpressionSyntax)base.VisitAnonymousMethodExpression(node)!);

    /// <summary>
    /// Removes a <c>static</c> modifier, because a woven guard reads a captured parameter.
    /// </summary>
    /// <remarks>
    /// <b>This is the one rewrite that changes what the compiler would have said, and it is
    /// deliberate.</b> <c>static</c> on a lambda or a local function is a promise not to capture,
    /// and the guard's <c>__token</c> is a capture — so weaving into a <c>static</c> body turns a
    /// working script into <c>CS8421</c>, naming a parameter the user has never heard of. Dropping
    /// the modifier only widens what is legal: nothing that compiled before stops compiling, and the
    /// only thing lost is an allocation guarantee on a lambda whose enclosing method now allocates a
    /// closure anyway.
    /// </remarks>
    private static TNode WithoutStatic<TNode>(TNode node)
        where TNode : SyntaxNode
    {
        SyntaxTokenList modifiers = node switch
        {
            LocalFunctionStatementSyntax local => local.Modifiers,
            AnonymousFunctionExpressionSyntax anonymous => anonymous.Modifiers,
            _ => default,
        };

        if (!modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return node;
        }

        SyntaxToken keyword = modifiers.First(m => m.IsKind(SyntaxKind.StaticKeyword));
        SyntaxTokenList without = modifiers.Remove(keyword);

        SyntaxNode replaced = node switch
        {
            LocalFunctionStatementSyntax local => local.WithModifiers(without),
            AnonymousFunctionExpressionSyntax anonymous => anonymous.WithModifiers(without),
            _ => node,
        };

        // The modifier carried the construct's leading trivia — its indentation — so the trivia is
        // moved rather than deleted with it.
        return (TNode)replaced.WithLeadingTrivia(keyword.LeadingTrivia);
    }

    private static bool ContainsYield(SyntaxNode node)
    {
        return node
            .DescendantNodes(child => child is not LocalFunctionStatementSyntax
                && child is not AnonymousFunctionExpressionSyntax)
            .Any(descendant => descendant is YieldStatementSyntax);
    }

    /// <summary>Puts the loop guard at the top of a loop's body, making it a block if it is not.</summary>
    private TLoop Guarded<TLoop>(
        TLoop loop,
        Func<TLoop, StatementSyntax> body,
        Func<TLoop, StatementSyntax, TLoop> withBody)
        where TLoop : StatementSyntax
    {
        StatementSyntax existing = body(loop);

        // `while (Advance()) ;` is a legal loop with an empty statement for a body, and a perfectly
        // good way to hang the application. Turning it into a block is what lets a guard go into it
        // at all.
        BlockSyntax block = existing as BlockSyntax
            ?? SyntaxFactory.Block(existing is EmptyStatementSyntax
                ? default
                : SyntaxFactory.SingletonList(existing));

        return withBody(loop, block.WithStatements(block.Statements.Insert(0, Statement(_tick))));
    }

    /// <summary>Brackets a body with the depth counter, so the count unwinds however it leaves.</summary>
    /// <remarks>
    /// The <c>finally</c> is what makes this correct rather than approximately correct: a recursive
    /// function that throws out of the middle of itself must still put the depth back, or the next
    /// call in the same invocation starts from a count that never came down.
    /// </remarks>
    private BlockSyntax Bracketed(BlockSyntax body) =>
        SyntaxFactory.Block(
            Statement(_enter),
            SyntaxFactory.TryStatement(
                body.WithoutTrivia(),
                [],
                SyntaxFactory.FinallyClause(SyntaxFactory.Block(Statement(_exit)))));

    /// <summary>The statement the guard weaves, carrying no trivia at all.</summary>
    /// <remarks>
    /// <b>No trivia is the point.</b> A parsed statement arrives with elastic trivia that a
    /// formatter would turn into newlines, and a newline here moves every subsequent line of the
    /// user's script — which moves every compiler diagnostic off the line the user is looking at.
    /// </remarks>
    private static StatementSyntax Statement(string text) =>
        SyntaxFactory.ParseStatement(text).WithoutTrivia();
}

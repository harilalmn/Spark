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
/// <c>while</c> and <c>do</c> body gains a leading <see cref="ScriptGuard.Tick(System.Threading.CancellationToken)"/>, which both tests
/// the token and counts; every <c>goto</c> gains one too, because a label and a jump are the one
/// other way to write an unbounded loop in C#; every local function — and, since `E6-T34`, every
/// method, constructor, operator and property accessor of a type the block declares — is bracketed
/// with <see cref="ScriptGuard.Enter"/> and <see cref="ScriptGuard.Exit"/>, so recursion ends in a
/// diagnostic rather than in a <see cref="StackOverflowException"/> — which cannot be caught in
/// .NET and would take the application down with it; and every <c>static</c> modifier on a local
/// function or lambda is removed, because a woven check reads <c>__token</c> and a <c>static</c>
/// local function is precisely the thing that may not capture it.
/// </para>
/// <para>
/// <b>Where the guard cannot name <c>__token</c>, it reads the thread instead</b> (`E6-T34`). The
/// token is a parameter of the generated entry point, so a loop inside a type the block declares is
/// outside its scope — and weaving <c>Tick(__token)</c> there is <c>CS0103</c> about an identifier
/// the user never wrote. <see cref="ScriptGuard.Begin(long, int, System.Threading.CancellationToken)"/>
/// puts the token where the counters already live, on the thread, and
/// <see cref="ScriptGuard.Tick()"/> reads it back — so a declared type's loops are cancelled and
/// counted exactly like the block's own.
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
    private readonly string _tickOnThread;
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
        _tickOnThread = GuardType + ".Tick();";
        _enter = GuardType + ".Enter();";
        _exit = GuardType + ".Exit();";
        _begin = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Begin({1}L, {2}, {3});",
            GuardType,
            iterationLimit,
            depthLimit,
            TokenParameterName);
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
        Guarded((WhileStatementSyntax)base.VisitWhileStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b), Tick(node));

    /// <inheritdoc/>
    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
        Guarded((DoStatementSyntax)base.VisitDoStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b), Tick(node));

    /// <inheritdoc/>
    public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
        Guarded((ForStatementSyntax)base.VisitForStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b), Tick(node));

    /// <inheritdoc/>
    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) =>
        Guarded((ForEachStatementSyntax)base.VisitForEachStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b), Tick(node));

    /// <inheritdoc/>
    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node) =>
        Guarded((ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!, s => s.Statement, (s, b) => s.WithStatement(b), Tick(node));

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
        SyntaxFactory.Block(Statement(Tick(node)), (GotoStatementSyntax)base.VisitGotoStatement(node)!);

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The recursion guard goes on local functions and, since `E6-T34`, on the members of the
    /// types a block declares.</b> A code block's body is the body of one generated method, so a
    /// user who writes a helper writes a local function — the idiomatic route to recursion here, and
    /// the one route whose return type is stated in the syntax, which is what makes a
    /// <c>try</c>/<c>finally</c> rewrite possible without a semantic model. A declared type's
    /// methods state theirs too, and leaving them out would have made <c>R11</c> reachable again by
    /// the shortest script anybody could write: a class with a method that calls itself.
    /// </remarks>
    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        LocalFunctionStatementSyntax visited =
            WithoutStatic((LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!);

        return Bounded(
            visited,
            visited.Modifiers,
            Returns(visited.ReturnType),
            visited.Body,
            visited.ExpressionBody,
            (local, block) => local.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default))
            .WithTriviaFrom(node);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The generated entry point is the one method that is not bounded</b>, and it is recognised
    /// by the parameter it declares rather than by its name — <c>Run</c> is a name a user could give
    /// a method of their own, <c>__token</c> is not a parameter they can declare without the
    /// compiler saying so.
    /// </remarks>
    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        MethodDeclarationSyntax visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;

        return IsEntryPoint(node)
            ? visited
            : Bounded(
                visited,
                visited.Modifiers,
                Returns(visited.ReturnType),
                visited.Body,
                visited.ExpressionBody,
                (method, block) => method.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default));
    }

    /// <inheritdoc/>
    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        ConstructorDeclarationSyntax visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;

        return Bounded(
            visited,
            visited.Modifiers,
            returnsValue: false,
            visited.Body,
            visited.ExpressionBody,
            (constructor, block) =>
                constructor.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default));
    }

    /// <inheritdoc/>
    public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        OperatorDeclarationSyntax visited = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node)!;

        return Bounded(
            visited,
            visited.Modifiers,
            Returns(visited.ReturnType),
            visited.Body,
            visited.ExpressionBody,
            (declared, block) => declared.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>A property is where a stack overflow gets written by accident rather than on purpose</b> —
    /// <c>public int X =&gt; X;</c> is a typo, not a recursive algorithm, and without this it ends
    /// the application rather than the evaluation.
    /// </remarks>
    public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        AccessorDeclarationSyntax visited = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node)!;

        return Bounded(
            visited,
            visited.Modifiers,
            visited.IsKind(SyntaxKind.GetAccessorDeclaration),
            visited.Body,
            visited.ExpressionBody,
            (accessor, block) =>
                accessor.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>An expression-bodied property has no accessor node to visit</b> — <c>public int X =&gt;
    /// X;</c> is an <c>ArrowExpressionClause</c> hanging off the property itself — so a weaver that
    /// only overrode accessors left exactly the shape this guard exists for completely unguarded.
    /// Found by the test written for it, which took the test process down with it rather than
    /// failing, which is <c>R11</c> demonstrated once again.
    /// </remarks>
    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        PropertyDeclarationSyntax visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;

        return Getter(visited, visited.ExpressionBody, (property, accessors) => property
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithAccessorList(accessors));
    }

    /// <inheritdoc/>
    public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        IndexerDeclarationSyntax visited = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node)!;

        return Getter(visited, visited.ExpressionBody, (indexer, accessors) => indexer
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithAccessorList(accessors));
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
    private static TLoop Guarded<TLoop>(
        TLoop loop,
        Func<TLoop, StatementSyntax> body,
        Func<TLoop, StatementSyntax, TLoop> withBody,
        string tick)
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

        return withBody(loop, block.WithStatements(block.Statements.Insert(0, Statement(tick))));
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

    /// <summary>
    /// Brackets a function-shaped member with the depth counter, whichever way its body is written.
    /// </summary>
    /// <remarks>
    /// <b>An iterator or an <c>async</c> member returns to its caller long before its body
    /// finishes</b>, so a depth counter around the body would be counting something other than
    /// stack frames. Left alone deliberately, rather than guarded wrongly. A member with neither a
    /// body nor an expression body is <c>abstract</c>, <c>extern</c>, <c>partial</c>, an
    /// auto-property accessor or a syntax error, and there is nothing there to guard.
    /// </remarks>
    private TNode Bounded<TNode>(
        TNode node,
        SyntaxTokenList modifiers,
        bool returnsValue,
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? arrow,
        Func<TNode, BlockSyntax, TNode> withBody)
        where TNode : SyntaxNode
    {
        // A MEMBER THAT DOES NOT PARSE IS LEFT EXACTLY AS IT IS, AND THIS IS A FIX RATHER THAN
        // CAUTION. Turning `=> expr;` into a block drops the member's own semicolon token, and for
        // `double Twice(double x) => x *;` that token is where the parser hung `CS1525: invalid
        // expression term ';'` - so the rewrite deleted the one diagnostic the user needed and the
        // editor showed *nothing* on a line that plainly does not compile. Found while generalising
        // this to declared types (`E6-T34`); it was reachable from a local function all along.
        // Nothing is lost by skipping the weave: a member that does not parse never runs.
        if (node.ContainsDiagnostics
            || modifiers.Any(SyntaxKind.AsyncKeyword)
            || ContainsYield(node))
        {
            return node;
        }

        BlockSyntax? block = body ?? Expanded(arrow, returnsValue);

        return block is null ? node : withBody(node, Bracketed(block));
    }

    /// <summary>
    /// Turns <c>=&gt; expr;</c> on a property or an indexer into a bracketed <c>get</c>, which is
    /// the only shape a guard can go inside (`E6-T34`).
    /// </summary>
    /// <remarks>
    /// The member is left alone when it already has an accessor list — those accessors are visited
    /// in their own right — and when it does not parse, for the reason <see cref="Bounded"/> gives.
    /// </remarks>
    private TNode Getter<TNode>(
        TNode node,
        ArrowExpressionClauseSyntax? arrow,
        Func<TNode, AccessorListSyntax, TNode> withAccessors)
        where TNode : SyntaxNode
    {
        if (arrow is null || node.ContainsDiagnostics || Expanded(arrow, returnsValue: true) is not { } block)
        {
            return node;
        }

        return withAccessors(
            node,
            SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Bracketed(block)))));
    }

    /// <summary>An expression body as the block it stands for, so a guard can go inside it.</summary>
    /// <remarks>
    /// <b><c>=&gt; throw …</c> is its own case and not a value</b>: <c>return throw new X();</c> is
    /// not C#, so a throwing expression body becomes a <c>throw</c> statement. Found while
    /// generalising this to declared types (`E6-T34`); it was reachable from a local function before
    /// that, and it generated source the compiler refused on a line the user could not see.
    /// </remarks>
    private static BlockSyntax? Expanded(ArrowExpressionClauseSyntax? arrow, bool returnsValue)
    {
        if (arrow is null)
        {
            return null;
        }

        ExpressionSyntax expression = arrow.Expression
            .WithoutTrivia()
            .WithLeadingTrivia(SyntaxFactory.Space);

        return SyntaxFactory.Block(expression switch
        {
            ThrowExpressionSyntax thrown => SyntaxFactory.ThrowStatement(thrown.Expression),
            _ when returnsValue => SyntaxFactory.ReturnStatement(expression),
            _ => SyntaxFactory.ExpressionStatement(expression),
        });
    }

    /// <summary>Whether a member hands back a value, read from the syntax alone.</summary>
    private static bool Returns(TypeSyntax type) =>
        type is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };

    /// <summary>Whether this is the generated entry point rather than something the user wrote.</summary>
    private static bool IsEntryPoint(BaseMethodDeclarationSyntax method) =>
        method.ParameterList.Parameters.Any(
            parameter => parameter.Identifier.ValueText == TokenParameterName);

    /// <summary>
    /// The loop guard to weave at a given place: the one that reads <c>__token</c>, or the one that
    /// reads the thread (`E6-T34`).
    /// </summary>
    /// <remarks>
    /// <b>Asked of the <i>original</i> node rather than the visited copy</b>, because the copy a
    /// rewriter hands back has no parent and the question is entirely about where the node sits.
    /// </remarks>
    private string Tick(SyntaxNode node) => TokenInScope(node) ? _tick : _tickOnThread;

    /// <summary>Whether <c>__token</c> can be named at a node's position.</summary>
    /// <remarks>
    /// It is a parameter of the generated entry point, so it is in scope inside that method and
    /// nowhere else. The walk stops at the first type declaration for the same reason it stops at
    /// the first method: a type the block declares is compiled beside the entry point, not inside
    /// it, and nothing in it can reach a local of <c>Run</c>.
    /// </remarks>
    private static bool TokenInScope(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is BaseMethodDeclarationSyntax method)
            {
                return IsEntryPoint(method);
            }

            if (current is BaseTypeDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The statement the guard weaves, carrying no trivia at all.</summary>
    /// <remarks>
    /// <b>No trivia is the point.</b> A parsed statement arrives with elastic trivia that a
    /// formatter would turn into newlines, and a newline here moves every subsequent line of the
    /// user's script — which moves every compiler diagnostic off the line the user is looking at.
    /// </remarks>
    private static StatementSyntax Statement(string text) =>
        SyntaxFactory.ParseStatement(text).WithoutTrivia();
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Spark.Api;

// Roslyn declares its own DiagnosticSeverity, and this file consumes both it and ours.
// Aliasing rather than fully qualifying at each use keeps the intent obvious and stops
// the ambiguity returning the next time someone adds a diagnostic here.
using DiagnosticSeverity = Spark.Api.DiagnosticSeverity;

namespace Spark.Scripting;

/// <summary>The regular expressions the analyser uses, source-generated so none is built at run time.</summary>
internal static partial class ScriptPatterns
{
    /// <summary>Matches an explicit port declaration comment, for example <c>// in: double radius = 5.0</c>.</summary>
    [GeneratedRegex(@"^[ \t]*//[ \t]*in[ \t]*:[ \t]*(?<body>\S.*?)[ \t]*$", RegexOptions.Multiline)]
    internal static partial Regex InputDirective();
}

/// <summary>What a code block's last word is, and therefore how many output ports it has.</summary>
internal enum ScriptResultKind
{
    /// <summary>The block returns nothing. It still has one output port, which carries <see langword="null"/>.</summary>
    None = 0,

    /// <summary>The block produces one value, on a port called <c>result</c>.</summary>
    Value = 1,

    /// <summary>The block returns a named tuple, giving one port per element.</summary>
    NamedTuple = 2,
}

/// <summary>One input port a code block declared for itself with an <c>// in:</c> comment.</summary>
/// <param name="Name">The variable name, which is also the port name and the port's identity.</param>
/// <param name="TypeName">The C# type name exactly as the user wrote it.</param>
/// <param name="DefaultExpression">The C# expression to use when nothing is wired, or <see langword="null"/>.</param>
/// <param name="DefaultValue">
/// The default as a plain CLR value, when it was written as a literal and could therefore be read
/// without running anything. This is what becomes the port's literal on the canvas.
/// </param>
/// <param name="Offset">Where the declaration appeared, so ports keep the order they were written in.</param>
internal readonly record struct ExplicitInput(
    string Name, string TypeName, string? DefaultExpression, object? DefaultValue, int Offset);

/// <summary>
/// Everything the rewriter needs to know about a code block that can be worked out by parsing it
/// alone: what it declares, what it returns, where the guards go, and what has to be blanked before
/// the text can be copied into a method body.
/// </summary>
/// <remarks>
/// The parse is <see cref="SourceCodeKind.Script"/>, which accepts statements, methods and a bare
/// trailing expression at the top level — the shape a code block is actually written in. The text is
/// then copied <b>verbatim</b> into a lambda body, so anything that is legal in a script but not in a
/// method body is blanked in place (replaced by spaces, never removed) and re-emitted in the header.
/// Blanking keeps every character offset and every line number identical to the file on screen, which
/// is the invariant the whole editor experience rests on.
/// </remarks>
internal sealed class ScriptTextAnalysis
{
    private ScriptTextAnalysis(
        string userText,
        SourceText sourceText,
        CompilationUnitSyntax root,
        IReadOnlyList<ExplicitInput> explicitInputs,
        IReadOnlyList<string> headerUsings,
        ScriptResultKind resultKind,
        IReadOnlyList<string> tupleNames,
        IReadOnlyList<SourceInjection> injections,
        IReadOnlyList<TextSpan> blanks,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        ExpressionStatementSyntax? trailingCandidate)
    {
        TrailingCandidate = trailingCandidate;
        UserText = userText;
        SourceText = sourceText;
        Root = root;
        ExplicitInputs = explicitInputs;
        HeaderUsings = headerUsings;
        ResultKind = resultKind;
        TupleNames = tupleNames;
        Injections = injections;
        Blanks = blanks;
        Diagnostics = diagnostics;
    }

    /// <summary>The exact text the user typed.</summary>
    internal string UserText { get; }

    /// <summary>The user's text as Roslyn source, for turning an offset into a line and column.</summary>
    internal SourceText SourceText { get; }

    /// <summary>The parsed script.</summary>
    internal CompilationUnitSyntax Root { get; }

    /// <summary>Ports the user declared explicitly, in the order they were written.</summary>
    internal IReadOnlyList<ExplicitInput> ExplicitInputs { get; }

    /// <summary>Using directives lifted out of the body and re-emitted in the header.</summary>
    internal IReadOnlyList<string> HeaderUsings { get; }

    /// <summary>What the block's last word is.</summary>
    internal ScriptResultKind ResultKind { get; }

    /// <summary>The output port names, when the block returns a named tuple.</summary>
    internal IReadOnlyList<string> TupleNames { get; }

    /// <summary>Guard calls and result rewriting, sorted into the order they must be applied.</summary>
    internal IReadOnlyList<SourceInjection> Injections { get; }

    /// <summary>Spans to replace with spaces before copying.</summary>
    internal IReadOnlyList<TextSpan> Blanks { get; }

    /// <summary>Anything wrong that parsing alone could find.</summary>
    internal IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }

    /// <summary>
    /// A trailing statement that <i>might</i> be the block's result — <c>Math.Sqrt(x);</c>, semicolon
    /// and all — but cannot be decided by parsing alone, because whether it produces a value depends
    /// on what the call returns.
    /// </summary>
    /// <remarks>
    /// The compiler settles it. <see cref="CodeBlockCompiler"/> asks the semantic model for the
    /// expression's type once the block has compiled, and calls <see cref="WithTrailingResult"/> when
    /// the answer is not <c>void</c>. Guessing here instead would either drop the result of the most
    /// common code block anyone writes, or turn <c>Log(x);</c> into a compile error.
    /// </remarks>
    internal ExpressionStatementSyntax? TrailingCandidate { get; }

    /// <summary>Re-analyses this text with the trailing statement treated as the block's result.</summary>
    /// <returns>A new analysis. This one is unchanged.</returns>
    internal ScriptTextAnalysis WithTrailingResult() => Of(UserText, forceTrailingResult: true);

    /// <summary>Parse options for a code block: statements at the top level, a trailing expression allowed.</summary>
    internal static CSharpParseOptions ScriptParseOptions { get; } =
        new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);

    /// <summary>Parse options for the generated compilation unit, which is ordinary C#.</summary>
    internal static CSharpParseOptions RegularParseOptions { get; } =
        new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    /// <summary>Accessibility and inheritance modifiers that a local declaration cannot carry.</summary>
    private static readonly HashSet<SyntaxKind> IllegalInMethodBody =
    [
        SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword,
        SyntaxKind.InternalKeyword, SyntaxKind.VirtualKeyword, SyntaxKind.OverrideKeyword,
        SyntaxKind.AbstractKeyword, SyntaxKind.SealedKeyword, SyntaxKind.NewKeyword,
        SyntaxKind.PartialKeyword, SyntaxKind.ReadOnlyKeyword, SyntaxKind.StaticKeyword,
    ];

    /// <summary>Analyses one code block's text.</summary>
    /// <param name="userText">The text the user typed. <see langword="null"/> is treated as empty.</param>
    /// <param name="forceTrailingResult">
    /// Treat a trailing <c>expression;</c> as the block's result. Set only by
    /// <see cref="WithTrailingResult"/>, once the compiler has confirmed it produces a value.
    /// </param>
    /// <returns>The analysis.</returns>
    internal static ScriptTextAnalysis Of(string? userText, bool forceTrailingResult = false)
    {
        string text = userText ?? string.Empty;
        SourceText source = SourceText.From(text);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, ScriptParseOptions);
        CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();

        List<ScriptDiagnostic> diagnostics = [];
        List<TextSpan> blanks = [];
        List<SourceInjection> injections = [];

        // Using directives are legal at the top of a script and illegal inside a method body, so
        // they move to the header. Blanking rather than deleting is what keeps the columns after
        // them — and every line number below them — exactly where the user sees them.
        List<string> headerUsings = [];
        foreach (UsingDirectiveSyntax directive in root.Usings)
        {
            headerUsings.Add(directive.ToString());
            blanks.Add(directive.Span);
        }

        BlankIllegalModifiers(root, blanks);

        GuardWeaver.Plan guards = GuardWeaver.For(root);
        blanks.AddRange(guards.Blanks);
        injections.AddRange(guards.Injections);

        ScriptResultKind resultKind = AnalyseResult(
            root, source, injections, diagnostics, forceTrailingResult,
            out List<string> tupleNames, out ExpressionStatementSyntax? trailingCandidate);

        injections.Sort(static (left, right) => left.Offset != right.Offset
            ? left.Offset.CompareTo(right.Offset)
            : right.Order.CompareTo(left.Order));

        List<ExplicitInput> explicitInputs = ReadInputDirectives(text, source, diagnostics);

        return new ScriptTextAnalysis(
            text, source, root, explicitInputs, headerUsings, resultKind, tupleNames, injections, blanks,
            diagnostics, trailingCandidate);
    }

    /// <summary>Turns an offset in the user's text into a one-based line and column.</summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The line and column, both one-based, or <c>(0, 0)</c> when the offset is out of range.</returns>
    internal (int Line, int Column) PositionOf(int offset)
    {
        if (offset < 0 || offset > SourceText.Length)
        {
            return (0, 0);
        }

        LinePosition position = SourceText.Lines.GetLinePosition(offset);
        return (position.Line + 1, position.Character + 1);
    }

    private static void BlankIllegalModifiers(CompilationUnitSyntax root, List<TextSpan> blanks)
    {
        foreach (MemberDeclarationSyntax member in root.Members)
        {
            SyntaxTokenList modifiers = member switch
            {
                MethodDeclarationSyntax method => method.Modifiers,
                FieldDeclarationSyntax field => field.Modifiers,
                _ => default,
            };

            foreach (SyntaxToken modifier in modifiers)
            {
                if (IllegalInMethodBody.Contains(modifier.Kind()))
                {
                    blanks.Add(modifier.Span);
                }
            }
        }
    }

    /// <summary>
    /// Works out what the block produces, and rewrites a bare trailing expression into a
    /// <c>return</c> so the generated lambda has a value to give back.
    /// </summary>
    /// <remarks>
    /// Output ports come from a named tuple return — <c>return (area: a, perimeter: p);</c> — or, when
    /// there is no tuple, from a single port called <c>result</c>. They are deliberately <b>not</b>
    /// inferred from "locals that are never read again", which is the other obvious rule and a trap:
    /// adding one debug line that reads a local would silently change the node's port set and drop
    /// the wires hanging off it.
    /// </remarks>
    private static ScriptResultKind AnalyseResult(
        CompilationUnitSyntax root,
        SourceText source,
        List<SourceInjection> injections,
        List<ScriptDiagnostic> diagnostics,
        bool forceTrailingResult,
        out List<string> tupleNames,
        out ExpressionStatementSyntax? trailingCandidate)
    {
        tupleNames = [];
        trailingCandidate = null;

        List<ExpressionSyntax> results = [];

        List<ReturnStatementSyntax> returns = [];
        CollectReturns(root, returns);
        foreach (ReturnStatementSyntax statement in returns)
        {
            if (statement.Expression is not null)
            {
                results.Add(statement.Expression);
            }
        }

        // A bare trailing expression is the terse form a code block is usually written in. It is
        // legal in a script and not in a method body, so it becomes a `return`.
        if (root.Members.Count > 0
            && root.Members[^1] is GlobalStatementSyntax { Statement: ExpressionStatementSyntax trailing })
        {
            bool decided = IsResultExpression(trailing);

            if (!decided && CouldBeResult(trailing))
            {
                if (forceTrailingResult)
                {
                    decided = true;
                }
                else
                {
                    trailingCandidate = trailing;
                }
            }

            if (decided)
            {
                injections.Add(new SourceInjection(trailing.Expression.SpanStart, trailing.SpanStart, "return "));

                if (trailing.SemicolonToken.IsMissing)
                {
                    injections.Add(new SourceInjection(trailing.Expression.Span.End, trailing.SpanStart, ";"));
                }

                results.Add(trailing.Expression);
            }
        }

        if (results.Count == 0)
        {
            return ScriptResultKind.None;
        }

        foreach (ExpressionSyntax expression in results)
        {
            if (!TryReadTupleNames(expression, out List<string> names))
            {
                continue;
            }

            if (tupleNames.Count == 0)
            {
                tupleNames = names;
                continue;
            }

            if (!tupleNames.SequenceEqual(names, StringComparer.Ordinal))
            {
                LinePosition position = source.Lines.GetLinePosition(expression.SpanStart);
                diagnostics.Add(new ScriptDiagnostic(
                    DiagnosticSeverity.Warning,
                    ScriptDiagnosticCodes.InconsistentTupleNames,
                    $"This return names its tuple elements ({string.Join(", ", names)}) differently from the first "
                    + $"one ({string.Join(", ", tupleNames)}). The output ports come from the first return; name "
                    + "them the same way in every return, or the ports will not match what this branch produces.",
                    line: position.Line + 1,
                    column: position.Character + 1,
                    start: expression.SpanStart,
                    length: expression.Span.Length));
            }
        }

        return tupleNames.Count > 0 ? ScriptResultKind.NamedTuple : ScriptResultKind.Value;
    }

    /// <summary>
    /// Every <c>return</c> that belongs to the block itself, skipping the ones inside anything the
    /// block declares — a local function's <c>return</c> is that function's, not the block's.
    /// </summary>
    private static void CollectReturns(SyntaxNode node, List<ReturnStatementSyntax> results)
    {
        foreach (SyntaxNode child in node.ChildNodes())
        {
            if (child is LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax
                or BaseTypeDeclarationSyntax
                or MethodDeclarationSyntax
                or DelegateDeclarationSyntax)
            {
                continue;
            }

            if (child is ReturnStatementSyntax statement)
            {
                results.Add(statement);
            }

            CollectReturns(child, results);
        }
    }

    private static bool TryReadTupleNames(ExpressionSyntax expression, out List<string> names)
    {
        names = [];

        if (expression is not TupleExpressionSyntax tuple || tuple.Arguments.Count < 2)
        {
            return false;
        }

        foreach (ArgumentSyntax argument in tuple.Arguments)
        {
            if (argument.NameColon is null)
            {
                names = [];
                return false;
            }

            names.Add(argument.NameColon.Name.Identifier.ValueText);
        }

        return true;
    }

    /// <summary>
    /// Whether a trailing expression statement is the block's result rather than something done for
    /// its effect.
    /// </summary>
    /// <remarks>
    /// A missing semicolon settles it outright — that is a script's trailing expression and nothing
    /// else. With a semicolon, anything that would not be a legal statement in real C# must be a
    /// result, because the alternative is CS0201. Object creation is counted as a result too: as the
    /// last line of a code block, <c>new Point3d(0, 0, 1);</c> is a value somebody wants, not a
    /// constructor called for its side effects.
    /// </remarks>
    private static bool IsResultExpression(ExpressionStatementSyntax statement) =>
        statement.SemicolonToken.IsMissing || !IsStatementExpression(statement.Expression);

    /// <summary>
    /// Whether a trailing <c>expression;</c> could be the block's result if it turns out to produce a
    /// value. An assignment or an increment never is: the value it yields is a C# curiosity, not
    /// something a user meant to send down a wire.
    /// </summary>
    private static bool CouldBeResult(ExpressionStatementSyntax statement) => statement.Expression switch
    {
        AssignmentExpressionSyntax => false,
        PostfixUnaryExpressionSyntax => false,
        PrefixUnaryExpressionSyntax prefix =>
            !prefix.IsKind(SyntaxKind.PreIncrementExpression) && !prefix.IsKind(SyntaxKind.PreDecrementExpression),
        _ => true,
    };

    private static bool IsStatementExpression(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax => true,
        AwaitExpressionSyntax => true,
        AssignmentExpressionSyntax => true,
        PostfixUnaryExpressionSyntax postfix =>
            postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
        PrefixUnaryExpressionSyntax prefix =>
            prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression),
        ConditionalAccessExpressionSyntax conditional => conditional.WhenNotNull is InvocationExpressionSyntax,
        _ => false,
    };

    private static List<ExplicitInput> ReadInputDirectives(
        string text, SourceText source, List<ScriptDiagnostic> diagnostics)
    {
        List<ExplicitInput> inputs = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (Match match in ScriptPatterns.InputDirective().Matches(text))
        {
            Group body = match.Groups["body"];
            LinePosition position = source.Lines.GetLinePosition(body.Index);

            void Reject(string message) => diagnostics.Add(new ScriptDiagnostic(
                DiagnosticSeverity.Error,
                ScriptDiagnosticCodes.MalformedInputDirective,
                message,
                line: position.Line + 1,
                column: position.Character + 1,
                start: body.Index,
                length: body.Length));

            string declaration = body.Value;
            int assignment = IndexOfAssignment(declaration);

            string? defaultExpression = assignment >= 0 ? declaration[(assignment + 1)..].Trim() : null;
            string head = (assignment >= 0 ? declaration[..assignment] : declaration).TrimEnd();

            int split = LastSeparator(head);
            if (split <= 0)
            {
                Reject($"'{declaration}' is not a port declaration. Write it as '// in: double radius = 5.0' — a "
                    + "type, then a name, then an optional default.");
                continue;
            }

            string typeName = head[..split].Trim();
            string name = head[(split + 1)..].Trim();

            if (typeName.Length == 0 || !SyntaxFacts.IsValidIdentifier(name))
            {
                Reject($"'{name}' is not a usable port name in '{declaration}'. A port name is a C# identifier, "
                    + "because it is also the variable the code block reads.");
                continue;
            }

            if (!seen.Add(name))
            {
                Reject($"'{name}' is declared more than once. A port's identity is its name, so two ports cannot "
                    + "share one.");
                continue;
            }

            inputs.Add(new ExplicitInput(
                name, typeName, defaultExpression, LiteralValueOf(defaultExpression), body.Index));
        }

        return inputs;
    }

    /// <summary>
    /// The index of the <c>=</c> that separates a declaration from its default, or <c>-1</c>. Angle
    /// and round brackets are counted so a generic type argument or an argument list cannot be
    /// mistaken for one, and <c>==</c>, <c>&lt;=</c>, <c>&gt;=</c>, <c>!=</c> and <c>=&gt;</c> are
    /// skipped.
    /// </summary>
    private static int IndexOfAssignment(string text)
    {
        int depth = 0;

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];

            if (current is '(' or '[' or '<')
            {
                depth++;
            }
            else if (current is ')' or ']' or '>')
            {
                depth--;
            }
            else if (current == '=' && depth <= 0)
            {
                bool partOfAnotherOperator =
                    (index + 1 < text.Length && (text[index + 1] == '=' || text[index + 1] == '>'))
                    || (index > 0 && text[index - 1] is '=' or '!' or '<' or '>');

                if (!partOfAnotherOperator)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int LastSeparator(string head)
    {
        for (int index = head.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(head[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The default as a plain CLR value, when it was written as a literal. Anything more elaborate
    /// stays an expression evaluated inside the script, because evaluating it here would mean running
    /// user code to draw a node.
    /// </summary>
    private static object? LiteralValueOf(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        ExpressionSyntax parsed = SyntaxFactory.ParseExpression(expression);

        return parsed switch
        {
            LiteralExpressionSyntax literal => literal.Token.Value,
            PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax negated } prefix
                when prefix.IsKind(SyntaxKind.UnaryMinusExpression) => Negate(negated.Token.Value),
            _ => null,
        };
    }

    private static object? Negate(object? value) => value switch
    {
        double number => -number,
        float number => -number,
        decimal number => -number,
        int number => -number,
        long number => -number,
        _ => null,
    };

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{ResultKind}, {ExplicitInputs.Count} declared input(s)");
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Spark.Api;
using Spark.Engine;
using ApiSeverity = Spark.Api.DiagnosticSeverity;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Spark.Scripting;

/// <summary>
/// Compiles a code block: infers its ports, rewrites it into ordinary C#, compiles it with Roslyn,
/// loads it into a collectible context, and hands back a <see cref="NodeDefinition"/> the engine can
/// evaluate like any other node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Input ports are inferred semantically, not syntactically.</b> The block is compiled once
/// against the standard prelude and the <c>CS0103</c> and <c>CS0117</c> diagnostics are read off:
/// every identifier the compiler could not resolve becomes an input port, in source order. That is
/// materially more robust than walking the syntax tree, because Roslyn has already resolved locals,
/// <c>using</c> aliases, type parameters and every other name that is in scope for a reason a walker
/// would have to reimplement — and get wrong.
/// </para>
/// <para>
/// <b>A port's identity is its name.</b> Moving a usage around, or using it in more places, does not
/// rewire the graph.
/// </para>
/// <para>
/// <b>Output ports come from a named tuple return.</b> <c>return (area: a, perimeter: p);</c> yields
/// ports <c>area</c> and <c>perimeter</c>; anything else yields one port called <c>result</c>. This
/// is idiomatic C#, statically analysable, and invents no syntax. Ports are deliberately not inferred
/// from "locals never read again", because adding one debug line would then silently change the port
/// set and drop the wires hanging off it.
/// </para>
/// <para>
/// <b>Security, stated plainly.</b> A code block is executable code and it runs in this process with
/// this process's privileges. .NET has no code-access security, so <i>a Spark graph containing a code
/// block is a program</i>, and opening one from an untrusted source is equivalent to running an
/// unknown executable. Nothing here sandboxes anything, and claiming otherwise would be dishonest.
/// What actually helps is procedural: never evaluate on open, show what a graph will run before it
/// runs, keep a content-hash trust list, and offer a no-script mode for automation. See ADR-0008.
/// </para>
/// <para>
/// <b>A runaway script can still end the process.</b> Guards are woven into every loop and every
/// declared method, but <see cref="StackOverflowException"/> cannot be caught in .NET. See
/// <see cref="ScriptGuard"/>.
/// </para>
/// </remarks>
public static class CodeBlockCompiler
{
    /// <summary>Warnings that are artefacts of the rewriting rather than anything the user did.</summary>
    private static readonly ImmutableDictionary<string, ReportDiagnostic> Suppressed =
        new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal)
        {
            ["CS0105"] = ReportDiagnostic.Suppress, // using appeared previously
            ["CS8019"] = ReportDiagnostic.Suppress, // unnecessary using
            ["CS8321"] = ReportDiagnostic.Suppress, // local function never used
            ["CS0219"] = ReportDiagnostic.Suppress, // assigned but never used — an unwired input port
            ["CS0164"] = ReportDiagnostic.Suppress, // label never used
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly CSharpCompilationOptions CompilationOptions =
        new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false,
                warningLevel: 4,
                nullableContextOptions: NullableContextOptions.Disable,
                deterministic: true)
            .WithSpecificDiagnosticOptions(Suppressed);

    /// <summary>Compiles one code block.</summary>
    /// <param name="text">The C# the user typed. <see langword="null"/> is treated as empty.</param>
    /// <param name="options">What is wired to the block and what it may reference. Omit for the defaults.</param>
    /// <returns>The ports, the diagnostics, and the node definition when it compiled.</returns>
    public static CodeBlockCompilation Compile(string? text, CodeBlockOptions? options = null)
    {
        options ??= new CodeBlockOptions();

        ScriptTextAnalysis analysis = ScriptTextAnalysis.Of(text);
        List<ScriptDiagnostic> diagnostics = [.. analysis.Diagnostics];

        string key = ComputeKey(analysis.UserText, options);

        if (diagnostics.All(static diagnostic => diagnostic.Severity != ApiSeverity.Error))
        {
            if (options.Cache.TryGetResident(key, out CompiledScript? resident) && resident is not null)
            {
                return FromCache(key, analysis, resident, options, diagnostics);
            }

            if (options.Cache.TryLoadFromDisk(key, out CompiledScript? stored) && stored is not null)
            {
                return FromCache(key, analysis, stored, options, diagnostics);
            }
        }

        return CompileFresh(key, analysis, options, diagnostics);
    }

    private static CodeBlockCompilation FromCache(
        string key,
        ScriptTextAnalysis analysis,
        CompiledScript script,
        CodeBlockOptions options,
        IReadOnlyList<ScriptDiagnostic> diagnostics)
    {
        List<InputDeclaration> declarations = [];

        foreach (PortDefinition port in script.Inputs)
        {
            declarations.Add(new InputDeclaration(
                port.Name, TypeNames.CSharpName(port.ValueType), DefaultExpressionFor(analysis, port.Name)));
        }

        RewrittenCodeBlock rewritten = ScriptRewriter.Rewrite(analysis, declarations, options.FilePath);

        return new CodeBlockCompilation(
            key,
            analysis.UserText,
            rewritten.Text,
            rewritten.Map,
            diagnostics,
            script.Inputs,
            script.Outputs,
            BuildDefinition(key, script, options),
            fromCache: true);
    }

    private static CodeBlockCompilation CompileFresh(
        string key, ScriptTextAnalysis analysis, CodeBlockOptions options, List<ScriptDiagnostic> diagnostics)
    {
        // Explicitly declared ports come first, in the order their comments appear. They are declared
        // before the probe compile, so they never turn up as unresolved names.
        List<InputDeclaration> declarations = [];
        foreach (ExplicitInput input in analysis.ExplicitInputs)
        {
            declarations.Add(new InputDeclaration(input.Name, input.TypeName, input.DefaultExpression));
        }

        RewrittenCodeBlock rewritten = ScriptRewriter.Rewrite(analysis, declarations, options.FilePath);
        CSharpCompilation compilation = Create(key, rewritten, options.References, out SyntaxTree tree);

        List<string> inferred = InferredInputNames(
            compilation, tree, rewritten.Map, compilation.GetDiagnostics(), declarations);

        if (inferred.Count > 0)
        {
            foreach (string name in inferred)
            {
                Type type = ConnectedTypeOf(options, name);
                declarations.Add(new InputDeclaration(name, TypeNames.CSharpName(type), null));
            }

            rewritten = ScriptRewriter.Rewrite(analysis, declarations, options.FilePath);
            compilation = Create(key, rewritten, options.References, out tree);
        }

        // `Math.Sqrt(x);` is the block's result and `Log(x);` is not, and only the compiler knows
        // which. Ask it now that every name resolves, and rewrite once more if the answer is a value.
        if (analysis.TrailingCandidate is not null && TrailingProducesValue(compilation, tree, rewritten, analysis))
        {
            analysis = analysis.WithTrailingResult();
            rewritten = ScriptRewriter.Rewrite(analysis, declarations, options.FilePath);
            compilation = Create(key, rewritten, options.References, out tree);
        }

        Translate(compilation.GetDiagnostics(), rewritten, analysis, tree, diagnostics);

        List<PortDefinition> inputs = BuildInputPorts(declarations, analysis, compilation, tree);

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == ApiSeverity.Error))
        {
            return Failed(key, analysis, rewritten, diagnostics, inputs);
        }

        List<PortDefinition> outputs = InferOutputs(compilation, tree, analysis);

        using MemoryStream assemblyStream = new();
        using MemoryStream symbolStream = new();

        EmitResult emit = compilation.Emit(
            assemblyStream,
            symbolStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        if (!emit.Success)
        {
            Translate(emit.Diagnostics, rewritten, analysis, tree, diagnostics);
            return Failed(key, analysis, rewritten, diagnostics, inputs);
        }

        CompiledScript script = options.Cache.Store(
            key, assemblyStream.ToArray(), symbolStream.ToArray(), inputs, outputs);

        return new CodeBlockCompilation(
            key,
            analysis.UserText,
            rewritten.Text,
            rewritten.Map,
            diagnostics,
            script.Inputs,
            script.Outputs,
            BuildDefinition(key, script, options),
            fromCache: false);
    }

    private static CodeBlockCompilation Failed(
        string key,
        ScriptTextAnalysis analysis,
        RewrittenCodeBlock rewritten,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        IReadOnlyList<PortDefinition> inputs) =>
        new(key, analysis.UserText, rewritten.Text, rewritten.Map, diagnostics, inputs, [], null, fromCache: false);

    private static CSharpCompilation Create(
        string key, RewrittenCodeBlock rewritten, ReferenceCatalog catalog, out SyntaxTree tree)
    {
        // The encoding matters: emitting a PDB requires source text that carries one.
        tree = CSharpSyntaxTree.ParseText(
            SourceText.From(rewritten.Text, Encoding.UTF8),
            ScriptTextAnalysis.RegularParseOptions,
            rewritten.FilePath);

        return CSharpCompilation.Create(
            "SparkCodeBlock_" + key[..Math.Min(16, key.Length)],
            [tree],
            catalog.References,
            CompilationOptions);
    }

    private static Type ConnectedTypeOf(CodeBlockOptions options, string name) =>
        options.ConnectedInputTypes is not null
        && options.ConnectedInputTypes.TryGetValue(name, out Type? connected)
        && connected is not null
            ? connected
            : typeof(object);

    /// <summary>
    /// Reads the input port names off the compiler's own resolution failures, in source order.
    /// </summary>
    /// <remarks>
    /// <c>CS0103</c> is the ordinary case: a bare name nothing in scope explains. <c>CS0117</c> is the
    /// case a syntax walker gets wrong — a name that <i>does</i> resolve, to a type, where the user
    /// meant a value of their own. Taking the receiver there is what stops a port called
    /// <c>Transform</c> from silently failing to appear.
    /// </remarks>
    private static List<string> InferredInputNames(
        CSharpCompilation compilation,
        SyntaxTree tree,
        SourceMap map,
        ImmutableArray<Diagnostic> diagnostics,
        List<InputDeclaration> declarations)
    {
        SyntaxNode root = tree.GetRoot();
        SemanticModel? model = null;

        HashSet<string> known = new(StringComparer.Ordinal);
        foreach (InputDeclaration declaration in declarations)
        {
            known.Add(declaration.Name);
        }

        List<(string Name, int UserOffset)> found = [];

        foreach (Diagnostic diagnostic in diagnostics.OrderBy(static item => item.Location.SourceSpan.Start))
        {
            if (diagnostic.Location.SourceTree != tree || !diagnostic.Location.IsInSource)
            {
                continue;
            }

            string? name = diagnostic.Id switch
            {
                "CS0103" => IdentifierAt(root, diagnostic.Location.SourceSpan),
                "CS0117" => ShadowedValueAt(root, diagnostic.Location.SourceSpan, compilation, tree, ref model),
                _ => null,
            };

            if (name is null || name.StartsWith("__", StringComparison.Ordinal) || known.Contains(name))
            {
                continue;
            }

            int userOffset = map.ToUser(diagnostic.Location.SourceSpan.Start);
            if (userOffset < 0)
            {
                // The failure is in scaffolding, not in anything the user typed.
                continue;
            }

            known.Add(name);
            found.Add((name, userOffset));
        }

        found.Sort(static (left, right) => left.UserOffset.CompareTo(right.UserOffset));

        return [.. found.Select(static item => item.Name)];
    }

    /// <summary>
    /// Turns the declarations into ports, reading each one's CLR type from the semantic model rather
    /// than from the string it was written as — so an explicitly declared <c>// in: List&lt;double&gt; xs</c>
    /// gets the rank-1 port it deserves without a bespoke type-name parser.
    /// </summary>
    private static List<PortDefinition> BuildInputPorts(
        List<InputDeclaration> declarations,
        ScriptTextAnalysis analysis,
        CSharpCompilation compilation,
        SyntaxTree tree)
    {
        List<PortDefinition> ports = [];

        if (declarations.Count == 0)
        {
            return ports;
        }

        SemanticModel model = compilation.GetSemanticModel(tree);

        Dictionary<string, VariableDeclaratorSyntax> declarators = new(StringComparer.Ordinal);
        foreach (VariableDeclaratorSyntax declarator in tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            // Ours come first in document order, so a name the user happens to reuse never wins.
            _ = declarators.TryAdd(declarator.Identifier.ValueText, declarator);
        }

        foreach (InputDeclaration declaration in declarations)
        {
            Type type = typeof(object);

            if (declarators.TryGetValue(declaration.Name, out VariableDeclaratorSyntax? declarator)
                && model.GetDeclaredSymbol(declarator) is ILocalSymbol local)
            {
                type = TypeNames.Resolve(local.Type);
            }

            ports.Add(new PortDefinition(
                declaration.Name,
                type,
                PortDefinition.RankOfType(type),
                defaultValue: DefaultValueFor(analysis, declaration.Name)));
        }

        return ports;
    }

    /// <summary>
    /// Whether the trailing <c>expression;</c> the parse could not classify actually produces a
    /// value.
    /// </summary>
    private static bool TrailingProducesValue(
        CSharpCompilation compilation, SyntaxTree tree, RewrittenCodeBlock rewritten, ScriptTextAnalysis analysis)
    {
        ExpressionSyntax expression = analysis.TrailingCandidate!.Expression;

        int start = rewritten.Map.ToGenerated(expression.SpanStart);
        int end = rewritten.Map.ToGenerated(expression.Span.End);

        if (start < 0 || end <= start || end > rewritten.Text.Length)
        {
            return false;
        }

        SyntaxNode? node = NodeAt(tree.GetRoot(), TextSpan.FromBounds(start, end));
        if (node is ExpressionStatementSyntax statement)
        {
            node = statement.Expression;
        }

        if (node is not ExpressionSyntax generated)
        {
            return false;
        }

        ITypeSymbol? type = compilation.GetSemanticModel(tree).GetTypeInfo(generated).Type;

        return type is not null
            && type.SpecialType != SpecialType.System_Void
            && type.TypeKind != TypeKind.Error;
    }

    private static string? IdentifierAt(SyntaxNode root, TextSpan span)
    {
        SyntaxNode? node = NodeAt(root, span);
        return node is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
    }

    private static string? ShadowedValueAt(
        SyntaxNode root, TextSpan span, CSharpCompilation compilation, SyntaxTree tree, ref SemanticModel? model)
    {
        SyntaxNode? node = NodeAt(root, span);

        MemberAccessExpressionSyntax? access = node?.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (access?.Expression is not IdentifierNameSyntax receiver)
        {
            return null;
        }

        model ??= compilation.GetSemanticModel(tree);

        return model.GetSymbolInfo(receiver).Symbol is ITypeSymbol ? receiver.Identifier.ValueText : null;
    }

    private static SyntaxNode? NodeAt(SyntaxNode root, TextSpan span)
    {
        try
        {
            return root.FindNode(span, getInnermostNodeForTie: true);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the output ports off the inferred type of the generated body lambda, which is the type
    /// the user's <c>return</c> actually produces.
    /// </summary>
    private static List<PortDefinition> InferOutputs(
        CSharpCompilation compilation, SyntaxTree tree, ScriptTextAnalysis analysis)
    {
        if (analysis.ResultKind == ScriptResultKind.None)
        {
            return [PortDefinition.Inferred("result", typeof(object))];
        }

        ITypeSymbol? resultType = ResultTypeOf(compilation, tree);

        if (analysis.ResultKind == ScriptResultKind.Value)
        {
            return [PortDefinition.Inferred("result", TypeNames.Resolve(resultType))];
        }

        List<PortDefinition> ports = [];
        INamedTypeSymbol? tuple = resultType as INamedTypeSymbol;

        foreach (string name in analysis.TupleNames)
        {
            Type type = typeof(object);

            if (tuple is { IsTupleType: true })
            {
                foreach (IFieldSymbol element in tuple.TupleElements)
                {
                    if (string.Equals(element.Name, name, StringComparison.Ordinal))
                    {
                        type = TypeNames.Resolve(element.Type);
                        break;
                    }
                }
            }

            ports.Add(PortDefinition.Inferred(name, type));
        }

        return ports.Count > 0 ? ports : [PortDefinition.Inferred("result", typeof(object))];
    }

    private static ITypeSymbol? ResultTypeOf(CSharpCompilation compilation, SyntaxTree tree)
    {
        VariableDeclaratorSyntax? declarator = tree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(static node =>
                string.Equals(node.Identifier.ValueText, ScriptRewriter.BodyVariableName, StringComparison.Ordinal));

        if (declarator is null)
        {
            return null;
        }

        SemanticModel model = compilation.GetSemanticModel(tree);

        return model.GetDeclaredSymbol(declarator) is ILocalSymbol { Type: INamedTypeSymbol delegateType }
            ? delegateType.DelegateInvokeMethod?.ReturnType
            : null;
    }

    /// <summary>Moves compiler messages off the generated text and back onto the user's own.</summary>
    private static void Translate(
        IEnumerable<Diagnostic> diagnostics,
        RewrittenCodeBlock rewritten,
        ScriptTextAnalysis analysis,
        SyntaxTree tree,
        List<ScriptDiagnostic> into)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity is RoslynSeverity.Hidden or RoslynSeverity.Info)
            {
                continue;
            }

            bool mapped = false;
            int start = -1;
            int length = 0;
            int line = 0;
            int column = 0;

            if (diagnostic.Location.SourceTree == tree && diagnostic.Location.IsInSource)
            {
                int candidate = rewritten.Map.ToUser(diagnostic.Location.SourceSpan.Start);

                if (candidate >= 0 && candidate <= analysis.UserText.Length)
                {
                    start = candidate;
                    int end = rewritten.Map.ToUser(diagnostic.Location.SourceSpan.End);
                    length = Math.Max(0, Math.Min(end < 0 ? start : end, analysis.UserText.Length) - start);

                    (line, column) = analysis.PositionOf(start);
                    mapped = true;
                }
            }

            into.Add(new ScriptDiagnostic(
                diagnostic.Severity == RoslynSeverity.Error ? ApiSeverity.Error : ApiSeverity.Warning,
                diagnostic.Severity == RoslynSeverity.Error
                    ? ScriptDiagnosticCodes.CompilerError
                    : ScriptDiagnosticCodes.CompilerWarning,
                Humanise(diagnostic, mapped),
                diagnostic.Id,
                line,
                column,
                start,
                length));
        }
    }

    /// <summary>
    /// Rephrases the handful of compiler messages that land on generated scaffolding, where the
    /// compiler's own wording describes code the user never wrote.
    /// </summary>
    private static string Humanise(Diagnostic diagnostic, bool mapped)
    {
        if (mapped)
        {
            return diagnostic.GetMessage(CultureInfo.InvariantCulture);
        }

        return diagnostic.Id switch
        {
            "CS8917" or "CS0173" =>
                "This code block returns different types on different paths, so it has no single result type. "
                + "Make every return produce the same type.",
            "CS0161" =>
                "Not every path through this code block returns a value. Add a return at the end, or make the "
                + "last line the block's result.",
            _ => diagnostic.GetMessage(CultureInfo.InvariantCulture),
        };
    }

    private static string? DefaultExpressionFor(ScriptTextAnalysis analysis, string name)
    {
        foreach (ExplicitInput input in analysis.ExplicitInputs)
        {
            if (string.Equals(input.Name, name, StringComparison.Ordinal))
            {
                return input.DefaultExpression;
            }
        }

        return null;
    }

    private static object? DefaultValueFor(ScriptTextAnalysis analysis, string name)
    {
        foreach (ExplicitInput input in analysis.ExplicitInputs)
        {
            if (string.Equals(input.Name, name, StringComparison.Ordinal))
            {
                return input.DefaultValue;
            }
        }

        return null;
    }

    private static NodeDefinition BuildDefinition(string key, CompiledScript script, CodeBlockOptions options)
    {
        TimeSpan budget = options.TimeBudget;
        Func<CancellationToken>? cancellation = options.Cancellation;

        object?[] Invoke(object?[] arguments)
        {
            CancellationToken token = cancellation is null ? CancellationToken.None : cancellation();
            using IDisposable scope = ScriptGuard.Begin(budget, token);
            return script.Invoke(arguments!);
        }

        return new NodeDefinition(
            new NodeKey(options.Package, "CodeBlock"),
            options.DisplayName,
            script.Inputs,
            script.Outputs,
            Invoke,
            LacingMode.Longest,
            VersionOf(key),
            isSideEffect: false,
            description: "A C# code block. Its ports are inferred from the code: every name the block does not "
                + "define becomes an input, and a named tuple return becomes the outputs.",
            category: NodeCategories.Script);
    }

    /// <summary>
    /// A definition version derived from the compile cache key, so that editing the script
    /// invalidates every cached evaluation result computed by the previous text.
    /// </summary>
    private static int VersionOf(string key) =>
        (int)(uint.Parse(key[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0x7FFFFFFF);

    private static string ComputeKey(string text, CodeBlockOptions options)
    {
        StringBuilder material = new();

        material.Append("spark-codeblock/1\n")
            .Append(ScriptCompilationCache.SparkVersion()).Append('\n')
            .Append(LanguageVersion.Latest.MapSpecifiedToEffectiveVersion().ToString()).Append('\n')
            .Append(options.References.Version).Append('\n');

        if (options.ConnectedInputTypes is { Count: > 0 })
        {
            List<string> entries = [];
            foreach (KeyValuePair<string, Type> pair in options.ConnectedInputTypes)
            {
                entries.Add(pair.Key + "=" + (pair.Value?.AssemblyQualifiedName ?? "?"));
            }

            entries.Sort(StringComparer.Ordinal);
            material.AppendJoin('\n', entries).Append('\n');
        }

        // Line endings are normalised so that the same script saved on two platforms shares one
        // compiled assembly. The rewrite still uses the original text, so the source map is exact
        // either way.
        material.Append("--\n").Append(text.Replace("\r\n", "\n", StringComparison.Ordinal));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }
}

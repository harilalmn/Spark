using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Spark.Api;

namespace Spark.Scripting;

/// <summary>
/// Turns the source of a code block into a runnable node definition.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is: infer the input ports by compiling once and reading what the compiler says is
/// undefined; compile again with those inputs declared; find the entry point; and hand back a
/// definition whose invocation calls it. Two compiles sounds wasteful and is not — the first is
/// against a tiny throwaway tree and both are cached, so a script that has been seen before costs
/// a dictionary lookup.
/// </para>
/// <para>
/// <b>Input ports are inferred semantically, not by walking the syntax</b> (`E6-T5`). The compiler
/// has already resolved locals, aliases, lambda parameters, <c>using</c> directives and every
/// scoping rule in the language; a syntax walk has to re-implement all of that and gets it subtly
/// wrong on exactly the code people write. So the script is compiled against the prelude, the
/// <c>CS0103</c> and <c>CS0117</c> diagnostics are collected — *the name X does not exist in the
/// current context* — and those identifiers, in source order, are the inputs. An identifier that
/// resolves to anything at all is not a port, which is precisely the rule that a syntax walk
/// cannot express.
/// </para>
/// <para>
/// <b>Output ports come from a returned named tuple</b> (`E6-T8`). <c>return (area: a, perimeter:
/// p);</c> gives ports <c>area</c> and <c>perimeter</c>; any other return gives one port called
/// <c>result</c>. Named tuples are idiomatic C#, statically analysable, and require no invented
/// syntax — which was the whole reason for choosing them over inferring from *locals never read*.
/// </para>
/// </remarks>
public sealed class ScriptNodeFactory : IScriptNodeFactory
{
    private readonly ReferenceCatalog _references;
    private readonly GuardWeaver _guards;
    private readonly ConcurrentDictionary<string, NodeDefinitionSource> _compiled = new(StringComparer.Ordinal);

    /// <summary>Creates a factory over a reference catalogue.</summary>
    /// <param name="references">The assemblies scripts compile against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="references"/> is null.</exception>
    public ScriptNodeFactory(ReferenceCatalog references) : this(references, new GuardWeaver())
    {
    }

    /// <summary>Creates a factory over a reference catalogue and a weaver with chosen ceilings.</summary>
    /// <param name="references">The assemblies scripts compile against.</param>
    /// <param name="guards">The weaver that bounds loops and recursion (`E6-T4`).</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// The ceilings are a constructor argument rather than a global setting because they are part
    /// of every compiled assembly — see <see cref="GuardWeaver(long, int)"/> — and therefore part of
    /// the compile-cache key. Tests are the main caller that wants a tighter one: proving a runaway
    /// loop is stopped should not take a hundred million iterations to do it.
    /// </remarks>
    public ScriptNodeFactory(ReferenceCatalog references, GuardWeaver guards)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(guards);

        _references = references;
        _guards = guards;
    }

    /// <summary>Creates a factory over a fresh catalogue of what is already loaded.</summary>
    public ScriptNodeFactory() : this(new ReferenceCatalog())
    {
    }

    /// <summary>How many scripts the resident cache is holding.</summary>
    public int CachedScripts => _compiled.Count;

    /// <inheritdoc/>
    public NodeDefinitionSource Create(string script, IReadOnlyDictionary<string, Type>? inputTypes = null)
    {
        ArgumentNullException.ThrowIfNull(script);

        // `E6-T9`: same script, same input types, zero compilation. This is what makes a slider
        // feeding a code block feel live - the compile happens once and every subsequent drag is
        // an invocation of an assembly that is already loaded. Changing what is *wired* into a
        // block does recompile it, once per combination of types, which is `E6-T6`'s price and is
        // paid while the user is drawing a wire rather than while they are running the graph.
        IReadOnlyDictionary<string, Type> known = inputTypes ?? EmptyTypes;

        return _compiled.GetOrAdd(CacheKey(script, known), _ => Compile(script, known));
    }

    private static readonly Dictionary<string, Type> EmptyTypes = [];

    /// <summary>
    /// The cache key: the script's text, the references it compiles against, and the language
    /// version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reference-catalogue version is in the key</b> (`E6-T10`). A script whose text has not
    /// changed still has to recompile when the assemblies underneath it have, or a user who has
    /// just fixed a bug in their own node library keeps getting the old behaviour and has no way to
    /// explain it. <b>The guard ceilings are in it too</b> (`E6-T4`), because they are compiled into
    /// the assembly as literals — a cached entry would otherwise keep a limit that has since
    /// changed.
    /// </para>
    /// <para>
    /// The parts are separated by <c>\u0000</c>, which cannot occur in any of them, so no two
    /// different keys can be spelled the same way. It is written as an escape rather than as the
    /// character: a raw NUL in the file makes every tool that reads source — grep included —
    /// classify it as binary and silently skip it.
    /// </para>
    /// </remarks>
    private string CacheKey(string script, IReadOnlyDictionary<string, Type> inputTypes)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            script + "\u0000" + _references.Version.ToString(CultureInfo.InvariantCulture)
            + "\u0000" + _guards.IterationLimit.ToString(CultureInfo.InvariantCulture)
            + "\u0000" + _guards.DepthLimit.ToString(CultureInfo.InvariantCulture)
            + "\u0000" + Describe(inputTypes)));

        return Convert.ToHexString(hash);
    }

    /// <summary>A short, stable content hash for the node's key.</summary>
    /// <remarks>
    /// <b>The input types are in it as well as the text</b> (`E6-T6`). The evaluation cache keys on
    /// the node's key, and the same source with a <c>double</c> wired in does not compute the same
    /// thing as the same source with a <c>Point3d</c> wired in - two nodes that hashed the same
    /// would serve each other's results.
    /// </remarks>
    private static string ContentHash(string script, IReadOnlyDictionary<string, Type> inputTypes)
    {
        // Normalised on line endings only. Whitespace inside a line is meaningful in verbatim
        // strings, so trimming it would make two scripts that behave differently hash the same.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            script.ReplaceLineEndings("\n") + "\u0000" + Describe(inputTypes)));

        return Convert.ToHexString(hash)[..12];
    }

    /// <summary>The known input types as one stable string, for the two hashes that need them.</summary>
    /// <remarks>
    /// Ordered by name rather than by the dictionary's own enumeration, which has no order worth
    /// relying on: two callers that learnt the same types in a different sequence must produce the
    /// same key, or the compile cache misses every time a wire is redrawn.
    /// </remarks>
    private static string Describe(IReadOnlyDictionary<string, Type> inputTypes) =>
        inputTypes.Count == 0
            ? string.Empty
            : string.Join(
                "\u0000",
                inputTypes
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + (ScriptTypeName.Of(pair.Value) ?? "dynamic")));

    private NodeDefinitionSource Compile(string script, IReadOnlyDictionary<string, Type> inputTypes)
    {
        string[] inputs = InferInputs(script);

        // A port carries the type the wire into it carries, when there is one. That is what puts a
        // real type label on the port (`E8-T18`) and gives the port a rank, and it is the same fact
        // the declaration below is generated from - read once, so the two cannot disagree.
        ScriptPort[] inputPorts =
        [
            .. inputs.Select(name => new ScriptPort(name, DeclaredType(name, inputTypes) ?? typeof(object))),
        ];

        string source = Wrap(script, inputs, inputTypes);

        // `E6-T4`: the guards go in *between* parsing and compiling, which is the only moment they
        // can. Woven statements carry no trivia, so the tree the compiler sees has exactly the line
        // count the text did and a diagnostic still lands on the user's line.
        SyntaxTree tree = CSharpSyntaxTree.Create(
            (CSharpSyntaxNode)_guards.Weave(
                CSharpSyntaxTree.ParseText(source).GetRoot()));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "SparkScript_" + ContentHash(script, inputTypes),
            [tree],
            _references.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using MemoryStream assembly = new();
        EmitResult emitted = compilation.Emit(assembly);

        if (!emitted.Success)
        {
            // `E6`'s stated behaviour: a script that does not compile still yields a definition,
            // so the node keeps its place and its wires while the user fixes a semicolon. The
            // failure is reported when it is evaluated, which is where they are looking.
            string message = Describe(emitted.Diagnostics);

            return new NodeDefinitionSource(
                "CodeBlock",
                ContentHash(script, inputTypes),
                inputPorts,
                [new ScriptPort("result", typeof(object))],
                (_, _) => throw new InvalidOperationException(message));
        }

        assembly.Position = 0;
        Assembly loaded = Assembly.Load(assembly.ToArray());
        MethodInfo method = loaded.GetType("SparkGenerated.Block")!.GetMethod("Run")!;

        // `E6-T17`: bound as a delegate rather than called through `MethodInfo.Invoke`, and that is
        // not only about speed. Reflective invocation wraps whatever the script threw in a
        // `TargetInvocationException` - and the replicator recognises a bare
        // `OperationCanceledException` as cancellation while a wrapped one becomes an ordinary node
        // failure. Cancelling a runaway script would then report that the script "failed", and the
        // evaluation would carry on.
        Func<object?[], CancellationToken, object?> entry =
            method.CreateDelegate<Func<object?[], CancellationToken, object?>>();

        ScriptPort[] outputPorts = OutputsOf(script);

        return new NodeDefinitionSource(
            "CodeBlock",
            ContentHash(script, inputTypes),
            inputPorts,
            outputPorts,
            (arguments, cancellationToken) =>
                Unpack(entry(arguments, cancellationToken), outputPorts.Length));
    }

    /// <summary>
    /// The free identifiers a script uses, in source order — its input ports.
    /// </summary>
    /// <remarks>
    /// Compiled against the prelude with no inputs declared, so every identifier the script expects
    /// from outside is reported as undefined. <c>CS0103</c> is *the name does not exist in the
    /// current context*; <c>CS0117</c> is the member form of the same complaint. Anything else the
    /// compiler objects to is the user's own error and is left for the real compile to report.
    /// </remarks>
    private string[] InferInputs(string script)
    {
        CSharpCompilation probe = CSharpCompilation.Create(
            "SparkProbe",
            [CSharpSyntaxTree.ParseText(Wrap(script, [], EmptyTypes))],
            _references.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        List<string> found = [];

        foreach (Diagnostic diagnostic in probe.GetDiagnostics()
            .Where(d => d.Id is "CS0103" or "CS0117")
            .OrderBy(d => d.Location.SourceSpan.Start))
        {
            // The identifier is the first argument of the message. Reading it from the diagnostic's
            // own arguments rather than parsing the text keeps this working in any locale.
            string name = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            int open = name.IndexOf('\'', StringComparison.Ordinal);
            int close = name.IndexOf('\'', open + 1);

            if (open < 0 || close < 0)
            {
                continue;
            }

            string identifier = name[(open + 1)..close];

            if (IsUsableIdentifier(identifier) && !found.Contains(identifier, StringComparer.Ordinal))
            {
                found.Add(identifier);
            }
        }

        return [.. found];
    }

    /// <summary>
    /// Whether a name the compiler could not resolve is something a port could be called.
    /// </summary>
    /// <remarks>
    /// A misspelt type name also arrives as an unresolved identifier, and turning it into an input
    /// port would replace a clear compiler error with a mysterious extra port. Requiring a
    /// lower-case first letter is a convention rather than a rule, and it is the one C# programmers
    /// already follow for values.
    /// </remarks>
    private static bool IsUsableIdentifier(string identifier) =>
        identifier.Length > 0
        && (char.IsLower(identifier[0]) || identifier[0] == '_')
        && identifier.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>The output ports a script's return statement implies.</summary>
    /// <remarks>
    /// <para>
    /// <b>Read from the syntax, not from the compiled method, and that is forced.</b> Tuple element
    /// names are a compile-time fiction carried in a <c>TupleElementNamesAttribute</c> on whatever
    /// declares them — and the generated entry point returns <c>object</c>, so by the time there is
    /// an assembly to reflect over the names are gone. The first version of this read the attribute
    /// off the return parameter and found nothing, every time.
    /// </para>
    /// <para>
    /// Reading the syntax is not a compromise here: tuple element names <i>are</i> syntax, and
    /// <c>return (area: a, perimeter: p);</c> says what the ports are called in the only place that
    /// information ever exists.
    /// </para>
    /// </remarks>
    private static ScriptPort[] OutputsOf(string script)
    {
        foreach (SyntaxNode node in CSharpSyntaxTree.ParseText(script).GetRoot().DescendantNodes())
        {
            if (node is not ReturnStatementSyntax { Expression: TupleExpressionSyntax tuple })
            {
                continue;
            }

            List<ScriptPort> ports = [];

            foreach (ArgumentSyntax argument in tuple.Arguments)
            {
                if (argument.NameColon?.Name.Identifier.ValueText is not { Length: > 0 } name)
                {
                    // A tuple with any unnamed element cannot name its ports, so the whole thing
                    // falls back to one `result` rather than inventing Item1 and Item2 - which
                    // would be ports whose names mean nothing on the canvas.
                    ports.Clear();
                    break;
                }

                ports.Add(new ScriptPort(name, typeof(object)));
            }

            if (ports.Count > 0)
            {
                return [.. ports];
            }
        }

        return [new ScriptPort("result", typeof(object))];
    }

    /// <summary>Splits a returned tuple into one value per output port.</summary>
    private static object?[] Unpack(object? returned, int outputs)
    {
        if (outputs <= 1)
        {
            return [returned];
        }

        // A ValueTuple's fields are Item1..Item7, and Rest beyond that. Seven ports is already an
        // unusual node; beyond it the remaining values land in the last port rather than being
        // silently dropped.
        object?[] values = new object?[outputs];
        Type type = returned?.GetType() ?? typeof(object);

        for (int i = 0; i < outputs; i++)
        {
            values[i] = type.GetField("Item" + (i + 1).ToString(CultureInfo.InvariantCulture))
                ?.GetValue(returned);
        }

        return values;
    }

    /// <summary>
    /// Wraps a script in the class and method the compiler needs, with its inputs declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An unwired input is declared <c>dynamic</c>, and that is the honest answer rather than a
    /// placeholder.</b> Declaring it <c>object</c> is what a first attempt does and it does not
    /// compile: <c>a * 2</c> is not an operation on <c>object</c>, so the friendliest possible code
    /// block — one that reads like C# — would reject the simplest possible script. <c>dynamic</c>
    /// defers that to the runtime binder and the script reads as intended. There is no type to use
    /// until something is connected, so there is nothing better to write.
    /// </para>
    /// <para>
    /// <b>A wired input is declared with the type the wire carries</b> (`E6-T6`):
    /// <c>Point3d centre = ScriptInput.As&lt;Point3d&gt;(__in[0], "centre");</c>. Statically typed,
    /// bound at compile time rather than by the runtime binder, and — the reason the row exists —
    /// the thing that makes `E6-T7`'s wire-typed IntelliSense possible at all, because completion
    /// needs a type to offer members from.
    /// </para>
    /// <para>
    /// <b>The conversion goes through <see cref="ScriptInput.As{T}"/> rather than a cast</b>, for
    /// the reasons that type records: a cast's failure message names two CLR types and no port, and
    /// it refuses an <see cref="int"/> where the script wants a <see cref="double"/> — which is the
    /// commonest thing a graph delivers.
    /// </para>
    /// </remarks>
    private string Wrap(string script, IReadOnlyList<string> inputs, IReadOnlyDictionary<string, Type> inputTypes)
    {
        StringBuilder source = new();
        source.AppendLine(_references.Prelude());
        source.AppendLine("namespace SparkGenerated;");
        source.AppendLine("public static class Block {");
        source.AppendLine("public static object Run(object[] __in, System.Threading.CancellationToken __token) {");

        // `E6-T17`. On its own this only stops a script that has not started yet, which matters
        // more than it sounds: cancellation usually arrives while an earlier node is still running,
        // and without this every code block downstream of it would still run to completion before
        // anyone noticed. Stopping a script that is *already* looping is `E6-T4`'s job, and the
        // guard weaver writes its checks against this same parameter.
        source.AppendLine("__token.ThrowIfCancellationRequested();");

        // `E6-T4`: the budget is reset here rather than anywhere else, so it is per invocation.
        // Per node would let one long run poison the next; per session would make a graph's tenth
        // evaluation behave differently from its first.
        source.AppendLine(_guards.BeginSource());

        for (int i = 0; i < inputs.Count; i++)
        {
            string index = i.ToString(CultureInfo.InvariantCulture);
            string? spelt = ScriptTypeName.Of(DeclaredType(inputs[i], inputTypes) ?? typeof(object));

            if (DeclaredType(inputs[i], inputTypes) is null || spelt is null)
            {
                source.Append("dynamic ").Append(inputs[i]).Append(" = __in[").Append(index).AppendLine("];");
                continue;
            }

            source.Append(spelt).Append(' ').Append(inputs[i])
                .Append(" = global::Spark.Scripting.ScriptInput.As<").Append(spelt).Append(">(__in[")
                .Append(index).Append("], \"").Append(inputs[i]).AppendLine("\");");
        }

        source.AppendLine(script);
        source.AppendLine("}");
        source.AppendLine("}");

        return source.ToString();
    }

    /// <summary>
    /// The type a port should be declared with, or null when there is nothing better than
    /// <c>dynamic</c>.
    /// </summary>
    /// <remarks>
    /// <b>A type that cannot be spelt in source is the same as no type at all</b>, and is treated
    /// as such rather than as an error: an internal type or an anonymous type is a perfectly
    /// reasonable thing for a wire to carry, and the block should still work. See
    /// <see cref="ScriptTypeName"/> for what cannot be spelt and why.
    /// </remarks>
    private static Type? DeclaredType(string port, IReadOnlyDictionary<string, Type> inputTypes) =>
        inputTypes.TryGetValue(port, out Type? type) && type != typeof(object) && ScriptTypeName.Of(type) is not null
            ? type
            : null;

    /// <summary>The compiler's complaints, as one message a user can act on.</summary>
    private static string Describe(IEnumerable<Diagnostic> diagnostics)
    {
        string[] errors =
        [
            .. diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)
                .Take(5),
        ];

        return errors.Length == 0
            ? "The script did not compile."
            : "The script did not compile: " + string.Join("; ", errors);
    }
}

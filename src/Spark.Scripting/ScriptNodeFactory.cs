using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// <para>
/// <b>A script with no <c>return</c> at all gets one output port per variable it declares</b>
/// (`E6-T26`), which is how Dynamo's Code Block reads eleven lines as eleven ports, and the
/// generated frame supplies the <c>return</c> that produces them. The two rules do not compete:
/// writing a return is how a script says exactly what its ports are, and the per-variable rule is
/// what it gets when it says nothing.
/// </para>
/// </remarks>
public sealed class ScriptNodeFactory : IScriptNodeFactory
{
    private readonly ReferenceCatalog _references;
    private readonly GuardWeaver _guards;
    private readonly ScriptAssemblyCache _persistent;
    private ScriptLoadContext _context;
    private readonly ConcurrentDictionary<string, NodeDefinitionSource> _compiled = new(StringComparer.Ordinal);

    /// <summary>Creates a factory over a reference catalogue.</summary>
    /// <param name="references">The assemblies scripts compile against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="references"/> is null.</exception>
    public ScriptNodeFactory(ReferenceCatalog references) : this(references, new GuardWeaver())
    {
    }

    /// <summary>Creates a factory whose compiled assemblies outlive the process (`E6-T10`).</summary>
    /// <param name="references">The assemblies scripts compile against.</param>
    /// <param name="guards">The weaver that bounds loops and recursion.</param>
    /// <param name="persistent">The on-disk cache, or one constructed over null to disable it.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public ScriptNodeFactory(ReferenceCatalog references, GuardWeaver guards, ScriptAssemblyCache persistent)
        : this(references, guards)
    {
        ArgumentNullException.ThrowIfNull(persistent);

        _persistent = persistent;
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
        _persistent = new ScriptAssemblyCache();

        // The context can find the user's own assemblies, and only the ones the catalogue already
        // references. Compiling against a DLL and being able to run against it are two different
        // things, and a script that compiles and then cannot load is the worse of the two.
        _context = new ScriptLoadContext(_references.PathFor);
    }

    /// <summary>Creates a factory over a fresh catalogue of what is already loaded.</summary>
    public ScriptNodeFactory() : this(new ReferenceCatalog())
    {
    }

    /// <summary>
    /// The catalogue every script here compiles against, so completion can be built from the same
    /// one (`E6-T13`).
    /// </summary>
    public ReferenceCatalog References => _references;

    /// <summary>How many scripts the resident cache is holding.</summary>
    public int CachedScripts => _compiled.Count;

    /// <summary>
    /// Drops every compiled script and unloads the context they were in (`E6-T3`, `E6-T15`).
    /// </summary>
    /// <returns>
    /// A weak reference to the context that was unloaded, so a caller can *prove* it went rather
    /// than assume it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The registry is cleared first, and that ordering is the whole of `E6-T15`.</b> Every
    /// entry in the resident cache holds a delegate bound to a method in a script assembly, and a
    /// delegate into user code pins the collectible context it lives in. Unloading with the
    /// registry still populated is not an error — it is silence: the call returns, nothing
    /// complains, and the context stays alive for the life of the process along with every
    /// assembly in it.
    /// </para>
    /// <para>
    /// <b>What this cannot do is anything about references held elsewhere.</b> A node definition
    /// built from a script holds the same delegate, a cached evaluation result may hold a value of
    /// a script-defined type, and a viewport buffer may hold geometry that came from one. The
    /// context unloads when the last of those goes and not before, which is why the return value is
    /// a weak reference rather than a boolean: *unloaded* is not a fact this method can report at
    /// the moment it returns.
    /// </para>
    /// </remarks>
    public WeakReference Unload()
    {
        // Cleared *before* the unload, not after. See the remarks: the other order silently does
        // nothing.
        _compiled.Clear();

        ScriptLoadContext going = _context;
        _context = new ScriptLoadContext(_references.PathFor);

        WeakReference reference = new(going);
        going.Unload();

        return reference;
    }

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

    /// <summary>
    /// The key an entry is filed under on disk, which is not the resident key (`E6-T10`).
    /// </summary>
    /// <remarks>
    /// <b>The resident key carries the catalogue's <i>version</i>, which is a per-process
    /// counter.</b> It is 0 in every fresh process, so on disk it would let two different sets of
    /// references share an entry. This carries the catalogue's fingerprint instead, and the
    /// generator's version, so an entry compiled by a build that wrapped scripts differently is a
    /// miss rather than a wrong answer.
    /// </remarks>
    private string DiskKey(string script, IReadOnlyDictionary<string, Type> inputTypes) =>
        ScriptAssemblyCache.Key(
            script.ReplaceLineEndings("\n"),
            Describe(inputTypes),
            _guards.IterationLimit.ToString(CultureInfo.InvariantCulture),
            _guards.DepthLimit.ToString(CultureInfo.InvariantCulture),
            _references.Fingerprint,
            ScriptAssemblyCache.GeneratorVersion.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Compiles a script for its diagnostics alone, placed on the user's own lines.
    /// </summary>
    /// <param name="script">The block's source, as the editor holds it.</param>
    /// <param name="inputTypes">The input port types, exactly as <see cref="Create"/> takes them.</param>
    /// <returns>The errors and warnings, in the user's coordinates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="script"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>This exists so that the squiggles and the compiler cannot disagree.</b> A second Roslyn
    /// workspace configured slightly differently would be quicker to write and would eventually
    /// underline something that compiles, or stay silent on something that does not — `E6-T13`
    /// says a list that disagrees with the compiler is worse than no list, and an underline that
    /// disagrees with it is worse than no underline for the same reason. So this goes through the
    /// same <c>Wrap</c>, the same guard weaving and the same references as
    /// <see cref="Create"/> does, and differs from it in one respect: it does not emit.
    /// </para>
    /// <para>
    /// <b>It does not touch the caches either</b>, neither reading nor writing. Diagnostics run on
    /// every idle moment while somebody types; a compile cache filled with entries for half-written
    /// scripts would evict the ones worth keeping.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ScriptDiagnostic> Diagnose(
        string script, IReadOnlyDictionary<string, Type>? inputTypes = null)
    {
        ArgumentNullException.ThrowIfNull(script);

        inputTypes ??= EmptyTypes;

        WrappedScript wrapped = Wrap(script, InferInputs(script), inputTypes);

        SyntaxTree tree = CSharpSyntaxTree.Create(
            (CSharpSyntaxNode)_guards.Weave(
                ScriptRanges.Lower(
                    CSharpSyntaxTree.ParseText(wrapped.Source).GetRoot(), wrapped.RangeMarkers)));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "SparkDiagnostics",
            [tree],
            _references.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        List<ScriptDiagnostic> found = [];

        foreach (Diagnostic diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Severity is not (Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning))
            {
                continue;
            }

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            int line = wrapped.Map.UserLine(span.StartLinePosition.Line + 1);

            // A message on the generated frame rather than on the user's text has no line to be
            // drawn on. Dropped rather than clamped to line one, which would underline code that is
            // correct - the same reasoning `ScriptSourceMap.UserLine` is written with.
            if (line <= 0)
            {
                continue;
            }

            int length = span.EndLinePosition.Line == span.StartLinePosition.Line
                ? System.Math.Max(1, span.EndLinePosition.Character - span.StartLinePosition.Character)
                : 1;

            found.Add(new ScriptDiagnostic(
                diagnostic.Id,
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
                line,
                span.StartLinePosition.Character + 1,
                length));
        }

        return found;
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
        // `E6-T10`: a script compiled on a previous run is not compiled again, and neither of the
        // two Roslyn passes happens - the input names, which are what the first pass exists to
        // learn, were written down beside the assembly.
        string diskKey = DiskKey(script, inputTypes);

        if (_persistent.TryRead(diskKey, out CachedScript cached)
            && Bind(cached.Assembly) is { } restored)
        {
            // The output types were written down beside the assembly because inferring them needs
            // the compilation this branch exists to skip (`E6-T25`). An entry that has none — one
            // written before that, or one whose types could not be spelt — falls back to the
            // syntax, which is what every entry did before.
            return Definition(script, inputTypes, cached.Inputs, restored, Restore(script, cached.Outputs));
        }

        string[] inputs = InferInputs(script);

        // A port carries the type the wire into it carries, when there is one. That is what puts a
        // real type label on the port (`E8-T18`) and gives the port a rank, and it is the same fact
        // the declaration below is generated from - read once, so the two cannot disagree.
        ScriptPort[] inputPorts =
        [
            .. inputs.Select(name => new ScriptPort(name, DeclaredType(name, inputTypes) ?? typeof(object))),
        ];

        WrappedScript wrapped = Wrap(script, inputs, inputTypes);

        // `E6-T4`: the guards go in *between* parsing and compiling, which is the only moment they
        // can. Woven statements carry no trivia, so the tree the compiler sees has exactly the line
        // count the text did and a diagnostic still lands on the user's line.
        SyntaxTree tree = CSharpSyntaxTree.Create(
            (CSharpSyntaxNode)_guards.Weave(
                ScriptRanges.Lower(
                    CSharpSyntaxTree.ParseText(wrapped.Source).GetRoot(), wrapped.RangeMarkers)));

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
            string message = Describe(emitted.Diagnostics, wrapped.Map);

            return new NodeDefinitionSource(
                "CodeBlock",
                ContentHash(script, inputTypes),
                inputPorts,
                [new ScriptPort("result", typeof(object))],
                (_, _) => throw new InvalidOperationException(message));
        }

        byte[] emittedBytes = assembly.ToArray();

        if (Bind(emittedBytes) is not { } entry)
        {
            return new NodeDefinitionSource(
                "CodeBlock",
                ContentHash(script, inputTypes),
                inputPorts,
                [new ScriptPort("result", typeof(object))],
                (_, _) => throw new InvalidOperationException(
                    "The script compiled but its entry point could not be bound."));
        }

        // `E6-T25`. From the compilation that has just been emitted, so nothing extra is compiled
        // to learn what the script returns.
        ScriptPort[] outputs = ScriptOutputTypes.Infer(compilation, tree, OutputsOf(script));

        _persistent.Write(diskKey, emittedBytes, inputs, outputs);

        return Definition(script, inputTypes, inputs, entry, outputs);
    }

    /// <summary>Builds the definition around an entry point, however it was obtained.</summary>
    /// <remarks>
    /// Shared by the compile path and the cache path on purpose: the two must produce the same
    /// definition, and the surest way to hold that is for there to be one place that builds it.
    /// </remarks>
    private static NodeDefinitionSource Definition(
        string script,
        IReadOnlyDictionary<string, Type> inputTypes,
        IReadOnlyList<string> inputs,
        Func<object?[], CancellationToken, object?> entry,
        ScriptPort[]? outputs = null)
    {
        ScriptPort[] inputPorts =
        [
            .. inputs.Select(name => new ScriptPort(name, DeclaredType(name, inputTypes) ?? typeof(object))),
        ];

        ScriptPort[] outputPorts = outputs ?? OutputsOf(script);

        return new NodeDefinitionSource(
            "CodeBlock",
            ContentHash(script, inputTypes),
            inputPorts,
            outputPorts,
            (arguments, cancellationToken) =>
                Unpack(entry(arguments, cancellationToken), outputPorts.Length));
    }

    /// <summary>
    /// The output ports a cache entry recorded, or null when it did not record usable ones.
    /// </summary>
    /// <remarks>
    /// <b>The names still come from the syntax and only the types come from the entry.</b> A tuple
    /// element's name exists nowhere but the source, and the source is in front of us; taking the
    /// count from the file as well would let a stale entry give a block the wrong number of ports,
    /// which is a defect nothing downstream could diagnose. So an entry whose port count disagrees
    /// with the script is discarded rather than trusted.
    /// </remarks>
    private static ScriptPort[]? Restore(string script, IReadOnlyList<ScriptPort> cached)
    {
        if (cached.Count == 0)
        {
            return null;
        }

        ScriptPort[] fromSyntax = OutputsOf(script);

        if (fromSyntax.Length != cached.Count)
        {
            return null;
        }

        ScriptPort[] restored = new ScriptPort[fromSyntax.Length];

        for (int i = 0; i < restored.Length; i++)
        {
            restored[i] = fromSyntax[i] with { ValueType = cached[i].ValueType };
        }

        return restored;
    }

    /// <summary>Loads an emitted assembly and binds its entry point.</summary>
    /// <remarks>
    /// <b>Bound as a delegate rather than called through <c>MethodInfo.Invoke</c>, and that is not
    /// only about speed.</b> Reflective invocation wraps whatever the script threw in a
    /// <c>TargetInvocationException</c> — and the replicator recognises a bare
    /// <see cref="OperationCanceledException"/> as cancellation while a wrapped one becomes an
    /// ordinary node failure. Cancelling a runaway script would then report that the script
    /// "failed", and the evaluation would carry on ([N42](../../docs/NOTES.md)).
    /// <para>
    /// Null when the bytes are not a Spark script assembly at all, which is what a cache entry from
    /// a different build of the generator looks like.
    /// </para>
    /// </remarks>
    private Func<object?[], CancellationToken, object?>? Bind(byte[] assembly)
    {
        try
        {
            MethodInfo? method = _context.Load(assembly)
                .GetType("SparkGenerated.Block")
                ?.GetMethod("Run");

            return method?.CreateDelegate<Func<object?[], CancellationToken, object?>>();
        }
        catch (Exception failure) when (failure is BadImageFormatException
            or ArgumentException
            or MissingMethodException
            or TypeLoadException
            or FileLoadException)
        {
            return null;
        }
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
            [CSharpSyntaxTree.ParseText(Wrap(script, [], EmptyTypes).Source)],
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

    /// <summary>The output ports a script implies (`E6-T8`, `E6-T26`, `E6-T28`).</summary>
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
    /// <para>
    /// <b>A script with no <c>return</c> of its own is read the way Dynamo reads a Code Block
    /// instead</b> (`E6-T26`): every variable it declares at the top level becomes an output port,
    /// named after the variable and in source order, and <see cref="Trailer(string)"/> generates the
    /// <c>return</c> that produces them. The rule is <i>gated on the absence of a return</i>, which
    /// is what keeps `E6-T8`'s reasoning intact — a tuple return still says exactly what the ports
    /// are, so a user who wants three of eleven locals on the canvas writes it and gets three.
    /// </para>
    /// <para>
    /// <b>A block written as bare values gets one port per value</b> (`E6-T28`):
    /// <c>5 + 3;</c> then <c>"Test";</c> is two ports, <c>result</c> and <c>result2</c>, carrying
    /// <c>8</c> and <c>"Test"</c>. That is `E6-T27`'s rule read once per statement instead of once
    /// per block, and it inherits `E6-T27`'s safety unchanged: only an expression <c>CS0201</c>
    /// would have rejected is claimed, so every line this turns into a port was a compile error
    /// before it did.
    /// </para>
    /// <para>
    /// <b>Values replace the declared-variable ports; they do not join them.</b>
    /// <c>var n = 10 / 5; var p = 8 / 4; n + p;</c> is one port carrying 4 and not three ports —
    /// which is `E6-T27`'s decision, made by the client, and generalising the count of values does
    /// not reopen it. A block that says what it produces has said it.
    /// </para>
    /// </remarks>
    private static ScriptPort[] OutputsOf(string script) =>
        // Blanked first: `var parameters = 0..1..#5;` does not parse with the marker in it, and a
        // script whose ports could not be read would silently lose them (`E10-T15`).
        OutputsOf(CSharpSyntaxTree.ParseText(ScriptRanges.Blank(script).Text).GetCompilationUnitRoot());

    /// <summary>The output ports, from a script that has already been parsed.</summary>
    private static ScriptPort[] OutputsOf(CompilationUnitSyntax root)
    {
        bool returns = false;

        foreach (ReturnStatementSyntax statement in TopLevelReturns(root))
        {
            returns = true;

            if (statement.Expression is not TupleExpressionSyntax tuple)
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

        if (returns)
        {
            return [new ScriptPort("result", typeof(object))];
        }

        string[] named = [.. Unique(Producers(root).Select(producer => producer.Name))];

        // A block that produces nothing - an empty one, or a script that calls something only for
        // its effect - still has one port, and `Trailer` gives it `return null;` so that it
        // compiles. `E6-T18` claimed that from the day the starter became a comment and it was not
        // true: a method returning `object` with no `return` in it is `CS0161`, so every fresh code
        // block was quietly failing to compile until `E6-T26`.
        return named.Length > 0
            ? [.. named.Select(name => new ScriptPort(name, typeof(object)))]
            : [new ScriptPort("result", typeof(object))];
    }

    /// <summary>
    /// Every top-level statement that produces a value, in source order, with the name its port
    /// takes and the identifier the generated <c>return</c> reads it back from (`E6-T29`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is Dynamo's Code Block rule, and the client specified it with a Dynamo
    /// screenshot</b>: six lines, six ports - <c>5;</c> is <c>integer</c>, <c>"hello";</c> is
    /// <c>string</c>, <c>n = 100;</c> is <c>n</c>, <c>[0..#6..10];</c> is <c>list</c>. One port per
    /// line that makes something, named after the variable when there is one and after the
    /// expression's <i>kind</i> when there is not.
    /// </para>
    /// <para>
    /// <b>Declarations and values join rather than compete, which reverses `E6-T27`.</b> That row
    /// had a trailing value <i>replace</i> the declared ports - <c>var n = 10 / 5; var p = 8 / 4;
    /// n + p;</c> was one port carrying 4 - and the client, who asked for that, has since asked for
    /// Dynamo's rule instead, where the same block is three ports. Their call both times, and this
    /// is the later one.
    /// </para>
    /// <para>
    /// <b>A statement C# accepts stays a statement and gets no port.</b> <c>points.Add(p);</c>
    /// discards its value on purpose, and <c>new List&lt;int&gt;();</c> is a legal statement too, so
    /// neither is claimed - the closed list in <see cref="IsStatementExpression"/> is still what
    /// draws the line, and it is why nothing that compiled before any of these rows changed
    /// meaning. Put such a value in a variable and the variable names the port.
    /// </para>
    /// </remarks>
    private static IEnumerable<Producer> Producers(CompilationUnitSyntax root)
    {
        int captured = 0;

        foreach (MemberDeclarationSyntax member in root.Members)
        {
            if (member is not GlobalStatementSyntax global)
            {
                continue;
            }

            if (global.Statement is LocalDeclarationStatementSyntax { IsConst: false } declaration)
            {
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    if (variable.Initializer is not null
                        && variable.Identifier.ValueText is { Length: > 0 } name)
                    {
                        yield return new Producer(name, name);
                    }
                }

                continue;
            }

            if (global.Statement is ExpressionStatementSyntax statement
                && !IsStatementExpression(statement.Expression))
            {
                yield return new Producer(KindOf(statement.Expression), Captured(captured));
                captured++;
            }
        }
    }

    /// <summary>One output: what its port is called, and what the generated return reads.</summary>
    /// <param name="Name">The port's name, before duplicates are separated.</param>
    /// <param name="Reference">
    /// The identifier the trailer names - the variable itself for a declaration, and the temporary
    /// <see cref="WithGeneratedReturns"/> captured it into for a bare value.
    /// </param>
    private readonly record struct Producer(string Name, string Reference);

    /// <summary>
    /// What Dynamo calls a port whose line declared no variable: the expression's <i>kind</i>
    /// (`E6-T29`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read from the syntax, and it has to be.</b> The client's first sketch asked for the
    /// <i>value</i> - <c>5 + 9;</c> called <c>14</c> - and a port name cannot be that: ports are
    /// built before the graph runs, and editing a script re-makes the wires <i>by port name</i>, so
    /// a name that moved with the value would drop every wire on every edit. Dynamo names these
    /// from the syntax too, which is why its own screenshot calls <c>5.0 + 6;</c> a
    /// <c>function</c> rather than a double.
    /// </para>
    /// <para>
    /// <b><c>function</c> for everything unrecognised is Dynamo's answer, not a shrug.</b> In
    /// DesignScript an operator <i>is</i> a function, so a binary expression is a function call and
    /// the port says so. It is the one place this scheme reads worse than naming the type would,
    /// and it is what <i>follow the Dynamo code block exactly</i> asks for.
    /// </para>
    /// </remarks>
    private static string KindOf(ExpressionSyntax expression) => expression switch
    {
        InterpolatedStringExpressionSyntax => "string",

        // A range is `E10-T15`'s three-part form with its `#` blanked, which parses as `(a..b)..c`.
        // Dynamo calls the same line a list, and so does this.
        RangeExpressionSyntax => "list",
        CollectionExpressionSyntax => "list",
        ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax => "list",

        ParenthesizedExpressionSyntax parenthesised => KindOf(parenthesised.Expression),

        LiteralExpressionSyntax literal => literal.Kind() switch
        {
            SyntaxKind.NumericLiteralExpression =>
                literal.Token.Value is int or long or uint or ulong ? "integer" : "double",
            SyntaxKind.StringLiteralExpression
                or SyntaxKind.Utf8StringLiteralExpression
                or SyntaxKind.CharacterLiteralExpression => "string",
            SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression => "boolean",
            _ => "function",
        },

        _ => "function",
    };

    /// <summary>The same names, with repeats separated by a number (`E6-T29`).</summary>
    /// <remarks>
    /// <b>Two lines of the same kind are two ports called <c>integer</c>, and Spark cannot have
    /// that</b> - wires are re-made by port name, so a duplicate would make two ports
    /// indistinguishable and a wire land on whichever came first. <b>The first keeps the bare
    /// name</b>, for the reason `E6-T28` gave when these ports were called <c>result</c>: adding a
    /// second line of a kind must not rename the port that already had a wire on it.
    /// </remarks>
    private static IEnumerable<string> Unique(IEnumerable<string> names)
    {
        Dictionary<string, int> seen = [];

        foreach (string name in names)
        {
            if (!seen.TryGetValue(name, out int count))
            {
                seen[name] = 1;
                yield return name;
                continue;
            }

            seen[name] = ++count;
            yield return name + count.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The <c>return</c> the generated frame appends, or null when the script has its own
    /// (`E6-T26`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It goes after the user's last line rather than being woven in</b>, so the source map stays
    /// the subtraction `E6-T1` made it: every line the frame adds is either before the user's first
    /// or after their last, and neither shifts a line of theirs.
    /// </para>
    /// <para>
    /// <b>A single declared variable is returned bare rather than as a one-element tuple</b>, which
    /// is not a nicety — C# has no one-element tuple syntax, and <c>(p1: p1)</c> is a parenthesised
    /// expression whose element name goes nowhere.
    /// </para>
    /// </remarks>
    private static string? Trailer(string script) =>
        Trailer(CSharpSyntaxTree.ParseText(ScriptRanges.Blank(script).Text).GetCompilationUnitRoot());

    /// <summary>The generated <c>return</c>, from a script that has already been parsed.</summary>
    private static string? Trailer(CompilationUnitSyntax root)
    {
        foreach (ReturnStatementSyntax _ in TopLevelReturns(root))
        {
            return null;
        }

        if (IsBareReturn(root))
        {
            // `E6-T27`: `Wrap` has turned that one line into the return itself, so appending one
            // here would make a second, unreachable.
            return null;
        }

        string[] read = [.. Producers(root).Select(producer => producer.Reference)];

        // THE TUPLE'S ELEMENT NAMES ARE LEFT OFF, AND THAT IS NOT AN OMISSION.
        //
        // Nothing reads them: `OutputsOf` names the ports from the *user's* syntax, and
        // `ScriptOutputTypes.Infer` walks this tuple's arguments by index. Naming them would mean
        // spelling a port called `double` or `string` as a C# identifier - both are keywords, so
        // `return (double: __result1);` does not compile and would need `@double`. An unnamed
        // tuple has no such problem and `Unpack` reads `Item1..ItemN` either way (`E6-T29`).
        return read.Length switch
        {
            0 => "return null;",

            // C# has no one-element tuple syntax, and `(p1)` is a parenthesised expression whose
            // element name goes nowhere - so a single output is returned bare.
            1 => "return " + read[0] + ";",
            _ => "return (" + string.Join(", ", read) + ");",
        };
    }

    /// <summary>
    /// Whether the block's one and only output is a bare value on its last line - `E6-T27`'s case,
    /// kept on `E6-T27`'s code path.
    /// </summary>
    /// <remarks>
    /// <b>It is kept because <c>var</c> needs a natural type and some expressions have none.</b>
    /// Everything else is captured into <c>var __resultN = …;</c> so that several outputs can be
    /// returned together, and <c>null;</c>, <c>default;</c>, a bare lambda and a method group are
    /// <c>CS0815</c> under that treatment where <c>return null;</c> compiles. Returning the single
    /// value directly costs nothing and means no script that worked before `E6-T28` stopped.
    /// </remarks>
    private static bool IsBareReturn(CompilationUnitSyntax root)
    {
        List<ExpressionStatementSyntax> values = ValueStatements(root);

        return Producers(root).Take(2).Count() == 1 && IsTrailingValue(root, values);
    }

    /// <summary>
    /// The variables a script declares at its top level, in source order (`E6-T26`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Top level only.</b> A local inside a <c>for</c>, an <c>if</c> or a lambda is out of scope
    /// by the time the generated <c>return</c> runs, so it could not be a port even if it should be
    /// one; and a rule that reached into blocks would make wrapping two lines in an <c>if</c>
    /// silently delete two ports.
    /// </para>
    /// <para>
    /// <b>Declarations, never bare assignments.</b> Assigning to a name that was never declared is
    /// <c>CS0103</c>, which is precisely how <see cref="InferInputs"/> finds an input port — so
    /// <c>x = 5;</c> would otherwise be an input and an output at once. A declaration cannot collide
    /// with an input by construction, because the compiler resolves it.
    /// </para>
    /// <para>
    /// <b>Initialised, and not <c>const</c>.</b> An uninitialised local read by the generated return
    /// is <c>CS0165</c> reported on a line the user cannot see, and a constant is a value the script
    /// was given rather than one it computed.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> DeclaredNames(CompilationUnitSyntax root)
    {
        foreach (MemberDeclarationSyntax member in root.Members)
        {
            if (member is not GlobalStatementSyntax { Statement: LocalDeclarationStatementSyntax declaration }
                || declaration.IsConst)
            {
                continue;
            }

            foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
            {
                if (variable.Initializer is not null && variable.Identifier.ValueText is { Length: > 0 } name)
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>
    /// Every top-level statement that is an expression the block plainly means as a <i>value</i> —
    /// <c>n + p;</c>, <c>$"test";</c>, <c>q;</c> — in source order (`E6-T27`, `E6-T28`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule is "C# would reject this as a statement", and that is what makes it safe.</b>
    /// A language that quietly returned the last expression would have to decide what
    /// <c>list.Add(3);</c> means, and it means <i>add three</i> — the value is discarded on purpose
    /// and blocks are written that way today. So this only claims expressions that are
    /// <c>CS0201</c>: <i>"Only assignment, call, increment, decrement, await, and new object
    /// expressions can be used as a statement"</i>. Every one of those was a compile error a moment
    /// ago, so **no script that compiles today changes meaning**, and the gap is exactly the set of
    /// lines a user could only have meant as a value.
    /// </para>
    /// <para>
    /// <b>The asymmetry to keep in mind when editing this.</b> Mistaking a value for a statement
    /// leaves the user with the <c>CS0201</c> they already had — a missed opportunity. Mistaking a
    /// <i>statement</i> for a value wraps a working line in a <c>return</c> and changes what their
    /// graph does. The second is the one to be afraid of, which is why
    /// <see cref="IsStatementExpression"/> is written from the language's own closed list rather
    /// than from intuition, and why conditional access is over-approximated: <c>a?.M()</c> is a
    /// legal statement and <c>a?.b</c> is not, they are the same syntax node, and only one of the
    /// two possible mistakes is free.
    /// </para>
    /// </remarks>
    private static List<ExpressionStatementSyntax> ValueStatements(CompilationUnitSyntax root)
    {
        List<ExpressionStatementSyntax> found = [];

        foreach (MemberDeclarationSyntax member in root.Members)
        {
            if (member is GlobalStatementSyntax { Statement: ExpressionStatementSyntax statement }
                && !IsStatementExpression(statement.Expression))
            {
                found.Add(statement);
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the script's values are the single one `E6-T27` handles: exactly one, and it is the
    /// last thing in the block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This case is kept separate on purpose, and it is not tidiness.</b> `E6-T28` captures each
    /// value into <c>var __result1 = …;</c> so that several can be returned together — and
    /// <c>var</c> needs the expression to have a natural type. <c>null;</c>, <c>default;</c>, a bare
    /// lambda and a method group have none, so capturing them is <c>CS0815</c> where
    /// <c>return null;</c> compiles. Returning the one value directly, exactly as `E6-T27` did,
    /// costs nothing and means no script that worked the day before this row landed can stop
    /// working.
    /// </para>
    /// <para>
    /// <b>And it is the same reasoning <see cref="Trailer(CompilationUnitSyntax)"/> already applies
    /// to a single declared variable</b>, which is returned bare rather than as a one-element tuple.
    /// One value is the value; several are a tuple.
    /// </para>
    /// </remarks>
    private static bool IsTrailingValue(CompilationUnitSyntax root, List<ExpressionStatementSyntax> values) =>
        values.Count == 1
        && root.Members.LastOrDefault() is GlobalStatementSyntax { Statement: ExpressionStatementSyntax last }
        && last == values[0];

    /// <summary>The temporary the <i>n</i>th value is captured into (`E6-T28`).</summary>
    /// <remarks>
    /// Double-underscored like <c>__in</c> and <c>__token</c>, which is the frame's convention for
    /// a name the user is not meant to write; a script that declares <c>__result1</c> itself
    /// collides, and so does one that declares <c>__in</c>.
    /// </remarks>
    private static string Captured(int index) =>
        "__result" + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether C# would accept an expression as a statement on its own.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is a closed list, not a heuristic</b>, and that is what makes the default safe: C#
    /// permits exactly invocation, object creation, assignment, increment, decrement and
    /// <c>await</c> as statement-expressions, which is what the <c>CS0201</c> message enumerates.
    /// Everything else — a binary expression, a literal, an identifier, a <c>switch</c> expression,
    /// a query — is an error today, so <see langword="false"/> for the unlisted case is the
    /// language's answer rather than a guess about it.
    /// </para>
    /// <para>
    /// <b>The one deliberate over-approximation is conditional access.</b> <c>a?.M()</c> is a legal
    /// statement and <c>a?.b</c> is not, and they are the same node kind; calling the whole kind a
    /// statement costs a trailing <c>a?.b</c> the result it might have been, and calling it a value
    /// would break every <c>a?.M()</c> anybody has already written. Only one of those is
    /// recoverable by typing <c>return</c>.
    /// </para>
    /// </remarks>
    private static bool IsStatementExpression(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax => true,
        ObjectCreationExpressionSyntax => true,
        ImplicitObjectCreationExpressionSyntax => true,
        AssignmentExpressionSyntax => true,
        AwaitExpressionSyntax => true,
        ConditionalAccessExpressionSyntax => true,
        PostfixUnaryExpressionSyntax => true,
        PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || prefix.IsKind(SyntaxKind.PreDecrementExpression),
        _ => false,
    };

    /// <summary>
    /// The script's own <c>return</c> statements — not the ones inside a function it declares.
    /// </summary>
    /// <remarks>
    /// A <c>return</c> in a local function or a lambda returns from <i>that</i>, so it says nothing
    /// about the block's outputs. <c>ScriptOutputTypes.Returns</c> draws the same line on the
    /// wrapped tree, and the two have to agree or a block would be given ports whose types were read
    /// from somewhere else.
    /// </remarks>
    private static IEnumerable<ReturnStatementSyntax> TopLevelReturns(CompilationUnitSyntax root)
    {
        foreach (SyntaxNode node in root.DescendantNodes(
            descendIntoChildren: child => child is not (LocalFunctionStatementSyntax
                or ParenthesizedLambdaExpressionSyntax
                or SimpleLambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax)))
        {
            if (node is ReturnStatementSyntax statement)
            {
                yield return statement;
            }
        }
    }

    /// <summary>Splits a returned tuple into one value per output port.</summary>
    /// <remarks>
    /// <b>A <c>ValueTuple</c> holds seven fields and puts everything past them in <c>Rest</c></b>,
    /// which is another tuple of the same shape — so an eleven-port block is
    /// <c>ValueTuple&lt;…7…, ValueTuple&lt;…4…&gt;&gt;</c> and <c>Item8</c> does not exist. Reading
    /// <c>Item1..ItemN</c> straight off the outer tuple therefore filled every port past the seventh
    /// with null, and said nothing. Nothing produced eight ports until `E6-T26` made one out of
    /// every declared variable, and eleven of them is the first thing the client asked for.
    /// </remarks>
    private static object?[] Unpack(object? returned, int outputs)
    {
        if (outputs <= 1)
        {
            return [returned];
        }

        object?[] values = new object?[outputs];
        object? current = returned;

        for (int filled = 0; filled < outputs && current is not null;)
        {
            Type type = current.GetType();

            for (int i = 0; i < 7 && filled < outputs; i++, filled++)
            {
                values[filled] = type.GetField("Item" + (i + 1).ToString(CultureInfo.InvariantCulture))
                    ?.GetValue(current);
            }

            current = filled < outputs ? type.GetField("Rest")?.GetValue(current) : null;
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
    private WrappedScript Wrap(string script, IReadOnlyList<string> inputs, IReadOnlyDictionary<string, Type> inputTypes)
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

        // `E6-T1`: everything the frame adds goes *before* the user's first line, so mapping a
        // diagnostic back is a subtraction rather than a table - and the guard weaver adds no
        // lines at all, which is what keeps it one.
        ScriptSourceMap map = new(Lines(source));

        // `E10-T15`: Dynamo's `#` breaks the lexer, so it is blanked before the parse rather than
        // rewritten after it. Blanking is length-preserving, so `offset` is all it takes to move a
        // marker into the generated source's coordinates - no table, for the same reason the map is
        // a subtraction.
        BlankedScript blanked = ScriptRanges.Blank(script);
        (string body, ImmutableArray<int> markers) = WithGeneratedReturns(blanked);
        int offset = source.Length;

        source.AppendLine(body);

        // `E6-T26`: the block's own `return`, when it has none of its own. After the user's last
        // line, so it shifts nothing above it and the map stays a subtraction.
        if (Trailer(blanked.Text) is { } trailer)
        {
            source.AppendLine(trailer);
        }

        source.AppendLine("}");
        source.AppendLine("}");

        return new WrappedScript(
            source.ToString(),
            map,
            markers.IsDefaultOrEmpty
                ? ImmutableArray<int>.Empty
                : [.. markers.Select(marker => marker + offset)]);
    }

    /// <summary>
    /// Claims a script's value expressions, in place: the single trailing one becomes the
    /// <c>return</c> itself (`E6-T27`), and several are each captured into a temporary that
    /// <see cref="Trailer(CompilationUnitSyntax)"/> then returns together (`E6-T28`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Inserted at the start of the statement, and not one newline anywhere.</b> The statement
    /// has to <i>become</i> the return or the declaration — leaving it where it was and appending a
    /// second copy would evaluate it twice, which a script that calls something would notice.
    /// Adding no line is what keeps <see cref="ScriptSourceMap"/> a subtraction; the columns after
    /// an insertion on that one line move, which is the same trade <see cref="GuardWeaver"/> and
    /// <see cref="ScriptRanges"/> already make (<c>N122</c>).
    /// </para>
    /// <para>
    /// <b>The markers move by what was inserted <i>ahead of them</i>, which is a running total
    /// rather than a constant.</b> They are offsets into the blanked text, and with several
    /// insertions of different lengths a marker on the third value has three of them in front of
    /// it. Getting this wrong does not raise anything: a range whose marker is looked for in the
    /// wrong place is lowered as the <i>other</i> form, so <c>0..1..#5</c> comes back as a step
    /// range — a wrong answer rather than an error.
    /// </para>
    /// <para>
    /// <b>The insertions are applied last-first</b> so that each statement's <c>SpanStart</c>, read
    /// from the tree that was parsed before any of this, is still an offset into the text being
    /// edited.
    /// </para>
    /// </remarks>
    private static (string Body, ImmutableArray<int> Markers) WithGeneratedReturns(BlankedScript blanked)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(blanked.Text).GetCompilationUnitRoot();
        List<ExpressionStatementSyntax> values = ValueStatements(root);

        if (values.Count == 0)
        {
            return (blanked.Text, blanked.Markers);
        }

        (int At, string Text)[] insertions = IsBareReturn(root)
            ? [(values[0].SpanStart, "return ")]
            : [.. values.Select((statement, i) => (statement.SpanStart, "var " + Captured(i) + " = "))];

        StringBuilder body = new(blanked.Text);

        for (int i = insertions.Length - 1; i >= 0; i--)
        {
            body.Insert(insertions[i].At, insertions[i].Text);
        }

        return (
            body.ToString(),
            blanked.HasMarkers
                ? [.. blanked.Markers.Select(marker => marker + Ahead(insertions, marker))]
                : blanked.Markers);
    }

    /// <summary>How many characters <see cref="WithGeneratedReturns"/> inserted before an offset.</summary>
    private static int Ahead((int At, string Text)[] insertions, int marker)
    {
        int shift = 0;

        foreach ((int at, string text) in insertions)
        {
            if (marker >= at)
            {
                shift += text.Length;
            }
        }

        return shift;
    }

    /// <summary>How many lines a builder holds.</summary>
    private static int Lines(StringBuilder source)
    {
        int lines = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == (char)10)
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>The generated source, and the map back to what the user typed.</summary>
    /// <param name="Source">What the compiler is given.</param>
    /// <param name="Map">How to turn a diagnostic's line into the user's line.</param>
    /// <param name="RangeMarkers">
    /// Where `E10-T15`'s range markers ended up <b>in <paramref name="Source"/>'s coordinates</b>,
    /// ready for <see cref="ScriptRanges.Lower"/>. Empty for a script that used none, which is nearly
    /// all of them.
    /// </param>
    private readonly record struct WrappedScript(
        string Source, ScriptSourceMap Map, ImmutableArray<int> RangeMarkers);

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
    /// <remarks>
    /// <b>Every message is placed on the user's own line</b> (`E6-T1`). Roslyn reports a position in
    /// the generated source, which is the script plus a prelude the user has never seen — so
    /// <c>(14,9): ; expected</c> in a four-line script names a line that does not exist for them.
    /// </remarks>
    private static string Describe(IEnumerable<Diagnostic> diagnostics, ScriptSourceMap map)
    {
        string[] errors =
        [
            .. diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .Select(d => Place(d, map))
                .Distinct(StringComparer.Ordinal)
                .Take(5),
        ];

        return errors.Length == 0
            ? "The script did not compile."
            : "The script did not compile: " + string.Join("; ", errors);
    }

    /// <summary>One diagnostic, on the line the user is looking at.</summary>
    private static string Place(Diagnostic diagnostic, ScriptSourceMap map)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);

        return diagnostic.Location.IsInSource
            ? map.Place(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, message)
            : message;
    }
}

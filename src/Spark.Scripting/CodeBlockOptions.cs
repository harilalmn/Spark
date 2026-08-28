using System;
using System.Collections.Generic;
using System.Threading;
using Spark.Engine;

namespace Spark.Scripting;

/// <summary>
/// Everything the compiler needs beyond the text itself: what is wired to the block, what it may
/// reference, where compiled assemblies are kept, and how long it may run.
/// </summary>
/// <remarks>
/// None of this except <see cref="ConnectedInputTypes"/> takes part in the compile cache key, so
/// changing a time budget or a display name does not throw away a compilation.
/// </remarks>
public sealed class CodeBlockOptions
{
    /// <summary>
    /// The path named in <c>#line</c> directives and therefore in stack traces from inside the
    /// block. It need not exist on disk.
    /// </summary>
    public string FilePath { get; init; } = "codeblock.cs";

    /// <summary>
    /// The static type on each wired input port, by port name.
    /// </summary>
    /// <remarks>
    /// <b>This is the feature.</b> A port whose name appears here is declared as that type in the
    /// generated source instead of as <see cref="object"/>, so the compiler — and therefore
    /// completion inside the editor — knows the type on the incoming wire. A port that is not
    /// listed types as <see cref="object"/>, which still accepts anything.
    /// </remarks>
    public IReadOnlyDictionary<string, Type>? ConnectedInputTypes { get; init; }

    /// <summary>The assemblies the block compiles against.</summary>
    public ReferenceCatalog References { get; init; } = ReferenceCatalog.Default;

    /// <summary>Where compiled assemblies are cached. Pass a fresh one to isolate a test.</summary>
    public ScriptCompilationCache Cache { get; init; } = ScriptCompilationCache.Shared;

    /// <summary>
    /// How long one invocation may run before the woven guards stop it.
    /// <see cref="TimeSpan.Zero"/> means no limit, leaving cancellation as the only way out.
    /// </summary>
    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Supplies the cancellation token the guards watch, asked once per invocation.
    /// </summary>
    /// <remarks>
    /// A function rather than a token because a <see cref="NodeDefinition"/> outlives any one
    /// evaluation run, and the token that matters is the one belonging to the run in progress.
    /// </remarks>
    public Func<CancellationToken>? Cancellation { get; init; }

    /// <summary>The package half of the node key. Node identity is <c>package/CodeBlock</c>.</summary>
    public string Package { get; init; } = "Spark.Scripting";

    /// <summary>The name shown on the canvas.</summary>
    public string DisplayName { get; init; } = "Code Block";
}

/// <summary>
/// The result of compiling one code block: its ports, its diagnostics positioned in the user's own
/// text, the node definition when it succeeded, and the generated source for anything that wants to
/// analyse it.
/// </summary>
public sealed class CodeBlockCompilation
{
    internal CodeBlockCompilation(
        string cacheKey,
        string userText,
        string generatedSource,
        SourceMap map,
        IReadOnlyList<ScriptDiagnostic> diagnostics,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs,
        NodeDefinition? definition,
        bool fromCache)
    {
        CacheKey = cacheKey;
        UserText = userText;
        GeneratedSource = generatedSource;
        Map = map;
        Diagnostics = diagnostics;
        Inputs = inputs;
        Outputs = outputs;
        Definition = definition;
        FromCache = fromCache;
    }

    /// <summary>
    /// The compile cache key. Two code blocks with this key share one compiled assembly, which is
    /// what makes ten copies of the same script cost one compile.
    /// </summary>
    public string CacheKey { get; }

    /// <summary>The exact text that was compiled.</summary>
    public string UserText { get; }

    /// <summary>
    /// The generated compilation unit. The user's text appears in it verbatim, below a <c>#line 1</c>
    /// directive, so line numbers coming out of the compiler match the editor.
    /// </summary>
    public string GeneratedSource { get; }

    /// <summary>The offset map between <see cref="UserText"/> and <see cref="GeneratedSource"/>.</summary>
    public SourceMap Map { get; }

    /// <summary>
    /// Everything the compiler had to say, positioned in the user's own text rather than in the
    /// generated source.
    /// </summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }

    /// <summary>The inferred input ports, in port order.</summary>
    public IReadOnlyList<PortDefinition> Inputs { get; }

    /// <summary>The inferred output ports, in port order. There is always at least one.</summary>
    public IReadOnlyList<PortDefinition> Outputs { get; }

    /// <summary>
    /// The node definition, or <see langword="null"/> when the block did not compile. A definition
    /// from here is an ordinary definition: it caches, replicates and laces like any other node.
    /// </summary>
    public NodeDefinition? Definition { get; }

    /// <summary>Whether the compiled assembly came from a cache rather than from Roslyn.</summary>
    public bool FromCache { get; }

    /// <summary>Whether the block compiled.</summary>
    public bool Success => Definition is not null;
}

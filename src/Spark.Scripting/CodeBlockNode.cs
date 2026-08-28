using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Engine;

namespace Spark.Scripting;

/// <summary>
/// A C# code block as a node: the text, the ports inferred from it, and a
/// <see cref="NodeDefinition"/> the engine evaluates exactly like any other.
/// </summary>
/// <remarks>
/// <para>
/// Instances are immutable. Editing the text or learning what is wired to a port produces a new
/// instance, which is what lets the canvas hold the old ports while the new compile runs and swap
/// them atomically when it finishes.
/// </para>
/// <para>
/// The definition this produces is an ordinary definition. It has a <see cref="NodeKey"/>, its
/// version is derived from the script's content so an edit invalidates cached results computed by the
/// old text, and it replicates and laces through the same code path as a built-in node — a code block
/// fed a list of numbers on a scalar port fans out, like everything else.
/// </para>
/// <para>
/// <b>This runs user code in this process.</b> See <see cref="CodeBlockCompiler"/> for the security
/// posture, which is stated rather than mitigated.
/// </para>
/// </remarks>
public sealed class CodeBlockNode
{
    private CodeBlockNode(string text, CodeBlockOptions options, CodeBlockCompilation compilation)
    {
        Text = text;
        Options = options;
        Compilation = compilation;
    }

    /// <summary>Compiles a code block into a node.</summary>
    /// <param name="text">The C# the user typed. <see langword="null"/> is treated as empty.</param>
    /// <param name="options">What is wired to it and what it may reference. Omit for the defaults.</param>
    /// <returns>The node, valid or not. A block that did not compile still reports its diagnostics.</returns>
    public static CodeBlockNode Create(string? text, CodeBlockOptions? options = null)
    {
        CodeBlockOptions effective = options ?? new CodeBlockOptions();
        return new CodeBlockNode(text ?? string.Empty, effective, CodeBlockCompiler.Compile(text, effective));
    }

    /// <summary>The C# in the block.</summary>
    public string Text { get; }

    /// <summary>The options this block was compiled with.</summary>
    public CodeBlockOptions Options { get; }

    /// <summary>The full compile result, including the generated source and the source map.</summary>
    public CodeBlockCompilation Compilation { get; }

    /// <summary>The node definition, or <see langword="null"/> when the block did not compile.</summary>
    public NodeDefinition? Definition => Compilation.Definition;

    /// <summary>Whether the block compiled.</summary>
    public bool IsValid => Compilation.Success;

    /// <summary>The inferred input ports, in port order.</summary>
    public IReadOnlyList<PortDefinition> Inputs => Compilation.Inputs;

    /// <summary>The inferred output ports, in port order.</summary>
    public IReadOnlyList<PortDefinition> Outputs => Compilation.Outputs;

    /// <summary>Everything the compiler had to say, positioned in the user's own text.</summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics => Compilation.Diagnostics;

    /// <summary>The same diagnostics in the engine's own shape, for a panel that shows all of them together.</summary>
    /// <returns>One <see cref="SparkDiagnostic"/> per script diagnostic.</returns>
    public IReadOnlyList<SparkDiagnostic> EngineDiagnostics()
    {
        List<SparkDiagnostic> diagnostics = new(Compilation.Diagnostics.Count);

        foreach (ScriptDiagnostic diagnostic in Compilation.Diagnostics)
        {
            diagnostics.Add(diagnostic.ToSparkDiagnostic());
        }

        return diagnostics;
    }

    /// <summary>Recompiles this block with different text.</summary>
    /// <param name="text">The new C#.</param>
    /// <returns>A new node. This one is unchanged.</returns>
    public CodeBlockNode WithText(string? text) => Create(text, Options);

    /// <summary>
    /// Recompiles this block knowing the static types on its wired input ports, which is what turns
    /// an <see cref="object"/> port into a typed one.
    /// </summary>
    /// <param name="connectedInputTypes">The upstream port type for each connected input, by port name.</param>
    /// <returns>A new node. This one is unchanged.</returns>
    public CodeBlockNode WithConnectedInputTypes(IReadOnlyDictionary<string, Type>? connectedInputTypes) =>
        Create(Text, new CodeBlockOptions
        {
            FilePath = Options.FilePath,
            ConnectedInputTypes = connectedInputTypes,
            References = Options.References,
            Cache = Options.Cache,
            TimeBudget = Options.TimeBudget,
            Cancellation = Options.Cancellation,
            Package = Options.Package,
            DisplayName = Options.DisplayName,
        });

    /// <inheritdoc/>
    public override string ToString() =>
        IsValid
            ? $"{Options.DisplayName}: {Inputs.Count} in, {Outputs.Count} out"
            : $"{Options.DisplayName}: {Compilation.Diagnostics.Count} diagnostic(s)";
}

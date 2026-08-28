using System;
using System.Collections.Generic;
using Spark.Engine;

namespace Spark.Scripting;

/// <summary>
/// One compiled code block: the assembly, the collectible context holding it, the entry point, and
/// the ports the compile worked out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Disposal order is load-bearing.</b> The delegate is dropped <i>before</i> the context is
/// unloaded, because a live delegate points into the collectible context and pins it: the
/// <c>Unload</c> then silently does nothing at all — no exception, no log — and the assembly stays
/// for the life of the process while the next edit loads another one beside it. This is the failure
/// the prior art paid for and documented, and it is why the order below is not cosmetic.
/// </para>
/// <para>
/// Even done correctly, unloading is best-effort. A <see cref="NodeDefinition"/> built from this
/// script holds its invoker, so nothing is collected until the graph has let that go too.
/// </para>
/// </remarks>
internal sealed class CompiledScript : IDisposable
{
    private readonly ScriptLoadContext _context;
    private Func<object[], object[]>? _run;

    internal CompiledScript(
        string key,
        ScriptLoadContext context,
        Func<object[], object[]> run,
        IReadOnlyList<PortDefinition> inputs,
        IReadOnlyList<PortDefinition> outputs)
    {
        Key = key;
        _context = context;
        _run = run;
        Inputs = inputs;
        Outputs = outputs;
    }

    /// <summary>The compile cache key this script was compiled under.</summary>
    internal string Key { get; }

    /// <summary>The input ports, in port order.</summary>
    internal IReadOnlyList<PortDefinition> Inputs { get; }

    /// <summary>The output ports, in port order.</summary>
    internal IReadOnlyList<PortDefinition> Outputs { get; }

    /// <summary>Whether the script can still be invoked.</summary>
    internal bool IsAlive => _run is not null;

    /// <summary>Runs the code block once.</summary>
    /// <param name="arguments">One argument per input port.</param>
    /// <returns>One value per output port.</returns>
    /// <exception cref="ObjectDisposedException">The script has been unloaded.</exception>
    internal object[] Invoke(object[] arguments)
    {
        Func<object[], object[]> run = _run
            ?? throw new ObjectDisposedException(nameof(CompiledScript),
                "This code block's assembly has been unloaded. Recompile it before running it.");

        return run(arguments);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Order matters. See the type-level remarks.
        _run = null;
        _context.Unload();
    }
}

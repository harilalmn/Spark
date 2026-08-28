using System;
using System.Reflection;
using System.Runtime.Loader;

namespace Spark.Scripting;

/// <summary>
/// Holds exactly one compiled code block. Collectible, so that editing and re-running a code block
/// does not pile up assemblies for the life of the session.
/// </summary>
/// <remarks>
/// <para>
/// A script must see exactly the assemblies Spark sees, or it gets a second, incompatible set of
/// types — a <c>Point3d</c> that is not the <c>Point3d</c> on the wire, a <c>SparkList</c> the
/// engine will not accept. Deferring to the context Spark itself is loaded in answers both halves:
/// Spark's own assemblies resolve there, and anything it does not own is passed on to the default
/// context. Returning <see langword="null"/> would only reach the second half, so a script that
/// touched a Spark type would fail to load or, worse, load a duplicate of it.
/// </para>
/// <para>
/// <b>Unloading is best-effort and frequently does not happen.</b> A collectible context stays alive
/// while <i>anything</i> still refers into it, and the delegate a
/// <see cref="Spark.Engine.NodeDefinition"/> invokes is exactly such a reference. Every field
/// holding one must be cleared before <see cref="AssemblyLoadContext.Unload"/> is called or the
/// unload silently does nothing — no exception, no log line, just an assembly that never goes away.
/// That is what <see cref="CompiledScript.Dispose"/> is careful about, and it is the single most
/// expensive lesson in the prior art this was ported from.
/// </para>
/// </remarks>
internal sealed class ScriptLoadContext : AssemblyLoadContext
{
    private static readonly AssemblyLoadContext? Host = GetLoadContext(typeof(ScriptLoadContext).Assembly);

    internal ScriptLoadContext(string name) : base(name, isCollectible: true)
    {
    }

    /// <inheritdoc/>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (Host is null || Host == Default)
        {
            return null;
        }

        try
        {
            return Host.LoadFromAssemblyName(assemblyName);
        }
        catch (Exception exception) when (exception is BadImageFormatException or System.IO.FileLoadException
                                              or System.IO.FileNotFoundException)
        {
            // Genuinely not resolvable there; let the default context say so.
            return null;
        }
    }
}

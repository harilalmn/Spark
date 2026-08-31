using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Spark.Scripting;

/// <summary>
/// The collectible context every compiled script is loaded into (`E6-T3`).
/// </summary>
/// <remarks>
/// <para>
/// <b>An assembly loaded into the default context can never be unloaded.</b> A code block is
/// recompiled on every edit and on every change to what is wired into it, so a session in which
/// somebody works on one script for ten minutes loads dozens of assemblies — and without a
/// collectible context every one of them stays for the life of the process, with its types, its
/// static fields and its JIT-compiled code.
/// </para>
/// <para>
/// <b>The <c>Load</c> override returns null on purpose, and that is the whole of the resolution
/// policy.</b> Returning null defers to the default context, so a script's reference to
/// <c>Spark.Geometry</c> resolves to the <i>same</i> <c>Spark.Geometry</c> the graph is using. A
/// context that loaded its own copy would give the script a <c>Point3d</c> that is not the
/// <c>Point3d</c> a node produces — the two types would have the same name, the same shape, and no
/// assignment between them would compile. That is the single most confusing failure this layer can
/// produce, and it is avoided by doing nothing.
/// </para>
/// <para>
/// <b>Unloading is best-effort and cannot be checked by asking.</b> A collectible context unloads
/// only when nothing references anything in it — including delegates into user code, cached values
/// of user types, and the compiled invokers a node definition holds. `E6-T15` is the rule that
/// follows: <b>clear every registry before unloading</b>, because an ALC that fails to unload does
/// so silently, and the only honest proof is a weak reference that goes dead.
/// </para>
/// </remarks>
public sealed class ScriptLoadContext : AssemblyLoadContext
{
    private readonly Func<string?, string?>? _locate;

    /// <summary>Creates a collectible context for script assemblies.</summary>
    public ScriptLoadContext() : this(null)
    {
    }

    /// <summary>
    /// Creates a collectible context that can also find a user's own assemblies (<c>E7-T9</c>).
    /// </summary>
    /// <param name="locate">
    /// Given an assembly's simple name, where it lives, or null for none. Consulted <b>only after
    /// the default context has failed</b>, so nothing it returns can shadow a contract assembly.
    /// </param>
    public ScriptLoadContext(Func<string?, string?>? locate) : base("SparkScripts", isCollectible: true)
    {
        _locate = locate;

        // Resolving fires only when the default context could not satisfy the reference, which is
        // exactly the boundary this context wants: Spark.Geometry is found there and shared, and a
        // user's Acme.Maths is not and is found here.
        Resolving += OnResolving;
    }

    /// <summary>Loads an emitted script assembly into this context.</summary>
    /// <param name="assembly">The emitted bytes.</param>
    /// <returns>The loaded assembly.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    /// <remarks>
    /// From a stream rather than a file, because the compiler emitted bytes and writing them out
    /// to load them back would take a lock on a file the cache also wants to replace.
    /// </remarks>
    public Assembly Load(byte[] assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using MemoryStream stream = new(assembly, writable: false);

        return LoadFromStream(stream);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Null means <i>defer to the default context</i>, which is what keeps a script's
    /// <c>Point3d</c> the same type as the graph's. See the remarks on the type.
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName) => null;

    /// <summary>
    /// Finds an assembly the default context could not, among the ones the user has agreed to.
    /// </summary>
    /// <remarks>
    /// <b>Loaded from bytes, not from the path.</b> <c>LoadFromAssemblyPath</c> maps the file and
    /// holds it open for the life of the context, which would mean a user could not rebuild the
    /// very library they added — and not being able to rebuild while Spark is open is the
    /// thing <c>E7-T9</c> exists to prevent.
    /// </remarks>
    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        if (_locate?.Invoke(name.Name) is not { } path)
        {
            return null;
        }

        try
        {
            using MemoryStream bytes = new(File.ReadAllBytes(path), writable: false);

            return LoadFromStream(bytes);
        }
        catch (Exception failure) when (failure is IOException
            or UnauthorizedAccessException
            or BadImageFormatException)
        {
            // Mid-rebuild is the common case. Null lets the runtime report the missing assembly,
            // which is a better message than one about a file being briefly unreadable.
            return null;
        }
    }
}

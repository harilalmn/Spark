using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Spark.Packages;

/// <summary>
/// The collectible load context holding one package at one version (<c>E7-T3</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>One context per package version.</b> Not one per package, which would make it impossible to
/// have two versions loaded at once — the case a graph saved last year and a graph saved today
/// put in front of us the first week anybody uses this. Not one per assembly, which would break
/// type identity <i>inside</i> a package, so a package's own internal types would stop matching
/// each other. The version-scoped middle is the only one that works, and it is
/// <see cref="PackageIdentity"/>'s reason for carrying a version at all.
/// </para>
/// <para>
/// <b>Resolution order is the design, and it is two rules in a fixed sequence.</b>
/// <list type="number">
/// <item>
/// A <see cref="ContractAssemblies">contract assembly</see> always defers to the default context,
/// <b>even when the package folder contains a file of that name</b> — and it usually does, because
/// NuGet packages ship what they were compiled against.
/// </item>
/// <item>
/// Anything else loads from this context's own folder <b>if a file of that name is there</b>, and
/// otherwise defers. Deciding by file existence rather than by a list of known names is
/// deliberate: a hardcoded list rots the moment a package adds a dependency, and it rots silently,
/// because the symptom is a type from the wrong context rather than an error.
/// </item>
/// </list>
/// Reversing those two rules compiles, runs, and produces a <c>Circle</c> that is not a
/// <c>Circle</c>.
/// </para>
/// <para>
/// <b>Unloading is best-effort and cannot be established by asking.</b> A collectible context
/// unloads only when nothing references anything inside it — no node definition, no compiled
/// invoker, no cached value, no viewport buffer, no undo entry. <c>E7-T5</c> is the rule that
/// follows: purge every registry first, and prove the unload with a weak reference that goes
/// dead, because a context that fails to unload does so in silence. <b>Restart is the documented
/// default</b>; a live unload is an optimisation, never a promise.
/// </para>
/// <para>
/// <b>Assemblies are loaded by path, which locks the file on Windows, and that is a choice with a
/// visible consequence.</b> A package folder cannot be deleted or overwritten while its context is
/// alive, so an upgrade that fails to unload cannot replace the files either — the same fact as
/// the paragraph above seen from the filesystem, and the same reason restart is the default.
/// Loading from a byte array would avoid the lock and lose <see cref="Assembly.Location"/>, which
/// is what a diagnostic has to print to answer <i>where did this node come from</i>. Packages live
/// in an immutable, version-scoped cache, so the lock costs nothing restart does not already
/// cover. <b>Local DLL references are the opposite case</b> — a user rebuilds those while Spark is
/// open — so <c>E7-T9</c> has to read them without locking rather than reuse this path.
/// </para>
/// </remarks>
public sealed class PackageLoadContext : AssemblyLoadContext
{
    private readonly string _folder;

    /// <summary>Creates a collectible context for one package version.</summary>
    /// <param name="identity">The package and version this context holds.</param>
    /// <param name="folder">
    /// The directory its assemblies live in. It does not have to exist yet; a context over a
    /// missing folder simply resolves nothing and defers everything, which is the correct
    /// behaviour for a package whose files have been removed underneath us.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is null or blank.</exception>
    public PackageLoadContext(PackageIdentity identity, string folder)
        : base($"SparkPackage:{identity.Id}:{identity.Version}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        Identity = identity;
        _folder = Path.GetFullPath(folder);
    }

    /// <summary>The package and version this context holds.</summary>
    public PackageIdentity Identity { get; }

    /// <summary>The directory this context resolves assemblies from.</summary>
    public string Folder => _folder;

    /// <summary>
    /// Loads one of the package's own assemblies by simple name.
    /// </summary>
    /// <param name="simpleName">The assembly's simple name, without the <c>.dll</c> extension.</param>
    /// <returns>The loaded assembly.</returns>
    /// <exception cref="ArgumentException"><paramref name="simpleName"/> is null or blank.</exception>
    /// <exception cref="FileNotFoundException">
    /// No assembly of that name is in the package folder. This is deliberately not a deferral to
    /// the default context: a caller asking this context for one of <i>its own</i> assemblies and
    /// silently receiving Spark's copy is a bug that would present much later and somewhere else.
    /// </exception>
    public Assembly LoadPackageAssembly(string simpleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(simpleName);

        string path = Path.Combine(_folder, simpleName + ".dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Package '{Identity}' has no assembly '{simpleName}.dll' in {_folder}.", path);
        }

        return LoadFromAssemblyPath(path);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returning null means <i>defer to the default context</i>. See the remarks on the type for
    /// why the contract check has to precede the file check.
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (ContractAssemblies.IsContract(assemblyName.Name))
        {
            return null;
        }

        if (string.IsNullOrEmpty(assemblyName.Name))
        {
            return null;
        }

        string candidate = Path.Combine(_folder, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Native dependencies are looked for in the package's own <c>runtimes/{rid}/native</c> folder
    /// and then beside its managed assemblies, matching NuGet's own layout.
    /// </para>
    /// <para>
    /// <b>Spark itself promises no native dependencies, and a package is entitled to break that
    /// promise on its own behalf — but not silently.</b> Resolving these here is what makes the
    /// disclosure in <c>E7-T8</c> meaningful: the installer can tell a user a package carries
    /// native binaries precisely because this layer knows where they would have to be.
    /// </para>
    /// </remarks>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        string fileName = Path.GetFileName(unmanagedDllName);

        string[] candidates =
        [
            Path.Combine(_folder, "runtimes", rid, "native", fileName),
            Path.Combine(_folder, fileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return LoadUnmanagedDllFromPath(candidate);
            }
        }

        return IntPtr.Zero;
    }
}

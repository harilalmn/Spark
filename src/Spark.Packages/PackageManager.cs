using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Spark.Engine;

namespace Spark.Packages;

/// <summary>What loading one package produced.</summary>
/// <param name="Identity">The package and version.</param>
/// <param name="Nodes">How many node definitions it contributed.</param>
/// <param name="Problems">
/// Assemblies its manifest named that could not be loaded or read, with the reason. <b>Empty is
/// the normal case</b>; a package that loaded partly is reported rather than silently reduced.
/// </param>
public sealed record PackageLoadReport(
    PackageIdentity Identity, int Nodes, IReadOnlyList<string> Problems);

/// <summary>
/// Loads installed packages into a node library, and unloads them again (<c>E7-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the sentence the epic exists for</b>: install a package and use its nodes. Everything
/// under it is already built — the store knows what is installed, the manifest says which
/// assemblies hold nodes, the load context isolates them while sharing the contract, and the
/// importer turns public static methods into definitions. This is the wiring.
/// </para>
/// <para>
/// <b>Unloading is best-effort and the registries have to be purged first.</b> A collectible
/// context is pinned by anything reachable inside it: a node definition, a compiled invoker, a
/// cached value, a viewport buffer, an undo entry. Removing the definitions from the library is
/// necessary and is <i>not</i> sufficient, which is why <see cref="Unload"/> returns a weak
/// reference rather than a boolean. <b>Restart is the documented default</b> and a live unload is
/// an optimisation.
/// </para>
/// <para>
/// <b>Nothing here decides whether a package should be trusted.</b> That is
/// <see cref="PackageTrustStore"/>'s record and a person's decision; by the time a package reaches
/// this class, somebody has already said yes.
/// </para>
/// </remarks>
public sealed class PackageManager
{
    private readonly Dictionary<PackageIdentity, PackageLoadContext> _loaded = [];
    private readonly Dictionary<PackageIdentity, List<NodeKey>> _contributed = [];
    private readonly PackageStore _store;
    private readonly NodeLibrary _library;

    /// <summary>Creates a manager over a store and the library packages contribute to.</summary>
    /// <param name="store">Where installed packages live.</param>
    /// <param name="library">The library their nodes are added to.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public PackageManager(PackageStore store, NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(library);

        _store = store;
        _library = library;
    }

    /// <summary>The packages currently loaded.</summary>
    public IReadOnlyCollection<PackageIdentity> Loaded => _loaded.Keys;

    /// <summary>The node keys one loaded package contributed.</summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>Its keys, or empty when it is not loaded.</returns>
    public IReadOnlyList<NodeKey> NodesOf(PackageIdentity identity) =>
        _contributed.TryGetValue(identity, out List<NodeKey>? keys) ? keys : [];

    /// <summary>
    /// Loads one installed package and adds its nodes to the library.
    /// </summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>What it contributed, and what did not work.</returns>
    /// <exception cref="SparkPackageException">
    /// It is not installed, or its manifest cannot be read.
    /// </exception>
    /// <remarks>
    /// <b>The nodes are imported under the package's own id</b>, not the assembly's, so two
    /// packages shipping an assembly of the same name do not collide and a node's key says where
    /// it came from. That is also what makes a missing-package placeholder legible: the key names
    /// the package the user has to install.
    /// </remarks>
    public PackageLoadReport Load(PackageIdentity identity)
    {
        if (_loaded.ContainsKey(identity))
        {
            return new PackageLoadReport(identity, NodesOf(identity).Count, []);
        }

        SparkPackageManifest manifest = _store.ManifestOf(identity);

        if (!manifest.IsReadable)
        {
            throw new SparkPackageException(
                $"'{identity}' was built for a newer Spark: its manifest is schema {manifest.Schema} "
                + $"and this build reads {SparkPackageManifest.CurrentSchema}.");
        }

        PackageLoadContext context = new(identity, _store.FolderFor(identity));
        List<NodeKey> added = [];
        List<string> problems = [];

        foreach (string name in manifest.Assemblies)
        {
            try
            {
                Assembly assembly = context.LoadPackageAssembly(name);
                ImportReport report = NodeImporter.Import(assembly, identity.Id);

                foreach (NodeDefinition definition in report.Definitions())
                {
                    // A key already in the library is a genuine clash - two packages claiming the
                    // same node - and it is reported rather than silently resolved either way.
                    // Whichever rule was chosen, a user whose node quietly changed meaning would
                    // have no way to find out why.
                    if (_library.TryGet(definition.Key, out _))
                    {
                        problems.Add(
                            $"{definition.Key} is already provided by something else and was not added.");
                        continue;
                    }

                    _library.Add(definition);
                    added.Add(definition.Key);
                }
            }
            catch (Exception failure) when (failure is FileNotFoundException
                or BadImageFormatException
                or ReflectionTypeLoadException
                or FileLoadException)
            {
                // One unreadable assembly does not sink the package: the others may be fine, and a
                // package that loaded partly is far more useful reported than refused.
                problems.Add($"{name}: {failure.Message}");
            }
        }

        _loaded[identity] = context;
        _contributed[identity] = added;

        return new PackageLoadReport(identity, added.Count, problems);
    }

    /// <summary>Loads every installed package.</summary>
    /// <returns>One report per package, in the store's order.</returns>
    /// <remarks>
    /// A package that will not load is reported rather than thrown, because one bad package must
    /// not stop the application starting — the user needs to get in to remove it.
    /// </remarks>
    public IReadOnlyList<PackageLoadReport> LoadAll()
    {
        List<PackageLoadReport> reports = [];

        foreach (PackageIdentity identity in _store.Installed())
        {
            try
            {
                reports.Add(Load(identity));
            }
            catch (SparkPackageException failure)
            {
                reports.Add(new PackageLoadReport(identity, 0, [failure.Message]));
            }
        }

        return reports;
    }

    /// <summary>
    /// Purges a package's nodes from the library and unloads its context (<c>E7-T5</c>).
    /// </summary>
    /// <param name="identity">The package and version.</param>
    /// <returns>
    /// A weak reference to the load context, or <see langword="null"/> when it was not loaded.
    /// <b>The caller checks whether it went dead</b>; this method cannot honestly return a boolean.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Purging is necessary and not sufficient, and the return type says so.</b> Everything this
    /// class put in the library is taken out again, which is the part it can guarantee. Whether the
    /// context then unloads depends on every other thing that might hold a reference into it —
    /// a cached value of a package type, a compiled invoker, a viewport buffer, an entry in the
    /// undo stack. A method returning <c>true</c> here would be claiming to know about all of them.
    /// </para>
    /// <para>
    /// <b>The only honest proof is a weak reference that goes dead</b>, and an ALC that fails to
    /// unload does so silently. That is why restart is the documented default for an upgrade.
    /// </para>
    /// </remarks>
    public WeakReference? Unload(PackageIdentity identity)
    {
        if (!_loaded.TryGetValue(identity, out PackageLoadContext? context))
        {
            return null;
        }

        foreach (NodeKey key in NodesOf(identity))
        {
            _library.Remove(key);
        }

        _ = _loaded.Remove(identity);
        _ = _contributed.Remove(identity);

        WeakReference reference = new(context);
        context.Unload();
        return reference;
    }
}

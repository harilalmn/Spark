using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Spark.Engine;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// <b>The sentence the whole epic exists for</b>: install a package from a feed and use its nodes
/// (<c>E7</c>'s goal, <c>E7-T5</c>'s unload).
/// </summary>
/// <remarks>
/// <para>
/// The package these tests install carries a <b>real assembly</b> — <c>Spark.Nodes.Core.dll</c>,
/// copied into a <c>.nupkg</c> built by the test. That is deliberate: a hand-made stub would
/// prove the plumbing and nothing about whether the importer, the load context and the contract
/// rule actually work together on something with a hundred real nodes in it.
/// </para>
/// <para>
/// It also exercises the rule that matters most. <c>Spark.Nodes.Core</c> references
/// <c>Spark.Api</c> and <c>Spark.Geometry</c>, which are <b>contract assemblies</b> — so a package
/// loading it must get Spark's copies rather than its own, or the <c>Point3d</c> its nodes return
/// would not be the <c>Point3d</c> a graph understands.
/// </para>
/// </remarks>
public sealed class PackageManagerTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;
    private readonly string _store;

    /// <summary>Creates a scratch feed and store.</summary>
    public PackageManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-manager-tests", Guid.NewGuid().ToString("n"));
        _feed = Path.Combine(_root, "feed");
        _store = Path.Combine(_root, "store");
        Directory.CreateDirectory(_feed);
        Directory.CreateDirectory(_store);
    }

    /// <summary>Removes them.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// <b>Install a package, and its nodes are in the library.</b> The whole epic in one test.
    /// </summary>
    [Fact]
    public async Task AnInstalledPackageContributesItsNodes()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        PackageManager manager = new(store, library);

        PackageLoadReport report = manager.Load(identity);

        Assert.Empty(report.Problems);
        Assert.True(report.Nodes > 50, $"expected the core node library's nodes, got {report.Nodes}");
        Assert.Equal(report.Nodes, library.Count);
    }

    /// <summary>
    /// <b>Nodes are keyed by the package that shipped them, not the assembly.</b> Two packages
    /// shipping an assembly of the same name must not collide, and a node's key has to name the
    /// package a user would have to install — which is what makes a placeholder legible.
    /// </summary>
    [Fact]
    public async Task NodesAreKeyedByThePackageThatShippedThem()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        new PackageManager(store, library).Load(identity);

        Assert.All(
            library.Definitions(),
            definition => Assert.Equal("Acme.Nodes", definition.Key.Package));
    }

    /// <summary>
    /// <b>The contract rule holds across the package boundary.</b> A geometry type returned by a
    /// package's node is the same <see cref="Type"/> the host uses — which is the difference
    /// between a wire that connects and an error naming the same type twice.
    /// </summary>
    [Fact]
    public async Task AGeometryTypeFromAPackageIsTheHostsType()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        new PackageManager(store, library).Load(identity);

        NodeDefinition point = library.Definitions()
            .First(definition => definition.DisplayName.StartsWith("Point.", StringComparison.Ordinal));

        Type produced = point.Outputs[0].ValueType;

        Assert.Equal(typeof(Spark.Geometry.Point3d).Assembly, produced.Assembly);
        Assert.Same(typeof(Spark.Geometry.Point3d).Assembly, produced.Assembly);
    }

    /// <summary>Loading a package twice is idempotent rather than a duplicate-key failure.</summary>
    [Fact]
    public async Task LoadingTwiceIsIdempotent()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        PackageManager manager = new(store, library);

        int first = manager.Load(identity).Nodes;
        int second = manager.Load(identity).Nodes;

        Assert.Equal(first, second);
        Assert.Equal(first, library.Count);
    }

    /// <summary>
    /// <b>Unloading purges the library</b>, which is the half <c>E7-T5</c> can guarantee.
    /// </summary>
    [Fact]
    public async Task UnloadingRemovesExactlyWhatThePackageAdded()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        PackageManager manager = new(store, library);
        manager.Load(identity);

        Assert.True(library.Count > 0);

        manager.Unload(identity);

        Assert.Equal(0, library.Count);
        Assert.Empty(manager.Loaded);
        Assert.Empty(manager.NodesOf(identity));
    }

    /// <summary>
    /// <b>And the context can then genuinely go</b> — the only honest proof, since an ALC that
    /// fails to unload does so silently.
    /// </summary>
    [Fact]
    public async Task TheContextIsCollectableOnceThePackageIsPurged()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        WeakReference reference = LoadAndUnload(store, identity);

        for (int attempt = 0; attempt < 20 && reference.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(
            reference.IsAlive,
            "the package's load context is still alive after purging and unloading, so its "
            + "assemblies can never be released");
    }

    /// <summary>Unloading something that was never loaded reports so rather than throwing.</summary>
    [Fact]
    public void UnloadingSomethingNotLoadedReturnsNull()
    {
        PackageManager manager = new(new PackageStore(_store), new NodeLibrary());

        Assert.Null(manager.Unload(PackageIdentity.Create("Acme.Nothing", "1.0.0")));
    }

    /// <summary>
    /// A manifest naming an assembly the package does not contain is reported, and the rest of the
    /// package still loads. One bad name must not sink a working package.
    /// </summary>
    [Fact]
    public async Task AMissingAssemblyIsReportedAndTheRestStillLoads()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Partial", "1.0.0");
        BuildPackageAround(
            identity,
            typeof(Spark.Nodes.Core.Point).Assembly,
            manifestAssemblies: ["Spark.Nodes.Core", "Acme.NotThere"]);

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        PackageLoadReport report = new PackageManager(store, library).Load(identity);

        Assert.Contains("Acme.NotThere", Assert.Single(report.Problems), StringComparison.Ordinal);
        Assert.True(report.Nodes > 50, "the assembly that was present should still have loaded");
    }

    /// <summary>Loading every installed package returns one report each.</summary>
    [Fact]
    public async Task LoadingAllReturnsOneReportPerPackage()
    {
        PackageStore store = new(_store);

        foreach (string version in (string[])["1.0.0", "2.0.0"])
        {
            PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", version);
            BuildPackageAround(identity, typeof(Spark.Nodes.Core.Point).Assembly);
            await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);
        }

        NodeLibrary library = new();
        var reports = new PackageManager(store, library).LoadAll();

        Assert.Equal(2, reports.Count);

        // The second version's nodes collide with the first's, by design: both claim the same
        // keys. That is reported rather than silently resolved.
        Assert.NotEmpty(reports[1].Problems);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(PackageStore store, PackageIdentity identity)
    {
        NodeLibrary library = new();
        PackageManager manager = new(store, library);
        manager.Load(identity);

        return manager.Unload(identity)!;
    }

    private NuGetPackageClient Client() => new(_feed);

    /// <summary>Builds a <c>.nupkg</c> carrying a real assembly and everything it needs.</summary>
    private void BuildPackageAround(
        PackageIdentity identity, System.Reflection.Assembly assembly, string[]? manifestAssemblies = null)
    {
        string simpleName = assembly.GetName().Name!;
        string path = Path.Combine(_feed, $"{identity.Id}.{identity.Version}.nupkg");

        using FileStream file = File.Create(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        Write(archive, $"{identity.Id}.nuspec", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{identity.Id}</id>
                <version>{identity.Version}</version>
                <authors>Acme Ltd</authors>
                <description>A package carrying a real assembly.</description>
                <tags>{SparkPackageManifest.Tag}</tags>
              </metadata>
            </package>
            """);

        Write(archive, SparkPackageManifest.PathInPackage,
            SparkPackageManifest.Write(manifestAssemblies ?? [simpleName]));

        // The assembly itself, at the top of the package folder, which is where the load context
        // looks. Its dependencies are deliberately NOT shipped: Spark.Api and Spark.Geometry are
        // contract assemblies and must come from the host.
        archive.CreateEntryFromFile(assembly.Location, simpleName + ".dll");
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using Stream entry = archive.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}

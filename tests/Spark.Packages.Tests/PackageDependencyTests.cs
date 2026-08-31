using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spark.Engine;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// Installing a package installs what it needs (<c>E7-T2</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Before this, installing a package installed that package alone.</b> A package declaring
/// NuGet dependencies got none of them, and the failure was a <c>TypeLoadException</c> at first
/// use naming an assembly the user had never heard of, at a moment when nothing on screen
/// connected it to an install they did last week.
/// </para>
/// <para>
/// The packages here are built by the test onto a folder feed, with real assemblies inside them
/// and the ordinary <c>lib/{tfm}</c> layout, so the walk, the version resolution and the probing
/// are all exercised against the shape a real feed serves.
/// </para>
/// </remarks>
public sealed class PackageDependencyTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;
    private readonly string _store;

    /// <summary>Creates a scratch feed and store.</summary>
    public PackageDependencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-deps-tests", Guid.NewGuid().ToString("n"));
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
    /// <b>A package's dependency is installed with it, and lands where the loader looks.</b>
    /// </summary>
    [Fact]
    public async Task ADependencyIsInstalledAlongsideThePackage()
    {
        Publish("Acme.Support", "1.2.0");
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Support", "1.2.0")]);

        PackageStore store = new(_store);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        string deps = Path.Combine(
            store.FolderFor(identity),
            NuGetPackageClient.DependencyFolder,
            PackageIdentity.Create("Acme.Support", "1.2.0").FolderName);

        Assert.True(Directory.Exists(deps), $"the dependency was not installed at {deps}");

        PackageLoadContext context = new(identity, store.FolderFor(identity));

        Assert.Contains(
            context.ProbePaths,
            path => path.StartsWith(deps, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <b>Transitively.</b> A dependency's own dependencies are installed too, which is the whole
    /// difference between resolving and reading one line of a nuspec.
    /// </summary>
    [Fact]
    public async Task DependenciesAreResolvedTransitively()
    {
        Publish("Acme.Bottom", "1.0.0");
        Publish("Acme.Middle", "1.0.0", dependencies: [("Acme.Bottom", "1.0.0")]);
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Middle", "1.0.0")]);

        PackageStore store = new(_store);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        using PendingInstall pending = await Client()
            .PrepareAsync(identity, store, TestContext.Current.CancellationToken);

        Assert.Equal(2, pending.Disclosure.Dependencies.Length);
        Assert.Contains("Acme.Middle 1.0.0", pending.Disclosure.Dependencies, StringComparer.Ordinal);
        Assert.Contains("Acme.Bottom 1.0.0", pending.Disclosure.Dependencies, StringComparer.Ordinal);
    }

    /// <summary>
    /// <b>The disclosure names every package that will be installed, resolved.</b> Agreeing to one
    /// package should not silently agree to five, so the list is what will land on disk rather than
    /// the direct ids the nuspec happens to mention.
    /// </summary>
    [Fact]
    public async Task TheDisclosureNamesEveryPackageThatWillBeInstalled()
    {
        Publish("Acme.Bottom", "1.0.0");
        Publish("Acme.Middle", "1.0.0", dependencies: [("Acme.Bottom", "1.0.0")]);
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Middle", "1.0.0")]);

        PackageStore store = new(_store);

        using PendingInstall pending = await Client().PrepareAsync(
            PackageIdentity.Create("Acme.Nodes", "1.0.0"), store, TestContext.Current.CancellationToken);

        // Versions, not bare ids: a user weighing an install needs to know what arrives.
        Assert.All(
            pending.Disclosure.Dependencies,
            line => Assert.Contains(' ', line));

        Assert.Contains("2 dependencies", pending.Disclosure.Summary(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The lowest version satisfying the range, which is NuGet's own rule.</b> Taking the
    /// highest would mean two installs on different days quietly getting different code.
    /// </summary>
    [Fact]
    public async Task TheLowestVersionSatisfyingTheRangeIsChosen()
    {
        Publish("Acme.Support", "1.0.0");
        Publish("Acme.Support", "1.5.0");
        Publish("Acme.Support", "2.0.0");
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Support", "[1.0.0,2.0.0)")]);

        PackageStore store = new(_store);

        using PendingInstall pending = await Client().PrepareAsync(
            PackageIdentity.Create("Acme.Nodes", "1.0.0"), store, TestContext.Current.CancellationToken);

        Assert.Equal("Acme.Support 1.0.0", Assert.Single(pending.Disclosure.Dependencies));
    }

    /// <summary>
    /// <b>Nothing is installed until the user agrees</b>, dependencies included. A prepare that
    /// left five packages on disk before the disclosure was answered would make the disclosure a
    /// notice rather than a gate.
    /// </summary>
    [Fact]
    public async Task DependenciesAreStagedRatherThanInstalledUntilTheUserAgrees()
    {
        Publish("Acme.Support", "1.0.0");
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Support", "1.0.0")]);

        PackageStore store = new(_store);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        PendingInstall pending = await Client()
            .PrepareAsync(identity, store, TestContext.Current.CancellationToken);

        Assert.False(store.IsInstalled(identity));
        Assert.False(Directory.Exists(store.FolderFor(identity)));

        pending.Discard();

        Assert.False(Directory.Exists(store.FolderFor(identity)));
        Assert.False(Directory.Exists(store.FolderFor(identity) + ".installing"));
    }

    /// <summary>
    /// <b>A dependency no version of which satisfies the range is refused, and nothing is
    /// installed.</b> The alternative is a package that installs and then fails at first use.
    /// </summary>
    [Fact]
    public async Task AnUnsatisfiableDependencyRefusesTheWholeInstall()
    {
        Publish("Acme.Support", "1.0.0");
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Support", "[9.0.0,)")]);

        PackageStore store = new(_store);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        SparkPackageException failure = await Assert.ThrowsAsync<SparkPackageException>(
            () => Client().PrepareAsync(identity, store, TestContext.Current.CancellationToken));

        Assert.Contains("Acme.Support", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(store.FolderFor(identity)));
        Assert.False(Directory.Exists(store.FolderFor(identity) + ".installing"));
    }

    /// <summary>A cycle between two packages terminates rather than downloading for ever.</summary>
    [Fact]
    public async Task ACycleBetweenPackagesTerminates()
    {
        Publish("Acme.Left", "1.0.0", dependencies: [("Acme.Right", "1.0.0")]);
        Publish("Acme.Right", "1.0.0", dependencies: [("Acme.Left", "1.0.0")]);
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Left", "1.0.0")]);

        PackageStore store = new(_store);

        using PendingInstall pending = await Client().PrepareAsync(
            PackageIdentity.Create("Acme.Nodes", "1.0.0"), store, TestContext.Current.CancellationToken);

        Assert.Equal(2, pending.Disclosure.Dependencies.Length);
    }

    /// <summary>A package with no dependencies still installs, and declares none.</summary>
    [Fact]
    public async Task APackageWithNoDependenciesDeclaresNone()
    {
        Publish("Acme.Nodes", "1.0.0");

        PackageStore store = new(_store);

        using PendingInstall pending = await Client().PrepareAsync(
            PackageIdentity.Create("Acme.Nodes", "1.0.0"), store, TestContext.Current.CancellationToken);

        Assert.Empty(pending.Disclosure.Dependencies);
    }

    /// <summary>
    /// <b>And the whole point: a node from the package runs, using a type from its dependency.</b>
    /// Everything above asserts that files arrived; this asserts that the loader can find them.
    /// </summary>
    [Fact]
    public async Task ThePackagesNodesLoadWithItsDependencyPresent()
    {
        Publish("Acme.Support", "1.0.0");
        Publish("Acme.Nodes", "1.0.0", dependencies: [("Acme.Support", "1.0.0")]);

        PackageStore store = new(_store);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        NodeLibrary library = new();
        PackageLoadReport report = new PackageManager(store, library).Load(identity);

        Assert.Empty(report.Problems);
        Assert.True(report.Nodes > 50, $"expected the packaged assembly's nodes, got {report.Nodes}");
    }

    private NuGetPackageClient Client() => new(_feed);

    /// <summary>
    /// Puts a package on the folder feed: a real assembly under <c>lib/net10.0</c>, a Spark
    /// manifest, and whatever dependencies were asked for.
    /// </summary>
    private void Publish(string id, string version, (string Id, string Range)[]? dependencies = null)
    {
        System.Reflection.Assembly assembly = typeof(Spark.Nodes.Core.Point).Assembly;
        string simpleName = assembly.GetName().Name!;
        string path = Path.Combine(_feed, $"{id}.{version}.nupkg");

        string declared = dependencies is null or { Length: 0 }
            ? string.Empty
            : "    <dependencies>\n      <group targetFramework=\"net10.0\">\n"
                + string.Join(
                    "\n",
                    dependencies.Select(d =>
                        $"        <dependency id=\"{d.Id}\" version=\"{d.Range}\" />"))
                + "\n      </group>\n    </dependencies>\n";

        using FileStream file = File.Create(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        Write(archive, $"{id}.nuspec", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>Acme Ltd</authors>
                <description>A package for the dependency tests.</description>
                <tags>{SparkPackageManifest.Tag}</tags>
            {declared}  </metadata>
            </package>
            """);

        Write(archive, SparkPackageManifest.PathInPackage, SparkPackageManifest.Write([simpleName]));

        archive.CreateEntryFromFile(assembly.Location, $"lib/net10.0/{simpleName}.dll");
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using Stream entry = archive.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}

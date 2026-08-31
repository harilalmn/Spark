using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// Installing from a local feed (<c>E7-T2</c>): the extract, the validation, and the guard against
/// an archive that writes outside its own folder.
/// </summary>
/// <remarks>
/// <b>A folder source rather than nuget.org.</b> NuGet reads a directory of <c>.nupkg</c> files as
/// a feed, so a package can be built in the test and installed for real — the whole path, not a
/// mock of it — with no network and without installing a stranger's code as a side effect of
/// running tests.
/// </remarks>
public sealed class PackageInstallTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;
    private readonly string _store;

    /// <summary>Creates a scratch feed and store.</summary>
    public PackageInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-install-tests", Guid.NewGuid().ToString("n"));
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
    /// <b>A real package installs, and lands where the load context reads.</b> The whole path:
    /// download from a feed, extract, validate the manifest, move into place.
    /// </summary>
    [Fact]
    public async Task APackageInstallsIntoTheStore()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.2.3");
        BuildPackage(identity, SparkPackageManifest.Write(["Acme.Nodes"], "Acme Nodes"));

        PackageStore store = new(_store);
        SparkPackageManifest manifest = await Client().InstallAsync(
            identity, store, TestContext.Current.CancellationToken);

        Assert.Equal("Acme.Nodes", Assert.Single(manifest.Assemblies));
        Assert.True(store.IsInstalled(identity));
        Assert.True(File.Exists(Path.Combine(store.FolderFor(identity), "lib", "net10.0", "Acme.Nodes.dll")));

        PackageLoadContext context = new(identity, store.FolderFor(identity));
        Assert.Equal(identity, context.Identity);
    }

    /// <summary>
    /// <b>A NuGet package that is not a Spark package is refused, and the message says what is
    /// missing.</b> "It did not work" is not something a user can act on.
    /// </summary>
    [Fact]
    public async Task ANuGetPackageWithNoManifestIsRefusedWithAReason()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Plain", "1.0.0");
        BuildPackage(identity, manifest: null);

        PackageStore store = new(_store);

        SparkPackageException thrown = await Assert.ThrowsAsync<SparkPackageException>(
            () => Client().InstallAsync(identity, store, TestContext.Current.CancellationToken));

        Assert.Contains("not a Spark package", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(SparkPackageManifest.PathInPackage, thrown.Message, StringComparison.Ordinal);
        Assert.False(store.IsInstalled(identity));
    }

    /// <summary>A package built for a newer Spark is refused, naming both schema versions.</summary>
    [Fact]
    public async Task APackageFromTheFutureIsRefused()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Future", "1.0.0");
        BuildPackage(identity, "{ \"schema\": 99, \"assemblies\": [\"Acme.Future\"] }");

        SparkPackageException thrown = await Assert.ThrowsAsync<SparkPackageException>(
            () => Client().InstallAsync(identity, new PackageStore(_store), TestContext.Current.CancellationToken));

        Assert.Contains("newer Spark", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>An archive entry that would escape its folder is refused, and nothing is written.</b>
    /// A zip may name <c>../../something</c>, and an extractor that obeys turns installing a
    /// package into an arbitrary file write.
    /// </summary>
    [Fact]
    public async Task AnEntryThatEscapesTheFolderIsRefusedAndNothingIsWritten()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Escape", "1.0.0");
        string escapee = Path.Combine(_root, "escaped.txt");

        BuildPackage(
            identity,
            SparkPackageManifest.Write(["Acme.Escape"]),
            extraEntry: ("../../escaped.txt", "this should never be written"));

        PackageStore store = new(_store);

        SparkPackageException thrown = await Assert.ThrowsAsync<SparkPackageException>(
            () => Client().InstallAsync(identity, store, TestContext.Current.CancellationToken));

        Assert.Contains("outside its own folder", thrown.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(escapee), "the archive wrote a file outside the package folder");
        Assert.False(store.IsInstalled(identity));
    }

    /// <summary>
    /// A failed install leaves nothing behind: no half-extracted folder that a later run would
    /// treat as installed.
    /// </summary>
    [Fact]
    public async Task AFailedInstallLeavesNoFolderBehind()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Plain", "1.0.0");
        BuildPackage(identity, manifest: null);

        PackageStore store = new(_store);

        await Assert.ThrowsAsync<SparkPackageException>(
            () => Client().InstallAsync(identity, store, TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(store.FolderFor(identity)));
        Assert.False(Directory.Exists(store.FolderFor(identity) + ".installing"));
        Assert.Empty(store.Installed());
    }

    /// <summary>Installing over an existing install replaces it rather than merging into it.</summary>
    [Fact]
    public async Task InstallingTwiceReplacesRatherThanMerges()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackage(identity, SparkPackageManifest.Write(["Acme.Nodes"]));

        PackageStore store = new(_store);
        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        // A file that the package does not contain, left over from a previous version.
        string stale = Path.Combine(store.FolderFor(identity), "lib", "net10.0", "Gone.dll");
        File.WriteAllText(stale, "stale");

        await Client().InstallAsync(identity, store, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(stale), "a stale file survived a reinstall");
        Assert.True(store.IsInstalled(identity));
    }

    private NuGetPackageClient Client() => new(_feed);

    /// <summary>
    /// Writes a minimal but real <c>.nupkg</c> into the feed folder.
    /// </summary>
    /// <remarks>
    /// Hand-built rather than produced by <c>dotnet pack</c>: a package is a zip with a
    /// <c>.nuspec</c>, and building one here keeps the test independent of the SDK being
    /// available and fast enough to run on every build.
    /// </remarks>
    private void BuildPackage(
        PackageIdentity identity, string? manifest, (string Path, string Content)? extraEntry = null)
    {
        string path = Path.Combine(_feed, $"{identity.Id}.{identity.Version}.nupkg");

        using FileStream file = File.Create(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        Write(archive, $"{identity.Id}.nuspec", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{identity.Id}</id>
                <version>{identity.Version}</version>
                <authors>Acme</authors>
                <description>A test package.</description>
                <tags>{SparkPackageManifest.Tag}</tags>
              </metadata>
            </package>
            """);

        Write(archive, $"lib/net10.0/{identity.Id}.dll", "not a real assembly, and nothing here loads it");

        if (manifest is not null)
        {
            Write(archive, SparkPackageManifest.PathInPackage, manifest);
        }

        if (extraEntry is { } extra)
        {
            Write(archive, extra.Path, extra.Content);
        }
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using Stream entry = archive.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}

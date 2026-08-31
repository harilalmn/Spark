using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// The package convention and the store (<c>E7-T1</c>): what makes a NuGet package a Spark
/// package, and where an installed one lives.
/// </summary>
/// <remarks>
/// <b>Every test here runs with no network.</b> The feed-facing half is
/// <see cref="NuGetFeedTests"/>, which skips loudly when there is none; this half must always
/// assert, so that a machine without a network still proves the convention rather than proving
/// nothing.
/// </remarks>
public sealed class PackageConventionTests : IDisposable
{
    private readonly string _root;

    /// <summary>Creates a scratch store.</summary>
    public PackageConventionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-package-store", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Removes the scratch store.</summary>
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

    /// <summary>A manifest names the assemblies to load nodes from.</summary>
    [Fact]
    public void AManifestNamesTheAssembliesToLoadNodesFrom()
    {
        SparkPackageManifest manifest = SparkPackageManifest.Parse(
            "{ \"schema\": 1, \"displayName\": \"Acme Nodes\", \"assemblies\": [\"Acme.Nodes\"] }");

        Assert.Equal(1, manifest.Schema);
        Assert.Equal("Acme Nodes", manifest.DisplayName);
        Assert.Equal("Acme.Nodes", Assert.Single(manifest.Assemblies));
        Assert.True(manifest.IsReadable);
    }

    /// <summary>
    /// A <c>.dll</c> suffix is tolerated, because an author will write one and the loader wants a
    /// simple name.
    /// </summary>
    [Fact]
    public void ADllSuffixIsAccepted()
    {
        SparkPackageManifest manifest = SparkPackageManifest.Parse(
            "{ \"assemblies\": [\"Acme.Nodes.dll\"] }");

        Assert.Equal("Acme.Nodes", Assert.Single(manifest.Assemblies));
    }

    /// <summary>
    /// <b>A manifest naming nothing is refused.</b> A package with no assemblies has nothing to
    /// contribute, and installing it would appear to work and add no nodes — which the user would
    /// report as the package being broken.
    /// </summary>
    [Fact]
    public void AManifestNamingNoAssembliesIsRefusedWithAReason()
    {
        SparkPackageException thrown = Assert.Throws<SparkPackageException>(
            () => SparkPackageManifest.Parse("{ \"assemblies\": [] }"));

        Assert.Contains("names no assemblies", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Malformed JSON is refused with the parser's reason, not a bare failure.</summary>
    [Fact]
    public void MalformedJsonIsRefusedWithTheReason()
    {
        Assert.Throws<SparkPackageException>(() => SparkPackageManifest.Parse("not json"));
        Assert.Throws<SparkPackageException>(() => SparkPackageManifest.Parse("[]"));
    }

    /// <summary>
    /// <b>Unknown properties are ignored rather than refused</b>, so a package built against a
    /// later Spark still installs into an earlier one.
    /// </summary>
    [Fact]
    public void UnknownPropertiesAreIgnored()
    {
        SparkPackageManifest manifest = SparkPackageManifest.Parse(
            "{ \"assemblies\": [\"Acme.Nodes\"], \"somethingFromTheFuture\": { \"a\": 1 } }");

        Assert.Equal("Acme.Nodes", Assert.Single(manifest.Assemblies));
    }

    /// <summary>
    /// A newer schema is refused rather than guessed at, because misreading a manifest fails much
    /// later as an absent node with no explanation.
    /// </summary>
    [Fact]
    public void ANewerSchemaIsNotReadable()
    {
        Assert.False(SparkPackageManifest.Parse(
            "{ \"schema\": 99, \"assemblies\": [\"Acme.Nodes\"] }").IsReadable);
    }

    /// <summary>What the writer produces, the reader reads.</summary>
    [Fact]
    public void AWrittenManifestRoundTrips()
    {
        string text = SparkPackageManifest.Write(["Acme.Nodes", "Acme.Extra"], "Acme", "Nodes for Acme.");

        SparkPackageManifest manifest = SparkPackageManifest.Parse(text);

        Assert.Equal(["Acme.Nodes", "Acme.Extra"], manifest.Assemblies);
        Assert.Equal("Acme", manifest.DisplayName);
        Assert.Equal("Nodes for Acme.", manifest.Description);
    }

    /// <summary>
    /// <b>The store splits a folder name at the version, not at the first dot.</b> A package id
    /// contains dots, so splitting naively would call <c>Acme.Nodes.Geometry</c> a package named
    /// <c>Acme</c>.
    /// </summary>
    [Fact]
    public void AnInstalledPackageIsFoundWithItsFullIdIntact()
    {
        PackageStore store = new(_root);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes.Geometry", "2.1.0");

        Install(store, identity);

        PackageIdentity found = Assert.Single(store.Installed());
        Assert.Equal("acme.nodes.geometry", found.Id);
        Assert.Equal("2.1.0", found.Version);
    }

    /// <summary>Two versions of one package are two installs, which is the whole point.</summary>
    [Fact]
    public void TwoVersionsOfOnePackageAreBothInstalled()
    {
        PackageStore store = new(_root);
        Install(store, PackageIdentity.Create("Acme.Nodes", "1.0.0"));
        Install(store, PackageIdentity.Create("Acme.Nodes", "2.0.0"));

        Assert.Equal(2, store.Installed().Count);
    }

    /// <summary>
    /// <b>A folder with no manifest is treated as absent</b>, so a half-finished install is
    /// replaced rather than loaded.
    /// </summary>
    [Fact]
    public void AFolderWithoutAManifestIsNotInstalled()
    {
        PackageStore store = new(_root);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        Directory.CreateDirectory(store.FolderFor(identity));

        Assert.False(store.IsInstalled(identity));
        Assert.Empty(store.Installed());
    }

    /// <summary>Uninstalling removes the folder, and doing it twice is not an error.</summary>
    [Fact]
    public void UninstallingRemovesItAndIsIdempotent()
    {
        PackageStore store = new(_root);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        Install(store, identity);

        Assert.True(store.Uninstall(identity));
        Assert.False(store.Uninstall(identity));
        Assert.False(store.IsInstalled(identity));
    }

    /// <summary>Reading the manifest of something not installed says so.</summary>
    [Fact]
    public void ReadingAManifestThatIsNotInstalledSaysSo()
    {
        PackageStore store = new(_root);

        SparkPackageException thrown = Assert.Throws<SparkPackageException>(
            () => store.ManifestOf(PackageIdentity.Create("Acme.Nodes", "1.0.0")));

        Assert.Contains("not installed", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A package folder is exactly what a load context reads.</b> The store and the loader agree
    /// by construction, which is the reason install is an extract rather than a restore.
    /// </summary>
    [Fact]
    public void AnInstalledPackageFolderIsWhatTheLoadContextReads()
    {
        PackageStore store = new(_root);
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        Install(store, identity);

        PackageLoadContext context = new(identity, store.FolderFor(identity));

        Assert.Equal(store.FolderFor(identity), context.Folder, ignoreCase: true);
        Assert.Equal(identity, context.Identity);
    }

    /// <summary>Writes a minimal installed package: a manifest and nothing else.</summary>
    private static void Install(PackageStore store, PackageIdentity identity)
    {
        string folder = store.FolderFor(identity);
        Directory.CreateDirectory(Path.Combine(folder, "tools"));
        File.WriteAllText(
            Path.Combine(folder, SparkPackageManifest.PathInPackage),
            SparkPackageManifest.Write([identity.Id]));
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// What a user is told before agreeing to install, and what is remembered afterwards
/// (<c>E7-T8</c>).
/// </summary>
/// <remarks>
/// <b>The disclosure is read out of the package, so these tests build packages that carry the
/// things being disclosed</b> — a licence, dependencies, a signature entry, a native binary — and
/// assert that they come back out. A disclosure a package could assert about itself would be
/// worth nothing to the user it is shown to, and a test that mocked one would prove nothing.
/// </remarks>
public sealed class DisclosureAndTrustTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;
    private readonly string _store;

    /// <summary>Creates a scratch feed and store.</summary>
    public DisclosureAndTrustTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-trust-tests", Guid.NewGuid().ToString("n"));
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
    /// <b>The headline disclosure: a package carrying a native binary says so.</b> Spark promises
    /// no native dependencies; a package may break that on its own behalf, but not silently.
    /// </summary>
    [Fact]
    public async Task APackageCarryingANativeBinarySaysSo()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Native", "1.0.0");
        BuildPackage(identity, native: "runtimes/win-x64/native/acme.dll");

        using PendingInstall pending = await Client().PrepareAsync(
            identity, new PackageStore(_store), TestContext.Current.CancellationToken);

        Assert.True(pending.Disclosure.CarriesNativeBinaries);
        Assert.Contains(
            "runtimes/win-x64/native/acme.dll",
            pending.Disclosure.NativeBinaries,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains("native binar", pending.Disclosure.Summary(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A native library dropped beside the managed ones is found too. A check that only knew
    /// NuGet's <c>runtimes/{rid}/native</c> convention would report *no native binaries* for a
    /// package that plainly has one.
    /// </summary>
    [Fact]
    public async Task ANativeLibraryOutsideTheConventionalFolderIsStillFound()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Sneaky", "1.0.0");
        BuildPackage(identity, native: "lib/net10.0/libacme.so");

        using PendingInstall pending = await Client().PrepareAsync(
            identity, new PackageStore(_store), TestContext.Current.CancellationToken);

        Assert.True(pending.Disclosure.CarriesNativeBinaries);
    }

    /// <summary>An ordinary managed package reports no native binaries, so the check is not always on.</summary>
    [Fact]
    public async Task AManagedOnlyPackageReportsNoNativeBinaries()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Managed", "1.0.0");
        BuildPackage(identity);

        using PendingInstall pending = await Client().PrepareAsync(
            identity, new PackageStore(_store), TestContext.Current.CancellationToken);

        Assert.False(pending.Disclosure.CarriesNativeBinaries);
        Assert.Empty(pending.Disclosure.NativeBinaries);
    }

    /// <summary>Publisher, licence and dependencies are read from the package's own metadata.</summary>
    [Fact]
    public async Task PublisherLicenceAndDependenciesComeFromThePackage()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackage(identity, dependencies: ["Newtonsoft.Json", "Acme.Core"]);

        using PendingInstall pending = await Client().PrepareAsync(
            identity, new PackageStore(_store), TestContext.Current.CancellationToken);

        Assert.Equal("Acme Ltd", pending.Disclosure.Authors);
        Assert.Equal("MIT", pending.Disclosure.Licence);
        Assert.Equal(2, pending.Disclosure.Dependencies.Length);
        Assert.Contains("Acme.Core", pending.Disclosure.Dependencies, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Acme.Nodes", Assert.Single(pending.Disclosure.NodeAssemblies));
    }

    /// <summary>
    /// <b>An unsigned package is reported as unsigned, and a signed one as *present but
    /// unverified*.</b> Spark does not build a certificate chain, and saying "signed" would imply
    /// it does.
    /// </summary>
    [Fact]
    public async Task SignatureIsReportedWithoutClaimingItWasVerified()
    {
        PackageIdentity plain = PackageIdentity.Create("Acme.Plain", "1.0.0");
        PackageIdentity signed = PackageIdentity.Create("Acme.Signed", "1.0.0");
        BuildPackage(plain);
        BuildPackage(signed, signature: true);

        PackageStore store = new(_store);

        using PendingInstall a = await Client().PrepareAsync(plain, store, TestContext.Current.CancellationToken);
        using PendingInstall b = await Client().PrepareAsync(signed, store, TestContext.Current.CancellationToken);

        Assert.Equal(PackageSignature.Unsigned, a.Disclosure.Signature);
        Assert.Equal(PackageSignature.PresentButUnverified, b.Disclosure.Signature);
        Assert.Contains("not verified by Spark", b.Disclosure.Summary(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Nothing is installed until it is committed.</b> The whole point of preparing separately
    /// is that a user can be shown the disclosure and say no.
    /// </summary>
    [Fact]
    public async Task PreparingInstallsNothingUntilCommitted()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackage(identity);
        PackageStore store = new(_store);

        using (PendingInstall pending = await Client().PrepareAsync(
            identity, store, TestContext.Current.CancellationToken))
        {
            Assert.False(store.IsInstalled(identity));
            Assert.False(pending.IsSettled);

            pending.Commit();

            Assert.True(store.IsInstalled(identity));
            Assert.True(pending.IsSettled);
        }
    }

    /// <summary>Discarding leaves nothing behind, in the store or in staging.</summary>
    [Fact]
    public async Task DiscardingLeavesNothingBehind()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackage(identity);
        PackageStore store = new(_store);

        PendingInstall pending = await Client().PrepareAsync(
            identity, store, TestContext.Current.CancellationToken);
        string staging = pending.StagingFolder;

        pending.Discard();

        Assert.False(store.IsInstalled(identity));
        Assert.False(Directory.Exists(staging));
        Assert.Empty(store.Installed());
    }

    /// <summary>
    /// Disposing without committing discards, so the ordinary <c>using</c> shape cannot leak a
    /// downloaded package into a temporary folder nobody remembers.
    /// </summary>
    [Fact]
    public async Task DisposingWithoutCommittingDiscards()
    {
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        BuildPackage(identity);
        PackageStore store = new(_store);

        string staging;
        using (PendingInstall pending = await Client().PrepareAsync(
            identity, store, TestContext.Current.CancellationToken))
        {
            staging = pending.StagingFolder;
        }

        Assert.False(Directory.Exists(staging));
        Assert.False(store.IsInstalled(identity));
    }

    /// <summary>
    /// <b>Trust is per version, not per package.</b> What a user weighed can change between
    /// versions — a patch release can acquire a native dependency — so agreeing to one version is
    /// not agreeing to the next.
    /// </summary>
    [Fact]
    public void TrustIsRecordedPerVersion()
    {
        PackageTrustStore trust = new(Path.Combine(_root, "trusted.json"));

        trust.Trust(PackageIdentity.Create("Acme.Nodes", "1.0.0"));

        Assert.True(trust.IsTrusted(PackageIdentity.Create("Acme.Nodes", "1.0.0")));
        Assert.False(trust.IsTrusted(PackageIdentity.Create("Acme.Nodes", "2.0.0")));
    }

    /// <summary>A decision survives a restart, which is the only reason to write it down.</summary>
    [Fact]
    public void ADecisionSurvivesReopeningTheStore()
    {
        string path = Path.Combine(_root, "trusted.json");
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");

        new PackageTrustStore(path).Trust(identity);

        Assert.True(new PackageTrustStore(path).IsTrusted(identity));
    }

    /// <summary>Revoking makes the user be asked again, and revoking twice is not an error.</summary>
    [Fact]
    public void RevokingForgetsTheDecision()
    {
        PackageTrustStore trust = new(Path.Combine(_root, "trusted.json"));
        PackageIdentity identity = PackageIdentity.Create("Acme.Nodes", "1.0.0");
        trust.Trust(identity);

        Assert.True(trust.Revoke(identity));
        Assert.False(trust.Revoke(identity));
        Assert.False(trust.IsTrusted(identity));
    }

    /// <summary>
    /// <b>An unreadable trust file trusts nothing</b>, which is the safe direction: the worst
    /// outcome is that a user is asked again.
    /// </summary>
    [Fact]
    public void AnUnreadableTrustFileTrustsNothing()
    {
        string path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{ this is not the array it should be");

        PackageTrustStore trust = new(path);

        Assert.Equal(0, trust.Count);
        Assert.False(trust.IsTrusted(PackageIdentity.Create("Acme.Nodes", "1.0.0")));
    }

    private NuGetPackageClient Client() => new(_feed);

    private void BuildPackage(
        PackageIdentity identity,
        string? native = null,
        bool signature = false,
        string[]? dependencies = null)
    {
        string path = Path.Combine(_feed, $"{identity.Id}.{identity.Version}.nupkg");

        using FileStream file = File.Create(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        string dependencyXml = dependencies is null or { Length: 0 }
            ? string.Empty
            : "<dependencies>"
                + string.Join(string.Empty, dependencies.Select(id => $"<dependency id=\"{id}\" version=\"1.0.0\" />"))
                + "</dependencies>";

        Write(archive, $"{identity.Id}.nuspec", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{identity.Id}</id>
                <version>{identity.Version}</version>
                <authors>Acme Ltd</authors>
                <license type="expression">MIT</license>
                <projectUrl>https://example.com/acme</projectUrl>
                <description>A test package.</description>
                <tags>{SparkPackageManifest.Tag}</tags>
                {dependencyXml}
              </metadata>
            </package>
            """);

        Write(archive, $"lib/net10.0/{identity.Id}.dll", "not a real assembly");
        Write(archive, SparkPackageManifest.PathInPackage, SparkPackageManifest.Write([identity.Id]));

        if (native is not null)
        {
            Write(archive, native, "not a real native library either");
        }

        if (signature)
        {
            Write(archive, ".signature.p7s", "not a real signature");
        }
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using Stream entry = archive.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}

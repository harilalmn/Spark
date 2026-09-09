using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spark.Engine;
using Spark.Packages;
using Spark.UI.ViewModels;
using Spark.UI.Views;

namespace Spark.UI.Tests;

/// <summary>
/// The package manager as a user meets it (<c>E7-T10</c>, and the UI half of <c>E7-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>These run against a real package on a real folder feed</b>, built by the test and carrying a
/// real assembly — <c>Spark.Nodes.Core.dll</c>. A stub would prove the buttons are wired and
/// nothing about whether a user who presses them ends up with usable nodes.
/// </para>
/// <para>
/// <b>Every test supplies its own store root.</b> <c>PackageStore.Default()</c> writes under the
/// user's local application data, and a test suite that installed packages into a developer's real
/// Spark would be a bug worse than anything it could catch.
/// </para>
/// </remarks>
public sealed class PackageBrowserTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;
    private readonly string _store;

    /// <summary>Creates a scratch feed and store.</summary>
    public PackageBrowserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-browser-tests", Guid.NewGuid().ToString("n"));
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
    /// <b>Preparing an install shows the disclosure and installs nothing.</b> That is the whole of
    /// <c>E7-T8</c> seen from the UI: the user weighs it first and answers second.
    /// </summary>
    [Fact]
    public async Task PreparingShowsTheDisclosureAndInstallsNothing()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out NodeLibrary library);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);

        Assert.True(browser.HasPendingInstall);
        Assert.Contains("Acme Ltd", browser.Disclosure, StringComparison.Ordinal);
        Assert.False(browser.CarriesNativeCode);
        Assert.Contains("No native code", browser.NativeNotice, StringComparison.Ordinal);

        Assert.False(new PackageStore(_store).IsInstalled(identity));
        Assert.Equal(0, library.Count);
        Assert.Empty(browser.Installed);
    }

    /// <summary>
    /// The disclosure names the licence and says plainly that a signature is not verified, because
    /// reporting <i>signed</i> would imply a check nobody performed.
    /// </summary>
    [Fact]
    public async Task TheDisclosureNamesTheLicenceAndDoesNotClaimAVerifiedSignature()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);

        Assert.Contains("MIT", browser.Disclosure, StringComparison.Ordinal);
        Assert.Contains("Signature: none", browser.Disclosure, StringComparison.Ordinal);
        Assert.DoesNotContain("Signature: verified", browser.Disclosure, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Answering yes installs it and its nodes arrive in the library.</b> The sentence the whole
    /// epic exists for, reached through the browser rather than the engine.
    /// </summary>
    [Fact]
    public async Task ConfirmingInstallsThePackageAndItsNodesArrive()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out NodeLibrary library);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);
        await browser.ConfirmAsync();

        Assert.False(browser.HasPendingInstall);
        Assert.Empty(browser.Disclosure);
        Assert.True(new PackageStore(_store).IsInstalled(identity));
        Assert.True(library.Count > 50, $"expected the packaged assembly's nodes, got {library.Count}");
        Assert.Equal("Acme.Nodes", Assert.Single(browser.Installed).Id);
    }

    /// <summary>
    /// <b>Answering no installs nothing and leaves nothing behind.</b> A staged download that
    /// survived a refusal would be a package a user declined sitting on their disk.
    /// </summary>
    [Fact]
    public async Task CancellingInstallsNothingAndLeavesNoStaging()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out NodeLibrary library);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);
        browser.Cancel();

        Assert.False(browser.HasPendingInstall);
        Assert.Empty(browser.Disclosure);
        Assert.False(new PackageStore(_store).IsInstalled(identity));
        Assert.Equal(0, library.Count);
        Assert.False(
            Directory.Exists(new PackageStore(_store).FolderFor(identity) + ".installing"),
            "a refused install left its download staged");
    }

    /// <summary>
    /// <b>Trust is recorded per version</b>, so agreeing to one release is not agreeing to the
    /// next — the point of <c>E7-T8</c>'s per-version store.
    /// </summary>
    [Fact]
    public async Task AgreeingToOneVersionIsNotAgreeingToTheNext()
    {
        PackageIdentity first = Publish("Acme.Nodes", "1.0.0");
        PackageIdentity second = Publish("Acme.Nodes", "2.0.0");
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(Row(first), TestContext.Current.CancellationToken);
        await browser.ConfirmAsync();

        PackageTrustStore trust = PackageTrustStore.For(new PackageStore(_store));

        Assert.True(trust.IsTrusted(first));
        Assert.False(trust.IsTrusted(second));
    }

    /// <summary>
    /// <b>Removing takes the nodes back out of the library</b>, which is the half of an unload the
    /// UI can guarantee.
    /// </summary>
    [Fact]
    public async Task RemovingTakesTheNodesBackOutOfTheLibrary()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out NodeLibrary library);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);
        await browser.ConfirmAsync();

        Assert.True(library.Count > 50);

        browser.Remove(browser.Installed[0]);

        // The library is purged whatever happens: that is the half removal can guarantee, and it
        // is the half a user sees on the canvas. Whether the folder goes depends on the context
        // unloading, which is best-effort - so the second assertion is the honest disjunction
        // rather than a claim the method does not make. Asserting the folder always went failed
        // one run in three under a parallel suite.
        Assert.Equal(0, library.Count);

        Assert.True(
            !new PackageStore(_store).IsInstalled(identity)
            || browser.Status.Contains("Restart Spark", StringComparison.Ordinal),
            "removal neither took the package out of the store nor said a restart was needed: "
            + browser.Status);
    }

    /// <summary>
    /// <b>When the unload does not take, the status says so and asks for a restart.</b> That is
    /// <c>E7-T5</c>'s UI half, and the wording matters: a user whose old nodes are still there
    /// needs to be told why rather than left to discover it.
    /// </summary>
    /// <remarks>
    /// The status is asserted to be one of exactly two sentences, so a future change that made
    /// removal silently claim success in the pinned case would fail here rather than mislead.
    /// </remarks>
    [Fact]
    public async Task RemovalEitherReportsSuccessOrAsksForARestart()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);
        await browser.ConfirmAsync();

        browser.Remove(browser.Installed[0]);

        Assert.True(
            browser.Status == "Removed Acme.Nodes 1.0.0."
            || browser.Status.Contains("Restart Spark", StringComparison.Ordinal),
            "removal reported neither a clean release nor the restart a pinned context needs: "
            + browser.Status);
    }

    /// <summary>
    /// A second browser over the same store finds what the first one installed, and loading it
    /// contributes the nodes again — which is what makes an install survive a restart.
    /// </summary>
    [Fact]
    public async Task AnInstalledPackageIsFoundAndLoadedByTheNextSession()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel first = Browser(out _);

        await first.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);
        await first.ConfirmAsync();

        PackageBrowserViewModel next = Browser(out NodeLibrary library);

        Assert.Equal("Acme.Nodes", Assert.Single(next.Installed).Id);

        IReadOnlyList<string> lines = next.LoadInstalled();

        Assert.Empty(next.StartupProblems);
        Assert.Contains("Acme.Nodes", Assert.Single(lines), StringComparison.Ordinal);
        Assert.True(library.Count > 50);
    }

    /// <summary>
    /// <b>A package whose manifest names an assembly it does not carry is reported</b> rather than
    /// swallowed, because the nodes it failed to contribute are the ones that become placeholders
    /// in a document the user will otherwise blame.
    /// </summary>
    [Fact]
    public async Task APartlyBrokenPackageIsReportedAtStartup()
    {
        PackageIdentity identity = Publish("Acme.Partial", "1.0.0", ["Spark.Nodes.Core", "Acme.NotThere"]);
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);
        await browser.ConfirmAsync();

        PackageBrowserViewModel next = Browser(out _);
        _ = next.LoadInstalled();

        Assert.Contains("Acme.NotThere", Assert.Single(next.StartupProblems), StringComparison.Ordinal);
    }

    /// <summary>Asking for a package the feed does not have says so rather than throwing.</summary>
    [Fact]
    public async Task AMissingPackageIsReportedInTheStatus()
    {
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(
            new PackageRow("Acme.Nothing", "1.0.0", "Acme.Nothing", string.Empty, IsInstalled: false),
            TestContext.Current.CancellationToken);

        Assert.False(browser.HasPendingInstall);
        Assert.NotEmpty(browser.Status);
        Assert.Empty(browser.Disclosure);
    }

    /// <summary>
    /// <b>The window shows the disclosure only while one is pending</b>, and answering it takes the
    /// gate off screen. A disclosure that lingered would be one a user answers twice.
    /// </summary>
    /// <remarks>
    /// <b>The install is prepared before the dispatcher is entered, and that is not a style
    /// choice.</b> <c>HeadlessSession.Run</c> blocks the caller on the UI thread; awaiting inside
    /// it deadlocks, because the continuation is posted to the very thread that is waiting. The
    /// first run of these tests hung for seven minutes and had to be killed. So the asynchronous
    /// half happens outside and only the window is driven within.
    /// </remarks>
    [Fact]
    public async Task TheWindowShowsTheDisclosureOnlyWhileOneIsPending()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);

        HeadlessSession.Run(() =>
        {
            PackageWindow window = new(browser);

            Assert.True(window.IsShowingDisclosure);
            Assert.Contains("Acme Ltd", window.DisclosureText, StringComparison.Ordinal);

            browser.Cancel();

            Assert.False(window.IsShowingDisclosure);
            window.Close();
        });
    }

    /// <summary>
    /// A window opened with nothing pending shows no gate, which is the ordinary case and the one
    /// a user meets first.
    /// </summary>
    [Fact]
    public void AWindowWithNothingPendingShowsNoDisclosure() => HeadlessSession.Run(() =>
    {
        PackageBrowserViewModel browser = Browser(out _);
        PackageWindow window = new(browser);

        Assert.False(window.IsShowingDisclosure);
        Assert.Empty(window.DisclosureText);
        Assert.NotEmpty(window.StatusText);

        window.Close();
    });

    /// <summary>
    /// The buttons that act on a row need a row. An <i>Install</i> that is pressable with nothing
    /// selected is an install of nothing that reports an error the user did not cause.
    /// </summary>
    [Fact]
    public void TheRowActionsNeedARow() => HeadlessSession.Run(() =>
    {
        PackageBrowserViewModel browser = Browser(out _);
        PackageWindow window = new(browser);

        Assert.False(window.CanInstall);
        Assert.False(window.CanRemove);

        browser.Results.Add(Row(PackageIdentity.Create("Acme.Nodes", "1.0.0")));
        window.SelectFound(0);

        Assert.True(window.CanInstall);
        Assert.False(window.CanRemove);

        window.Close();
    });

    /// <summary>
    /// <b>The window discards an unanswered install when it closes.</b> A download must not
    /// outlive the question it was fetched to answer.
    /// </summary>
    [Fact]
    public async Task ClosingTheWindowDiscardsAnUnansweredInstall()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Browser(out _);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);

        Assert.True(browser.HasPendingInstall);

        HeadlessSession.Run(() =>
        {
            PackageWindow window = new(browser);

            Assert.True(window.IsShowingDisclosure);
            window.Close();
        });

        Assert.False(browser.HasPendingInstall);
        Assert.False(new PackageStore(_store).IsInstalled(identity));
        Assert.False(Directory.Exists(new PackageStore(_store).FolderFor(identity) + ".installing"));
    }

    /// <summary>
    /// <b>A graph with no file refuses, and the refusal names the folder it would have needed</b>
    /// (`E7-T18`). Naming it is the difference between a rule a user can work with and a rule that
    /// looks arbitrary.
    /// </summary>
    [Fact]
    public void AGraphWithNoFileRefusesAndSaysWhy()
    {
        PackageBrowserViewModel browser = Unsaved(out _);

        Assert.False(browser.IsGraphSaved);
        Assert.Contains("Save the graph first", browser.UnsavedGraphRefusal, StringComparison.Ordinal);
        Assert.Contains(GraphPackages.FolderSuffix, browser.UnsavedGraphRefusal, StringComparison.Ordinal);

        // And it says what still works, so the user does not conclude the window is shut.
        Assert.Contains("Local assemblies", browser.UnsavedGraphRefusal, StringComparison.Ordinal);
    }

    /// <summary>A graph with a file does not refuse, and says nothing at all about it.</summary>
    [Fact]
    public void AGraphWithAFileDoesNotRefuse()
    {
        PackageBrowserViewModel browser = Browser(out _);

        Assert.True(browser.IsGraphSaved);
        Assert.Empty(browser.UnsavedGraphRefusal);
    }

    /// <summary>
    /// <b>Saving lifts the refusal on the browser that is already open</b> (`E7-T18`), and the
    /// change is announced, because the window redraws on a property change and nothing else.
    /// </summary>
    [Fact]
    public void SavingLiftsTheRefusalAndAnnouncesIt()
    {
        PackageBrowserViewModel browser = Unsaved(out _);
        List<string> announced = [];
        browser.PropertyChanged += (_, e) => announced.Add(e.PropertyName ?? string.Empty);

        browser.GraphPath = Path.Combine(_root, "saved.spark");

        Assert.True(browser.IsGraphSaved);
        Assert.Empty(browser.UnsavedGraphRefusal);
        Assert.Contains(nameof(PackageBrowserViewModel.IsGraphSaved), announced, StringComparer.Ordinal);
        Assert.Contains(nameof(PackageBrowserViewModel.UnsavedGraphRefusal), announced, StringComparer.Ordinal);
    }

    /// <summary>
    /// <b>The refusal is not only a disabled button</b> (`E7-T18`): preparing an install over a
    /// graph with no file refuses at the point where a folder would have had to be invented, and
    /// downloads nothing.
    /// </summary>
    [Fact]
    public async Task PreparingOverAnUnsavedGraphInstallsNothing()
    {
        PackageIdentity identity = Publish("Acme.Nodes", "1.0.0");
        PackageBrowserViewModel browser = Unsaved(out _);

        await browser.PrepareAsync(Row(identity), TestContext.Current.CancellationToken);

        Assert.False(browser.HasPendingInstall);
        Assert.Empty(browser.Disclosure);
        Assert.Contains("Save the graph first", browser.Status, StringComparison.Ordinal);
        Assert.False(new PackageStore(_store).IsInstalled(identity));
    }

    /// <summary>
    /// The window opened over an unsaved graph shows the refusal, lands on the tab that still
    /// works, and offers neither search nor install (`E7-T18`).
    /// </summary>
    [Fact]
    public void TheWindowRefusesTheUnsavedGraphAndOpensOnLocalAssemblies() => HeadlessSession.Run(() =>
    {
        PackageBrowserViewModel browser = Unsaved(out _);
        PackageWindow window = new(browser);

        Assert.True(window.IsRefusingUnsavedGraph);
        Assert.Equal(1, window.SelectedTab);
        Assert.Contains("Save the graph first", window.UnsavedGraphText, StringComparison.Ordinal);

        // The Install button stays unavailable even with a row selected, because the row is not
        // what is missing.
        browser.Results.Add(Row(PackageIdentity.Create("Acme.Nodes", "1.0.0")));
        window.SelectFound(0);
        Assert.False(window.CanInstall);

        window.Close();
    });

    /// <summary>
    /// <b>Saving while the window is open lifts the refusal there and then</b> (`E7-T18`), rather
    /// than on the next window. That is the whole reason the path is pushed in rather than read
    /// once at construction.
    /// </summary>
    [Fact]
    public void SavingWhileTheWindowIsOpenLiftsItsRefusal() => HeadlessSession.Run(() =>
    {
        PackageBrowserViewModel browser = Unsaved(out _);
        PackageWindow window = new(browser);

        Assert.True(window.IsRefusingUnsavedGraph);

        browser.GraphPath = Path.Combine(_root, "saved.spark");

        Assert.False(window.IsRefusingUnsavedGraph);
        Assert.Empty(window.UnsavedGraphText);

        browser.Results.Add(Row(PackageIdentity.Create("Acme.Nodes", "1.0.0")));
        window.SelectFound(0);
        Assert.True(window.CanInstall);

        window.Close();
    });

    /// <summary>
    /// <b>The local assemblies tab is untouched by the refusal</b> (`E7-T18`). It adds a DLL by its
    /// full path and needs no graph on disk, which is exactly what an unsaved scratch graph has.
    /// </summary>
    [Fact]
    public void TheLocalAssembliesTabWorksWithNoGraphOnDisk() => HeadlessSession.Run(() =>
    {
        PackageBrowserViewModel browser = Unsaved(out _);
        PackageWindow window = new(browser);

        Assert.True(window.IsRefusingUnsavedGraph);

        window.ShowLocalAssemblies();
        Assert.Equal(1, window.SelectedTab);

        string assembly = typeof(Spark.Nodes.Core.Point).Assembly.Location;
        window.Local.Choose(assembly);

        Assert.True(window.IsShowingPrompt);
        Assert.Contains(Path.GetFileName(assembly), window.PromptText, StringComparison.Ordinal);

        window.Close();
    });

    /// <summary>
    /// A browser over a graph that has been saved, which is the ordinary case (`E7-T18`).
    /// </summary>
    /// <remarks>
    /// <b>The path is set rather than left null, because a browser with no graph refuses.</b> That
    /// refusal is a rule worth its own tests below; every test that is about installing a package
    /// has to get past it first, and one that did so by accident would be testing the refusal.
    /// </remarks>
    private PackageBrowserViewModel Browser(out NodeLibrary library)
    {
        PackageBrowserViewModel browser = Unsaved(out library);
        browser.GraphPath = Path.Combine(_root, "graph.spark");
        return browser;
    }

    /// <summary>A browser over a graph that has never been saved.</summary>
    private PackageBrowserViewModel Unsaved(out NodeLibrary library)
    {
        library = new NodeLibrary();
        return new PackageBrowserViewModel(library, new PackageStore(_store), _feed);
    }

    private static PackageRow Row(PackageIdentity identity) =>
        new(identity.Id, identity.Version, identity.Id, string.Empty, IsInstalled: false);

    /// <summary>Puts a package carrying a real assembly on the folder feed.</summary>
    private PackageIdentity Publish(string id, string version, string[]? manifestAssemblies = null)
    {
        PackageIdentity identity = PackageIdentity.Create(id, version);
        System.Reflection.Assembly assembly = typeof(Spark.Nodes.Core.Point).Assembly;
        string simpleName = assembly.GetName().Name!;
        string path = Path.Combine(_feed, $"{identity.Id}.{identity.Version}.nupkg");

        using (FileStream file = File.Create(path))
        {
            using ZipArchive archive = new(file, ZipArchiveMode.Create);

            Write(archive, $"{identity.Id}.nuspec", $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{identity.Id}</id>
                    <version>{identity.Version}</version>
                    <authors>Acme Ltd</authors>
                    <license type="expression">MIT</license>
                    <description>A package carrying a real assembly.</description>
                    <tags>{SparkPackageManifest.Tag}</tags>
                  </metadata>
                </package>
                """);

            Write(
                archive,
                SparkPackageManifest.PathInPackage,
                SparkPackageManifest.Write(manifestAssemblies ?? [simpleName]));

            // The assembly's own dependencies are deliberately not shipped: Spark.Api and
            // Spark.Geometry are contract assemblies and must come from the host, or a Point3d
            // crossing the boundary would not be the Point3d a graph understands.
            archive.CreateEntryFromFile(assembly.Location, simpleName + ".dll");
        }

        return identity;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using Stream entry = archive.CreateEntry(path).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}

using System;
using System.IO;
using System.Linq;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// The package folder that lives beside a graph — `E7-T16`,
/// [ADR-0024](../../docs/adr/0024-graph-local-package-folder.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client, who drew the layout</b>: <c>&lt;name&gt;.packages</c> beside
/// <c>&lt;name&gt;.spark</c>, one folder per NuGet package and loose <c>.dll</c> files for anything
/// added by hand.
/// </para>
/// <para>
/// <b>The assertion the row exists for is the one about consent</b>, not the one about finding
/// files. A folder of DLLs beside a downloaded graph is remote code execution, and loading is not
/// passive — module initialisers and static constructors run, and the node importer reflects over
/// types. Finding an assembly and being allowed to load it are two questions, and this type only
/// answers the first.
/// </para>
/// </remarks>
public sealed class GraphPackagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp folder that will not delete is the operating system's problem, not a test
            // failure: the assertion has already been made by the time this runs.
        }
    }

    /// <summary>The folder is named after the graph and sits beside it.</summary>
    [Fact]
    public void TheFolderIsNamedAfterTheGraphAndSitsBesideIt()
    {
        string graph = Path.Combine(_root, "project1.spark");

        Assert.Equal(
            Path.Combine(_root, "project1.packages"),
            GraphPackages.FolderFor(graph));
    }

    /// <summary>
    /// <b>A graph that was never saved has no folder, and that is not an error.</b> It has no
    /// filename to name one after — which is also why `E7-T18` refuses to open the Packages window
    /// until the file is saved, rather than inventing a location and moving it later.
    /// </summary>
    [Fact]
    public void AGraphThatWasNeverSavedHasNoPackages()
    {
        GraphPackages packages = GraphPackages.Discover(null);

        Assert.False(packages.Exists);
        Assert.Empty(packages.Assemblies);
    }

    /// <summary>A saved graph with no folder beside it is empty rather than a failure.</summary>
    [Fact]
    public void AGraphWithNoFolderIsEmptyRatherThanAFailure()
    {
        Directory.CreateDirectory(_root);
        string graph = Path.Combine(_root, "project1.spark");
        File.WriteAllText(graph, "{}");

        GraphPackages packages = GraphPackages.Discover(graph);

        Assert.False(packages.Exists);
        Assert.Empty(packages.Assemblies);
    }

    /// <summary>
    /// <b>The client's own layout, found</b>: a loose <c>.dll</c> at the top and one folder per
    /// package.
    /// </summary>
    [Fact]
    public void BothLayoutsAreFound()
    {
        string graph = Graph("project1");
        string folder = GraphPackages.FolderFor(graph);

        Write(Path.Combine(folder, "custom_user_package.dll"), "loose");
        Write(Path.Combine(folder, "custom_nuget_package1", "custom_nuget_package1.dll"), "one");
        Write(Path.Combine(folder, "custom_nuget_package2", "custom_nuget_package2.dll"), "two");

        GraphPackages packages = GraphPackages.Discover(graph);

        Assert.True(packages.Exists);

        Assert.Equal(
            ["custom_nuget_package1.dll", "custom_nuget_package2.dll", "custom_user_package.dll"],
            packages.Assemblies.Select(a => a.Name).Order(StringComparer.Ordinal));

        // A loose file belongs to no package, and reads as itself in a disclosure.
        GraphAssembly loose = packages.Assemblies.Single(a => a.Name == "custom_user_package.dll");
        Assert.Equal(string.Empty, loose.Package);
        Assert.Equal("custom_user_package.dll", loose.Describe());

        GraphAssembly packaged = packages.Assemblies.Single(a => a.Name == "custom_nuget_package1.dll");
        Assert.Equal("custom_nuget_package1", packaged.Package);
        Assert.Equal("custom_nuget_package1 / custom_nuget_package1.dll", packaged.Describe());
    }

    /// <summary>
    /// <b>A NuGet extraction offers one copy per framework and only one may be loaded.</b> Taking
    /// them all would offer the same type several times over and let the loader pick.
    /// </summary>
    [Fact]
    public void OnlyTheBestFrameworkOfAPackageIsOffered()
    {
        string graph = Graph("project1");
        string package = Path.Combine(GraphPackages.FolderFor(graph), "MathNet.Numerics");

        Write(Path.Combine(package, "lib", "netstandard2.0", "MathNet.Numerics.dll"), "old");
        Write(Path.Combine(package, "lib", "net8.0", "MathNet.Numerics.dll"), "newer");
        Write(Path.Combine(package, "lib", "net10.0", "MathNet.Numerics.dll"), "newest");

        GraphAssembly only = Assert.Single(GraphPackages.Discover(graph).Assemblies);

        Assert.Contains(Path.Combine("lib", "net10.0"), only.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A framework newer than this build runs on is refused rather than ranked.</b> It may use
    /// runtime features that are not here, and being wrong is a type load failure at run time
    /// rather than a message anybody can read.
    /// </summary>
    [Fact]
    public void AFrameworkNewerThanThisBuildIsNotChosen()
    {
        string graph = Graph("project1");
        string package = Path.Combine(GraphPackages.FolderFor(graph), "FromTheFuture");

        Write(Path.Combine(package, "lib", "net99.0", "FromTheFuture.dll"), "future");
        Write(Path.Combine(package, "lib", "net10.0", "FromTheFuture.dll"), "now");

        GraphAssembly only = Assert.Single(GraphPackages.Discover(graph).Assemblies);

        Assert.Contains(Path.Combine("lib", "net10.0"), only.Path, StringComparison.Ordinal);
    }

    /// <summary>A package with no <c>lib</c> folder falls back to what it has at its root.</summary>
    [Fact]
    public void APackageWithoutALibFolderFallsBackToItsRoot()
    {
        string graph = Graph("project1");
        string package = Path.Combine(GraphPackages.FolderFor(graph), "HandAssembled");

        Write(Path.Combine(package, "HandAssembled.dll"), "bytes");

        Assert.Equal("HandAssembled.dll", Assert.Single(GraphPackages.Discover(graph).Assemblies).Name);
    }

    /// <summary>
    /// <b>Two graphs in one folder keep their own packages.</b> This is the property the whole
    /// design is for: one may pin a version the other has never heard of.
    /// </summary>
    [Fact]
    public void TwoGraphsInOneFolderKeepTheirOwnPackages()
    {
        string first = Graph("project1");
        string second = Graph("project2");

        Write(Path.Combine(GraphPackages.FolderFor(first), "OnlyInOne.dll"), "one");
        Write(Path.Combine(GraphPackages.FolderFor(second), "OnlyInTwo.dll"), "two");

        Assert.Equal("OnlyInOne.dll", Assert.Single(GraphPackages.Discover(first).Assemblies).Name);
        Assert.Equal("OnlyInTwo.dll", Assert.Single(GraphPackages.Discover(second).Assemblies).Name);
    }

    /// <summary>
    /// <b>The hash is of the bytes, so identical files agree and different ones do not.</b> It is
    /// what a trust decision is recorded against, so a rebuilt assembly asks again.
    /// </summary>
    [Fact]
    public void TheHashFollowsTheBytes()
    {
        string graph = Graph("project1");
        string folder = GraphPackages.FolderFor(graph);

        Write(Path.Combine(folder, "a.dll"), "same");
        Write(Path.Combine(folder, "b.dll"), "same");
        Write(Path.Combine(folder, "c.dll"), "different");

        var byName = GraphPackages.Discover(graph).Assemblies.ToDictionary(a => a.Name, a => a.Hash);

        Assert.Equal(byName["a.dll"], byName["b.dll"]);
        Assert.NotEqual(byName["a.dll"], byName["c.dll"]);
        Assert.NotEmpty(byName["a.dll"]);
    }

    /// <summary>The order does not depend on how the file system happened to enumerate.</summary>
    [Fact]
    public void TheOrderIsStable()
    {
        string graph = Graph("project1");
        string folder = GraphPackages.FolderFor(graph);

        Write(Path.Combine(folder, "zeta.dll"), "z");
        Write(Path.Combine(folder, "alpha.dll"), "a");
        Write(Path.Combine(folder, "Middle", "middle.dll"), "m");

        Assert.Equal(
            GraphPackages.Discover(graph).Assemblies.Select(a => a.Path),
            GraphPackages.Discover(graph).Assemblies.Select(a => a.Path));
    }

    /// <summary>
    /// <b>Nothing in a graph's folder is trusted until somebody says so.</b> This is the assertion
    /// the whole row exists for: a <c>.spark</c> and a folder of DLLs arriving together, opened,
    /// and running before anybody has read anything is remote code execution — and `E6-T16` already
    /// refuses to auto-run the *readable* version of the same hazard.
    /// </summary>
    [Fact]
    public void NothingIsTrustedUntilItIsAgreedTo()
    {
        string graph = Graph("project1");
        Write(Path.Combine(GraphPackages.FolderFor(graph), "helper.dll"), "payload");

        GraphAssembly found = Assert.Single(GraphPackages.Discover(graph).Assemblies);
        PackageTrustStore trust = new(Path.Combine(_root, "trusted.json"));

        Assert.False(trust.IsTrusted(found.Hash));

        trust.Trust(found.Hash);

        Assert.True(trust.IsTrusted(found.Hash));
    }

    /// <summary>
    /// <b>Consent follows the bytes, at the client's instruction.</b> A rebuilt assembly is
    /// different code and asks again; an unchanged one never does. A path-keyed decision would
    /// agree once to a filename and then load whatever later occupied it.
    /// </summary>
    [Fact]
    public void RebuildingAnAssemblyAsksAgain()
    {
        string graph = Graph("project1");
        string dll = Path.Combine(GraphPackages.FolderFor(graph), "helper.dll");

        Write(dll, "first build");

        PackageTrustStore trust = new(Path.Combine(_root, "trusted.json"));
        trust.Trust(Assert.Single(GraphPackages.Discover(graph).Assemblies).Hash);

        // The same path, different bytes - which is what a rebuild is.
        Write(dll, "second build");

        Assert.False(trust.IsTrusted(Assert.Single(GraphPackages.Discover(graph).Assemblies).Hash));
    }

    /// <summary>
    /// <b>A decision survives a restart</b>, or it is not a decision. It is recorded in the same
    /// file, and beside, the package decisions `E7-T8` already stores.
    /// </summary>
    [Fact]
    public void ADecisionIsRemembered()
    {
        string path = Path.Combine(_root, "trusted.json");
        Directory.CreateDirectory(_root);

        const string Hash = "ABCDEF0123456789";

        new PackageTrustStore(path).Trust(Hash);

        Assert.True(new PackageTrustStore(path).IsTrusted(Hash));
        Assert.Contains("sha256:", string.Join(" ", new PackageTrustStore(path).Entries()), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>An assembly that could not be read is never trusted.</b> It hashes to nothing, nothing
    /// matches nothing, and the failure is closed rather than open — which is the direction this
    /// has to fail in.
    /// </summary>
    [Fact]
    public void AnUnreadableAssemblyCanNeverMatchADecision()
    {
        PackageTrustStore trust = new(Path.Combine(_root, "trusted.json"));

        Assert.False(trust.IsTrusted(string.Empty));
        Assert.False(trust.Revoke(string.Empty));
    }

    /// <summary>A decision can be taken back, and then it asks again.</summary>
    [Fact]
    public void ADecisionCanBeRevoked()
    {
        string path = Path.Combine(_root, "trusted.json");
        Directory.CreateDirectory(_root);

        const string Hash = "0123456789ABCDEF";
        PackageTrustStore trust = new(path);

        trust.Trust(Hash);

        Assert.True(trust.Revoke(Hash));
        Assert.False(trust.IsTrusted(Hash));
        Assert.False(trust.Revoke(Hash));
    }

    private string Graph(string name)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, name + ".spark");
        File.WriteAllText(path, "{}");

        return path;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Spark.Engine;
using Spark.Packages;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark pkg</c> (<c>E12-T5</c>): reconciling a graph's package folder against what its file
/// records, and fetching what is missing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The feed is a folder on disk</b>, built by the test, so nothing here touches the network and
/// the restore path is exercised for real rather than mocked. That is the shape
/// <c>PackageDependencyTests</c> established, and it is why this file can assert what
/// <c>restore</c> actually put on disk.
/// </para>
/// <para>
/// <b>What is not tested here is resolution.</b> Which version satisfies which range belongs to
/// <c>Spark.Packages.Tests</c>; this covers the verb — its sub-commands, its exit codes, and the
/// one thing it must never do, which is agree to load what it just downloaded.
/// </para>
/// </remarks>
public sealed class PkgVerbTests : IDisposable
{
    private readonly string _root;
    private readonly string _feed;

    public PkgVerbTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-cli-pkg", Guid.NewGuid().ToString("n"));
        _feed = Path.Combine(_root, "feed");
        Directory.CreateDirectory(_feed);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Left for the operating system; the next run makes its own folder.
        }
    }

    /// <summary>A graph whose folder holds everything its file records passes and says so.</summary>
    [Fact]
    public void ListPassesWhenEveryRecordedPackageIsThere()
    {
        string graph = SaveGraph("complete", ["complete.packages/acme.nodes.1.0.0"]);
        Directory.CreateDirectory(Path.Combine(_root, "complete.packages", "acme.nodes.1.0.0"));

        StringWriter output = new();

        Assert.Equal(0, Program.Pkg(["list", "--open", graph], output, new StringWriter()));

        Assert.Contains("present  complete.packages/acme.nodes.1.0.0", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("0 missing", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A missing package exits 1, which is the whole reason this verb beats reading the
    /// folder.</b> A build that cannot gate here discovers the same fact later as a compile error
    /// inside a code block, two steps from the cause.
    /// </summary>
    [Fact]
    public void ListExitsOneWhenAPackageIsMissingAndNamesIt()
    {
        string graph = SaveGraph("incomplete", ["incomplete.packages/acme.nodes.1.0.0"]);

        StringWriter output = new();

        Assert.Equal(1, Program.Pkg(["list", "--open", graph], output, new StringWriter()));

        Assert.Contains("missing  incomplete.packages/acme.nodes.1.0.0", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 missing", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A package in the folder that the file does not record is listed too: it loads and works, and
    /// it will not travel with the graph.
    /// </summary>
    [Fact]
    public void ListNamesAPackageTheFileDoesNotRecord()
    {
        string graph = SaveGraph("extra", []);
        Directory.CreateDirectory(Path.Combine(_root, "extra.packages", "acme.stray.1.0.0"));

        StringWriter output = new();

        Assert.Equal(0, Program.Pkg(["list", "--open", graph], output, new StringWriter()));

        Assert.Contains("unrecorded  extra.packages/acme.stray.1.0.0", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Restore fetches what the file records and the folder lacks</b>, from the feed, into the
    /// folder beside the graph.
    /// </summary>
    [Fact]
    public void RestoreInstallsAMissingPackageIntoTheGraphsFolder()
    {
        Publish("Acme.Nodes", "1.0.0");

        string graph = SaveGraph("restoring", ["restoring.packages/acme.nodes.1.0.0"]);

        StringWriter output = new();
        StringWriter error = new();

        Assert.Equal(0, Program.Pkg(["restore", "--open", graph], output, error, _feed));

        Assert.True(
            Directory.Exists(Path.Combine(_root, "restoring.packages", "acme.nodes.1.0.0")),
            "the package was not restored into the graph's folder");

        // Lower-cased, because the identity is recovered from the folder name a graph records and
        // NuGet's folder convention is lower case. Ids are case-insensitive, so this installs the
        // same package; it is asserted in the form the user will actually see.
        Assert.Contains("restored acme.nodes 1.0.0", output.ToString(), StringComparison.Ordinal);

        // And the folder now reconciles, which is the outcome the caller actually wanted.
        Assert.Equal(0, Program.Pkg(["list", "--open", graph], new StringWriter(), new StringWriter()));
    }

    /// <summary>
    /// <b>Restoring downloads; it does not agree.</b> The gate in <c>E7-T16</c> is the reason a
    /// graph's package folder is not remote code execution, and a convenience verb that quietly
    /// consented on the user's behalf would be a hole in it. The verb says so in as many words.
    /// </summary>
    [Fact]
    public void RestoreSaysItHasNotAgreedToLoadAnything()
    {
        Publish("Acme.Nodes", "1.0.0");

        string graph = SaveGraph("consent", ["consent.packages/acme.nodes.1.0.0"]);
        StringWriter output = new();

        Assert.Equal(0, Program.Pkg(["restore", "--open", graph], output, new StringWriter(), _feed));

        Assert.Contains("does not agree to load", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--trust-packages", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A loose assembly somebody added by hand came from no feed, so it is reported rather than
    /// attempted — and the run fails, because the folder is still incomplete.
    /// </summary>
    [Fact]
    public void RestoreRefusesALooseAssemblyAndSaysWhy()
    {
        string graph = SaveGraph("loose", ["loose.packages/Handmade.dll"]);

        StringWriter error = new();

        Assert.Equal(1, Program.Pkg(["restore", "--open", graph], new StringWriter(), error, _feed));

        Assert.Contains("Handmade.dll", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("by hand", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Nothing to do is success, and says so rather than printing an empty report.</summary>
    [Fact]
    public void RestoreWithNothingMissingSucceeds()
    {
        string graph = SaveGraph("nothing", []);
        StringWriter output = new();

        Assert.Equal(0, Program.Pkg(["restore", "--open", graph], output, new StringWriter(), _feed));

        Assert.Contains("nothing to restore", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The sub-command is required, and an unknown one is named rather than ignored.</summary>
    [Theory]
    [InlineData(new string[0], "sub-command")]
    [InlineData(new[] { "instal" }, "instal")]
    public void ABadSubCommandIsRefused(string[] args, string expected)
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Pkg(args, new StringWriter(), error));
        Assert.Contains(expected, error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Both sub-commands need a graph, and say which flag is missing.</summary>
    [Theory]
    [InlineData("list")]
    [InlineData("restore")]
    public void EachSubCommandNeedsAGraph(string command)
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Pkg([command], new StringWriter(), error));
        Assert.Contains("--open", error.ToString(), StringComparison.Ordinal);
    }

    private string SaveGraph(string name, string[] packages)
    {
        Graph graph = new();
        string path = Path.Combine(_root, name + ".spark");

        GraphDocument document = GraphDocument.Capture(
            graph,
            positions: null,
            notes: [],
            groups: [],
            appearance: null,
            packages: [.. packages.Select(package => new GraphDocumentPackage(package))]);

        File.WriteAllText(path, SparkFile.Write(document));

        return path;
    }

    private void Publish(string id, string version)
    {
        System.Reflection.Assembly assembly = typeof(Spark.Nodes.Core.Point).Assembly;
        string simpleName = assembly.GetName().Name!;
        string path = Path.Combine(_feed, $"{id}.{version}.nupkg");

        using FileStream file = File.Create(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Create);

        Write(archive, $"{id}.nuspec", $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>Acme Ltd</authors>
                <description>A package for the pkg verb tests.</description>
                <tags>{SparkPackageManifest.Tag}</tags>
              </metadata>
            </package>
            """);

        Write(archive, SparkPackageManifest.PathInPackage, SparkPackageManifest.Write([simpleName]));

        archive.CreateEntryFromFile(assembly.Location, $"lib/net10.0/{simpleName}.dll");
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using StreamWriter writer = new(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

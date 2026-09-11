using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// The <c>.sparkz</c> bundle: a graph and its package folder in one file, and a bundle from somebody
/// else refused when it is wrong (`E3-T20`, ADR-0017).
/// </summary>
public sealed class SparkBundleTests : IDisposable
{
    private const string Graph = "{\n  \"formatVersion\": 5\n}\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public SparkBundleTests() => Directory.CreateDirectory(_root);

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
            // A temporary folder left behind is not a test failure.
        }
    }

    private string In(params string[] parts) => Path.Combine([_root, .. parts]);

    private string WriteGraph(string folder = "work")
    {
        string graph = In(folder, "tower.spark");
        Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
        File.WriteAllText(graph, Graph);
        return graph;
    }

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A zip written by hand, for the bundles Spark would never write.</summary>
    private string Zip(params (string Name, string Text)[] entries)
    {
        string path = In("hand.sparkz");

        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach ((string name, string text) in entries)
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return path;
    }

    /// <summary>
    /// <b>The row.</b> A graph packed with its package folder opens as the same pair: the graph, and the
    /// folder named after it beside it, which is where every loader already looks.
    /// </summary>
    [Fact]
    public void APackedGraphOpensWithItsPackages()
    {
        string graph = WriteGraph();
        WriteFile(In("work", "tower.packages", "Helpers.dll"), "helpers");
        WriteFile(In("work", "tower.packages", "Revit", "lib", "net8.0", "RevitAPI.dll"), "revit");

        SparkBundleContents packed = SparkBundle.Pack(graph, In("share", "..", "tower.sparkz"));

        Assert.Equal(2, packed.PackageFiles);
        Assert.Equal("tower.spark", packed.GraphName);

        string opened = SparkBundle.Unpack(packed.BundlePath, In("opened"));

        Assert.Equal(In("opened", "tower.spark"), opened);
        Assert.Equal(Graph, File.ReadAllText(opened));
        Assert.Equal(In("opened", "tower.packages"), GraphPackages.FolderFor(opened));
        Assert.Equal("helpers", File.ReadAllText(In("opened", "tower.packages", "Helpers.dll")));
        Assert.Equal("revit", File.ReadAllText(In("opened", "tower.packages", "Revit", "lib", "net8.0", "RevitAPI.dll")));
    }

    /// <summary>A graph with no package folder packs alone.</summary>
    [Fact]
    public void AGraphWithNoPackagesPacksAlone()
    {
        SparkBundleContents packed = SparkBundle.Pack(WriteGraph(), In("tower.sparkz"));

        Assert.Equal(0, packed.PackageFiles);
        Assert.Equal(Graph, File.ReadAllText(SparkBundle.Unpack(packed.BundlePath, In("opened"))));
    }

    /// <summary>A half-finished download in the folder is not packed, as Save As does not carry it.</summary>
    [Fact]
    public void AHalfFinishedDownloadIsNotPacked()
    {
        string graph = WriteGraph();
        WriteFile(In("work", "tower.packages", "Helpers.dll"), "helpers");
        WriteFile(In("work", "tower.packages", "Partial" + NuGetPackageClient.StagingSuffix, "lib.dll"), "half");

        SparkBundleContents packed = SparkBundle.Pack(graph, In("tower.sparkz"));
        SparkBundle.Unpack(packed.BundlePath, In("opened"));

        Assert.Equal(1, packed.PackageFiles);
        Assert.False(Directory.Exists(In("opened", "tower.packages", "Partial" + NuGetPackageClient.StagingSuffix)));
    }

    /// <summary>A file that is not a zip is refused as not being a bundle.</summary>
    [Fact]
    public void AFileThatIsNotAZipIsRefused()
    {
        string path = In("text.sparkz");
        File.WriteAllText(path, "not a zip");

        SparkBundleException refused = Assert.Throws<SparkBundleException>(() => SparkBundle.Unpack(path, In("opened")));
        Assert.Contains("not a zip", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A zip with no manifest is not a Spark bundle, and says so.</summary>
    [Fact]
    public void AZipWithoutAManifestIsRefused()
    {
        string path = Zip(("tower.spark", Graph));

        SparkBundleException refused = Assert.Throws<SparkBundleException>(() => SparkBundle.Unpack(path, In("opened")));
        Assert.Contains("manifest.json", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bundle from a newer format is refused by version, rather than opened without whatever the
    /// newer format added.
    /// </summary>
    [Fact]
    public void ANewerBundleIsRefusedByVersion()
    {
        string path = Zip(("manifest.json", "{ \"formatVersion\": 2, \"graph\": \"tower.spark\" }"), ("tower.spark", Graph));

        SparkBundleException refused = Assert.Throws<SparkBundleException>(() => SparkBundle.Unpack(path, In("opened")));
        Assert.Contains("newer", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A manifest naming a graph the bundle does not hold is refused by that name.</summary>
    [Fact]
    public void AManifestNamingAMissingGraphIsRefused()
    {
        string path = Zip(("manifest.json", "{ \"formatVersion\": 1, \"graph\": \"tower.spark\" }"), ("other.spark", Graph));

        SparkBundleException refused = Assert.Throws<SparkBundleException>(() => SparkBundle.Unpack(path, In("opened")));
        Assert.Contains("'tower.spark'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The zip-slip.</b> An entry that would land outside the folder refuses the whole bundle, and
    /// nothing is written - not the escaping file, and not the graph beside it.
    /// </summary>
    [Fact]
    public void AnEntryEscapingTheFolderRefusesTheBundleAndWritesNothing()
    {
        string path = Zip(
            ("manifest.json", "{ \"formatVersion\": 1, \"graph\": \"tower.spark\" }"),
            ("tower.spark", Graph),
            ("../escaped.txt", "outside"));

        SparkBundleException refused = Assert.Throws<SparkBundleException>(() => SparkBundle.Unpack(path, In("opened")));

        Assert.Contains("outside the folder", refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(In("escaped.txt")));
        Assert.False(File.Exists(In("opened", "tower.spark")));
    }
}

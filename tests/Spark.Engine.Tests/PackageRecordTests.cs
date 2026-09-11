using System;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// A <c>.spark</c> file names the packages it expects beside it — the format half of `E7-T17`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The load order is in the file's order.</b> Opening a graph compiles its code blocks, so what
/// they compile against has to be settled before the first node is touched; the list is therefore
/// written straight after <c>formatVersion</c>, before <c>nodes</c>, where a person opening the file
/// in an editor also reads it first.
/// </para>
/// <para>
/// <b>And `E7-T7` is re-proved here rather than inherited.</b> A file naming its packages re-saves
/// byte for byte, and a file naming none is exactly what earlier builds wrote — version 1, and no
/// <c>packages</c> key anywhere in it.
/// </para>
/// </remarks>
public sealed class PackageRecordTests
{
    private static readonly GraphDocumentPackage[] Two =
    [
        new("tower.packages/MathNet.Numerics.5.0.0"),
        new("tower.packages/Helpers.dll"),
    ];

    /// <summary>
    /// <b>Version 5, and only when there is a list.</b> A version-4 reader would open the graph and
    /// drop the list on the next save, so a file carrying one must refuse to open in it.
    /// </summary>
    [Fact]
    public void AGraphThatNamesItsPackagesIsWrittenAsVersionFive() =>
        Assert.Contains(
            "\"formatVersion\": 5",
            SparkFile.Write(GraphDocument.Capture(new Graph(), packages: Two)),
            StringComparison.Ordinal);

    /// <summary>A graph naming no package is untouched by the new version.</summary>
    [Fact]
    public void AGraphThatNamesNoPackageIsUntouched()
    {
        string text = SparkFile.Write(GraphDocument.Capture(new Graph()));

        Assert.Contains("\"formatVersion\": 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("packages", text, StringComparison.Ordinal);
    }

    /// <summary><b>First in the file</b>, after the version and before any node.</summary>
    [Fact]
    public void ThePackagesAreWrittenBeforeTheNodes()
    {
        string text = SparkFile.Write(GraphDocument.Capture(new Graph(), packages: Two));

        int version = text.IndexOf("\"formatVersion\"", StringComparison.Ordinal);
        int packages = text.IndexOf("\"packages\"", StringComparison.Ordinal);
        int nodes = text.IndexOf("\"nodes\"", StringComparison.Ordinal);

        Assert.True(version < packages && packages < nodes, text);
    }

    /// <summary><b>`E7-T7`, re-proved</b> for a file that carries the new section.</summary>
    [Fact]
    public void AFileNamingItsPackagesReSavesByteForByte()
    {
        string first = SparkFile.Write(GraphDocument.Capture(new Graph(), packages: Two));
        GraphDocument read = SparkFile.Read(first);

        Assert.Equal(2, read.Packages.Count);
        Assert.Equal(first, SparkFile.Write(read));
    }

    /// <summary>
    /// The list is canonical however it was assembled: ordered by path, and a path named twice in
    /// two spellings of case is named once, because the file system does not tell them apart.
    /// </summary>
    [Fact]
    public void TheListIsCanonicalHoweverItWasAssembled()
    {
        string forwards = SparkFile.Write(GraphDocument.Capture(new Graph(), packages: [Two[0], Two[1]]));
        string shuffled = SparkFile.Write(GraphDocument.Capture(
            new Graph(),
            packages: [Two[1], new("tower.packages/helpers.dll"), Two[0]]));

        Assert.Equal(forwards, shuffled);
    }

    /// <summary>A message about a package names its last segment, folder or file alike.</summary>
    [Fact]
    public void APackageIsNamedByItsLastSegment()
    {
        Assert.Equal("Helpers.dll", new GraphDocumentPackage("tower.packages/Helpers.dll").Name);
        Assert.Equal("MathNet.Numerics.5.0.0", new GraphDocumentPackage("tower.packages/MathNet.Numerics.5.0.0/").Name);
    }

    /// <summary>
    /// An entry with no path is refused, like a note with no identity: a record that cannot say
    /// where the package is cannot say that it is missing.
    /// </summary>
    [Fact]
    public void AnEntryWithNoPathIsRefused() =>
        Assert.Throws<SparkFileException>(() => SparkFile.Read(
            """{"formatVersion": 5, "packages": [ {} ], "nodes": [], "wires": []}"""));
}

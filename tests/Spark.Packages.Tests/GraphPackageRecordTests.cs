using System;
using System.IO;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// What a graph's file says it needs, checked against its folder (`E7-T17`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The client's call was <i>fail loudly with a named list</i></b> when the folder is missing, and a
/// folder with less in it is not missing anything — so the file records what it expects, and these
/// are the rules for checking and writing that record.
/// </para>
/// <para>
/// <b>Two of them protect somebody else's graph.</b> A recorded package that is missing is never
/// dropped on save, which is `E7-T7`'s byte-for-byte re-save; and a recorded path that leaves the
/// folder is never looked up, so a file from elsewhere cannot probe this machine.
/// </para>
/// </remarks>
public sealed class GraphPackageRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private string Graph => Path.Combine(_root, "project1.spark");

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
            // The assertion has been made by the time this runs.
        }
    }

    /// <summary>Entries that are there — a loose assembly and a package folder — are not absent.</summary>
    [Fact]
    public void AnEntryThatIsThereIsNotAbsent()
    {
        Put("project1.packages/Helpers.dll");
        Directory.CreateDirectory(Path.Combine(_root, "project1.packages", "MathNet.Numerics.5.0.0"));

        Assert.Empty(GraphPackages.Absent(
            Graph, ["project1.packages/Helpers.dll", "project1.packages/MathNet.Numerics.5.0.0"]));
    }

    /// <summary>One that is not there is named, with the folder that was searched.</summary>
    [Fact]
    public void AnEntryThatIsNotThereIsNamed()
    {
        AbsentGraphPackage gone = Assert.Single(GraphPackages.Absent(Graph, ["project1.packages/Gone.dll"]));

        Assert.Equal("Gone.dll", gone.Name);
        Assert.Equal("project1.packages/Gone.dll", gone.Recorded);
        Assert.Equal(GraphPackages.FolderFor(Graph), gone.LookedIn);
    }

    /// <summary>
    /// <b>A file renamed outside Spark looks in the folder named after it now</b>, and says so —
    /// the client's call, rather than guessing which nearby folder was meant. Rename the folder to
    /// match and the package is found.
    /// </summary>
    [Fact]
    public void ARenamedFileLooksInTheFolderNamedAfterIt()
    {
        Put("project1.packages/Helpers.dll");
        string renamed = Path.Combine(_root, "project2.spark");

        AbsentGraphPackage absent = Assert.Single(GraphPackages.Absent(renamed, ["project1.packages/Helpers.dll"]));
        Assert.Equal(GraphPackages.FolderFor(renamed), absent.LookedIn);

        Put("project2.packages/Helpers.dll");
        Assert.Empty(GraphPackages.Absent(renamed, ["project1.packages/Helpers.dll"]));
    }

    /// <summary>
    /// <b>Confined, and never resolved when it is not.</b> Every one of these names a file that
    /// exists, and every one is reported absent without being looked up.
    /// </summary>
    [Fact]
    public void APathThatLeavesTheFolderIsNeverResolved()
    {
        Put("escape.dll");

        Assert.Equal(
            3,
            GraphPackages.Absent(
                Graph,
                ["project1.packages/../escape.dll", Path.Combine(_root, "escape.dll"), "/escape.dll"]).Length);
    }

    /// <summary>
    /// The folder is listed as a file records it — a package folder and a loose assembly each once,
    /// and neither a staged download nor a file that is not an assembly.
    /// </summary>
    [Fact]
    public void EntriesListPackagesAndLooseAssembliesButNotAStagedDownload()
    {
        Put("project1.packages/Helpers.dll");
        Put("project1.packages/readme.txt");
        Directory.CreateDirectory(Path.Combine(_root, "project1.packages", "MathNet.Numerics.5.0.0"));
        Directory.CreateDirectory(Path.Combine(_root, "project1.packages", "Half.1.0.0" + NuGetPackageClient.StagingSuffix));

        Assert.Equal(
            new[] { "project1.packages/Helpers.dll", "project1.packages/MathNet.Numerics.5.0.0" },
            GraphPackages.Entries(Graph));
    }

    /// <summary>
    /// <b>`E7-T7`: a recorded package that is missing is never dropped</b>, and what is in the folder
    /// is added beside it.
    /// </summary>
    [Fact]
    public void ARecordedPackageThatIsMissingIsNeverDropped()
    {
        Put("project1.packages/Helpers.dll");

        Assert.Equal(
            new[] { "project1.packages/Gone.dll", "project1.packages/Helpers.dll" },
            GraphPackages.ToRecord(Graph, ["project1.packages/Gone.dll"]));
    }

    /// <summary>Save As records the new name, because the folder is named after the file.</summary>
    [Fact]
    public void SaveAsRecordsUnderTheNewName() =>
        Assert.Equal(
            new[] { "project2.packages/Gone.dll" },
            GraphPackages.ToRecord(Path.Combine(_root, "project2.spark"), ["project1.packages/Gone.dll"]));

    /// <summary>
    /// An entry already recorded under this file's folder is kept exactly as written, spelling of
    /// case included, so an untouched graph produces no diff — and it is not recorded twice when
    /// the folder lists it again.
    /// </summary>
    [Fact]
    public void AnEntryAlreadyUnderThisNameIsKeptExactlyAsWritten()
    {
        Put("project1.packages/Helpers.dll");

        Assert.Equal(
            new[] { "Project1.packages/Helpers.dll" },
            GraphPackages.ToRecord(Graph, ["Project1.packages/Helpers.dll"]));
    }

    /// <summary>A graph with no file has nothing to discover and nothing to rename against.</summary>
    [Fact]
    public void AGraphWithNoFileCarriesItsListUnchanged() =>
        Assert.Equal(
            new[] { "project1.packages/Gone.dll" },
            GraphPackages.ToRecord(null, ["project1.packages/Gone.dll"]));

    private void Put(string relative)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }
}

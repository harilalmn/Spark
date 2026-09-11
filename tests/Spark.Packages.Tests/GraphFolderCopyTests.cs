using System;
using System.Collections.Generic;
using System.IO;
using Spark.Packages;

namespace Spark.Packages.Tests;

/// <summary>
/// Save As carries a graph's package folder to the new file (`E7-T19`).
/// </summary>
/// <remarks>
/// <b>The client's calls, all three</b>: copy rather than move, because the original must still
/// open; a rename outside Spark is not tracked; and the folder travels with the one rename Spark
/// performs itself. The last test is the reason the row is urgent: since `E7-T17` a saved-as file
/// records its packages under the new name, and it only reopens clean if the folder came too.
/// </remarks>
public sealed class GraphFolderCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private string Original => Path.Combine(_root, "project1.spark");

    private string Copy => Path.Combine(_root, "project2.spark");

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

    /// <summary>
    /// <b>Copied, and the original left working</b> — a loose assembly and a package folder alike.
    /// </summary>
    [Fact]
    public void SaveAsCopiesTheFolderAndLeavesTheOriginal()
    {
        Put("project1.packages/Helpers.dll", "helpers");
        Put("project1.packages/Pkg.1.0.0/lib/net8.0/Pkg.dll", "pkg");

        GraphFolderCopy copy = GraphPackages.CopyFolder(Original, Copy);

        Assert.Equal(2, copy.Copied);
        Assert.Equal(0, copy.Kept);
        Assert.Equal("helpers", Read("project2.packages/Helpers.dll"));
        Assert.Equal("pkg", Read("project2.packages/Pkg.1.0.0/lib/net8.0/Pkg.dll"));
        Assert.Equal("helpers", Read("project1.packages/Helpers.dll"));
    }

    /// <summary>A Save to the same file carries nothing.</summary>
    [Fact]
    public void ASaveToTheSameFileCopiesNothing()
    {
        Put("project1.packages/Helpers.dll", "helpers");

        GraphFolderCopy copy = GraphPackages.CopyFolder(Original, Original);

        Assert.Equal(0, copy.Copied);
        Assert.Equal(0, copy.Kept);
    }

    /// <summary>A graph with no folder has nothing to carry, and no folder is invented.</summary>
    [Fact]
    public void AGraphWithNoFolderCopiesNothing()
    {
        GraphFolderCopy copy = GraphPackages.CopyFolder(Original, Copy);

        Assert.Equal(0, copy.Copied);
        Assert.False(Directory.Exists(GraphPackages.FolderFor(Copy)));
    }

    /// <summary>
    /// <b>Merged, and nothing already there is overwritten</b>: it belongs to the graph that was saved
    /// under that name before, and replacing its library with another build would change what that
    /// graph computes.
    /// </summary>
    [Fact]
    public void AFolderAlreadyThereIsMergedAndNothingInItIsOverwritten()
    {
        Put("project1.packages/Helpers.dll", "ours");
        Put("project1.packages/Extra.dll", "extra");
        Put("project2.packages/Helpers.dll", "theirs");

        GraphFolderCopy copy = GraphPackages.CopyFolder(Original, Copy);

        Assert.Equal(1, copy.Copied);
        Assert.Equal(1, copy.Kept);
        Assert.Equal("theirs", Read("project2.packages/Helpers.dll"));
        Assert.Equal("extra", Read("project2.packages/Extra.dll"));
    }

    /// <summary>A staged download is half an extract, and is not carried.</summary>
    [Fact]
    public void AStagedDownloadIsNotCarried()
    {
        Put("project1.packages/Half.1.0.0" + NuGetPackageClient.StagingSuffix + "/lib/Half.dll", "half");

        GraphFolderCopy copy = GraphPackages.CopyFolder(Original, Copy);

        Assert.Equal(0, copy.Copied);
        Assert.False(Directory.Exists(GraphPackages.FolderFor(Copy)));
    }

    /// <summary>
    /// <b>Why the row is urgent</b>: the saved-as file records its packages under the new name, and
    /// once the folder has come with it, nothing it records is absent.
    /// </summary>
    [Fact]
    public void AfterTheCopyTheNewFileRecordsNothingAbsent()
    {
        Put("project1.packages/Helpers.dll", "helpers");

        _ = GraphPackages.CopyFolder(Original, Copy);

        IReadOnlyList<string> recorded = GraphPackages.ToRecord(Copy, ["project1.packages/Helpers.dll"]);

        Assert.Equal(new[] { "project2.packages/Helpers.dll" }, recorded);
        Assert.Empty(GraphPackages.Absent(Copy, recorded));
    }

    private void Put(string relative, string content)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Read(string relative) =>
        File.ReadAllText(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
}

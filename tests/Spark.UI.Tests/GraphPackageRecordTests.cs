using System;
using System.IO;
using System.Linq;
using Spark.Engine;
using Spark.Packages;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The window writes the list of packages a graph expects, and names the ones that are missing
/// (`E7-T17`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The client's call was <i>fail loudly with a named list</i>, and it is read as a banner, not a
/// refusal.</b> `E7-T6`'s promise is that nobody's graph is damaged by opening it, and a graph that
/// was refused could not be repaired — so it opens, and the message names what is absent before
/// anybody sees a compile error about a type.
/// </para>
/// <para>
/// <b>`E7-T7` is re-proved through the window</b>: a graph naming a package this machine does not
/// have saves back byte for byte.
/// </para>
/// </remarks>
public sealed class GraphPackageRecordTests : IDisposable
{
    private readonly string _root;
    private readonly string _graph;
    private readonly string _folder;
    private readonly PackageTrustStore _trust;

    public GraphPackageRecordTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-package-record", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);

        _graph = Path.Combine(_root, "facade.spark");
        _folder = GraphPackages.FolderFor(_graph);
        _trust = new PackageTrustStore(Path.Combine(_root, "trusted.json"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The next run makes its own.
        }
    }

    /// <summary><b>The row</b>: a recorded package that is not there is named when the graph opens.</summary>
    [Fact]
    public void AGraphWhosePackageIsMissingOpensAndNamesIt()
    {
        using MainWindowViewModel model = Model();

        Assert.True(model.TryOpenDocument(Naming("facade.packages/Gone.dll"), _graph));

        Assert.Equal("Gone.dll", Assert.Single(model.AbsentPackageNames));
        Assert.Contains("Gone.dll", model.AbsentPackagesMessage, StringComparison.Ordinal);
        Assert.Contains("facade.packages", model.AbsentPackagesMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>`E7-T7`, through the window</b>: opened on a machine without the package, saved untouched,
    /// the file is exactly what it was.
    /// </summary>
    [Fact]
    public void AGraphNamingAMissingPackageSavesUnchanged()
    {
        string text = Naming("facade.packages/Gone.dll");

        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(text, _graph));

        Assert.Equal(text, model.TrySaveDocument());
    }

    /// <summary>A package that is where the file says is not reported.</summary>
    [Fact]
    public void APackageThatIsThereIsNotReported()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Helpers.dll"), "x");

        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming("facade.packages/Helpers.dll"), _graph));

        Assert.Empty(model.AbsentPackageNames);
        Assert.Null(model.AbsentPackagesMessage);
    }

    /// <summary>
    /// Saving records what is in the folder — including an assembly nobody wrote down — first in the
    /// file, at version 5.
    /// </summary>
    [Fact]
    public void SavingRecordsWhatIsInTheFolder()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Helpers.dll"), "x");

        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming(), _graph));

        string saved = Assert.IsType<string>(model.TrySaveDocument());

        Assert.Contains("\"formatVersion\": 5", saved, StringComparison.Ordinal);
        Assert.Contains("\"path\": \"facade.packages/Helpers.dll\"", saved, StringComparison.Ordinal);
        Assert.True(
            saved.IndexOf("\"packages\"", StringComparison.Ordinal) < saved.IndexOf("\"nodes\"", StringComparison.Ordinal));
    }

    /// <summary>Save As writes the list for the file it is about to become.</summary>
    [Fact]
    public void SaveAsWritesTheListForTheNewName()
    {
        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming("facade.packages/Gone.dll"), _graph));

        string saved = Assert.IsType<string>(model.TrySaveDocument(Path.Combine(_root, "renamed.spark")));

        Assert.Contains("renamed.packages/Gone.dll", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("facade.packages", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file renamed in Explorer says which folder it looked in and which one it names, because
    /// that is what a user needs to know to rename one of them back.
    /// </summary>
    [Fact]
    public void ARenamedFileSaysWhichFolderToRename()
    {
        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming("tower.packages/Helpers.dll"), _graph));

        Assert.Contains("'facade.packages'", model.AbsentPackagesMessage, StringComparison.Ordinal);
        Assert.Contains("'tower.packages'", model.AbsentPackagesMessage, StringComparison.Ordinal);
    }

    /// <summary>A new document expects nothing, and says nothing about the last one's packages.</summary>
    [Fact]
    public void ReplacingTheDocumentForgetsTheList()
    {
        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming("facade.packages/Gone.dll"), _graph));

        model.NewGraph();

        Assert.Empty(model.AbsentPackageNames);
        Assert.Null(model.AbsentPackagesMessage);
        Assert.DoesNotContain("packages", model.TrySaveDocument(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>`E7-T19` through the window</b>: Save As carries the folder, the saved-as copy reopens with
    /// nothing absent, the original still has its folder, and the package gate follows the file.
    /// </summary>
    [Fact]
    public void SaveAsCarriesThePackagesAndTheCopyReopensClean()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Helpers.dll"), "x");

        string renamed = Path.Combine(_root, "renamed.spark");

        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming("facade.packages/Helpers.dll"), _graph));

        Assert.Contains("renamed.packages", model.CarryPackagesTo(renamed), StringComparison.Ordinal);

        string text = Assert.IsType<string>(model.TrySaveDocument(renamed));
        File.WriteAllText(renamed, text);
        model.NoteSavedTo(renamed);

        Assert.Equal(GraphPackages.FolderFor(renamed), model.PackageGate.Folder);
        Assert.Empty(model.AbsentPackageNames);
        Assert.True(File.Exists(Path.Combine(_folder, "Helpers.dll")), "the original lost its package");

        using MainWindowViewModel reopened = Model();
        Assert.True(reopened.TryOpenDocument(text, renamed));
        Assert.Empty(reopened.AbsentPackageNames);
    }

    /// <summary>A Save to the file the graph already lives in carries nothing.</summary>
    [Fact]
    public void ASaveToTheSameFileCarriesNothing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "Helpers.dll"), "x");

        using MainWindowViewModel model = Model();
        Assert.True(model.TryOpenDocument(Naming("facade.packages/Helpers.dll"), _graph));

        Assert.Null(model.CarryPackagesTo(_graph));
    }

    private MainWindowViewModel Model()
    {
        MainWindowViewModel model = new();
        model.PackageTrust = _trust;

        return model;
    }

    /// <summary>An empty graph whose file names these packages.</summary>
    private static string Naming(params string[] paths) =>
        SparkFile.Write(GraphDocument.Capture(
            new Spark.Engine.Graph(),
            packages: [.. paths.Select(path => new GraphDocumentPackage(path))]));
}

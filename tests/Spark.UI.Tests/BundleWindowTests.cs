using System;
using System.IO;
using System.Threading.Tasks;
using Spark.Engine;
using Spark.Packages;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The application opens a <c>.sparkz</c> and shares one (`E3-T20` step C).
/// </summary>
/// <remarks>
/// <para>
/// ADR-0017: the bundle <i>only works if it is discoverable, and if it is buried in the CLI people
/// will hit this before they find it</i>. So Open takes one, and Share as bundle… makes one.
/// </para>
/// <para>
/// <b>Each test settles the model's own first run before asserting on the diagnostics pane</b>,
/// because a run applied late would write over a refusal's message.
/// </para>
/// </remarks>
public sealed class BundleWindowTests : IDisposable
{
    private static readonly string Graph = SparkFile.Write(GraphDocument.Capture(
        new Spark.Engine.Graph(),
        packages: [new GraphDocumentPackage("tower.packages/Helpers.dll")]));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "spark-bundle-window", Guid.NewGuid().ToString("n"));
    private readonly PackageTrustStore _trust;

    public BundleWindowTests()
    {
        Directory.CreateDirectory(_root);
        _trust = new PackageTrustStore(Path.Combine(_root, "trusted.json"));
    }

    private string Bundles => Path.Combine(_root, "bundles");

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

    /// <summary>
    /// <b>The row.</b> A bundle opens as its graph, from a folder under the bundle store, with its
    /// package where the file says - so nothing is reported missing - and the same document.
    /// </summary>
    [Fact]
    public async Task ABundleOpensAsItsGraphWithItsPackagesBesideIt()
    {
        string bundle = SparkBundle.Pack(WriteGraph(), Path.Combine(_root, "tower.sparkz")).BundlePath;

        using MainWindowViewModel model = await Model();

        Assert.True(model.TryOpenBundle(bundle));

        string opened = Assert.IsType<string>(model.GraphPath);
        Assert.StartsWith(Bundles, opened, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("tower.spark", Path.GetFileName(opened));
        Assert.Empty(model.AbsentPackageNames);
        Assert.Equal(Graph, model.TrySaveDocument());
    }

    /// <summary>
    /// A bundle named at startup - <c>--open tower.sparkz</c>, or a double-click - opens exactly as
    /// File ▸ Open would open it.
    /// </summary>
    [Fact]
    public async Task ABundleNamedAtStartupOpensAsItsGraph()
    {
        string bundle = SparkBundle.Pack(WriteGraph(), Path.Combine(_root, "tower.sparkz")).BundlePath;

        using MainWindowViewModel model = new(startupGraph: null, bundle, noScript: false, Bundles);
        await model.EvaluateAsync();

        string opened = Assert.IsType<string>(model.GraphPath);
        Assert.StartsWith(Bundles, opened, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("tower.spark", Path.GetFileName(opened));
        Assert.Equal(Graph, model.TrySaveDocument());
    }

    /// <summary>The same bundle opened twice lands in one folder, not two.</summary>
    [Fact]
    public async Task OpeningTheSameBundleTwiceReusesOneFolder()
    {
        string bundle = SparkBundle.Pack(WriteGraph(), Path.Combine(_root, "tower.sparkz")).BundlePath;

        using MainWindowViewModel first = await Model();
        using MainWindowViewModel second = await Model();

        Assert.True(first.TryOpenBundle(bundle));
        Assert.True(second.TryOpenBundle(bundle));

        Assert.Single(Directory.GetDirectories(Bundles));
        Assert.Equal(first.GraphPath, second.GraphPath);
    }

    /// <summary>A bundle that is refused says why, and the graph on the canvas is left as it was.</summary>
    [Fact]
    public async Task ARefusedBundleIsReportedAndTheGraphIsLeftAlone()
    {
        string bad = Path.Combine(_root, "bad.sparkz");
        File.WriteAllText(bad, "not a zip");

        using MainWindowViewModel model = await Model();
        string? before = model.TrySaveDocument();

        Assert.False(model.TryOpenBundle(bad));
        Assert.Contains("not a zip", model.DiagnosticsText, StringComparison.Ordinal);
        Assert.Equal(before, model.TrySaveDocument());
    }

    /// <summary>A saved graph is shared as a bundle beside it, which opens to the same file and folder.</summary>
    [Fact]
    public async Task ASavedGraphIsSharedAsABundleBesideIt()
    {
        string graph = WriteGraph();

        using MainWindowViewModel model = await Model();
        Assert.True(model.TryOpenDocument(File.ReadAllText(graph), graph));

        string shared = Assert.IsType<string>(model.ShareAsBundle());
        Assert.Equal(Path.Combine(_root, "work", "tower.sparkz"), shared);

        string unpacked = SparkBundle.Unpack(shared, Path.Combine(_root, "check"));
        Assert.Equal(Graph, File.ReadAllText(unpacked));
        Assert.True(File.Exists(Path.Combine(_root, "check", "tower.packages", "Helpers.dll")));
    }

    /// <summary>A graph with no file is not shared, and the pane says to save it first.</summary>
    [Fact]
    public async Task AGraphWithNoFileIsNotSharedAndSaysWhy()
    {
        using MainWindowViewModel model = await Model();

        Assert.Null(model.ShareAsBundle());
        Assert.Contains("Save the graph first", model.DiagnosticsText, StringComparison.Ordinal);
    }

    private async Task<MainWindowViewModel> Model()
    {
        MainWindowViewModel model = new();
        model.PackageTrust = _trust;
        model.BundleFolder = Bundles;

        await model.EvaluateAsync();
        return model;
    }

    private string WriteGraph()
    {
        string folder = Path.Combine(_root, "work", "tower.packages");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Helpers.dll"), "x");

        string graph = Path.Combine(_root, "work", "tower.spark");
        File.WriteAllText(graph, Graph);
        return graph;
    }
}

using System.Collections.Generic;
using Spark.UI;
using Spark.UI.Shell;

namespace Spark.UI.Tests;

/// <summary>
/// The serialisable layout model, and the command-line options that drive the measurement modes.
/// </summary>
public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void FractionsAreClampedSoNoPaneBecomesImpossibleToGrab()
    {
        WorkspaceLayout layout = new()
        {
            LibraryFraction = 5,
            InspectorFraction = -1,
            CanvasFraction = 0,
        };

        // A corrupt or hand-edited settings file must not produce a pane a user cannot recover.
        Assert.InRange(layout.LibraryFraction, 0.08, 0.60);
        Assert.InRange(layout.InspectorFraction, 0.08, 0.60);
        Assert.InRange(layout.CanvasFraction, 0.08, 0.92);
    }

    [Fact]
    public void ALayoutRoundTripsThroughJson()
    {
        WorkspaceLayout original = new()
        {
            LibraryFraction = 0.22,
            InspectorFraction = 0.18,
            CanvasFraction = 0.4,
        };

        original.SetVisible(WorkspacePane.Inspector, false);

        WorkspaceLayout restored = WorkspaceLayout.FromJson(original.ToJson());

        Assert.Equal(0.22, restored.LibraryFraction);
        Assert.Equal(0.18, restored.InspectorFraction);
        Assert.Equal(0.4, restored.CanvasFraction);
        Assert.False(restored.IsVisible(WorkspacePane.Inspector));
        Assert.True(restored.IsVisible(WorkspacePane.Canvas));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    public void MalformedJsonFallsBackToTheDefaultLayout(string? json)
    {
        // A settings file that fails to parse must not stop the application from starting. The
        // worst outcome of a bad layout file is a default layout.
        WorkspaceLayout layout = WorkspaceLayout.FromJson(json);

        Assert.Equal(WorkspaceLayout.Default.LibraryFraction, layout.LibraryFraction);
        Assert.True(layout.IsVisible(WorkspacePane.Canvas));
    }

    [Fact]
    public void ALayoutWithNoVisiblePanesStillShowsTheCanvas()
    {
        WorkspaceLayout layout = WorkspaceLayout.FromJson(
            """{"Library":0.2,"Inspector":0.2,"Canvas":0.5,"Panes":[]}""");

        Assert.True(layout.IsVisible(WorkspacePane.Canvas));
    }

    [Fact]
    public void EveryPresetIsUsableAndDistinct()
    {
        IReadOnlyDictionary<string, WorkspaceLayout> presets = WorkspaceLayout.Presets();

        Assert.Equal(4, presets.Count);

        foreach ((string name, WorkspaceLayout preset) in presets)
        {
            Assert.True(preset.IsVisible(WorkspacePane.Canvas), $"'{name}' hides the canvas.");
            Assert.InRange(preset.LibraryFraction, 0.08, 0.60);
            Assert.InRange(preset.CanvasFraction, 0.08, 0.92);
        }

        Assert.False(presets["Presenting"].IsVisible(WorkspacePane.Library));
        Assert.False(presets["Modelling"].IsVisible(WorkspacePane.Inspector));
    }

    [Fact]
    public void ApplyingAPresetCopiesItRatherThanSharingIt()
    {
        WorkspaceLayout live = WorkspaceLayout.Default;
        WorkspaceLayout preset = WorkspaceLayout.Presets()["Presenting"];

        live.CopyFrom(preset);
        live.SetVisible(WorkspacePane.Library, true);

        // Presets are values, not shared state: applying one and then changing the live layout
        // must not edit the preset for next time.
        Assert.False(WorkspaceLayout.Presets()["Presenting"].IsVisible(WorkspacePane.Library));
    }

    [Fact]
    public void ResettingReturnsEveryPane()
    {
        WorkspaceLayout layout = WorkspaceLayout.Default;
        layout.SetVisible(WorkspacePane.Library, false);
        layout.SetVisible(WorkspacePane.Viewport, false);

        layout.CopyFrom(WorkspaceLayout.Default);

        Assert.True(layout.IsVisible(WorkspacePane.Library));
        Assert.True(layout.IsVisible(WorkspacePane.Viewport));
        Assert.True(layout.IsVisible(WorkspacePane.Inspector));
        Assert.True(layout.IsVisible(WorkspacePane.Canvas));
    }

    [Fact]
    public void NoArgumentsMeansTheOrdinaryInteractiveStart()
    {
        StartupOptions options = StartupOptions.Parse(null);

        Assert.Equal(StartupOptions.Default, options);
        Assert.False(options.IsBenchmark);
        Assert.False(options.IsScreenshot);
    }

    [Fact]
    public void TheBenchmarkImpliesTheNodeCountTheAdrNames()
    {
        StartupOptions options = StartupOptions.Parse(["--canvas-benchmark"]);

        Assert.True(options.IsBenchmark);
        Assert.Equal(2000, options.SyntheticNodeCount);
    }

    [Fact]
    public void TheBenchmarkTakesAnExplicitFrameCountAndNodeCount()
    {
        StartupOptions options = StartupOptions.Parse(["--canvas-benchmark", "900", "--nodes", "500", "--zoom", "0.5"]);

        Assert.Equal(900, options.BenchmarkFrames);
        Assert.Equal(500, options.SyntheticNodeCount);
        Assert.Equal(0.5, options.BenchmarkZoom);
    }

    [Fact]
    public void ASwitchFollowingTheBenchmarkIsNotEatenAsItsFrameCount()
    {
        StartupOptions options = StartupOptions.Parse(["--canvas-benchmark", "--nodes", "300"]);

        Assert.Equal(600, options.BenchmarkFrames);
        Assert.Equal(300, options.SyntheticNodeCount);
    }

    [Fact]
    public void UnparseableCountsFallBackRatherThanThrowing()
    {
        StartupOptions options = StartupOptions.Parse(["--nodes", "banana", "--zoom", "banana"]);

        Assert.Equal(0, options.SyntheticNodeCount);
        Assert.Equal(0, options.BenchmarkZoom);
    }

    [Fact]
    public void ScreenshotModeTakesAPrefix()
    {
        StartupOptions options = StartupOptions.Parse(["--screenshot", "out/spark"]);

        Assert.True(options.IsScreenshot);
        Assert.Equal("out/spark", options.ScreenshotPrefix);
    }
}

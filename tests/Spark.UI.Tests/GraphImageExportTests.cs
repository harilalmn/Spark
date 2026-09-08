using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Spark.UI;
using Spark.UI.Canvas;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// `E8-T69` — exporting the graph to a PNG at a chosen resolution.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>provide option to export the graph to png. User should be
/// able to set the export resolution, with option to lock aspect ratio and default width and
/// height being the canvas size.</i> A canvas-sized picture is a screenshot anybody could take;
/// the resolution is what makes it worth having a command for.
/// </para>
/// <para>
/// <b>The arithmetic and the render are tested apart, because they fail apart.</b> The aspect lock
/// is pure and lives in <see cref="CanvasExport"/>, so it is asserted without a window. The render
/// needs one, and this suite's headless window draws nothing ([N124](../../docs/NOTES.md)) — so
/// what is asserted about the render is what is ours rather than the framework's: it writes a
/// file, and it leaves the view exactly where it found it. The picture is checked against the
/// running application through <c>--export-graph</c>.
/// </para>
/// </remarks>
public sealed class GraphImageExportTests
{
    /// <summary>A size outside the permitted range comes back inside it, on both ends.</summary>
    [Fact]
    public void ARequestedSizeIsClampedToWhatCanBeDrawn()
    {
        Assert.Equal(CanvasExport.MinimumPixels, CanvasExport.Clamp(0));
        Assert.Equal(CanvasExport.MinimumPixels, CanvasExport.Clamp(-4000));
        Assert.Equal(CanvasExport.MaximumPixels, CanvasExport.Clamp(int.MaxValue));
        Assert.Equal(1920, CanvasExport.Clamp(1920));
    }

    /// <summary>
    /// <b>The lock derives the other number from the ratio the dialog opened at.</b> Doubling the
    /// width doubles the height, and the picture keeps its shape.
    /// </summary>
    [Fact]
    public void TheAspectLockDerivesTheOtherSide()
    {
        double aspect = CanvasExport.Aspect(1480, 900);

        Assert.Equal(900, CanvasExport.HeightFor(1480, aspect));
        Assert.Equal(1800, CanvasExport.HeightFor(2960, aspect));
        Assert.Equal(1480, CanvasExport.WidthFor(900, aspect));
        Assert.Equal(2960, CanvasExport.WidthFor(1800, aspect));
    }

    /// <summary>
    /// <b>The ratio is a constant, which is what stops it drifting.</b> Deriving the height from a
    /// width and then the width back from that height returns the width it started at — and it is
    /// deriving each from the *other box* rather than from the ratio that makes it wander.
    /// </summary>
    [Theory]
    [InlineData(1480, 900)]
    [InlineData(1920, 1080)]
    [InlineData(800, 1200)]
    public void DerivingBackAndForwardIsStable(int width, int height)
    {
        double aspect = CanvasExport.Aspect(width, height);

        int derived = CanvasExport.HeightFor(width, aspect);

        Assert.Equal(width, CanvasExport.WidthFor(derived, aspect));
    }

    /// <summary>
    /// A half-typed box is a moment, not a mistake: a degenerate size answers with a square ratio
    /// rather than an exception or an infinity.
    /// </summary>
    [Fact]
    public void ADegenerateSizeHasASquareRatio()
    {
        Assert.Equal(1, CanvasExport.Aspect(0, 900));
        Assert.Equal(1, CanvasExport.Aspect(1480, 0));
        Assert.Equal(1, CanvasExport.Aspect(double.NaN, 900));
    }

    /// <summary>
    /// <b>The export writes a file, and the pixels in it are not this suite's to assert.</b>
    /// </summary>
    /// <remarks>
    /// <b>The headless platform runs with <c>UseHeadlessDrawing</c>, which draws nothing</b>
    /// ([N124](../../docs/NOTES.md)) — so a <c>RenderTargetBitmap</c> saved here is an empty file
    /// whatever the scene was, and an assertion about its dimensions or its content would be an
    /// assertion about Avalonia's null backend. What this asserts is the part that is ours: the
    /// call completes, and it made a file. The image itself is checked by
    /// <c>--export-graph … --export-size 2400x1500</c> against the real application, which is the
    /// switch that exists for exactly this reason.
    /// </remarks>
    [Fact]
    public void TheExportWritesAFile() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = TestGraphs.Demo();
        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        string path = Path.Combine(Path.GetTempPath(), $"spark-export-{Guid.NewGuid():N}.png");

        try
        {
            canvas.ExportImage(path, 1234, 567);

            Assert.True(File.Exists(path), "the export wrote no file at all");
        }
        finally
        {
            File.Delete(path);
            window.Close();
        }
    });

    /// <summary>
    /// <b>An export is not an edit, and the view has to be exactly where it was.</b> The render
    /// fits the whole graph into the image, which means moving the view — and a user who exports
    /// and finds their canvas somewhere else has been charged for a file.
    /// </summary>
    [Fact]
    public void ExportingLeavesTheViewExactlyWhereItWas() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = TestGraphs.Demo();
        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();
        window.CaptureRenderedFrame();

        canvas.Transform.Zoom = 1.75;
        canvas.Transform.OffsetX = 123.5;
        canvas.Transform.OffsetY = -47.25;

        string path = Path.Combine(Path.GetTempPath(), $"spark-export-{Guid.NewGuid():N}.png");

        try
        {
            canvas.ExportImage(path, 640, 480);

            Assert.Equal(1.75, canvas.Transform.Zoom);
            Assert.Equal(123.5, canvas.Transform.OffsetX);
            Assert.Equal(-47.25, canvas.Transform.OffsetY);
        }
        finally
        {
            File.Delete(path);
            window.Close();
        }
    });

    /// <summary>A path that names nothing is refused rather than written somewhere surprising.</summary>
    [Fact]
    public void ABlankPathIsRefused() => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = TestGraphs.Demo() };

        Assert.Throws<ArgumentException>(() => canvas.ExportImage("   ", 640, 480));
    });

    /// <summary>
    /// The switches that make the export reachable without two dialogs, which is how it was looked
    /// at at all.
    /// </summary>
    [Fact]
    public void TheExportSwitchesParse()
    {
        StartupOptions options = StartupOptions.Parse(
            ["--export-graph", "out.png", "--export-size", "2400x1500"]);

        Assert.True(options.IsGraphExport);
        Assert.Equal("out.png", options.ExportGraph);
        Assert.Equal(2400, options.ExportWidth);
        Assert.Equal(1500, options.ExportHeight);

        // And a size that is not a size leaves the defaults alone rather than half-applying.
        StartupOptions bad = StartupOptions.Parse(["--export-graph", "out.png", "--export-size", "wide"]);

        Assert.Equal(0, bad.ExportWidth);
        Assert.Equal(0, bad.ExportHeight);
    }
}

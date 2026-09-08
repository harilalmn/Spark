using System;
using System.Buffers.Binary;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Spark.UI;
using Spark.UI.Controls;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// `E9-T15` — exporting what the viewport is showing, as a picture and as a solid.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked for by the client</b>: <i>provide option to export the Viewport geometry into ACIS
/// solid and png (with option to set resolution).</i> **The solid is STEP, not ACIS**, and the
/// substitution was agreed rather than assumed: nothing in this repository can write ACIS and
/// OpenCascade has no ACIS writer, so the client was asked and chose STEP — which is what every
/// ACIS-based application reads.
/// </para>
/// <para>
/// <b>The image half is assertable here where the graph's was not</b>
/// ([N124](../../docs/NOTES.md)). The graph export goes through Avalonia's
/// <c>RenderTargetBitmap</c>, and the headless platform draws nothing into one; the viewport
/// export goes through Spark's own software rasteriser and Spark's own PNG encoder, neither of
/// which is the platform's — so the bytes are real, and the file's own header can be read back.
/// </para>
/// </remarks>
public sealed class ViewportExportTests
{
    /// <summary>The exported image is the size that was asked for, read out of the PNG itself.</summary>
    [Fact]
    public void TheExportedViewportIsTheSizeThatWasAskedFor() => HeadlessSession.Run(() =>
    {
        ViewportControl viewport = new();
        string path = Path.Combine(Path.GetTempPath(), $"spark-viewport-{Guid.NewGuid():N}.png");

        try
        {
            viewport.ExportImage(path, 1600, 1000);

            (int width, int height) = PngSize(path);

            Assert.Equal(1600, width);
            Assert.Equal(1000, height);
        }
        finally
        {
            File.Delete(path);
        }
    });

    /// <summary>
    /// <b>A silly size is clamped rather than attempted.</b> 100,000 pixels square is forty
    /// gigabytes, and a typo in a text box should not be an out-of-memory.
    /// </summary>
    [Fact]
    public void ASillySizeIsClamped() => HeadlessSession.Run(() =>
    {
        ViewportControl viewport = new();
        string path = Path.Combine(Path.GetTempPath(), $"spark-viewport-{Guid.NewGuid():N}.png");

        try
        {
            viewport.ExportImage(path, 40, 0);

            (int width, int height) = PngSize(path);

            Assert.Equal(40, width);
            Assert.Equal(Spark.UI.Canvas.CanvasExport.MinimumPixels, height);
        }
        finally
        {
            File.Delete(path);
        }
    });

    /// <summary>
    /// <b>The user's camera is copied, not borrowed.</b> Rendering sets a camera's viewport size to
    /// the image, and a camera framed for one aspect ratio and drawn at another crops silently — so
    /// exporting at 1600×1000 must not leave the on-screen view framed for 1600×1000.
    /// </summary>
    [Fact]
    public void ExportingDoesNotRefameTheUsersCamera() => HeadlessSession.Run(() =>
    {
        ViewportControl viewport = new();

        viewport.Camera.SetViewportSize(900, 400);

        string path = Path.Combine(Path.GetTempPath(), $"spark-viewport-{Guid.NewGuid():N}.png");

        try
        {
            viewport.ExportImage(path, 1600, 1000);

            Assert.Equal(900, viewport.Camera.ViewportWidth);
            Assert.Equal(400, viewport.Camera.ViewportHeight);
        }
        finally
        {
            File.Delete(path);
        }
    });

    /// <summary>A path that names nothing is refused rather than written somewhere surprising.</summary>
    [Fact]
    public void ABlankPathIsRefused() => HeadlessSession.Run(() =>
    {
        ViewportControl viewport = new();

        Assert.Throws<ArgumentException>(() => viewport.ExportImage(" ", 640, 480));
    });

    /// <summary>
    /// <b>A graph that has not been run has nothing to export, and says so.</b> An empty file
    /// would be the worse answer: the user would find out at the other end, in somebody else's
    /// application.
    /// </summary>
    [Fact]
    public void ExportingSolidsFromAnEmptySceneRefusesWithAReason()
    {
        MainWindowViewModel model = new();

        string path = Path.Combine(Path.GetTempPath(), $"spark-solids-{Guid.NewGuid():N}.step");

        Assert.Empty(model.SolidsInScene());
        Assert.False(model.TryExportSolids(path));
        Assert.False(File.Exists(path));
        Assert.Contains("no solids", model.DiagnosticsText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A blank path is a caller's mistake here too.</summary>
    [Fact]
    public void ExportingSolidsToABlankPathIsRefused()
    {
        MainWindowViewModel model = new();

        Assert.Throws<ArgumentException>(() => model.TryExportSolids("  "));
    }

    /// <summary>The switches that make both exports reachable without a dialog.</summary>
    [Fact]
    public void TheViewportExportSwitchesParse()
    {
        StartupOptions options = StartupOptions.Parse(
            ["--export-viewport", "v.png", "--export-solids", "m.step", "--export-size", "800x600"]);

        Assert.True(options.IsGraphExport);
        Assert.Equal("v.png", options.ExportViewport);
        Assert.Equal("m.step", options.ExportSolids);
        Assert.Equal(800, options.ExportWidth);
        Assert.Equal(600, options.ExportHeight);
    }

    /// <summary>The width and height a PNG's IHDR chunk declares.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its pixel dimensions.</returns>
    /// <remarks>
    /// <b>Sixteen bytes in, big-endian, and it is the file's own answer.</b> Decoding the image
    /// with the same encoder that wrote it would prove the encoder agrees with itself.
    /// </remarks>
    private static (int Width, int Height) PngSize(string path)
    {
        byte[] header = new byte[24];

        using (FileStream file = File.OpenRead(path))
        {
            Assert.Equal(header.Length, file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false));
        }

        // The eight-byte signature, then a length and the four characters `IHDR`.
        Assert.Equal(0x89, header[0]);
        Assert.Equal((byte)'P', header[1]);
        Assert.Equal((byte)'I', header[12]);
        Assert.Equal((byte)'H', header[13]);

        return (
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
    }
}

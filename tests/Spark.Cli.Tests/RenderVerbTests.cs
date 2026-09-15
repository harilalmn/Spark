using System;
using System.IO;
using Spark.Engine;
using Spark.Viewport.Software;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark render</c> (<c>E12-T5</c>): a picture of a graph, drawn with no window, no graphics
/// device and no display connection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Determinism is the claim worth testing and it is the reason the verb exists.</b> <c>E9-T5</c>
/// gives the software rasteriser three jobs and this is the third — GPU output varies by driver and
/// by vendor, where this path does not, so the same graph gives the same bytes on a build agent
/// with nothing attached to it. A render that quietly used a GPU when one was present would be a
/// check that passed differently on every machine, and no assertion about a single run would see
/// it.
/// </para>
/// <para>
/// <b>What is not tested here is the picture.</b> Whether the triangles are in the right places is
/// <c>Spark.Viewport.Tests</c>'s question and it has its own goldens; these tests cover the verb —
/// the arguments it takes, the files it writes, and the three exit codes it distinguishes.
/// </para>
/// </remarks>
public sealed class RenderVerbTests : IDisposable
{
    private readonly string _root;

    public RenderVerbTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spark-cli-render", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
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

    /// <summary>A graph with geometry in it renders, and the file is a PNG of the size asked for.</summary>
    [Fact]
    public void AGraphWithGeometryRendersToAPngOfTheRequestedSize()
    {
        string graph = SaveGraph(WithALine);
        string png = Path.Combine(_root, "line.png");
        StringWriter report = new();
        StringWriter error = new();

        Assert.Equal(
            0,
            Program.Render(["--open", graph, "--out", png, "--width", "320", "--height", "240"], report, error));

        // Decoded rather than measured on disk: a file of the right length that is not a PNG would
        // pass a size check, and the whole output of this verb is "a picture somebody can open".
        byte[] pixels = PngImage.Decode(File.ReadAllBytes(png), out int width, out int height);

        Assert.Equal(320, width);
        Assert.Equal(240, height);
        Assert.Equal(320 * 240 * 4, pixels.Length);
        Assert.Contains("320x240", report.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The claim the verb exists for.</b> Two runs of one graph produce byte-identical files.
    /// A visual regression check is worthless the moment this stops being true, and it would stop
    /// being true silently — the pictures would still look right.
    /// </summary>
    [Fact]
    public void TwoRendersOfOneGraphAreByteIdentical()
    {
        string graph = SaveGraph(WithALine);
        string first = Path.Combine(_root, "first.png");
        string second = Path.Combine(_root, "second.png");

        Assert.Equal(0, Program.Render(["--open", graph, "--out", first], new StringWriter(), new StringWriter()));
        Assert.Equal(0, Program.Render(["--open", graph, "--out", second], new StringWriter(), new StringWriter()));

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    /// <summary>
    /// <b>Exit 2: the graph ran and drew nothing.</b> A graph of pure arithmetic is not an error —
    /// it is a graph with no geometry — and a CI job that accepted the empty viewport without
    /// complaint is the vacuously-green failure this verb exists to prevent. The file is still
    /// written, because looking at it is how somebody finds out why.
    /// </summary>
    [Fact]
    public void AGraphThatDrawsNothingExitsTwoAndStillWritesTheFile()
    {
        string graph = SaveGraph(graph =>
        {
            NodeInstance number = graph.AddNode(Library.ByName("Math.Add"));
            graph.SetLiteral(number.Id, 0, 2.0);
            graph.SetLiteral(number.Id, 1, 2.0);
        });

        string png = Path.Combine(_root, "empty.png");
        StringWriter error = new();

        Assert.Equal(2, Program.Render(["--open", graph, "--out", png], new StringWriter(), error));

        Assert.True(File.Exists(png));
        Assert.Contains("nothing to draw", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--no-grid</c> changes the picture, which is the only way to tell it was honoured. The
    /// grid is on by default because this render stands in for the viewport.
    /// </summary>
    [Fact]
    public void NoGridProducesADifferentPictureFromTheDefault()
    {
        string graph = SaveGraph(WithALine);
        string furnished = Path.Combine(_root, "grid.png");
        string bare = Path.Combine(_root, "bare.png");

        Assert.Equal(0, Program.Render(["--open", graph, "--out", furnished], new StringWriter(), new StringWriter()));
        Assert.Equal(
            0,
            Program.Render(["--open", graph, "--out", bare, "--no-grid"], new StringWriter(), new StringWriter()));

        Assert.NotEqual(File.ReadAllBytes(furnished), File.ReadAllBytes(bare));
    }

    /// <summary>
    /// A name that is not a PNG is refused rather than written, because the one thing worse than
    /// no file is a PNG called <c>picture.jpg</c>.
    /// </summary>
    [Fact]
    public void ANameThatIsNotAPngIsRefusedAndNothingIsWritten()
    {
        string graph = SaveGraph(WithALine);
        string jpg = Path.Combine(_root, "line.jpg");
        StringWriter error = new();

        Assert.Equal(1, Program.Render(["--open", graph, "--out", jpg], new StringWriter(), error));

        Assert.False(File.Exists(jpg));
        Assert.Contains(".png", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A size that would make a meaningless image, or a process the operating system kills, is
    /// refused with the range named.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("100000")]
    [InlineData("wide")]
    public void ASizeOutsideTheAllowedRangeIsRefused(string width)
    {
        string graph = SaveGraph(WithALine);
        string png = Path.Combine(_root, "unwritten.png");
        StringWriter error = new();

        Assert.Equal(1, Program.Render(["--open", graph, "--out", png, "--width", width], new StringWriter(), error));

        Assert.False(File.Exists(png));
        Assert.Contains("--width", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Both paths are required, and saying which is missing costs nothing.</summary>
    [Fact]
    public void RenderNeedsBothPaths()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Render(["--out", Path.Combine(_root, "x.png")], new StringWriter(), error));
        Assert.Contains("--open", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An option nobody recognises is named rather than ignored.</summary>
    [Fact]
    public void AnUnrecognisedOptionIsNamed()
    {
        StringWriter error = new();

        Assert.Equal(1, Program.Render(["--zoom", "2"], new StringWriter(), error));
        Assert.Contains("--zoom", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A line between two points, built from wired nodes rather than from literals.
    /// </summary>
    /// <remarks>
    /// <b>A <c>.spark</c> file cannot carry a <c>Point3d</c> literal</b> — <c>GraphDocument.Capture</c>
    /// refuses one by name — so a graph that is saved and reopened, which is what this verb does,
    /// has to make its points with nodes. The literals here are the six doubles.
    /// </remarks>
    /// <param name="graph">The graph being built.</param>
    private static void WithALine(Graph graph)
    {
        NodeInstance start = Corner(graph, 0, 0, 0);
        NodeInstance end = Corner(graph, 10, 10, 10);

        NodeInstance line = graph.AddNode(Library.ByName("Line.FromStartPointEndPoint"));
        graph.TryConnect(start.Id, 0, line.Id, 0);
        graph.TryConnect(end.Id, 0, line.Id, 1);
    }

    private static NodeInstance Corner(Graph graph, double x, double y, double z)
    {
        NodeInstance point = graph.AddNode(Library.ByName("Point.FromCoordinates"));
        graph.SetLiteral(point.Id, 0, x);
        graph.SetLiteral(point.Id, 1, y);
        graph.SetLiteral(point.Id, 2, z);

        return point;
    }

    private string SaveGraph(Action<Graph> build)
    {
        Graph graph = new();
        build(graph);

        string path = Path.Combine(_root, Guid.NewGuid().ToString("n") + ".spark");
        File.WriteAllText(path, SparkFile.Write(GraphDocument.Capture(graph)));

        return path;
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }
}

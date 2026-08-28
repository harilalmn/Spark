using System;
using System.Globalization;

namespace Spark.UI;

/// <summary>
/// What the entry point learned from the command line. Kept in <c>Spark.UI</c> rather than in
/// <c>Spark.Desktop</c> so the window can read it without the executable having to reach into the
/// view layer to configure it.
/// </summary>
/// <param name="SyntheticNodeCount">
/// How many synthetic nodes to load instead of the demo graph, or zero for the demo graph.
/// </param>
/// <param name="BenchmarkFrames">
/// How many frames to measure before printing a summary and exiting, or zero to run normally.
/// </param>
/// <param name="ScreenshotPrefix">
/// A file path prefix to write <c>-shell.png</c> and <c>-viewport.png</c> to before exiting, or
/// null to run normally.
/// </param>
/// <param name="Graph">
/// Which seeded graph to open: <c>demo</c> for the point grid, <c>curves</c> for the curve demo.
/// </param>
/// <param name="OpenPath">
/// A `.spark` file to open instead of a seeded graph, or null.
/// </param>
/// <param name="BenchmarkZoom">
/// A zoom to pin the benchmark at, or zero to sweep. Pinning is what separates "how much does the
/// graph cost" from "how much does what is on screen cost", which is the claim ADR-0013 actually
/// makes.
/// </param>
public readonly record struct StartupOptions(
    int SyntheticNodeCount,
    int BenchmarkFrames,
    double BenchmarkZoom,
    string? ScreenshotPrefix,
    string? Graph,
    string? OpenPath)
{
    /// <summary>The ordinary interactive start: the demo graph, no benchmark.</summary>
    public static StartupOptions Default => new(0, 0, 0, null, null, null);

    /// <summary>True when the window should open a file rather than a seeded graph.</summary>
    public bool IsFileOpen => !string.IsNullOrWhiteSpace(OpenPath);

    /// <summary>True when the window should open the curve demo rather than the point grid.</summary>
    public bool IsCurveGraph =>
        string.Equals(Graph, "curves", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the window should run the canvas benchmark and then exit.</summary>
    public bool IsBenchmark => BenchmarkFrames > 0;

    /// <summary>True when the window should capture images and then exit.</summary>
    public bool IsScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPrefix);

    /// <summary>
    /// True when a splash window should be shown while the shell is built.
    /// </summary>
    /// <remarks>
    /// <b>Never during a measurement mode</b>, and the two reasons are different. A splash in
    /// front of <c>--screenshot</c> is a second window in the capture, so the picture stops being
    /// of the thing under test. A splash in front of <c>--canvas-benchmark</c> is a second window
    /// being composited while the compositor is the thing being measured — it would not merely add
    /// noise, it would bias the number the run exists to produce.
    /// </remarks>
    public bool ShowSplash => !IsBenchmark && !IsScreenshot;

    /// <summary>
    /// Parses the command line.
    /// </summary>
    /// <param name="args">The raw arguments.</param>
    /// <returns>The options, defaulted where nothing was said.</returns>
    /// <remarks>
    /// <para>
    /// Two switches, both aimed at settling ADR-0013's bet rather than at users:
    /// </para>
    /// <list type="bullet">
    /// <item><c>--nodes N</c> loads N synthetic nodes instead of the demo graph.</item>
    /// <item>
    /// <c>--canvas-benchmark [frames]</c> drives a fixed pan-and-zoom cycle for that many frames,
    /// prints the frame-time distribution and exits. It implies <c>--nodes 2000</c> unless a count
    /// is given, because 2,000 is the number the ADR names.
    /// </item>
    /// <item><c>--zoom Z</c> pins the benchmark at one zoom instead of sweeping.</item>
    /// <item>
    /// <c>--screenshot PREFIX</c> writes <c>PREFIX-shell.png</c> and <c>PREFIX-viewport.png</c> and
    /// exits. The viewport image is a GPU read-back rather than a window grab, so it works over a
    /// locked session and in CI, where a screen capture returns the lock screen.
    /// </item>
    /// </list>
    /// </remarks>
    public static StartupOptions Parse(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return Default;
        }

        int nodes = 0;
        int frames = 0;
        double zoom = 0;
        string? screenshot = null;
        string? graph = null;
        string? open = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--nodes" when i + 1 < args.Length:
                    nodes = ParseCount(args[++i], nodes);
                    break;

                case "--canvas-benchmark":
                    frames = 600;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        frames = ParseCount(args[++i], frames);
                    }

                    break;

                case "--zoom" when i + 1 < args.Length:
                    zoom = double.TryParse(
                        args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                        ? parsed
                        : 0;
                    break;

                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;

                case "--graph" when i + 1 < args.Length:
                    graph = args[++i];
                    break;

                case "--open" when i + 1 < args.Length:
                    open = args[++i];
                    break;

                default:
                    break;
            }
        }

        if (frames > 0 && nodes == 0)
        {
            nodes = 2000;
        }

        return new StartupOptions(nodes, frames, zoom, screenshot, graph, open);
    }

    private static int ParseCount(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : fallback;
}

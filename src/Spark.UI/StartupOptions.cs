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
/// Which seeded graph to open: <c>demo</c> for the point grid, <c>curves</c> for the curve demo,
/// <c>surfaces</c> for the surface demo, <c>solids</c> for the exact solid demo.
/// </param>
/// <param name="OpenPath">
/// A `.spark` file to open instead of a seeded graph, or null.
/// </param>
/// <param name="NoScript">
/// True when scripting is refused for the whole session (`E6-T16`). A graph containing a code
/// block then fails to open, naming the node, rather than opening with the node quietly missing —
/// a Spark graph is executable code, and dropping the executable parts would be worse than
/// refusing.
/// </param>
/// <param name="ForceSoftwareRenderer">
/// True when the viewport must use the software rasteriser and never ask for an OpenGL context
/// (<c>--software-renderer</c>). The fallback happens on its own when GL fails; this makes it
/// reachable on purpose, which is what a support conversation needs and what lets the fallback be
/// exercised rather than merely hoped for (`E9-T5`, `E9-T11`).
/// </param>
/// <param name="HelpTopic">
/// The topic to open the help window on at startup (<c>--help-window [topic]</c>), or null to
/// leave it closed. Aimed at the
/// screenshot path rather than at users: it is how the help renderer gets photographed and
/// therefore checked, since a control that lays out wrongly still passes every test that only
/// asks it which topic it is showing.
/// </param>
/// <param name="OpenAbout">
/// True when the About box should open at startup (<c>--about-window</c>). Like
/// <c>--help-window</c>, this exists so the dialog can be photographed and therefore checked; a
/// licence notice that lays out wrongly still satisfies every test that only reads its text.
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
    string? OpenPath,
    bool NoScript = false,
    bool ForceSoftwareRenderer = false,
    string? HelpTopic = null,
    bool OpenAbout = false)
{
    /// <summary>The ordinary interactive start: the demo graph, no benchmark.</summary>
    public static StartupOptions Default => new(0, 0, 0, null, null, null);

    /// <summary>True when the window should open a file rather than a seeded graph.</summary>
    public bool IsFileOpen => !string.IsNullOrWhiteSpace(OpenPath);

    /// <summary>True when the window should open the curve demo rather than the point grid.</summary>
    public bool IsCurveGraph =>
        string.Equals(Graph, "curves", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the window should open the surface demo.</summary>
    public bool IsSurfaceGraph =>
        string.Equals(Graph, "surfaces", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the window should open the solid demo.</summary>
    public bool IsSolidGraph =>
        string.Equals(Graph, "solids", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the window should run the canvas benchmark and then exit.</summary>
    public bool IsBenchmark => BenchmarkFrames > 0;

    /// <summary>True when the help window should open at startup.</summary>
    public bool OpensHelp => !string.IsNullOrWhiteSpace(HelpTopic);

    /// <summary>True when the window should capture images and then exit.</summary>
    public bool IsScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPrefix);

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
    /// <item>
    /// <c>--no-script</c> refuses scripting for the session, which is the one switch here that is
    /// aimed at users rather than at a bet (`E6-T16`).
    /// </item>
    /// <item>
    /// <c>--software-renderer</c> draws the viewport on the CPU and never requests an OpenGL
    /// context. Also aimed at users: it is the answer to "the viewport is black on my virtual
    /// machine", and it is how the fallback gets exercised deliberately rather than only when
    /// something has already gone wrong.
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
        bool noScript = false;
        bool software = false;
        string? helpTopic = null;
        bool aboutWindow = false;

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

                case "--no-script":
                    noScript = true;
                    break;

                case "--software-renderer":
                case "--software":
                    software = true;
                    break;

                case "--about-window":
                    aboutWindow = true;
                    break;

                case "--help-window":
                    helpTopic = "nodes.index";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        helpTopic = args[++i];
                    }

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

        return new StartupOptions(nodes, frames, zoom, screenshot, graph, open, noScript, software, helpTopic, aboutWindow);
    }

    private static int ParseCount(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : fallback;
}

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
/// <param name="NoUpdateCheck">
/// True when the update check is refused for this session (<c>--no-update-check</c>). The check
/// is the one outbound request Spark makes on its own behalf, and a switch that turns it off
/// without writing anything down is what a locked-down environment, a test and a screenshot run
/// all need. The persisted preference is <c>UpdatePreference</c>; this overrides it downwards
/// only, because a flag that could turn a setting back <i>on</i> would make the setting a
/// suggestion.
/// </param>
/// <param name="CodeBlock">
/// Source to pose a code block with, so that the editor and its popups can be photographed
/// (`E6-T22`). A block is placed, selected and filled with this text, and both popups are asked
/// for at the caret — which is the only way to see a completion list and a signature in a
/// screenshot, since neither exists until somebody types.
/// </param>
/// <param name="CodeBlockCommand">
/// A Selection command to run on the posed code block instead of opening its popups (`E6-T24`),
/// named as the context menu's tags name them — <c>SelectAllOccurrences</c>, <c>AddCaretBelow</c>.
/// Multiple carets, like a completion list, exist only after somebody has pressed something.
/// </param>
/// <param name="CodeBlockInNode">
/// Whether the posed code block's editor opens <i>on the node</i> rather than in the properties
/// pane (`E8-T39`). The in-node editor exists only while somebody is typing into a block, so
/// like the completion list it cannot appear in a screenshot unless the application is asked to
/// put it there.
/// </param>
/// <param name="UpdateBadge">
/// A version to show in the update badge at startup (<c>--update-badge 9.9.9</c>), or null.
/// Aimed at the screenshot path for the reason <c>--about-window</c> and <c>--help-window</c> are:
/// the badge only appears when a newer release genuinely exists, so without this the one control
/// in the shell whose whole job is to be noticed could never be photographed, and a badge that
/// laid out wrongly would still pass every test that only asks whether it is visible. It sets the
/// label directly and makes no request.
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
/// <param name="PackageSource">
/// The package feed to use instead of nuget.org (<c>--package-source</c>), or null. A NuGet v3
/// service index, a folder, or a network share: <c>E7</c> states that the loader must not know
/// which, and an organisation running an internal feed has no other way to say so.
/// </param>
/// <param name="PreparePackage">
/// A package id to select and prepare once the startup search finishes
/// (<c>--package-prepare</c>), or null. Nothing is installed: preparing is what puts the
/// disclosure on screen, and this exists for the same reason <c>--collapse</c> does — the gesture
/// is a button, a button needs a click, and a click is the one thing a headless run cannot do.
/// The disclosure is the screen on which a user decides to run somebody else's code, so it is the
/// one that most needs to be looked at rather than merely asserted about.
/// </param>
/// <param name="ReferenceAssembly">
/// A local assembly to choose once the package manager opens (<c>--reference</c>), or null.
/// Nothing is referenced: choosing is what puts the prompt on screen, and this exists for the same
/// reason <c>--package-prepare</c> does. The prompt is where a user decides to compile against
/// somebody else's code, so it is one that should be looked at rather than only asserted about.
/// </param>
/// <param name="PackageQuery">
/// The feed search to run when the package manager opens at startup
/// (<c>--packages-window [query]</c>), empty to open it without searching, or null not to open it
/// at all. It exists for the same reason as <c>--about-window</c>: a window whose install
/// disclosure lays out wrongly still passes every test that only reads the text it would show.
/// </param>
/// <param name="FreezeFirst">
/// How many of the graph's leading nodes to freeze at startup (<c>--freeze N</c>), or zero. Aimed
/// at the screenshot path for the same reason <c>--collapse</c> is: the gesture is a button, a
/// button needs a click, and a click is the one thing a headless run cannot do.
/// </param>
/// <param name="CollapseFirst">
/// How many of the graph's leading nodes to select and collapse into a custom node at startup
/// (<c>--collapse N</c>), or zero. Aimed at the screenshot path: the gesture is a button, a button
/// needs a click, and a click is the one thing a headless run cannot do.
/// </param>
/// <param name="SelectFirst">
/// How many of the graph's leading nodes to select at startup (<c>--select N</c>), or zero. Aimed
/// at the screenshot path for the reason <c>--freeze</c> and <c>--collapse</c> are: what a
/// selection <i>looks</i> like — the accent ring and the orange halo of <c>E8-T28</c> — cannot be
/// asserted in a headless test, because headless drawing produces no pixels. It can only be looked
/// at, and looking at it needs a click that a headless run cannot make.
/// </param>
/// <param name="LibrarySearch">
/// A query to type into the library panel's search box at startup (<c>--library "text"</c>), or
/// null. Aimed at the screenshot path for the reason <c>--select</c> is: the library's groups start
/// closed, so a capture of the running application shows ten headings and none of the structure
/// underneath them.
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
    bool OpenAbout = false,
    string? PackageSource = null,
    string? PackageQuery = null,
    string? PreparePackage = null,
    string? ReferenceAssembly = null,
    int FreezeFirst = 0,
    int CollapseFirst = 0,
    int SelectFirst = 0,
    string? LibrarySearch = null,
    bool NoUpdateCheck = false,
    string? UpdateBadge = null,
    string? CodeBlock = null,
    string? CodeBlockCommand = null,
    bool CodeBlockInNode = false)
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

    /// <summary>True when the package manager should open at startup.</summary>
    public bool OpensPackages => PackageQuery is not null || ReferenceAssembly is not null;

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
        bool noUpdateCheck = false;
        string? updateBadge = null;
        string? codeBlock = null;
        string? codeBlockCommand = null;
        bool codeBlockInNode = false;
        bool software = false;
        string? helpTopic = null;
        bool aboutWindow = false;
        string? packageQuery = null;
        string? packageSource = null;
        string? preparePackage = null;
        string? referenceAssembly = null;
        int freezeFirst = 0;
        int collapseFirst = 0;
        int selectFirst = 0;
        string? librarySearch = null;

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

                case "--no-update-check":
                    noUpdateCheck = true;
                    break;

                case "--code-block" when i + 1 < args.Length:
                    codeBlock = args[++i];
                    break;

                case "--code-block-command" when i + 1 < args.Length:
                    codeBlockCommand = args[++i];
                    break;

                case "--code-block-in-node":
                    codeBlockInNode = true;
                    break;

                case "--update-badge" when i + 1 < args.Length:
                    updateBadge = args[++i];
                    break;

                case "--software-renderer":
                case "--software":
                    software = true;
                    break;

                case "--about-window":
                    aboutWindow = true;
                    break;

                case "--reference" when i + 1 < args.Length:
                    referenceAssembly = args[++i];
                    break;

                case "--package-prepare" when i + 1 < args.Length:
                    preparePackage = args[++i];
                    break;

                case "--package-source" when i + 1 < args.Length:
                    packageSource = args[++i];
                    break;

                case "--packages-window":
                    packageQuery = string.Empty;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        packageQuery = args[++i];
                    }

                    break;

                case "--freeze" when i + 1 < args.Length:
                    freezeFirst = ParseCount(args[++i], 0);
                    break;

                case "--collapse" when i + 1 < args.Length:
                    collapseFirst = ParseCount(args[++i], 0);
                    break;

                case "--select" when i + 1 < args.Length:
                    selectFirst = ParseCount(args[++i], 0);
                    break;

                case "--library" when i + 1 < args.Length:
                    librarySearch = args[++i];
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

        return new StartupOptions(nodes, frames, zoom, screenshot, graph, open, noScript, software, helpTopic, aboutWindow, packageSource, packageQuery, preparePackage, referenceAssembly, freezeFirst, collapseFirst, selectFirst, librarySearch, noUpdateCheck, updateBadge, codeBlock, codeBlockCommand, codeBlockInNode);
    }

    private static int ParseCount(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : fallback;
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NuGet.Versioning;
using Spark.Api;
using Spark.Api.Help;
using Spark.Engine;
using Spark.Geometry;
using Spark.Geometry.Io;
using Spark.Host;
using Spark.Packages;
using Spark.Viewport;
using Spark.Viewport.Software;

namespace Spark.Cli;

/// <summary>
/// Entry point for the <c>spark</c> command line.
/// </summary>
/// <remarks>
/// <para>
/// <c>spark export</c> is M1's demoable: open a `.spark` graph, evaluate it with no window
/// anywhere, and write the geometry as an OBJ file that a third-party viewer opens. It is the
/// first proof that a graph is a document and an evaluation is a computation, rather than
/// something that only exists inside the desktop application.
/// </para>
/// <para>
/// <c>spark run</c> is the same claim without the geometry: open a graph, evaluate it with no
/// window, and say what it produced. It reports through <see cref="ValueText"/>, which is also
/// what the canvas and the properties pane render with — <c>E12-T5</c> requires the command line
/// to produce output identical to the desktop application's, and one shared implementation is the
/// only way to keep a requirement like that true rather than merely asserted.
/// </para>
/// <para>
/// <c>spark check</c> is <c>run</c> with the printing taken away: it opens a graph, evaluates it
/// with no window, and says nothing at all unless something is wrong. It exists to be put in a
/// build script, which is why it is silent on success and why its exit code rather than its
/// output is the answer.
/// </para>
/// <para>
/// <c>spark pack</c> zips a graph and the package folder beside it into a <c>.sparkz</c> for sharing
/// (`E3-T20`), and <c>run</c>, <c>check</c> and <c>export</c> open one wherever they take a graph.
/// </para>
/// <para>
/// <c>spark graph</c> describes a graph file without binding it — the format, the counts, the
/// definitions it names against the ones this build holds, and the packages it records against the
/// folder beside it. It is the only verb that still works on a graph this build cannot open, which
/// is the moment somebody reaches for it.
/// </para>
/// <para>
/// <c>spark docs</c> is the help the application shows under F1, from a terminal: it lists the
/// topics, prints one as Markdown, or writes them all out as files. It generates nothing — the node
/// reference is produced from the live library by <c>NodeReference</c> and the concept topics are
/// files, and <c>HelpComposition</c> assembles the same library for both hosts. With it, `E12-T5`'s
/// seven verbs all exist.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        // The value renderings carry '·' and '…', and Windows consoles default to a code page
        // that cannot represent either — including when the output is redirected to a file, which
        // is the case that matters, because the whole point of `spark run` is output somebody can
        // diff. Set once, before anything is written.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console attached — a redirected or service context. The stream encoding is then
            // already whatever the host chose, and failing to start over it would be absurd.
        }

        // ADR-0020: the provider is installed if it is there, and its absence is silent here.
        // `spark run` on a graph with no solids in it must not print a warning about a kernel it
        // never needed, and a graph that does need one gets a diagnostic on the node itself.
        _ = Spark.Geometry.Occt.OcctKernel.TryInstall(out _);

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            Usage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "run" => Run(args.AsSpan(1), Console.Out, Console.Error),
                "check" => Check(args.AsSpan(1), Console.Error),
                "export" => Export(args.AsSpan(1), Console.Out, Console.Error),
                "render" => Render(args.AsSpan(1), Console.Out, Console.Error),
                "pkg" => Pkg(args.AsSpan(1), Console.Out, Console.Error),
                "graph" => Graph(args.AsSpan(1), Console.Out, Console.Error),
                "docs" => Docs(args.AsSpan(1), Console.Out, Console.Error),
                "pack" => Pack(args.AsSpan(1), Console.Out, Console.Error),
                "--version" => Version(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception failure) when (failure is IOException or SparkFileException or ArgumentException)
        {
            // A bad path, a corrupt file or a graph this build cannot bind is the user's problem
            // to fix and needs one line, not a stack trace. Anything else is our problem and the
            // stack trace is the useful part, so it is deliberately not caught here.
            Console.Error.WriteLine($"spark: {failure.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Opens a graph, evaluates it with no window anywhere, and reports what it produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Watch nodes are what it prints by default.</b> A graph of two thousand nodes has two
    /// thousand values and almost none of them is what the person running it wanted to see; a
    /// watch is the user saying <i>this one</i>, and it is already the thing the canvas pins a
    /// bubble to. <c>--all</c> is there for a diff, where every value is exactly what you want.
    /// </para>
    /// <para>
    /// Diagnostics go to standard error and values to standard output, so that
    /// <c>spark run g.spark &gt; values.txt</c> captures the answer and still shows the problems.
    /// </para>
    /// <para>
    /// <b>It takes its two streams rather than reaching for the console</b>, for <see cref="Check"/>'s
    /// reason: so that what it printed and what it returned can both be asserted (`E7-T25`).
    /// </para>
    /// </remarks>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="output">Where values go. <see cref="Console.Out"/> in the product.</param>
    /// <param name="error">Where diagnostics go. <see cref="Console.Error"/> in the product.</param>
    /// <param name="trust">
    /// The record of package-folder assemblies the user has agreed to, or null for the one the
    /// desktop application keeps. A test passes its own so that the user's is never touched.
    /// </param>
    /// <returns>Zero when nothing errored, one otherwise.</returns>
    internal static int Run(
        ReadOnlySpan<string> args, TextWriter output, TextWriter error, PackageTrustStore? trust = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? input = null;
        bool all = false;
        bool scripting = true;
        bool once = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Length:
                    input = args[++i];
                    break;

                case "--all":
                    all = true;
                    break;

                case "--no-script":
                    scripting = false;
                    break;

                case "--trust-packages":
                    once = true;
                    break;

                default:
                    // A bare path is the ordinary way to name a file to a command line, and
                    // requiring --open for the argument the verb is about would be ceremony.
                    if (input is null && !args[i].StartsWith('-'))
                    {
                        input = args[i];
                        break;
                    }

                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null)
        {
            error.WriteLine("spark: run needs a graph to run. Try: spark run graph.spark");
            return 1;
        }

        using SparkSession session = new();

        // `E3-T20`: a bundle is opened into a temporary folder, and the graph inside it is what is read.
        using OpenedInput opened = OpenedInput.From(input);
        GraphDocument document = SparkFile.Read(File.ReadAllText(opened.Graph));

        // `E6-T16`. **A graph is executable code, and `spark run` is the one place that runs it
        // without a person watching** - in a build, on a schedule, from a hook. So the flag is
        // here, and it refuses rather than dropping the executable parts: a graph that silently
        // ran with its code blocks missing would produce a wrong answer quietly, which is worse
        // than an error. And a document with no scripts in it never asks for a factory at all,
        // which is what keeps Roslyn out of a `spark run` that has no code blocks (`E6-T14`).
        if (!scripting && document.HasScripts)
        {
            error.WriteLine(
                "spark: this graph contains a code block and --no-script was given, so it was not run.");

            return 1;
        }

        IScriptNodeFactory? scripts = scripting && document.HasScripts
            ? session.EnableScripting()
            : null;

        // `E7-T25`: what the blocks may compile against is settled before they are built.
        if (scripts is not null
            && !AdmitPackages(opened.Graph, document, session, trust, once, "spark: ", "run", error, out _))
        {
            return 1;
        }

        Graph graph = document.Restore(session.Library, scripts);

        EvaluationContext context = new(default, new SequentialEvaluationScheduler());
        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, CancellationToken.None);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            error.WriteLine($"spark: {diagnostic.Code}: {diagnostic.Message}");
        }

        int reported = 0;

        // The document's order, not the graph's. `graph.Nodes()` walks a dictionary, so two runs
        // of the same file could print the same values in a different order — and the reason to
        // print values at all is so that two runs can be compared. The document is already sorted
        // by identity, for exactly this reason.
        foreach (GraphDocumentNode documented in document.Nodes)
        {
            NodeInstance node = graph.Node(documented.Id);

            if (!all && !node.Definition.ShowsValue)
            {
                continue;
            }

            object? value = result.Value(node.Id);
            if (ValueText.Summary(value) is not { } summary)
            {
                continue;
            }

            reported++;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{node.Definition.DisplayName}  {ValueText.Shape(value)}  {summary}"));
        }

        if (reported == 0 && !all)
        {
            // Not an error, and not silence either: a graph with no watches in it ran perfectly
            // well and simply said nothing, which looks identical to a graph that did nothing.
            error.WriteLine(
                "spark: no watch nodes in this graph. Add a Watch node, or run with --all.");
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: {result.NodesEvaluated} node(s) evaluated, {result.CacheHits} cache hit(s), "
            + $"{result.Diagnostics.Count} diagnostic(s)"));

        return result.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Opens a graph, evaluates it with no window, and reports only what is wrong with it.
    /// </summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="error">Where diagnostics go. <see cref="Console.Error"/> in the product.</param>
    /// <param name="trust">
    /// The record of package-folder assemblies the user has agreed to, or null for the one the
    /// desktop application keeps. A test passes its own so that the user's is never touched.
    /// </param>
    /// <returns>Zero when nothing errored, one otherwise.</returns>
    /// <remarks>
    /// <para>
    /// <b>It is <c>spark run</c> with the printing taken away, and the difference is the point.</b>
    /// <c>run</c> answers <i>what did this graph produce</i> and prints values to standard output.
    /// <c>check</c> answers <i>is this graph broken</i> and prints nothing at all when it is not —
    /// a gate that writes a line on the happy path is a gate whose output stops being read, and
    /// then the one run that had something to say scrolls past with the rest.
    /// </para>
    /// <para>
    /// <b>Warnings print and do not fail, and <c>--strict</c> is there because that is not always
    /// what a build wants.</b> A per-element replication failure is a warning by
    /// <see cref="DiagnosticSeverity"/>'s own definition — the node produced a value and everything
    /// downstream still evaluated — and a gate that refused every one of those by default is a gate
    /// somebody turns off. But <b>the definition holds when one element of eight fails and also
    /// when eight of eight do</b>: a circle node given a radius of zero for every input reports
    /// <c>SPK1042</c> as a warning, produces a list of nothing, and would otherwise pass. That
    /// asymmetry is real and surprising — the same failure on an unreplicated node is an error —
    /// so <c>--strict</c> fails on any diagnostic at all, and the default is documented rather
    /// than quietly relied upon.
    /// </para>
    /// <para>
    /// Errors fail either way, and so does a graph that will not load at all, which is caught by
    /// the same handler <c>run</c> and <c>export</c> use.
    /// </para>
    /// <para>
    /// <b>The node is named, which <c>run</c> does not do.</b> A build log is read by somebody who
    /// was not watching, and <c>SPK1043</c> without a node name is a message that costs an hour.
    /// The name comes from the same <c>Definition.DisplayName</c> the canvas draws, so what the log
    /// says and what the user sees when they open the file agree.
    /// </para>
    /// <para>
    /// <b>Diagnostics carry a node id, not a node</b>, and a diagnostic can carry no id at all —
    /// one raised while restoring the graph belongs to the file rather than to any node. Both
    /// cases print; only the first gets a name.
    /// </para>
    /// </remarks>
    internal static int Check(ReadOnlySpan<string> args, TextWriter error, PackageTrustStore? trust = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        string? input = null;
        bool scripting = true;
        bool strict = false;
        bool once = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Length:
                    input = args[++i];
                    break;

                case "--no-script":
                    scripting = false;
                    break;

                case "--strict":
                    strict = true;
                    break;

                case "--trust-packages":
                    once = true;
                    break;

                default:
                    if (input is null && !args[i].StartsWith('-'))
                    {
                        input = args[i];
                        break;
                    }

                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null)
        {
            error.WriteLine("spark: check needs a graph to check. Try: spark check graph.spark");
            return 1;
        }

        using SparkSession session = new();

        // `E3-T20`: a bundle is opened into a temporary folder, and the graph inside it is what is read.
        using OpenedInput opened = OpenedInput.From(input);
        GraphDocument document = SparkFile.Read(File.ReadAllText(opened.Graph));

        // `E6-T16`, and the same refusal `spark run` makes for the same reason. A graph is
        // executable code; a build that declines to run somebody else's must be told that it
        // contains some, rather than being handed a green result computed without it.
        if (!scripting && document.HasScripts)
        {
            error.WriteLine(
                "spark: this graph contains a code block and --no-script was given, so it was not checked.");

            return 1;
        }

        IScriptNodeFactory? scripts = scripting && document.HasScripts
            ? session.EnableScripting()
            : null;

        int warned = 0;

        if (scripts is not null
            && !AdmitPackages(opened.Graph, document, session, trust, once, $"spark: {input}: ", "checked", error, out warned))
        {
            return 1;
        }

        Graph graph = document.Restore(session.Library, scripts);

        EvaluationContext context = new(default, new SequentialEvaluationScheduler());
        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, CancellationToken.None);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            error.WriteLine($"spark: {input}: {Describe(graph, diagnostic)}");
        }

        return result.HasErrors || (strict && result.Diagnostics.Count + warned > 0) ? 1 : 0;
    }

    /// <summary>
    /// Settles what a graph's code blocks may compile against from its own package folder, before
    /// anything is built (`E7-T25`).
    /// </summary>
    /// <param name="input">The graph's path, which names the folder beside it.</param>
    /// <param name="document">The graph as read, for the packages its file names.</param>
    /// <param name="session">The session whose catalogue agreed assemblies are handed to.</param>
    /// <param name="trust">The user's record, or null for the one the desktop application keeps.</param>
    /// <param name="once">Whether <c>--trust-packages</c> was given.</param>
    /// <param name="prefix">What every line starts with, which differs between the two verbs.</param>
    /// <param name="refused">The last word of a refusal: <i>run</i> or <i>checked</i>.</param>
    /// <param name="error">Where all of it is said.</param>
    /// <param name="warnings">How many warnings were printed, which <c>--strict</c> counts.</param>
    /// <returns>True to go on; false when the graph was refused, which has already been said.</returns>
    /// <remarks>
    /// <para>
    /// <b>The window's rule, with the question taken out.</b> The desktop application references only
    /// assemblies whose bytes the user agreed to and asks about the rest (`E7-T16`). A build agent
    /// has nobody to ask, so this references what the record already holds and <b>refuses</b> the
    /// graph when anything else is there, naming each file and its full hash — refusing rather than
    /// running without them, for <c>--no-script</c>'s reason: a block would then fail naming a type,
    /// and the cause is the folder.
    /// </para>
    /// <para>
    /// <b><c>--trust-packages</c> agrees for this run and records nothing</b>
    /// (<see cref="GraphPackageGate.AgreeOnce"/> says why). An unreadable file is refused even then:
    /// it has no hash, so there is nothing to agree to.
    /// </para>
    /// <para>
    /// <b>Called only for a graph with code blocks</b>, the only thing that compiles against the
    /// folder. A graph without one never reads it or the trust record, and never loads Roslyn
    /// (`E6-T14`). <b>This is stricter than the command line is about code blocks themselves</b>,
    /// which run unasked unless <c>--no-script</c> is given, and deliberately so — see N149.
    /// </para>
    /// </remarks>
    private static bool AdmitPackages(
        string input,
        GraphDocument document,
        SparkSession session,
        PackageTrustStore? trust,
        bool once,
        string prefix,
        string refused,
        TextWriter error,
        out int warnings)
    {
        warnings = 0;

        // `E7-T17`: a package the file names and the folder does not hold is said first, because it
        // is the explanation for the compile error a block that uses it is about to produce.
        foreach (AbsentGraphPackage absent in GraphPackages.Absent(
            input, document.Packages.Select(package => package.Path)))
        {
            error.WriteLine(
                $"{prefix}warning: this graph expects '{absent.Name}' in "
                + $"'{Path.GetFileName(absent.LookedIn)}' beside it, and it is not there.");

            warnings++;
        }

        if (!Directory.Exists(GraphPackages.FolderFor(input)))
        {
            return true;
        }

        GraphPackageGate gate = new(
            trust ?? PackageTrustStore.For(PackageStore.Default()),
            () => session.ScriptReferences());

        _ = gate.Open(input);

        if (once)
        {
            _ = gate.AgreeOnce();
        }

        foreach (GraphAssembly assembly in gate.Pending.Where(assembly => assembly.Hash.Length == 0))
        {
            error.WriteLine(
                $"{prefix}{assembly.Describe()} in the graph's package folder could not be read, so "
                + $"the graph was not {refused}. A build may still be writing it.");
        }

        GraphAssembly[] unagreed = [.. gate.Pending.Where(assembly => assembly.Hash.Length > 0)];

        if (unagreed.Length > 0)
        {
            bool one = unagreed.Length == 1;
            string count = one
                ? "an assembly"
                : unagreed.Length.ToString(CultureInfo.InvariantCulture) + " assemblies";

            error.WriteLine(
                $"{prefix}the graph's package folder holds {count} nobody has agreed to load, so it was not {refused}:");

            // The full hash, where the window shows eight characters: a log is compared against a
            // known build by a script as often as by eye.
            foreach (GraphAssembly assembly in unagreed)
            {
                error.WriteLine($"{prefix}  {assembly.Describe()}  sha256 {assembly.Hash}");
            }

            string them = one ? "it" : "them";

            error.WriteLine(
                $"{prefix}agree to {them} by opening the graph in Spark, or pass --trust-packages "
                + $"to use {them} for this run only, recording nothing.");
        }

        return gate.Pending.IsEmpty;
    }

    /// <summary>
    /// Renders one diagnostic as a line, naming the node it belongs to when it has one.
    /// </summary>
    /// <param name="graph">The graph the diagnostic came from.</param>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>The line, without the leading <c>spark:</c> and the file name.</returns>
    /// <remarks>
    /// The lookup is defensive on purpose. A diagnostic's node id is a <c>Guid</c> and nothing in
    /// the type system says it names a node in <i>this</i> graph — a diagnostic raised against a
    /// node that was then removed would otherwise turn a helpful message into a crash inside the
    /// thing whose whole job is reporting problems.
    /// </remarks>
    private static string Describe(Graph graph, SparkDiagnostic diagnostic)
    {
        string severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };

        string? node = null;

        if (diagnostic.NodeId is { } id)
        {
            try
            {
                node = graph.Node(new NodeId(id)).Definition.DisplayName;
            }
            catch (KeyNotFoundException)
            {
                node = null;
            }
        }

        string message = OneLine(diagnostic.Message);

        return node is null
            ? $"{severity} {diagnostic.Code}: {message}"
            : $"{severity} {diagnostic.Code}: {node}: {message}";
    }

    /// <summary>
    /// Flattens a diagnostic message onto one line.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The same text with every run of whitespace reduced to a single space.</returns>
    /// <remarks>
    /// <b>A build log is read one line at a time, by <c>grep</c> as often as by a person.</b>
    /// Several messages arrive with a newline in them without anybody choosing that — a
    /// <see cref="ArgumentOutOfRangeException"/> appends <i>Actual value was 0.</i> on its own
    /// line — and a diagnostic split across two lines is one whose second half has lost its file
    /// name, its node and its severity. This is done here rather than at the source because the
    /// canvas wants the line break: it has room, and the second line is genuinely useful there.
    /// </remarks>
    private static string OneLine(string message)
    {
        if (message.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            return message;
        }

        string[] parts = message.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Opens a graph, evaluates it with no window, and writes its geometry to a file.
    /// </summary>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="report">Where the line saying what was written goes. <see cref="Console.Out"/> in the product.</param>
    /// <param name="error">Where diagnostics go. <see cref="Console.Error"/> in the product.</param>
    /// <param name="trust">
    /// The record of package-folder assemblies the user has agreed to, or null for the one the
    /// desktop application keeps. A test passes its own so that the user's is never touched.
    /// </param>
    /// <returns>Zero when a file was written and nothing errored, two when there was nothing to write, one otherwise.</returns>
    /// <remarks>
    /// <b>A graph with a code block exports like any other</b> (`E12-T24`). It used to be restored
    /// with no script factory at all, so every such graph was refused as though <c>--no-script</c>
    /// had been given - including graphs whose geometry came entirely from ordinary nodes. It now
    /// takes <c>run</c>'s rules whole: a factory only when the document has code blocks, the
    /// package folder settled before anything is built, <c>--no-script</c> to refuse, and
    /// <c>--trust-packages</c> for one run.
    /// </remarks>
    internal static int Export(
        ReadOnlySpan<string> args, TextWriter report, TextWriter error, PackageTrustStore? trust = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(error);

        string? input = null;
        string? output = null;
        double tolerance = 0.0;
        bool scripting = true;
        bool once = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Length:
                    input = args[++i];
                    break;

                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                case "--tolerance" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out tolerance)
                        || !double.IsFinite(tolerance)
                        || tolerance < 0.0)
                    {
                        error.WriteLine("spark: --tolerance takes a non-negative number.");
                        return 1;
                    }

                    break;

                case "--no-script":
                    scripting = false;
                    break;

                case "--trust-packages":
                    once = true;
                    break;

                default:
                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null || output is null)
        {
            error.WriteLine("spark: export needs --open PATH and --out FILE.obj.");
            return 1;
        }

        Tolerance chosen = tolerance > 0.0
            ? new Tolerance(tolerance, Angle.FromDegrees(0.1), 1e-12)
            : Tolerance.Default;

        using SparkSession session = new();

        // `E3-T20`: a bundle is opened into a temporary folder, and the graph inside it is what is read.
        using OpenedInput opened = OpenedInput.From(input);
        GraphDocument document = SparkFile.Read(File.ReadAllText(opened.Graph));

        if (!scripting && document.HasScripts)
        {
            error.WriteLine(
                "spark: this graph contains a code block and --no-script was given, so it was not exported.");

            return 1;
        }

        IScriptNodeFactory? scripts = scripting && document.HasScripts
            ? session.EnableScripting()
            : null;

        if (scripts is not null
            && !AdmitPackages(opened.Graph, document, session, trust, once, "spark: ", "exported", error, out _))
        {
            return 1;
        }

        Graph graph = document.Restore(session.Library, scripts);

        EvaluationContext context = new(default, new SequentialEvaluationScheduler());
        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, CancellationToken.None);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            error.WriteLine($"spark: {diagnostic.Code}: {diagnostic.Message}");
        }

        // **The format comes from the extension, and surfaces are tessellated on the way out.**
        // A user who typed `--out model.stl` has said what they want; making them repeat it in a
        // `--format` flag would be ceremony, and writing OBJ regardless would produce a file whose
        // name lies about its contents.
        string extension = Path.GetExtension(output).ToUpperInvariant();

        if (extension is ".STEP" or ".STP" or ".IGES" or ".IGS")
        {
            List<Brep> solids = [.. Solids(graph, result)];

            if (solids.Count == 0)
            {
                error.WriteLine(
                    "spark: the graph produced no solids, so nothing was written. STEP and IGES "
                    + "carry exact solids; use .obj, .stl, .ply or .glb for curves and meshes.");

                return result.HasErrors ? 1 : 2;
            }

            // Several solids become one shape by sewing, because a STEP file holding one product
            // is what a receiving CAD system expects from `--out one-file.step`. Sewing rather
            // than a union: a union would merge solids that merely touch, which is a modelling
            // decision nobody asked for.
            KernelResult<Brep> together = solids.Count == 1
                ? KernelResult<Brep>.Success(solids[0])
                : BrepKernel.Current.Sew(solids, chosen);

            if (!together.TryGetValue(out Brep? shape))
            {
                error.WriteLine($"spark: {together.Diagnostic!.Code}: {together.Diagnostic.Message}");
                error.WriteLine($"spark: {together.Diagnostic.Detail}");

                return 1;
            }

            KernelResult<bool> wrote = BrepKernel.Current.WriteFile(shape, output, chosen);

            if (!wrote.IsSuccess)
            {
                error.WriteLine($"spark: {wrote.Diagnostic!.Code}: {wrote.Diagnostic.Message}");
                error.WriteLine($"spark: {wrote.Diagnostic.Detail}");

                return 1;
            }

            report.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"spark: wrote {solids.Count} solid(s), {shape.FaceCount} face(s), to {output} "
                + $"({result.NodesEvaluated} node(s) evaluated, {result.CacheHits} cache hit(s))"));

            return result.HasErrors ? 1 : 0;
        }

        if (extension is ".STL" or ".PLY" or ".GLB")
        {
            List<Mesh> meshes = [.. Meshes(graph, result, chosen)];

            if (meshes.Count == 0)
            {
                error.WriteLine(
                    "spark: the graph produced no surfaces or meshes, so nothing was written.");

                return result.HasErrors ? 1 : 2;
            }

            int faces = WriteMeshes(output, extension, meshes);

            report.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"spark: wrote {meshes.Count} mesh(es), {faces} face(s), to {output} at tolerance "
                + $"{chosen.Linear:G9} ({result.NodesEvaluated} node(s) evaluated, {result.CacheHits} cache hit(s))"));

            return result.HasErrors ? 1 : 0;
        }

        List<Curve> curves = [.. Results(graph, result)];
        List<Mesh> alsoMeshes = [.. Meshes(graph, result, chosen)];

        if (curves.Count == 0 && alsoMeshes.Count == 0)
        {
            // Not an error: a graph of numbers is a legal graph. But writing an empty file and
            // saying nothing would look like success, so say which it was.
            error.WriteLine(
                "spark: the graph produced no curves, surfaces or meshes, so nothing was written.");

            return result.HasErrors ? 1 : 2;
        }

        int written = curves.Count > 0
            ? ObjWriter.WriteCurvesToFile(output, curves, chosen)
            : ObjWriter.WriteMeshesToFile(output, alsoMeshes);

        report.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: wrote {written} object(s) to {output} at tolerance {chosen.Linear:G9} "
            + $"({result.NodesEvaluated} node(s) evaluated, {result.CacheHits} cache hit(s))"));

        return result.HasErrors ? 1 : 0;
    }

    /// <summary>
    /// Writes meshes in whichever of the mesh formats the extension named.
    /// </summary>
    /// <remarks>
    /// <b>Several meshes are joined into one before writing.</b> STL and PLY hold one mesh by
    /// construction, and a caller who asked for one file expects one file - glTF's scene graph
    /// could hold several and does not need to here.
    /// </remarks>
    /// <summary>
    /// Opens a graph, evaluates it with no window anywhere, and writes a picture of what it made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The software rasteriser, never the GPU, and that is the whole reason this verb is
    /// useful.</b> <c>E9-T5</c> gives the fallback three jobs and this is the third: GPU output is
    /// not testable — it varies by driver, by vendor and by day — where the software path is
    /// deterministic, so the same graph gives the same bytes on a build agent with no display and
    /// no driver at all. A <c>render</c> that quietly used a GPU when one was present would be a
    /// check that passes differently on every machine.
    /// </para>
    /// <para>
    /// <b>It composes; it does not re-implement.</b> <see cref="ThumbnailRenderer"/> says in its own
    /// summary that it exists to be this verb's mechanism, <c>SceneBuilder</c> turns values into
    /// renderables through the same walk the viewport uses, and <c>PngImage</c> writes the file.
    /// What is here is argument handling and the walk over the graph's output ports.
    /// </para>
    /// <para>
    /// <b>Every node's outputs, not the graph's last ones</b>, for the reason
    /// <see cref="Results"/> records at length: a graph's interesting geometry is routinely
    /// mid-chain, and the leaves are frequently <c>Display</c> nodes whose output is an appearance.
    /// <c>SceneBuilder</c> decides what is renderable, so this hands it everything and counts what
    /// it took.
    /// </para>
    /// <para>
    /// <b>The camera frames the geometry rather than being supplied.</b> A fixed camera would need
    /// a coordinate convention on the command line before anybody has asked for one, and
    /// auto-framing is what makes the output depend on the graph alone — which is the property a
    /// regression check is built on. A <c>--camera</c> flag can arrive when a caller wants a
    /// specific view; it cannot be taken away once the default is a view nobody chose.
    /// </para>
    /// <para>
    /// <b>Exit 2 means the graph ran and drew nothing.</b> An empty scene is a legitimate picture —
    /// <see cref="ThumbnailRenderer"/> deliberately renders the empty viewport rather than a black
    /// rectangle — but a CI job that writes one without complaint is the vacuously-green failure
    /// this whole verb exists to prevent. The file is still written, because looking at it is how
    /// somebody finds out why.
    /// </para>
    /// </remarks>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="report">Where the summary line goes.</param>
    /// <param name="error">Where diagnostics and refusals go.</param>
    /// <param name="trust">
    /// The record of package-folder assemblies the user has agreed to, or null for the one the
    /// desktop application keeps. A test passes its own so that the user's is never touched.
    /// </param>
    /// <returns>Zero on success, one on error, two when nothing was renderable.</returns>
    internal static int Render(
        ReadOnlySpan<string> args, TextWriter report, TextWriter error, PackageTrustStore? trust = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(error);

        string? input = null;
        string? output = null;
        int width = 1280;
        int height = 720;
        bool grid = true;
        bool scripting = true;
        bool once = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Length:
                    input = args[++i];
                    break;

                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                case "--width" when i + 1 < args.Length:
                    if (!TrySize(args[++i], "--width", error, out width))
                    {
                        return 1;
                    }

                    break;

                case "--height" when i + 1 < args.Length:
                    if (!TrySize(args[++i], "--height", error, out height))
                    {
                        return 1;
                    }

                    break;

                case "--no-grid":
                    grid = false;
                    break;

                case "--no-script":
                    scripting = false;
                    break;

                case "--trust-packages":
                    once = true;
                    break;

                default:
                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null || output is null)
        {
            error.WriteLine("spark: render needs --open PATH and --out FILE.png.");
            return 1;
        }

        // PNG and nothing else, and it is said rather than assumed. The rasteriser produces RGBA
        // and `PngImage` is the only encoder in the tree, so a `--out picture.jpg` that wrote a
        // PNG under a lying name is the one outcome worth refusing outright.
        if (!string.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine("spark: render writes PNG; give --out a .png file name.");
            return 1;
        }

        using SparkSession session = new();

        using OpenedInput opened = OpenedInput.From(input);
        GraphDocument document = SparkFile.Read(File.ReadAllText(opened.Graph));

        if (!scripting && document.HasScripts)
        {
            error.WriteLine(
                "spark: this graph contains a code block and --no-script was given, so it was not rendered.");

            return 1;
        }

        IScriptNodeFactory? scripts = scripting && document.HasScripts
            ? session.EnableScripting()
            : null;

        if (scripts is not null
            && !AdmitPackages(opened.Graph, document, session, trust, once, "spark: ", "rendered", error, out _))
        {
            return 1;
        }

        Graph graph = document.Restore(session.Library, scripts);

        EvaluationContext context = new(default, new SequentialEvaluationScheduler());
        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, CancellationToken.None);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            error.WriteLine($"spark: {diagnostic.Code}: {diagnostic.Message}");
        }

        SceneBuilder builder = new();

        foreach (NodeInstance node in graph.Nodes())
        {
            for (int port = 0; port < node.Definition.Outputs.Count; port++)
            {
                builder.Add(new GeometryKey(node.Id.ToString(), port), result.Value(node.Id, port));
            }
        }

        ViewportScene scene = new();
        builder.PublishTo(scene);

        byte[] pixels = ThumbnailRenderer.Render(scene, width, height, grid);
        File.WriteAllBytes(output, PngImage.Encode(pixels, width, height));

        report.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: wrote {width}x{height} to {output} "
            + $"({builder.RenderableCount} renderable(s), {result.NodesEvaluated} node(s) evaluated, "
            + $"{result.CacheHits} cache hit(s))"));

        if (result.HasErrors)
        {
            return 1;
        }

        if (builder.RenderableCount == 0)
        {
            error.WriteLine(
                "spark: the graph produced nothing to draw, so the image is the empty viewport.");

            return 2;
        }

        return 0;
    }

    /// <summary>Parses a pixel dimension, refusing the values that make a meaningless image.</summary>
    /// <remarks>
    /// An upper bound as well as a lower one: <c>--width 100000</c> is a two-hundred-gigabyte
    /// allocation and a process the operating system kills, which reads to the caller as Spark
    /// crashing rather than as an argument they should not have typed.
    /// </remarks>
    private static bool TrySize(string text, string option, TextWriter error, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            || value < 1
            || value > 16384)
        {
            error.WriteLine($"spark: {option} takes a whole number of pixels between 1 and 16384.");
            value = 0;
            return false;
        }

        return true;
    }
    /// <summary>
    /// Reports and repairs the package folder beside a graph (<c>E12-T5</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A face on a finished library rather than new machinery.</b> <c>GraphPackages</c> already
    /// answers <i>what is beside this graph</i> and <i>what does its file record that is not
    /// there</i>, and <c>NuGetPackageClient</c> already installs into a store. What this adds is
    /// the two questions a build asks — <i>is this checkout complete?</i> and <i>make it
    /// complete</i> — and an exit code for the first.
    /// </para>
    /// <para>
    /// <b><c>list</c> exits 1 when something is missing</b>, which is the whole reason it is worth
    /// having over reading the folder. Without it a build discovers an absent package as a compile
    /// error inside a code block, two steps and one confusing message away from the cause.
    /// </para>
    /// <para>
    /// <b><c>restore</c> downloads; it does not agree.</b> Consent is unchanged and that is
    /// deliberate: <c>run</c> and <c>check</c> still refuse assemblies nobody has trusted, so a
    /// package restored on a fresh machine needs <c>--trust-packages</c> or the desktop window
    /// exactly as it did before. A verb that both fetched code and consented to it on the user's
    /// behalf would be a hole in <c>E7-T16</c>'s gate wearing a convenience's clothes.
    /// </para>
    /// </remarks>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="output">Where the listing goes.</param>
    /// <param name="error">Where problems go.</param>
    /// <param name="source">The feed to restore from, or null for nuget.org. Tests pass a folder.</param>
    /// <returns>Zero when the folder is complete, one otherwise.</returns>
    internal static int Pkg(
        ReadOnlySpan<string> args, TextWriter output, TextWriter error, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            error.WriteLine("spark: pkg needs a sub-command: list or restore.");
            return 1;
        }

        string command = args[0];

        if (command is not ("list" or "restore"))
        {
            error.WriteLine($"spark: unknown pkg sub-command '{command}'. Use list or restore.");
            return 1;
        }

        string? input = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Length:
                    input = args[++i];
                    break;

                default:
                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null)
        {
            error.WriteLine($"spark: pkg {command} needs --open PATH.");
            return 1;
        }

        GraphDocument document = SparkFile.Read(File.ReadAllText(input));
        IReadOnlyList<string> recorded = [.. document.Packages.Select(package => package.Path)];
        ImmutableArray<AbsentGraphPackage> absent = GraphPackages.Absent(input, recorded);

        return command is "list"
            ? List(input, recorded, absent, output)
            : Restore(input, absent, source, output, error);
    }

    /// <summary>Prints what the graph records and what is beside it.</summary>
    /// <remarks>
    /// <b>The recorded list and the folder are printed as one reconciliation rather than two
    /// listings.</b> What a person needs is not *what is in the folder* but *does the folder match
    /// the file*, and a pair of lists makes them do that comparison by eye.
    /// </remarks>
    /// <param name="input">The graph's path.</param>
    /// <param name="recorded">What the file records.</param>
    /// <param name="absent">Which of those are not there.</param>
    /// <param name="output">Where the listing goes.</param>
    /// <returns>Zero when nothing is missing, one otherwise.</returns>
    private static int List(
        string input,
        IReadOnlyList<string> recorded,
        ImmutableArray<AbsentGraphPackage> absent,
        TextWriter output)
    {
        GraphPackages beside = GraphPackages.Discover(input);

        output.WriteLine($"spark: {Path.GetFileName(GraphPackages.FolderFor(input))}");

        HashSet<string> missing = new(absent.Select(a => a.Recorded), StringComparer.OrdinalIgnoreCase);

        foreach (string entry in recorded)
        {
            output.WriteLine(missing.Contains(entry) ? $"  missing  {entry}" : $"  present  {entry}");
        }

        // Anything in the folder the file does not record. It loads and works; it is listed because
        // a package nobody wrote down is a package that does not travel with the graph.
        foreach (string entry in GraphPackages.Entries(input))
        {
            if (!recorded.Contains(entry, StringComparer.OrdinalIgnoreCase))
            {
                output.WriteLine($"  unrecorded  {entry}");
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: {recorded.Count} recorded, {absent.Length} missing, "
            + $"{beside.Assemblies.Length} assembly(ies) beside the graph"));

        return absent.Length == 0 ? 0 : 1;
    }

    /// <summary>Installs what the file records and the folder lacks.</summary>
    /// <remarks>
    /// <b>The identity is recovered from the folder name, because a graph records paths.</b>
    /// <c>PackageIdentity.FolderName</c> is <c>id.version</c> lower-cased, so the split is found by
    /// trying the dots — see <see cref="SplitIdentity"/>, which scans from the long end for a
    /// reason.
    /// </remarks>
    /// <param name="input">The graph's path.</param>
    /// <param name="absent">What to fetch.</param>
    /// <param name="source">The feed, or null for nuget.org.</param>
    /// <param name="output">Where progress goes.</param>
    /// <param name="error">Where failures go.</param>
    /// <returns>Zero when everything recorded is now present, one otherwise.</returns>
    private static int Restore(
        string input,
        ImmutableArray<AbsentGraphPackage> absent,
        string? source,
        TextWriter output,
        TextWriter error)
    {
        if (absent.Length == 0)
        {
            output.WriteLine("spark: nothing to restore; every package the graph records is there.");
            return 0;
        }

        PackageStore store = new(GraphPackages.FolderFor(input));
        NuGetPackageClient client = new(source);
        int restored = 0;
        int refused = 0;

        foreach (AbsentGraphPackage package in absent)
        {
            if (SplitIdentity(package.Name) is not { } identity)
            {
                // A loose assembly somebody dropped in the folder by hand. It came from no feed, so
                // there is nowhere to fetch it from, and saying so is the whole of what this verb
                // can do about it.
                error.WriteLine(
                    $"spark: '{package.Name}' is not a package folder, so it cannot be restored "
                    + "from a feed. It was added by hand and has to be put back by hand.");

                refused++;
                continue;
            }

            try
            {
                client.InstallAsync(identity, store, CancellationToken.None).GetAwaiter().GetResult();

                output.WriteLine($"spark: restored {identity}");
                restored++;
            }
            catch (SparkPackageException failure)
            {
                error.WriteLine($"spark: {identity} could not be restored: {failure.Message}");
                refused++;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: restored {restored} of {absent.Length}; {refused} could not be."));

        if (restored > 0)
        {
            // Said once, here, because the person who just downloaded code is the person who needs
            // to know that downloading it did not agree to run it.
            output.WriteLine(
                "spark: restoring does not agree to load anything. Open the graph in Spark to "
                + "agree to the assemblies, or pass --trust-packages to run and check.");
        }

        return refused == 0 ? 0 : 1;
    }

    /// <summary>
    /// Splits a package folder name into an identity.
    /// </summary>
    /// <remarks>
    /// <b>Scanned from the long end, and that is not arbitrary.</b> The name is
    /// <c>id.version</c> with dots in both halves, so several splits parse: for
    /// <c>Acme.Nodes.1.0.0</c> the suffixes <c>1.0.0</c>, <c>0.0</c> and <c>0</c> are all valid
    /// <c>NuGetVersion</c>s. Taking the first that parses while scanning from the short end yields
    /// <c>Acme.Nodes.1.0</c> at version <c>0</c>, which is a package that does not exist. The
    /// longest valid suffix is the version.
    /// </remarks>
    /// <param name="name">The folder name.</param>
    /// <returns>The identity, or null when no split parses — a loose file or a hand-made folder.</returns>
    /// <remarks>
    /// <b>The id comes back lower-cased</b>, because that is how NuGet names the folder and the
    /// folder is all a graph records. Package ids are case-insensitive, so the install is the same
    /// one the publisher intended; what is lost is only the publisher's capitalisation, and
    /// inventing it back would be a guess printed as a fact.
    /// </remarks>
    private static PackageIdentity? SplitIdentity(string name)
    {
        string[] parts = name.Split('.');

        for (int take = parts.Length - 1; take >= 1; take--)
        {
            string version = string.Join('.', parts[^take..]);

            if (NuGetVersion.TryParse(version, out _))
            {
                return new PackageIdentity(string.Join('.', parts[..^take]), version);
            }
        }

        return null;
    }
    private static int WriteMeshes(string output, string extension, List<Mesh> meshes)
    {
        Mesh combined = Combine(meshes);

        return extension switch
        {
            ".STL" => StlFile.WriteToFile(output, combined),
            ".PLY" => PlyFile.WriteToFile(output, combined),
            _ => GltfWriter.WriteToFile(output, combined),
        };
    }

    /// <summary>Joins several meshes into one, offsetting each one's indices.</summary>
    private static Mesh Combine(List<Mesh> meshes)
    {
        if (meshes.Count == 1)
        {
            return meshes[0];
        }

        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        foreach (Mesh mesh in meshes)
        {
            int offset = vertices.Count;

            vertices.AddRange(mesh.Vertices());

            foreach (MeshFace face in mesh.Faces())
            {
                faces.Add(face.IsQuad
                    ? new MeshFace(face.A + offset, face.B + offset, face.C + offset, face.D + offset)
                    : new MeshFace(face.A + offset, face.B + offset, face.C + offset));
            }
        }

        return new Mesh(vertices, faces);
    }

    /// <summary>
    /// Every mesh the graph produced, with surfaces tessellated at the export tolerance.
    /// </summary>
    /// <remarks>
    /// The same reasoning as <see cref="Results"/>: every node's outputs rather than only the
    /// leaves, because a graph's interesting geometry is routinely mid-chain, and repeats removed
    /// by reference because a pass-through node yields the instance it was given.
    /// </remarks>
    private static IEnumerable<Mesh> Meshes(Graph graph, EvaluationResult result, Tolerance tolerance)
    {
        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);

        foreach (NodeInstance node in graph.Nodes())
        {
            for (int port = 0; port < node.Definition.Outputs.Count; port++)
            {
                foreach (object value in Renderable(result.Value(node.Id, port)))
                {
                    if (!seen.Add(value))
                    {
                        continue;
                    }

                    yield return value switch
                    {
                        Mesh mesh => mesh,
                        Spark.Geometry.Surface surface => surface.ToMesh(tolerance),
                        _ => throw new InvalidOperationException("Renderable yielded something else."),
                    };
                }
            }
        }
    }

    /// <summary>
    /// Every solid the graph produced, in node order, with repeats removed by reference.
    /// </summary>
    /// <remarks>
    /// <b>Solids and not their tessellations.</b> The whole point of a STEP export is that the
    /// exact surfaces travel; harvesting through <see cref="Meshes"/> would write a file full of
    /// triangles with a `.step` extension, which is worse than refusing.
    /// </remarks>
    private static IEnumerable<Brep> Solids(Graph graph, EvaluationResult result)
    {
        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);

        foreach (NodeInstance node in graph.Nodes())
        {
            for (int port = 0; port < node.Definition.Outputs.Count; port++)
            {
                foreach (Brep solid in SolidsIn(result.Value(node.Id, port)))
                {
                    if (seen.Add(solid))
                    {
                        yield return solid;
                    }
                }
            }
        }
    }

    private static IEnumerable<Brep> SolidsIn(object? value)
    {
        switch (value)
        {
            case Brep solid:
                yield return solid;
                break;

            case Displayable displayable:
                foreach (Brep nested in SolidsIn(displayable.Geometry))
                {
                    yield return nested;
                }

                break;

            case System.Collections.IEnumerable list and not string:
                foreach (object? item in list)
                {
                    foreach (Brep nested in SolidsIn(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static IEnumerable<object> Renderable(object? value)
    {
        switch (value)
        {
            case Mesh or Spark.Geometry.Surface:
                yield return value;
                break;

            case Displayable displayable:
                foreach (object inner in Renderable(displayable.Geometry))
                {
                    yield return inner;
                }

                break;

            case SparkList list:
                foreach (object? item in list)
                {
                    foreach (object inner in Renderable(item))
                    {
                        yield return inner;
                    }
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Every curve the graph produced, in node order, without repeats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Every node's outputs, not only the graph's last ones.** The first version of this took
    /// the nodes nothing consumes, on the reasoning that ingredients are not results — and it
    /// exported nothing at all from `docs/examples/curves.spark`, because that graph ends in
    /// `Display.FromGeometryColour` nodes whose output is an appearance rather than a curve. The
    /// lesson generalises: **a graph's interesting geometry is routinely mid-chain**, and a rule
    /// that only looks at the leaves is a rule that exports the labelling.
    /// </para>
    /// <para>
    /// Repeats are removed by **reference**, not by value, which is exactly right here: a node
    /// that passes geometry through — `Display` above all — yields the same instance its input
    /// had, and the provenance cache makes that identity reliable. A curve genuinely rebuilt by
    /// another node is a different object and is exported, which is why joining two lines into a
    /// polycurve writes all three. That is a real duplication and it is the caller's to avoid by
    /// exporting a different graph.
    /// </para>
    /// </remarks>
    private static IEnumerable<Curve> Results(Graph graph, EvaluationResult result)
    {
        HashSet<Curve> seen = new(ReferenceEqualityComparer.Instance as IEqualityComparer<Curve>
            ?? EqualityComparer<Curve>.Default);

        foreach (NodeInstance node in graph.Nodes())
        {
            for (int port = 0; port < node.Definition.Outputs.Count; port++)
            {
                foreach (Curve curve in Curves(result.Value(node.Id, port)))
                {
                    if (seen.Add(curve))
                    {
                        yield return curve;
                    }
                }
            }
        }
    }

    private static IEnumerable<Curve> Curves(object? value)
    {
        switch (value)
        {
            case Curve curve:
                yield return curve;
                break;

            case SparkList list:
                for (int i = 0; i < list.Count; i++)
                {
                    foreach (Curve curve in Curves(list[i]))
                    {
                        yield return curve;
                    }
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Lists, prints or writes out the help library (<c>E12-T5</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It generates nothing.</b> The node reference is already produced from the live library by
    /// <c>NodeReference</c> (<c>E10-T5</c>), the diagnostic pages by <c>DiagnosticReference</c>, and
    /// the concept topics are files; <c>HelpComposition</c> assembles the three and the F1 window
    /// shows exactly what this writes. <b>So this verb is a second destination for one library, not
    /// a second copy of it</b> — <c>ValueText</c>'s rule, which is the only way <i>the command line
    /// agrees with the application</i> stays true rather than merely asserted.
    /// </para>
    /// <para>
    /// <b>Three modes over one mechanism.</b> Bare, it lists what there is. <c>--topic</c> prints
    /// one page, which is <c>man</c> for a Spark node and the mode a person will actually use from
    /// a terminal. <c>--out</c> writes the lot as Markdown files, for publishing, for reading on a
    /// machine with no Spark, and for putting a documentation change in a pull request diff.
    /// </para>
    /// <para>
    /// <b>A topic that does not exist exits 1 and suggests</b>, rather than printing nothing and
    /// succeeding. The suggestions come from <c>HelpLibrary.Search</c>, which is the same search the
    /// help window's box uses — a misspelt id is the ordinary case and an empty answer is the one
    /// outcome that helps nobody.
    /// </para>
    /// <para>
    /// <b>It never loads Roslyn</b>, because it never asks for a script factory: the node pages come
    /// from definitions already in the library (<c>E6-T14</c>).
    /// </para>
    /// </remarks>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="output">Where the listing or the page goes.</param>
    /// <param name="error">Where problems go.</param>
    /// <returns>Zero on success, one otherwise.</returns>
    internal static int Docs(ReadOnlySpan<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? topic = null;
        string? directory = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--topic" when i + 1 < args.Length:
                    topic = args[++i];
                    break;

                case "--out" when i + 1 < args.Length:
                    directory = args[++i];
                    break;

                default:
                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (topic is not null && directory is not null)
        {
            error.WriteLine("spark: docs takes --topic or --out, not both.");
            return 1;
        }

        using SparkSession session = new();
        HelpLibrary help = HelpComposition.Build(session.Library);

        if (topic is not null)
        {
            return Print(help, topic, output, error);
        }

        return directory is not null
            ? WriteAll(help, directory, output, error)
            : Catalogue(help, output);
    }

    /// <summary>Prints one topic as Markdown.</summary>
    /// <param name="help">The library.</param>
    /// <param name="id">The topic id asked for.</param>
    /// <param name="output">Where the page goes.</param>
    /// <param name="error">Where a miss is reported.</param>
    /// <returns>Zero when the topic was found, one otherwise.</returns>
    private static int Print(HelpLibrary help, string id, TextWriter output, TextWriter error)
    {
        if (help.TryGet(id, out HelpDocument? found) && found is not null)
        {
            output.Write(HelpMarkdown.Write(found));
            return 0;
        }

        error.WriteLine($"spark: no help topic '{id}'.");

        IReadOnlyList<HelpDocument> near = Suggestions(help, id);

        if (near.Count > 0)
        {
            error.WriteLine("spark: did you mean:");

            foreach (HelpDocument candidate in near)
            {
                error.WriteLine($"  {candidate.Id}");
            }
        }
        else
        {
            error.WriteLine("spark: run 'spark docs' with no arguments to list every topic.");
        }

        return 1;
    }

    /// <summary>What to offer somebody whose topic id did not resolve.</summary>
    /// <param name="help">The library.</param>
    /// <param name="id">The id they asked for.</param>
    /// <returns>At most five topics, id matches first.</returns>
    /// <remarks>
    /// <para>
    /// <b>An id that misses is usually one segment wrong rather than gibberish</b> —
    /// <c>nodes.Point.FromCoordinates</c> with the package left out is the ordinary mistake — so
    /// the last segment is searched as well as the whole string.
    /// </para>
    /// <para>
    /// <b>Both searches run, and id matches are put first, because the first version only fell
    /// back when the whole-id search found nothing and that turned out to be far too fragile.</b>
    /// It worked until the command-line help topic gained a worked example of this very message,
    /// which put the literal text <c>nodes.Point.FromCoordinates</c> into the corpus: the whole-id
    /// search then matched that page's prose, the fallback never fired, and the one suggestion
    /// offered was the topic that happened to quote the mistake. A rule that switches off as soon
    /// as any result appears is a rule that any incidental sentence can switch off.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<HelpDocument> Suggestions(HelpLibrary help, string id)
    {
        string segment = id.LastIndexOfAny(['.', '/']) is > 0 and int cut ? id[(cut + 1)..] : id;

        List<HelpDocument> ranked = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // A topic whose *id* carries the segment is what somebody typing an id meant. Everything
        // else is a body match, which is worth offering and is not worth offering first.
        foreach (HelpDocument document in help.Search(segment, limit: 25))
        {
            if (document.Id.Contains(segment, StringComparison.OrdinalIgnoreCase) && seen.Add(document.Id))
            {
                ranked.Add(document);
            }
        }

        foreach (HelpDocument document in help.Search(id, limit: 25).Concat(help.Search(segment, limit: 25)))
        {
            if (seen.Add(document.Id))
            {
                ranked.Add(document);
            }
        }

        return [.. ranked.Take(5)];
    }

    /// <summary>Lists every topic: its id, and its title.</summary>
    /// <remarks>
    /// <b>Generated pages are marked as such</b>, because the distinction is the one a reader of
    /// this list needs: a concept topic is a file somebody can edit and send a change to, and a
    /// node page is produced from the node and cannot be edited at all. Telling somebody to fix a
    /// page that does not exist as a file wastes an afternoon.
    /// </remarks>
    /// <param name="help">The library.</param>
    /// <param name="output">Where the listing goes.</param>
    /// <returns>Zero.</returns>
    private static int Catalogue(HelpLibrary help, TextWriter output)
    {
        int generated = 0;

        foreach (HelpDocument document in help.Topics.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            bool fromLibrary = IsGenerated(document.Id);
            generated += fromLibrary ? 1 : 0;

            output.WriteLine($"  {(fromLibrary ? "generated" : "written  ")}  {document.Id}  {document.Title}");
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: {help.Topics.Count} topic(s); {help.Topics.Count - generated} written, {generated} generated"));

        return 0;
    }

    /// <summary>Writes every topic into a directory as Markdown.</summary>
    /// <remarks>
    /// <para>
    /// <b>The tree mirrors what a topic id actually means, which is not every dot.</b> An id is a
    /// kind, then the rest: <c>concepts.lacing</c>, <c>diagnostics.SPK1042</c>,
    /// <c>nodes.Spark.Nodes.Core/Point.FromCoordinates</c>. Splitting on every dot was tried first
    /// and it shreds the two parts that are single names — the package becomes three directories and
    /// <c>Point.FromCoordinates</c> becomes a <c>Point</c> folder holding a
    /// <c>FromCoordinates.md</c>, which is a node family nobody declared. So it splits on the
    /// <i>first</i> dot and on <c>/</c>, the separator a node key already uses for exactly this
    /// distinction, and the result is <c>nodes/Spark.Nodes.Core/Point.FromCoordinates.md</c>.
    /// </para>
    /// <para>
    /// <b>The directory is created and files in it are overwritten, but nothing is deleted.</b>
    /// Emptying a directory the user named is not this verb's business — a mistyped path would take
    /// somebody's work with it, and the failure would be silent and total.
    /// </para>
    /// </remarks>
    /// <param name="help">The library.</param>
    /// <param name="directory">Where to write.</param>
    /// <param name="output">Where progress goes.</param>
    /// <param name="error">Where failures go.</param>
    /// <returns>Zero when every topic was written, one otherwise.</returns>
    private static int WriteAll(
        HelpLibrary help, string directory, TextWriter output, TextWriter error)
    {
        Directory.CreateDirectory(directory);

        int written = 0;

        foreach (HelpDocument document in help.Topics.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            string path = Path.Combine(directory, RelativePathFor(document.Id) + ".md");

            string? parent = Path.GetDirectoryName(path);

            if (parent is { Length: > 0 })
            {
                Directory.CreateDirectory(parent);
            }

            // A line feed, not the platform's newline, for the reason `HelpMarkdown.Write` gives:
            // a generated tree written on Windows and one written on Linux have to be the same
            // bytes, or every file in it reads as changed on the other machine.
            File.WriteAllText(path, HelpMarkdown.Write(document));
            written++;
        }

        if (written == 0)
        {
            error.WriteLine("spark: there were no topics to write, which should be impossible.");
            return 1;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"spark: wrote {written} topic(s) to {directory}"));

        return 0;
    }

    /// <summary>The path a topic is written to, relative to the output directory.</summary>
    /// <param name="id">The topic id.</param>
    /// <returns>The relative path, without an extension.</returns>
    private static string RelativePathFor(string id)
    {
        int dot = id.IndexOf('.', StringComparison.Ordinal);

        // No dot at all is not an id this build produces, but a hand-written topic could carry one
        // and a verb that threw over it would be refusing to write documentation it had been given.
        return dot <= 0
            ? Sanitise(id)
            : Path.Combine([Sanitise(id[..dot]), .. id[(dot + 1)..].Split('/').Select(Sanitise)]);
    }

    /// <summary>Makes one path segment safe to write.</summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The segment with anything a file name cannot hold replaced by <c>_</c>.</returns>
    /// <remarks>
    /// Node keys and topic ids are already conservative, so this replaces nothing in practice. It
    /// is here because a package identity comes from a third party and <c>docs --out</c> writes
    /// files: a key holding a colon or a backslash would otherwise escape the directory it was
    /// pointed at, which is a path traversal wearing a documentation verb's clothes.
    /// </remarks>
    private static string Sanitise(string segment)
    {
        if (segment is "." or "..")
        {
            return "_";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        return invalid.Any(segment.Contains)
            ? string.Concat(segment.Select(c => invalid.Contains(c) ? '_' : c))
            : segment;
    }

    /// <summary>Whether a topic id names a page produced from the library rather than a file.</summary>
    /// <param name="id">The topic id.</param>
    /// <returns>True for a node page, the node index or a diagnostic page.</returns>
    /// <remarks>
    /// The index's own id is <c>nodes.index</c>, so the node prefix catches it without a third
    /// case — which is the reason that id was given that shape.
    /// </remarks>
    private static bool IsGenerated(string id) =>
        id.StartsWith(NodeReference.TopicPrefix, StringComparison.Ordinal)
        || id.StartsWith(DiagnosticReference.TopicPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Describes a <c>.spark</c> file without binding it (<c>E12-T5</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is the only verb that works on a graph this build cannot open</b>, and that is the
    /// whole of why it exists. <c>run</c>, <c>check</c>, <c>export</c> and <c>render</c> all call
    /// <see cref="GraphDocument.Restore"/>, which needs the node library and — for a graph with a
    /// code block in it — the whole of Roslyn. This reads the document and reports on it. The
    /// moment somebody wants it is the moment <c>check</c> has just said <i>this graph names a
    /// definition I do not have</i>, which is precisely the moment the verbs that bind are
    /// useless.
    /// </para>
    /// <para>
    /// <b>One reconciliation, not three listings</b> — <see cref="List"/>'s rule, for
    /// <see cref="List"/>'s reason. What a person reading a build log needs is not <i>what is in
    /// this file</i> and separately <i>what does this build have</i>, but whether the two agree,
    /// and printing the pair makes them do that comparison by eye.
    /// </para>
    /// <para>
    /// <b>A code block is counted apart and is never <i>missing</i>.</b> Its definition is its
    /// source, carried in the file; no library holds it and none could. Reporting it against the
    /// library would make every graph containing one look broken.
    /// </para>
    /// <para>
    /// <b>It must not load Roslyn</b>, which is the standing constraint on every path a graph with
    /// no code block can reach (<c>E6-T14</c>) — and this verb reaches nothing else, because it
    /// never asks for a factory. <c>ScriptingResidencyTests</c> is what holds it to that.
    /// </para>
    /// <para>
    /// <b>Exit 1 when something is missing</b>, for <c>pkg list</c>'s reason: the exit code is the
    /// answer to <i>will this open here</i>, and a verb whose exit code never varies is a verb a
    /// build script cannot use. A definition the library lacks, a recorded package the folder
    /// lacks, or a format this build is too old to read all fail it.
    /// </para>
    /// </remarks>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="output">Where the description goes.</param>
    /// <param name="error">Where problems go.</param>
    /// <returns>Zero when this build could open the file, one otherwise.</returns>
    internal static int Graph(ReadOnlySpan<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? input = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open" when i + 1 < args.Length:
                    input = args[++i];
                    break;

                default:
                    if (input is null && !args[i].StartsWith('-'))
                    {
                        input = args[i];
                        break;
                    }

                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null)
        {
            error.WriteLine("spark: graph needs a graph to describe. Try: spark graph graph.spark");
            return 1;
        }

        // `E3-T20`: a bundle is described by describing the graph inside it, so that the package
        // folder is where the reconciliation below expects to find it.
        using OpenedInput opened = OpenedInput.From(input);
        GraphDocument document = SparkFile.Read(File.ReadAllText(opened.Graph));

        // The library, and nothing else. No session scripting, no factory, no catalogue - the
        // question here is only which definitions exist, and asking it must not cost Roslyn.
        using SparkSession session = new();

        return Report(input, opened.Graph, document, session.Library, output);
    }

    /// <summary>Prints what the file holds against what this build has.</summary>
    /// <param name="input">The path the user typed, which is the name they know.</param>
    /// <param name="graph">The `.spark` file itself, which is inside the bundle for a `.sparkz`.</param>
    /// <param name="document">The file as read.</param>
    /// <param name="library">The definitions this build holds.</param>
    /// <param name="output">Where it goes.</param>
    /// <returns>Zero when nothing is missing and the format is readable, one otherwise.</returns>
    private static int Report(
        string input, string graph, GraphDocument document, NodeLibrary library, TextWriter output)
    {
        int blocks = document.Nodes.Count(node => node.Script is not null);
        int appearance = document.Nodes.Count(node => node.Title is not null || node.Colour is not null);

        int needs = GraphDocument.MinimumReaderVersion(
            document.Notes.Count, document.Groups.Count, blocks, appearance, document.Packages.Count);

        bool readable = document.FormatVersion <= GraphDocument.CurrentFormatVersion;

        string verdict = readable
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"readable by this build (which writes {GraphDocument.CurrentFormatVersion}, "
                + $"and this file needs a reader of {needs})")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"NOT readable by this build, which reads up to {GraphDocument.CurrentFormatVersion}");

        output.WriteLine($"spark: {Path.GetFileName(input)}");
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  format       {document.FormatVersion}, {verdict}"));

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  nodes        {document.Nodes.Count}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  wires        {document.Wires.Count}"));
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  literals     {document.Nodes.Sum(node => node.Literals.Count)}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  notes        {document.Notes.Count}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  groups       {document.Groups.Count}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  code blocks  {blocks}"));

        int missing = Definitions(document, library, blocks, output);

        missing += Recorded(graph, document, output);

        string outcome = missing == 0
            ? "this build can open it"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{missing} thing(s) missing, so this build cannot open it as authored");

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: {document.Nodes.Count} node(s), {document.Wires.Count} wire(s); {outcome}"));

        return missing == 0 && readable ? 0 : 1;
    }

    /// <summary>Reconciles the definitions the file names against the ones this build holds.</summary>
    /// <remarks>
    /// <b>Sorted by key, and counted.</b> A graph of two thousand nodes has a few dozen distinct
    /// definitions, and the count beside each one is what turns the list into a description of the
    /// graph rather than a dump of it. Sorting ordinally rather than by count keeps two runs over
    /// the same file byte-identical, which is what makes the output diffable.
    /// </remarks>
    /// <param name="document">The file as read.</param>
    /// <param name="library">The definitions this build holds.</param>
    /// <param name="blocks">How many nodes carry their own source.</param>
    /// <param name="output">Where it goes.</param>
    /// <returns>How many distinct definitions the library does not have.</returns>
    private static int Definitions(
        GraphDocument document, NodeLibrary library, int blocks, TextWriter output)
    {
        Dictionary<string, int> used = [];

        foreach (GraphDocumentNode node in document.Nodes)
        {
            // A code block's definition is its source, not a library entry, so it is counted with
            // the others above and never reconciled against anything.
            if (node.Script is null)
            {
                used[node.Key.Value] = used.GetValueOrDefault(node.Key.Value) + 1;
            }
        }

        if (used.Count == 0 && blocks == 0)
        {
            return 0;
        }

        output.WriteLine();
        output.WriteLine("  definitions");

        int absent = 0;

        foreach ((string key, int count) in used.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            int slash = key.IndexOf('/', StringComparison.Ordinal);
            bool held = slash > 0
                && library.TryGet(new NodeKey(key[..slash], key[(slash + 1)..]), out _);

            if (!held)
            {
                absent++;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    {(held ? "present" : "missing")}  {key}  x{count}"));
        }

        if (blocks > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    in file  (code block, its source is its definition)  x{blocks}"));
        }

        return absent;
    }

    /// <summary>Reconciles the packages the file records against the folder beside it.</summary>
    /// <remarks>
    /// The same reconciliation <c>pkg list</c> prints, and deliberately the same words, so that
    /// somebody who has read one recognises the other. It is here as well because the question
    /// <i>will this open</i> has two halves and a missing package is the commoner one.
    /// </remarks>
    /// <param name="graph">The `.spark` file, which names the folder beside it.</param>
    /// <param name="document">The file as read.</param>
    /// <param name="output">Where it goes.</param>
    /// <returns>How many recorded packages are not there.</returns>
    private static int Recorded(string graph, GraphDocument document, TextWriter output)
    {
        if (document.Packages.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<string> recorded = [.. document.Packages.Select(package => package.Path)];
        ImmutableArray<AbsentGraphPackage> absent = GraphPackages.Absent(graph, recorded);
        HashSet<string> gone = new(absent.Select(one => one.Recorded), StringComparer.OrdinalIgnoreCase);

        output.WriteLine();
        output.WriteLine("  packages");

        foreach (string entry in recorded.OrderBy(one => one, StringComparer.Ordinal))
        {
            output.WriteLine(gone.Contains(entry) ? $"    missing  {entry}" : $"    present  {entry}");
        }

        return absent.Length;
    }

    /// <summary>
    /// Prints the version, and what this build links.
    /// </summary>
    /// <remarks>
    /// <b>The kernel line is a licence obligation, not a courtesy.</b> The Open CASCADE exception
    /// requires <i>prominent notice in supporting documentation</i> that the work makes use of
    /// facilities provided by OpenCascade, and `spark --version` is where somebody with only a
    /// binary looks. `E12-T18` puts the same thing in the application's About box; this is the
    /// half a command line has. **Nothing here is legal advice** — see `Q13`.
    /// </remarks>
    private static int Version()
    {
        IBrepKernel kernel = BrepKernel.Current;

        // One notice, printed here and shown in the About box, rather than two that drift apart.
        // The kernel's own version string is used when the provider is loaded, because "8.0.1" is
        // the fact somebody reporting a problem needs and "opencascade" is not.
        string? description = kernel is UnavailableBrepKernel ? null : kernel.Description;

        // SparkVersion.Of and not Assembly.GetName().Version. MinVer truncates the assembly
        // version to major.0.0.0 because it participates in binding, so this printed `0.0.0.0` for
        // every build ever made until E12-T21 needed a real number to compare a release against.
        Console.Write(ProductNotice.ToText(
            SparkVersion.Of(typeof(Program).Assembly)?.ToString() ?? "unknown", description));

        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"spark: unknown command '{command}'.");
        Usage();

        return 1;
    }

    /// <summary>
    /// Zips a graph and the package folder beside it into one <c>.sparkz</c> file for sharing
    /// (<c>E3-T20</c>, ADR-0017).
    /// </summary>
    /// <remarks>
    /// <b>The bundle defaults to the graph's own name, beside it</b>, and where it went is printed,
    /// because that is the question a person packing a graph has next. A bad path or an unreadable
    /// file is an <see cref="IOException"/>, which <c>Main</c> reports in one line.
    /// </remarks>
    /// <param name="args">The arguments after the verb.</param>
    /// <param name="output">Where the report goes. <see cref="Console.Out"/> in the product.</param>
    /// <param name="error">Where problems go. <see cref="Console.Error"/> in the product.</param>
    /// <returns>Zero when the bundle was written, one otherwise.</returns>
    internal static int Pack(ReadOnlySpan<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string? input = null;
        string? bundle = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    bundle = args[++i];
                    break;

                default:
                    if (input is null && !args[i].StartsWith('-'))
                    {
                        input = args[i];
                        break;
                    }

                    error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null)
        {
            error.WriteLine("spark: pack needs a graph to pack. Try: spark pack graph.spark");
            return 1;
        }

        SparkBundleContents packed = SparkBundle.Pack(input, bundle ?? Path.ChangeExtension(input, SparkBundle.Extension));

        output.WriteLine(packed.PackageFiles == 0
            ? $"spark: packed {packed.GraphName} into {packed.BundlePath}."
            : $"spark: packed {packed.GraphName} and {packed.PackageFiles} package file(s) into {packed.BundlePath}.");

        return 0;
    }

    private static void Usage()
    {
        Console.WriteLine("spark — the Spark command line");
        Console.WriteLine();
        Console.WriteLine("  spark run GRAPH.spark [--all] [--no-script] [--trust-packages]");
        Console.WriteLine("      Evaluate a graph with no window and print what its watches saw.");
        Console.WriteLine("      --all prints every node's value instead, which is what a diff wants.");
        Console.WriteLine("      --no-script refuses a graph containing a code block. A Spark graph is");
        Console.WriteLine("      executable code; this is how a build declines to run somebody else's.");
        Console.WriteLine("      A graph whose GRAPH.packages folder holds an assembly nobody has agreed");
        Console.WriteLine("      to in Spark is refused, naming each one and its hash. --trust-packages");
        Console.WriteLine("      uses them for this run only, and records nothing.");
        Console.WriteLine();
        Console.WriteLine("  spark check GRAPH.spark [--strict] [--no-script] [--trust-packages]");
        Console.WriteLine("      Evaluate a graph with no window and say nothing unless something");
        Console.WriteLine("      is wrong. Exit 1 if any node errored or the file would not open.");
        Console.WriteLine("      Warnings print and do not fail, because a gate that refused every");
        Console.WriteLine("      one is a gate somebody turns off. --strict fails on any of them,");
        Console.WriteLine("      which is what you want when a replication can fail wholesale and");
        Console.WriteLine("      still only be a warning. For build scripts.");
        Console.WriteLine("      --trust-packages as for run; a package the file names and the folder");
        Console.WriteLine("      lacks is a warning, which --strict fails.");
        Console.WriteLine();
        Console.WriteLine("  spark export --open GRAPH.spark --out FILE.[obj|stl|ply|glb] [--tolerance T]");
        Console.WriteLine("               [--no-script] [--trust-packages]");
        Console.WriteLine("      Evaluate a graph with no window and write its geometry.");
        Console.WriteLine("      The format comes from the extension: obj for curves and meshes,");
        Console.WriteLine("      stl, ply and glb for meshes, step and iges for exact solids.");
        Console.WriteLine("      Surfaces are tessellated on the way out; solids are not - a STEP");
        Console.WriteLine("      file carries the exact surfaces, which is the point of having them.");
        Console.WriteLine("      Curves become polylines; the tolerance used is in the file's header.");
        Console.WriteLine();
        Console.WriteLine("  spark render --open GRAPH.spark --out FILE.png [--width N] [--height N]");
        Console.WriteLine("               [--no-grid] [--no-script] [--trust-packages]");
        Console.WriteLine("      Evaluate a graph with no window and write a picture of it, through");
        Console.WriteLine("      the software rasteriser rather than the GPU - so the same graph gives");
        Console.WriteLine("      the same bytes on a machine with no display and no driver, which is");
        Console.WriteLine("      what makes it usable as a CI check. 1280x720 by default.");
        Console.WriteLine("      The camera frames the geometry automatically. --no-grid leaves out");
        Console.WriteLine("      the ground grid and world axes, for a picture of the geometry alone.");
        Console.WriteLine("      Exit 2 if the graph ran but produced nothing to draw: an empty image");
        Console.WriteLine("      written silently is the failure a visual check exists to catch.");
        Console.WriteLine();
        Console.WriteLine("  spark pkg list|restore --open GRAPH.spark");
        Console.WriteLine("      list reconciles the graph's package folder against what the file");
        Console.WriteLine("      records, and exits 1 when something is missing - so a build finds out");
        Console.WriteLine("      here rather than as a compile error inside a code block later.");
        Console.WriteLine("      restore fetches what the file records and the folder lacks.");
        Console.WriteLine("      Restoring downloads; it does not agree to load anything, so run and");
        Console.WriteLine("      check still need --trust-packages or a visit to the desktop window.");
        Console.WriteLine();
        Console.WriteLine("  spark graph GRAPH.spark");
        Console.WriteLine("      Describe a graph file without opening it: the format version, the");
        Console.WriteLine("      counts, every definition it names marked present or missing against");
        Console.WriteLine("      this build's library, and the packages it records against the folder");
        Console.WriteLine("      beside it. It binds nothing, so it is the one verb that still works");
        Console.WriteLine("      on a graph this build cannot open - which is when you want it.");
        Console.WriteLine("      Exit 1 if anything it names is missing here.");
        Console.WriteLine();
        Console.WriteLine("  spark docs [--topic ID] [--out DIR]");
        Console.WriteLine("      The help the application shows under F1, from a terminal. With no");
        Console.WriteLine("      arguments it lists every topic and marks which are generated from");
        Console.WriteLine("      the node library rather than written by hand. --topic prints one as");
        Console.WriteLine("      Markdown, which is man for a Spark node. --out writes them all as");
        Console.WriteLine("      .md files, for publishing or for reading with no Spark installed.");
        Console.WriteLine("      It is the same library the window shows, not a second copy of it.");
        Console.WriteLine();
        Console.WriteLine("  spark pack GRAPH.spark [--out FILE.sparkz]");
        Console.WriteLine("      Zip a graph and the GRAPH.packages folder beside it into one .sparkz");
        Console.WriteLine("      file for sharing; by default GRAPH.sparkz, beside the graph. run, check");
        Console.WriteLine("      and export open a .sparkz wherever they take a .spark, into a");
        Console.WriteLine("      temporary folder that is removed afterwards.");
        Console.WriteLine();
        Console.WriteLine("  spark --version");
    }

    /// <summary>
    /// The graph a verb was pointed at: the path itself, or for a <c>.sparkz</c> the graph inside it,
    /// opened into a temporary folder that is deleted when the verb is done (<c>E3-T20</c>).
    /// </summary>
    /// <remarks>
    /// The package folder comes out beside the graph, so the package gate finds it exactly as it finds
    /// one beside a <c>.spark</c>, and nothing downstream knows a bundle was involved. Messages keep the
    /// path the user typed, which is the name they know.
    /// </remarks>
    private sealed class OpenedInput : IDisposable
    {
        private readonly string? _folder;

        private OpenedInput(string graph, string? folder)
        {
            Graph = graph;
            _folder = folder;
        }

        /// <summary>The <c>.spark</c> file to read.</summary>
        public string Graph { get; }

        /// <summary>Opens a bundle, or passes a graph's path straight through.</summary>
        /// <param name="input">What the verb was given.</param>
        /// <returns>The graph to read.</returns>
        public static OpenedInput From(string input)
        {
            if (!input.EndsWith(SparkBundle.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return new OpenedInput(input, null);
            }

            string folder = Path.Combine(Path.GetTempPath(), "spark-bundles", Guid.NewGuid().ToString("n"));

            try
            {
                return new OpenedInput(SparkBundle.Unpack(input, folder), folder);
            }
            catch
            {
                Remove(folder);
                throw;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_folder is not null)
            {
                Remove(_folder);
            }
        }

        private static void Remove(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException)
            {
                // A temporary folder left behind is the operating system's to clear, not a failed run.
            }
            catch (UnauthorizedAccessException)
            {
                // A file still held open - a loaded package - is not a reason to fail a verb that worked.
            }
        }
    }
}

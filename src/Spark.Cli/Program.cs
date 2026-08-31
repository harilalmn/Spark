using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Geometry.Io;
using Spark.Host;

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
/// <c>check</c>, <c>render</c>, <c>pkg</c>, <c>docs</c> and <c>graph</c> are `E12-T5` and arrive
/// with the milestones that give them something to do.
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
                "run" => Run(args.AsSpan(1)),
                "export" => Export(args.AsSpan(1)),
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
    /// </remarks>
    private static int Run(ReadOnlySpan<string> args)
    {
        string? input = null;
        bool all = false;
        bool scripting = true;

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

                default:
                    // A bare path is the ordinary way to name a file to a command line, and
                    // requiring --open for the argument the verb is about would be ceremony.
                    if (input is null && !args[i].StartsWith('-'))
                    {
                        input = args[i];
                        break;
                    }

                    Console.Error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null)
        {
            Console.Error.WriteLine("spark: run needs a graph to run. Try: spark run graph.spark");
            return 1;
        }

        using SparkSession session = new();

        GraphDocument document = SparkFile.Read(File.ReadAllText(input));

        // `E6-T16`. **A graph is executable code, and `spark run` is the one place that runs it
        // without a person watching** - in a build, on a schedule, from a hook. So the flag is
        // here, and it refuses rather than dropping the executable parts: a graph that silently
        // ran with its code blocks missing would produce a wrong answer quietly, which is worse
        // than an error. And a document with no scripts in it never asks for a factory at all,
        // which is what keeps Roslyn out of a `spark run` that has no code blocks (`E6-T14`).
        if (!scripting && document.HasScripts)
        {
            Console.Error.WriteLine(
                "spark: this graph contains a code block and --no-script was given, so it was not run.");

            return 1;
        }

        IScriptNodeFactory? scripts = scripting && document.HasScripts
            ? session.EnableScripting()
            : null;

        Graph graph = document.Restore(session.Library, scripts);

        EvaluationContext context = new(default, new SequentialEvaluationScheduler());
        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, CancellationToken.None);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"spark: {diagnostic.Code}: {diagnostic.Message}");
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
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{node.Definition.DisplayName}  {ValueText.Shape(value)}  {summary}"));
        }

        if (reported == 0 && !all)
        {
            // Not an error, and not silence either: a graph with no watches in it ran perfectly
            // well and simply said nothing, which looks identical to a graph that did nothing.
            Console.Error.WriteLine(
                "spark: no watch nodes in this graph. Add a Watch node, or run with --all.");
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"spark: {result.NodesEvaluated} node(s) evaluated, {result.CacheHits} cache hit(s), "
            + $"{result.Diagnostics.Count} diagnostic(s)"));

        return result.HasErrors ? 1 : 0;
    }

    private static int Export(ReadOnlySpan<string> args)
    {
        string? input = null;
        string? output = null;
        double tolerance = 0.0;

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
                        Console.Error.WriteLine("spark: --tolerance takes a non-negative number.");
                        return 1;
                    }

                    break;

                default:
                    Console.Error.WriteLine($"spark: unrecognised option '{args[i]}'.");
                    return 1;
            }
        }

        if (input is null || output is null)
        {
            Console.Error.WriteLine("spark: export needs --open PATH and --out FILE.obj.");
            return 1;
        }

        Tolerance chosen = tolerance > 0.0
            ? new Tolerance(tolerance, Angle.FromDegrees(0.1), 1e-12)
            : Tolerance.Default;

        using SparkSession session = new();

        GraphDocument document = SparkFile.Read(File.ReadAllText(input));
        Graph graph = document.Restore(session.Library);

        EvaluationContext context = new(default, new SequentialEvaluationScheduler());
        EvaluationResult result = GraphEvaluator.Evaluate(graph, context, CancellationToken.None);

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"spark: {diagnostic.Code}: {diagnostic.Message}");
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
                Console.Error.WriteLine(
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
                Console.Error.WriteLine($"spark: {together.Diagnostic!.Code}: {together.Diagnostic.Message}");
                Console.Error.WriteLine($"spark: {together.Diagnostic.Detail}");

                return 1;
            }

            KernelResult<bool> wrote = BrepKernel.Current.WriteFile(shape, output, chosen);

            if (!wrote.IsSuccess)
            {
                Console.Error.WriteLine($"spark: {wrote.Diagnostic!.Code}: {wrote.Diagnostic.Message}");
                Console.Error.WriteLine($"spark: {wrote.Diagnostic.Detail}");

                return 1;
            }

            Console.WriteLine(string.Create(
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
                Console.Error.WriteLine(
                    "spark: the graph produced no surfaces or meshes, so nothing was written.");

                return result.HasErrors ? 1 : 2;
            }

            int faces = WriteMeshes(output, extension, meshes);

            Console.WriteLine(string.Create(
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
            Console.Error.WriteLine(
                "spark: the graph produced no curves, surfaces or meshes, so nothing was written.");

            return result.HasErrors ? 1 : 2;
        }

        int written = curves.Count > 0
            ? ObjWriter.WriteCurvesToFile(output, curves, chosen)
            : ObjWriter.WriteMeshesToFile(output, alsoMeshes);

        Console.WriteLine(string.Create(
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
    /// `Display.ByGeometryColour` nodes whose output is an appearance rather than a curve. The
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
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

        IBrepKernel kernel = BrepKernel.Current;

        if (kernel is Spark.Geometry.Occt.OcctBrepKernel occt)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"solid-modelling kernel: {occt.Version} — LGPL-2.1 with the Open CASCADE "
                + $"exception. This software makes use of facilities provided by the Open CASCADE "
                + $"Technology software. See THIRD-PARTY-NOTICES.md."));
        }
        else
        {
            Console.WriteLine(
                "solid-modelling kernel: none installed. Booleans, fillets and STEP are "
                + "unavailable; everything else works.");
        }

        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"spark: unknown command '{command}'.");
        Usage();

        return 1;
    }

    private static void Usage()
    {
        Console.WriteLine("spark — the Spark command line");
        Console.WriteLine();
        Console.WriteLine("  spark run GRAPH.spark [--all] [--no-script]");
        Console.WriteLine("      Evaluate a graph with no window and print what its watches saw.");
        Console.WriteLine("      --all prints every node's value instead, which is what a diff wants.");
        Console.WriteLine("      --no-script refuses a graph containing a code block. A Spark graph is");
        Console.WriteLine("      executable code; this is how a build declines to run somebody else's.");
        Console.WriteLine();
        Console.WriteLine("  spark export --open GRAPH.spark --out FILE.[obj|stl|ply|glb] [--tolerance T]");
        Console.WriteLine("      Evaluate a graph with no window and write its geometry.");
        Console.WriteLine("      The format comes from the extension: obj for curves and meshes,");
        Console.WriteLine("      stl, ply and glb for meshes, step and iges for exact solids.");
        Console.WriteLine("      Surfaces are tessellated on the way out; solids are not - a STEP");
        Console.WriteLine("      file carries the exact surfaces, which is the point of having them.");
        Console.WriteLine("      Curves become polylines; the tolerance used is in the file's header.");
        Console.WriteLine();
        Console.WriteLine("  spark --version");
        Console.WriteLine();
        Console.WriteLine("  check, render, pkg, docs and graph arrive with later milestones.");
    }
}

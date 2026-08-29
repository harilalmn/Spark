using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// <c>spark run</c> is the headless proof that a graph evaluates outside the desktop
/// application: same <see cref="SparkSession"/>, same node library, same
/// <see cref="SparkFile"/> reader, no Avalonia anywhere in the reference graph. If the two ever
/// disagree about a document, the disagreement is a defect in the layering rather than in one
/// of them.
/// </para>
/// <para>
/// <b>The argument parser is hand-written and stays that way while there are two commands.</b>
/// A parsing library is a dependency, a startup cost and a set of conventions to learn, and it
/// buys nothing until the surface is large enough to be inconsistent without one. The point at
/// which that changes is a real one and it is not here yet.
/// </para>
/// </remarks>
internal static class Program
{
    private const int Ok = 0;
    private const int Failed = 1;
    private const int BadUsage = 2;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            WriteUsage();
            return BadUsage;
        }

        return args[0] switch
        {
            "run" => Run(args[1..]),
            "--help" or "-h" or "help" => WriteUsage(),
            "--version" => WriteVersion(),
            string other => Fail($"spark: '{other}' is not a command."),
        };
    }

    private static int Run(string[] args)
    {
        string? path = null;
        string? export = null;
        double? tolerance = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--export" when index + 1 < args.Length:
                    export = args[++index];
                    break;

                case "--tolerance" when index + 1 < args.Length:
                    if (!double.TryParse(
                            args[++index],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double parsed)
                        || !double.IsFinite(parsed)
                        || parsed <= 0.0)
                    {
                        return Fail($"spark run: '{args[index]}' is not a positive tolerance.");
                    }

                    tolerance = parsed;
                    break;

                case "--export":
                case "--tolerance":
                    return Fail($"spark run: {args[index]} needs a value.");

                case string option when option.StartsWith('-'):
                    return Fail($"spark run: '{option}' is not an option of run.");

                default:
                    if (path is not null)
                    {
                        return Fail("spark run: takes one file.");
                    }

                    path = args[index];
                    break;
            }
        }

        if (path is null)
        {
            return Fail("spark run: needs a .spark file.");
        }

        if (!File.Exists(path))
        {
            return Fail($"spark run: '{path}' does not exist.");
        }

        // --tolerance is the LINEAR tolerance itself, not a characteristic length. The
        // alternative reads the same and behaves backwards: Tolerance.ForScale(0.01) is a
        // tolerance for a model 0.01 across, which is a HUNDRED TIMES TIGHTER than the default
        // - so somebody asking for a coarser export by typing a bigger number would get a file
        // eight times the size. Measured, not imagined: 79,361 vertices at the default against
        // 649,105 at "--tolerance 0.01" while that was the reading.
        //
        // The angular component and the relative epsilon stay at their defaults, because both
        // are dimensionless. They have to be passed explicitly: a Tolerance with a non-zero
        // linear component reports its angular component as given, so constructing one with
        // 'default' for it yields a tolerance whose angular part is zero degrees.
        Tolerance resolved = tolerance is double linear
            ? new Tolerance(linear, Tolerance.Default.Angular, Tolerance.Default.RelativeEpsilon)
            : Tolerance.Default;

        using SparkSession session = new(resolved);

        GraphDocument document;

        try
        {
            document = SparkFile.Read(File.ReadAllText(path));
        }
        catch (SparkFileException failure)
        {
            // The reader's own diagnostic, not a rephrasing of it. A CLI that paraphrases the
            // component that actually knows what went wrong makes the message unsearchable.
            return Fail($"spark run: {failure.Message}");
        }

        session.Replace(document.Restore(session.Library));

        EvaluationResult result = session.Evaluate();

        foreach (SparkDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Code} {diagnostic.Message}");
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{document.Nodes.Count} nodes, {result.NodesEvaluated} evaluated, "
                + $"{result.CacheHits} from cache."));

        if (export is not null)
        {
            int written = Export(session, result, export, resolved);

            Console.WriteLine(
                string.Create(CultureInfo.InvariantCulture, $"{written} curves and points written to {export}."));
        }

        // Errors are an exit code, not just a message: a run in a script has to fail loudly, and
        // "it printed something red" is not a contract anything can depend on.
        return result.HasErrors ? Failed : Ok;
    }

    private static int Export(SparkSession session, EvaluationResult result, string path, in Tolerance tolerance)
    {
        List<Curve> curves = [];
        List<Point3d> points = [];

        foreach (NodeInstance node in session.Graph.Nodes())
        {
            for (int port = 0; port < node.Definition.Outputs.Count; port++)
            {
                Collect(result.Value(node.Id, port), curves, points);
            }
        }

        ObjWriter.WriteFile(path, curves, points, tolerance);

        return curves.Count + points.Count;
    }

    // The same shape SceneBuilder.Collect has, and deliberately so: a value the viewport draws
    // and a value the exporter writes should be the same set. Anything else is a user asking why
    // the thing on screen is not in the file.
    private static void Collect(object? value, List<Curve> curves, List<Point3d> points)
    {
        switch (value)
        {
            case null:
                return;

            case Displayable displayable:
                Collect(displayable.Geometry, curves, points);
                return;

            case SparkList list:
                foreach (object? item in list)
                {
                    Collect(item, curves, points);
                }

                return;

            case Curve curve:
                curves.Add(curve);
                return;

            case Point3d point:
                points.Add(point);
                return;

            default:
                // Not an error. A graph is full of numbers, planes and booleans, and OBJ has
                // nowhere to put them.
                return;
        }
    }

    private static int WriteUsage()
    {
        Console.WriteLine(
            """
            spark — the Spark command line.

            Usage:
              spark run FILE [--export OUT.obj] [--tolerance N]

            Commands:
              run        Evaluate a .spark graph and report what it did.

            Options for run:
              --export      Write the curves and points the graph produced to a Wavefront OBJ.
              --tolerance   The linear tolerance, in the document's own units. It is both the
                            document tolerance and the chord tolerance curves are tessellated
                            at, so a larger number means a coarser export. Defaults to 1e-6.

            Exit codes:
              0  the graph evaluated with no errors
              1  the graph reported an error, or the file could not be read
              2  the command line was wrong

            check, render, export, pkg, docs and graph are planned and not built. See
            docs/TASKS.md, E12-T5.
            """);

        return Ok;
    }

    private static int WriteVersion()
    {
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

        return Ok;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'spark --help' for usage.");

        return BadUsage;
    }
}

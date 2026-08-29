using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spark.Benchmarks;

/// <summary>
/// Turns a benchmark run into a pass or a fail against the budgets committed in
/// `bench/budgets.jsonc`.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the half of `E8-T15` and `E4-T3` that makes a benchmark a guard.</b> The suites
/// measured; nothing compared the numbers to anything, so nothing could go red. Reading a
/// benchmark report is a thing people do once, on the day they write it.
/// </para>
/// <para>
/// Three kinds of budget, held to three different standards, because they are not equally
/// trustworthy:
/// </para>
/// <list type="number">
/// <item>
/// <b>Allocation ceilings are the real guard.</b> Bytes allocated per operation is deterministic:
/// the same code allocates the same amount on a laptop and on a shared CI runner. These are set
/// close to the measurement, and a few per cent of slack is all they get.
/// </item>
/// <item>
/// <b>Time ceilings catch a step change and nothing finer.</b> A hosted runner is shared,
/// throttled and of unknown vintage, so an absolute nanosecond budget calibrated anywhere else is
/// worth roughly one order of magnitude. They are set to catch <i>an algorithm changed</i> — an
/// accidental O(n²), a cache that stopped hitting — and they will not catch a 20% drift. Saying so
/// is the point: a tight time budget on a hosted runner produces a nightly that fails at random,
/// which is a guard everybody learns to ignore.
/// </item>
/// <item>
/// <b>Ratios are machine-independent, and are the sharpest thing here.</b> Machine speed cancels
/// in a quotient, so <i>warm evaluation costs at most a fifth of cold</i> holds on any hardware and
/// tests the provenance cache's central claim directly. Where a claim can be written as a ratio,
/// it should be.
/// </item>
/// </list>
/// <para>
/// The name matching is a <b>two-way diff</b>, the same rule the reflection importer applies to
/// public members: a budgeted case the run did not produce is a failure, and a measured case with
/// no budget is a failure. A guard that quietly stops covering something is the failure mode this
/// project has already been bitten by three times.
/// </para>
/// </remarks>
public static class BudgetCheck
{
    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Checks a completed run against the budgets.
    /// </summary>
    /// <param name="args">
    /// `--budgets PATH` (default `bench/budgets.jsonc`), `--results DIR` (default
    /// `BenchmarkDotNet.Artifacts/results`), `--canvas PATH` for the log of a `--canvas-benchmark`
    /// run, and `--no-canvas` to state that this run deliberately did not measure the canvas.
    /// </param>
    /// <returns>Zero when every budget holds, one otherwise.</returns>
    public static int Run(string[] args)
    {
        string budgetsPath = Path.Combine("bench", "budgets.jsonc");
        string resultsDirectory = Path.Combine("BenchmarkDotNet.Artifacts", "results");
        string? canvasLog = null;
        bool canvasSkipped = false;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--budgets" when index + 1 < args.Length:
                    budgetsPath = args[++index];
                    break;

                case "--results" when index + 1 < args.Length:
                    resultsDirectory = args[++index];
                    break;

                case "--canvas" when index + 1 < args.Length:
                    canvasLog = args[++index];
                    break;

                case "--no-canvas":
                    canvasSkipped = true;
                    break;

                default:
                    Console.Error.WriteLine($"::error::Unrecognised argument '{args[index]}'.");
                    return 1;
            }
        }

        if (!File.Exists(budgetsPath))
        {
            Console.Error.WriteLine($"::error::No budgets file at '{budgetsPath}'.");
            return 1;
        }

        using JsonDocument budgets = JsonDocument.Parse(File.ReadAllText(budgetsPath), ReaderOptions);
        JsonElement root = budgets.RootElement;

        List<string> failures = [];
        Dictionary<string, Measurement> measured = ReadMeasurements(resultsDirectory, failures);

        Console.WriteLine($"==> Budgets  {Path.GetFullPath(budgetsPath)}");
        Console.WriteLine($"==> Results  {Path.GetFullPath(resultsDirectory)} — {measured.Count} case(s)");
        Console.WriteLine();

        // A run with no reports at all fails on that, and only on that. Letting it fall through
        // would print one budgeted-but-not-measured failure per case and bury the single fact
        // somebody actually needs under thirty derived ones.
        if (failures.Count > 0 && measured.Count == 0)
        {
            Console.WriteLine("Nothing was measured, so nothing could be checked.");
            return 1;
        }

        CheckCases(root, measured, failures);
        CheckRatios(root, measured, failures);
        CheckCanvas(root, canvasLog, canvasSkipped, failures);

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("Every budget holds.");
            return 0;
        }

        Console.WriteLine($"{failures.Count} budget(s) broken:");
        foreach (string failure in failures)
        {
            Console.WriteLine($"  {failure}");
        }

        return 1;
    }

    /// <summary>
    /// Reads every BenchmarkDotNet JSON report under a directory.
    /// </summary>
    /// <remarks>
    /// An empty directory is a failure rather than a vacuous pass. A run that measured nothing and
    /// a run in which everything held are indistinguishable from an exit code alone, and the first
    /// is by far the more likely of the two to happen without anybody noticing.
    /// </remarks>
    /// <param name="directory">The BenchmarkDotNet artifacts results directory.</param>
    /// <param name="failures">Collects what went wrong.</param>
    /// <returns>Every case the run produced, keyed by its full name.</returns>
    private static Dictionary<string, Measurement> ReadMeasurements(string directory, List<string> failures)
    {
        Dictionary<string, Measurement> measurements = [];

        if (!Directory.Exists(directory))
        {
            Report(failures, $"No results directory at '{directory}'. Did the benchmark run get that far?");
            return measurements;
        }

        string[] reports = Directory.GetFiles(
            directory, "*-report-full-compressed.json", SearchOption.AllDirectories);

        if (reports.Length == 0)
        {
            Report(failures, $"No BenchmarkDotNet JSON reports under '{directory}'. Was --exporters json passed?");
            return measurements;
        }

        foreach (string report in reports)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(report), ReaderOptions);
            if (!document.RootElement.TryGetProperty("Benchmarks", out JsonElement cases))
            {
                Report(failures, $"'{report}' carries no Benchmarks array.");
                continue;
            }

            foreach (JsonElement item in cases.EnumerateArray())
            {
                string? name = item.TryGetProperty("FullName", out JsonElement fullName)
                    ? fullName.GetString()
                    : null;

                if (name is null)
                {
                    continue;
                }

                double mean = item.TryGetProperty("Statistics", out JsonElement statistics)
                    && statistics.ValueKind == JsonValueKind.Object
                    && statistics.TryGetProperty("Mean", out JsonElement meanValue)
                        ? meanValue.GetDouble()
                        : double.NaN;

                long? allocated = item.TryGetProperty("Memory", out JsonElement memory)
                    && memory.ValueKind == JsonValueKind.Object
                    && memory.TryGetProperty("BytesAllocatedPerOperation", out JsonElement bytes)
                    && bytes.ValueKind == JsonValueKind.Number
                        ? bytes.GetInt64()
                        : null;

                measurements[name] = new Measurement(name, mean, allocated);
            }
        }

        return measurements;
    }

    /// <summary>Checks the per-case ceilings, in both directions.</summary>
    /// <param name="root">The budgets document.</param>
    /// <param name="measured">What the run produced.</param>
    /// <param name="failures">Collects what went wrong.</param>
    private static void CheckCases(
        JsonElement root, Dictionary<string, Measurement> measured, List<string> failures)
    {
        if (!root.TryGetProperty("cases", out JsonElement cases) || cases.ValueKind != JsonValueKind.Object)
        {
            Report(failures, "The budgets file carries no 'cases' object.");
            return;
        }

        HashSet<string> budgeted = [];

        foreach (JsonProperty budget in cases.EnumerateObject())
        {
            budgeted.Add(budget.Name);

            if (!measured.TryGetValue(budget.Name, out Measurement? measurement))
            {
                Report(failures, $"{budget.Name}: budgeted, and this run did not produce it.");
                continue;
            }

            if (double.IsNaN(measurement.MeanNanoseconds))
            {
                Report(failures, $"{budget.Name}: the run produced no statistics for it, so it did not complete.");
                continue;
            }

            if (budget.Value.TryGetProperty("maxMeanNs", out JsonElement maxMean))
            {
                double ceiling = maxMean.GetDouble();
                bool held = measurement.MeanNanoseconds <= ceiling;
                Line(held, budget.Name, $"mean {Time(measurement.MeanNanoseconds)} of {Time(ceiling)}");
                if (!held)
                {
                    Report(
                        failures,
                        $"{budget.Name}: mean {Time(measurement.MeanNanoseconds)}, over its {Time(ceiling)} ceiling.");
                }
            }

            if (budget.Value.TryGetProperty("maxAllocatedBytes", out JsonElement maxAllocated))
            {
                long ceiling = maxAllocated.GetInt64();
                if (measurement.AllocatedBytes is not long allocated)
                {
                    Report(
                        failures,
                        $"{budget.Name}: has an allocation budget, and the run measured no allocation. "
                        + "Is [MemoryDiagnoser] still on the suite?");
                    continue;
                }

                bool held = allocated <= ceiling;
                Line(held, budget.Name, $"allocated {Bytes(allocated)} of {Bytes(ceiling)}");
                if (!held)
                {
                    Report(failures, $"{budget.Name}: allocates {Bytes(allocated)}, over its {Bytes(ceiling)} ceiling.");
                }
            }
        }

        // The other direction. A new benchmark with no budget is covered by nothing, and the only
        // moment anybody is going to notice is this one.
        foreach (string name in measured.Keys.Where(name => !budgeted.Contains(name)).Order(StringComparer.Ordinal))
        {
            Report(failures, $"{name}: measured, and no budget names it. Add one.");
        }
    }

    /// <summary>Checks the ratios, which are the budgets that survive a change of machine.</summary>
    /// <param name="root">The budgets document.</param>
    /// <param name="measured">What the run produced.</param>
    /// <param name="failures">Collects what went wrong.</param>
    private static void CheckRatios(
        JsonElement root, Dictionary<string, Measurement> measured, List<string> failures)
    {
        if (!root.TryGetProperty("ratios", out JsonElement ratios) || ratios.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement ratio in ratios.EnumerateArray())
        {
            string name = ratio.GetProperty("name").GetString() ?? "(unnamed)";
            string numerator = ratio.GetProperty("numerator").GetString() ?? string.Empty;
            string denominator = ratio.GetProperty("denominator").GetString() ?? string.Empty;
            double ceiling = ratio.GetProperty("maxRatio").GetDouble();

            if (!measured.TryGetValue(numerator, out Measurement? top)
                || !measured.TryGetValue(denominator, out Measurement? bottom))
            {
                Report(failures, $"ratio '{name}': one of its two cases was not measured.");
                continue;
            }

            if (double.IsNaN(top.MeanNanoseconds) || bottom.MeanNanoseconds <= 0)
            {
                Report(failures, $"ratio '{name}': this run's numbers cannot produce it.");
                continue;
            }

            double value = top.MeanNanoseconds / bottom.MeanNanoseconds;
            bool held = value <= ceiling;
            Line(held, $"ratio: {name}", $"{value:F3} of {ceiling:F3}");
            if (!held)
            {
                Report(failures, $"ratio '{name}': {value:F3}, over its {ceiling:F3} ceiling.");
            }
        }
    }

    /// <summary>
    /// Checks the application's own canvas benchmark, from the log of a `--canvas-benchmark` run.
    /// </summary>
    /// <remarks>
    /// This one is driven through a real window and a real compositor, which is why it cannot live
    /// in the BenchmarkDotNet suites and why its numbers are the softest here. The frame and node
    /// counts are checked as well as the times, because a run that did a tenth of its frames over
    /// an empty graph would otherwise report an excellent median.
    /// </remarks>
    /// <param name="root">The budgets document.</param>
    /// <param name="logPath">The captured output of the run, or null.</param>
    /// <param name="skipped">Whether this run deliberately did not measure the canvas.</param>
    /// <param name="failures">Collects what went wrong.</param>
    private static void CheckCanvas(JsonElement root, string? logPath, bool skipped, List<string> failures)
    {
        if (!root.TryGetProperty("canvas", out JsonElement canvas) || canvas.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (logPath is null)
        {
            if (skipped)
            {
                Console.WriteLine($"skip {"canvas",-58} not measured in this run (--no-canvas)");
                return;
            }

            Report(
                failures,
                "canvas: budgeted, and no --canvas log was given. Pass --no-canvas to say so deliberately.");
            return;
        }

        if (!File.Exists(logPath))
        {
            Report(failures, $"canvas: no log at '{logPath}'.");
            return;
        }

        string log = File.ReadAllText(logPath);
        Match header = Regex.Match(log, @"canvas-benchmark nodes=(\d+) wires=(\d+) frames=(\d+)");
        Match render = Regex.Match(log, @"render pass:\s*([0-9.]+) ms median,\s*([0-9.]+) ms p95");

        if (!header.Success || !render.Success)
        {
            Report(
                failures,
                $"canvas: '{logPath}' holds no canvas-benchmark result. The application printed nothing "
                + "this check recognises, which usually means it never opened a window.");
            return;
        }

        double nodes = double.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
        double frames = double.Parse(header.Groups[3].Value, CultureInfo.InvariantCulture);
        double median = double.Parse(render.Groups[1].Value, CultureInfo.InvariantCulture);
        double p95 = double.Parse(render.Groups[2].Value, CultureInfo.InvariantCulture);

        CheckAtLeast(canvas, "minNodes", nodes, "canvas: nodes", failures);
        CheckAtLeast(canvas, "minFrames", frames, "canvas: frames", failures);
        CheckAtMost(canvas, "maxMedianMs", median, "canvas: median", failures);
        CheckAtMost(canvas, "maxP95Ms", p95, "canvas: p95", failures);
    }

    /// <summary>Checks one floor, when the budgets name it.</summary>
    private static void CheckAtLeast(
        JsonElement canvas, string key, double value, string label, List<string> failures)
    {
        if (!canvas.TryGetProperty(key, out JsonElement floor))
        {
            return;
        }

        double required = floor.GetDouble();
        bool held = value >= required;
        Line(held, label, $"{value:F0}, at least {required:F0}");
        if (!held)
        {
            Report(
                failures,
                $"{label}: {value:F0}, below the {required:F0} this benchmark is only meaningful above.");
        }
    }

    /// <summary>Checks one millisecond ceiling, when the budgets name it.</summary>
    private static void CheckAtMost(
        JsonElement canvas, string key, double value, string label, List<string> failures)
    {
        if (!canvas.TryGetProperty(key, out JsonElement ceiling))
        {
            return;
        }

        double allowed = ceiling.GetDouble();
        bool held = value <= allowed;
        Line(held, label, $"{value:F2} ms of {allowed:F2} ms");
        if (!held)
        {
            Report(failures, $"{label}: {value:F2} ms, over its {allowed:F2} ms ceiling.");
        }
    }

    /// <summary>One line of the report, so a passing run is readable rather than silent.</summary>
    private static void Line(bool held, string label, string detail) =>
        Console.WriteLine($"{(held ? "ok  " : "FAIL")} {label,-58} {detail}");

    /// <summary>Records a failure, on the console and as a CI annotation.</summary>
    private static void Report(List<string> failures, string message)
    {
        failures.Add(message);
        Console.WriteLine($"FAIL {message}");
        Console.Error.WriteLine($"::error::{message}");
    }

    /// <summary>Nanoseconds in whatever unit reads best.</summary>
    private static string Time(double nanoseconds) => nanoseconds switch
    {
        >= 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{nanoseconds / 1_000_000:F3} ms"),
        >= 1_000 => string.Create(CultureInfo.InvariantCulture, $"{nanoseconds / 1_000:F3} us"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{nanoseconds:F1} ns"),
    };

    /// <summary>Bytes in whatever unit reads best. 1 KiB is 1024 B, as BenchmarkDotNet reports it.</summary>
    private static string Bytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):F2} MiB"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F2} KiB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
    };

    /// <summary>One case's numbers, reduced to what a budget can be written against.</summary>
    /// <param name="FullName">BenchmarkDotNet's own full name, parameters included.</param>
    /// <param name="MeanNanoseconds">The arithmetic mean, in nanoseconds.</param>
    /// <param name="AllocatedBytes">Managed bytes per operation, when the suite measured them.</param>
    private sealed record Measurement(string FullName, double MeanNanoseconds, long? AllocatedBytes);
}

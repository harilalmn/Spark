using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Running;

namespace Spark.Benchmarks;

/// <summary>
/// The benchmark host.
/// </summary>
/// <remarks>
/// <para>
/// Run everything with `dotnet run --project bench/Spark.Benchmarks --configuration Release`, or
/// one suite with `--filter *SceneIndex*`. Release is not optional and BenchmarkDotNet will say so:
/// an unoptimised measurement is not a slower measurement, it is a different program.
/// </para>
/// <para>
/// <b>The `check` verb is what makes these numbers a guard rather than a report.</b>
/// `dotnet run --project bench/Spark.Benchmarks --configuration Release -- check` compares a
/// finished run against `bench/budgets.jsonc` and exits non-zero when a budget is broken — see
/// <see cref="BudgetCheck"/> for what is and is not worth budgeting. The nightly workflow
/// (`.github/workflows/nightly.yml`) runs the suites and then the check; `E1-T13` was the harness
/// and `E8-T15` is the schedule.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Runs the benchmarks named on the command line, or checks a finished run.</summary>
    /// <param name="args">
    /// `check ...` to check a run against the budgets, `tessellate ...` to measure what the
    /// viewport asks the kernel to tessellate to; otherwise BenchmarkDotNet's own switches,
    /// chiefly `--filter`.
    /// </param>
    /// <returns>Zero on success, one when a benchmark could not run or a budget is broken.</returns>
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "check")
        {
            return BudgetCheck.Run([.. args.Skip(1)]);
        }

        // `tessellate` measures what the viewport asks the kernel for. A verb rather than a
        // BenchmarkDotNet case because it needs a provider, and a case would run without one,
        // measure a failed operation and report an excellent time.
        if (args.Length > 0 && args[0] == "tessellate")
        {
            return TessellationMeasurement.Run([.. args.Skip(1)]);
        }

        // The exit code matters here for the same reason the check exists: a nightly whose
        // benchmarks failed to build would otherwise be green, and a green nightly that measured
        // nothing is worse than no nightly at all.
        return BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly())
            .Run(args)
            .Any(summary => summary.HasCriticalValidationErrors)
                ? 1
                : 0;
    }
}

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
/// <b>Nothing here runs in CI yet</b>, and the register says so rather than implying otherwise
/// (`E1-T13` is the harness; `E8-T15` is the schedule that makes it a guard). A benchmark nobody
/// runs is a file, not a gate.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Runs the benchmarks named on the command line, or all of them.</summary>
    /// <param name="args">BenchmarkDotNet's own switches, chiefly `--filter`.</param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}

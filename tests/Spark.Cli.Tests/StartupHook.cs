using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;

/// <summary>
/// Reports the assemblies a <i>child</i> process loaded, so that <c>E6-T14</c>'s promise — a graph
/// containing no script nodes never loads <c>Spark.Scripting</c> — can be asserted rather than
/// believed.
/// </summary>
/// <remarks>
/// <para>
/// <b>In the global namespace, named exactly this, with exactly this signature, because the
/// runtime requires all three.</b> <c>DOTNET_STARTUP_HOOKS</c> names an assembly by path; the
/// runtime loads it into the default context before <c>Main</c> and calls
/// <c>StartupHook.Initialize()</c>. Nothing in this test assembly calls it, and nothing should:
/// it runs only in a process started with that variable set, which is
/// <see cref="Spark.Cli.Tests.ScriptingResidencyTests"/> and nothing else.
/// </para>
/// <para>
/// <b>This lives in the test assembly rather than in a project of its own</b> so that the thing
/// under test needs no hook, no flag and no diagnostic verb of its own. A production switch that
/// prints the loaded-assembly list would be a second thing to keep true; the runtime already has
/// one, and it costs the shipping <c>spark.exe</c> nothing.
/// </para>
/// </remarks>
internal sealed class StartupHook
{
    /// <summary>
    /// Arranges for the loaded-assembly list to be written to the file named by
    /// <c>SPARK_LOADED_ASSEMBLIES</c> when the process exits.
    /// </summary>
    /// <remarks>
    /// Written at exit rather than as each assembly arrives: the question is what the process
    /// loaded over its whole life, and one write is one thing that can fail. The hook touches
    /// nothing but the base class library, so loading it into the child cannot itself pull in an
    /// assembly the test is about to look for.
    /// </remarks>
    public static void Initialize()
    {
        string? destination = Environment.GetEnvironmentVariable("SPARK_LOADED_ASSEMBLIES");

        if (string.IsNullOrEmpty(destination))
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            IEnumerable<string> names = AssemblyLoadContext.Default.Assemblies
                .Select(assembly => assembly.GetName().Name ?? string.Empty)
                .Where(name => name.Length > 0)
                .Order();

            File.WriteAllLines(destination, names);
        };
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spark.Api;
using Spark.Engine;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// Shared scaffolding. Every compile in this suite runs against a fresh, memory-only compile cache
/// so that one test cannot see another's assembly and the developer's real
/// <c>%LOCALAPPDATA%</c> cache is never touched.
/// </summary>
internal static class CodeBlockTestHarness
{
    /// <summary>Options with an isolated, memory-only cache.</summary>
    internal static CodeBlockOptions Options(
        IReadOnlyDictionary<string, Type>? connected = null,
        TimeSpan? budget = null,
        ScriptCompilationCache? cache = null) =>
        new()
        {
            ConnectedInputTypes = connected,
            Cache = cache ?? new ScriptCompilationCache(string.Empty),
            TimeBudget = budget ?? TimeSpan.FromSeconds(10),
        };

    /// <summary>
    /// The connected-type map for a set of ports all carrying one type.
    /// </summary>
    /// <remarks>
    /// Most of these tests wire their ports rather than leaving them unconnected, because an
    /// unconnected port is typed <see cref="object"/> and <c>a + b</c> over two objects is not
    /// arithmetic — which is the whole reason injecting the upstream type matters.
    /// </remarks>
    internal static Dictionary<string, Type> Wired(Type type, params string[] names)
    {
        Dictionary<string, Type> map = new(StringComparer.Ordinal);

        foreach (string name in names)
        {
            map[name] = type;
        }

        return map;
    }

    /// <summary>The connected-type map for a set of ports all carrying <see cref="double"/>.</summary>
    internal static Dictionary<string, Type> Doubles(params string[] names) => Wired(typeof(double), names);

    /// <summary>
    /// Runs work on another thread and fails if it does not finish in time.
    /// </summary>
    /// <remarks>
    /// A guard test whose guard does not work would otherwise hang the whole run and report nothing.
    /// This turns that into a named failure. The runaway thread is left behind deliberately: there is
    /// no way to stop it, which is the entire reason the guards exist.
    /// </remarks>
    internal static T RunWithHardTimeout<T>(Func<T> work, int milliseconds = 30_000)
    {
        Task<T> task = Task.Run(work);

        Assert.True(
            task.Wait(milliseconds),
            $"The work did not finish within {milliseconds} ms. Something that should have been stopped was not.");

        return task.Result;
    }

    /// <summary>
    /// Emits a small assembly to disk, standing in for a node library a user built themselves.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="assemblyName">The assembly name, which is what the catalog deduplicates on.</param>
    /// <param name="source">The C# to compile.</param>
    internal static void EmitLibrary(string path, string assemblyName, string source)
    {
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                assemblyName,
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
                ReferenceCatalog.Default.References,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(path);

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics));
    }

    /// <summary>A node that produces one fixed value, for feeding a code block in a graph.</summary>
    internal static NodeDefinition Constant(string name, Type type, object? value) =>
        new(
            new NodeKey("Spark.Scripting.Tests", name),
            name,
            [],
            [PortDefinition.Inferred("value", type)],
            _ => [value],
            LacingMode.Longest);

    /// <summary>Renders every diagnostic, so an assertion failure says what actually went wrong.</summary>
    internal static string Report(CodeBlockCompilation compilation) =>
        compilation.Diagnostics.Count == 0
            ? "(no diagnostics)"
            : string.Join(Environment.NewLine, compilation.Diagnostics);

    /// <summary>The names of a port list, in port order.</summary>
    internal static string[] NamesOf(IReadOnlyList<PortDefinition> ports)
    {
        string[] names = new string[ports.Count];
        for (int index = 0; index < ports.Count; index++)
        {
            names[index] = ports[index].Name;
        }

        return names;
    }
}

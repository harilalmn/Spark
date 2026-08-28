using System;
using System.IO;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// The compile cache is what makes a code block usable rather than merely possible: Roslyn's cold
/// start is real, and a slider feeding a code block must not pay it on every tick.
/// </summary>
public sealed class CompilationCacheTests
{
    [Fact]
    public void IdenticalTextCompilesOnceHoweverManyNodesContainIt()
    {
        ScriptCompilationCache cache = new(string.Empty);
        CodeBlockOptions options = CodeBlockTestHarness.Options(cache: cache);

        for (int index = 0; index < 10; index++)
        {
            CodeBlockCompilation compilation = CodeBlockCompiler.Compile("21 * 2", options);
            Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));
        }

        ScriptCacheStatistics statistics = cache.Statistics;

        Assert.Equal(1, statistics.Compilations);
        Assert.Equal(9, statistics.ResidentHits);
    }

    /// <summary>
    /// Different input values must not invalidate anything — that is the whole point of the resident
    /// level, and it is why dragging a slider into a code block feels live.
    /// </summary>
    [Fact]
    public void DifferentInputValuesReuseOneCompilation()
    {
        ScriptCompilationCache cache = new(string.Empty);
        CodeBlockOptions options = CodeBlockTestHarness.Options(
            CodeBlockTestHarness.Doubles("x"), cache: cache);

        CodeBlockCompilation compilation = CodeBlockCompiler.Compile("x * 2", options);
        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        for (double value = 0; value < 50; value++)
        {
            Assert.Equal(value * 2, compilation.Definition!.Invoke([value])[0]);
        }

        Assert.Equal(1, cache.Statistics.Compilations);
    }

    [Fact]
    public void EditingTheTextCompilesAgain()
    {
        ScriptCompilationCache cache = new(string.Empty);
        CodeBlockOptions options = CodeBlockTestHarness.Options(cache: cache);

        _ = CodeBlockCompiler.Compile("1 + 1", options);
        _ = CodeBlockCompiler.Compile("1 + 2", options);

        Assert.Equal(2, cache.Statistics.Compilations);
    }

    /// <summary>
    /// The connected types are part of the key, because they change the generated source. Two nodes
    /// with the same text but different wires are genuinely different programs.
    /// </summary>
    [Fact]
    public void ChangingAConnectedTypeCompilesAgain()
    {
        ScriptCompilationCache cache = new(string.Empty);

        _ = CodeBlockCompiler.Compile("value", CodeBlockTestHarness.Options(cache: cache));
        _ = CodeBlockCompiler.Compile(
            "value", CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("value"), cache: cache));

        Assert.Equal(2, cache.Statistics.Compilations);
    }

    /// <summary>
    /// The persistent level. A second session with a cold in-memory cache reads the assembly off the
    /// disk instead of compiling it again.
    /// </summary>
    [Fact]
    public void ASecondSessionLoadsTheAssemblyFromDiskRatherThanCompiling()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "spark-scripting-tests", Guid.NewGuid().ToString("N"));

        try
        {
            ScriptCompilationCache first = new(directory);
            CodeBlockCompilation compiled = CodeBlockCompiler.Compile(
                "(doubled: v * 2, halved: v / 2)",
                CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("v"), cache: first));

            Assert.True(compiled.Success, CodeBlockTestHarness.Report(compiled));
            Assert.Equal(1, first.Statistics.Compilations);
            Assert.False(compiled.FromCache);

            ScriptCompilationCache second = new(directory);
            CodeBlockCompilation reloaded = CodeBlockCompiler.Compile(
                "(doubled: v * 2, halved: v / 2)",
                CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("v"), cache: second));

            Assert.True(reloaded.Success, CodeBlockTestHarness.Report(reloaded));
            Assert.True(reloaded.FromCache);
            Assert.Equal(0, second.Statistics.Compilations);
            Assert.Equal(1, second.Statistics.DiskHits);

            // The ports have to survive the round trip through the metadata sidecar, or the node
            // would come back with the right code and the wrong shape.
            Assert.Equal(["doubled", "halved"], CodeBlockTestHarness.NamesOf(reloaded.Outputs));
            Assert.Equal(typeof(double), reloaded.Inputs[0].ValueType);
            Assert.Equal(8.0, reloaded.Definition!.Invoke([4.0])[0]);

            first.Clear();
            second.Clear();
        }
        finally
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Editing a code block must invalidate results the engine cached for the old text, which it does
    /// through the definition's version rather than through its key.
    /// </summary>
    [Fact]
    public void TheDefinitionVersionChangesWithTheText()
    {
        ScriptCompilationCache cache = new(string.Empty);
        CodeBlockOptions options = CodeBlockTestHarness.Options(cache: cache);

        CodeBlockCompilation before = CodeBlockCompiler.Compile("1 + 1", options);
        CodeBlockCompilation after = CodeBlockCompiler.Compile("1 + 2", options);

        Assert.NotEqual(before.Definition!.Version, after.Definition!.Version);
        Assert.Equal(before.Definition.Key, after.Definition.Key);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.UI.Tests;

/// <summary>
/// The on-disk compile cache and the source map — `E6-T10` and `E6-T1`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cache is tested across two factories</b>, because one factory would answer from the
/// resident cache and prove nothing. Two factories over the same directory is exactly the shape of
/// the case the row exists for: closing Spark and opening the graph again.
/// </para>
/// <para>
/// <b>The source map is tested by counting</b> — the reported line for a deliberate error on the
/// third line of a script is 3 — because the alternative, asserting a whole message, would pin
/// Roslyn's wording and go red on an upgrade that changed nothing.
/// </para>
/// </remarks>
public sealed class ScriptCacheAndSourceMapTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "spark-script-cache-" + Guid.NewGuid().ToString("N"));

    private ScriptNodeFactory Factory()
    {
        _ = typeof(Point3d).Assembly.Location;

        return new ScriptNodeFactory(
            new ReferenceCatalog(), new GuardWeaver(), new ScriptAssemblyCache(_directory));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked assembly file is not this test's problem; the directory is under the
            // temporary path and the operating system will take it.
        }
    }

    /// <summary>Compiling a script leaves an assembly and its port names behind.</summary>
    [Fact]
    public void CompilingWritesAnEntry()
    {
        Factory().Create("return radius * 2;");

        Assert.Single(Directory.GetFiles(_directory, "*.dll"));
        Assert.Single(Directory.GetFiles(_directory, "*.ports"));
    }

    /// <summary>
    /// <b>A second process gets the ports without compiling.</b> The input names are the one thing
    /// that took a compilation to learn, and reading them back is what makes the cache worth
    /// having — an entry that still had to run the inference pass would save half the cost.
    /// </summary>
    [Fact]
    public void ASecondFactoryReadsTheEntryBack()
    {
        const string Script = "return width * height;";

        NodeDefinitionSource first = Factory().Create(Script);
        NodeDefinitionSource second = Factory().Create(Script);

        Assert.Equal(["width", "height"], second.Inputs.Select(port => port.Name));
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(12.0, Assert.Single(second.Invoke([3.0, 4.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>The same source with different input types is a different entry.</b> The types are in
    /// the key, so a cached assembly cannot be read back under types it was not compiled for —
    /// which would give a script compiled against <c>dynamic</c> to a node whose ports are typed.
    /// </summary>
    [Fact]
    public void InputTypesChangeTheEntry()
    {
        ScriptNodeFactory factory = Factory();

        factory.Create("return a;");
        factory.Create("return a;", new Dictionary<string, Type> { ["a"] = typeof(double) });

        Assert.Equal(2, Directory.GetFiles(_directory, "*.dll").Length);
    }

    /// <summary>
    /// A cache with nowhere to write still runs scripts. A host in a sandbox, or with a read-only
    /// profile, loses a faster start and nothing else.
    /// </summary>
    [Fact]
    public void ADisabledCacheStillCompiles()
    {
        ScriptNodeFactory factory = new(
            new ReferenceCatalog(), new GuardWeaver(), new ScriptAssemblyCache(directory: null));

        Assert.Equal(6.0, Assert.Single(factory.Create("return a * 2;").Invoke([3.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>A corrupt entry is a miss, not a crash.</b> A half-written file from a process that was
    /// killed, or an assembly from a build that generated a different frame, both arrive here.
    /// </summary>
    [Fact]
    public void ACorruptEntryFallsBackToCompiling()
    {
        const string Script = "return a + 1;";

        Factory().Create(Script);

        foreach (string entry in Directory.GetFiles(_directory, "*.dll"))
        {
            File.WriteAllBytes(entry, [0x4D, 0x5A, 0x00, 0x00]);
        }

        Assert.Equal(4.0, Assert.Single(Factory().Create(Script).Invoke([3.0], CancellationToken.None)));
    }

    /// <summary>
    /// An assembly without its ports file is a miss. The ports file is written second, so its
    /// absence is what says the pair was never completed.
    /// </summary>
    [Fact]
    public void AnEntryWithNoPortsFileIsAMiss()
    {
        const string Script = "return a - 1;";

        Factory().Create(Script);

        foreach (string ports in Directory.GetFiles(_directory, "*.ports"))
        {
            File.Delete(ports);
        }

        Assert.Equal(2.0, Assert.Single(Factory().Create(Script).Invoke([3.0], CancellationToken.None)));
    }

    /// <summary>
    /// The fingerprint is what makes the key mean the same thing tomorrow, and it moves when the
    /// references do — unlike the version counter, which starts at zero in every process.
    /// </summary>
    [Fact]
    public void TheFingerprintMovesWithTheReferences()
    {
        ReferenceCatalog catalogue = new();
        string before = catalogue.Fingerprint;

        Assert.Equal(before, new ReferenceCatalog().Fingerprint);

        // A *copy* under a new path, because the catalogue is built from what this process has
        // already loaded - adding an assembly that is already in it changes nothing, correctly.
        Directory.CreateDirectory(_directory);
        string copy = Path.Combine(_directory, "Copied.dll");
        File.Copy(typeof(Point3d).Assembly.Location, copy, overwrite: true);

        catalogue.Add([copy]);

        Assert.NotEqual(before, catalogue.Fingerprint);
    }

    /// <summary>
    /// <b>A compile error is reported on the user's line, not the generated one.</b> The script
    /// below is three lines long and the error is on the third; without the map the number reported
    /// is somewhere in the teens and names a line the user has never seen.
    /// </summary>
    [Fact]
    public void ACompileErrorIsReportedOnTheUsersLine()
    {
        NodeDefinitionSource broken = Factory().Create("var a = 1;\nvar b = 2;\nvar c = ;\n");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => broken.Invoke([], CancellationToken.None));

        Assert.Contains("line 3,", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The map is a subtraction, and a position inside the frame maps to nothing.</summary>
    [Theory]
    [InlineData(10, 11, 1)]
    [InlineData(10, 15, 5)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 3, 0)]
    public void TheMapSubtractsThePrelude(int prelude, int generated, int expected) =>
        Assert.Equal(expected, new ScriptSourceMap(prelude).UserLine(generated));

    /// <summary>
    /// A message about a generated line is left unplaced rather than blamed on the user's first
    /// line, which would send them to look at code that is correct.
    /// </summary>
    [Fact]
    public void AMessageInsideTheFrameIsNotPlaced() =>
        Assert.Equal("boom", new ScriptSourceMap(10).Place(4, 1, "boom"));
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Spark.Cli;
using Spark.Engine;
using Spark.Packages;
using Spark.Scripting;

namespace Spark.Cli.Tests;

/// <summary>
/// <c>spark run</c> and <c>spark check</c> read a graph's own package folder, behind the same
/// content-hash consent the window asks for (`E7-T25`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The command line has nobody to ask</b>, so where the window shows a banner, these refuse — and
/// name each assembly and its full hash, which is what the person reading a build log needs in order
/// to decide. <c>--trust-packages</c> agrees for one run and records nothing.
/// </para>
/// <para>
/// <b>Every assembly here is real</b>, compiled by the test into a scratch <c>.packages</c> folder and
/// named uniquely, because whether an assembly is loaded is process-wide state. <b>Every trust record
/// is a scratch file</b>; the user's own is never read or written.
/// </para>
/// </remarks>
public sealed class PackageFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _graph;
    private readonly string _folder;
    private readonly string _trustFile;
    private readonly PackageTrustStore _trust;

    public PackageFolderTests()
    {
        // Geometry has to be loaded before a catalogue is built, or its prelude line does not
        // resolve and every block fails for a reason that has nothing to do with packages.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        _root = Path.Combine(Path.GetTempPath(), "spark-cli-packages", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);

        _graph = Path.Combine(_root, "facade.spark");
        _folder = GraphPackages.FolderFor(_graph);
        _trustFile = Path.Combine(_root, "trusted.json");
        _trust = new PackageTrustStore(_trustFile);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // An assembly a block ran against is mapped, and Windows will not delete a mapped
            // file. The folder is under the temp directory and the next run makes its own.
        }
    }

    /// <summary>
    /// <b>The refusal, which is the row.</b> An assembly nobody agreed to is named with its hash, the
    /// graph is not checked, and nothing was compiled or loaded on the way to saying so.
    /// </summary>
    [Fact]
    public void AnAssemblyNobodyAgreedToIsNamedAndTheGraphIsNotChecked()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(Calling(name));

        StringWriter error = new();

        Assert.Equal(1, Program.Check([_graph], error, _trust));

        string message = error.ToString();

        Assert.Contains(name + ".dll", message, StringComparison.Ordinal);
        Assert.Contains(HashOf(dll), message, StringComparison.Ordinal);
        Assert.Contains("--trust-packages", message, StringComparison.Ordinal);

        // Refused before the graph was built: a block that had been compiled would have reported
        // the missing name, which is the symptom this exists to replace with its cause.
        Assert.DoesNotContain("CS0", message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name == name);
    }

    /// <summary>An assembly the user agreed to in the window is compiled against, silently.</summary>
    [Fact]
    public void AnAgreedAssemblyIsCompiledAgainstAndTheGraphPasses()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(Calling(name));

        _trust.Trust(HashOf(dll));

        StringWriter error = new();

        Assert.Equal(0, Program.Check([_graph], error, _trust));
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>
    /// <b><c>--trust-packages</c> is for this run only.</b> The graph passes, the trust record is not
    /// written, and the next run without the flag is refused exactly as before.
    /// </summary>
    [Fact]
    public void TrustPackagesAgreesForThisRunOnlyAndRecordsNothing()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(Calling(name));

        StringWriter once = new();

        Assert.Equal(0, Program.Check([_graph, "--trust-packages"], once, _trust));
        Assert.Equal(string.Empty, once.ToString());

        Assert.False(File.Exists(_trustFile), "--trust-packages wrote a trust record");
        Assert.False(new PackageTrustStore(_trustFile).IsTrusted(HashOf(dll)));

        StringWriter again = new();

        Assert.Equal(1, Program.Check([_graph], again, new PackageTrustStore(_trustFile)));
        Assert.Contains(name + ".dll", again.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A graph with no code block never consults its folder</b>, because nothing else compiles
    /// against it — which is also what keeps Roslyn unloaded for it (`E6-T14`).
    /// </summary>
    [Fact]
    public void AGraphWithNoCodeBlockIgnoresItsPackageFolder()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(script: null);

        StringWriter error = new();

        Assert.Equal(0, Program.Check([_graph], error, _trust));
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary><c>spark run</c> refuses the same way, and prints no values for a graph it did not run.</summary>
    [Fact]
    public void RunRefusesTheSameWayAndPrintsNoValues()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(Calling(name));

        StringWriter output = new();
        StringWriter error = new();

        Assert.Equal(1, Program.Run([_graph, "--all"], output, error, _trust));

        Assert.Contains(HashOf(dll), error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    /// <summary>
    /// <b>And with the flag, the block really runs against the library</b> — the value printed is
    /// the one only the assembly in the folder can produce.
    /// </summary>
    [Fact]
    public void RunWithTrustPackagesPrintsWhatTheLibraryReturned()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(Calling(name));

        StringWriter output = new();
        StringWriter error = new();

        Assert.Equal(0, Program.Run([_graph, "--all", "--trust-packages"], output, error, _trust));
        Assert.Contains("4217", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A package the file names and the folder lacks is named</b> (`E7-T17`), as a warning:
    /// it is the explanation for the compile error a block using it would report. It does not fail
    /// the default gate, and <c>--strict</c> fails it, as it does every other warning.
    /// </summary>
    [Fact]
    public void APackageTheFileNamesAndTheFolderLacksIsAWarningThatStrictFails()
    {
        SaveGraph("return 7;", [new GraphDocumentPackage(Path.GetFileName(_folder) + "/Missing.dll")]);

        StringWriter lenient = new();
        StringWriter strict = new();

        Assert.Equal(0, Program.Check([_graph], lenient, _trust));
        Assert.Equal(1, Program.Check([_graph, "--strict"], strict, _trust));

        Assert.Contains("warning", lenient.ToString(), StringComparison.Ordinal);
        Assert.Contains("Missing.dll", lenient.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>spark export</c> refuses the same way (`E12-T24`), and writes no file for a graph it did
    /// not build.
    /// </summary>
    [Fact]
    public void ExportRefusesTheSameWayAndWritesNoFile()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 4217));
        SaveGraph(Calling(name));

        string obj = Path.Combine(_root, "out.obj");
        StringWriter error = new();

        Assert.Equal(1, Program.Export(["--open", _graph, "--out", obj], new StringWriter(), error, _trust));

        Assert.Contains(HashOf(dll), error.ToString(), StringComparison.Ordinal);
        Assert.Contains("was not exported", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(obj), "a refused export wrote a file");
    }

    private static string Calling(string name) => $"return {name}.Payload.Run();";

    /// <summary>
    /// Saves a graph holding one code block, or one number node when <paramref name="script"/> is
    /// null, as <c>facade.spark</c>.
    /// </summary>
    /// <remarks>
    /// The block is compiled against a catalogue without the library, exactly as a user's would be
    /// before they had one — what the file keeps is the source, and that is what the verb compiles.
    /// </remarks>
    private void SaveGraph(string? script, IReadOnlyList<GraphDocumentPackage>? packages = null)
    {
        Graph graph = new();

        if (script is null)
        {
            _ = graph.AddNode(Library.Get(new NodeKey("Spark.Nodes.Core", "Number.Value")));
        }
        else
        {
            ScriptNodeFactory factory = new(new ReferenceCatalog());
            _ = graph.AddNode(NodeDefinition.FromScript(factory.Create(script), script));
        }

        File.WriteAllText(_graph, SparkFile.Write(GraphDocument.Capture(graph, packages: packages)));
    }

    private static NodeLibrary Library { get; } = BuildLibrary();

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }

    /// <summary>The hash as <see cref="GraphAssembly.Hash"/> gives it: SHA-256, uppercase hex.</summary>
    private static string HashOf(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Unique() => "Pkg" + Guid.NewGuid().ToString("n");

    private static string Payload(string name, int value) =>
        $$"""
        namespace {{name}}
        {
            public static class Payload
            {
                public static int Run() => {{value}};
            }
        }
        """;

    /// <summary>Compiles a tiny assembly into a folder and returns its path.</summary>
    private static string Compile(string folder, string name, string source)
    {
        Directory.CreateDirectory(folder);

        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [SyntaxFactory.ParseSyntaxTree(source)],
            new ReferenceCatalog().References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        string path = Path.Combine(folder, name + ".dll");
        EmitResult emitted = compilation.Emit(path);

        Assert.True(
            emitted.Success,
            "the test's own library did not compile: "
            + string.Join("; ", emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return path;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Spark.Host;
using Spark.Packages;
using Spark.Scripting;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// A graph's own package folder, consumed behind its trust gate (`E7-T16`,
/// [ADR-0024](../../docs/adr/0024-graph-local-package-folder.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim that matters is a negative one, and it is asserted as a negative.</b> A folder of
/// DLLs beside a graph nobody has agreed to must not be referenced and must not be loaded — so the
/// refusal test checks the catalogue has no path for it, that no assembly of that name is in the
/// process, and that a block naming its type does not compile. <i>A banner appeared</i> would
/// prove only that somebody was asked.
/// </para>
/// <para>
/// <b>Every assembly here is real</b>, compiled by the test into a scratch <c>.packages</c> folder,
/// and each is named uniquely: whether an assembly is loaded is process-wide state, and a name
/// shared between tests would let one test's load pass or fail another's assertion.
/// </para>
/// <para>
/// <b>Every trust record is a scratch file</b> — the user's own is never written. The one test
/// that goes through the startup door reads the real record and writes nothing.
/// </para>
/// </remarks>
public sealed class GraphPackageGateTests : IDisposable
{
    private readonly string _root;
    private readonly string _graph;
    private readonly string _folder;
    private readonly string _trustFile;
    private readonly PackageTrustStore _trust;

    public GraphPackageGateTests()
    {
        // Geometry has to be loaded before a catalogue is built, or its prelude line does not
        // resolve and every block fails for a reason that has nothing to do with packages.
        _ = typeof(Spark.Geometry.Point3d).Assembly.Location;

        _root = Path.Combine(Path.GetTempPath(), "spark-graph-packages", Guid.NewGuid().ToString("n"));
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
    /// <b>The refusal, which is the row.</b> Opening a graph whose folder holds an assembly nobody
    /// agreed to references nothing, loads nothing, and does not even reach for the catalogue.
    /// </summary>
    [Fact]
    public void AFolderNobodyAgreedToIsNeitherReferencedNorLoaded()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));

        ReferenceCatalog catalogue = new();
        bool reached = false;
        GraphPackageGate gate = new(_trust, () =>
        {
            reached = true;
            return catalogue;
        });

        Assert.Equal(0, gate.Open(_graph));

        Assert.False(reached, "the catalogue was reached for a folder holding nothing agreed to");
        Assert.Null(catalogue.PathFor(name));
        Assert.Contains(
            new ScriptNodeFactory(catalogue).Diagnose($"return {name}.Payload.Run();"),
            diagnostic => diagnostic.IsError);

        Assert.True(gate.IsAwaitingConsent);
        Assert.Contains(name + ".dll", gate.Banner, StringComparison.Ordinal);

        // Last, so that it also covers the compile attempt above: trying to compile against a
        // name must not have loaded the assembly that would have answered it.
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name == name);
    }

    /// <summary>Agreeing references it, and a block compiles and runs against it.</summary>
    [Fact]
    public void AgreeingLetsABlockCompileAndRunAgainstIt()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 42));

        ReferenceCatalog catalogue = new();
        GraphPackageGate gate = new(_trust, () => catalogue);
        _ = gate.Open(_graph);

        Assert.Equal(1, gate.Agree());

        Assert.False(gate.IsAwaitingConsent);
        Assert.Null(gate.Banner);
        Assert.Equal(dll, catalogue.PathFor(name), ignoreCase: true);
        Assert.Equal(
            42,
            Assert.Single(new ScriptNodeFactory(catalogue).Create("return Payload.Run();").Invoke([], CancellationToken.None)));
    }

    /// <summary>
    /// <b>The client's rule, first half</b>: an unchanged assembly never asks twice — including in
    /// the next session, which is a new trust store reading the same file.
    /// </summary>
    [Fact]
    public void AnUnchangedAssemblyIsNotAskedAboutTwice()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));

        GraphPackageGate first = new(_trust, () => null);
        _ = first.Open(_graph);
        Assert.Equal(1, first.Agree());

        ReferenceCatalog catalogue = new();
        GraphPackageGate next = new(new PackageTrustStore(_trustFile), () => catalogue);

        Assert.Equal(1, next.Open(_graph));
        Assert.False(next.IsAwaitingConsent);
        Assert.NotNull(catalogue.PathFor(name));
    }

    /// <summary>
    /// <b>The client's rule, second half</b>: a rebuilt assembly is different code, and asks again.
    /// Keyed on the path it would have been agreed to once and loaded whatever later occupied it.
    /// </summary>
    [Fact]
    public void ARebuiltAssemblyIsAskedAboutAgain()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));

        GraphPackageGate first = new(_trust, () => null);
        _ = first.Open(_graph);
        _ = first.Agree();

        _ = Compile(_folder, name, Payload(name, 43));

        GraphPackageGate next = new(new PackageTrustStore(_trustFile), () => null);

        Assert.Equal(0, next.Open(_graph));
        Assert.Equal(name + ".dll", Assert.Single(next.Pending).Name);
    }

    /// <summary>
    /// <b>Two graphs may disagree about a library</b>, which is why each has a folder — and they can
    /// only do that if opening the second lets go of the first, namespaces included.
    /// </summary>
    [Fact]
    public void OpeningAnotherGraphLetsGoOfTheFirstGraphsPackages()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));

        ReferenceCatalog catalogue = new();
        GraphPackageGate gate = new(_trust, () => catalogue);
        _ = gate.Open(_graph);
        _ = gate.Agree();

        Assert.Contains(name, catalogue.Imports);

        _ = gate.Open(Path.Combine(_root, "other.spark"));

        Assert.Empty(gate.Referenced);
        Assert.Null(catalogue.PathFor(name));
        Assert.DoesNotContain(name, catalogue.Imports);
        Assert.DoesNotContain(new ScriptNodeFactory(catalogue).Diagnose("return 1;"), diagnostic => diagnostic.IsError);
    }

    /// <summary>
    /// <b>Releasing goes by folder, not by what the gate itself referenced</b>, because
    /// <i>Add as a library…</i> installs into the same folder and references what it installed.
    /// </summary>
    [Fact]
    public void ReleasingTakesWhatAnInstallReferencedToo()
    {
        ReferenceCatalog catalogue = new();
        GraphPackageGate gate = new(_trust, () => catalogue);
        _ = gate.Open(_graph);

        // What `CommitLibrary` does once its disclosure is answered: install beside the graph and
        // reference the result directly, without going through the gate.
        string name = Unique();
        string installed = Compile(Path.Combine(_folder, name + ".1.0.0", "lib", "net10.0"), name, Payload(name, 7));
        _ = catalogue.Add([installed]);

        _ = gate.Open(Path.Combine(_root, "other.spark"));

        Assert.Null(catalogue.PathFor(name));
    }

    /// <summary>
    /// <b>It fails closed.</b> A file that cannot be read has no hash, so there is nothing to agree
    /// to — agreeing to its path instead is exactly the decision the client ruled out.
    /// </summary>
    [Fact]
    public void AnUnreadableAssemblyStaysWaitingAndCannotBeAgreedTo()
    {
        string name = Unique();
        string dll = Compile(_folder, name, Payload(name, 42));

        using FileStream held = new(dll, FileMode.Open, FileAccess.Read, FileShare.None);

        ReferenceCatalog catalogue = new();
        GraphPackageGate gate = new(_trust, () => catalogue);
        _ = gate.Open(_graph);

        Assert.Equal(string.Empty, Assert.Single(gate.Pending).Hash);
        Assert.Equal(0, gate.Agree());
        Assert.Single(gate.Pending);
        Assert.Equal(0, _trust.Count);
        Assert.Null(catalogue.PathFor(name));
        Assert.Contains("could not be read", gate.Banner, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A graph with no package folder never reaches the catalogue</b>, which is what keeps
    /// <c>E6-T14</c>'s promise — no code blocks, no Roslyn — true for the ordinary graph.
    /// </summary>
    [Fact]
    public void AGraphWithNoFolderNeverReachesTheCatalogue()
    {
        bool reached = false;
        GraphPackageGate gate = new(_trust, () =>
        {
            reached = true;
            return null;
        });

        _ = gate.Open(_graph);
        _ = gate.Open(null);
        _ = gate.Open(Path.Combine(_root, "elsewhere.spark"));
        gate.Release();

        Assert.False(reached);
        Assert.Null(gate.Banner);
    }

    /// <summary>
    /// <b>The same refusal through the window</b>, and the button that lifts it. Replacing the
    /// document afterwards lets the packages go.
    /// </summary>
    [Fact]
    public void TheWindowRefusesUntilAgreedAndLetsGoWhenTheDocumentIsReplaced()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));

        string saved = Saved();

        using MainWindowViewModel model = new();
        model.PackageTrust = _trust;

        Assert.True(model.TryOpenDocument(saved, _graph));
        Assert.NotNull(model.PackageBanner);
        Assert.Empty(model.PackageGate.Referenced);
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name == name);

        Assert.Equal(1, model.AgreeToPackages());
        Assert.Null(model.PackageBanner);
        Assert.Single(model.PackageGate.Referenced);

        Assert.True(model.TryOpenDocument(saved, origin: null));
        Assert.Empty(model.PackageGate.Referenced);
        Assert.Null(model.PackageBanner);
    }

    /// <summary>
    /// <b>The other door.</b> <c>--open</c> builds the document in the constructor rather than
    /// through <c>TryOpenDocument</c>, and a gate on one door only is not a gate.
    /// </summary>
    [Fact]
    public void TheStartupDoorIsGatedToo()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));
        File.WriteAllText(_graph, Saved());

        using MainWindowViewModel model = new(null, _graph);

        Assert.NotNull(model.PackageBanner);
        Assert.Empty(model.PackageGate.Referenced);
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => assembly.GetName().Name == name);
    }

    /// <summary>
    /// <b>Agreeing rebuilds the blocks that were compiled without it.</b> They were built when the
    /// graph opened, against a catalogue that did not have the library, and they hold that failure
    /// until something rebuilds them. Both halves are in the test: the block fails before agreeing,
    /// and runs after — so it goes red if agreeing stops rebuilding.
    /// </summary>
    [Fact]
    public async Task AgreeingRebuildsTheBlocksThatWereCompiledWithoutIt()
    {
        string name = Unique();
        _ = Compile(_folder, name, Payload(name, 42));

        string saved;

        using (MainWindowViewModel author = new())
        {
            int slot = author.PlaceCodeBlock(0, 0);
            author.ShowCodeBlock(author.Graph.Nodes[slot]);
            author.ScriptText = $"return {name}.Payload.Run();";

            Assert.True(author.CommitScriptText(), "the block's text did not reach the node");

            saved = Assert.IsType<string>(author.TrySaveDocument());
        }

        using MainWindowViewModel model = new();
        model.PackageTrust = _trust;

        Assert.True(model.TryOpenDocument(saved, _graph));

        await model.EvaluateAsync();
        Assert.Contains(model.Graph.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));

        Assert.Equal(1, model.AgreeToPackages());

        await model.EvaluateAsync();
        Assert.DoesNotContain(model.Graph.Nodes, node => node.State.HasFlag(CanvasNodeState.Error));
    }

    /// <summary>
    /// <b>One record, one instance.</b> The Packages window and the gate both write the trust file,
    /// and two instances over it would each load it once, miss the other's decisions, and write
    /// their own set back over the other's.
    /// </summary>
    [Fact]
    public void ThePackagesWindowAndTheGateShareOneRecord()
    {
        // Nothing overridden: this is the wiring the application runs with. The constructor builds
        // the Packages window while loading installed packages, before a test could reach the
        // setter - which is why the setter is documented as reaching the gate alone. The record is
        // only read here.
        using MainWindowViewModel model = new();

        Assert.Same(model.PackageTrust, model.Packages().Trust);
    }

    /// <summary>An empty graph, as a file would hold it.</summary>
    private static string Saved()
    {
        using MainWindowViewModel author = new();

        return Assert.IsType<string>(author.TrySaveDocument());
    }

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

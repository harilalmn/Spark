using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The seam a code block reaches the engine through — `E6-T14` and `E6-T16`.
/// </summary>
/// <remarks>
/// <para>
/// A code block's ports depend on what the user typed, so its definition belongs to one node
/// instance and cannot come from a library. That means the file has to carry the source and the
/// definition has to be rebuilt on open — and rebuilding needs Roslyn, which a graph of boxes and
/// circles must never load. <see cref="IScriptNodeFactory"/> is how those two facts coexist: the
/// engine holds the contract, the host supplies an implementation, and a document with no scripts
/// never asks for one.
/// </para>
/// <para>
/// The factory here is a stand-in with no compiler in it, which is the point: everything below is
/// about the seam and none of it waits on Roslyn.
/// </para>
/// </remarks>
public sealed class ScriptNodeSeamTests
{
    private const string Doubling = "return a * 2;";

    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>A code block round-trips through a file, source and all.</summary>
    [Fact]
    public void ACodeBlockSurvivesBeingSavedAndReopened()
    {
        Graph graph = new();
        NodeInstance block = graph.AddNode(
            NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));
        graph.SetLiteral(block.Id, 0, 21.0);

        string text = SparkFile.Write(GraphDocument.Capture(graph));
        Graph reopened = SparkFile.Read(text).Restore(Library, new StubFactory());

        NodeInstance restored = reopened.Node(block.Id);
        Assert.Equal(Doubling, restored.Definition.Script);
        Assert.Equal("CodeBlock", restored.Definition.DisplayName);
        Assert.Single(restored.Definition.Inputs);
    }

    /// <summary>And it still computes what it computed.</summary>
    [Fact]
    public void AReopenedCodeBlockStillEvaluates()
    {
        Graph graph = new();
        NodeInstance block = graph.AddNode(
            NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));
        graph.SetLiteral(block.Id, 0, 21.0);

        Graph reopened = SparkFile.Read(SparkFile.Write(GraphDocument.Capture(graph)))
            .Restore(Library, new StubFactory());

        EvaluationResult result = GraphEvaluator.Evaluate(
            reopened, new EvaluationContext(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(42.0, result.Value(block.Id));
    }

    /// <summary>
    /// <b>`E6-T16`'s trust posture, at the seam.</b> Opening a graph with a code block and no
    /// factory refuses, naming the node — it does not open with the node quietly missing. A Spark
    /// graph is executable code, and a switch that silently dropped the executable parts would be
    /// worse than no switch.
    /// </summary>
    [Fact]
    public void AGraphWithACodeBlockIsRefusedWhenScriptingIsUnavailable()
    {
        Graph graph = new();
        graph.AddNode(NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));

        string text = SparkFile.Write(GraphDocument.Capture(graph));

        SparkFileException failure = Assert.Throws<SparkFileException>(
            () => SparkFile.Read(text).Restore(Library));

        Assert.Contains("code block", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>And a graph without one never asks.</b> This is what lets the scripting assembly stay
    /// unloaded for the overwhelming majority of graphs.
    /// </summary>
    [Fact]
    public void AGraphWithoutACodeBlockOpensWithNoFactoryAtAll()
    {
        Graph graph = new();
        graph.AddNode(Library.ByName("Number.Value"));

        Graph reopened = SparkFile.Read(SparkFile.Write(GraphDocument.Capture(graph)))
            .Restore(Library);

        Assert.Single(reopened.Nodes());
    }

    /// <summary>
    /// <b>A file carrying a script needs a version-3 reader.</b> A version-2 build does not know
    /// the field exists and would open the graph, show an empty code block and write the code away
    /// on the next save.
    /// </summary>
    [Fact]
    public void AGraphWithACodeBlockIsWrittenAsVersionThree()
    {
        Graph graph = new();
        graph.AddNode(NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));

        Assert.Contains(
            "\"formatVersion\": 3",
            SparkFile.Write(GraphDocument.Capture(graph)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And a graph without one is untouched by the new version — still 1, byte for byte, which is
    /// the rule notes established and scripts inherit.
    /// </summary>
    [Fact]
    public void AGraphWithoutACodeBlockIsStillVersionOne()
    {
        Graph graph = new();
        graph.AddNode(Library.ByName("Number.Value"));

        string text = SparkFile.Write(GraphDocument.Capture(graph));

        Assert.Contains("\"formatVersion\": 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("script", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The key carries a hash of the script, so the cache does not confuse two blocks.</b> Two
    /// nodes with different code must not share a cache entry, and two with the same code should —
    /// which is what makes ten copies of a snippet evaluate once.
    /// </summary>
    [Fact]
    public void TwoBlocksWithDifferentCodeHaveDifferentKeys()
    {
        StubFactory factory = new();

        NodeDefinition doubling = NodeDefinition.FromScript(factory.Create("return a * 2;"), "return a * 2;");
        NodeDefinition tripling = NodeDefinition.FromScript(factory.Create("return a * 3;"), "return a * 3;");
        NodeDefinition alsoDoubling = NodeDefinition.FromScript(factory.Create("return a * 2;"), "return a * 2;");

        Assert.NotEqual(doubling.Key, tripling.Key);
        Assert.Equal(doubling.Key, alsoDoubling.Key);
    }

    [Fact]
    public void ACodeBlockIsInTheScriptCategory()
    {
        NodeDefinition block = NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling);

        Assert.Equal(NodeCategories.Script, block.Category);
        Assert.Equal(NodeDefinition.ScriptPackage, block.Key.Package);
    }

    /// <summary>
    /// <b>`E6-T17`: the evaluation's own token is the one the script is handed.</b>
    /// </summary>
    /// <remarks>
    /// Asserted by identity rather than by observing a cancellation, because that is the part that
    /// actually breaks. A seam that fabricated a fresh <see cref="CancellationToken"/> — or passed
    /// <see cref="CancellationToken.None"/>, which is what <c>NodeDefinition.Invoke</c> still does —
    /// would satisfy every test that only checks "something was passed", and would then never
    /// cancel anything.
    /// </remarks>
    [Fact]
    public void AScriptIsInvokedWithTheEvaluationsOwnToken()
    {
        CancellationToken seen = default;
        RecordingFactory factory = new((_, token) =>
        {
            seen = token;
            return [1.0];
        });

        Graph graph = new();
        NodeInstance block = graph.AddNode(
            NodeDefinition.FromScript(factory.Create(Doubling), Doubling));
        graph.SetLiteral(block.Id, 0, 21.0);

        using CancellationTokenSource source = new();
        GraphEvaluator.Evaluate(graph, new EvaluationContext(), source.Token);

        Assert.Equal(source.Token, seen);
        Assert.NotEqual(CancellationToken.None, seen);
    }

    /// <summary>
    /// <b>A script that cancels stops the evaluation, rather than being reported as a node that
    /// failed.</b>
    /// </summary>
    /// <remarks>
    /// This is the half of `E6-T17` that the replicator owns. Its two catch filters already exclude
    /// <see cref="OperationCanceledException"/>, so cancellation propagates — but only while it
    /// arrives <i>bare</i>. Anything that wraps it, and reflective invocation through
    /// <c>MethodInfo.Invoke</c> is exactly such a thing, turns "the user pressed stop" into
    /// "'CodeBlock' failed" and lets the evaluation carry on to the next node.
    /// </remarks>
    [Fact]
    public void AScriptThatObservesCancellationStopsTheEvaluation()
    {
        using CancellationTokenSource source = new();
        RecordingFactory factory = new((_, token) =>
        {
            // The realistic shape: the token is not yet cancelled when the script starts, and
            // becomes so while it is running. This is what the guard weaver's loop checks will do.
            source.Cancel();
            token.ThrowIfCancellationRequested();

            return [1.0];
        });

        Graph graph = new();
        NodeInstance block = graph.AddNode(
            NodeDefinition.FromScript(factory.Create(Doubling), Doubling));
        graph.SetLiteral(block.Id, 0, 21.0);

        Assert.Throws<OperationCanceledException>(
            () => GraphEvaluator.Evaluate(graph, new EvaluationContext(), source.Token));
    }

    /// <summary>
    /// <see cref="NodeDefinition.Call"/> is the same call as <see cref="NodeDefinition.Invoke"/>
    /// for a node that came from a library, and is the only one that carries a token for a script.
    /// </summary>
    [Fact]
    public void OnlyAScriptDefinitionCarriesACancellableInvocation()
    {
        NodeDefinition block = NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling);
        NodeDefinition library = Library.Definitions().First(d => d.Inputs.Count > 0);

        Assert.NotNull(block.InvokeScript);
        Assert.Null(library.InvokeScript);

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => block.Call([21.0], cancelled.Token));
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }

    /// <summary>
    /// A factory with no compiler in it. It understands exactly two scripts, which is enough to
    /// exercise every path through the seam and keeps these tests independent of Roslyn.
    /// </summary>
    /// <summary>A factory whose one script is whatever the test wants it to be.</summary>
    private sealed class RecordingFactory(ScriptInvocation invoke) : IScriptNodeFactory
    {
        public NodeDefinitionSource Create(
            string script,
            System.Collections.Generic.IReadOnlyDictionary<string, Type>? inputTypes = null)
        {
            ArgumentNullException.ThrowIfNull(script);

            return new NodeDefinitionSource(
                "CodeBlock",
                script.GetHashCode(StringComparison.Ordinal).ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                [new ScriptPort("a", typeof(double))],
                [new ScriptPort("result", typeof(double))],
                invoke);
        }
    }

    /// <summary>
    /// <b>A declared input type survives a save and reopen</b> (<c>E6-T11</c>).
    /// </summary>
    /// <remarks>
    /// The declaration is the one thing about a code block that cannot be recovered from its
    /// source — the ports come back from compiling the script, and the wire types come back from
    /// the wires, but a type the user chose for an <i>unwired</i> port exists nowhere else. Losing
    /// it on save would mean the setting worked until you closed the file.
    /// </remarks>
    [Fact]
    public void ADeclaredInputTypeSurvivesBeingSavedAndReopened()
    {
        Graph graph = new();
        NodeInstance block = graph.AddNode(
            NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));

        graph.SetDeclaredInputType(block.Id, "a", typeof(Spark.Geometry.Point3d));

        string text = SparkFile.Write(GraphDocument.Capture(graph));

        Assert.Contains("inputTypes", text, StringComparison.Ordinal);
        Assert.Contains("point", text, StringComparison.Ordinal);

        Graph reopened = SparkFile.Read(text).Restore(Library, new StubFactory());

        Assert.Equal(
            typeof(Spark.Geometry.Point3d),
            reopened.Node(block.Id).DeclaredInputTypes["a"]);
    }

    /// <summary>
    /// <b>A graph that declares nothing writes no <c>inputTypes</c> at all</b>, so every file
    /// written before this existed is still byte-for-byte what this build writes.
    /// </summary>
    /// <remarks>
    /// The same rule <c>frozen</c> follows, and for the same reason: <c>E7-T7</c>'s round trip is
    /// an assertion about every file, not only about files written by the current build.
    /// </remarks>
    [Fact]
    public void AGraphThatDeclaresNothingWritesNoInputTypes()
    {
        Graph graph = new();
        graph.AddNode(NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));

        Assert.DoesNotContain(
            "inputTypes", SparkFile.Write(GraphDocument.Capture(graph)), StringComparison.Ordinal);
    }

    /// <summary>
    /// A token this build does not recognise costs the user that one setting and nothing else. It
    /// is what a file written by a later version of Spark looks like, and refusing to open it would
    /// lose a whole document over a dropdown.
    /// </summary>
    [Fact]
    public void AnUnknownDeclaredTypeTokenIsSkippedRatherThanRefused()
    {
        Graph graph = new();
        NodeInstance block = graph.AddNode(
            NodeDefinition.FromScript(new StubFactory().Create(Doubling), Doubling));

        graph.SetDeclaredInputType(block.Id, "a", typeof(Spark.Geometry.Point3d));

        string text = SparkFile.Write(GraphDocument.Capture(graph))
            .Replace("\"point\"", "\"tesseract\"", StringComparison.Ordinal);

        Graph reopened = SparkFile.Read(text).Restore(Library, new StubFactory());

        Assert.Empty(reopened.Node(block.Id).DeclaredInputTypes);
    }

    private sealed class StubFactory : IScriptNodeFactory
    {
        public NodeDefinitionSource Create(
            string script,
            System.Collections.Generic.IReadOnlyDictionary<string, Type>? inputTypes = null)
        {
            ArgumentNullException.ThrowIfNull(script);

            double factor = script.Contains('3', StringComparison.Ordinal) ? 3.0 : 2.0;

            return new NodeDefinitionSource(
                "CodeBlock",
                script.GetHashCode(StringComparison.Ordinal).ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                [new ScriptPort("a", typeof(double))],
                [new ScriptPort("result", typeof(double))],
                (arguments, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return [Convert.ToDouble(arguments[0], System.Globalization.CultureInfo.InvariantCulture) * factor];
                });
        }
    }
}

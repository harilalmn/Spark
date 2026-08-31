using System;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Freezing a node skips it, and everything downstream says why (<c>E7-T14</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frozen and not-evaluated are different states, and that is the feature.</b> Not evaluated
/// means something upstream produced nothing; frozen means the user asked for this. A canvas that
/// greyed both the same way would make a deliberate act look like a fault, and the user would go
/// looking for a bug they created on purpose.
/// </para>
/// <para>
/// <b>The report is on the frozen node, once.</b> Downstream carries the state and no diagnostic,
/// which is the rule an error already follows: cascading turns a one-node situation into a
/// fifty-line wall that hides the cause.
/// </para>
/// </remarks>
public sealed class FreezeTests
{
    /// <summary>A frozen node is skipped and reported, and it produces nothing.</summary>
    [Fact]
    public void AFrozenNodeIsSkippedAndReported()
    {
        Graph graph = Chain(out NodeId first, out NodeId second, out _);

        Assert.True(graph.SetFrozen(first, frozen: true));

        EvaluationResult result = Evaluate(graph);

        Assert.Equal(NodeState.Frozen, result.StateOf(first));
        Assert.Equal(NodeState.UpstreamFrozen, result.StateOf(second));

        SparkDiagnostic diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal(DiagnosticCodes.NodeFrozen, diagnostic.Code);
        Assert.Equal(first.Value, diagnostic.NodeId);
    }

    /// <summary>
    /// <b>It is reported once, not once per node downstream.</b> A frozen node at the head of a
    /// long branch must not fill the diagnostics pane with its own consequences.
    /// </summary>
    [Fact]
    public void AFrozenNodeIsReportedOnceHoweverLongTheBranch()
    {
        Graph graph = Chain(out NodeId first, out NodeId second, out NodeId third);

        graph.SetFrozen(first, frozen: true);

        EvaluationResult result = Evaluate(graph);

        Assert.Single(result.Diagnostics);
        Assert.Equal(NodeState.UpstreamFrozen, result.StateOf(second));
        Assert.Equal(NodeState.UpstreamFrozen, result.StateOf(third));
    }

    /// <summary>
    /// <b>Freezing is not an error</b>, which is the row's own wording. Nothing in the result
    /// carries an error or a warning.
    /// </summary>
    [Fact]
    public void FreezingProducesNoErrorAndNoWarning()
    {
        Graph graph = Chain(out NodeId first, out _, out _);

        graph.SetFrozen(first, frozen: true);

        EvaluationResult result = Evaluate(graph);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity is DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity is DiagnosticSeverity.Warning);
    }

    /// <summary>Unfreezing brings the branch back, values and all.</summary>
    [Fact]
    public void UnfreezingBringsTheBranchBack()
    {
        Graph graph = Chain(out NodeId first, out _, out NodeId third);

        graph.SetFrozen(first, frozen: true);
        Assert.Equal(NodeState.UpstreamFrozen, Evaluate(graph).StateOf(third));

        Assert.True(graph.SetFrozen(first, frozen: false));

        EvaluationResult back = Evaluate(graph);

        Assert.Equal(NodeState.Evaluated, back.StateOf(first));
        Assert.Equal(NodeState.Evaluated, back.StateOf(third));
        Assert.Empty(back.Diagnostics);
    }

    /// <summary>
    /// <b>A branch that does not pass through the frozen node still runs.</b> Freezing switches off
    /// one path, not the document.
    /// </summary>
    [Fact]
    public void AnUnrelatedBranchStillEvaluates()
    {
        NodeLibrary library = Library();
        Graph graph = new();

        NodeId frozen = graph.AddNode(library.Get(Key("Number.Value"))).Id;
        NodeId untouched = graph.AddNode(library.Get(Key("Number.Value"))).Id;

        graph.SetLiteral(frozen, 0, 1.0);
        graph.SetLiteral(untouched, 0, 2.0);
        graph.SetFrozen(frozen, frozen: true);

        EvaluationResult result = Evaluate(graph);

        Assert.Equal(NodeState.Frozen, result.StateOf(frozen));
        Assert.Equal(NodeState.Evaluated, result.StateOf(untouched));
        Assert.Equal(2.0, result.Value(untouched));
    }

    /// <summary>Setting the flag to what it already is changes nothing and says so.</summary>
    [Fact]
    public void SettingTheFlagToWhatItAlreadyIsChangesNothing()
    {
        Graph graph = Chain(out NodeId first, out _, out _);

        Assert.True(graph.SetFrozen(first, frozen: true));
        Assert.False(graph.SetFrozen(first, frozen: true));
        Assert.True(graph.SetFrozen(first, frozen: false));
        Assert.False(graph.SetFrozen(first, frozen: false));
    }

    /// <summary>
    /// <b>The flag survives a save and a load</b>, or freezing an expensive branch would last
    /// exactly as long as the session.
    /// </summary>
    [Fact]
    public void TheFlagSurvivesARoundTrip()
    {
        NodeLibrary library = Library();
        Graph graph = Chain(out NodeId first, out _, out _);

        graph.SetFrozen(first, frozen: true);

        Graph restored = SparkFile.Read(SparkFile.Write(GraphDocument.Capture(graph))).Restore(library);

        Assert.True(restored.Node(first).IsFrozen);
        Assert.Equal(NodeState.Frozen, Evaluate(restored).StateOf(first));
    }

    /// <summary>
    /// <b>A graph with nothing frozen writes exactly what it wrote before freezing existed.</b>
    /// The flag is written only when true, so <c>E7-T7</c>'s byte-for-byte round trip stays an
    /// assertion about every file rather than about files this build wrote.
    /// </summary>
    [Fact]
    public void AGraphWithNothingFrozenWritesNoFreezeAtAll()
    {
        Graph graph = Chain(out _, out _, out _);

        string text = SparkFile.Write(GraphDocument.Capture(graph));

        Assert.DoesNotContain("frozen", text, StringComparison.Ordinal);
    }

    /// <summary>And a frozen graph re-saves byte for byte.</summary>
    [Fact]
    public void AFrozenGraphResavesByteForByte()
    {
        NodeLibrary library = Library();
        Graph graph = Chain(out NodeId first, out _, out _);

        graph.SetFrozen(first, frozen: true);

        string once = SparkFile.Write(GraphDocument.Capture(graph));
        string twice = SparkFile.Write(GraphDocument.Capture(SparkFile.Read(once).Restore(library)));

        Assert.Equal(once, twice);
    }

    private static NodeKey Key(string name) => new("Spark.Nodes.Core", name);

    private static NodeLibrary Library()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));
        return library;
    }

    /// <summary>Three nodes in a line: a number, doubled, doubled again.</summary>
    private static Graph Chain(out NodeId first, out NodeId second, out NodeId third)
    {
        NodeLibrary library = Library();
        Graph graph = new();

        first = graph.AddNode(library.Get(Key("Number.Value"))).Id;
        second = graph.AddNode(library.Get(Key("Math.Add"))).Id;
        third = graph.AddNode(library.Get(Key("Math.Add"))).Id;

        graph.SetLiteral(first, 0, 21.0);
        graph.SetLiteral(second, 1, 0.0);
        graph.SetLiteral(third, 1, 0.0);

        graph.LoadWire(first, 0, second, 0);
        graph.LoadWire(second, 0, third, 0);

        return graph;
    }

    private static EvaluationResult Evaluate(Graph graph) =>
        GraphEvaluator.Evaluate(graph, new EvaluationContext());
}

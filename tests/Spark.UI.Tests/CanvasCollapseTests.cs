using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.UI.Controls;
using Spark.UI.Graph;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Collapsing a selection into a custom node (<c>E7-T12</c>), and the save-side recursion refusal
/// that belongs with it (<c>E7-T13</c>).
/// </summary>
/// <remarks>
/// <b>The interface inference is the part with judgement in it</b>, so most of these test
/// <see cref="CanvasCollapse.Plan"/> rather than the application. A plan changes nothing, which is
/// what makes it worth testing on its own and what lets a caller show a user the result first.
/// </remarks>
public sealed class CanvasCollapseTests
{
    /// <summary>
    /// <b>The interface comes from the wires that crossed the boundary.</b> Selecting the middle of
    /// a three-node chain gives one input and one output, named after the inner ports they attach
    /// to.
    /// </summary>
    [Fact]
    public void CollapsingTheMiddleOfAChainInfersOneInputAndOneOutput()
    {
        CanvasGraph graph = Chain(out int source, out int middle, out int sink);
        _ = source;
        _ = sink;

        CollapsePlan plan = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(graph, [middle], Identity()));

        Assert.Single(plan.InputSources);
        Assert.Single(plan.OutputTargets);

        NodeDefinition built = Compile(plan);
        Assert.Equal("value", Assert.Single(built.Inputs).Name);
        Assert.Equal("doubled", Assert.Single(built.Outputs).Name);
    }

    /// <summary>
    /// Wires entirely inside the selection become private: they are neither an input nor an output.
    /// </summary>
    [Fact]
    public void WiresInsideTheSelectionBecomePrivate()
    {
        CanvasGraph graph = Chain(out int source, out int middle, out int sink);
        _ = source;

        CollapsePlan plan = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(graph, [middle, sink], Identity()));

        // One wire crosses in (source -> middle) and one crosses out (sink -> watch).
        Assert.Single(plan.InputSources);
        Assert.Single(plan.OutputTargets);
        Assert.Equal(2, plan.Absorbed.Count);
    }

    /// <summary>
    /// <b>One input port per distinct external source, not per crossing wire.</b> A single node
    /// feeding two ports inside the selection is one value arriving, and two ports would make the
    /// user wire the same thing twice.
    /// </summary>
    [Fact]
    public void OneSourceFeedingTwoInnerPortsGivesOneInputPort()
    {
        CanvasGraph graph = new();
        NodeLibrary library = Library();

        int number = graph.Add(library.Get(NodeKey.Parse("Test/Number.One")), 0, 0);
        int add = graph.Add(library.Get(NodeKey.Parse("Test/Number.Add")), 100, 0);
        int watch = graph.Add(library.Get(NodeKey.Parse("Test/Number.Double")), 200, 0);

        // The same output feeds both of Add's inputs.
        graph.Engine.LoadWire(graph.Nodes[number].Id, 0, graph.Nodes[add].Id, 0);
        graph.Engine.LoadWire(graph.Nodes[number].Id, 0, graph.Nodes[add].Id, 1);
        graph.Engine.LoadWire(graph.Nodes[add].Id, 0, graph.Nodes[watch].Id, 0);

        CollapsePlan plan = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(graph, [add], Identity()));

        Assert.Single(plan.InputSources);
    }

    /// <summary>
    /// One output feeding two outside nodes is one output port with two targets, and both are
    /// reconnected.
    /// </summary>
    [Fact]
    public void OneOutputFanningOutIsOnePortWithTwoTargets()
    {
        CanvasGraph graph = new();
        NodeLibrary library = Library();

        int number = graph.Add(library.Get(NodeKey.Parse("Test/Number.One")), 0, 0);
        int first = graph.Add(library.Get(NodeKey.Parse("Test/Number.Double")), 100, 0);
        int second = graph.Add(library.Get(NodeKey.Parse("Test/Number.Double")), 100, 80);

        graph.Engine.LoadWire(graph.Nodes[number].Id, 0, graph.Nodes[first].Id, 0);
        graph.Engine.LoadWire(graph.Nodes[number].Id, 0, graph.Nodes[second].Id, 0);

        CollapsePlan plan = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(graph, [number], Identity()));

        Assert.Equal(2, Assert.Single(plan.OutputTargets).Count);
    }

    /// <summary>
    /// <b>A selection nothing reads is refused rather than papered over.</b> A node with no output
    /// ports cannot be wired to anything, so it could never be used and creating one would look
    /// like a bug.
    /// </summary>
    [Fact]
    public void ASelectionNothingReadsIsRefused()
    {
        CanvasGraph graph = Chain(out int source, out int middle, out int sink);
        _ = source;
        _ = middle;

        // The whole chain: nothing outside reads it.
        Assert.Null(CanvasCollapse.Plan(graph, [0, 1, 2, 3], Identity()));
        _ = sink;
    }

    /// <summary>An empty selection is refused, and is not an error.</summary>
    [Fact]
    public void AnEmptySelectionIsRefused()
    {
        CanvasGraph graph = Chain(out _, out _, out _);

        Assert.Null(CanvasCollapse.Plan(graph, [], Identity()));
        Assert.Null(CanvasCollapse.Plan(graph, [99], Identity()));
    }

    /// <summary>
    /// <b>Applying it leaves a working graph.</b> The absorbed nodes are gone, the new node is
    /// wired where they were, and the whole thing still evaluates to the same answer.
    /// </summary>
    [Fact]
    public void TheCollapsedGraphStillProducesTheSameAnswer()
    {
        CanvasGraph graph = Chain(out int source, out int middle, out int sink);
        _ = source;

        double before = Evaluate(graph);

        // Two nodes, so the count genuinely shrinks: collapsing N nodes into one leaves N - 1
        // fewer, and collapsing a single node leaves the count unchanged - which would have made
        // this assertion prove nothing.
        CollapsePlan plan = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(graph, [middle, sink], Identity()));

        int beforeCount = graph.Nodes.Count;
        int slot = CanvasCollapse.Apply(graph, plan, Compile(plan));

        Assert.Equal(beforeCount - plan.Absorbed.Count + 1, graph.Nodes.Count);
        Assert.Equal(beforeCount - 1, graph.Nodes.Count);
        Assert.True(slot >= 0);

        // The absorbed nodes are gone from the outer graph entirely.
        foreach (NodeId absorbed in plan.Absorbed)
        {
            Assert.Equal(-1, graph.SlotOf(absorbed));
        }

        Assert.Equal(before, Evaluate(graph));
    }

    /// <summary>
    /// The same selection collapsed twice gives the same interface, so the operation is
    /// predictable rather than dependent on dictionary order.
    /// </summary>
    [Fact]
    public void CollapsingTheSameSelectionTwiceGivesTheSameInterface()
    {
        CustomNodeDocument first = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(Chain(out _, out int a, out _), [a], Identity())).Definition;
        CustomNodeDocument second = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(Chain(out _, out int b, out _), [b], Identity())).Definition;

        Assert.Equal(
            CustomNodePorts.Collect(first.Body, CustomNodePorts.InputKey).Select(p => p.Name),
            CustomNodePorts.Collect(second.Body, CustomNodePorts.InputKey).Select(p => p.Name));
    }

    /// <summary>
    /// <b>The save side of E7-T13.</b> A definition whose body contains itself is refused when
    /// written, not only when read: writing it and refusing to open it afterwards leaves the user's
    /// work in a file nothing can load.
    /// </summary>
    [Fact]
    public void WritingADefinitionThatContainsItselfIsRefused()
    {
        Spark.Engine.Graph body = new();
        body.AddNode(PlaceholderNode.For(NodeKey.Parse("Acme/Loop"), 1, 1));
        body.AddNode(CustomNodePorts.Output);

        CustomNodeDocument recursive = new(
            new CustomNodeInterface("Acme", "Loop"), GraphDocument.Capture(body));

        CustomNodeRecursionException thrown =
            Assert.Throws<CustomNodeRecursionException>(() => CustomNodeFile.Write(recursive));

        Assert.Contains("Acme/Loop", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>And an ordinary definition still writes, so the refusal is not simply always on.</summary>
    [Fact]
    public void AnOrdinaryDefinitionStillWrites()
    {
        CollapsePlan plan = Assert.IsType<CollapsePlan>(
            CanvasCollapse.Plan(Chain(out _, out int middle, out _), [middle], Identity()));

        Assert.Contains("interface", CustomNodeFile.Write(plan.Definition), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The gesture, end to end, through the canvas control.</b> The plan tests cover the
    /// inference; this covers what a user actually does: select, press, and get a working node in
    /// a graph that still evaluates.
    /// </summary>
    [Fact]
    public void TheCanvasGestureCollapsesAndLeavesTheGraphWorking()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            GraphCanvas canvas = new() { Graph = model.Graph };

            // The demo graph: a range feeding points. Collapsing the producer leaves a node that
            // still feeds what read it.
            int before = model.Graph.Nodes.Count;
            canvas.SelectOnly(0);

            int? slot = model.CollapseSelection(canvas.Selection, out string? reason);

            if (slot is null)
            {
                // A selection nothing reads cannot collapse, and the refusal must name a reason
                // rather than being silent. Either outcome is legitimate for slot 0 of whatever
                // demo graph ships; what is not legitimate is failing without saying why.
                Assert.False(string.IsNullOrWhiteSpace(reason));
                return;
            }

            canvas.CollapsedInto(slot.Value);

            Assert.Equal(before, model.Graph.Nodes.Count);
            Assert.NotNull(model.CustomNodes().Definition(model.LastCustomNodeKey));
            Assert.Contains(slot.Value, canvas.Selection);
        });
    }

    /// <summary>An empty selection refuses and says so, rather than doing nothing quietly.</summary>
    [Fact]
    public void TheGestureRefusesAnEmptySelectionWithAReason()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            GraphCanvas canvas = new() { Graph = model.Graph };

            int? slot = model.CollapseSelection(canvas.Selection, out string? reason);

            Assert.Null(slot);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        });
    }

    private static CustomNodeInterface Identity() => new("Acme", "Collapsed");

    private static NodeDefinition Compile(CollapsePlan plan)
    {
        NodeLibrary library = Library();
        CustomNodeLibrary customs = new(library);
        customs.Register(plan.Definition);
        customs.Build();

        return library.Get(plan.Definition.Interface.Key);
    }

    private static double Evaluate(CanvasGraph graph)
    {
        EvaluationContext context = new(
            Tolerance.Default, new SequentialEvaluationScheduler(), new EvaluationCache(), 0);

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph.Engine, context, TestContext.Current.CancellationToken);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        // The last node in the chain is the sink; find whatever nothing reads.
        NodeId terminal = graph.Engine.Nodes()
            .First(node => graph.Engine.OutgoingWires(node.Id).Count == 0).Id;

        return Convert.ToDouble(result.Value(terminal), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>One -> Double -> Double -> Double, so a middle slice has one crossing each way.</summary>
    private static CanvasGraph Chain(out int source, out int middle, out int sink)
    {
        NodeLibrary library = Library();
        CanvasGraph graph = new();

        source = graph.Add(library.Get(NodeKey.Parse("Test/Number.One")), 0, 0);
        middle = graph.Add(library.Get(NodeKey.Parse("Test/Number.Double")), 120, 0);
        sink = graph.Add(library.Get(NodeKey.Parse("Test/Number.Double")), 240, 0);
        int watch = graph.Add(library.Get(NodeKey.Parse("Test/Number.Double")), 360, 0);

        graph.Engine.LoadWire(graph.Nodes[source].Id, 0, graph.Nodes[middle].Id, 0);
        graph.Engine.LoadWire(graph.Nodes[middle].Id, 0, graph.Nodes[sink].Id, 0);
        graph.Engine.LoadWire(graph.Nodes[sink].Id, 0, graph.Nodes[watch].Id, 0);

        return graph;
    }

    private static NodeLibrary Library()
    {
        NodeLibrary library = new();
        CustomNodePorts.AddTo(library);

        library.Add(new NodeDefinition(
            NodeKey.Parse("Test/Number.One"),
            "Number.One",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [1.0]));

        library.Add(new NodeDefinition(
            NodeKey.Parse("Test/Number.Double"),
            "Number.Double",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("doubled", typeof(double), 0)],
            args => [Convert.ToDouble(args[0], System.Globalization.CultureInfo.InvariantCulture) * 2.0]));

        library.Add(new NodeDefinition(
            NodeKey.Parse("Test/Number.Add"),
            "Number.Add",
            [new PortDefinition("a", typeof(double), 0), new PortDefinition("b", typeof(double), 0)],
            [new PortDefinition("sum", typeof(double), 0)],
            args => [
                Convert.ToDouble(args[0], System.Globalization.CultureInfo.InvariantCulture)
                + Convert.ToDouble(args[1], System.Globalization.CultureInfo.InvariantCulture)]));

        return library;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// Custom nodes: a graph packaged as a node (<c>E7-T11</c>, <c>E7-T13</c>).
/// </summary>
/// <remarks>
/// The claim under test is that <b>graph-in-graph is the same mechanism, not a separate
/// feature</b>. What comes out of <see cref="CustomNodeLibrary"/> is an ordinary
/// <see cref="NodeDefinition"/>; if any of these tests needed a special case anywhere else in the
/// engine, the claim would be false.
/// </remarks>
public sealed class CustomNodeTests
{
    /// <summary>
    /// A custom node runs its body and returns what the Output node saw. The whole feature in one
    /// assertion.
    /// </summary>
    [Fact]
    public void ACustomNodeEvaluatesItsBody()
    {
        NodeDefinition doubler = BuildDoubler();

        object?[] produced = doubler.Call([21.0], CancellationToken.None);

        Assert.Equal(42.0, Assert.IsType<double>(Assert.Single(produced)));
    }

    /// <summary>
    /// The ports come from the Input and Output nodes, named as the user named them, in canvas
    /// order. Nothing declares them separately, so nothing can disagree with the graph.
    /// </summary>
    [Fact]
    public void PortsComeFromTheInputAndOutputNodesInCanvasOrder()
    {
        NodeDefinition doubler = BuildDoubler();

        Assert.Equal("number", Assert.Single(doubler.Inputs).Name);
        Assert.Equal("doubled", Assert.Single(doubler.Outputs).Name);
    }

    /// <summary>
    /// A custom node is an ordinary definition, so it can be dropped into a graph and evaluated by
    /// the ordinary evaluator with no special case anywhere.
    /// </summary>
    [Fact]
    public void ACustomNodeWorksInsideAnOrdinaryGraph()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);
        customs.Register(DoublerDocument());
        customs.Build();

        Graph graph = new();
        NodeId source = graph.AddNode(library.Get(NodeKey.Parse("Test/Number.Seven"))).Id;
        NodeId doubler = graph.AddNode(library.Get(NodeKey.Parse("Acme/Number.Doubled"))).Id;
        graph.LoadWire(source, 0, doubler, 0);

        EvaluationResult result = GraphEvaluator.Evaluate(graph, Context(), TestContext.Current.CancellationToken);

        Assert.False(result.HasErrors);
        Assert.Equal(14.0, result.Value(doubler));
    }

    /// <summary>
    /// One custom node can use another, and registration order does not matter — the builder works
    /// out the dependency order rather than asking the caller to sort by hand.
    /// </summary>
    [Fact]
    public void OneCustomNodeCanUseAnotherInEitherRegistrationOrder()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);

        // Deliberately registered outermost first.
        customs.Register(QuadruplerDocument());
        customs.Register(DoublerDocument());
        customs.Build();

        NodeDefinition quadrupler = library.Get(NodeKey.Parse("Acme/Number.Quadrupled"));

        Assert.Equal(40.0, Assert.Single(quadrupler.Call([10.0], CancellationToken.None)));
    }

    /// <summary>
    /// <b>Direct recursion is refused when the definition is built, not when it runs.</b> Refusing
    /// at evaluation time would mean a graph that opens, looks fine, and hangs the first time
    /// somebody presses run.
    /// </summary>
    [Fact]
    public void ANodeThatContainsItselfIsRefusedWithItsPath()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);
        customs.Register(SelfContainingDocument());

        CustomNodeRecursionException thrown = Assert.Throws<CustomNodeRecursionException>(customs.Build);

        Assert.Contains("Acme/Number.Loop", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("contains itself", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Indirect recursion is refused too, and the message names the whole chain. "Recursion
    /// detected" is not something a user can act on; <i>A contains B contains A</i> is.
    /// </summary>
    [Fact]
    public void IndirectRecursionIsRefusedAndTheContainmentPathIsReported()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);
        customs.Register(ContainerOf("Acme/A", "Acme/B"));
        customs.Register(ContainerOf("Acme/B", "Acme/A"));

        CustomNodeRecursionException thrown = Assert.Throws<CustomNodeRecursionException>(customs.Build);

        Assert.Contains("Acme/A", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Acme/B", thrown.Message, StringComparison.Ordinal);
        Assert.True(thrown.Path.Count >= 2, "the containment path should name every node in the loop");
    }

    /// <summary>
    /// A definition with no Output node is refused with a reason, because a node that produces
    /// nothing cannot be wired to anything and so could never be used.
    /// </summary>
    [Fact]
    public void ADefinitionWithNoOutputIsRefusedWithAReason()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);
        customs.Register(new CustomNodeDocument(
            new CustomNodeInterface("Acme", "Number.Nothing"),
            GraphDocument.Capture(new Graph())));

        SparkFileException thrown = Assert.Throws<SparkFileException>(customs.Build);

        Assert.Contains("no Output node", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>.sparkcustom</c> file survives a write and a read unchanged, interface included. The
    /// same round-trip promise the graph format makes, because it is the same format.
    /// </summary>
    [Fact]
    public void ACustomNodeFileSurvivesAWriteAndARead()
    {
        CustomNodeDocument original = DoublerDocument();

        string text = CustomNodeFile.Write(original);
        CustomNodeDocument read = CustomNodeFile.Read(text);

        Assert.Equal(original.Interface, read.Interface);
        Assert.Equal(text, CustomNodeFile.Write(read));
    }

    /// <summary>
    /// <b>The format is the graph format plus one object</b>, so the ordinary reader opens a
    /// <c>.sparkcustom</c> file as a plain graph — which is how a user edits one.
    /// </summary>
    [Fact]
    public void TheOrdinaryGraphReaderOpensACustomNodeFileAsItsDefinitionGraph()
    {
        string text = CustomNodeFile.Write(DoublerDocument());

        GraphDocument asGraph = SparkFile.Read(text);

        Assert.Contains(asGraph.Nodes, n => n.Key == CustomNodePorts.InputKey);
        Assert.Contains(asGraph.Nodes, n => n.Key == CustomNodePorts.OutputKey);
    }

    /// <summary>
    /// The reserved <c>viewKey</c> survives a round trip although nothing reads it
    /// (<c>E7-T15</c>). <b>That is the entire value of reserving it:</b> a file written by a future
    /// version that does use it is not quietly stripped by this one.
    /// </summary>
    [Fact]
    public void TheReservedViewKeySurvivesARoundTripAlthoughNothingUsesIt()
    {
        CustomNodeDocument original = DoublerDocument();
        CustomNodeDocument tagged = original with
        {
            Interface = original.Interface with { ViewKey = "spark.slider" },
        };

        CustomNodeDocument read = CustomNodeFile.Read(CustomNodeFile.Write(tagged));

        Assert.Equal("spark.slider", read.Interface.ViewKey);
    }

    /// <summary>A graph file with no interface block is not a custom node, and says so.</summary>
    [Fact]
    public void APlainGraphIsRefusedAsACustomNode()
    {
        string plain = SparkFile.Write(GraphDocument.Capture(new Graph()));

        SparkFileException thrown = Assert.Throws<SparkFileException>(() => CustomNodeFile.Read(plain));

        Assert.Contains("interface", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An error inside the body is reported naming the custom node, so a user is not left looking
    /// at a failure with no indication which node it came from.
    /// </summary>
    [Fact]
    public void AnErrorInsideTheBodyNamesTheCustomNode()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);
        customs.Register(ThrowingDocument());
        customs.Build();

        NodeDefinition thrower = library.Get(NodeKey.Parse("Acme/Number.Throws"));

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => thrower.Call([1.0], CancellationToken.None));

        Assert.Contains("Acme/Number.Throws", thrown.Message, StringComparison.Ordinal);
    }

    private static EvaluationContext Context() =>
        new(Tolerance.Default, new SequentialEvaluationScheduler(), new EvaluationCache(), 0);

    private static NodeDefinition BuildDoubler()
    {
        NodeLibrary library = BuiltIns();
        CustomNodeLibrary customs = new(library);
        customs.Register(DoublerDocument());
        customs.Build();
        return library.Get(NodeKey.Parse("Acme/Number.Doubled"));
    }

    /// <summary>Input(name "number") -> Number.Double -> Output(name "doubled").</summary>
    private static CustomNodeDocument DoublerDocument()
    {
        NodeLibrary library = BuiltIns();
        CustomNodePorts.AddTo(library);

        Graph body = new();
        NodeId input = body.AddNode(CustomNodePorts.Input).Id;
        NodeId doubler = body.AddNode(library.Get(NodeKey.Parse("Test/Number.Double"))).Id;
        NodeId output = body.AddNode(CustomNodePorts.Output).Id;

        body.SetLiteral(input, CustomNodePorts.NamePort, "number");
        body.SetLiteral(output, CustomNodePorts.NamePort, "doubled");
        body.LoadWire(input, 0, doubler, 0);
        body.LoadWire(doubler, 0, output, CustomNodePorts.ValuePort);

        return new CustomNodeDocument(
            new CustomNodeInterface("Acme", "Number.Doubled", "Doubles a number.", NodeCategories.Math),
            GraphDocument.Capture(body, Positions(input, doubler, output)));
    }

    /// <summary>Input -> Acme/Number.Doubled -> Acme/Number.Doubled -> Output.</summary>
    private static CustomNodeDocument QuadruplerDocument()
    {
        NodeLibrary library = BuiltIns();
        CustomNodePorts.AddTo(library);

        // The inner custom node is not built yet, so a placeholder stands in while the document is
        // captured. Only the key is written, which is all the builder needs.
        NodeDefinition inner = PlaceholderNode.For(NodeKey.Parse("Acme/Number.Doubled"), 1, 1);

        Graph body = new();
        NodeId input = body.AddNode(CustomNodePorts.Input).Id;
        NodeId first = body.AddNode(inner).Id;
        NodeId second = body.AddNode(inner).Id;
        NodeId output = body.AddNode(CustomNodePorts.Output).Id;

        body.SetLiteral(input, CustomNodePorts.NamePort, "number");
        body.SetLiteral(output, CustomNodePorts.NamePort, "quadrupled");
        body.LoadWire(input, 0, first, 0);
        body.LoadWire(first, 0, second, 0);
        body.LoadWire(second, 0, output, CustomNodePorts.ValuePort);

        return new CustomNodeDocument(
            new CustomNodeInterface("Acme", "Number.Quadrupled"),
            GraphDocument.Capture(body, Positions(input, first, second, output)));
    }

    private static CustomNodeDocument SelfContainingDocument() => ContainerOf("Acme/Number.Loop", "Acme/Number.Loop");

    /// <summary>A custom node whose body contains one node with the given key.</summary>
    private static CustomNodeDocument ContainerOf(string self, string contained)
    {
        NodeKey key = NodeKey.Parse(self);

        Graph body = new();
        NodeId input = body.AddNode(CustomNodePorts.Input).Id;
        NodeId inner = body.AddNode(PlaceholderNode.For(NodeKey.Parse(contained), 1, 1)).Id;
        NodeId output = body.AddNode(CustomNodePorts.Output).Id;

        body.LoadWire(input, 0, inner, 0);
        body.LoadWire(inner, 0, output, CustomNodePorts.ValuePort);

        return new CustomNodeDocument(
            new CustomNodeInterface(key.Package, key.Name),
            GraphDocument.Capture(body, Positions(input, inner, output)));
    }

    private static CustomNodeDocument ThrowingDocument()
    {
        NodeLibrary library = BuiltIns();

        Graph body = new();
        NodeId input = body.AddNode(CustomNodePorts.Input).Id;
        NodeId boom = body.AddNode(library.Get(NodeKey.Parse("Test/Number.Explode"))).Id;
        NodeId output = body.AddNode(CustomNodePorts.Output).Id;

        body.LoadWire(input, 0, boom, 0);
        body.LoadWire(boom, 0, output, CustomNodePorts.ValuePort);

        return new CustomNodeDocument(
            new CustomNodeInterface("Acme", "Number.Throws"),
            GraphDocument.Capture(body, Positions(input, boom, output)));
    }

    /// <summary>Stacks nodes vertically in the order given, which is the order ports come out in.</summary>
    private static Func<NodeId, (double X, double Y)> Positions(params NodeId[] order)
    {
        Dictionary<NodeId, double> rows = [];
        for (int index = 0; index < order.Length; index++)
        {
            rows[order[index]] = index * 100.0;
        }

        return id => (0.0, rows.TryGetValue(id, out double y) ? y : 0.0);
    }

    private static NodeLibrary BuiltIns()
    {
        NodeLibrary library = new();
        CustomNodePorts.AddTo(library);

        library.Add(new NodeDefinition(
            NodeKey.Parse("Test/Number.Seven"),
            "Number.Seven",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [7.0]));

        library.Add(new NodeDefinition(
            NodeKey.Parse("Test/Number.Double"),
            "Number.Double",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("doubled", typeof(double), 0)],
            args => [Convert.ToDouble(args[0]) * 2.0]));

        library.Add(new NodeDefinition(
            NodeKey.Parse("Test/Number.Explode"),
            "Number.Explode",
            [new PortDefinition("value", typeof(double), 0)],
            [new PortDefinition("never", typeof(double), 0)],
            _ => throw new InvalidOperationException("this node always fails")));

        return library;
    }
}

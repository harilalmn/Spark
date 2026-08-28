using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Scripting;

namespace Spark.Scripting.Tests;

/// <summary>
/// A code block has to be an ordinary node. These tests put one in a real <see cref="Graph"/> and run
/// it through <see cref="GraphEvaluator"/> — wired, cached, laced and all — rather than calling its
/// invoker directly, because "it compiles" and "it evaluates in a graph" are different claims.
/// </summary>
public sealed class GraphIntegrationTests
{
    [Fact]
    public void ACodeBlockEvaluatesInsideAGraph()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "centre.X + centre.Y + centre.Z",
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(Point3d), "centre")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        Graph graph = new();
        NodeInstance point = graph.AddNode(
            CodeBlockTestHarness.Constant("point", typeof(Point3d), new Point3d(1, 2, 3)));
        NodeInstance block = graph.AddNode(compilation.Definition!);

        ConnectionResult connection = graph.TryConnect(point.Id, 0, block.Id, 0);
        Assert.True(connection.Accepted, connection.Diagnostic?.Message);

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext());

        Assert.Equal(NodeState.Evaluated, result.StateOf(block.Id));
        Assert.Equal(6.0, result.Value(block.Id));
    }

    /// <summary>
    /// A named tuple return really does become two wireable output ports, each carrying its own type
    /// downstream.
    /// </summary>
    [Fact]
    public void ANamedTupleGivesTwoWireableOutputPorts()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "(sum: a + b, product: a * b)",
            CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("a", "b")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        Graph graph = new();
        NodeInstance first = graph.AddNode(CodeBlockTestHarness.Constant("a", typeof(double), 3.0));
        NodeInstance second = graph.AddNode(CodeBlockTestHarness.Constant("b", typeof(double), 4.0));
        NodeInstance block = graph.AddNode(compilation.Definition!);

        Assert.True(graph.TryConnect(first.Id, 0, block.Id, 0).Accepted);
        Assert.True(graph.TryConnect(second.Id, 0, block.Id, 1).Accepted);

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext());

        Assert.Equal(7.0, result.Value(block.Id, 0));
        Assert.Equal(12.0, result.Value(block.Id, 1));
    }

    /// <summary>
    /// The evaluation cache treats a code block like anything else, which is what makes re-running an
    /// unchanged graph free.
    /// </summary>
    [Fact]
    public void ASecondRunOfAnUnchangedGraphIsServedFromTheEvaluationCache()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "x * 2", CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("x")));

        Graph graph = new();
        NodeInstance source = graph.AddNode(CodeBlockTestHarness.Constant("x", typeof(double), 21.0));
        NodeInstance block = graph.AddNode(compilation.Definition!);
        Assert.True(graph.TryConnect(source.Id, 0, block.Id, 0).Accepted);

        EvaluationContext context = new();

        EvaluationResult first = GraphEvaluator.Evaluate(graph, context);
        EvaluationResult second = GraphEvaluator.Evaluate(graph, context);

        Assert.Equal(42.0, first.Value(block.Id));
        Assert.Equal(42.0, second.Value(block.Id));
        Assert.Equal(0, second.NodesEvaluated);
    }

    /// <summary>
    /// A scalar port fed a list replicates, exactly as it would on a built-in node. This is what
    /// "participates in lacing like any other node" means in practice.
    /// </summary>
    [Fact]
    public void AScalarPortFedAListReplicates()
    {
        CodeBlockCompilation compilation = CodeBlockCompiler.Compile(
            "x * x", CodeBlockTestHarness.Options(CodeBlockTestHarness.Doubles("x")));

        Assert.True(compilation.Success, CodeBlockTestHarness.Report(compilation));

        Graph graph = new();
        NodeInstance source = graph.AddNode(CodeBlockTestHarness.Constant(
            "numbers", typeof(IReadOnlyList<double>), new SparkList([1.0, 2.0, 3.0], 1)));
        NodeInstance block = graph.AddNode(compilation.Definition!);
        Assert.True(graph.TryConnect(source.Id, 0, block.Id, 0).Accepted);

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext());

        SparkList squares = Assert.IsType<SparkList>(result.Value(block.Id));

        Assert.Equal(3, squares.Count);
        Assert.Equal(1.0, squares[0]);
        Assert.Equal(4.0, squares[1]);
        Assert.Equal(9.0, squares[2]);
    }

    /// <summary>
    /// A code block whose output type is known can be wired into a typed input. Typed as
    /// <see cref="object"/> it could not be, which is why output inference is not decoration.
    /// </summary>
    [Fact]
    public void ATypedOutputCanBeWiredIntoATypedInput()
    {
        CodeBlockCompilation producer = CodeBlockCompiler.Compile(
            "new Point3d(1, 2, 3)", CodeBlockTestHarness.Options());

        CodeBlockCompilation consumer = CodeBlockCompiler.Compile(
            "p.Z", CodeBlockTestHarness.Options(CodeBlockTestHarness.Wired(typeof(Point3d), "p")));

        Assert.True(producer.Success, CodeBlockTestHarness.Report(producer));
        Assert.True(consumer.Success, CodeBlockTestHarness.Report(consumer));

        Graph graph = new();
        NodeInstance first = graph.AddNode(producer.Definition!);
        NodeInstance second = graph.AddNode(consumer.Definition!);

        ConnectionResult connection = graph.TryConnect(first.Id, 0, second.Id, 0);
        Assert.Equal(PortCompatibility.Direct, connection.Compatibility);

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext());
        Assert.Equal(3.0, result.Value(second.Id));
    }

    /// <summary>
    /// A block that throws fails its node and leaves the rest of the graph alone, rather than taking
    /// the run down.
    /// </summary>
    [Fact]
    public void ABlockThatThrowsFailsItsOwnNodeAndNoOther()
    {
        CodeBlockCompilation thrower = CodeBlockCompiler.Compile(
            "throw new System.InvalidOperationException(\"deliberate\");", CodeBlockTestHarness.Options());

        Assert.True(thrower.Success, CodeBlockTestHarness.Report(thrower));

        Graph graph = new();
        NodeInstance block = graph.AddNode(thrower.Definition!);
        NodeInstance untouched = graph.AddNode(
            CodeBlockTestHarness.Constant("elsewhere", typeof(double), 1.0));

        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext());

        Assert.Equal(NodeState.Error, result.StateOf(block.Id));
        Assert.Equal(NodeState.Evaluated, result.StateOf(untouched.Id));
    }

    /// <summary>The node wrapper is the shape the canvas actually holds.</summary>
    [Fact]
    public void TheNodeWrapperRecompilesWhenAWireIsDrawn()
    {
        CodeBlockNode unwired = CodeBlockNode.Create("centre.X", CodeBlockTestHarness.Options());
        Assert.False(unwired.IsValid);

        CodeBlockNode wired = unwired.WithConnectedInputTypes(
            CodeBlockTestHarness.Wired(typeof(Point3d), "centre"));

        Assert.True(wired.IsValid, string.Join(Environment.NewLine, wired.Diagnostics));
        Assert.False(unwired.IsValid);
        Assert.Equal(typeof(Point3d), wired.Inputs[0].ValueType);
    }
}

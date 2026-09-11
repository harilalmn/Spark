using System;
using System.Linq;
using System.Threading;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The evaluation's cancellation token reaches a library node that asks for it (`E3-T12`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Before this, only a code block was ever handed the token.</b> A library node's method that
/// accepted a <see cref="CancellationToken"/> was imported with the token as an input port — a socket
/// nothing could plug into — and a node like <c>Surface.ToMesh</c>, whose work can be long, could not
/// be stopped once it had started.
/// </para>
/// <para>
/// <b>Delivery is asserted by its consequence, deterministically</b>: the fixture reports whether the
/// token it was handed can be cancelled at all. Handed the evaluation's token, it can; handed nothing,
/// it is <see cref="CancellationToken.None"/> and cannot. No timing is involved.
/// </para>
/// </remarks>
public sealed class CancellableNodeTests
{
    private static readonly ImportReport Report = NodeImporter.Import([typeof(TokenFixtures)], "Fixtures");

    private static NodeDefinition Node(string name) =>
        Report.Nodes.Single(node => node.Definition.Key.Name == name).Definition;

    /// <summary>
    /// <b>The token is not a port</b>, so the node has only the port its value takes, and its key is
    /// the plain member name — exactly what it would be had the method never accepted a token.
    /// </summary>
    [Fact]
    public void ATokenParameterIsNotAPort()
    {
        NodeDefinition node = Node("TokenFixtures.Reports");

        Assert.Equal(["value"], node.Inputs.Select(port => port.Name));
        Assert.Equal("TokenFixtures.Reports", node.Key.Value.Split('/')[^1]);
    }

    /// <summary>
    /// <b>The row.</b> The node receives the token it is called with — a real, cancellable one — and
    /// receives none when it is invoked without one.
    /// </summary>
    [Fact]
    public void ALibraryNodeIsHandedTheEvaluationsToken()
    {
        NodeDefinition node = Node("TokenFixtures.Reports");
        using CancellationTokenSource source = new();

        Assert.Equal(1, Assert.Single(node.Call([5], source.Token)));
        Assert.Equal(0, Assert.Single(node.Invoke([5])));
    }

    /// <summary>
    /// Through the evaluator as well: a graph run with a token hands it to the node, which is the path
    /// every real evaluation takes.
    /// </summary>
    [Fact]
    public void TheEvaluatorHandsTheTokenOn()
    {
        Graph graph = new();
        NodeInstance reports = graph.AddNode(Node("TokenFixtures.Reports"));
        graph.SetLiteral(reports.Id, 0, 5);

        using CancellationTokenSource source = new();
        EvaluationResult result = GraphEvaluator.Evaluate(graph, new EvaluationContext(), source.Token);

        Assert.Equal(1, result.Value(reports.Id));
    }

    /// <summary>A node whose method takes no token is invoked exactly as it always was.</summary>
    [Fact]
    public void ANodeWithoutATokenIsUnchanged()
    {
        NodeDefinition node = Node("TokenFixtures.Doubles");

        Assert.Null(node.InvokeCancellable);
        Assert.Equal(10, Assert.Single(node.Call([5], TestContext.Current.CancellationToken)));
    }

}

/// <summary>
/// The nodes <see cref="CancellableNodeTests"/> imports — top level, because the importer does not
/// import nested types, and says so rather than importing them badly.
/// </summary>
public static class TokenFixtures
{
    /// <summary>One when handed a token that can be cancelled, zero when handed none.</summary>
    /// <param name="value">Ignored; a node needs an input.</param>
    /// <param name="cancellationToken">The evaluation's token.</param>
    /// <returns>Whether it arrived.</returns>
    public static int Reports(int value, CancellationToken cancellationToken) =>
        value > int.MinValue && cancellationToken.CanBeCanceled ? 1 : 0;

    /// <summary>Twice the value, with no token anywhere.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Twice it.</returns>
    public static int Doubles(int value) => value * 2;
}

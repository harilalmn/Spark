using System.Reflection;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The watch node, and the one attribute that makes it a watch rather than a bug.
/// </summary>
/// <remarks>
/// A watch takes a value and returns it unchanged. Everything interesting about it is what it does
/// <b>not</b> do: it does not replicate, so it sees the list rather than the items, and it does not
/// alter the value, so it can sit in the middle of a wire without changing the graph.
/// </remarks>
public sealed class WatchNodeTests
{
    private static readonly NodeLibrary Library = BuildLibrary();

    /// <summary>
    /// <b>The reason <c>[KeepStructure]</c> is on the port.</b> A plain <c>object</c> port is rank
    /// 0, so the engine would replicate the watch once per element and hand it one item at a time —
    /// and the list is exactly what a user opened a watch to look at.
    /// </summary>
    [Fact]
    public void AWatchSeesTheWholeListRatherThanOneItemAtATime()
    {
        SparkList list = SparkList.Of(1.0, 2.0, 3.0);

        object? seen = Run(list);

        SparkList produced = Assert.IsType<SparkList>(seen);
        Assert.Equal(3, produced.Count);
        Assert.Equal(1, produced.Rank);
    }

    /// <summary>A list of lists arrives as a list of lists, which is the case rank exists for.</summary>
    [Fact]
    public void AWatchPreservesRankTwo()
    {
        object? seen = Run(SparkList.Of(SparkList.Of(1.0, 2.0), SparkList.Of(3.0)));

        Assert.Equal(2, SparkList.RankOf(seen));
    }

    [Fact]
    public void AWatchReturnsAScalarUnchanged()
    {
        Assert.Equal(4.5, Run(4.5));
    }

    /// <summary>
    /// The declaration the canvas reads. The canvas has no library and must never name an engine
    /// type (ADR-0005), so <i>is this a watch?</i> has to be something the definition says about
    /// itself — the same route <c>Category</c> already travels.
    /// </summary>
    [Fact]
    public void TheWatchNodeDeclaresThatItShowsItsValue()
    {
        Assert.True(Library.ByName("Watch.Value").ShowsValue);
    }

    /// <summary>And nothing else does, or every node would draw a permanent bubble.</summary>
    [Fact]
    public void NoOtherNodeDeclaresIt()
    {
        Assert.DoesNotContain(
            Library.Definitions(),
            node => node.ShowsValue && node.DisplayName != "Watch.Value");
    }

    private static object? Run(object? value)
    {
        Graph graph = new();
        NodeInstance watch = graph.AddNode(Library.ByName("Watch.Value"));
        graph.SetLiteral(watch.Id, 0, value);

        EvaluationResult result = GraphEvaluator.Evaluate(
            graph, new EvaluationContext(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Diagnostics);
        return result.Value(watch.Id);
    }

    private static NodeLibrary BuildLibrary()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }
}

using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// What a preview bubble says, and in particular that it says the rank.
/// </summary>
/// <remarks>
/// <c>E8-T10</c> asks for a node's output <b>and its rank</b>, and gives the reason: rank is what
/// users get wrong. A node that quietly produced a list of lists where a list was expected is the
/// commonest way a graph goes wrong without ever erroring, and the two are indistinguishable in the
/// value at a glance — <c>[[1], [2]]</c> and <c>[1, 2]</c> read alike and are not alike. So the
/// rank line is asserted on its own, separately from the value.
/// </remarks>
public sealed class PreviewBubbleTests
{
    /// <summary>
    /// A scalar reads <i>one value</i> and never <i>0 items</i>. A single number and an empty list
    /// are the two things this line exists to tell apart.
    /// </summary>
    [Fact]
    public void AScalarSaysOneValueRatherThanZeroItems()
    {
        CanvasNode node = Evaluated(4.0);

        Assert.Equal("rank 0 · one value", CanvasGraph.RankLine(node));
    }

    [Fact]
    public void AFlatListReportsRankOneAndItsLength()
    {
        CanvasNode node = Evaluated(SparkList.Of(1.0, 2.0, 3.0));

        Assert.Equal("rank 1 · 3 items", CanvasGraph.RankLine(node));
    }

    /// <summary>
    /// <b>The case the feature is for.</b> A list of lists is rank 2, and says so, even though its
    /// value renders almost identically to a flat list of the same length.
    /// </summary>
    [Fact]
    public void AListOfListsReportsRankTwo()
    {
        SparkList nested = SparkList.Of(SparkList.Of(1.0, 2.0), SparkList.Of(3.0, 4.0));

        CanvasNode node = Evaluated(nested);

        Assert.Equal("rank 2 · 2 items", CanvasGraph.RankLine(node));
        Assert.Equal(2, node.ResultRank);
    }

    [Fact]
    public void OneItemIsSingular()
    {
        CanvasNode node = Evaluated(SparkList.Of(7.0));

        Assert.Equal("rank 1 · 1 item", CanvasGraph.RankLine(node));
    }

    /// <summary>
    /// An empty list is still rank 1. It is not a scalar, and a graph that produced one where a
    /// value was expected is exactly the situation a user is trying to diagnose.
    /// </summary>
    [Fact]
    public void AnEmptyListIsStillAList()
    {
        CanvasNode node = Evaluated(SparkList.Empty(1));

        Assert.Equal("rank 1 · 0 items", CanvasGraph.RankLine(node));
    }

    /// <summary>
    /// The value line stays short. A bubble is a glance, not a viewer — the untruncated value is
    /// what a watch is for.
    /// </summary>
    [Fact]
    public void TheValueLineIsTruncated()
    {
        CanvasNode node = Evaluated(new SparkList([.. Enumerable.Range(0, 400).Select(i => (object?)(double)i)], 1));

        Assert.NotNull(node.ResultSummary);
        Assert.True(node.ResultSummary!.Length < 120, node.ResultSummary);
        Assert.EndsWith("…", node.ResultSummary, System.StringComparison.Ordinal);
    }

    /// <summary>Clearing a run clears the rank as well as the text, or a stale rank outlives it.</summary>
    [Fact]
    public void ClearingTheResultClearsTheRank()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasNode node = graph.Nodes[0];
        node.ResultRank = 3;
        node.ResultCount = 9;
        node.ResultSummary = "stale";

        graph.ApplyResult(null);

        Assert.Null(node.ResultSummary);
        Assert.Equal(0, node.ResultRank);
        Assert.Equal(0, node.ResultCount);
    }

    /// <summary>
    /// Builds a node carrying a value, by hand rather than by evaluating something that happens to
    /// produce it — the rank rules are what is under test, not the node library.
    /// </summary>
    private static CanvasNode Evaluated(object? value)
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasNode node = graph.Nodes[0];

        node.ResultSummary = CanvasGraph.Summarise(value);
        node.ResultRank = SparkList.RankOf(value);
        node.ResultCount = value is SparkList list ? list.Count : 0;
        return node;
    }
}

using Spark.Api;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// That the shell and the command line render a value the same way, because they call the same
/// code.
/// </summary>
/// <remarks>
/// <c>E12-T5</c> requires <c>spark run</c> to produce output identical to the desktop
/// application's. That is a claim about two programs, and the only way to keep one true is to make
/// it structural: <see cref="ValueText"/> lives in <c>Spark.Api</c>, beneath both, and the canvas
/// delegates to it rather than paraphrasing it. These tests fail the moment somebody reintroduces a
/// second rendering, which is exactly the day the claim would quietly stop being true.
/// </remarks>
public sealed class ValueRenderingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheCanvasSummaryIsTheSharedSummary(int rank)
    {
        object? value = Sample(rank);

        Assert.Equal(ValueText.Summary(value), CanvasGraph.Summarise(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheWatchPanelRenderingIsTheSharedFullRendering(int rank)
    {
        object? value = Sample(rank);

        Assert.Equal(ValueText.Full(value), CanvasGraph.Expand(value));
    }

    [Fact]
    public void TheCanvasRankLineIsTheSharedShapeLine()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasNode node = graph.Nodes[0];

        node.ResultRank = 2;
        node.ResultCount = 7;

        Assert.Equal(ValueText.Shape(2, 7), CanvasGraph.RankLine(node));
    }

    [Fact]
    public void TheWatchPanelCapIsTheSharedCap()
    {
        Assert.Equal(ValueText.FullLength, CanvasGraph.WatchCharacterLimit);
    }

    private static object? Sample(int rank) => rank switch
    {
        0 => 3.25,
        1 => SparkList.Of(1.0, 2.0, 3.0),
        _ => SparkList.Of(SparkList.Of(1.0), SparkList.Of(2.0, 3.0)),
    };
}

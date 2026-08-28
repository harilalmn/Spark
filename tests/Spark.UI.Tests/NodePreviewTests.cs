using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;
using Spark.UI.Canvas;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// What a node's preview says about the value it is previewing.
/// </summary>
/// <remarks>
/// `E8-T10`'s one recorded requirement is that a preview shows **rank**, not only value, because
/// rank is what graph authors get wrong: a hundred points at rank 1 and a hundred at rank 2 draw
/// identically in the viewport and lace completely differently. So the rank is asserted here, and
/// it is asserted on the collapsed headline rather than on anything behind the toggle.
/// </remarks>
public sealed class NodePreviewTests
{
    /// <summary>A node that produced nothing has no preview at all.</summary>
    [Fact]
    public void NothingProducedIsNoPreview() => Assert.Null(CanvasGraph.PreviewOf(null));

    /// <summary>A single value is headlined by its type, in the words the ports use.</summary>
    [Fact]
    public void ASingleValueIsHeadlinedByItsType()
    {
        NodePreview preview = Assert.IsType<NodePreview>(CanvasGraph.PreviewOf(4.5));

        Assert.Equal("number", preview.Headline);
        Assert.Equal(["4.5"], preview.Lines);
        Assert.Equal(0, preview.Hidden);
    }

    /// <summary>A kernel value keeps its own type name.</summary>
    [Fact]
    public void AGeometryValueIsHeadlinedByItsKernelType()
    {
        NodePreview preview = Assert.IsType<NodePreview>(
            CanvasGraph.PreviewOf(new Point3d(1, 2, 3)));

        Assert.Equal("Point3d", preview.Headline);
    }

    /// <summary>
    /// A list says how many and <b>at what rank</b>, in the line that is always visible.
    /// </summary>
    [Fact]
    public void AListSaysHowManyAndAtWhatRank()
    {
        SparkList list = new([1.0, 2.0, 3.0], 1);

        NodePreview preview = Assert.IsType<NodePreview>(CanvasGraph.PreviewOf(list));

        Assert.Contains("3 items", preview.Headline, System.StringComparison.Ordinal);
        Assert.Contains("rank 1", preview.Headline, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Two lists of the same length at different ranks do not read the same.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the requirement, stated as a test: if these two headlines were
    /// equal the preview would be showing the half a user already knows.
    /// </remarks>
    [Fact]
    public void RankIsWhatSeparatesTwoListsOfTheSameLength()
    {
        NodePreview flat = Assert.IsType<NodePreview>(
            CanvasGraph.PreviewOf(new SparkList([1.0, 2.0], 1)));
        NodePreview nested = Assert.IsType<NodePreview>(CanvasGraph.PreviewOf(
            new SparkList([new SparkList([1.0], 1), new SparkList([2.0], 1)], 2)));

        Assert.NotEqual(flat.Headline, nested.Headline);
        Assert.Contains("rank 2", nested.Headline, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A long list shows its first few elements and says how many it left out.
    /// </summary>
    /// <remarks>
    /// Saying nothing would make a list of a hundred read as a list of six, which is a worse
    /// answer than not previewing it.
    /// </remarks>
    [Fact]
    public void ALongListSaysHowManyItLeftOut()
    {
        List<object?> items = [];
        for (int index = 0; index < 100; index++)
        {
            items.Add((double)index);
        }

        NodePreview preview = Assert.IsType<NodePreview>(CanvasGraph.PreviewOf(new SparkList(items, 1)));

        Assert.Equal(6, preview.Lines.Count);
        Assert.Equal(94, preview.Hidden);
    }

    /// <summary>
    /// A closed preview is exactly as wide as its node; an open one grows to fit its values.
    /// </summary>
    /// <remarks>
    /// The values are the reason a preview is opened, and a coordinate triple truncated to the
    /// node's width shows two of the three numbers somebody wanted. Growth is capped so that one
    /// long string cannot lay a strip across the whole graph.
    /// </remarks>
    [Fact]
    public void AnOpenPreviewGrowsToFitItsValuesAndNoFurther()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        CanvasNode node = graph.Nodes[0];

        node.Preview = new NodePreview(
            "3 items · rank 1",
            [
                "(5.388942295416207, 0.8793495662309033, 0)",
                "(4.389205463295536, 1.363609401591421, 0)",
                "(3.318146136715008, 1.666330173211755, 0)",
            ],
            0);

        CanvasBounds closed = Assert.IsType<CanvasBounds>(node.PreviewBounds);
        Assert.Equal(node.Width, closed.Width, 6);

        node.IsPreviewOpen = true;
        CanvasBounds open = Assert.IsType<CanvasBounds>(node.PreviewBounds);

        Assert.True(open.Width > node.Width, $"An open preview stayed at {open.Width}.");
        Assert.InRange(open.Width, node.Width, node.Width * CanvasNode.PreviewWidestFactor);
    }

    /// <summary>
    /// A single very long value cannot stretch the strip past the cap.
    /// </summary>
    [Fact]
    public void OneVeryLongValueCannotStretchTheStripAcrossTheGraph()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        CanvasNode node = graph.Nodes[0];

        node.Preview = new NodePreview("text", [new string('x', 400)], 0);
        node.IsPreviewOpen = true;

        CanvasBounds bounds = Assert.IsType<CanvasBounds>(node.PreviewBounds);
        Assert.Equal(node.Width * CanvasNode.PreviewWidestFactor, bounds.Width, 6);
    }

    /// <summary>A nested element is rendered as its own count and rank, not as a wall of numbers.</summary>
    [Fact]
    public void ANestedElementIsRenderedAsItsCountAndRank()
    {
        NodePreview preview = Assert.IsType<NodePreview>(CanvasGraph.PreviewOf(
            new SparkList([new SparkList([1.0, 2.0, 3.0], 1)], 2)));

        Assert.Contains("3 items", preview.Lines[0], System.StringComparison.Ordinal);
        Assert.Contains("rank 1", preview.Lines[0], System.StringComparison.Ordinal);
    }
}

using System;
using System.Linq;
using Spark.Api;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The watch panel's rendering, and the canvas's knowledge that a node is a watch.
/// </summary>
/// <remarks>
/// Two renderings of one value exist on purpose. <c>Summarise</c> cuts at sixty characters and is
/// what a preview bubble shows — a glance. <c>Expand</c> is where somebody goes to read the value,
/// and is capped only where a <c>TextBox</c> would stop being a user interface. Rendering the same
/// string in both places would make one of the two wrong.
/// </remarks>
public sealed class WatchPanelTests
{
    [Fact]
    public void TheWatchPanelShowsAValueThatTheBubbleWouldHaveCut()
    {
        SparkList long_ = new([.. Enumerable.Range(0, 200).Select(i => (object?)(double)i)], 1);

        string summary = CanvasGraph.Summarise(long_)!;
        string expanded = CanvasGraph.Expand(long_);

        Assert.EndsWith("…", summary, StringComparison.Ordinal);
        Assert.True(expanded.Length > summary.Length * 5, "The panel must not be a longer summary.");
        Assert.DoesNotContain("…", expanded, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cap exists and announces itself. A truncation that trails off is one a reader mistakes
    /// for the end of their data.
    /// </summary>
    [Fact]
    public void AnEnormousValueIsCappedAndSaysHowMuchIsMissing()
    {
        string enormous = new('x', CanvasGraph.WatchCharacterLimit + 500);

        string expanded = CanvasGraph.Expand(enormous);

        Assert.Contains("500 more characters not shown", expanded, StringComparison.Ordinal);
        Assert.True(expanded.Length < CanvasGraph.WatchCharacterLimit + 200);
    }

    [Fact]
    public void AValueThatFitsIsNotAnnotatedAtAll()
    {
        Assert.Equal("hello", CanvasGraph.Expand("hello"));
    }

    [Fact]
    public void NothingToWatchRendersAsNothing()
    {
        Assert.Equal(string.Empty, CanvasGraph.Expand(null));
    }

    /// <summary>
    /// The canvas learns that a node is a watch from the definition, never by recognising its
    /// name — the canvas has no library and must not name an engine type (ADR-0005).
    /// </summary>
    [Fact]
    public void AWatchNodeArrivesOnTheCanvasKnowingItShowsItsValue()
    {
        CanvasGraph graph = new();

        graph.Add(TestGraphs.Library.ByName("Watch.Value"), 0, 0);
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 300, 0);

        Assert.True(graph.Nodes[0].ShowsValue);
        Assert.False(graph.Nodes[1].ShowsValue);
    }

    /// <summary>
    /// <b>The rule that makes a watch a watch.</b> A bubble answers <i>what is this node under my
    /// pointer doing</i>; a watch answers <i>what is happening here</i> while you look elsewhere.
    /// So a watch shows its value with nothing selected and nothing hovered, and an ordinary node
    /// does not.
    /// </summary>
    [Fact]
    public void AWatchShowsItsValueWithNothingSelectedAndNothingHovered() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Watch.Value"), 0, 0);
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 300, 0);

        Spark.UI.Controls.GraphCanvas canvas = new() { Graph = graph };

        Assert.True(canvas.ShowsPreview(0), "A watch is permanent.");
        Assert.False(canvas.ShowsPreview(1), "An ordinary node is not.");
    });

    /// <summary>Selecting an ordinary node is what puts a bubble under it.</summary>
    [Fact]
    public void SelectingAnOrdinaryNodeShowsItsValue() => HeadlessSession.Run(() =>
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        Spark.UI.Controls.GraphCanvas canvas = new() { Graph = graph };

        Assert.False(canvas.ShowsPreview(0));

        canvas.SelectOnly(0);

        Assert.True(canvas.ShowsPreview(0));
        Assert.False(canvas.ShowsPreview(1));
    });

    /// <summary>A slot that is not a node is not a preview, rather than an exception.</summary>
    [Fact]
    public void AnImpossibleSlotShowsNothing() => HeadlessSession.Run(() =>
    {
        Spark.UI.Controls.GraphCanvas canvas = new() { Graph = TestGraphs.SourceAndSink() };

        Assert.False(canvas.ShowsPreview(-1));
        Assert.False(canvas.ShowsPreview(99));
    });

    /// <summary>
    /// And it still knows after a save and an open, because the flag comes from the definition
    /// rather than from the file — which is why it needed no format change.
    /// </summary>
    [Fact]
    public void AWatchStillKnowsAfterARoundTrip()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Watch.Value"), 0, 0);

        CanvasGraph reopened = CanvasDocument.Open(CanvasDocument.Save(graph), TestGraphs.Library);

        Assert.True(Assert.Single(reopened.Nodes).ShowsValue);

        // Version 1: a watch is a node, and nodes have always been in the format.
        Assert.Contains("\"formatVersion\": 1", CanvasDocument.Save(graph), StringComparison.Ordinal);
    }
}

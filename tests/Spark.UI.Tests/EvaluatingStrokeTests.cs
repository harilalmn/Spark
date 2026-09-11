using System;
using Avalonia.Controls;
using Avalonia.Headless;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// The evaluating stroke travels the outline on a 900 ms lap, and only where the animation budget
/// allows it (`E3-T14` step B, design language §7.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The clock and the decision are tested, not the pixels.</b> This suite's headless window draws
/// nothing ([N124](../../docs/NOTES.md)), but it does run the canvas's render path, so what is asserted
/// is the phase - a pure function of elapsed time - and whether a frame asked for another.
/// </para>
/// <para>
/// <b>The budget is the one the canvas already had.</b> §7.4 says the stroke stops below 55% zoom;
/// §10.2's animation budget, <see cref="Spark.UI.Canvas.CanvasLevelOfDetail.AllowsAnimation"/>, stops
/// every per-node animation below 60% or above 400 visible nodes. The stricter one is used, so both
/// rules hold.
/// </para>
/// </remarks>
public sealed class EvaluatingStrokeTests
{
    /// <summary><b>The row.</b> One lap every 900 ms: <c>motion.ambient</c>.</summary>
    [Fact]
    public void TheStrokeGoesRoundOnceEvery900Milliseconds()
    {
        Assert.Equal(0.0, GraphCanvas.EvaluatingPhase(TimeSpan.Zero), 9);
        Assert.Equal(0.5, GraphCanvas.EvaluatingPhase(TimeSpan.FromMilliseconds(450)), 9);
        Assert.Equal(0.0, GraphCanvas.EvaluatingPhase(TimeSpan.FromMilliseconds(900)), 9);
        Assert.Equal(0.25, GraphCanvas.EvaluatingPhase(TimeSpan.FromMilliseconds(1125)), 9);
    }

    /// <summary>An evaluating node at working zoom travels, and its frame asks for the next.</summary>
    [Fact]
    public void AnEvaluatingNodeAtWorkingZoomAnimates() => HeadlessSession.Run(() =>
        Assert.True(Rendered(evaluating: true, zoom: 1.0), "an evaluating node at 100% zoom did not animate"));

    /// <summary>Zoomed out past the budget, the same node keeps the static ring and asks for nothing.</summary>
    [Fact]
    public void ZoomedOutTheRingStandsStill() => HeadlessSession.Run(() =>
        Assert.False(Rendered(evaluating: true, zoom: 0.5), "an evaluating node at 50% zoom animated"));

    /// <summary>With nothing evaluating, the canvas asks for no frames at all.</summary>
    [Fact]
    public void NothingEvaluatingAsksForNoFrames() => HeadlessSession.Run(() =>
        Assert.False(Rendered(evaluating: false, zoom: 1.0), "a canvas with nothing evaluating animated"));

    /// <summary>
    /// Draws the demo graph with its first node aimed at from just above and to the left, so it is in
    /// view whatever the zoom, and answers whether that frame animated.
    /// </summary>
    private static bool Rendered(bool evaluating, double zoom)
    {
        CanvasGraph graph = TestGraphs.Demo();
        CanvasNode node = graph.Nodes[0];

        if (evaluating)
        {
            node.State |= CanvasNodeState.Evaluating;
        }

        GraphCanvas canvas = new() { Graph = graph };
        Window window = new() { Width = 900, Height = 700, Content = canvas };

        window.Show();

        try
        {
            // One frame first, so anything the canvas does on its first layout is done before the
            // view is set.
            window.CaptureRenderedFrame();

            canvas.Transform.Zoom = zoom;
            canvas.Transform.OffsetX = node.X - 40;
            canvas.Transform.OffsetY = node.Y - 40;
            canvas.InvalidateVisual();
            window.CaptureRenderedFrame();

            Assert.True(canvas.LastVisibleNodeCount > 0, "the aimed-at node is not in view");
            return canvas.LastFrameAnimatedEvaluation;
        }
        finally
        {
            window.Close();
        }
    }
}

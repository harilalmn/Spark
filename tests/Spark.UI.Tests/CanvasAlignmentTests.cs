using System;
using System.Collections.Generic;
using System.Linq;
using Spark.UI.Canvas;

namespace Spark.UI.Tests;

/// <summary>
/// Lining up nodes, as arithmetic — no window, no gesture, no selection.
/// </summary>
/// <remarks>
/// The boxes are deliberately different sizes. Every alignment is correct for equal-sized boxes by
/// accident, and the two that are easiest to get wrong — right and centres — are exactly the ones
/// that need a box's own width to be read rather than assumed.
/// </remarks>
public sealed class CanvasAlignmentTests
{
    /// <summary>Three boxes of three different sizes, scattered.</summary>
    private static IReadOnlyList<CanvasBounds> Scattered =>
    [
        CanvasBounds.FromSize(10, 100, 100, 40),   // wide and short
        CanvasBounds.FromSize(60, 20, 40, 80),     // narrow and tall
        CanvasBounds.FromSize(200, 60, 60, 60),    // square-ish, furthest right
    ];

    [Fact]
    public void AligningLeftPutsEveryLeftEdgeOnTheLeftmostOne()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.Left, Scattered);

        Assert.All(placed, p => Assert.Equal(10, p.X));

        // The other axis is untouched. An alignment that quietly tidied both would be impossible
        // to use to line up a column without also destroying its order.
        Assert.Equal([100, 20, 60], placed.Select(p => p.Y));
    }

    [Fact]
    public void AligningRightAccountsForEachBoxOwnWidth()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.Right, Scattered);

        // The rightmost edge is 260. A box 100 wide has to start at 160 to end there, and the
        // narrow one at 220 — which is the assertion that fails if widths are assumed equal.
        Assert.Equal([160, 220, 200], placed.Select(p => p.X));
    }

    [Fact]
    public void AligningHorizontalCentresCentresEachBoxOnTheSelectionCentre()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.HorizontalCentres, Scattered);

        // The extent runs 10..260, so its centre is 135.
        foreach (((double X, double _) p, CanvasBounds box) in placed.Zip(Scattered))
        {
            Assert.Equal(135, p.X + (box.Width / 2), 9);
        }
    }

    [Fact]
    public void AligningTopAndBottomWorkTheSameWayDownTheOtherAxis()
    {
        IReadOnlyList<(double X, double Y)> top = CanvasAlignment.Apply(CanvasAlign.Top, Scattered);
        IReadOnlyList<(double X, double Y)> bottom =
            CanvasAlignment.Apply(CanvasAlign.Bottom, Scattered);

        Assert.All(top, p => Assert.Equal(20, p.Y));

        // The lowest edge is 140: the 40-tall box already ends there, the 80-tall one has to
        // start at 60 and the 60-tall one at 80.
        Assert.Equal([100, 60, 80], bottom.Select(p => p.Y));
    }

    /// <summary>
    /// The whole reason distribution is written against gaps and not centres.
    /// </summary>
    [Fact]
    public void DistributingHorizontallyMakesTheGapsEqualRatherThanTheCentres()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.DistributeHorizontally, Scattered);

        // Written back in the caller's order, so re-sort by position to read the run off.
        List<(double Start, double End)> run = [.. placed
            .Zip(Scattered, (p, box) => (Start: p.X, End: p.X + box.Width))
            .OrderBy(r => r.Start)];

        double first = run[0].End - run[1].Start;
        double second = run[1].End - run[2].Start;

        Assert.Equal(first, second, 9);

        // And the outermost two did not move: 10 was the leftmost edge and 260 the rightmost.
        Assert.Equal(10, run[0].Start, 9);
        Assert.Equal(260, run[^1].End, 9);
    }

    [Fact]
    public void DistributingLeavesTheOtherAxisAlone()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.DistributeHorizontally, Scattered);

        Assert.Equal([100, 20, 60], placed.Select(p => p.Y));
    }

    /// <summary>
    /// Two nodes have exactly one gap between them, and one gap is already equal to itself.
    /// Offering distribute over two would be offering an operation that does nothing.
    /// </summary>
    [Fact]
    public void DistributionNeedsThreeNodesAndAlignmentNeedsTwo()
    {
        Assert.False(CanvasAlignment.IsApplicable(CanvasAlign.Left, 1));
        Assert.True(CanvasAlignment.IsApplicable(CanvasAlign.Left, 2));

        Assert.False(CanvasAlignment.IsApplicable(CanvasAlign.DistributeHorizontally, 2));
        Assert.True(CanvasAlignment.IsApplicable(CanvasAlign.DistributeHorizontally, 3));
    }

    /// <summary>
    /// A selection too small for the operation comes back unchanged rather than throwing, so the
    /// caller has one question to ask and not two.
    /// </summary>
    [Fact]
    public void ATooSmallSelectionIsReturnedWhereItAlreadyWas()
    {
        IReadOnlyList<CanvasBounds> one = [CanvasBounds.FromSize(7, 9, 100, 40)];

        IReadOnlyList<(double X, double Y)> placed = CanvasAlignment.Apply(CanvasAlign.Left, one);

        Assert.Equal((7.0, 9.0), placed[0]);
    }

    /// <summary>
    /// Aligning an already-aligned column has to be a genuine no-op, because that is what a user
    /// does to <i>check</i> a column is aligned.
    /// </summary>
    [Fact]
    public void AligningAnAlreadyAlignedSetMovesNothing()
    {
        IReadOnlyList<CanvasBounds> column =
        [
            CanvasBounds.FromSize(40, 0, 100, 30),
            CanvasBounds.FromSize(40, 50, 60, 30),
            CanvasBounds.FromSize(40, 100, 80, 30),
        ];

        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.Left, column);

        Assert.Equal(column.Select(b => (b.MinX, b.MinY)), placed);
    }

    /// <summary>
    /// Nodes that together are wider than their span overlap, rather than the span being widened.
    /// Widening it would move the outermost nodes, which is the one thing distribute promises not
    /// to do.
    /// </summary>
    [Fact]
    public void NodesTooWideForTheirSpanOverlapRatherThanPushingTheEndsApart()
    {
        IReadOnlyList<CanvasBounds> crowded =
        [
            CanvasBounds.FromSize(0, 0, 100, 20),
            CanvasBounds.FromSize(20, 0, 100, 20),
            CanvasBounds.FromSize(40, 0, 100, 20),
        ];

        IReadOnlyList<(double X, double Y)> placed =
            CanvasAlignment.Apply(CanvasAlign.DistributeHorizontally, crowded);

        List<double> starts = [.. placed.Select(p => p.X).Order()];

        Assert.Equal(0, starts[0], 9);
        Assert.Equal(40, starts[^1], 9);

        // The gap is negative and evenly so — the two overlaps are the same size.
        Assert.Equal(starts[1] - starts[0], starts[2] - starts[1], 9);
    }

    [Fact]
    public void EveryOperationHasALabelAndNoneOfThemNamesACount()
    {
        foreach (CanvasAlign align in Enum.GetValues<CanvasAlign>())
        {
            string label = CanvasAlignment.Describe(align);

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.DoesNotContain("node", label, StringComparison.OrdinalIgnoreCase);
        }
    }
}

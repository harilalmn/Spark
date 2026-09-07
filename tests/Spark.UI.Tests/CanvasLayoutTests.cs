using System;
using System.Collections.Generic;
using System.Linq;
using Spark.UI.Canvas;

namespace Spark.UI.Tests;

/// <summary>
/// Laying nodes out into columns, as arithmetic — no window, no gesture, no selection.
/// </summary>
/// <remarks>
/// The boxes are deliberately different sizes, for the reason <see cref="CanvasAlignmentTests"/>
/// gives: a column whose spacing assumes equal heights is correct by accident on a graph where
/// every node has the same port count, which is a graph nobody has.
/// </remarks>
public sealed class CanvasLayoutTests
{
    /// <summary>
    /// A chain of three, scattered, wired 0 → 1 → 2 and given in an order that is not the flow.
    /// </summary>
    private static IReadOnlyList<CanvasBounds> Chain =>
    [
        CanvasBounds.FromSize(300, 200, 100, 40),
        CanvasBounds.FromSize(100, 60, 120, 80),
        CanvasBounds.FromSize(500, 400, 80, 60),
    ];

    [Fact]
    public void EveryNodeLandsToTheRightOfWhatFeedsIt()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasLayout.Apply(Chain, [(0, 1), (1, 2)]);

        Assert.True(placed[1].X > placed[0].X);
        Assert.True(placed[2].X > placed[1].X);
    }

    /// <summary>
    /// The column gap is measured from the widest node in the column, not from the node the wire
    /// happens to leave. A narrow node beside a wide one in the same column would otherwise put
    /// the next column on top of the wide one.
    /// </summary>
    [Fact]
    public void AColumnIsClearedByItsWidestMember()
    {
        // Two sources of very different widths feeding one sink.
        IReadOnlyList<CanvasBounds> boxes =
        [
            CanvasBounds.FromSize(0, 0, 60, 40),
            CanvasBounds.FromSize(0, 100, 400, 40),
            CanvasBounds.FromSize(600, 50, 100, 40),
        ];

        IReadOnlyList<(double X, double Y)> placed = CanvasLayout.Apply(boxes, [(0, 2), (1, 2)]);

        Assert.Equal(placed[0].X, placed[1].X);
        Assert.Equal(placed[0].X + 400 + CanvasLayout.ColumnGap, placed[2].X);
    }

    /// <summary>
    /// <b>The longest path decides, not the shortest.</b> A node fed both directly by a source and
    /// through a chain must clear the chain, or the direct wire runs backwards past it — which is
    /// the one thing the arrangement exists to prevent.
    /// </summary>
    [Fact]
    public void ANodeClearsTheLongestPathThatReachesIt()
    {
        IReadOnlyList<CanvasBounds> boxes =
        [
            CanvasBounds.FromSize(0, 0, 100, 40),
            CanvasBounds.FromSize(0, 0, 100, 40),
            CanvasBounds.FromSize(0, 0, 100, 40),
        ];

        // 0 feeds 1 and 2; 1 also feeds 2. Node 2 is one past 0 by the short path and two past it
        // by the long one.
        IReadOnlyList<(double X, double Y)> placed =
            CanvasLayout.Apply(boxes, [(0, 1), (0, 2), (1, 2)]);

        double column = 100 + CanvasLayout.ColumnGap;
        Assert.Equal(0, placed[0].X);
        Assert.Equal(column, placed[1].X);
        Assert.Equal(2 * column, placed[2].X);
    }

    [Fact]
    public void NodesInAColumnAreStackedWithoutOverlapping()
    {
        // Three sources, all feeding one sink, so all three share a column.
        IReadOnlyList<CanvasBounds> boxes =
        [
            CanvasBounds.FromSize(0, 0, 100, 40),
            CanvasBounds.FromSize(0, 0, 100, 90),
            CanvasBounds.FromSize(0, 0, 100, 60),
            CanvasBounds.FromSize(900, 0, 100, 40),
        ];

        IReadOnlyList<(double X, double Y)> placed =
            CanvasLayout.Apply(boxes, [(0, 3), (1, 3), (2, 3)]);

        List<(double Top, double Bottom)> column = [.. Enumerable.Range(0, 3)
            .Select(i => (Top: placed[i].Y, Bottom: placed[i].Y + boxes[i].Height))
            .OrderBy(r => r.Top)];

        Assert.Equal(column[0].Bottom + CanvasLayout.RowGap, column[1].Top, 9);
        Assert.Equal(column[1].Bottom + CanvasLayout.RowGap, column[2].Top, 9);
    }

    /// <summary>
    /// <b>Two chains that never meet stay two chains.</b> This is the whole value of ordering a
    /// column by where its inputs ended up: without it the second column is in list order, the two
    /// chains interleave, and every wire crosses its neighbour.
    /// </summary>
    [Fact]
    public void TwoIndependentChainsDoNotInterleave()
    {
        // Sources 0 and 1; 0 feeds 3 and 1 feeds 2 — so list order and flow order disagree.
        IReadOnlyList<CanvasBounds> boxes =
        [
            CanvasBounds.FromSize(0, 0, 100, 40),
            CanvasBounds.FromSize(0, 200, 100, 40),
            CanvasBounds.FromSize(400, 0, 100, 40),
            CanvasBounds.FromSize(400, 200, 100, 40),
        ];

        IReadOnlyList<(double X, double Y)> placed = CanvasLayout.Apply(boxes, [(0, 3), (1, 2)]);

        // Node 0 is above node 1, so what 0 feeds must be above what 1 feeds.
        Assert.True(placed[0].Y < placed[1].Y);
        Assert.True(placed[3].Y < placed[2].Y);
    }

    /// <summary>
    /// The arrangement is anchored where the nodes already were. A clean-up that also moved the
    /// graph to the origin would be two operations wearing one name.
    /// </summary>
    [Fact]
    public void TheArrangementKeepsTheTopLeftCornerItStartedFrom()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasLayout.Apply(Chain, [(0, 1), (1, 2)]);

        Assert.Equal(100, placed.Min(p => p.X));
        Assert.Equal(60, placed.Min(p => p.Y));
    }

    /// <summary>
    /// <b>The undo-stack guard, at the arithmetic level.</b> Laying out an already-laid-out graph
    /// must produce exactly the same positions, or the canvas records a step every time the key is
    /// pressed and undo appears to do nothing.
    /// </summary>
    [Fact]
    public void LayingOutTwiceChangesNothingTheSecondTime()
    {
        IReadOnlyList<(int From, int To)> links = [(0, 2), (1, 2), (2, 3), (1, 3)];
        IReadOnlyList<CanvasBounds> boxes =
        [
            CanvasBounds.FromSize(300, 200, 100, 40),
            CanvasBounds.FromSize(100, 60, 120, 90),
            CanvasBounds.FromSize(500, 400, 80, 60),
            CanvasBounds.FromSize(50, 350, 140, 40),
        ];

        IReadOnlyList<(double X, double Y)> once = CanvasLayout.Apply(boxes, links);

        IReadOnlyList<CanvasBounds> moved = [.. once.Zip(boxes)
            .Select(pair => CanvasBounds.FromSize(
                pair.First.X, pair.First.Y, pair.Second.Width, pair.Second.Height))];

        Assert.Equal(once, CanvasLayout.Apply(moved, links));
    }

    /// <summary>
    /// A wire naming a node outside the set is dropped rather than throwing. The canvas hands this
    /// a selection, and a wire with one end outside it is not a link between two nodes that move.
    /// </summary>
    [Fact]
    public void ALinkOutsideTheSetIsIgnored()
    {
        IReadOnlyList<(double X, double Y)> placed =
            CanvasLayout.Apply(Chain, [(0, 1), (1, 7), (-1, 2), (2, 2)]);

        // Only 0 → 1 survived, so 2 is a source and shares the first column.
        Assert.Equal(placed[0].X, placed[2].X);
        Assert.True(placed[1].X > placed[0].X);
    }

    /// <summary>
    /// <b>A cycle must terminate.</b> <c>TryConnect</c> refuses a wire that would close one, but a
    /// <c>.spark</c> file can be loaded with a cycle already in it (<c>SPK1014</c>), and a layout
    /// that relaxed until the ranks stopped moving would never stop.
    /// </summary>
    [Fact]
    public void ACycleIsLaidOutRatherThanHangingOrThrowing()
    {
        IReadOnlyList<CanvasBounds> boxes =
        [
            CanvasBounds.FromSize(0, 0, 100, 40),
            CanvasBounds.FromSize(0, 0, 100, 40),
            CanvasBounds.FromSize(0, 0, 100, 40),
        ];

        IReadOnlyList<(double X, double Y)> placed =
            CanvasLayout.Apply(boxes, [(0, 1), (1, 2), (2, 0)]);

        Assert.Equal(3, placed.Count);
        Assert.All(placed, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)));
    }

    /// <summary>
    /// <b>The noise the idempotence guard is actually up against.</b> A box's width is
    /// <c>MaxX - MinX</c>, so moving a box and asking for its width again does not return the same
    /// double — <c>(520 + 209.2) - 520</c> against <c>(40 + 209.2) - 40</c>. The columns are then
    /// accumulated from very slightly different widths and the second pass lands a node about 1e-13
    /// units away from where the first one put it. This is the difference the canvas must not
    /// mistake for a move, and the size of it.
    /// </summary>
    [Fact]
    public void AMoveTooSmallToSeeIsNotAMove()
    {
        double far = (520 + 209.2) - 520;
        double near = (40 + 209.2) - 40;

        Assert.NotEqual(far, near);
        Assert.False(CanvasLayout.Moves((40 + far + 72, 10), (40 + near + 72, 10)));

        // And it is not a blanket tolerance: anything a user could see still counts.
        Assert.True(CanvasLayout.Moves((100, 10), (100.01, 10)));
        Assert.True(CanvasLayout.Moves((100, 10), (100, 10.01)));
    }

    [Fact]
    public void ASetTooSmallToLayOutIsReturnedUnchanged()
    {
        IReadOnlyList<CanvasBounds> one = [CanvasBounds.FromSize(37, 41, 100, 40)];

        Assert.False(CanvasLayout.IsApplicable(one.Count));
        Assert.Equal([(37.0, 41.0)], CanvasLayout.Apply(one, []));
        Assert.Equal([], CanvasLayout.Apply([], []));
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => CanvasLayout.Apply(null!, []));
        Assert.Throws<ArgumentNullException>(() => CanvasLayout.Apply(Chain, null!));
    }
}

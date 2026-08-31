using System.Collections.Generic;
using Spark.UI.Canvas;

namespace Spark.UI.Tests;

/// <summary>
/// The ported spatial index. Every claim ADR-0013 makes about the canvas rests on this class
/// answering correctly and answering cheaply, so both are asserted: the visible set has to be
/// right, and <see cref="SceneIndex.ConsideredCount"/> has to stay far below the graph size.
/// </summary>
public sealed class SceneIndexTests
{
    [Fact]
    public void AnEmptyIndexHasNothingVisible()
    {
        SceneIndex index = new();
        index.Rebuild([]);
        index.Query(-1000, -1000, 1000, 1000);

        Assert.Equal(0, index.VisibleCount);
        Assert.Empty(Collect(index.Visible));
    }

    [Fact]
    public void AQueryReturnsExactlyTheOverlappingItems()
    {
        SceneIndex index = new();
        index.Rebuild([
            CanvasBounds.FromSize(0, 0, 10, 10),
            CanvasBounds.FromSize(100, 0, 10, 10),
            CanvasBounds.FromSize(200, 200, 10, 10),
        ]);

        index.Query(-5, -5, 50, 50);

        Assert.Equal([0], Collect(index.Visible));
    }

    [Fact]
    public void AnItemTouchingTheQueryEdgeIsVisible()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(10, 10, 10, 10)]);

        index.Query(0, 0, 10, 10);

        Assert.Equal([0], Collect(index.Visible));
    }

    [Fact]
    public void VisibleSlotsComeBackInDrawOrder()
    {
        SceneIndex index = new();
        List<CanvasBounds> bounds = [];
        for (int i = 0; i < 40; i++)
        {
            bounds.Add(CanvasBounds.FromSize(i * 5, 0, 4, 4));
        }

        index.Rebuild(bounds);
        index.Query(-100, -100, 1000, 1000);

        int[] visible = Collect(index.Visible);

        // The slot index IS the draw order, which is what lets the visibility bitset be walked
        // ascending with no sort. If this ever regresses, nodes start drawing in the wrong
        // z-order and overlapping nodes swap.
        for (int i = 1; i < visible.Length; i++)
        {
            Assert.True(visible[i] > visible[i - 1]);
        }
    }

    [Fact]
    public void TopDownIsTheReverseOfDrawOrder()
    {
        SceneIndex index = new();
        index.Rebuild([
            CanvasBounds.FromSize(0, 0, 10, 10),
            CanvasBounds.FromSize(2, 2, 10, 10),
            CanvasBounds.FromSize(4, 4, 10, 10),
        ]);

        index.Query(5, 5, 6, 6);

        int[] ascending = Collect(index.Visible);
        int[] descending = Collect(index.VisibleTopDown);

        Assert.Equal(ascending.Length, descending.Length);
        for (int i = 0; i < ascending.Length; i++)
        {
            Assert.Equal(ascending[ascending.Length - 1 - i], descending[i]);
        }
    }

    [Fact]
    public void HitTestingReturnsTheTopmostItemUnderThePoint()
    {
        SceneIndex index = new();
        index.Rebuild([
            CanvasBounds.FromSize(0, 0, 100, 100),
            CanvasBounds.FromSize(10, 10, 50, 50),
            CanvasBounds.FromSize(20, 20, 10, 10),
        ]);

        // The thing drawn last is the thing you clicked. Anything else and a user cannot select an
        // overlapping node without moving it first.
        Assert.Equal(2, index.HitTest(25, 25));
        Assert.Equal(1, index.HitTest(15, 15));
        Assert.Equal(0, index.HitTest(5, 5));
        Assert.Equal(-1, index.HitTest(500, 500));
    }

    [Fact]
    public void CullingConsidersFarFewerItemsThanTheGraphHolds()
    {
        SceneIndex index = new();
        List<CanvasBounds> bounds = [];
        for (int i = 0; i < 2000; i++)
        {
            bounds.Add(CanvasBounds.FromSize((i % 50) * 260, (i / 50) * 150, 168, 80));
        }

        index.Rebuild(bounds);
        index.Query(0, 0, 1400, 800);

        // The ratio of considered to visible is the whole point of the class: when considered
        // equals the graph size, culling is doing nothing and the index has silently stopped
        // paying for itself.
        Assert.True(index.VisibleCount is > 0 and < 100, $"Visible was {index.VisibleCount}.");
        Assert.True(
            index.ConsideredCount < 200,
            $"Considered {index.ConsideredCount} of 2000 items; the grid is not culling.");
    }

    [Fact]
    public void AnItemLargerThanACellIsStillFound()
    {
        // This is the case that broke DoodleSharp's first grid: binning by corner and widening the
        // query by one cell looks cheaper until an item is larger than a cell, and then half the
        // document lands in the always-scanned list.
        SceneIndex index = new();
        List<CanvasBounds> bounds = [];
        for (int i = 0; i < 400; i++)
        {
            bounds.Add(CanvasBounds.FromSize(i * 10, 0, 8, 8));
        }

        bounds.Add(new CanvasBounds(-100, -100, 5000, 400));
        index.Rebuild(bounds);

        index.Query(3000, 100, 3010, 110);

        Assert.Contains(400, Collect(index.Visible));
    }

    [Fact]
    public void UpdatingAnItemMovesItInSubsequentQueries()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(0, 0, 10, 10)]);

        index.Update(0, CanvasBounds.FromSize(500, 500, 10, 10));

        index.Query(-5, -5, 50, 50);
        Assert.Equal(0, index.VisibleCount);

        index.Query(495, 495, 520, 520);
        Assert.Equal([0], Collect(index.Visible));
    }

    [Fact]
    public void RemovedSlotsStopBeingVisible()
    {
        SceneIndex index = new();
        index.Rebuild([
            CanvasBounds.FromSize(0, 0, 10, 10),
            CanvasBounds.FromSize(20, 0, 10, 10),
        ]);

        Assert.True(index.Remove(0));
        Assert.False(index.Remove(0));
        Assert.False(index.IsLive(0));

        index.Query(-100, -100, 100, 100);
        Assert.Equal([1], Collect(index.Visible));
    }

    [Fact]
    public void AddedItemsAreFoundBeforeAnyRebuild()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(0, 0, 10, 10)]);

        int slot = index.Add(CanvasBounds.FromSize(900, 900, 10, 10));
        index.Query(890, 890, 920, 920);

        Assert.Equal(1, slot);
        Assert.Equal([1], Collect(index.Visible));
    }

    [Fact]
    public void RebuildIsRequestedOnlyOnceIncrementalChangeStopsBeingNegligible()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(0, 0, 10, 10)]);

        for (int i = 0; i < 400; i++)
        {
            index.Add(CanvasBounds.FromSize(i, 0, 4, 4));
        }

        // Dragging out a few dozen nodes must not rebuild on every mouse-up.
        Assert.False(index.NeedsRebuild);

        for (int i = 0; i < 400; i++)
        {
            index.Add(CanvasBounds.FromSize(i, 100, 4, 4));
        }

        Assert.True(index.NeedsRebuild);
    }

    [Fact]
    public void UpdatingTheSameSlotRepeatedlyDoesNotGrowTheOverflow()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(0, 0, 10, 10)]);

        // A node drag calls Update once per pointer move. If each call appended, a long drag would
        // turn the always-scanned overflow into the whole graph.
        for (int i = 0; i < 5000; i++)
        {
            index.Update(0, CanvasBounds.FromSize(i, 0, 10, 10));
        }

        Assert.False(index.NeedsRebuild);
    }

    [Fact]
    public void ClearingEmptiesTheIndex()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(0, 0, 10, 10)]);
        index.Clear();

        Assert.Equal(0, index.SlotCount);
        index.Query(-100, -100, 100, 100);
        Assert.Equal(0, index.VisibleCount);
    }

    [Fact]
    public void MaximumExtentComesFromTheCachedBoundsRatherThanBeingRecomputed()
    {
        SceneIndex index = new();
        index.Rebuild([CanvasBounds.FromSize(0, 0, 40, 90)]);

        Assert.Equal(90, index.MaxExtentAt(0));
        Assert.Equal(0, index.MaxExtentAt(7));

        index.BoundsAt(0, out double minX, out double minY, out double maxX, out double maxY);
        Assert.Equal(0, minX);
        Assert.Equal(0, minY);
        Assert.Equal(40, maxX);
        Assert.Equal(90, maxY);
    }

    /// <summary>
    /// Containment is the window half of the box-select pair, and its edges are inclusive.
    /// </summary>
    /// <remarks>
    /// Edges are inclusive in <see cref="CanvasBounds.Intersects"/> too, on purpose: a box dragged
    /// flush to a node's edge that selected it in one mode and not the other would look like a bug
    /// whichever way it fell.
    /// </remarks>
    [Fact]
    public void ContainmentIncludesTheEdgesAndRejectsAnOverhang()
    {
        CanvasBounds box = new(0, 0, 100, 100);

        Assert.True(box.Contains(new CanvasBounds(10, 10, 20, 20)));
        Assert.True(box.Contains(box));
        Assert.True(box.Contains(new CanvasBounds(0, 0, 100, 20)));

        Assert.False(box.Contains(new CanvasBounds(-1, 10, 20, 20)));
        Assert.False(box.Contains(new CanvasBounds(10, 10, 101, 20)));

        // And an overhanging rectangle is still caught by the crossing rule, which is the whole
        // point of there being two.
        Assert.True(box.Intersects(new CanvasBounds(10, 10, 101, 20)));
    }

    private static int[] Collect(SceneIndex.VisibleEnumerator enumerator)
    {
        List<int> slots = [];
        foreach (int slot in enumerator)
        {
            slots.Add(slot);
        }

        return [.. slots];
    }
}

using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="PolyCurve.FromGroupedCurves"/> — `E2-T72`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the reversal.</b> A heap of curves has no direction, so a link that continues a
/// chain may have been drawn the other way round — and that is the ordinary case rather than the
/// awkward one. An implementation that only matches end to start chains the links that happen to
/// agree and leaves the rest as singletons, which looks like a grouping failure and is an
/// orientation failure.
/// </para>
/// <para>
/// <b>Everything here is asserted on the groups, not on the walk.</b> How many chains came back,
/// how long each is, and that each runs unbroken — which is what a caller sees.
/// </para>
/// </remarks>
public sealed class PolyCurveGroupingTests
{
    /// <summary>Two chains that do not touch come back as two.</summary>
    [Fact]
    public void SeparateChainsComeBackSeparately()
    {
        Curve[] heap =
        [
            Line((0, 0), (1, 0)),
            Line((1, 0), (2, 0)),
            Line((10, 0), (11, 0)),
            Line((11, 0), (12, 0)),
        ];

        PolyCurve[] groups = PolyCurve.FromGroupedCurves(heap);

        Assert.Equal(2, groups.Length);
        Assert.Equal(2, groups[0].SegmentCount);
        Assert.Equal(2, groups[1].SegmentCount);
    }

    /// <summary>
    /// <b>The branch.</b> Every link after the first is drawn backwards, which is what a heap
    /// gathered from a drawing looks like. One chain, running unbroken.
    /// </summary>
    [Fact]
    public void LinksDrawnTheOtherWayRoundStillChain()
    {
        Curve[] heap =
        [
            Line((0, 0), (1, 0)),
            Line((2, 0), (1, 0)),   // backwards
            Line((2, 0), (3, 0)),
            Line((4, 0), (3, 0)),   // backwards
        ];

        PolyCurve[] groups = PolyCurve.FromGroupedCurves(heap);

        Assert.Single(groups);
        Assert.Equal(4, groups[0].SegmentCount);
        AssertContinuous(groups[0]);
        Assert.Equal(4.0, groups[0].Length, 1e-9);
    }

    /// <summary>
    /// A seed taken from the middle still yields the whole chain, because the walk goes both ways.
    /// </summary>
    [Fact]
    public void AChainIsFoundFromTheMiddleOutwards()
    {
        // The first curve in the heap is the middle link of the chain.
        Curve[] heap =
        [
            Line((1, 0), (2, 0)),
            Line((0, 0), (1, 0)),
            Line((2, 0), (3, 0)),
        ];

        PolyCurve[] groups = PolyCurve.FromGroupedCurves(heap);

        Assert.Single(groups);
        Assert.Equal(3, groups[0].SegmentCount);
        AssertContinuous(groups[0]);
        Assert.True(groups[0].StartPoint.DistanceTo(new Point3d(0, 0, 0)) < 1e-9);
        Assert.True(groups[0].EndPoint.DistanceTo(new Point3d(3, 0, 0)) < 1e-9);
    }

    /// <summary>The grouping is the input's, not the ordering's.</summary>
    [Fact]
    public void AShuffledHeapGivesTheSameGroups()
    {
        Curve[] ordered =
        [
            Line((0, 0), (1, 0)),
            Line((1, 0), (1, 1)),
            Line((1, 1), (0, 1)),
            Line((10, 0), (11, 0)),
            Line((11, 0), (11, 1)),
        ];

        Curve[] shuffled = [ordered[3], ordered[1], ordered[4], ordered[2], ordered[0]];

        PolyCurve[] first = PolyCurve.FromGroupedCurves(ordered);
        PolyCurve[] second = PolyCurve.FromGroupedCurves(shuffled);

        Assert.Equal(2, first.Length);
        Assert.Equal(2, second.Length);

        // Same two chains, by length, whichever order they came out in.
        double[] fromOrdered = [first[0].Length, first[1].Length];
        double[] fromShuffled = [second[0].Length, second[1].Length];
        Array.Sort(fromOrdered);
        Array.Sort(fromShuffled);

        Assert.Equal(fromOrdered[0], fromShuffled[0], 1e-9);
        Assert.Equal(fromOrdered[1], fromShuffled[1], 1e-9);
    }

    /// <summary>A ring closes and the walk stops rather than going round again.</summary>
    [Fact]
    public void ARingClosesAndTerminates()
    {
        Curve[] heap =
        [
            Line((0, 0), (4, 0)),
            Line((4, 0), (4, 4)),
            Line((4, 4), (0, 4)),
            Line((0, 4), (0, 0)),
        ];

        PolyCurve[] groups = PolyCurve.FromGroupedCurves(heap);

        Assert.Single(groups);
        Assert.Equal(4, groups[0].SegmentCount);
        Assert.Equal(16.0, groups[0].Length, 1e-9);
        Assert.True(groups[0].IsClosed, "the ring did not come back closed.");
    }

    /// <summary>A curve that touches nothing is a group of one, never dropped.</summary>
    [Fact]
    public void ALonelyCurveIsAGroupOfOne()
    {
        Curve[] heap =
        [
            Line((0, 0), (1, 0)),
            Line((50, 50), (51, 50)),
            Line((1, 0), (2, 0)),
        ];

        PolyCurve[] groups = PolyCurve.FromGroupedCurves(heap);

        Assert.Equal(2, groups.Length);
        Assert.Equal(2, groups[0].SegmentCount);
        Assert.Equal(1, groups[1].SegmentCount);
    }

    /// <summary>Groups come back in the order their first curve appeared.</summary>
    [Fact]
    public void GroupsComeBackInTheOrderTheirFirstCurveAppeared()
    {
        Curve[] heap =
        [
            Line((20, 0), (21, 0)),
            Line((0, 0), (1, 0)),
            Line((1, 0), (2, 0)),
        ];

        PolyCurve[] groups = PolyCurve.FromGroupedCurves(heap);

        Assert.Equal(2, groups.Length);
        Assert.True(
            groups[0].StartPoint.DistanceTo(new Point3d(20, 0, 0)) < 1e-9,
            $"the first group starts at {groups[0].StartPoint}, not at the first curve in the heap.");
    }

    /// <summary>
    /// <b>A branching junction is resolved by input order, which is deterministic and arbitrary.</b>
    /// The test pins the determinism, which is the half worth relying on.
    /// </summary>
    [Fact]
    public void ABranchIsResolvedTheSameWayEveryTime()
    {
        Curve[] heap =
        [
            Line((0, 0), (1, 0)),
            Line((1, 0), (2, 0)),
            Line((1, 0), (1, 1)),   // a third curve at the same junction
        ];

        PolyCurve[] first = PolyCurve.FromGroupedCurves(heap);
        PolyCurve[] again = PolyCurve.FromGroupedCurves(heap);

        Assert.Equal(first.Length, again.Length);
        for (int index = 0; index < first.Length; index++)
        {
            Assert.Equal(first[index].Length, again[index].Length, 1e-12);
            Assert.Equal(first[index].SegmentCount, again[index].SegmentCount);
        }

        // Nothing is lost at a branch: every curve handed in is in some group.
        int segments = 0;
        foreach (PolyCurve group in first)
        {
            segments += group.SegmentCount;
        }

        Assert.Equal(heap.Length, segments);
    }

    /// <summary>The tolerance decides what counts as joined, and it is passed rather than assumed.</summary>
    [Fact]
    public void TheToleranceDecidesWhatCountsAsJoined()
    {
        Curve[] heap =
        [
            Line((0, 0), (1, 0)),
            Line((1.01, 0), (2, 0)),
        ];

        Assert.Equal(2, PolyCurve.FromGroupedCurves(heap).Length);
        Assert.Single(PolyCurve.FromGroupedCurves(heap, new Tolerance(0.1, Angle.FromDegrees(1), 1e-12)));
    }

    [Fact]
    public void AnEmptyHeapGivesNoGroups() =>
        Assert.Empty(PolyCurve.FromGroupedCurves([]));

    [Fact]
    public void ANullHeapAndANullCurveAreBothRefused()
    {
        Assert.Throws<ArgumentNullException>(() => PolyCurve.FromGroupedCurves(null!));
        Assert.Throws<ArgumentNullException>(
            () => PolyCurve.FromGroupedCurves([Line((0, 0), (1, 0)), null!]));
    }

    /// <summary>The chain runs unbroken from piece to piece.</summary>
    /// <param name="chain">The chain.</param>
    private static void AssertContinuous(PolyCurve chain)
    {
        Curve[] pieces = chain.Segments();

        for (int index = 1; index < pieces.Length; index++)
        {
            double gap = pieces[index - 1].EndPoint.DistanceTo(pieces[index].StartPoint);

            Assert.True(gap < 1e-9, $"pieces {index - 1} and {index} are {gap} apart.");
        }
    }

    /// <summary>A line between two points in the world XY plane.</summary>
    /// <param name="from">Where it starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <returns>The line.</returns>
    private static Line Line((double X, double Y) from, (double X, double Y) to) =>
        new(new Point3d(from.X, from.Y, 0.0), new Point3d(to.X, to.Y, 0.0));
}

using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The evaluation cache's two bounds, and the estimator the byte bound is built on.
/// </summary>
public sealed class EvaluationCacheTests
{
    [Fact]
    public void TheEntryCeilingStillHolds()
    {
        EvaluationCache cache = new(capacity: 3);

        for (int index = 0; index < 10; index++)
        {
            cache.Set(Key(index), Result(1.0));
        }

        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void TheByteBudgetEvictsWhereTheEntryCeilingWouldNot()
    {
        // Room for four thousand entries and room for about two of these, which is the case the
        // count bound cannot see: four thousand meshes and four thousand numbers are the same
        // cache by count and are not the same cache.
        long one = GraphValueSize.Estimate(Result(BigList(500)));
        EvaluationCache cache = new(capacity: 4096, byteBudget: (2 * one) + 1);

        for (int index = 0; index < 10; index++)
        {
            cache.Set(Key(index), Result(BigList(500)));
        }

        Assert.InRange(cache.Count, 1, 2);
        Assert.True(cache.Bytes <= cache.ByteBudget);
    }

    [Fact]
    public void BytesTracksWhatIsHeldAcrossInsertsReplacementsAndEviction()
    {
        EvaluationCache cache = new(capacity: 2);

        Assert.Equal(0, cache.Bytes);

        cache.Set(Key(0), Result(1.0));
        long afterOne = cache.Bytes;
        Assert.True(afterOne > 0);

        cache.Set(Key(1), Result(1.0));
        Assert.Equal(2 * afterOne, cache.Bytes);

        // Replacing an entry must subtract the old size before adding the new one, or the total
        // drifts upward forever and the cache starts evicting for no reason.
        cache.Set(Key(0), Result(1.0));
        Assert.Equal(2 * afterOne, cache.Bytes);

        cache.Set(Key(2), Result(1.0));
        Assert.Equal(2, cache.Count);
        Assert.Equal(2 * afterOne, cache.Bytes);
    }

    [Fact]
    public void AResultLargerThanTheWholeBudgetIsKeptAndTheBudgetIsKnowinglyExceeded()
    {
        EvaluationCache cache = new(capacity: 16, byteBudget: 8);

        cache.Set(Key(0), Result(BigList(100)));

        // Evicting it would empty the cache and then evict the thing just computed, so the next
        // run recomputes it and the cycle repeats: a cache that costs its budget in work and
        // returns nothing.
        Assert.Equal(1, cache.Count);
        Assert.True(cache.Bytes > cache.ByteBudget);
        Assert.True(cache.TryGet(Key(0), out _));
    }

    [Fact]
    public void EvictionIsByLastUseRatherThanByInsertionOrder()
    {
        EvaluationCache cache = new(capacity: 2);

        cache.Set(Key(0), Result(1.0));
        cache.Set(Key(1), Result(1.0));

        // Touching the older entry makes it the newest, so the other one goes.
        Assert.True(cache.TryGet(Key(0), out _));

        cache.Set(Key(2), Result(1.0));

        Assert.True(cache.TryGet(Key(0), out _));
        Assert.False(cache.TryGet(Key(1), out _));
    }

    [Fact]
    public void ClearingResetsTheRunningTotalAsWellAsTheEntries()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), Result(BigList(50)));
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void BothBoundsMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(byteBudget: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(byteBudget: -1));
    }

    [Fact]
    public void TheEstimatorChargesTheTypesItKnowsAndAFlatFeeForTheRest()
    {
        Assert.Equal(0, GraphValueSize.Estimate(value: null));
        Assert.Equal(8, GraphValueSize.Estimate(1.0));
        Assert.Equal(24, GraphValueSize.Estimate(new Point3d(1.0, 2.0, 3.0)));
        Assert.Equal(128, GraphValueSize.Estimate(Transform.Identity));

        // A type the estimator has never heard of is charged a small flat fee: zero would let a
        // graph full of unknown values fill memory while the cache reported nothing, and a large
        // guess would evict real results to make room for an imagined cost.
        Assert.Equal(GraphValueSize.UnknownValue, GraphValueSize.Estimate(new object()));
    }

    [Fact]
    public void AListIsChargedForItsPointersAndForWhatIsBehindThem()
    {
        SparkList list = SparkList.Of(1.0, 2.0, 3.0);

        // Three pointers at eight bytes, three boxed doubles at eight, plus the list itself.
        Assert.Equal(GraphValueSize.ObjectOverhead + (8 * 3) + (8 * 3), GraphValueSize.Estimate(list));
    }

    [Fact]
    public void ANestedListIsWalkedRatherThanCountedOnce()
    {
        SparkList inner = SparkList.Of(1.0, 2.0, 3.0);
        SparkList outer = new([inner, inner], 2);

        Assert.True(GraphValueSize.Estimate(outer) > 2 * GraphValueSize.Estimate(inner));
    }

    [Fact]
    public void SharingIsNotSeenAndTheErrorIsInTheSafeDirection()
    {
        // The same curve twice is charged twice. The estimator says so on its own documentation:
        // finding out would need identity tracking across the whole cache, and believing the
        // cache holds more than it does makes it evict sooner rather than later.
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        SparkList shared = SparkList.Of(line, line);

        Assert.Equal(GraphValueSize.Estimate(SparkList.Of(line, new Line(Point3d.Origin, new Point3d(2.0, 0.0, 0.0)))),
            GraphValueSize.Estimate(shared));
    }

    [Fact]
    public void APolyLineCostsMoreThanALineBecauseItHoldsMore()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        PolyLine polyLine = PolyLine.ByPoints(
            [.. Enumerable.Range(0, 500).Select(index => new Point3d(index, 0.0, 0.0))]);

        Assert.True(GraphValueSize.Estimate(polyLine) > 10 * GraphValueSize.Estimate(line));
    }

    [Fact]
    public void ADeeplyNestedListStopsRatherThanRunningOutOfStack()
    {
        SparkList nested = SparkList.Of(1.0);

        for (int depth = 0; depth < 500; depth++)
        {
            nested = new SparkList([nested], depth + 2);
        }

        // The estimate is a number rather than a StackOverflowException, which is the only
        // assertion available: a stack overflow cannot be caught and would take the run with it.
        Assert.True(GraphValueSize.Estimate(nested) > 0);
    }

    [Fact]
    public void DiagnosticsAreChargedForRatherThanBeingFree()
    {
        CachedResult without = new([1.0], []);
        CachedResult with = new(
            [1.0],
            [DiagnosticCodes.Create(DiagnosticSeverity.Warning, "SPK1013", "A long enough message to notice.")]);

        Assert.True(GraphValueSize.Estimate(with) > GraphValueSize.Estimate(without));
    }

    private static CacheKey Key(int seed) => CacheKey.For(
        Definition,
        LacingMode.Longest,
        Tolerance.Default,
        runEpoch: seed,
        [CacheKeyInput.Unwired((double)seed)]);

    private static CachedResult Result(object? value) => new([value], []);

    private static SparkList BigList(int count) =>
        new([.. Enumerable.Range(0, count).Select(index => (object?)(double)index)], 1);

    private static readonly NodeDefinition Definition = new(
        new NodeKey("Test", "Cached"),
        "Cached",
        [new PortDefinition("in", typeof(double), 0)],
        [new PortDefinition("out", typeof(double), 0)],
        arguments => [arguments[0]],
        LacingMode.Longest,
        version: 1,
        isSideEffect: true);
}

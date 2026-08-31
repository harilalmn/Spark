using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The cache's two ceilings, without a kernel provider anywhere.
/// </summary>
/// <remarks>
/// <b>A fake residency is legitimate here and would not be in the provider's own tests.</b> What is
/// under test is the eviction arithmetic — that a native total is accumulated, subtracted on
/// eviction, and enforced independently of the entry count. Whether the number is real is a
/// question about the provider, and <c>Spark.Geometry.Occt.Tests</c> answers it against shapes
/// OpenCascade is actually holding.
/// </remarks>
public sealed class EvaluationCacheTests
{
    /// <summary>A residency that holds nothing and claims a size.</summary>
    private sealed class Weight(long bytes) : BrepResidency
    {
        public override long NativeBytes { get; } = bytes;

        public override BrepData Materialise() =>
            new([], [], [], [], [], [], [], [], []);

        public override void Dispose()
        {
        }
    }

    private static readonly NodeDefinition Definition = new(
        new NodeKey("Test", "Held"),
        "Held",
        [],
        [new PortDefinition("value", typeof(object), 0)],
        _ => [null]);

    private static CacheKey Key(int index) =>
        CacheKey.For(
            Definition,
            LacingMode.Longest,
            Tolerance.Default,
            runEpoch: 0,
            [CacheKeyInput.Unwired((double)index)]);

    private static CachedResult Holding(long bytes) =>
        new([new Brep(new Weight(bytes))], []);

    [Fact]
    public void AnEmptyCacheHoldsNothingAndOwesNothing()
    {
        EvaluationCache cache = new();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0L, cache.NativeBytes);
    }

    [Fact]
    public void TheNativeTotalIsTheSumOfWhatIsHeld()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), Holding(100));
        cache.Set(Key(1), Holding(250));

        Assert.Equal(350L, cache.NativeBytes);
    }

    /// <summary>Replacing an entry subtracts the old figure before adding the new one.</summary>
    [Fact]
    public void ReplacingAnEntryDoesNotDoubleCountIt()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), Holding(100));
        cache.Set(Key(0), Holding(40));

        Assert.Equal(1, cache.Count);
        Assert.Equal(40L, cache.NativeBytes);
    }

    /// <summary>
    /// <b>NFR-4's failure, made to happen.</b> Ten entries against a ceiling of a thousand: the
    /// count is nowhere near full and the cache evicts anyway, because what it holds is over
    /// budget. Without the native ceiling this test could not be written at all.
    /// </summary>
    [Fact]
    public void TheCacheEvictsOnBytesWithTheCountNowhereNearItsCeiling()
    {
        EvaluationCache cache = new(capacity: 1000, nativeBudget: 300);

        for (int i = 0; i < 10; i++)
        {
            cache.Set(Key(i), Holding(100));
        }

        Assert.Equal(3, cache.Count);
        Assert.Equal(300L, cache.NativeBytes);
    }

    /// <summary>And the entry ceiling still works on its own.</summary>
    [Fact]
    public void TheCacheStillEvictsOnCountWhenNothingIsNative()
    {
        EvaluationCache cache = new(capacity: 3);

        for (int i = 0; i < 10; i++)
        {
            cache.Set(Key(i), new CachedResult([(double)i], []));
        }

        Assert.Equal(3, cache.Count);
        Assert.Equal(0L, cache.NativeBytes);
    }

    /// <summary>
    /// A single result larger than the whole budget is kept. Evicting it would make the next
    /// lookup miss on something that had just been computed, which is worse than no cache.
    /// </summary>
    [Fact]
    public void OneResultBiggerThanTheBudgetIsStillKept()
    {
        EvaluationCache cache = new(capacity: 1000, nativeBudget: 10);

        cache.Set(Key(0), Holding(10_000));

        Assert.Equal(1, cache.Count);
        Assert.Equal(10_000L, cache.NativeBytes);
    }

    /// <summary>Eviction is by last use, so a lookup protects an entry from the budget too.</summary>
    [Fact]
    public void UsingAnEntryProtectsItFromTheNativeBudget()
    {
        EvaluationCache cache = new(capacity: 1000, nativeBudget: 200);

        cache.Set(Key(0), Holding(100));
        cache.Set(Key(1), Holding(100));

        Assert.True(cache.TryGet(Key(0), out _));

        cache.Set(Key(2), Holding(100));

        Assert.True(cache.TryGet(Key(0), out _), "the entry that was used was evicted");
        Assert.False(cache.TryGet(Key(1), out _), "the entry that was not used survived");
    }

    [Fact]
    public void ClearingResetsBoth()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), Holding(500));
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0L, cache.NativeBytes);
    }

    /// <summary>Nesting is followed: a solid inside a list inside a Displayable still counts.</summary>
    [Fact]
    public void NestedValuesAreCounted()
    {
        EvaluationCache cache = new();

        List<object> nested =
        [
            new Displayable(new Brep(new Weight(70)), Appearance.Default),
            new List<object> { new Brep(new Weight(30)) },
        ];

        cache.Set(Key(0), new CachedResult([nested], []));

        Assert.Equal(100L, cache.NativeBytes);
    }

    /// <summary>A string is enumerable and is not a list of solids. It must not be walked.</summary>
    [Fact]
    public void AStringIsNotWalkedForSolids()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), new CachedResult(["a string that is also a sequence of characters"], []));

        Assert.Equal(0L, cache.NativeBytes);
    }

    [Fact]
    public void ANonPositiveBudgetIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(nativeBudget: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(nativeBudget: -1));
    }
}

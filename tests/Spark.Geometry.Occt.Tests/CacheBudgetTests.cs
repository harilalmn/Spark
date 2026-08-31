using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// NFR-4: the evaluation cache evicts on the memory a provider is holding, not on entry count.
/// </summary>
/// <remarks>
/// <b>These live here rather than in <c>Spark.Engine.Tests</c> because the thing under test only
/// exists when a provider does.</b> A fake residency reporting a made-up number would exercise the
/// arithmetic and prove nothing about whether the number is real; these use shapes OpenCascade is
/// actually holding, and read the figure it actually reports.
/// </remarks>
public sealed class CacheBudgetTests
{
    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    private static Brep Resident(int seed) =>
        Kernel.Union(
            BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4),
            BrepPrimitives.Box(
                Plane.ByOriginXAxisYAxis(new Point3d(1, 1, seed * 0.25), Vector3d.XAxis, Vector3d.YAxis),
                2,
                3,
                4),
            Fine).Value;

    /// <summary>
    /// A distinct key per index. The run epoch is what varies, because it is the one part of a
    /// provenance key a test can move without inventing a different node.
    /// </summary>
    private static CacheKey Key(int index) =>
        CacheKey.For(
            Definition,
            LacingMode.Longest,
            Tolerance.Default,
            runEpoch: 0,
            [CacheKeyInput.Unwired((double)index)]);

    private static readonly NodeDefinition Definition = new(
        new NodeKey("Test", "Held"),
        "Held",
        [],
        [new PortDefinition("solid", typeof(Brep), 0)],
        _ => [null]);

    [NativeFact]
    public void AResidentShapeIsCountedAgainstTheBudget()
    {
        EvaluationCache cache = new();
        Brep shape = Resident(1);

        Assert.True(shape.NativeBytes > 0, "the provider reported no memory");

        cache.Set(Key(0), new CachedResult([shape], []));

        Assert.Equal(shape.NativeBytes, cache.NativeBytes);
    }

    /// <summary>
    /// <b>The failure NFR-4 names, made to happen.</b> Twenty entries against a ceiling of a
    /// thousand: the count is nowhere near full, and the cache evicts anyway, because the shapes
    /// behind those twenty entries are over the memory budget.
    /// </summary>
    [NativeFact]
    public void TheCacheEvictsOnNativeBytesWithTheCountNowhereNearItsCeiling()
    {
        Brep sample = Resident(1);
        long each = sample.NativeBytes;

        Assert.True(each > 0);

        // A budget that holds three of them and not four.
        EvaluationCache cache = new(capacity: 1000, nativeBudget: (each * 7) / 2);

        for (int i = 0; i < 20; i++)
        {
            cache.Set(Key(i), new CachedResult([Resident(i)], []));
        }

        Assert.True(cache.Count < 20, $"nothing was evicted: {cache.Count} entries held");
        Assert.True(cache.Count >= 1, "everything was evicted");
        Assert.True(
            cache.NativeBytes <= (each * 7) / 2,
            $"the cache holds {cache.NativeBytes} bytes against a budget of {(each * 7) / 2}");
    }

    /// <summary>
    /// A single result larger than the whole budget is kept. Evicting it would make every lookup
    /// miss on something that had just been computed.
    /// </summary>
    [NativeFact]
    public void OneResultBiggerThanTheBudgetIsStillKept()
    {
        Brep shape = Resident(1);
        EvaluationCache cache = new(capacity: 1000, nativeBudget: 1);

        cache.Set(Key(0), new CachedResult([shape], []));

        Assert.Equal(1, cache.Count);
    }

    [NativeFact]
    public void ClearingResetsTheBudget()
    {
        EvaluationCache cache = new();
        cache.Set(Key(0), new CachedResult([Resident(1)], []));

        Assert.True(cache.NativeBytes > 0);

        cache.Clear();

        Assert.Equal(0L, cache.NativeBytes);
    }

    /// <summary>Nesting is followed: a solid inside a list inside a Displayable still counts.</summary>
    [NativeFact]
    public void ASolidInsideAListInsideADisplayableIsCounted()
    {
        EvaluationCache cache = new();
        Brep shape = Resident(1);

        List<object> nested = [new Displayable(shape, Appearance.Default)];
        cache.Set(Key(0), new CachedResult([nested], []));

        Assert.Equal(shape.NativeBytes, cache.NativeBytes);
    }
}

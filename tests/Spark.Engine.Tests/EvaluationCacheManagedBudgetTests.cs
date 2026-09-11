using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The cache's managed-memory budget (`E3-T9`).
/// </summary>
/// <remarks>
/// <para>
/// <b>What was missing.</b> The cache already evicted against an entry count and a native-memory
/// budget; a managed value weighed nothing but its one entry, so a two-million-triangle mesh cost the
/// same as a number, and 4,096 of them was within every ceiling the cache had.
/// </para>
/// <para>
/// <b>The tests learn a value's weight from the cache rather than restating the formula.</b> The
/// estimate is deliberately rough and may be tuned; what is under test is that it grows with the
/// value, that it is enforced independently of the other two ceilings, and that it never forces a
/// shape the provider holds out into managed memory to be weighed.
/// </para>
/// </remarks>
public sealed class EvaluationCacheManagedBudgetTests
{
    private static readonly NodeDefinition Definition = new(
        new NodeKey("Test", "Weighed"),
        "Weighed",
        [],
        [new PortDefinition("value", typeof(object), 0)],
        _ => [null]);

    private static CacheKey Key(int index) =>
        CacheKey.For(Definition, LacingMode.Longest, Tolerance.Default, runEpoch: 0, [CacheKeyInput.Unwired((double)index)]);

    /// <summary>A square grid mesh with <paramref name="side"/> vertices along each edge.</summary>
    private static Mesh Grid(int side)
    {
        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        for (int j = 0; j < side; j++)
        {
            for (int i = 0; i < side; i++)
            {
                vertices.Add(new Point3d(i, j, 0));
            }
        }

        for (int j = 0; j + 1 < side; j++)
        {
            for (int i = 0; i + 1 < side; i++)
            {
                int a = (j * side) + i;
                faces.Add(new MeshFace(a, a + 1, a + side + 1, a + side));
            }
        }

        return new Mesh(vertices, faces);
    }

    private static long WeightOf(object value)
    {
        EvaluationCache probe = new();
        probe.Set(Key(0), new CachedResult([value], []));

        return probe.ManagedBytes;
    }

    /// <summary>A mesh a hundred times larger weighs far more, which is the estimate doing its job.</summary>
    [Fact]
    public void ALargerMeshWeighsMore()
    {
        long small = WeightOf(Grid(10));
        long large = WeightOf(Grid(100));

        Assert.True(small > 0, "a mesh weighed nothing");
        Assert.True(large > 50 * small, $"a mesh a hundred times larger weighed {large} against {small}");
    }

    /// <summary>
    /// <b>The row.</b> Ten meshes against a ceiling of a thousand entries: the count is nowhere near
    /// full, nothing is native, and the cache evicts anyway because what it holds is over its managed
    /// budget.
    /// </summary>
    [Fact]
    public void TheCacheEvictsOnManagedBytesWithTheCountNowhereNearItsCeiling()
    {
        Mesh mesh = Grid(50);
        long one = WeightOf(mesh);

        EvaluationCache cache = new(capacity: 1000, managedBudget: (3 * one) + (one / 2));

        for (int i = 0; i < 10; i++)
        {
            cache.Set(Key(i), new CachedResult([mesh], []));
        }

        Assert.Equal(3, cache.Count);
        Assert.Equal(3 * one, cache.ManagedBytes);
        Assert.Equal(0L, cache.NativeBytes);
    }

    /// <summary>Replacing an entry subtracts its old weight, and clearing forgets all of it.</summary>
    [Fact]
    public void TheManagedTotalFollowsReplacementAndClearing()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), new CachedResult([Grid(40)], []));
        cache.Set(Key(0), new CachedResult([Grid(4)], []));

        Assert.Equal(WeightOf(Grid(4)), cache.ManagedBytes);

        cache.Clear();

        Assert.Equal(0L, cache.ManagedBytes);
    }

    /// <summary>
    /// <b>A shape the provider holds is never materialised to be weighed.</b> Its counts would read
    /// the managed arrays and pull the whole shape across — the conversion ADR-0021 exists to avoid.
    /// </summary>
    [Fact]
    public void AResidentShapeIsNotMaterialisedToBeWeighed()
    {
        EvaluationCache cache = new();

        cache.Set(Key(0), new CachedResult([new Brep(new Tripwire())], []));

        Assert.Equal(1, cache.Count);
        Assert.True(cache.ManagedBytes < 1024, $"a resident shape weighed {cache.ManagedBytes} managed bytes");
    }

    /// <summary>A budget that is not positive is refused, as the other two are.</summary>
    [Fact]
    public void ANonPositiveManagedBudgetIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(managedBudget: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationCache(managedBudget: -1));
    }

    /// <summary>A residency that fails the test if anything asks it to become managed arrays.</summary>
    private sealed class Tripwire : BrepResidency
    {
        public override long NativeBytes => 4096;

        public override BrepData Materialise() =>
            throw new InvalidOperationException("the cache materialised a resident shape to weigh it");

        public override void Dispose()
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spark.Api;
using Spark.Geometry;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// Tessellation in parallel inside <see cref="SceneBuilder"/> (`E9-T7`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim that matters is that parallel changes nothing but the time.</b> Each drawable is
/// tessellated on its own and emitted afterwards in key order, so a build on one thread and a build
/// on many produce the same packages, byte for byte and in the same order. A viewport whose
/// buffers depended on thread scheduling would flicker between runs of an unchanged graph.
/// </para>
/// <para>
/// <b>That it is parallel at all is observed, not timed.</b> Two surfaces meet at a barrier inside
/// the tessellation pass; only two threads working at once can both get through it. A stopwatch
/// would pass on a fast machine and fail on a loaded one.
/// </para>
/// </remarks>
public sealed class SceneBuilderParallelTests
{
    /// <summary>Six keys, each a list of spheres, circles and points of different sizes.</summary>
    private static SceneBuilder Scene(int parallelism)
    {
        SceneBuilder builder = new() { MaximumParallelism = parallelism };

        for (int node = 0; node < 6; node++)
        {
            List<object?> values = [];

            for (int i = 0; i < 4; i++)
            {
                double radius = 1.0 + node + (i * 0.25);
                values.Add(new SphericalSurface(Plane.WorldXY, radius));
                values.Add(Circle.FromCenterRadius(Point3d.Origin, radius));
                values.Add(new Point3d(radius, node, i));
            }

            builder.Add(new GeometryKey($"node-{node}", 0), new SparkList(values, 1));
        }

        return builder;
    }

    /// <summary>
    /// <b>The row's claim.</b> A build on many threads is the build on one: the same keys in the same
    /// order, and the same positions, normals, triangles and edges in each package.
    /// </summary>
    [Fact]
    public void AParallelBuildIsTheSerialBuild()
    {
        SceneBuilder serial = Scene(1);
        SceneBuilder parallel = Scene(Math.Max(4, Environment.ProcessorCount));

        IReadOnlyList<RenderPackage> expected = serial.Build();
        IReadOnlyList<RenderPackage> actual = parallel.Build();

        Assert.Equal(6, expected.Count);
        Assert.Equal(expected.Select(package => package.Key), actual.Select(package => package.Key));

        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i].TriangleCount > 0, $"package {i} has no triangles");
            Assert.Equal(expected[i].Positions.ToArray(), actual[i].Positions.ToArray());
            Assert.Equal(expected[i].Normals.ToArray(), actual[i].Normals.ToArray());
            Assert.Equal(expected[i].Indices.ToArray(), actual[i].Indices.ToArray());
            Assert.Equal(expected[i].EdgeIndices.ToArray(), actual[i].EdgeIndices.ToArray());
        }

        Assert.Equal(serial.RenderableCount, parallel.RenderableCount);
        Assert.Equal(serial.UnrenderableCount, parallel.UnrenderableCount);
    }

    /// <summary>
    /// Two surfaces are tessellated at the same time: each waits at a barrier for the other, which a
    /// one-thread build would never let it reach.
    /// </summary>
    [Fact]
    public void TwoSurfacesAreTessellatedAtOnce()
    {
        using Barrier barrier = new(2);
        int met = 0;

        SceneBuilder builder = new() { MaximumParallelism = 2 };
        builder.Preparing = () =>
        {
            if (barrier.SignalAndWait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            {
                Interlocked.Increment(ref met);
            }
        };

        builder.Add(new GeometryKey("a", 0), new SphericalSurface(Plane.WorldXY, 1.0));
        builder.Add(new GeometryKey("b", 0), new SphericalSurface(Plane.WorldXY, 2.0));

        Assert.Equal(2, builder.Build().Count);
        Assert.Equal(2, met);
    }

    /// <summary>A parallelism below one means nothing and is refused.</summary>
    [Fact]
    public void AParallelismBelowOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneBuilder { MaximumParallelism = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneBuilder { MaximumParallelism = -3 });
    }
}

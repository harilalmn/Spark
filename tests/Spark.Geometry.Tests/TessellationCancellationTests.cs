using System;
using System.Threading;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Tessellation stops when asked to (`E3-T12`).
/// </summary>
/// <remarks>
/// <para>
/// <b>The evaluator already stops between nodes and between replication elements</b>; what it could
/// not stop was one expensive element, because no kernel loop took a token. A surface asked for at a
/// tolerance far below its size runs to the sample cap — a quarter of a million quads — and until
/// now ran there whatever the user pressed.
/// </para>
/// <para>
/// <b>Deterministic, not timed.</b> A test that cancels after some milliseconds and asserts the work
/// stopped "soon" is a test that fails on a slow machine and passes on a broken one. The mid-grid test
/// instead cancels from inside the sink, on the first vertex, and asserts the grid stopped within one
/// row of it.
/// </para>
/// </remarks>
public sealed class TessellationCancellationTests
{
    /// <summary>Fine enough to hit the sample cap on a unit sphere: the loop worth stopping.</summary>
    private static readonly Tolerance Absurd = new(1e-9, Angle.FromDegrees(1), 1e-12);

    private static SphericalSurface Sphere => new(Plane.WorldXY, 1.0);

    /// <summary>A token cancelled before the call stops it before any work, and returns no mesh.</summary>
    [Fact]
    public void AnAlreadyCancelledTokenStopsBeforeAnyWork()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Counting sink = new(cancelAfter: int.MaxValue, cancelled);

        Assert.ThrowsAny<OperationCanceledException>(
            () => Tessellation.Tessellate(Sphere, sink, Absurd, cancelled.Token));

        Assert.Equal(0, sink.Vertices);
        Assert.ThrowsAny<OperationCanceledException>(() => Sphere.ToMesh(Absurd, cancelled.Token));
    }

    /// <summary>
    /// <b>The row.</b> Cancelled from inside the grid, the tessellation stops within one row of the
    /// request — not at the end of a quarter of a million quads.
    /// </summary>
    [Fact]
    public void CancellingMidGridStopsWithinARow()
    {
        using CancellationTokenSource source = new();
        Counting sink = new(cancelAfter: 1, source);

        Assert.ThrowsAny<OperationCanceledException>(
            () => Tessellation.Tessellate(Sphere, sink, Absurd, source.Token));

        // One row of the grid is at most the sample cap; the whole grid is its square.
        Assert.InRange(sink.Vertices, 1, Tessellation.MaximumSamplesPerDirection);
        Assert.Equal(0, sink.Faces);
    }

    /// <summary>The form without a token produces exactly the mesh it always did.</summary>
    [Fact]
    public void TheFormWithoutATokenIsUnchanged()
    {
        Tolerance ordinary = new(0.01, Angle.FromDegrees(5), 1e-12);

        // The token-free form, on purpose: it is the signature every existing caller compiled
        // against, and this test is the one place that must call it rather than the other.
#pragma warning disable xUnit1051
        Mesh before = Sphere.ToMesh(ordinary);
#pragma warning restore xUnit1051
        Mesh after = Sphere.ToMesh(ordinary, TestContext.Current.CancellationToken);

        Assert.Equal(before.VertexCount, after.VertexCount);
        Assert.Equal(before.FaceCount, after.FaceCount);
    }

    /// <summary>A sink that counts, and cancels its token once it has seen enough vertices.</summary>
    private sealed class Counting(int cancelAfter, CancellationTokenSource source) : ITessellationSink
    {
        public int Vertices { get; private set; }

        public int Faces { get; private set; }

        public int AddVertex(in Point3d position, in Vector3d normal, in UV textureCoordinate)
        {
            Vertices++;

            if (Vertices >= cancelAfter)
            {
                source.Cancel();
            }

            return Vertices - 1;
        }

        public void AddTriangle(int a, int b, int c) => Faces++;

        public void AddQuad(int a, int b, int c, int d) => Faces++;
    }
}

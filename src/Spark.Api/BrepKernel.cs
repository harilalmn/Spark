using System;

namespace Spark.Api;

/// <summary>
/// Which BRep kernel the process is using.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ambient, and it is the one place in Spark that is.</b> A node is a plain public static
/// method discovered by reflection (ADR-0005) — it has no constructor to receive a kernel through,
/// and a kernel *parameter* would appear on the canvas as a port on every solid node, which is a
/// port nobody would ever wire. The alternatives are all worse than an ambient one: a service
/// locator passed through <c>EvaluationContext</c> would reach the replicator but not the node's
/// own signature, and a per-node attribute would put the dependency in metadata where it could not
/// be typed.
/// </para>
/// <para>
/// <b>It is set once, by the host, before any graph runs</b>, and it defaults to
/// <see cref="UnavailableBrepKernel"/> so that a process with no provider behaves like a process
/// whose provider can do nothing — which is exactly what it is. There is no unset state and
/// therefore no null check at any call site.
/// </para>
/// <para>
/// <b>Deliberately not per-session.</b> A provider owns native resources and, under
/// [ADR-0021](../../docs/adr/0021-brep-kernel-residency.md), shapes are resident inside it — so two
/// sessions with two providers would be two heaps whose shapes could not be mixed, and a shape
/// crossing between them would fail in a way nothing could explain. One process, one kernel.
/// </para>
/// </remarks>
public static class BrepKernel
{
    private static IBrepKernel _current = UnavailableBrepKernel.Instance;

    /// <summary>The kernel every solid operation goes through.</summary>
    public static IBrepKernel Current => _current;

    /// <summary>Installs a provider.</summary>
    /// <param name="kernel">The provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kernel"/> is null.</exception>
    /// <remarks>
    /// <b>Installing twice is allowed and installing <i>during</i> a run is not defended
    /// against.</b> The host calls this at startup; a test calls it to put a fake in place and puts
    /// the real one back. Guarding it with a lock or a one-way latch would buy nothing — a process
    /// that changed its kernel mid-evaluation has a problem that a latch would only rename.
    /// </remarks>
    public static void Install(IBrepKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        _current = kernel;
    }

    /// <summary>Puts the no-provider kernel back.</summary>
    public static void Reset() => _current = UnavailableBrepKernel.Instance;
}

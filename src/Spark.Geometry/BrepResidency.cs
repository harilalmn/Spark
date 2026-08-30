using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// The nine arrays a <see cref="Brep"/> is made of, as one value.
/// </summary>
/// <param name="Points">Vertex positions.</param>
/// <param name="Curves">The curves edges lie on.</param>
/// <param name="Surfaces">The surfaces faces lie on.</param>
/// <param name="Vertices">The vertices.</param>
/// <param name="Edges">The edges.</param>
/// <param name="Trims">The trims, contiguous per loop.</param>
/// <param name="Loops">The loops, contiguous per face.</param>
/// <param name="Faces">The faces, contiguous per shell.</param>
/// <param name="Shells">The shells.</param>
/// <remarks>
/// <b>It exists so that <see cref="BrepResidency.Materialise"/> can return a model without
/// returning a <see cref="Brep"/>.</b> A resident BRep materialises by asking its residency for its
/// contents; if that call handed back another <c>Brep</c>, the one it handed back could itself be
/// resident, and materialising would be defined in terms of itself.
/// </remarks>
public readonly record struct BrepData(
    IReadOnlyList<Point3d> Points,
    IReadOnlyList<Curve> Curves,
    IReadOnlyList<Surface> Surfaces,
    IReadOnlyList<BrepVertex> Vertices,
    IReadOnlyList<BrepEdge> Edges,
    IReadOnlyList<BrepTrim> Trims,
    IReadOnlyList<BrepLoop> Loops,
    IReadOnlyList<BrepFace> Faces,
    IReadOnlyList<BrepShell> Shells);

/// <summary>
/// A shape that lives in a kernel provider rather than in these arrays
/// ([ADR-0021](../../docs/adr/0021-brep-kernel-residency.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>Residency is canonical, not cached, and the reason is fidelity rather than speed.</b> A
/// round trip through a tolerant BRep kernel is <i>not</i> identity — it re-sews, re-tolerances and
/// may re-parameterise — so a chain of ten operations that converted in and out at each step would
/// make the user's geometry drift while they did nothing. After an operation the provider's
/// representation is authoritative and ours is materialised **lazily, on structural demand**: a
/// chain of ten operations performs zero imports and one materialisation.
/// </para>
/// <para>
/// <b>The token is opaque here on purpose.</b> This class is the entire vocabulary
/// <c>Spark.Geometry</c> has for *the shape is over there* — it cannot see the provider's handle,
/// and neither can <c>Spark.Api</c>. Only the provider assembly implements it.
/// </para>
/// <para>
/// <b>Its cost is real and is the price of the decision: a <see cref="Brep"/> stops being a pure
/// value.</b> It carries a finalizable native resource, with everything that implies — lifetime,
/// disposal, and a shape that can outlive the provider that made it. <see cref="NativeBytes"/>
/// exists because of one consequence in particular: an evaluation cache that evicts by *managed*
/// size cannot see native memory, so a graph holding two hundred resident shapes may be holding
/// gigabytes while reporting megabytes.
/// </para>
/// </remarks>
public abstract class BrepResidency : IDisposable
{
    /// <summary>Reads the shape out of the provider and into these arrays.</summary>
    /// <returns>The nine arrays.</returns>
    /// <remarks>
    /// <b>Called at most once per <see cref="Brep"/></b>, on the first structural demand — a
    /// topology query, a transform, a bounding box, serialisation, equality, or the value reaching
    /// a node that is not a kernel node.
    /// </remarks>
    public abstract BrepData Materialise();

    /// <summary>
    /// Roughly how much memory this shape occupies inside the provider.
    /// </summary>
    /// <remarks>
    /// An estimate, and it is allowed to be one: what the evaluation cache needs is a number that
    /// grows with the shape, not an audit. Zero is a legal answer from a provider that cannot say.
    /// </remarks>
    public abstract long NativeBytes { get; }

    /// <summary>Releases the provider's shape.</summary>
    public abstract void Dispose();
}

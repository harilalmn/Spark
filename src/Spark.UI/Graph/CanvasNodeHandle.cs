using Spark.Engine;

namespace Spark.UI.Graph;

/// <summary>
/// A durable reference to one node on the canvas, which a slot is not (<c>E8-T54</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A slot is an index into an array that renumbers.</b> Removing a node shifts every node after
/// it down one, so a slot held across a deletion names a <i>different</i> node — and if that node
/// happens to be the same shape, nothing notices. The canvas is addressed in slots because
/// everything it draws is, and that is right; what is wrong is holding one across an edit.
/// </para>
/// <para>
/// <b>It is opaque on purpose, and the opacity is a layering rule rather than a style.</b> The
/// identity underneath is the engine's <see cref="NodeId"/>, and no file under
/// <c>Spark.UI/Controls</c> or <c>Spark.UI/Views</c> may name <c>Spark.Engine</c> —
/// <c>ViewLayerTests.NoViewFileReferencesTheEngine</c> enforces it by reading the source, and it
/// caught the first version of `E8-T54`, which put a <see cref="NodeId"/> field on
/// <c>CanvasPane</c>. <c>CanvasGraph</c> is the seam that is allowed to know both, so the handle is
/// minted and redeemed there and a view holds something it cannot take apart.
/// </para>
/// </remarks>
public readonly record struct CanvasNodeHandle
{
    /// <summary>Wraps an identity. Only <see cref="CanvasGraph"/> mints one.</summary>
    /// <param name="id">The node's identity.</param>
    internal CanvasNodeHandle(NodeId id) => Id = id;

    /// <summary>The identity, readable only inside the seam that minted it.</summary>
    internal NodeId Id { get; }
}

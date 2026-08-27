using System;

namespace Spark.Viewport;

/// <summary>
/// Identifies a buffer set in the viewport. Geometry has no identity of its own by design; the
/// graph's <c>(NodeId, PortIndex)</c> tuple <i>is</i> the identity, which is what makes selection
/// synchronisation free and what makes re-evaluating one node re-upload one buffer.
/// </summary>
/// <param name="NodeId">
/// The identifier of the node that produced the geometry. Opaque to the viewport — it never
/// parses or orders it.
/// </param>
/// <param name="PortIndex">The zero-based index of the output port the geometry came from.</param>
public readonly record struct GeometryKey(string NodeId, int PortIndex);

/// <summary>
/// How a package is drawn. Kept deliberately small: appearance is a rendering instruction, not a
/// place for graph state to accumulate.
/// </summary>
/// <param name="Surface">The shaded surface colour at full lighting.</param>
/// <param name="Edge">The colour of the edge lines.</param>
/// <param name="IsSelected">
/// True when the object carries a selection outline. The outline is authoritative; the 15%
/// accent lighting tint the design language allows is never the only signal.
/// </param>
/// <param name="IsGhosted">
/// True when the object is outside the isolated preview set. Ghosted geometry is drawn
/// <b>edges only and unshaded</b>: the distinction is a rendering mode rather than a contrast
/// ratio, which is how §8.4's declared exception is discharged.
/// </param>
public readonly record struct Appearance(
    ViewportColor Surface,
    ViewportColor Edge,
    bool IsSelected,
    bool IsGhosted)
{
    /// <summary>The default appearance: <c>geometry.surface</c> shaded, <c>geometry.edge</c> edges.</summary>
    public static Appearance Default { get; } = new(
        ViewportPalette.GeometrySurface,
        ViewportPalette.GeometryEdge,
        IsSelected: false,
        IsGhosted: false);
}

/// <summary>
/// An immutable tessellated result destined for one GPU buffer set. Produced by tessellation
/// after a node completes, at a camera-derived level of detail, and handed to the viewport
/// whole — there is no partial update of a package, because a package <i>is</i> the unit of
/// update.
/// </summary>
/// <remarks>
/// <para>
/// Every array is copied on construction. The producer runs on a worker thread and the consumer
/// on the render thread, so sharing the arrays would make the package immutable only by
/// convention, and the first bug it caused would present as a flickering triangle rather than as
/// anything that looks like a race.
/// </para>
/// <para>
/// The arrays stay accessible inside this assembly, as spans to callers outside it. The renderer
/// needs a real array to pin for the GL upload, and a <c>ReadOnlySpan</c> cannot be pinned
/// without unsafe code, which is forbidden repository-wide.
/// </para>
/// </remarks>
public sealed class RenderPackage
{
    internal readonly float[] PositionData;
    internal readonly float[] NormalData;
    internal readonly int[] IndexData;
    internal readonly int[] EdgeIndexData;

    /// <summary>Creates a package, copying every array the caller supplies.</summary>
    /// <param name="key">The <c>(NodeId, PortIndex)</c> identity of the geometry.</param>
    /// <param name="elementPath">
    /// The path of the element inside the node's output — which item of a list, which face of a
    /// solid. Opaque to the viewport; it exists so a selection can be reported back precisely.
    /// </param>
    /// <param name="positions">Vertex positions as consecutive x, y, z triples.</param>
    /// <param name="normals">
    /// Vertex normals as consecutive x, y, z triples, the same length as
    /// <paramref name="positions"/>. Pass an empty array for a package with no shaded surface.
    /// </param>
    /// <param name="indices">
    /// Triangle indices, three per triangle, wound counter-clockwise seen from outside.
    /// </param>
    /// <param name="edgeIndices">Line indices, two per edge segment.</param>
    /// <param name="appearance">How the package is drawn.</param>
    /// <exception cref="ArgumentNullException">Any array argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="positions"/> is not a whole number of triples, or
    /// <paramref name="normals"/> is neither empty nor the same length as
    /// <paramref name="positions"/>.
    /// </exception>
    public RenderPackage(
        GeometryKey key,
        string elementPath,
        float[] positions,
        float[] normals,
        int[] indices,
        int[] edgeIndices,
        Appearance appearance)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(edgeIndices);
        ArgumentNullException.ThrowIfNull(elementPath);

        if (positions.Length % 3 != 0)
        {
            throw new ArgumentException("Positions must be a whole number of x, y, z triples.", nameof(positions));
        }

        if (normals.Length != 0 && normals.Length != positions.Length)
        {
            throw new ArgumentException(
                "Normals must either be empty or match the position count.", nameof(normals));
        }

        Key = key;
        ElementPath = elementPath;
        Appearance = appearance;
        PositionData = (float[])positions.Clone();
        NormalData = (float[])normals.Clone();
        IndexData = (int[])indices.Clone();
        EdgeIndexData = (int[])edgeIndices.Clone();
    }

    /// <summary>The <c>(NodeId, PortIndex)</c> identity of this geometry.</summary>
    public GeometryKey Key { get; }

    /// <summary>The path of the element inside the producing node's output.</summary>
    public string ElementPath { get; }

    /// <summary>How this package is drawn.</summary>
    public Appearance Appearance { get; }

    /// <summary>Vertex positions as consecutive x, y, z triples.</summary>
    public ReadOnlySpan<float> Positions => PositionData;

    /// <summary>Vertex normals as consecutive x, y, z triples, or empty.</summary>
    public ReadOnlySpan<float> Normals => NormalData;

    /// <summary>Triangle indices, three per triangle.</summary>
    public ReadOnlySpan<int> Indices => IndexData;

    /// <summary>Line indices, two per edge segment.</summary>
    public ReadOnlySpan<int> EdgeIndices => EdgeIndexData;

    /// <summary>The number of vertices.</summary>
    public int VertexCount => PositionData.Length / 3;

    /// <summary>The number of triangles.</summary>
    public int TriangleCount => IndexData.Length / 3;

    /// <summary>The number of edge segments.</summary>
    public int EdgeCount => EdgeIndexData.Length / 2;

    /// <summary>The same package with a different appearance, sharing the geometry arrays.</summary>
    /// <param name="appearance">The replacement appearance.</param>
    /// <returns>A new package.</returns>
    /// <remarks>
    /// Selecting an object must not re-tessellate it, and it must not re-upload its buffers
    /// either. This overload exists so the appearance change is the only thing that travels.
    /// </remarks>
    public RenderPackage WithAppearance(Appearance appearance) =>
        Appearance.Equals(appearance) ? this : new RenderPackage(this, appearance);

    private RenderPackage(RenderPackage source, Appearance appearance)
    {
        Key = source.Key;
        ElementPath = source.ElementPath;
        Appearance = appearance;
        PositionData = source.PositionData;
        NormalData = source.NormalData;
        IndexData = source.IndexData;
        EdgeIndexData = source.EdgeIndexData;
    }

    /// <summary>The axis-aligned bounds of this package's vertices.</summary>
    /// <returns>The bounds, empty when the package has no vertices.</returns>
    public Bounds3 ComputeBounds()
    {
        Bounds3 bounds = Bounds3.Empty;
        for (int i = 0; i + 2 < PositionData.Length; i += 3)
        {
            bounds = bounds.Union(new System.Numerics.Vector3(
                PositionData[i], PositionData[i + 1], PositionData[i + 2]));
        }

        return bounds;
    }
}

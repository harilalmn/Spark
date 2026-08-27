using System;

namespace Spark.Viewport.Meshes;

/// <summary>
/// A tessellated mesh in the layout the viewport uploads: interleaved-free parallel arrays of
/// positions and normals, a triangle index list and an edge index list.
/// </summary>
/// <remarks>
/// This is deliberately a plain carrier and not a geometric type. The kernel owns geometry; this
/// owns what a GPU needs. Keeping them separate is what stops rendering concerns leaking into
/// <c>Spark.Geometry</c>.
/// </remarks>
public sealed class Mesh
{
    /// <summary>Creates a mesh from arrays the caller must not mutate afterwards.</summary>
    /// <param name="positions">Positions as consecutive x, y, z triples.</param>
    /// <param name="normals">Normals as consecutive x, y, z triples, the same length.</param>
    /// <param name="indices">Triangle indices, three per triangle.</param>
    /// <param name="edgeIndices">Line indices, two per edge segment.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public Mesh(float[] positions, float[] normals, int[] indices, int[] edgeIndices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(edgeIndices);

        PositionData = positions;
        NormalData = normals;
        IndexData = indices;
        EdgeIndexData = edgeIndices;
    }

    /// <summary>Positions as consecutive x, y, z triples.</summary>
    public ReadOnlySpan<float> Positions => PositionData;

    /// <summary>Normals as consecutive x, y, z triples.</summary>
    public ReadOnlySpan<float> Normals => NormalData;

    /// <summary>Triangle indices, three per triangle.</summary>
    public ReadOnlySpan<int> Indices => IndexData;

    /// <summary>Line indices, two per edge segment.</summary>
    public ReadOnlySpan<int> EdgeIndices => EdgeIndexData;

    /// <summary>The number of vertices.</summary>
    public int VertexCount => PositionData.Length / 3;

    /// <summary>The number of triangles.</summary>
    public int TriangleCount => IndexData.Length / 3;

    internal float[] PositionData { get; }

    internal float[] NormalData { get; }

    internal int[] IndexData { get; }

    internal int[] EdgeIndexData { get; }

    /// <summary>Wraps this mesh in a render package under the given identity.</summary>
    /// <param name="key">The <c>(NodeId, PortIndex)</c> identity to file the geometry under.</param>
    /// <param name="elementPath">The element path inside the node's output.</param>
    /// <param name="appearance">How the package is drawn.</param>
    /// <returns>The package.</returns>
    public RenderPackage ToRenderPackage(GeometryKey key, string elementPath, Appearance appearance) =>
        new(key, elementPath, PositionData, NormalData, IndexData, EdgeIndexData, appearance);
}

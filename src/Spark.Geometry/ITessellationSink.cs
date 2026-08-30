using System;

namespace Spark.Geometry;

/// <summary>
/// Where a tessellator puts its output (`E2-T26`).
/// </summary>
/// <remarks>
/// <para>
/// <b>A sink rather than a returned <see cref="Mesh"/>, and the reason is the viewport.</b> A
/// tessellator that returns a mesh has to allocate the whole thing before anything can be drawn,
/// and then the renderer copies it again into the buffers it actually uses — twice the memory and
/// twice the walk, at the moment a user is waiting to see something. A sink lets the renderer
/// receive vertices straight into a buffer it already owns, and lets anything that genuinely wants
/// a mesh ask for one through <see cref="MeshBuilder"/>.
/// </para>
/// <para>
/// <b>Vertices are added first and referred to by index afterwards</b>, which is what makes shared
/// vertices possible at all: a sphere's pole is one vertex used by every triangle in its fan, and a
/// closed surface's seam is one column of vertices used by the faces on both sides. A sink that
/// took whole triangles by position would have no way to express either, and would produce a mesh
/// that looks right and is not closed.
/// </para>
/// <para>
/// <b>Normals and texture coordinates are supplied per vertex and are not optional here</b>, even
/// though they are optional on a <see cref="Mesh"/>. The tessellator knows both exactly — it has
/// the surface — and a sink that had to guess later would be guessing from the triangles, which is
/// strictly worse. A sink that does not want them ignores them.
/// </para>
/// </remarks>
public interface ITessellationSink
{
    /// <summary>Adds a vertex and returns the index to refer to it by.</summary>
    /// <param name="position">Where it is.</param>
    /// <param name="normal">The surface normal there, already a unit vector.</param>
    /// <param name="textureCoordinate">
    /// Its position in the surface's parameter space, normalised to [0, 1] in both directions.
    /// </param>
    /// <returns>The vertex's index.</returns>
    int AddVertex(in Point3d position, in Vector3d normal, in UV textureCoordinate);

    /// <summary>Adds a triangular face.</summary>
    /// <param name="a">The first vertex index.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    void AddTriangle(int a, int b, int c);

    /// <summary>Adds a quadrilateral face.</summary>
    /// <param name="a">The first vertex index.</param>
    /// <param name="b">The second.</param>
    /// <param name="c">The third.</param>
    /// <param name="d">The fourth.</param>
    /// <remarks>
    /// <b>Quads are offered rather than split into triangles by the tessellator.</b> A tensor-grid
    /// tessellation is naturally quads, a renderer that wants triangles splits them in one line,
    /// and a sink that wanted the quad structure — a subdivision surface, a mesh exporter, a
    /// quad-dominant remesher — could never get it back once it was gone.
    /// </remarks>
    void AddQuad(int a, int b, int c, int d);
}

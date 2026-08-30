using System.Collections.Generic;
using System.Numerics;

namespace Spark.Viewport;

/// <summary>
/// Accumulates triangles and edge segments into the parallel arrays a
/// <see cref="RenderPackage"/> carries.
/// </summary>
/// <remarks>
/// Everything is flat-shaded — each triangle contributes its own three vertices with one normal —
/// because the markers this builds are small faceted solids where sharing a vertex normal would
/// round them off into lumps. It costs vertices and buys correctness on exactly the shapes it is
/// used for.
/// </remarks>
internal sealed class MeshAccumulator
{
    private readonly List<float> _positions = [];
    private readonly List<float> _normals = [];
    private readonly List<int> _indices = [];
    private readonly List<int> _edges = [];

    internal bool IsEmpty => _indices.Count == 0 && _edges.Count == 0;

    /// <summary>Adds a triangle with a flat normal derived from its winding.</summary>
    internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        normal = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitZ;

        int start = _positions.Count / 3;
        Write(a, normal);
        Write(b, normal);
        Write(c, normal);

        _indices.Add(start);
        _indices.Add(start + 1);
        _indices.Add(start + 2);
    }

    /// <summary>Adds a triangle whose three vertices carry their own normals.</summary>
    /// <remarks>
    /// <b>The one place this accumulator is not flat-shaded, and it is the case that needs it.</b>
    /// Everything else it builds is a small faceted marker where a shared vertex normal would round
    /// the shape into a lump; a tessellated surface is the opposite — it carries the surface's exact
    /// normals, and shading it flat would make a sphere look like a golf ball for no reason. The
    /// vertices are still written per triangle rather than shared, because the caller has no index
    /// buffer to give and welding here would cost a hash per vertex.
    /// </remarks>
    internal void AddShadedTriangle(
        Vector3 a, Vector3 normalA, Vector3 b, Vector3 normalB, Vector3 c, Vector3 normalC)
    {
        int start = _positions.Count / 3;

        Write(a, Unit(normalA));
        Write(b, Unit(normalB));
        Write(c, Unit(normalC));

        _indices.Add(start);
        _indices.Add(start + 1);
        _indices.Add(start + 2);
    }

    private static Vector3 Unit(Vector3 normal) =>
        normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitZ;

    /// <summary>Adds a quad as two triangles, with a stated normal, plus its four edges.</summary>
    internal void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        int start = _positions.Count / 3;
        Vector3 unit = normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitZ;

        Write(a, unit);
        Write(b, unit);
        Write(c, unit);
        Write(d, unit);

        _indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        _edges.AddRange([start, start + 1, start + 1, start + 2, start + 2, start + 3, start + 3, start]);
    }

    /// <summary>Adds a standalone line segment, drawn with no surface.</summary>
    internal void AddEdge(Vector3 start, Vector3 end)
    {
        int first = _positions.Count / 3;
        Write(start, Vector3.UnitZ);
        Write(end, Vector3.UnitZ);
        _edges.Add(first);
        _edges.Add(first + 1);
    }

    /// <summary>
    /// Adds a regular octahedron centred on a point. This is Spark's point marker: eight flat
    /// faces, twenty-four vertices, and a silhouette that reads as a dot from any direction.
    /// </summary>
    /// <remarks>
    /// A true screen-space billboard would be the design language's answer (§8.3 asks for a 5 px
    /// disc), but a billboard has to be rebuilt or transformed every time the camera turns, and the
    /// renderer's mesh path has no per-vertex orientation to hang that on. A small solid is
    /// view-independent, needs no shader work, and is visible from every angle.
    /// </remarks>
    internal void AddOctahedron(Vector3 centre, float radius)
    {
        Vector3 px = centre + new Vector3(radius, 0, 0);
        Vector3 nx = centre - new Vector3(radius, 0, 0);
        Vector3 py = centre + new Vector3(0, radius, 0);
        Vector3 ny = centre - new Vector3(0, radius, 0);
        Vector3 pz = centre + new Vector3(0, 0, radius);
        Vector3 nz = centre - new Vector3(0, 0, radius);

        AddTriangle(px, py, pz);
        AddTriangle(py, nx, pz);
        AddTriangle(nx, ny, pz);
        AddTriangle(ny, px, pz);
        AddTriangle(py, px, nz);
        AddTriangle(nx, py, nz);
        AddTriangle(ny, nx, nz);
        AddTriangle(px, ny, nz);
    }

    /// <summary>Adds an axis-aligned box with flat faces and its twelve edges.</summary>
    internal void AddBox(Vector3 min, Vector3 max)
    {
        Vector3 a = new(min.X, min.Y, min.Z);
        Vector3 b = new(max.X, min.Y, min.Z);
        Vector3 c = new(max.X, max.Y, min.Z);
        Vector3 d = new(min.X, max.Y, min.Z);
        Vector3 e = new(min.X, min.Y, max.Z);
        Vector3 f = new(max.X, min.Y, max.Z);
        Vector3 g = new(max.X, max.Y, max.Z);
        Vector3 h = new(min.X, max.Y, max.Z);

        AddQuad(a, d, c, b, -Vector3.UnitZ);
        AddQuad(e, f, g, h, Vector3.UnitZ);
        AddQuad(a, b, f, e, -Vector3.UnitY);
        AddQuad(c, d, h, g, Vector3.UnitY);
        AddQuad(b, c, g, f, Vector3.UnitX);
        AddQuad(d, a, e, h, -Vector3.UnitX);
    }

    internal RenderPackage ToPackage(GeometryKey key, Appearance appearance) => new(
        key,
        string.Empty,
        [.. _positions],
        [.. _normals],
        [.. _indices],
        [.. _edges],
        appearance);

    private void Write(Vector3 position, Vector3 normal)
    {
        _positions.Add(position.X);
        _positions.Add(position.Y);
        _positions.Add(position.Z);
        _normals.Add(normal.X);
        _normals.Add(normal.Y);
        _normals.Add(normal.Z);
    }
}

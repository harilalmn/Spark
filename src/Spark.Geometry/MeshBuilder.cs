using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// An <see cref="ITessellationSink"/> that collects what it is given into a <see cref="Mesh"/>.
/// </summary>
/// <remarks>
/// <b>The reference sink, and the one every test measures against.</b> A renderer's sink writes
/// into its own buffers and is hard to inspect; this one produces the ordinary immutable mesh, so
/// *did the tessellator produce the right thing* and *did the renderer receive it* stay two
/// separate questions.
/// </remarks>
public sealed class MeshBuilder : ITessellationSink
{
    private readonly List<Point3d> _vertices = [];
    private readonly List<Vector3d> _normals = [];
    private readonly List<UV> _textureCoordinates = [];
    private readonly List<MeshFace> _faces = [];

    /// <summary>How many vertices have been added.</summary>
    public int VertexCount => _vertices.Count;

    /// <summary>How many faces have been added.</summary>
    public int FaceCount => _faces.Count;

    /// <inheritdoc/>
    public int AddVertex(in Point3d position, in Vector3d normal, in UV textureCoordinate)
    {
        _vertices.Add(position);
        _normals.Add(normal);
        _textureCoordinates.Add(textureCoordinate);

        return _vertices.Count - 1;
    }

    /// <inheritdoc/>
    public void AddTriangle(int a, int b, int c) => _faces.Add(new MeshFace(a, b, c));

    /// <inheritdoc/>
    public void AddQuad(int a, int b, int c, int d) => _faces.Add(new MeshFace(a, b, c, d));

    /// <summary>The mesh built so far.</summary>
    /// <returns>An immutable mesh carrying normals and texture coordinates.</returns>
    /// <remarks>
    /// Can be called more than once and does not reset — a caller tessellating several surfaces
    /// into one builder gets one mesh, which is what a scene wants.
    /// </remarks>
    public Mesh Build() =>
        new(_vertices, _faces, _normals, _textureCoordinates, colours: null);
}

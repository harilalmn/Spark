using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Spark.Geometry;

namespace Spark.Geometry.Io;

/// <summary>
/// Reads and writes STL — the format every 3D printer and every mesh-repair tool speaks.
/// </summary>
/// <remarks>
/// <para>
/// <b>STL is the one mesh format whose reader is unambiguous</b>, which is why it has one here and
/// OBJ does not. An STL file contains triangles and nothing else: no materials, no groups, no
/// dialects, no free-form surfaces. There is exactly one decision to make on the way in, and it is
/// stated below.
/// </para>
/// <para>
/// <b>Binary is written and both are read.</b> A binary STL is a fifth the size of the ASCII form
/// and is what every tool produces; the ASCII form is what a person hand-edits and what a bug
/// report arrives as, so refusing to read it would be refusing the case that needs reading.
/// </para>
/// <para>
/// <b>The format has no indices, and welding them back is the one decision a reader makes.</b>
/// Every triangle in an STL carries its three vertices in full, so a cube arrives as 36 vertices
/// where it has 8. Read without welding, nothing downstream can ask a topological question — the
/// mesh has no shared edges at all, so it is never closed and never manifold. Welded, a printed
/// cube is a cube. Vertices are matched exactly rather than within a tolerance, because STL stores
/// <see cref="float"/>s and two triangles that meant to share a corner wrote the same four bytes;
/// a tolerance would additionally weld corners that were never meant to meet, which is a repair
/// operation and belongs to whoever asked for one.
/// </para>
/// <para>
/// <b>The per-facet normal is written and ignored on the way in.</b> Half the STL files in the
/// world carry zero normals and a good proportion of the rest carry wrong ones; the winding is the
/// only thing that can be trusted, and it is what a normal is recomputed from anyway.
/// </para>
/// </remarks>
public static class StlFile
{
    private const int BinaryHeaderBytes = 80;
    private const int BytesPerTriangle = 50;

    /// <summary>Writes a mesh as binary STL.</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="mesh">The mesh. Quads are split into triangles, because STL has only those.</param>
    /// <returns>How many triangles were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static int Write(Stream stream, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mesh);

        Mesh triangles = mesh.Triangulated();

        Span<byte> header = stackalloc byte[BinaryHeaderBytes];
        header.Clear();
        Encoding.ASCII.GetBytes("Binary STL written by Spark").CopyTo(header);
        stream.Write(header);

        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)triangles.FaceCount);
        stream.Write(count);

        Span<byte> facet = stackalloc byte[BytesPerTriangle];

        for (int index = 0; index < triangles.FaceCount; index++)
        {
            MeshFace face = triangles.Face(index);
            Vector3d normal = triangles.FaceNormal(index);

            facet.Clear();

            WriteVector(facet[..12], normal.X, normal.Y, normal.Z);
            WritePoint(facet.Slice(12, 12), triangles.Vertex(face.A));
            WritePoint(facet.Slice(24, 12), triangles.Vertex(face.B));
            WritePoint(facet.Slice(36, 12), triangles.Vertex(face.C));

            // The last two bytes are the "attribute byte count", which is zero in every STL that
            // is not carrying somebody's colour extension. Left zero deliberately: a non-zero value
            // there is read by some slicers as a colour and by others as a corrupt file.
            stream.Write(facet);
        }

        return triangles.FaceCount;
    }

    /// <summary>Writes a mesh as binary STL to a file, replacing it if it exists.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="mesh">The mesh.</param>
    /// <returns>How many triangles were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static int WriteToFile(string path, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = File.Create(path);

        return Write(stream, mesh);
    }

    /// <summary>Reads an STL file, binary or ASCII, and welds its vertices.</summary>
    /// <param name="bytes">The file's contents.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
    /// <exception cref="InvalidDataException">The bytes are not an STL file.</exception>
    /// <remarks>
    /// <b>Which form it is cannot be decided by the leading word.</b> An ASCII STL begins with
    /// <c>solid</c>, and so do a great many binary ones — some exporters write the word into the
    /// binary header. The reliable test is arithmetic: a binary STL is exactly
    /// <c>84 + 50n</c> bytes long for the triangle count it declares, and nothing else is.
    /// </remarks>
    public static Mesh Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < BinaryHeaderBytes + 4)
        {
            return ReadAscii(Encoding.UTF8.GetString(bytes));
        }

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(BinaryHeaderBytes, 4));

        return bytes.Length == BinaryHeaderBytes + 4 + ((long)declared * BytesPerTriangle)
            ? ReadBinary(bytes, (int)declared)
            : ReadAscii(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>Reads an STL file from disk.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file is not an STL file.</exception>
    public static Mesh ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Read(File.ReadAllBytes(path));
    }

    private static Mesh ReadBinary(ReadOnlySpan<byte> bytes, int triangles)
    {
        Welder welder = new();
        List<MeshFace> faces = new(triangles);

        for (int index = 0; index < triangles; index++)
        {
            ReadOnlySpan<byte> facet = bytes.Slice(BinaryHeaderBytes + 4 + (index * BytesPerTriangle), BytesPerTriangle);

            // The facet normal, at offset 0, is deliberately not read. See the remarks on the type.
            int a = welder.Add(ReadPoint(facet.Slice(12, 12)));
            int b = welder.Add(ReadPoint(facet.Slice(24, 12)));
            int c = welder.Add(ReadPoint(facet.Slice(36, 12)));

            if (a != b && b != c && c != a)
            {
                faces.Add(new MeshFace(a, b, c));
            }
        }

        return new Mesh(welder.Vertices, faces);
    }

    private static Mesh ReadAscii(string text)
    {
        Welder welder = new();
        List<MeshFace> faces = [];
        List<int> corners = [];

        foreach (string line in text.Split('\n'))
        {
            ReadOnlySpan<char> trimmed = line.AsSpan().Trim();

            if (trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                corners.Add(welder.Add(ParsePoint(trimmed[6..])));
            }
            else if (trimmed.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
            {
                if (corners.Count == 3 && corners[0] != corners[1] && corners[1] != corners[2] && corners[2] != corners[0])
                {
                    faces.Add(new MeshFace(corners[0], corners[1], corners[2]));
                }

                corners.Clear();
            }
        }

        if (faces.Count == 0 && welder.Vertices.Count == 0)
        {
            throw new InvalidDataException(
                "This is not an STL file: it is neither the right length for a binary one nor does "
                + "it contain any ASCII facets.");
        }

        return new Mesh(welder.Vertices, faces);
    }

    private static Point3d ParsePoint(ReadOnlySpan<char> text)
    {
        Span<double> values = stackalloc double[3];
        int found = 0;

        foreach (Range range in text.Split(' '))
        {
            ReadOnlySpan<char> token = text[range].Trim();

            if (token.Length == 0 || found == 3)
            {
                continue;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                values[found++] = value;
            }
        }

        return found == 3
            ? new Point3d(values[0], values[1], values[2])
            : throw new InvalidDataException($"An STL vertex line does not carry three numbers: '{text}'.");
    }

    private static void WritePoint(Span<byte> destination, in Point3d point) =>
        WriteVector(destination, point.X, point.Y, point.Z);

    private static void WriteVector(Span<byte> destination, double x, double y, double z)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[..4], (float)x);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(4, 4), (float)y);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), (float)z);
    }

    private static Point3d ReadPoint(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadSingleLittleEndian(source[..4]),
        BinaryPrimitives.ReadSingleLittleEndian(source.Slice(4, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(source.Slice(8, 4)));

    /// <summary>
    /// Turns the repeated vertices of a triangle soup back into indices.
    /// </summary>
    /// <remarks>
    /// Exact matching, for the reason the type's remarks give: STL stores singles, so two triangles
    /// that meant to share a corner wrote identical bytes, and a tolerance would weld corners that
    /// were never meant to meet.
    /// </remarks>
    private sealed class Welder
    {
        private readonly Dictionary<(double X, double Y, double Z), int> _seen = [];

        internal List<Point3d> Vertices { get; } = [];

        internal int Add(in Point3d point)
        {
            (double, double, double) key = (point.X, point.Y, point.Z);

            if (_seen.TryGetValue(key, out int index))
            {
                return index;
            }

            index = Vertices.Count;
            Vertices.Add(point);
            _seen[key] = index;

            return index;
        }
    }
}

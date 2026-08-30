using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Spark.Geometry;

namespace Spark.Geometry.Io;

/// <summary>
/// Reads and writes PLY — the format that carries per-vertex colour, and the reason
/// <see cref="Mesh"/> has a colour channel at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>ASCII only, both ways, and that is a stated limit rather than an oversight.</b> PLY's binary
/// forms come in two endiannesses and allow any property in any order and any scalar type, so a
/// complete binary reader is a small type system. The ASCII form is what a scanner exports for
/// inspection, what a bug report arrives as, and what a person can read — and a file this cannot
/// open says so by name rather than producing a wrong mesh.
/// </para>
/// <para>
/// <b>The header is read as a description rather than assumed.</b> PLY's whole design is that the
/// header names the properties and their order; a reader that assumed <c>x y z</c> would misread
/// the very common <c>x y z nx ny nz red green blue</c> by reading normals as colours. So the
/// property list is parsed and each vertex line is read by position.
/// </para>
/// <para>
/// <b>Colours are read and written, which no other format here does.</b> That is the whole reason
/// PLY is in this list: a scan carries measured colour, and a format pipeline that dropped it would
/// make <see cref="Mesh"/>'s colour channel unreachable from outside.
/// </para>
/// </remarks>
public static class PlyFile
{
    /// <summary>Writes a mesh as ASCII PLY.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="mesh">The mesh.</param>
    /// <returns>How many vertices were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// Quads are written as quads: PLY's face element is a list of any length, so splitting them
    /// would throw away structure for nothing.
    /// </remarks>
    public static int Write(TextWriter writer, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(mesh);

        Vector3d[]? normals = mesh.Normals();
        uint[]? colours = mesh.Colours();

        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine("comment written by Spark");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"element vertex {mesh.VertexCount}"));
        writer.WriteLine("property double x");
        writer.WriteLine("property double y");
        writer.WriteLine("property double z");

        if (normals is not null)
        {
            writer.WriteLine("property double nx");
            writer.WriteLine("property double ny");
            writer.WriteLine("property double nz");
        }

        if (colours is not null)
        {
            writer.WriteLine("property uchar red");
            writer.WriteLine("property uchar green");
            writer.WriteLine("property uchar blue");
            writer.WriteLine("property uchar alpha");
        }

        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"element face {mesh.FaceCount}"));
        writer.WriteLine("property list uchar int vertex_indices");
        writer.WriteLine("end_header");

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            Point3d vertex = mesh.Vertex(index);

            writer.Write(string.Create(
                CultureInfo.InvariantCulture, $"{Number(vertex.X)} {Number(vertex.Y)} {Number(vertex.Z)}"));

            if (normals is not null)
            {
                Vector3d normal = normals[index];

                writer.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $" {Number(normal.X)} {Number(normal.Y)} {Number(normal.Z)}"));
            }

            if (colours is not null)
            {
                uint colour = colours[index];

                writer.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $" {(colour >> 24) & 0xFF} {(colour >> 16) & 0xFF} {(colour >> 8) & 0xFF} {colour & 0xFF}"));
            }

            writer.WriteLine();
        }

        foreach (MeshFace face in mesh.Faces())
        {
            writer.Write(face.Count.ToString(CultureInfo.InvariantCulture));

            for (int corner = 0; corner < face.Count; corner++)
            {
                writer.Write(' ');
                writer.Write(face[corner].ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
        }

        return mesh.VertexCount;
    }

    /// <summary>Writes a mesh as ASCII PLY to a file, replacing it if it exists.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="mesh">The mesh.</param>
    /// <returns>How many vertices were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static int WriteToFile(string path, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(path);

        using StreamWriter writer = new(path, false, new UTF8Encoding(false));

        return Write(writer, mesh);
    }

    /// <summary>Reads an ASCII PLY file.</summary>
    /// <param name="text">The file's contents.</param>
    /// <returns>The mesh, with whichever channels the file carried.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="InvalidDataException">
    /// The text is not a PLY file, or is one of the binary forms this does not read.
    /// </exception>
    public static Mesh Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int line = 0;

        if (lines.Length == 0 || !lines[0].AsSpan().Trim().SequenceEqual("ply"))
        {
            throw new InvalidDataException("This is not a PLY file: it does not begin with 'ply'.");
        }

        int vertexCount = 0;
        int faceCount = 0;
        List<string> vertexProperties = [];
        string element = string.Empty;

        while (++line < lines.Length)
        {
            string[] parts = lines[line].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                continue;
            }

            switch (parts[0])
            {
                case "format" when parts.Length > 1 && parts[1] != "ascii":
                    throw new InvalidDataException(
                        $"This PLY file is '{parts[1]}'. Spark reads the ASCII form; the binary "
                        + "forms come in two endiannesses with arbitrary property types, and a "
                        + "half-implemented reader for them would produce a wrong mesh rather than "
                        + "an error.");

                case "element" when parts.Length > 2:
                    element = parts[1];

                    if (element == "vertex")
                    {
                        vertexCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    }
                    else if (element == "face")
                    {
                        faceCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    }

                    break;

                case "property" when element == "vertex" && parts.Length > 2 && parts[1] != "list":
                    vertexProperties.Add(parts[^1]);
                    break;

                case "end_header":
                    return ReadBody(lines, line + 1, vertexCount, faceCount, vertexProperties);

                default:
                    break;
            }
        }

        throw new InvalidDataException("This PLY file has no 'end_header' line.");
    }

    /// <summary>Reads an ASCII PLY file from disk.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable PLY file.</exception>
    public static Mesh ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Read(File.ReadAllText(path));
    }

    private static Mesh ReadBody(
        string[] lines, int start, int vertexCount, int faceCount, List<string> properties)
    {
        int x = properties.IndexOf("x");
        int y = properties.IndexOf("y");
        int z = properties.IndexOf("z");

        if (x < 0 || y < 0 || z < 0)
        {
            throw new InvalidDataException("This PLY file's vertices have no x, y and z properties.");
        }

        int nx = properties.IndexOf("nx");
        int red = properties.IndexOf("red");

        List<Point3d> vertices = new(vertexCount);
        List<Vector3d>? normals = nx >= 0 ? new List<Vector3d>(vertexCount) : null;
        List<uint>? colours = red >= 0 ? new List<uint>(vertexCount) : null;

        int line = start;

        for (int index = 0; index < vertexCount; index++)
        {
            string[] parts = NextTokens(lines, ref line);

            vertices.Add(new Point3d(Value(parts, x), Value(parts, y), Value(parts, z)));

            normals?.Add(new Vector3d(
                Value(parts, nx), Value(parts, properties.IndexOf("ny")), Value(parts, properties.IndexOf("nz"))));

            if (colours is not null)
            {
                uint r = (uint)Value(parts, red);
                uint g = (uint)Value(parts, properties.IndexOf("green"));
                uint b = (uint)Value(parts, properties.IndexOf("blue"));
                int alphaIndex = properties.IndexOf("alpha");
                uint a = alphaIndex >= 0 ? (uint)Value(parts, alphaIndex) : 255u;

                colours.Add((r << 24) | (g << 16) | (b << 8) | a);
            }
        }

        List<MeshFace> faces = new(faceCount);

        for (int index = 0; index < faceCount; index++)
        {
            string[] parts = NextTokens(lines, ref line);

            if (parts.Length < 4)
            {
                continue;
            }

            int corners = int.Parse(parts[0], CultureInfo.InvariantCulture);

            // A polygon of five corners or more is fanned from its first vertex. PLY allows any
            // arity and Mesh holds three or four, and fanning is the answer that keeps every
            // vertex it had rather than dropping the face.
            for (int corner = 2; corner + 1 <= corners; corner++)
            {
                faces.Add(new MeshFace(
                    int.Parse(parts[1], CultureInfo.InvariantCulture),
                    int.Parse(parts[corner], CultureInfo.InvariantCulture),
                    int.Parse(parts[corner + 1], CultureInfo.InvariantCulture)));
            }

            // Four corners is a quad, which Mesh holds directly, so the fan above is undone for
            // exactly that case rather than being avoided by a branch above it.
            if (corners == 4)
            {
                faces.RemoveRange(faces.Count - 2, 2);
                faces.Add(new MeshFace(
                    int.Parse(parts[1], CultureInfo.InvariantCulture),
                    int.Parse(parts[2], CultureInfo.InvariantCulture),
                    int.Parse(parts[3], CultureInfo.InvariantCulture),
                    int.Parse(parts[4], CultureInfo.InvariantCulture)));
            }
        }

        return new Mesh(vertices, faces, normals, textureCoordinates: null, colours);
    }

    private static string[] NextTokens(string[] lines, ref int line)
    {
        while (line < lines.Length)
        {
            string[] parts = lines[line++].Split(
                [' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                return parts;
            }
        }

        throw new InvalidDataException("This PLY file ends before the element counts its header declares.");
    }

    private static double Value(string[] parts, int index) =>
        index >= 0 && index < parts.Length
            && double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0.0;

    private static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);
}

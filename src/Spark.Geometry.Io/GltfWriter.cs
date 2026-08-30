using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Spark.Geometry;

namespace Spark.Geometry.Io;

/// <summary>
/// Writes meshes as binary glTF (<c>.glb</c>) — the format a browser, a game engine and a phone
/// all open without a plugin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Binary glTF and not the JSON-plus-files form.</b> A <c>.gltf</c> file references its buffers
/// by URI, so exporting one produces a directory rather than a file — and a user who emails the
/// <c>.gltf</c> alone has sent nothing. A <c>.glb</c> is one file with the JSON and the binary in
/// two chunks, and every viewer reads it.
/// </para>
/// <para>
/// <b>Written by hand rather than through a library, and the reason is `NFR-5`.</b> Every glTF
/// package on NuGet brings either a native dependency or a large object model, and what is needed
/// here is one mesh in one scene: positions, normals, indices, and a default material. That is a
/// few hundred lines of a format whose specification is a single page for this subset, and it keeps
/// the promise that Spark has no native dependencies.
/// </para>
/// <para>
/// <b>Two conventions have to be right or the model arrives rotated and inside out.</b> glTF is
/// <b>y-up</b> and right-handed where Spark is z-up, so positions and normals are rotated on the
/// way out; and its indices are unsigned, so a mesh is triangulated and its indices written as
/// <c>uint32</c>. Both are stated here because both are invisible until somebody opens the file in
/// a viewer and finds the model lying on its side.
/// </para>
/// </remarks>
public static class GltfWriter
{
    private const uint Magic = 0x46546C67;      // "glTF"
    private const uint Version = 2;
    private const uint JsonChunk = 0x4E4F534A;  // "JSON"
    private const uint BinaryChunk = 0x004E4942; // "BIN"

    private const int Float32 = 5126;
    private const int UnsignedInt32 = 5125;
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;
    private const int Triangles = 4;

    /// <summary>Writes one mesh as a binary glTF scene.</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="mesh">The mesh. Quads are split, because glTF draws triangles.</param>
    /// <returns>How many triangles were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static int Write(Stream stream, Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mesh);

        Mesh triangles = mesh.Triangulated().WithVertexNormals();

        byte[] binary = Binary(triangles, out Bounds bounds);
        byte[] json = Json(triangles, binary.Length, bounds);

        // Both chunks are padded to four bytes, which the specification requires and which several
        // readers enforce strictly: JSON with spaces, binary with zeros.
        byte[] paddedJson = Pad(json, 0x20);
        byte[] paddedBinary = Pad(binary, 0x00);

        uint total = 12u + 8u + (uint)paddedJson.Length + 8u + (uint)paddedBinary.Length;

        WriteUInt(stream, Magic);
        WriteUInt(stream, Version);
        WriteUInt(stream, total);

        WriteUInt(stream, (uint)paddedJson.Length);
        WriteUInt(stream, JsonChunk);
        stream.Write(paddedJson);

        WriteUInt(stream, (uint)paddedBinary.Length);
        WriteUInt(stream, BinaryChunk);
        stream.Write(paddedBinary);

        return triangles.FaceCount;
    }

    /// <summary>Writes one mesh as a binary glTF file, replacing it if it exists.</summary>
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

    /// <summary>
    /// The binary chunk: positions, then normals, then indices, each aligned to four bytes.
    /// </summary>
    private static byte[] Binary(Mesh mesh, out Bounds bounds)
    {
        Vector3d[] normals = mesh.Normals()!;

        using MemoryStream buffer = new();
        Span<byte> scratch = stackalloc byte[4];

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            (float x, float y, float z) = ToGltf(mesh.Vertex(index));

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);

            WriteSingle(buffer, scratch, x);
            WriteSingle(buffer, scratch, y);
            WriteSingle(buffer, scratch, z);
        }

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            (float x, float y, float z) = ToGltf(normals[index]);

            WriteSingle(buffer, scratch, x);
            WriteSingle(buffer, scratch, y);
            WriteSingle(buffer, scratch, z);
        }

        foreach (MeshFace face in mesh.Faces())
        {
            WriteIndex(buffer, scratch, face.A);
            WriteIndex(buffer, scratch, face.B);
            WriteIndex(buffer, scratch, face.C);
        }

        bounds = mesh.VertexCount == 0
            ? new Bounds(0, 0, 0, 0, 0, 0)
            : new Bounds(minX, minY, minZ, maxX, maxY, maxZ);

        return buffer.ToArray();
    }

    private static byte[] Json(Mesh mesh, int binaryLength, in Bounds bounds)
    {
        int positionBytes = mesh.VertexCount * 12;
        int normalBytes = mesh.VertexCount * 12;
        int indexBytes = mesh.FaceCount * 12;

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("asset");
            writer.WriteString("version", "2.0");
            writer.WriteString("generator", "Spark");
            writer.WriteEndObject();

            writer.WriteNumber("scene", 0);

            writer.WriteStartArray("scenes");
            writer.WriteStartObject();
            writer.WriteStartArray("nodes");
            writer.WriteNumberValue(0);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartArray("nodes");
            writer.WriteStartObject();
            writer.WriteNumber("mesh", 0);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartArray("meshes");
            writer.WriteStartObject();
            writer.WriteStartArray("primitives");
            writer.WriteStartObject();
            writer.WriteStartObject("attributes");
            writer.WriteNumber("POSITION", 0);
            writer.WriteNumber("NORMAL", 1);
            writer.WriteEndObject();
            writer.WriteNumber("indices", 2);
            writer.WriteNumber("mode", Triangles);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartArray("accessors");

            // POSITION carries min and max, and it is the one accessor for which glTF *requires*
            // them: a viewer frames the model from these, so a file without them opens with the
            // camera nowhere near the geometry.
            Accessor(writer, 0, Float32, mesh.VertexCount, "VEC3", bounds);
            Accessor(writer, 1, Float32, mesh.VertexCount, "VEC3", bounds: null);
            Accessor(writer, 2, UnsignedInt32, mesh.FaceCount * 3, "SCALAR", bounds: null);

            writer.WriteEndArray();

            writer.WriteStartArray("bufferViews");
            BufferView(writer, 0, positionBytes, ArrayBuffer);
            BufferView(writer, positionBytes, normalBytes, ArrayBuffer);
            BufferView(writer, positionBytes + normalBytes, indexBytes, ElementArrayBuffer);
            writer.WriteEndArray();

            writer.WriteStartArray("buffers");
            writer.WriteStartObject();
            writer.WriteNumber("byteLength", binaryLength);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void Accessor(
        Utf8JsonWriter writer, int view, int componentType, int count, string type, Bounds? bounds)
    {
        writer.WriteStartObject();
        writer.WriteNumber("bufferView", view);
        writer.WriteNumber("componentType", componentType);
        writer.WriteNumber("count", count);
        writer.WriteString("type", type);

        if (bounds is { } box)
        {
            writer.WriteStartArray("min");
            writer.WriteNumberValue(box.MinX);
            writer.WriteNumberValue(box.MinY);
            writer.WriteNumberValue(box.MinZ);
            writer.WriteEndArray();

            writer.WriteStartArray("max");
            writer.WriteNumberValue(box.MaxX);
            writer.WriteNumberValue(box.MaxY);
            writer.WriteNumberValue(box.MaxZ);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void BufferView(Utf8JsonWriter writer, int offset, int length, int target)
    {
        writer.WriteStartObject();
        writer.WriteNumber("buffer", 0);
        writer.WriteNumber("byteOffset", offset);
        writer.WriteNumber("byteLength", length);
        writer.WriteNumber("target", target);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Spark's z-up right-handed coordinates in glTF's y-up right-handed ones.
    /// </summary>
    /// <remarks>
    /// <b>A rotation, not a swap.</b> Exchanging y and z alone flips the handedness, so the model
    /// arrives mirrored — every face wound the wrong way and every normal pointing in. Negating one
    /// of them as well is what makes it a rotation about the x-axis, which is what glTF's own
    /// z-up-to-y-up guidance says.
    /// </remarks>
    private static (float X, float Y, float Z) ToGltf(in Point3d point) =>
        ((float)point.X, (float)point.Z, (float)-point.Y);

    private static (float X, float Y, float Z) ToGltf(in Vector3d vector) =>
        ((float)vector.X, (float)vector.Z, (float)-vector.Y);

    private static void WriteSingle(Stream stream, Span<byte> scratch, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(scratch, value);
        stream.Write(scratch);
    }

    private static void WriteIndex(Stream stream, Span<byte> scratch, int value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, (uint)value);
        stream.Write(scratch);
    }

    private static void WriteUInt(Stream stream, uint value)
    {
        Span<byte> scratch = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
        stream.Write(scratch);
    }

    private static byte[] Pad(byte[] bytes, byte filler)
    {
        int remainder = bytes.Length % 4;

        if (remainder == 0)
        {
            return bytes;
        }

        byte[] padded = new byte[bytes.Length + (4 - remainder)];
        bytes.CopyTo(padded, 0);

        for (int index = bytes.Length; index < padded.Length; index++)
        {
            padded[index] = filler;
        }

        return padded;
    }

    private readonly record struct Bounds(
        double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ);
}

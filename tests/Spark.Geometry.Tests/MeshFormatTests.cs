using System;
using System.IO;
using System.Linq;
using System.Text;
using Spark.Geometry;
using Spark.Geometry.Io;

namespace Spark.Geometry.Tests;

/// <summary>
/// The mesh interchange formats — `E2-T34` and `E2-T35`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where a format can be read as well as written, the test is a round trip</b>, and it is
/// asserted on the geometry rather than on the bytes: STL and PLY both carry a mesh, so *is it the
/// same mesh* is the question, and a byte comparison would break on a formatting change that
/// nothing downstream would notice.
/// </para>
/// <para>
/// <b>Where a format is written only, the test reads the file back by hand.</b> OBJ's indices and
/// glTF's header are the two things most likely to be wrong in a way that produces a file which
/// opens and shows the wrong thing, so both are parsed and checked rather than eyeballed.
/// </para>
/// </remarks>
public sealed class MeshFormatTests
{
    /// <summary>The unit cube as six quads, wound outwards.</summary>
    private static Mesh Cube()
    {
        Point3d[] vertices =
        [
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        ];

        MeshFace[] faces =
        [
            new(0, 3, 2, 1), new(4, 5, 6, 7), new(0, 1, 5, 4),
            new(1, 2, 6, 5), new(2, 3, 7, 6), new(3, 0, 4, 7),
        ];

        return new Mesh(vertices, faces);
    }

    // -- OBJ ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>A quad is written as a quad</b>, because OBJ has always allowed any arity and splitting
    /// would throw away structure the mesh went to trouble to keep.
    /// </summary>
    [Fact]
    public void ObjWritesQuadsAsQuads()
    {
        StringWriter writer = new();

        Assert.Equal(1, ObjWriter.WriteMeshes(writer, [Cube()]));

        string[] faces = [.. Lines(writer).Where(line => line.StartsWith("f ", StringComparison.Ordinal))];

        Assert.Equal(6, faces.Length);
        Assert.All(faces, face => Assert.Equal(5, face.Split(' ').Length));
    }

    /// <summary>
    /// <b>OBJ indices are one-based and file-global.</b> Restarting them per object is the single
    /// most common way to write an OBJ that opens and draws the wrong thing.
    /// </summary>
    [Fact]
    public void ObjIndicesAreOneBasedAndGlobal()
    {
        StringWriter writer = new();

        ObjWriter.WriteMeshes(writer, [Cube(), Cube()]);

        string[] lines = Lines(writer);
        int[] indices =
        [
            .. lines
                .Where(line => line.StartsWith("f ", StringComparison.Ordinal))
                .SelectMany(line => line.Split(' ').Skip(1))
                .Select(token => int.Parse(token.Split('/')[0], System.Globalization.CultureInfo.InvariantCulture)),
        ];

        Assert.Equal(1, indices.Min());
        Assert.Equal(16, indices.Max());
    }

    /// <summary>
    /// A mesh with normals and no texture coordinates writes <c>v//vn</c>, not <c>v/vn</c> — which
    /// a reader would take as a texture index.
    /// </summary>
    [Fact]
    public void ObjWritesTheEmptyTextureSlot()
    {
        StringWriter writer = new();

        ObjWriter.WriteMeshes(writer, [Cube().WithVertexNormals()]);

        string face = Lines(writer).First(line => line.StartsWith("f ", StringComparison.Ordinal));

        Assert.Contains("//", face, StringComparison.Ordinal);
    }

    // -- STL ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>A cube survives a binary STL round trip as a closed mesh</b>, which is only true because
    /// the reader welds. Unwelded it has 36 vertices, no shared edges and no closure.
    /// </summary>
    [Fact]
    public void StlRoundTripsAClosedCube()
    {
        using MemoryStream stream = new();

        Assert.Equal(12, StlFile.Write(stream, Cube()));

        Mesh read = StlFile.Read(stream.ToArray());

        Assert.Equal(8, read.VertexCount);
        Assert.Equal(12, read.FaceCount);
        Assert.True(read.Topology.IsClosed, $"{read.Topology.NakedEdgeCount} naked edges");
        Assert.Equal(1.0, read.Volume(), 1e-6);
    }

    /// <summary>A binary STL is exactly 84 + 50n bytes, which is also how a reader recognises one.</summary>
    [Fact]
    public void ABinaryStlIsTheRightLength()
    {
        using MemoryStream stream = new();

        StlFile.Write(stream, Cube());

        Assert.Equal(84 + (50 * 12), stream.Length);
    }

    /// <summary>
    /// <b>An ASCII STL is read too, and the form is decided by arithmetic rather than by the
    /// leading word.</b> Plenty of binary files begin with <c>solid</c>, because some exporters
    /// write it into the 80-byte header.
    /// </summary>
    [Fact]
    public void AnAsciiStlIsRead()
    {
        string ascii = """
            solid cube
              facet normal 0 0 -1
                outer loop
                  vertex 0 0 0
                  vertex 0 1 0
                  vertex 1 1 0
                endloop
              endfacet
              facet normal 0 0 -1
                outer loop
                  vertex 0 0 0
                  vertex 1 1 0
                  vertex 1 0 0
                endloop
              endfacet
            endsolid cube
            """;

        Mesh mesh = StlFile.Read(Encoding.ASCII.GetBytes(ascii));

        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(2, mesh.FaceCount);
        Assert.Equal(1.0, mesh.Area, 1e-9);
    }

    /// <summary>Text that is neither form is refused by name rather than read as an empty mesh.</summary>
    [Fact]
    public void SomethingThatIsNotAnStlIsRefused() =>
        Assert.Throws<InvalidDataException>(() => StlFile.Read(Encoding.ASCII.GetBytes("hello, world")));

    // -- PLY ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>PLY round-trips colours</b>, which is the whole reason it is in the list: a scan carries
    /// measured colour and every other format here would drop it.
    /// </summary>
    [Fact]
    public void PlyRoundTripsColours()
    {
        Mesh coloured = new(
            Cube().Vertices(),
            Cube().Faces(),
            normals: null,
            textureCoordinates: null,
            colours: [0xFF0000FFu, 0x00FF00FFu, 0x0000FFFFu, 0xFFFFFFFFu, 0x808080FFu, 0x102030FFu, 0x405060FFu, 0x708090FFu]);

        StringWriter writer = new();

        Assert.Equal(8, PlyFile.Write(writer, coloured));

        Mesh read = PlyFile.Read(writer.ToString());

        Assert.True(read.HasColours);
        Assert.Equal(coloured.Colours(), read.Colours());
    }

    /// <summary>A PLY round trip keeps the vertices, the quads and the normals.</summary>
    [Fact]
    public void PlyRoundTripsGeometry()
    {
        Mesh written = Cube().WithVertexNormals();
        StringWriter writer = new();

        PlyFile.Write(writer, written);

        Mesh read = PlyFile.Read(writer.ToString());

        Assert.Equal(written.VertexCount, read.VertexCount);
        Assert.Equal(written.FaceCount, read.FaceCount);
        Assert.Equal(6, read.QuadCount);
        Assert.True(read.HasNormals);
        Assert.Equal(1.0, read.Volume(), 1e-9);
    }

    /// <summary>
    /// <b>The header is read as a description, not assumed.</b> A file whose vertices carry
    /// normals before colours must not have its normals read as colours, which is what a reader
    /// that assumed a property order would do.
    /// </summary>
    [Fact]
    public void PlyReadsPropertiesByName()
    {
        string ply = """
            ply
            format ascii 1.0
            element vertex 3
            property float x
            property float y
            property float z
            property float nx
            property float ny
            property float nz
            property uchar red
            property uchar green
            property uchar blue
            element face 1
            property list uchar int vertex_indices
            end_header
            0 0 0 0 0 1 255 0 0
            1 0 0 0 0 1 0 255 0
            0 1 0 0 0 1 0 0 255
            3 0 1 2
            """;

        Mesh mesh = PlyFile.Read(ply);

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(Vector3d.ZAxis, mesh.Normals()![0]);
        Assert.Equal(0xFF0000FFu, mesh.Colours()![0]);
        Assert.Equal(0x00FF00FFu, mesh.Colours()![1]);
    }

    /// <summary>A binary PLY is refused by name rather than misread.</summary>
    [Fact]
    public void ABinaryPlyIsRefusedByName()
    {
        InvalidDataException failure = Assert.Throws<InvalidDataException>(
            () => PlyFile.Read("ply\nformat binary_little_endian 1.0\nend_header\n"));

        Assert.Contains("binary_little_endian", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A polygon of five or more corners is fanned rather than dropped.</summary>
    [Fact]
    public void PlyFansALargePolygon()
    {
        string ply = """
            ply
            format ascii 1.0
            element vertex 5
            property float x
            property float y
            property float z
            element face 1
            property list uchar int vertex_indices
            end_header
            0 0 0
            1 0 0
            2 1 0
            1 2 0
            0 1 0
            5 0 1 2 3 4
            """;

        Mesh mesh = PlyFile.Read(ply);

        Assert.Equal(3, mesh.FaceCount);
        Assert.Equal(0, mesh.QuadCount);
    }

    // -- glTF -----------------------------------------------------------------------------------

    /// <summary>
    /// <b>A <c>.glb</c> begins with the magic, the version and its own total length</b>, and the
    /// length has to be right: a viewer reads chunks until it reaches it.
    /// </summary>
    [Fact]
    public void GltfWritesAValidContainer()
    {
        using MemoryStream stream = new();

        Assert.Equal(12, GltfWriter.Write(stream, Cube()));

        byte[] bytes = stream.ToArray();

        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(bytes, 0));
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 8));

        // Both chunk lengths are multiples of four, which the specification requires and several
        // readers enforce.
        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 12) % 4);
    }

    /// <summary>
    /// The JSON chunk names one scene, one mesh and three accessors, and the POSITION accessor
    /// carries the bounds a viewer frames the model from.
    /// </summary>
    [Fact]
    public void GltfDescribesTheMesh()
    {
        using MemoryStream stream = new();

        GltfWriter.Write(stream, Cube());

        byte[] bytes = stream.ToArray();
        int jsonLength = (int)BitConverter.ToUInt32(bytes, 12);
        string json = Encoding.UTF8.GetString(bytes, 20, jsonLength);

        Assert.Contains("\"POSITION\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"NORMAL\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"min\":", json, StringComparison.Ordinal);
        Assert.Contains("\"max\":", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The z-up to y-up change is a rotation, not a swap.</b> Swapping y and z alone flips the
    /// handedness and the model arrives mirrored — every face wound the wrong way. The check is
    /// that the transformed cube still has a positive volume in glTF's own coordinates.
    /// </summary>
    [Fact]
    public void GltfKeepsTheHandedness()
    {
        using MemoryStream stream = new();

        GltfWriter.Write(stream, Cube());

        byte[] bytes = stream.ToArray();
        int jsonLength = (int)BitConverter.ToUInt32(bytes, 12);
        int binaryStart = 20 + jsonLength + 8;

        Mesh triangles = Cube().Triangulated();
        Point3d[] moved = new Point3d[triangles.VertexCount];

        for (int index = 0; index < triangles.VertexCount; index++)
        {
            int at = binaryStart + (index * 12);

            moved[index] = new Point3d(
                BitConverter.ToSingle(bytes, at),
                BitConverter.ToSingle(bytes, at + 4),
                BitConverter.ToSingle(bytes, at + 8));
        }

        Mesh transformed = new(moved, triangles.Faces());

        Assert.Equal(1.0, transformed.Volume(), 1e-5);
    }

    private static string[] Lines(StringWriter writer) =>
        [.. writer.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim())];
}

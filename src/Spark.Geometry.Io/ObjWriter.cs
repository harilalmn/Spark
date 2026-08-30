using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Spark.Geometry;

namespace Spark.Geometry.Io;

/// <summary>
/// Writes geometry as Wavefront OBJ — the format every 3D application on earth can open.
/// </summary>
/// <remarks>
/// <para>
/// <b>Writer only, and that is a decision rather than a stage.</b> An OBJ *reader* would have to
/// take a position on materials, groups, negative indices, free-form surfaces and a decade of
/// dialects, in exchange for importing a format that carries no curves and no precision. Spark's
/// import story is STEP and its own `.spark`; OBJ is how geometry leaves.
/// </para>
/// <para>
/// <b>A curve becomes a polyline, because OBJ has no curves.</b> Every curve is tessellated at a
/// tolerance the caller passes, and the tolerance is written into the file's header comment — so
/// the question *how round is this circle* has an answer inside the artefact rather than in
/// somebody's memory. OBJ's own `curv` elements are free-form NURBS and effectively no viewer
/// implements them; writing a polyline is what actually opens.
/// </para>
/// <para>
/// <b>Numbers are written with the invariant culture, always.</b> A German or French locale
/// writes <c>1,5</c> for one and a half, which produces an OBJ file that every viewer misreads or
/// rejects — and would do it only on some machines, which is the worst kind of bug to find. The
/// Linux CI leg exists partly to catch exactly this class of difference.
/// </para>
/// </remarks>
public static class ObjWriter
{
    /// <summary>
    /// The number of significant digits written per coordinate.
    /// </summary>
    /// <remarks>
    /// Nine digits round-trips a <see cref="float"/> exactly and is far more than any viewer
    /// draws with. Seventeen would round-trip a <see cref="double"/>, and would also fill the
    /// file with digits nothing downstream can use: OBJ is an interchange format for looking at,
    /// and `.spark` is the format for coming back.
    /// </remarks>
    public const int Digits = 9;

    /// <summary>
    /// Writes curves as OBJ polylines.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="curves">The curves. Nulls are skipped rather than throwing.</param>
    /// <param name="tolerance">
    /// The tessellation tolerance — the greatest distance the polyline may stray from the true
    /// curve. A default-constructed tolerance means <see cref="Tolerance.Default"/>.
    /// </param>
    /// <returns>How many curves were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static int WriteCurves(TextWriter writer, IEnumerable<Curve> curves, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(curves);

        List<Curve> real = [];
        foreach (Curve curve in curves)
        {
            if (curve is not null)
            {
                real.Add(curve);
            }
        }

        WriteHeader(writer, real.Count, tolerance);

        int vertex = 1;
        foreach (Curve curve in real)
        {
            Point3d[] points = curve.Tessellate(tolerance);

            if (points.Length < 2)
            {
                continue;
            }

            writer.WriteLine();
            writer.WriteLine(
                string.Create(CultureInfo.InvariantCulture, $"o {curve.GetType().Name}_{vertex}"));

            foreach (Point3d point in points)
            {
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"v {Format(point.X)} {Format(point.Y)} {Format(point.Z)}"));
            }

            writer.Write('l');
            for (int i = 0; i < points.Length; i++)
            {
                writer.Write(' ');
                writer.Write((vertex + i).ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
            vertex += points.Length;
        }

        return real.Count;
    }

    /// <summary>
    /// Writes meshes as OBJ faces.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="meshes">The meshes. Nulls are skipped rather than throwing.</param>
    /// <returns>How many meshes were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Quads are written as quads.</b> OBJ has always allowed a face of any arity and every
    /// viewer reads them, so splitting them would throw away structure the mesh went to some
    /// trouble to keep — and would double the face count of a tessellated surface for nothing.
    /// </para>
    /// <para>
    /// <b>Indices are one-based and file-global</b>, which is OBJ's rule and the single most common
    /// thing to get wrong when writing several objects into one file: a second mesh's indices
    /// continue from the first's rather than restarting. Normals and texture coordinates get their
    /// own independent global counters, because OBJ numbers the three streams separately.
    /// </para>
    /// </remarks>
    public static int WriteMeshes(TextWriter writer, IEnumerable<Mesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(meshes);

        List<Mesh> real = [];

        foreach (Mesh mesh in meshes)
        {
            if (mesh is not null)
            {
                real.Add(mesh);
            }
        }

        writer.WriteLine("# Wavefront OBJ written by Spark");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# meshes: {real.Count}"));

        int vertexBase = 1;
        int normalBase = 1;
        int textureBase = 1;
        int index = 0;

        foreach (Mesh mesh in real)
        {
            index++;

            writer.WriteLine();
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"o Mesh_{index}"));

            foreach (Point3d vertex in mesh.Vertices())
            {
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"v {Format(vertex.X)} {Format(vertex.Y)} {Format(vertex.Z)}"));
            }

            UV[]? textures = mesh.TextureCoordinates();

            if (textures is not null)
            {
                foreach (UV uv in textures)
                {
                    writer.WriteLine(string.Create(
                        CultureInfo.InvariantCulture, $"vt {Format(uv.U)} {Format(uv.V)}"));
                }
            }

            Vector3d[]? normals = mesh.Normals();

            if (normals is not null)
            {
                foreach (Vector3d normal in normals)
                {
                    writer.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"vn {Format(normal.X)} {Format(normal.Y)} {Format(normal.Z)}"));
                }
            }

            foreach (MeshFace face in mesh.Faces())
            {
                writer.Write('f');

                for (int corner = 0; corner < face.Count; corner++)
                {
                    int vertex = vertexBase + face[corner];

                    writer.Write(' ');
                    writer.Write(vertex.ToString(CultureInfo.InvariantCulture));

                    // OBJ's `v/vt/vn` triple, with an empty middle when there is no texture
                    // coordinate. Writing `v//vn` rather than `v/vn` is the part every hand-rolled
                    // writer gets wrong, and a viewer that reads the second form reads the normal
                    // index as a texture index.
                    if (textures is not null || normals is not null)
                    {
                        writer.Write('/');

                        if (textures is not null)
                        {
                            writer.Write((textureBase + face[corner]).ToString(CultureInfo.InvariantCulture));
                        }

                        if (normals is not null)
                        {
                            writer.Write('/');
                            writer.Write((normalBase + face[corner]).ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                writer.WriteLine();
            }

            vertexBase += mesh.VertexCount;

            if (textures is not null)
            {
                textureBase += textures.Length;
            }

            if (normals is not null)
            {
                normalBase += normals.Length;
            }
        }

        return real.Count;
    }

    /// <summary>Writes meshes as OBJ to a file, replacing it if it exists.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="meshes">The meshes.</param>
    /// <returns>How many meshes were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static int WriteMeshesToFile(string path, IEnumerable<Mesh> meshes)
    {
        ArgumentNullException.ThrowIfNull(path);

        using StreamWriter writer = new(path, false, new System.Text.UTF8Encoding(false));

        return WriteMeshes(writer, meshes);
    }

    /// <summary>
    /// Writes curves as OBJ polylines to a file, replacing it if it exists.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="curves">The curves.</param>
    /// <param name="tolerance">The tessellation tolerance.</param>
    /// <returns>How many curves were written.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static int WriteCurvesToFile(string path, IEnumerable<Curve> curves, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        // UTF-8 without a byte-order mark: a BOM is legal in a text file and several OBJ readers
        // choke on it, because they compare the first token against "v" byte for byte.
        using StreamWriter writer = new(path, false, new System.Text.UTF8Encoding(false));

        return WriteCurves(writer, curves, tolerance);
    }

    private static void WriteHeader(TextWriter writer, int count, in Tolerance tolerance)
    {
        writer.WriteLine("# Wavefront OBJ written by Spark");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# curves: {count}"));
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"# tessellation tolerance: {Format(tolerance.Linear)}"));
        writer.WriteLine("# curves are written as polylines: OBJ has no curve of its own that viewers read");
    }

    private static string Format(double value) =>
        value.ToString("G" + Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Spark.Geometry;

namespace Spark.Geometry.Io;

/// <summary>
/// Writes points and curves as Wavefront OBJ.
/// </summary>
/// <remarks>
/// <para>
/// <b>OBJ has no curve entities, so curves are tessellated and the file says so.</b> The format
/// does define free-form geometry, and almost nothing reads it; every viewer worth testing
/// against reads <c>v</c>, <c>l</c> and <c>p</c>. So a circle leaves here as a polyline, the
/// tolerance that produced it is written into the header as a comment, and the loss is recorded
/// in the artefact rather than in somebody's memory. An exporter that silently approximates is
/// the reason people distrust exporters.
/// </para>
/// <para>
/// <b>Nothing is welded.</b> Two curves meeting at a point produce two coincident vertices, and
/// a closed polyline repeats its first point at the end. Welding is a tolerance decision — how
/// close is the same place — and the tolerance for it belongs to whoever knows what the model
/// is, not to a writer that has been handed a list. The consequence to expect: a viewer's vertex
/// count is the sum of the tessellations, not the number of distinct positions.
/// </para>
/// <para>
/// <b>Numbers are written in the invariant culture and lines end with <c>\n</c>, on every
/// platform.</b> Both are correctness rather than tidiness. A machine with a comma decimal
/// separator writes <c>1,5</c>, which OBJ reads as two fields and no reader complains about;
/// and a file whose bytes depend on the operating system that wrote it cannot be compared
/// against a golden file, which is how this writer is tested.
/// </para>
/// <para>
/// <b>Coordinates are unitless</b>, per PRD decision D12. OBJ carries no units either, so
/// nothing is converted and nothing is claimed.
/// </para>
/// </remarks>
public static class ObjWriter
{
    /// <summary>
    /// Writes points and curves to a stream of OBJ text.
    /// </summary>
    /// <param name="writer">Where to write. Not closed or flushed by this method.</param>
    /// <param name="curves">
    /// The curves. Each becomes one <c>l</c> element over its own tessellation.
    /// </param>
    /// <param name="points">The points. Each becomes one <c>p</c> element. May be empty.</param>
    /// <param name="tolerance">
    /// The chord tolerance for tessellating the curves; only <see cref="Tolerance.Linear"/> is
    /// consulted. A default-constructed tolerance means <see cref="Tolerance.Default"/>. It is
    /// recorded in the file's header, because a polyline without the tolerance that produced it
    /// is a shape nobody can reproduce.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="writer"/> or <paramref name="curves"/> is
    /// <see langword="null"/>, or when a curve in the sequence is.
    /// </exception>
    public static void Write(
        TextWriter writer,
        IEnumerable<Curve> curves,
        IEnumerable<Point3d>? points = null,
        in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(curves);

        Tolerance chord = tolerance;

        writer.Write("# Written by Spark. https://github.com/harilalmn/Spark\n");
        writer.Write("# Coordinates are unitless.\n");
        writer.Write(
            string.Create(
                CultureInfo.InvariantCulture,
                $"# Curves are tessellated; chord tolerance {chord.Linear}.\n"));

        int next = 1;

        // Points before curves, so that a file of both is stable under a change to either: an
        // OBJ index is a position in the vertex list, and interleaving would make adding a curve
        // renumber the points.
        if (points is not null)
        {
            List<int> indices = [];

            foreach (Point3d point in points)
            {
                WriteVertex(writer, point);
                indices.Add(next++);
            }

            foreach (int index in indices)
            {
                writer.Write(string.Create(CultureInfo.InvariantCulture, $"p {index}\n"));
            }
        }

        foreach (Curve curve in curves)
        {
            ArgumentNullException.ThrowIfNull(curve, nameof(curves));

            Point3d[] tessellated = curve.Tessellate(chord);

            if (tessellated.Length < 2)
            {
                // A curve that tessellates to fewer than two points cannot be a line element.
                // Skipping it silently would lose it; writing a one-vertex 'l' produces a file
                // some readers reject and others render as nothing.
                continue;
            }

            int first = next;

            foreach (Point3d point in tessellated)
            {
                WriteVertex(writer, point);
                next++;
            }

            StringBuilder line = new("l");

            for (int index = first; index < next; index++)
            {
                line.Append(' ').Append(index.ToString(CultureInfo.InvariantCulture));
            }

            writer.Write(line.Append('\n').ToString());
        }
    }

    /// <summary>
    /// Writes points and curves to a file.
    /// </summary>
    /// <param name="path">Where to write. An existing file is overwritten.</param>
    /// <param name="curves">The curves.</param>
    /// <param name="points">The points. May be empty.</param>
    /// <param name="tolerance">The chord tolerance for tessellation.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> or <paramref name="curves"/> is <see langword="null"/>.
    /// </exception>
    public static void WriteFile(
        string path,
        IEnumerable<Curve> curves,
        IEnumerable<Point3d>? points = null,
        in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        // UTF-8 without a byte-order mark, and no automatic newline translation. A BOM is legal
        // in OBJ and several readers treat it as part of the first keyword.
        using StreamWriter writer = new(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Write(writer, curves, points, tolerance);
    }

    private static void WriteVertex(TextWriter writer, in Point3d point)
    {
        // "R" round-trips a double exactly and is what keeps a coordinate that came from a
        // computation from becoming a slightly different coordinate in the file.
        writer.Write(
            string.Create(
                CultureInfo.InvariantCulture,
                $"v {Number(point.X)} {Number(point.Y)} {Number(point.Z)}\n"));
    }

    private static string Number(double value) =>
        double.IsFinite(value)
            ? value.ToString("R", CultureInfo.InvariantCulture)

            // OBJ has no spelling for these. Zero is the least wrong thing a writer can do and
            // it is still wrong, so it is stated here rather than discovered: a non-finite
            // coordinate reaching an exporter is a defect upstream.
            : "0";
}

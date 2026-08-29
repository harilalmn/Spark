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

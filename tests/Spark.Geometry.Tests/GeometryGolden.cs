using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Spark.Geometry.Tests;

/// <summary>
/// One named measurement of a piece of geometry: <c>mesh.area</c> and <c>24.0000000000</c>.
/// </summary>
/// <param name="Field">The measurement's name. Stable, lower-case, dotted.</param>
/// <param name="Value">Its value, formatted for a human to read.</param>
internal readonly record struct Measurement(string Field, string Value);

/// <summary>
/// Golden files for geometry, and the diff table that makes a failure worth reading
/// (<c>E11-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The row this implements is about the failure message, not the comparison.</b> Its words:
/// <i>hashes plus summary stats; failures print bounding box, counts, area and volume</i>, in the
/// spirit of DoodleSharp's ASCII-art rasteriser failures, because <b>a bare hash mismatch tells
/// you nothing</b>. A test that says <c>expected 3f9a…, got 71c2…</c> has told the reader only
/// that something moved — not what, not by how much, and not whether it matters.
/// </para>
/// <para>
/// <b>So every golden carries both, and they do different jobs.</b> The summary is rounded to ten
/// decimal places and is what a reader compares: a volume that changed by 2.0 is a different
/// shape, and a volume that changed by 1e-12 is arithmetic. The <c>hash</c> is over the
/// <i>exact bits</i> of the canonical detail — every vertex, every control point — and catches
/// what rounding hides. <b>A failure in the hash alone, with every summary field equal, is itself
/// a finding</b>, and the report says so in those words rather than leaving the reader to notice.
/// </para>
/// <para>
/// <b>Fixtures must be built from exact arithmetic, and this is the one rule that matters.</b>
/// <c>VisualRegressionTests</c> carries a long note about the same hazard: the one thing in that
/// pipeline not guaranteed bit-identical across platforms is <c>MathF.Sin</c> and
/// <c>MathF.Cos</c>. The same applies here, and worse, because a hash has no tolerance at all.
/// <b>Addition, subtraction, multiplication, division and square root are correctly rounded by
/// IEEE 754 and therefore identical on every machine</b>; the transcendentals are not. NURBS
/// evaluation, polygon area, the divergence-theorem volume and every bounding box are the former.
/// A sphere, a cone, an arc and a circle are the latter, and are deliberately not golden fixtures.
/// </para>
/// <para>
/// <b><see cref="Compare"/> returns <see langword="null"/> when nothing moved</b>, matching
/// <c>VisualRegressionTests.Compare</c> — the same shape, so the two read alike — and the
/// <c>SPARK_UPDATE_GOLDEN=1</c> rewrite is the same deliberate, visible act there and here.
/// </para>
/// </remarks>
internal static class GeometryGolden
{
    /// <summary>Set to <c>1</c> to rewrite goldens instead of asserting against them.</summary>
    internal const string UpdateVariable = "SPARK_UPDATE_GOLDEN";

    /// <summary>Ten decimal places: far below any real change, far above any last-ulp wobble.</summary>
    private const string Number = "0.##########";

    /// <summary>How many points a curve or surface contributes to its hash.</summary>
    /// <remarks>
    /// Only used for shapes with no exact description of their own. A <see cref="NurbsCurve"/>
    /// hashes its degree, knots, control points and weights instead, because those <i>are</i> the
    /// curve and a sample is only a shadow of it.
    /// </remarks>
    private const int Samples = 64;

    /// <summary>The measurements for one piece of geometry, in a stable order.</summary>
    /// <param name="geometry">A mesh, curve, surface, bounding box or point.</param>
    /// <returns>The summary, ending with the exact hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    /// <exception cref="NotSupportedException">Nothing here knows how to summarise it.</exception>
    internal static IReadOnlyList<Measurement> Summarise(object geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        List<Measurement> summary = [];
        StringBuilder detail = new();

        switch (geometry)
        {
            case Mesh mesh:
                Describe(mesh, summary, detail);
                break;

            case Curve curve:
                Describe(curve, summary, detail);
                break;

            case Surface surface:
                Describe(surface, summary, detail);
                break;

            default:
                throw new NotSupportedException(
                    $"No golden summary is defined for {geometry.GetType().Name}. Add one here "
                    + "rather than hashing it blind: a golden nobody can read is the thing this "
                    + "class exists to avoid.");
        }

        summary.Add(new Measurement("hash", Hash(detail.ToString())));
        return summary;
    }

    /// <summary>
    /// The header line every golden carries, asserted on read.
    /// </summary>
    /// <remarks>
    /// <b>The house format for a tabular fixture, which this is the third of.</b>
    /// <c>tests/corpus/dynamo-parity.tsv</c> and its exclusions file both open with a <c>#</c>
    /// provenance block and a header row that their reader asserts <i>literally</i>, so that a
    /// column reordered by hand is caught by the reader rather than silently reinterpreted. The
    /// same rule applies here for the same reason.
    /// </remarks>
    private const string Header = "Field\tValue";

    /// <summary>The summary as a tab-separated fixture, with the provenance a reader needs.</summary>
    /// <param name="name">The fixture's name, for the comment block.</param>
    /// <param name="summary">The measurements.</param>
    /// <returns>The file's contents, newline-terminated.</returns>
    internal static string ToText(string name, IEnumerable<Measurement> summary) =>
        $"# Golden summary for '{name}' - E11-T11. Generated; do not hand-edit.\n"
        + $"# Rewrite it with {UpdateVariable}=1, then READ THE DIFF before committing it.\n"
        + "#\n"
        + "# Every row but the last is rounded to ten decimal places and is meant to be compared by\n"
        + "# eye. `hash` is over the EXACT BITS of the canonical detail - every vertex, every control\n"
        + "# point - so a hash that moves while every other row holds means the shape changed below\n"
        + "# the printed precision: a reordered vertex, an inserted knot, rearranged arithmetic.\n"
        + "#\n"
        + "# The fixture is built from exact arithmetic only. Sin, Cos and Pow are not guaranteed\n"
        + "# bit-identical across platforms and a hash has no tolerance, so no sphere, cone, arc or\n"
        + "# circle is a golden fixture here.\n"
        + Header + "\n"
        + string.Concat(summary.Select(m => $"{m.Field}\t{m.Value}\n"));

    /// <summary>Reads back what <see cref="ToText"/> wrote.</summary>
    /// <param name="text">The file's contents.</param>
    /// <returns>The measurements, in file order.</returns>
    /// <exception cref="InvalidDataException">The header is missing or is not the expected one.</exception>
    internal static IReadOnlyList<Measurement> Parse(string text)
    {
        List<Measurement> summary = [];
        bool seenHeader = false;

        foreach (string raw in (text ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!seenHeader)
            {
                if (!string.Equals(line, Header, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"A golden's first row must be '{Header.Replace("\t", "<tab>", StringComparison.Ordinal)}' and this one is "
                        + $"'{line.Replace("\t", "<tab>", StringComparison.Ordinal)}'. A reordered column read as if it "
                        + "had not moved is worse than a file that will not parse.");
                }

                seenHeader = true;
                continue;
            }

            string[] parts = line.Split('\t', 2);

            if (parts.Length == 2)
            {
                summary.Add(new Measurement(parts[0], parts[1]));
            }
        }

        if (!seenHeader)
        {
            throw new InvalidDataException("A golden has no header row, so nothing about it can be trusted.");
        }

        return summary;
    }

    /// <summary>
    /// The diff table, or <see langword="null"/> when the two agree on every measurement.
    /// </summary>
    /// <param name="golden">What was committed.</param>
    /// <param name="actual">What this run produced.</param>
    /// <returns>A table naming every field, marking the ones that moved.</returns>
    /// <remarks>
    /// <b>Every field is printed, not only the ones that changed.</b> A volume that moved means
    /// one thing beside an unchanged face count and another beside a face count that halved, and
    /// the reader cannot tell which without both. Ten rows is not a wall of text; a hash alone is
    /// not a diagnosis.
    /// </remarks>
    internal static string? Compare(
        IReadOnlyList<Measurement> golden,
        IReadOnlyList<Measurement> actual)
    {
        ArgumentNullException.ThrowIfNull(golden);
        ArgumentNullException.ThrowIfNull(actual);

        Dictionary<string, string> committed = golden.ToDictionary(m => m.Field, m => m.Value, StringComparer.Ordinal);
        Dictionary<string, string> produced = actual.ToDictionary(m => m.Field, m => m.Value, StringComparer.Ordinal);

        List<string> fields =
        [
            .. golden.Select(m => m.Field),
            .. actual.Select(m => m.Field).Where(f => !committed.ContainsKey(f)),
        ];

        List<(string Field, string Golden, string Actual, string Delta)> rows = [];
        int moved = 0;

        foreach (string field in fields)
        {
            string was = committed.GetValueOrDefault(field, "(absent)");
            string now = produced.GetValueOrDefault(field, "(absent)");
            bool changed = !string.Equals(was, now, StringComparison.Ordinal);

            if (changed)
            {
                moved++;
            }

            rows.Add((field, was, now, changed ? Delta(was, now) : string.Empty));
        }

        if (moved == 0)
        {
            return null;
        }

        return Table(rows) + Verdict(rows, moved);
    }

    /// <summary>
    /// Compares a piece of geometry against its committed golden, failing with the diff table.
    /// </summary>
    /// <param name="name">The fixture's name. Becomes <c>tests/corpus/geometry/NAME.tsv</c>.</param>
    /// <param name="geometry">The geometry.</param>
    /// <returns>Null when it matches, or the report to fail with.</returns>
    /// <remarks>
    /// Returns the report rather than asserting, so that the caller decides — and so that the
    /// tests of this class can exercise the whole flow without a failing assertion of their own.
    /// </remarks>
    internal static string? Check(string name, object geometry)
    {
        string path = PathFor(name);
        IReadOnlyList<Measurement> actual = Summarise(geometry);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToText(name, actual));
            return $"Golden rewritten at {path}. Unset {UpdateVariable} and re-run; read the file before committing it.";
        }

        if (!File.Exists(path))
        {
            return $"No golden at {path}. Run with {UpdateVariable}=1 to create one, then read it.";
        }

        string? report = Compare(Parse(File.ReadAllText(path)), actual);

        if (report is null)
        {
            return null;
        }

        string actualPath = Path.ChangeExtension(path, ".actual.tsv");
        File.WriteAllText(actualPath, ToText(name, actual));

        return $"{name} does not match its golden.\n\n{report}\n"
            + $"The summary this run produced was written to {actualPath}.\n"
            + $"If the change is intended, re-run with {UpdateVariable}=1 and commit the new golden.";
    }

    /// <summary>Where a fixture's golden lives.</summary>
    /// <param name="name">The fixture's name.</param>
    /// <returns>The absolute path.</returns>
    internal static string PathFor(string name) =>
        Path.Combine(RepositoryRoot(), "tests", "corpus", "geometry", name + ".tsv");

    private static void Describe(Mesh mesh, List<Measurement> summary, StringBuilder detail)
    {
        summary.Add(new Measurement("kind", "mesh"));
        summary.Add(new Measurement("mesh.vertices", mesh.VertexCount.ToString(CultureInfo.InvariantCulture)));
        summary.Add(new Measurement("mesh.faces", mesh.FaceCount.ToString(CultureInfo.InvariantCulture)));
        summary.Add(new Measurement("mesh.closed", mesh.Topology.IsClosed ? "yes" : "no"));
        summary.Add(new Measurement("mesh.naked edges", mesh.Topology.NakedEdges().Length.ToString(CultureInfo.InvariantCulture)));
        summary.Add(new Measurement("mesh.area", Format(mesh.Area)));

        // A SIGNED TETRAHEDRON SUM OVER AN OPEN MESH IS A NUMBER AND NOT A VOLUME. Mesh.Volume's
        // own remarks say to check the topology first, so this summary says `open` where a reader
        // would otherwise see a plausible figure and compare it to another plausible figure.
        summary.Add(new Measurement(
            "mesh.volume",
            mesh.Topology.IsClosed ? Format(mesh.Volume()) : "n/a (open)"));
        Bounds(mesh.BoundingBox, summary);

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Exact(mesh.Vertex(i), detail);
        }

        for (int i = 0; i < mesh.FaceCount; i++)
        {
            MeshFace face = mesh.Face(i);
            detail.Append(face.A).Append(' ').Append(face.B).Append(' ').Append(face.C).Append('\n');
        }
    }

    private static void Describe(Curve curve, List<Measurement> summary, StringBuilder detail)
    {
        summary.Add(new Measurement("kind", "curve/" + curve.GetType().Name));
        summary.Add(new Measurement("curve.closed", curve.IsClosed ? "yes" : "no"));
        summary.Add(new Measurement("curve.domain", $"{Format(curve.Domain.Min)} to {Format(curve.Domain.Max)}"));
        summary.Add(new Measurement("curve.length", Format(curve.Length)));
        summary.Add(new Measurement("curve.start", Format(curve.StartPoint)));
        summary.Add(new Measurement("curve.end", Format(curve.EndPoint)));
        Bounds(curve.BoundingBox, summary);

        // A NURBS curve IS its knots and control points, so hash those rather than a sample of
        // the shape they make. Anything else has no exact description of its own and is sampled.
        if (curve is NurbsCurve nurbs)
        {
            summary.Add(new Measurement("nurbs.degree", nurbs.Degree.ToString(CultureInfo.InvariantCulture)));
            summary.Add(new Measurement("nurbs.control points", nurbs.ControlPoints().Length.ToString(CultureInfo.InvariantCulture)));
            summary.Add(new Measurement("nurbs.rational", nurbs.IsRational ? "yes" : "no"));

            detail.Append(nurbs.Degree).Append('\n');

            foreach (double knot in nurbs.Knots.ToArray())
            {
                Exact(knot, detail);
            }

            foreach (Point3d point in nurbs.ControlPoints())
            {
                Exact(point, detail);
            }

            foreach (double weight in nurbs.Weights())
            {
                Exact(weight, detail);
            }

            return;
        }

        for (int i = 0; i < Samples; i++)
        {
            Exact(curve.PointAt(curve.Domain.Denormalise(i / (double)(Samples - 1))), detail);
        }
    }

    private static void Describe(Surface surface, List<Measurement> summary, StringBuilder detail)
    {
        summary.Add(new Measurement("kind", "surface/" + surface.GetType().Name));
        summary.Add(new Measurement("surface.closed u", surface.IsClosedU ? "yes" : "no"));
        summary.Add(new Measurement("surface.closed v", surface.IsClosedV ? "yes" : "no"));
        summary.Add(new Measurement("surface.area", Format(surface.Area)));
        Bounds(surface.BoundingBox, summary);

        const int side = 8;
        for (int i = 0; i < side; i++)
        {
            for (int j = 0; j < side; j++)
            {
                Exact(
                    surface.PointAt(
                        surface.DomainU.Denormalise(i / (double)(side - 1)),
                        surface.DomainV.Denormalise(j / (double)(side - 1))),
                    detail);
            }
        }
    }

    private static void Bounds(BoundingBox box, List<Measurement> summary)
    {
        summary.Add(new Measurement("bbox.min", Format(box.Min)));
        summary.Add(new Measurement("bbox.max", Format(box.Max)));
        summary.Add(new Measurement("bbox.diagonal", Format(box.Diagonal.Length)));
    }

    /// <summary>The exact bits, so the hash sees what the printed summary rounds away.</summary>
    private static void Exact(double value, StringBuilder detail) =>
        detail.Append(BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Exact(in Point3d point, StringBuilder detail)
    {
        Exact(point.X, detail);
        Exact(point.Y, detail);
        Exact(point.Z, detail);
    }

    private static string Format(double value) => value.ToString(Number, CultureInfo.InvariantCulture);

    private static string Format(in Point3d point) =>
        $"({Format(point.X)}, {Format(point.Y)}, {Format(point.Z)})";

    private static string Hash(string detail) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(detail)))[..16];

    /// <summary>The signed difference when both values are numbers, and "changed" when they are not.</summary>
    private static string Delta(string was, string now) =>
        double.TryParse(was, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
        && double.TryParse(now, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)
            ? (b - a).ToString("+" + Number + ";-" + Number + ";0", CultureInfo.InvariantCulture)
            : "changed";

    private static string Table(IReadOnlyList<(string Field, string Golden, string Actual, string Delta)> rows)
    {
        int field = Math.Max(5, rows.Max(r => r.Field.Length));
        int golden = Math.Max(6, rows.Max(r => r.Golden.Length));
        int actual = Math.Max(6, rows.Max(r => r.Actual.Length));

        StringBuilder table = new();
        table.Append("  ").Append("field".PadRight(field))
             .Append("  ").Append("golden".PadRight(golden))
             .Append("  ").Append("actual".PadRight(actual))
             .Append("  delta\n");
        table.Append("  ").Append(new string('-', field + golden + actual + 12)).Append('\n');

        foreach ((string name, string was, string now, string delta) in rows)
        {
            table.Append(delta.Length == 0 ? "  " : "! ")
                 .Append(name.PadRight(field))
                 .Append("  ").Append(was.PadRight(golden))
                 .Append("  ").Append(now.PadRight(actual))
                 .Append("  ").Append(delta).Append('\n');
        }

        return table.ToString();
    }

    /// <summary>
    /// The sentence after the table. <b>A hash that moved on its own is the interesting case</b>
    /// and the one a reader is least likely to interpret correctly without being told.
    /// </summary>
    private static string Verdict(
        IReadOnlyList<(string Field, string Golden, string Actual, string Delta)> rows,
        int moved)
    {
        bool hashOnly = moved == 1 && rows.Single(r => r.Delta.Length > 0).Field == "hash";

        return hashOnly
            ? "\nOnly the hash moved. Every printed measurement agrees, so the shape changed below "
              + "ten decimal places: a different vertex order, a knot inserted without changing the "
              + "curve, or arithmetic reordered. It is a real difference and it is not a different "
              + "shape.\n"
            : $"\n{moved} of {rows.Count} measurements moved.\n";
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("the repository root was not found above " + AppContext.BaseDirectory)
            : directory.FullName;
    }
}

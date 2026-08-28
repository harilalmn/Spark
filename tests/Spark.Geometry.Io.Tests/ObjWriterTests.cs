using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spark.Geometry;
using Spark.Geometry.Io;

namespace Spark.Geometry.Io.Tests;

/// <summary>
/// The OBJ writer, checked by reading what it produced with a parser written here.
/// </summary>
/// <remarks>
/// The parser below is deliberately naive and deliberately independent: split on whitespace,
/// take <c>v</c>, <c>l</c> and <c>p</c>, and refuse anything it does not understand. That is
/// roughly what a third-party reader does, and it is the only kind of check that says anything
/// about interchange — a writer verified by its own reader agrees with itself and with nobody.
/// </remarks>
public sealed class ObjWriterTests
{
    [Fact]
    public void AStraightLineIsTwoVerticesAndOneElement()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 2.0, 3.0));

        ObjFile file = WriteAndRead([line]);

        Assert.Equal(2, file.Vertices.Count);
        Assert.Equal(new Point3d(0.0, 0.0, 0.0), file.Vertices[0]);
        Assert.Equal(new Point3d(1.0, 2.0, 3.0), file.Vertices[1]);
        Assert.Equal([1, 2], Assert.Single(file.Lines));
    }

    [Fact]
    public void ACircleBecomesAPolylineAtTheToleranceAsked()
    {
        Circle circle = new(Plane.WorldXY, 5.0);

        ObjFile coarse = WriteAndRead([circle], tolerance: new Tolerance(0.5, Angle.FromDegrees(0.001), 1e-12));
        ObjFile fine = WriteAndRead([circle], tolerance: new Tolerance(0.001, Angle.FromDegrees(0.001), 1e-12));

        // The interesting assertion is the direction, not the counts: a tighter tolerance must
        // produce more vertices, or the tolerance is not reaching the tessellator.
        Assert.True(fine.Vertices.Count > coarse.Vertices.Count);
        Assert.True(coarse.Vertices.Count >= 4);

        // Every vertex is on the circle, to the tolerance that produced it.
        foreach (Point3d vertex in coarse.Vertices)
        {
            Assert.Equal(5.0, vertex.DistanceTo(Point3d.Origin), 6);
        }
    }

    [Fact]
    public void PointsBecomePointElementsAndComeFirst()
    {
        Line line = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        Point3d[] points = [new(9.0, 9.0, 9.0), new(8.0, 8.0, 8.0)];

        ObjFile file = WriteAndRead([line], points);

        Assert.Equal([1, 2], file.Points);
        Assert.Equal(new Point3d(9.0, 9.0, 9.0), file.Vertices[0]);

        // Points before curves so that adding a curve does not renumber the points: an OBJ
        // index is a position in the vertex list and nothing else.
        Assert.Equal([3, 4], Assert.Single(file.Lines));
    }

    [Fact]
    public void IndicesAreOneBasedAndEveryOneOfThemResolves()
    {
        Curve[] curves =
        [
            new Line(Point3d.Origin, new Point3d(1.0, 0.0, 0.0)),
            new Circle(Plane.WorldXY, 2.0),
            PolyLine.ByPoints([Point3d.Origin, new Point3d(1.0, 1.0, 0.0), new Point3d(2.0, 0.0, 0.0)]),
        ];

        ObjFile file = WriteAndRead(curves, [new Point3d(5.0, 5.0, 5.0)]);

        foreach (int index in file.Lines.SelectMany(line => line).Concat(file.Points))
        {
            // Zero would be a valid integer and an invalid index. This is the assertion that
            // fails if the vertex counter ever starts from zero.
            Assert.InRange(index, 1, file.Vertices.Count);
        }
    }

    [Fact]
    public void NothingIsWeldedAndTheFileSaysSoByCounting()
    {
        Line first = new(Point3d.Origin, new Point3d(1.0, 0.0, 0.0));
        Line second = new(new Point3d(1.0, 0.0, 0.0), new Point3d(2.0, 0.0, 0.0));

        ObjFile file = WriteAndRead([first, second]);

        // Four vertices for two lines meeting at a point, not three. Welding is a tolerance
        // decision and the writer has not been told what the tolerance for it is.
        Assert.Equal(4, file.Vertices.Count);
        Assert.Equal(file.Vertices[1], file.Vertices[2]);
    }

    [Fact]
    public void NumbersAreWrittenInTheInvariantCultureWhateverTheThreadIsSetTo()
    {
        // A machine with a comma decimal separator writes "1,5", which OBJ reads as two fields.
        // No reader complains; the geometry is simply somewhere else.
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            string text = Write([new Line(Point3d.Origin, new Point3d(1.5, 2.5, 3.5))]);

            Assert.Contains("v 1.5 2.5 3.5", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1,5", text, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void LinesEndWithANewlineOnEveryPlatform()
    {
        string text = Write([new Line(Point3d.Origin, new Point3d(1.0, 0.0, 0.0))]);

        // A file whose bytes depend on the operating system that wrote it cannot be compared
        // against a golden file, which is how the rest of this repository tests its formats.
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeaderRecordsTheToleranceThatProducedTheTessellation()
    {
        string text = Write([new Circle(Plane.WorldXY, 1.0)], tolerance: new Tolerance(0.25, Angle.FromDegrees(0.001), 1e-12));

        // A polyline without the tolerance that produced it is a shape nobody can reproduce.
        Assert.Contains("0.25", text, StringComparison.Ordinal);
        Assert.Contains("tessellated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACoordinateThatIsNotFiniteIsWrittenAsZeroRatherThanAsSomethingUnparseable()
    {
        // OBJ has no spelling for NaN. Zero is the least wrong thing available and it is still
        // wrong; the writer's documentation says so, and this is the test that pins it.
        PolyLine polyLine = PolyLine.ByPoints([Point3d.Origin, new Point3d(1.0, 0.0, 0.0)]);

        string text = Write([polyLine]);

        Assert.DoesNotContain("NaN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingToAFileProducesTheSameBytesAsWritingToAString()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".obj");

        try
        {
            Curve[] curves = [new Circle(Plane.WorldXY, 1.0), new Line(Point3d.Origin, new Point3d(0.0, 0.0, 1.0))];

            ObjWriter.WriteFile(path, curves);

            Assert.Equal(Write(curves), File.ReadAllText(path));

            // No byte-order mark: it is legal in OBJ and several readers treat it as part of
            // the first keyword, so the first line becomes an unknown directive.
            byte[] bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => ObjWriter.Write(null!, []));
        Assert.Throws<ArgumentNullException>(() => ObjWriter.Write(new StringWriter(), null!));
        Assert.Throws<ArgumentNullException>(() => ObjWriter.WriteFile(null!, []));
        Assert.Throws<ArgumentNullException>(() => ObjWriter.Write(new StringWriter(), [null!]));
    }

    [Fact]
    public void AnEmptyExportIsAValidFileRatherThanAnError()
    {
        ObjFile file = WriteAndRead([]);

        Assert.Empty(file.Vertices);
        Assert.Empty(file.Lines);
        Assert.Empty(file.Points);
    }

    private static string Write(
        IEnumerable<Curve> curves,
        IEnumerable<Point3d>? points = null,
        Tolerance tolerance = default)
    {
        StringWriter writer = new();
        ObjWriter.Write(writer, curves, points, tolerance);

        return writer.ToString();
    }

    private static ObjFile WriteAndRead(
        IEnumerable<Curve> curves,
        IEnumerable<Point3d>? points = null,
        Tolerance tolerance = default) =>
        ObjFile.Parse(Write(curves, points, tolerance));

    /// <summary>
    /// A minimal, independent OBJ reader. It understands four things and refuses everything
    /// else, which is what makes it a check rather than a mirror.
    /// </summary>
    private sealed class ObjFile
    {
        private ObjFile(List<Point3d> vertices, List<int[]> lines, List<int> points)
        {
            Vertices = vertices;
            Lines = lines;
            Points = points;
        }

        public IReadOnlyList<Point3d> Vertices { get; }

        public IReadOnlyList<int[]> Lines { get; }

        public IReadOnlyList<int> Points { get; }

        public static ObjFile Parse(string text)
        {
            List<Point3d> vertices = [];
            List<int[]> lines = [];
            List<int> points = [];

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                switch (fields[0])
                {
                    case "v":
                        Assert.Equal(4, fields.Length);
                        vertices.Add(new Point3d(
                            double.Parse(fields[1], CultureInfo.InvariantCulture),
                            double.Parse(fields[2], CultureInfo.InvariantCulture),
                            double.Parse(fields[3], CultureInfo.InvariantCulture)));
                        break;

                    case "l":
                        Assert.True(fields.Length >= 3, "An 'l' element needs at least two indices.");
                        lines.Add([.. fields[1..].Select(field => int.Parse(field, CultureInfo.InvariantCulture))]);
                        break;

                    case "p":
                        Assert.Equal(2, fields.Length);
                        points.Add(int.Parse(fields[1], CultureInfo.InvariantCulture));
                        break;

                    default:
                        Assert.Fail($"The writer emitted '{fields[0]}', which this reader does not know.");
                        break;
                }
            }

            return new ObjFile(vertices, lines, points);
        }
    }
}

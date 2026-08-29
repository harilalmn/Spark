using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spark.Geometry;
using Spark.Geometry.Io;

namespace Spark.Geometry.Tests;

public sealed class ObjWriterTests
{
    private static readonly Curve[] Two =
    [
        new Line(new Point3d(0.0, 0.0, 0.0), new Point3d(1.0, 0.0, 0.0)),
        Circle.ByCentreRadius(new Point3d(5.0, 0.0, 0.0), 2.0),
    ];

    [Fact]
    public void TheHeaderRecordsTheToleranceTheFileWasWrittenAt()
    {
        // How round the circle is is a property of this file, so the answer belongs inside it
        // rather than in whoever ran the export.
        string obj = Write(Two, new Tolerance(0.01, Angle.FromDegrees(0.1), 1e-12));

        Assert.Contains("# Wavefront OBJ written by Spark", obj);
        Assert.Contains("# curves: 2", obj);
        Assert.Contains("# tessellation tolerance: 0.01", obj);
    }

    [Fact]
    public void EachCurveBecomesOneNamedObjectAndOnePolyline()
    {
        string[] lines = Lines(Write(Two));

        Assert.Equal(2, lines.Count(line => line.StartsWith("o ", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(line => line.StartsWith("l ", StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.StartsWith("o Line", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("o Circle", StringComparison.Ordinal));
    }

    [Fact]
    public void VertexIndicesAreOneBasedAndKeepCountingAcrossCurves()
    {
        // OBJ indices are one-based and file-global. Restarting them per object, or starting at
        // zero, is the classic way to produce a file that opens and draws nonsense - which is
        // worse than one that fails to open.
        string[] lines = Lines(Write(Two));
        int vertices = lines.Count(line => line.StartsWith("v ", StringComparison.Ordinal));

        List<int> indices = [];
        foreach (string line in lines.Where(line => line.StartsWith("l ", StringComparison.Ordinal)))
        {
            indices.AddRange(line.Split(' ').Skip(1).Select(int.Parse));
        }

        Assert.Equal(1, indices.Min());
        Assert.Equal(vertices, indices.Max());
        Assert.Equal(vertices, indices.Distinct().Count());

        // The second curve's first index follows the first curve's last, with no gap and no reuse.
        string[] polylines = [.. lines.Where(line => line.StartsWith("l ", StringComparison.Ordinal))];
        int firstEnd = polylines[0].Split(' ').Skip(1).Select(int.Parse).Last();
        int secondStart = polylines[1].Split(' ').Skip(1).Select(int.Parse).First();

        Assert.Equal(firstEnd + 1, secondStart);
    }

    [Fact]
    public void NumbersAreWrittenWithTheInvariantCultureWhateverTheThreadIsSetTo()
    {
        // A German locale writes 1,5 for one and a half, which produces an OBJ that viewers
        // misread or reject - and only on some machines, which is the worst way to find a bug.
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            string obj = Write([new Line(new Point3d(1.5, -2.25, 0.125), new Point3d(3.5, 0.0, 0.0))]);

            Assert.Contains("v 1.5 -2.25 0.125", obj);
            Assert.DoesNotContain(",", obj);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ACoarserToleranceProducesFewerVertices()
    {
        int fine = Lines(Write(Two, new Tolerance(1e-5, Angle.FromDegrees(0.1), 1e-12)))
            .Count(line => line.StartsWith("v ", StringComparison.Ordinal));
        int coarse = Lines(Write(Two, new Tolerance(1e-2, Angle.FromDegrees(0.1), 1e-12)))
            .Count(line => line.StartsWith("v ", StringComparison.Ordinal));

        Assert.True(coarse < fine, $"coarse {coarse} should be fewer than fine {fine}");
        Assert.True(coarse >= 4);
    }

    [Fact]
    public void NullCurvesAreSkippedRatherThanThrowing()
    {
        // A list with a hole in it is what a graph produces when one node fails, and refusing to
        // write the rest would lose the work that succeeded.
        string obj = Write([Two[0], null!, Two[1]]);

        Assert.Equal(2, Lines(obj).Count(line => line.StartsWith("o ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnEmptySequenceWritesAHeaderAndNothingElse()
    {
        string[] lines = Lines(Write([]));

        Assert.Contains("# curves: 0", string.Join('\n', lines));
        Assert.DoesNotContain(lines, line => line.StartsWith("v ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFileIsUtf8WithNoByteOrderMark()
    {
        // A BOM is legal in a text file and several OBJ readers choke on it, because they
        // compare the first token against "v" byte for byte.
        string path = Path.Combine(Path.GetTempPath(), $"spark-obj-{Guid.NewGuid():N}.obj");

        try
        {
            Assert.Equal(2, ObjWriter.WriteCurvesToFile(path, Two));

            byte[] bytes = File.ReadAllBytes(path);

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Equal('#', (char)bytes[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => ObjWriter.WriteCurves(null!, Two));
        Assert.Throws<ArgumentNullException>(() => ObjWriter.WriteCurves(new StringWriter(), null!));
        Assert.Throws<ArgumentNullException>(() => ObjWriter.WriteCurvesToFile(null!, Two));
    }

    private static string Write(IEnumerable<Curve> curves, in Tolerance tolerance = default)
    {
        StringWriter writer = new(CultureInfo.InvariantCulture);

        ObjWriter.WriteCurves(writer, curves, tolerance);

        return writer.ToString();
    }

    private static string[] Lines(string obj) =>
        [.. obj.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
}

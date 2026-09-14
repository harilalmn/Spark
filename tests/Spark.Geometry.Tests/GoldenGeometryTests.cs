using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spark.Geometry.Tests;

/// <summary>
/// The golden-file check over a fixed set of shapes, and the diff table it fails with
/// (<c>E11-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What a golden here is for.</b> The rest of this suite asserts named properties — this arc
/// is tangent, that offset is at the right distance. A golden asserts something none of them can:
/// that the <i>whole</i> shape is the one it was yesterday, including the parts nobody wrote an
/// assertion about. It is the cheapest possible guard against a refactor that quietly moves a
/// vertex, and the most useless possible failure message unless it is built to say what moved —
/// which is the whole of <see cref="GeometryGolden"/>.
/// </para>
/// <para>
/// <b>Every fixture is built from exact arithmetic, and that is a constraint rather than a
/// preference.</b> The hash has no tolerance, so a fixture whose construction runs through
/// <c>Sin</c>, <c>Cos</c> or <c>Pow</c> could differ between Windows and Linux legitimately and
/// turn the check red for no reason — the hazard <c>VisualRegressionTests</c> already documents at
/// length. <b>So there is no sphere, no cone, no arc and no circle here</b>, and their absence is
/// deliberate rather than an oversight: they are covered by property tests that can afford a
/// tolerance, and golden files cannot.
/// </para>
/// <para>
/// <b>The checks of the checker come first.</b> A golden suite that passes because its comparison
/// is broken is the failure this project has a note about ([N167](../../docs/NOTES.md)), so the
/// diff table is tested directly — against a summary deliberately changed — before any fixture is
/// compared to anything.
/// </para>
/// </remarks>
public sealed class GoldenGeometryTests
{
    /// <summary>The fixtures, by the name their golden file carries.</summary>
    /// <remarks>
    /// Named rather than anonymous so that a failure says <c>extruded-square</c> and not
    /// <c>fixture 3</c>, and so that the golden files are readable on their own.
    /// </remarks>
    public static TheoryData<string> Names =>
        [.. Fixtures().Keys];

    /// <summary>Every fixture matches the summary committed beside it.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheFixtureMatchesItsGolden(string name)
    {
        string? report = GeometryGolden.Check(name, Fixtures()[name]());

        Assert.True(report is null, report);
    }

    /// <summary>
    /// <b>Every fixture is deterministic.</b> If a shape is not the same twice in one process, the
    /// golden above is meaningless and this is the test that says so first.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheFixtureIsTheSameTwice(string name)
    {
        Func<object> build = Fixtures()[name];

        Assert.Equal(
            GeometryGolden.ToText(name, GeometryGolden.Summarise(build())),
            GeometryGolden.ToText(name, GeometryGolden.Summarise(build())));
    }

    /// <summary>
    /// <b>Every golden is committed.</b> A missing file makes <see cref="GeometryGolden.Check"/>
    /// report rather than throw, which is right for a first run and wrong for a green suite.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheGoldenIsInTheRepository(string name)
    {
        string path = GeometryGolden.PathFor(name);

        Assert.True(File.Exists(path), $"no golden at {path}; run with {GeometryGolden.UpdateVariable}=1 and read it before committing.");
    }

    /// <summary>An identical pair reports no difference, so the comparison is not simply always red.</summary>
    [Fact]
    public void AnIdenticalPairReportsNoDifference()
    {
        IReadOnlyList<Measurement> summary = GeometryGolden.Summarise(ClosedCube());

        Assert.Null(GeometryGolden.Compare(summary, summary));
    }

    /// <summary>
    /// <b>The report names the field, both values and the signed difference.</b> This is the row's
    /// actual deliverable — <i>a bare hash mismatch tells you nothing</i> — so it is asserted
    /// directly rather than inferred from the check going red.
    /// </summary>
    [Fact]
    public void TheReportNamesWhatMovedAndByHowMuch()
    {
        IReadOnlyList<Measurement> golden = GeometryGolden.Summarise(ClosedCube());
        List<Measurement> actual = [.. golden];
        int volume = actual.FindIndex(m => m.Field == "mesh.volume");

        actual[volume] = new Measurement("mesh.volume", "10");

        string report = Assert.IsType<string>(GeometryGolden.Compare(golden, actual));

        Assert.Contains("mesh.volume", report, StringComparison.Ordinal);
        Assert.Contains("10", report, StringComparison.Ordinal);
        Assert.Contains("+2", report, StringComparison.Ordinal);

        // The context the row asks for is present whether or not it moved. A volume that changed
        // beside an unchanged face count means something different from one beside a face count
        // that halved, and the reader cannot tell which without both in front of them.
        Assert.Contains("mesh.faces", report, StringComparison.Ordinal);
        Assert.Contains("mesh.area", report, StringComparison.Ordinal);
        Assert.Contains("bbox.min", report, StringComparison.Ordinal);
        Assert.Contains("bbox.max", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A hash that moves on its own is explained rather than reported.</b> It is the one
    /// outcome a reader is likely to misread as a broken check, so the report says in words what
    /// it means: the shape changed below the printed precision.
    /// </summary>
    [Fact]
    public void AMovedHashWithEveryMeasurementEqualIsExplained()
    {
        IReadOnlyList<Measurement> golden = GeometryGolden.Summarise(ClosedCube());
        List<Measurement> actual = [.. golden];
        int hash = actual.FindIndex(m => m.Field == "hash");

        actual[hash] = new Measurement("hash", "0000000000000000");

        string report = Assert.IsType<string>(GeometryGolden.Compare(golden, actual));

        Assert.Contains("Only the hash moved", report, StringComparison.Ordinal);
        Assert.Contains("below", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The hash catches what the summary rounds away.</b> A vertex moved by 1e-15 changes no
    /// printed measurement, and the check must still fail — otherwise the summary <i>is</i> the
    /// check and the hash is decoration.
    /// </summary>
    [Fact]
    public void AChangeTooSmallToPrintStillFails()
    {
        Mesh original = ClosedCube();
        List<Point3d> moved = [.. Enumerable.Range(0, original.VertexCount).Select(original.Vertex)];
        moved[0] = new Point3d(moved[0].X + 1e-15, moved[0].Y, moved[0].Z);

        Mesh nudged = new(moved, [.. Enumerable.Range(0, original.FaceCount).Select(original.Face)]);

        IReadOnlyList<Measurement> before = GeometryGolden.Summarise(original);
        IReadOnlyList<Measurement> after = GeometryGolden.Summarise(nudged);

        Assert.Equal(
            before.Where(m => m.Field != "hash"),
            after.Where(m => m.Field != "hash"));

        Assert.NotNull(GeometryGolden.Compare(before, after));
    }

    /// <summary>A field present in one summary and not the other is reported, not skipped.</summary>
    /// <remarks>
    /// This is how a golden written before a measurement existed behaves, and silently ignoring
    /// the new field would mean a golden that stops checking the thing that was added.
    /// </remarks>
    [Fact]
    public void AFieldThatOnlyOneSideHasIsReported()
    {
        IReadOnlyList<Measurement> golden = GeometryGolden.Summarise(ClosedCube());
        List<Measurement> actual = [.. golden, new Measurement("mesh.genus", "0")];

        string report = Assert.IsType<string>(GeometryGolden.Compare(golden, actual));

        Assert.Contains("mesh.genus", report, StringComparison.Ordinal);
        Assert.Contains("(absent)", report, StringComparison.Ordinal);
    }

    /// <summary>A golden survives being written and read back unchanged.</summary>
    [Fact]
    public void ASummarySurvivesATextRoundTrip()
    {
        IReadOnlyList<Measurement> summary = GeometryGolden.Summarise(ClosedCube());

        Assert.Equal(summary, GeometryGolden.Parse(GeometryGolden.ToText("cuboid", summary)));
    }

    /// <summary>
    /// <b>A golden whose header has moved will not parse.</b> The parity fixtures assert their
    /// header literally for this reason, and the reason is worth restating: a column reordered by
    /// hand and read as though it had not moved is worse than a file that refuses to load, because
    /// the first is a green suite checking the wrong thing.
    /// </summary>
    /// <remarks>
    /// <b>This test exists because a mutation survived without it.</b> Deleting the header check
    /// entirely left all twenty-eight tests green — the guard was written, committed and proved
    /// nothing, which is the same shape as a gate that has never run.
    /// </remarks>
    [Fact]
    public void AGoldenWithTheWrongHeaderRefusesToParse()
    {
        string good = GeometryGolden.ToText("closed-cube", GeometryGolden.Summarise(ClosedCube()));

        Assert.NotEmpty(GeometryGolden.Parse(good));

        InvalidDataException reordered = Assert.Throws<InvalidDataException>(
            () => GeometryGolden.Parse(good.Replace("Field\tValue", "Value\tField", StringComparison.Ordinal)));

        Assert.Contains("Field<tab>Value", reordered.Message, StringComparison.Ordinal);

        InvalidDataException headless = Assert.Throws<InvalidDataException>(
            () => GeometryGolden.Parse("# nothing but a comment\n"));

        Assert.Contains("no header", headless.Message, StringComparison.Ordinal);
    }

    /// <summary>Something with no summary defined says so rather than being hashed blind.</summary>
    [Fact]
    public void AnUnknownShapeRefusesRatherThanHashingBlind()
    {
        NotSupportedException refused =
            Assert.Throws<NotSupportedException>(() => GeometryGolden.Summarise(Plane.WorldXY));

        Assert.Contains("Plane", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixtures. Exact arithmetic only — see the remarks on this class and on
    /// <see cref="GeometryGolden"/>.
    /// </summary>
    private static Dictionary<string, Func<object>> Fixtures() => new(StringComparer.Ordinal)
    {
        // A CLOSED mesh, built by hand with shared vertices, so `mesh.closed` is yes and the
        // volume is a volume. Eight vertices, twelve triangles, area 24 and volume 8, all of them
        // integers - which is what makes this the fixture the report tests use.
        ["closed-cube"] = ClosedCube,

        // MeshPrimitives.Cuboid, WHICH IS NOT CLOSED. Twenty-four vertices for eight corners: the
        // six sides share no vertex, so every one of its edges is naked and the signed tetrahedron
        // sum is not a volume. That surprised this project once already and cost three wrong test
        // fixtures, so it is pinned here rather than remembered.
        ["cuboid"] = Cuboid,

        // An open mesh, so that `mesh.closed` is exercised in both directions and a volume
        // computed over a surface that does not enclose anything is pinned rather than trusted.
        ["plane-grid"] = () => MeshPrimitives.Plane(Plane.WorldXY, 4.0, 2.0, 4, 2),

        // A cubic NURBS curve from explicit control points: hashed as degree, knots, points and
        // weights, which is what the curve IS rather than a sample of what it looks like.
        ["cubic-nurbs"] = () => new NurbsCurve(
            3,
            [
                new Point3d(0, 0, 0),
                new Point3d(1, 2, 0),
                new Point3d(3, -1, 1),
                new Point3d(4, 1, 0),
                new Point3d(6, 0, 2),
            ],
            [0, 0, 0, 0, 0.5, 1, 1, 1, 1]),

        // A rational one, so that the weights are in the hash and `nurbs.rational` is exercised.
        // The weights are halves and quarters: exactly representable, so this stays bit-stable.
        ["rational-nurbs"] = () => new NurbsCurve(
            2,
            [new Point3d(0, 0, 0), new Point3d(2, 3, 0), new Point3d(4, 0, 0)],
            [0, 0, 0, 1, 1, 1],
            [1.0, 0.5, 1.0]),

        // A polyline, which has no exact description of its own and is therefore sampled - the
        // other branch of the hash, and the one no NURBS fixture would reach.
        ["polyline"] = () => new PolyLine(
        [
            new Point3d(0, 0, 0),
            new Point3d(2, 0, 0),
            new Point3d(2, 3, 0),
            new Point3d(0, 3, 1),
            new Point3d(0, 0, 0),
        ]),

        // A surface, so the surface branch of the summary is a fixture and not only a code path.
        ["ruled-surface"] = () => new RuledSurface(
            new PolyLine([new Point3d(0, 0, 0), new Point3d(4, 0, 0)]),
            new PolyLine([new Point3d(0, 3, 1), new Point3d(4, 3, 2)])),
    };

    private static Mesh Cuboid() => MeshPrimitives.Cuboid(Plane.WorldXY, 2.0, 2.0, 2.0);

    /// <summary>
    /// A closed two-by-two-by-two cube with shared vertices: area 24, volume 8, every number an
    /// integer and every coordinate exactly representable.
    /// </summary>
    /// <remarks>
    /// <b>Hand-built rather than taken from <see cref="MeshPrimitives"/></b>, because
    /// <c>MeshPrimitives.Cuboid</c> gives each side its own four vertices and is therefore open —
    /// twenty-four naked edges and no volume. Both are fixtures here; this is the one the report
    /// tests use, so that a deliberate change to <c>mesh.volume</c> produces a delta a reader can
    /// check in their head.
    /// </remarks>
    private static Mesh ClosedCube()
    {
        Point3d[] corners =
        [
            new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
            new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
        ];

        MeshFace[] faces =
        [
            new(0, 3, 2), new(0, 2, 1),   // z = -1
            new(4, 5, 6), new(4, 6, 7),   // z = +1
            new(0, 1, 5), new(0, 5, 4),   // y = -1
            new(3, 7, 6), new(3, 6, 2),   // y = +1
            new(0, 4, 7), new(0, 7, 3),   // x = -1
            new(1, 2, 6), new(1, 6, 5),   // x = +1
        ];

        return new Mesh(corners, faces);
    }
}

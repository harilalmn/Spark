using System;
using System.Linq;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="MeshRemeshing.Remeshed"/>: triangles of one size, the same shape (<c>E2-T68</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every fixture here starts badly conditioned, and that is the point.</b> A remesher that does
/// nothing at all passes every test written over an already-uniform mesh — so the fixtures are a
/// grid stretched twenty to one, and a mesh whose two halves are subdivided differently. The
/// assertion that matters is not *the edges are uniform* but *the edges got more uniform than they
/// were*, measured against the input.
/// </para>
/// <para>
/// <b>The second thing a remesher does wrong is lose the shape.</b> Splitting, collapsing and
/// relaxing all move vertices, and a loop that runs long enough turns any closed mesh into a
/// sphere and then into a point. So every test that measures edges also measures volume or the
/// bounding box.
/// </para>
/// </remarks>
public sealed class MeshRemeshingTests
{
    /// <summary>
    /// <b>A grid stretched twenty to one comes back with edges of about one length.</b> The
    /// measure is the share of interior edges within half and double the target — which is what the
    /// algorithm is actually steering, and what a person means by *the triangles are all about the
    /// same size*.
    /// </summary>
    /// <remarks>
    /// <b>Not the largest edge over the smallest, which the first version of this test used.</b>
    /// That ratio is set entirely by two edges out of hundreds, so it reports a mesh as unimproved
    /// when one sliver survives in a corner and says nothing about the other 99%. It is the right
    /// thing to look at when *hunting* a bad mesh and the wrong thing to assert improvement with.
    /// </remarks>
    [Fact]
    public void AStretchedGridBecomesFarMoreUniform()
    {
        Mesh stretched = Grid(20.0, 1.0, 10, 10);
        double before = InBand(stretched, 1.0);

        Mesh remeshed = MeshRemeshing.Remeshed(stretched, 1.0);
        double after = InBand(remeshed, 1.0);

        // Measured 2026-09-15: 0.32 in, 0.86 out. The bounds are set well inside those so that
        // ordinary drift does not fail the suite, and far enough apart that a remesher doing
        // nothing - which would return the input's 0.32 - cannot pass.
        Assert.True(before < 0.4, $"the fixture is not badly conditioned enough: {before:F3} of its edges are already in band.");
        Assert.True(after > 0.75, $"only {after:F3} of the edges came back in band, against {before:F3} going in.");
    }

    /// <summary>
    /// <b>And it is still the same rectangle.</b> The relaxation pass holds boundary vertices
    /// still, so a remeshed panel keeps its outline exactly — a remesher that shrank the sheet
    /// away from its own border would be useless whatever its triangles looked like.
    /// </summary>
    [Fact]
    public void TheOutlineOfAnOpenMeshIsExactlyWhereItWas()
    {
        Mesh sheet = Grid(20.0, 1.0, 10, 10);

        Mesh remeshed = MeshRemeshing.Remeshed(sheet, 1.0);

        Tolerance exact = new(1e-9, Angle.Zero, 1e-12);
        Assert.True(remeshed.BoundingBox.Min.EqualsWithin(sheet.BoundingBox.Min, exact), remeshed.BoundingBox.Min.ToString());
        Assert.True(remeshed.BoundingBox.Max.EqualsWithin(sheet.BoundingBox.Max, exact), remeshed.BoundingBox.Max.ToString());
    }

    /// <summary>
    /// <b>A coarse region gains triangles.</b> This is the difference between remeshing and
    /// reducing, stated as a test: asked for a length shorter than anything the mesh has,
    /// <see cref="MeshRemeshing.Remeshed"/> must **add** triangles, where
    /// <see cref="MeshDecimation.Reduced"/> can only ever remove them.
    /// </summary>
    [Fact]
    public void ACoarseMeshGainsTrianglesRatherThanLosingThem()
    {
        Mesh coarse = Grid(4.0, 4.0, 2, 2);

        Mesh remeshed = MeshRemeshing.Remeshed(coarse, 0.5);

        Assert.True(
            remeshed.FaceCount > coarse.FaceCount * 4,
            $"asked for edges four times shorter, the mesh went from {coarse.FaceCount} faces to {remeshed.FaceCount}.");
    }

    /// <summary>
    /// <b>A closed mesh stays closed, and keeps its volume.</b> Every pass moves vertices, and the
    /// loop is five iterations of four of them — more than enough to deflate a sphere if the
    /// relaxation is unbalanced or a collapse is tearing holes.
    /// </summary>
    [Fact]
    public void AClosedMeshStaysClosedAndKeepsItsVolume()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 1.0, 24, 12);

        Assert.True(sphere.Topology.IsClosed, "the fixture is not closed, so this test proves nothing.");

        double before = sphere.Volume();
        Mesh remeshed = MeshRemeshing.Remeshed(sphere, 0.25);

        Assert.Equal(0, remeshed.Topology.NakedEdgeCount);
        Assert.Equal(0, remeshed.Topology.NonManifoldEdgeCount);
        Assert.InRange(remeshed.Volume() / before, 0.85, 1.15);
    }

    /// <summary>
    /// <b>Two sheets, one twenty times finer than the other, come out matching.</b> This is the
    /// case a boolean or an importer actually produces — part of the surface subdivided to death
    /// and part of it not at all — and it needs the split and the collapse passes to work in
    /// opposite directions on one mesh in the same call.
    /// </summary>
    /// <remarks>
    /// <b>The first version of this fixture was two grids at 0.25 and 1.33 against a target of
    /// 0.5, and 97% of its edges were already in band before anything ran.</b> It was measured
    /// before it was trusted, which is the only reason that was noticed: a fixture that starts
    /// where it is supposed to finish tests nothing at all.
    /// </remarks>
    [Fact]
    public void AMeshFineOnOneSideAndCoarseOnTheOtherEvensOut()
    {
        Mesh lopsided = Merged(Grid(4.0, 4.0, 40, 40), Translated(Grid(4.0, 4.0, 2, 2), 4.0));
        double before = InBand(lopsided, 0.5);

        Mesh remeshed = MeshRemeshing.Remeshed(lopsided, 0.5);
        double after = InBand(remeshed, 0.5);

        Assert.True(before < 0.35, $"the fixture is not lopsided enough: {before:F3} of its edges are already in band.");
        Assert.True(after > before * 2.0, $"the share in band went from {before:F3} to {after:F3}.");
    }

    /// <summary>
    /// <b>The border keeps its own edge lengths, and that is a limitation stated rather than
    /// discovered.</b> A boundary vertex is never moved and a boundary edge is never collapsed, so
    /// a sheet arriving with a finely chopped outline comes back uniform inside and unchanged
    /// around the edge. **A caller remeshing a panel gets its outline back to the last bit**, which
    /// is the trade this makes and usually the one they want.
    /// </summary>
    /// <remarks>
    /// Collapsing along a straight run of boundary would be safe — the midpoint of a straight
    /// segment is still on it — and is deliberately not done, because telling a straight run from a
    /// corner needs a tolerance <see cref="MeshRemeshing.Remeshed"/> does not take.
    /// </remarks>
    [Fact]
    public void TheBorderKeepsItsOwnEdgeLengths()
    {
        Mesh sheet = Grid(20.0, 1.0, 10, 10);

        // The short sides are chopped into ten segments of 0.1, which is a twentieth of the target.
        Mesh remeshed = MeshRemeshing.Remeshed(sheet, 1.0);

        double[] onTheLeftEdge =
        [
            .. remeshed.Vertices().Where(v => Math.Abs(v.X) < 1e-9).Select(v => v.Y).OrderBy(y => y),
        ];

        Assert.Equal(11, onTheLeftEdge.Length);

        for (int i = 1; i < onTheLeftEdge.Length; i++)
        {
            Assert.Equal(0.1, onTheLeftEdge[i] - onTheLeftEdge[i - 1], 9);
        }
    }

    /// <summary>A target length of zero or less is refused by name.</summary>
    [Fact]
    public void ATargetLengthThatIsNotPositiveIsRefused()
    {
        Mesh grid = Grid(2.0, 2.0, 2, 2);

        foreach (double bad in (double[])[0.0, -1.0, double.NaN, double.PositiveInfinity])
        {
            ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
                () => MeshRemeshing.Remeshed(grid, bad));

            Assert.Equal("targetEdgeLength", refused.ParamName);
        }
    }

    /// <summary>
    /// <b>Quads are triangulated rather than refused</b>, as everywhere else in the mesh layer.
    /// </summary>
    [Fact]
    public void AQuadMeshIsTriangulatedRatherThanRefused()
    {
        Mesh quads = MeshPrimitives.Plane(Plane.WorldXY, 4.0, 4.0, 4, 4);

        Assert.True(quads.QuadCount > 0, "the fixture has no quads, so this test proves nothing.");

        Mesh remeshed = MeshRemeshing.Remeshed(quads, 0.5);

        Assert.Equal(0, remeshed.QuadCount);
        Assert.True(remeshed.FaceCount > 0);
    }

    /// <summary>A triangulated rectangle, deliberately stretched when the sides differ.</summary>
    private static Mesh Grid(double width, double height, int columns, int rows)
    {
        System.Collections.Generic.List<Point3d> vertices = [];
        System.Collections.Generic.List<MeshFace> faces = [];

        for (int y = 0; y <= rows; y++)
        {
            for (int x = 0; x <= columns; x++)
            {
                vertices.Add(new Point3d(width * x / columns, height * y / rows, 0.0));
            }
        }

        int Index(int x, int y) => (y * (columns + 1)) + x;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                faces.Add(new MeshFace(Index(x, y), Index(x + 1, y), Index(x + 1, y + 1)));
                faces.Add(new MeshFace(Index(x, y), Index(x + 1, y + 1), Index(x, y + 1)));
            }
        }

        return new Mesh(vertices, faces);
    }

    private static Mesh Translated(Mesh mesh, double alongX) =>
        mesh.TransformedBy(Transform.Translation(new Vector3d(alongX, 0.0, 0.0)));

    /// <summary>Two meshes side by side as one, written here because the kernel has no append.</summary>
    /// <remarks>
    /// <b>The two sheets do not share a seam and do not need to.</b> What this fixture is for is a
    /// single mesh carrying two very different triangle sizes, which is what the spread metric
    /// reads; welding them would test <c>Mesh.Welded</c> as well and make a failure ambiguous.
    /// </remarks>
    private static Mesh Merged(Mesh first, Mesh second)
    {
        System.Collections.Generic.List<Point3d> vertices = [.. first.Vertices(), .. second.Vertices()];
        System.Collections.Generic.List<MeshFace> faces = [.. first.Faces()];
        int offset = first.VertexCount;

        for (int f = 0; f < second.FaceCount; f++)
        {
            MeshFace face = second.Face(f);
            faces.Add(new MeshFace(face.A + offset, face.B + offset, face.C + offset));
        }

        return new Mesh(vertices, faces);
    }

    /// <summary>The fraction of interior edges within half and double the target length.</summary>
    private static double InBand(Mesh mesh, double target)
    {
        System.Collections.Generic.Dictionary<(int, int), int> uses = [];

        for (int f = 0; f < mesh.FaceCount; f++)
        {
            MeshFace face = mesh.Face(f);

            for (int c = 0; c < 3; c++)
            {
                int a = face[c];
                int b = face[(c + 1) % 3];
                (int, int) k = a < b ? (a, b) : (b, a);
                uses[k] = uses.TryGetValue(k, out int n) ? n + 1 : 1;
            }
        }

        int total = 0;
        int inside = 0;

        foreach (System.Collections.Generic.KeyValuePair<(int, int), int> e in uses)
        {
            if (e.Value < 2)
            {
                continue;
            }

            double length = (mesh.Vertex(e.Key.Item2) - mesh.Vertex(e.Key.Item1)).Length;
            total++;

            if (length >= target / 2.0 && length <= target * 2.0)
            {
                inside++;
            }
        }

        return total == 0 ? 0.0 : (double)inside / total;
    }
}

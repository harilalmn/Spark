using System;
using System.Linq;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// Surface tessellation and its sink — `E2-T26`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The assertions are about the mesh being *right*, not about it existing.</b> Every point of
/// it lies on the surface within tolerance; a closed surface produces a closed mesh; a sphere's
/// area and volume come out near the closed forms; and a tighter tolerance produces a nearer mesh.
/// Any of those failing is a real defect, and none of them can be satisfied by a tessellator that
/// merely emits something.
/// </para>
/// <para>
/// <b>The welding tests are the ones that would have been easiest to leave out.</b> A sphere
/// tessellated without welding its seam and its poles looks perfect from every angle, and is not
/// closed, reports a nonsense volume, and cannot be booleaned. It is only visible by asking the
/// topology.
/// </para>
/// </remarks>
public sealed class TessellationTests
{
    private static readonly Tolerance Coarse = new(0.05, Angle.FromRadians(0.1), 1e-9);
    private static readonly Tolerance Fine = new(0.002, Angle.FromRadians(0.01), 1e-9);

    /// <summary>A plane rectangle tessellates to one quad, because it is already flat.</summary>
    [Fact]
    public void APlaneNeedsNoRefinement()
    {
        Mesh mesh = new PlaneSurface(Plane.WorldXY, new Interval(0, 3), new Interval(0, 4)).ToMesh(Coarse);

        Assert.Equal(1, mesh.FaceCount);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(12.0, mesh.Area, 1e-9);
    }

    /// <summary>
    /// <b>Every vertex of a tessellated sphere is on the sphere.</b> The tessellator samples the
    /// surface, so this is nearly a tautology — and it is the assertion that fails loudly if a
    /// welded index ever points at the wrong vertex.
    /// </summary>
    [Fact]
    public void EveryVertexIsOnTheSurface()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        Mesh mesh = sphere.ToMesh(Coarse);

        foreach (Point3d vertex in mesh.Vertices())
        {
            Assert.Equal(2.0, vertex.DistanceTo(Point3d.Origin), 1e-9);
        }
    }

    /// <summary>
    /// <b>The mesh is within tolerance of the surface, measured where it is furthest: the middle of
    /// a facet.</b> Checking the vertices alone would pass a tessellation with two samples on a
    /// whole sphere.
    /// </summary>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public void TheMeshIsWithinToleranceOfTheSurface(Surface surface)
    {
        Mesh mesh = surface.ToMesh(Coarse);

        for (int index = 0; index < mesh.FaceCount; index++)
        {
            MeshFace face = mesh.Face(index);

            Point3d centre = Centroid(mesh, face);

            surface.ClosestPoint(centre, out double u, out double v);

            Assert.True(
                surface.PointAt(u, v).DistanceTo(centre) <= Coarse.Linear * 2.0,
                $"a facet's middle is {surface.PointAt(u, v).DistanceTo(centre)} from the surface");
        }
    }

    /// <summary>A tighter tolerance produces more faces and a nearer mesh.</summary>
    [Fact]
    public void ATighterToleranceRefinesFurther()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);

        Mesh coarse = sphere.ToMesh(Coarse);
        Mesh fine = sphere.ToMesh(Fine);

        Assert.True(fine.FaceCount > coarse.FaceCount, "a tighter tolerance should refine further");

        double exact = 4.0 * Math.PI * 4.0;

        Assert.True(
            Math.Abs(fine.Area - exact) < Math.Abs(coarse.Area - exact),
            "a tighter tolerance should come nearer the true area");
    }

    /// <summary>
    /// <b>A closed surface produces a closed mesh</b>, which is only true because the seam and the
    /// poles are welded. This is the test that a tessellation which looks perfect can fail.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClosedSurfaces))]
    public void AClosedSurfaceProducesAClosedMesh(Surface surface)
    {
        MeshTopology topology = surface.ToMesh(Coarse).Topology;

        Assert.True(topology.IsManifold, $"{topology.NonManifoldEdgeCount} non-manifold edges");
        Assert.True(topology.IsClosed, $"{topology.NakedEdgeCount} naked edges");
    }

    /// <summary>
    /// A sphere's tessellated volume approaches 4/3πr³ from below, because a facetted sphere is
    /// inscribed in the real one.
    /// </summary>
    [Fact]
    public void ATessellatedSpheresVolumeApproachesTheClosedForm()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);

        double exact = 4.0 / 3.0 * Math.PI * 8.0;
        double volume = sphere.ToMesh(Fine).Volume();

        Assert.True(volume > 0.0, "a sphere tessellated outwards should have a positive volume");
        Assert.True(volume < exact, "an inscribed facetted sphere holds less than the sphere");

        // The bound is the physics rather than a round number: the shell between the polyhedron
        // and the sphere is nowhere thicker than the sag, so its volume is at most the sphere's
        // area times the sag. A fixed percentage would be a number nobody could defend.
        Assert.True(
            exact - volume < sphere.Area * Fine.Linear,
            $"the facetted volume is {exact - volume} short, and the shell can hold at most {sphere.Area * Fine.Linear}");
    }

    /// <summary>
    /// <b>A pole is one vertex, not a row of coincident ones.</b> A row emitted as distinct
    /// vertices gives a ring of zero-area triangles and a hole underneath them.
    /// </summary>
    [Fact]
    public void APoleIsOneVertex()
    {
        Mesh mesh = new SphericalSurface(Plane.WorldXY, 2.0).ToMesh(Coarse);

        int atNorthPole = mesh.Vertices().Count(vertex => vertex.DistanceTo(new Point3d(0, 0, 2)) < 1e-9);

        Assert.Equal(1, atNorthPole);
    }

    /// <summary>
    /// A cell that touches a pole is emitted as a triangle rather than as a quad naming one vertex
    /// twice, which is degenerate and has no normal.
    /// </summary>
    [Fact]
    public void NoFaceNamesTheSameVertexTwice()
    {
        Mesh mesh = new SphericalSurface(Plane.WorldXY, 2.0).ToMesh(Coarse);

        Assert.DoesNotContain(mesh.Faces(), face => face.IsDegenerate);
        Assert.Contains(mesh.Faces(), face => !face.IsQuad);
        Assert.Contains(mesh.Faces(), face => face.IsQuad);
    }

    /// <summary>
    /// <b>A closed direction's seam is welded</b>, so a cylinder's mesh has no vertex duplicated
    /// along its join.
    /// </summary>
    [Fact]
    public void TheSeamIsWelded()
    {
        Mesh mesh = new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 4.0)).ToMesh(Coarse);

        Point3d[] onTheSeam =
        [
            .. mesh.Vertices().Where(vertex => Math.Abs(vertex.Y) < 1e-9 && vertex.X > 0.0),
        ];

        // Two rings' worth of seam samples at the two ends, and no more: one per end, not two.
        Assert.Equal(2, onTheSeam.Length);
    }

    /// <summary>
    /// Normals point outwards on a sphere, which is what the surface's u × v convention says and
    /// what a renderer needs.
    /// </summary>
    [Fact]
    public void TheNormalsPointOutwards()
    {
        SphericalSurface sphere = new(Plane.WorldXY, 2.0);
        Mesh mesh = sphere.ToMesh(Coarse);

        Vector3d[] normals = mesh.Normals()!;

        for (int index = 0; index < mesh.VertexCount; index++)
        {
            Vector3d outwards = (mesh.Vertex(index) - Point3d.Origin).Normalised();

            Assert.True(
                normals[index].Dot(outwards) > 0.9,
                $"vertex {index}'s normal points {normals[index]} where outwards is {outwards}");
        }
    }

    /// <summary>Texture coordinates run over the unit square, in the surface's own directions.</summary>
    [Fact]
    public void TextureCoordinatesCoverTheUnitSquare()
    {
        Mesh mesh = new PlaneSurface(Plane.WorldXY, new Interval(-1, 2), new Interval(3, 7)).ToMesh(Coarse);
        UV[] uvs = mesh.TextureCoordinates()!;

        Assert.Equal(0.0, uvs.Min(uv => uv.U), 1e-12);
        Assert.Equal(1.0, uvs.Max(uv => uv.U), 1e-12);
        Assert.Equal(0.0, uvs.Min(uv => uv.V), 1e-12);
        Assert.Equal(1.0, uvs.Max(uv => uv.V), 1e-12);
    }

    /// <summary>
    /// <b>The sample cap holds.</b> A tolerance far below the surface's size would otherwise ask
    /// for an unbounded grid, and a viewport that hangs is worse than a facet that is a micron out.
    /// </summary>
    [Fact]
    public void TheSampleCapHolds()
    {
        Tolerance absurd = new(1e-12, Angle.FromRadians(1e-9), 1e-15);

        Mesh mesh = new SphericalSurface(Plane.WorldXY, 1000.0).ToMesh(absurd);

        Assert.True(
            mesh.FaceCount <= Tessellation.MaximumSamplesPerDirection * Tessellation.MaximumSamplesPerDirection,
            $"{mesh.FaceCount} faces is past the cap");
    }

    /// <summary>
    /// <b>Sag is probed at several parameters in the other direction.</b> A cone measured along one
    /// v samples either the narrow end or the wide one, and under-refines the other; the test is
    /// that the wide end is inside tolerance too.
    /// </summary>
    [Fact]
    public void ATaperIsRefinedAtItsWideEnd()
    {
        ConicalSurface cone = new(
            Plane.WorldXY, 0.1, Angle.FromRadians(Math.Atan(2.0)), new Interval(0.0, 4.0));

        Mesh mesh = cone.ToMesh(Coarse);

        // The widest ring is the last one; its facets must be inside tolerance like the rest.
        foreach (MeshFace face in mesh.Faces())
        {
            Point3d centre = Centroid(mesh, face);

            if (centre.Z < 3.0)
            {
                continue;
            }

            cone.ClosestPoint(centre, out double u, out double v);

            Assert.True(
                cone.PointAt(u, v).DistanceTo(centre) <= Coarse.Linear * 2.0,
                "the wide end of a cone should be refined as much as the narrow end");
        }
    }

    /// <summary>A sink can be given several surfaces and produces one mesh.</summary>
    [Fact]
    public void OneSinkTakesSeveralSurfaces()
    {
        MeshBuilder builder = new();

        Tessellation.Tessellate(new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit), builder, Coarse);
        Tessellation.Tessellate(new PlaneSurface(Plane.WorldXZ, Interval.Unit, Interval.Unit), builder, Coarse);

        Mesh mesh = builder.Build();

        Assert.Equal(2, mesh.FaceCount);
        Assert.Equal(8, mesh.VertexCount);
    }

    /// <summary>One of each surface, for the checks that apply to all of them.</summary>
    public static TheoryData<Surface> Surfaces() =>
    [
        new PlaneSurface(Plane.WorldXY, new Interval(0, 2), new Interval(0, 3)),
        new SphericalSurface(Plane.WorldXY, 2.0),
        new CylindricalSurface(Plane.WorldXY, 2.0, new Interval(0.0, 5.0)),
        new ConicalSurface(Plane.WorldXY, 1.0, Angle.FromRadians(0.3), new Interval(0.0, 4.0)),
        new ToroidalSurface(Plane.WorldXY, 5.0, 2.0),
    ];

    /// <summary>The surfaces that close, whose meshes must close too.</summary>
    public static TheoryData<Surface> ClosedSurfaces() =>
    [
        new SphericalSurface(Plane.WorldXY, 2.0),
        new ToroidalSurface(Plane.WorldXY, 5.0, 2.0),
    ];

    private static Point3d Centroid(Mesh mesh, in MeshFace face)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;

        for (int corner = 0; corner < face.Count; corner++)
        {
            Point3d point = mesh.Vertex(face[corner]);

            x += point.X;
            y += point.Y;
            z += point.Z;
        }

        return new Point3d(x / face.Count, y / face.Count, z / face.Count);
    }
}

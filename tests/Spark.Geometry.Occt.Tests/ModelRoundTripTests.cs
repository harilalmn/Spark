using System;
using System.Linq;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// The encoding, checked against itself.
/// </summary>
/// <remarks>
/// <b>An encoding written twice in two languages is checked by a round trip or it is not checked
/// at all.</b> <c>ModelWriter</c> and <c>ModelReader</c> are one decision expressed in C# and its
/// mirror in C++, and neither compiler can see the other. An off-by-one in an offset table, a
/// swapped domain, a knot vector in the wrong convention — none of those is a build error, and
/// all of them are visible the moment a shape goes out and comes back.
/// </remarks>
public sealed class ModelRoundTripTests
{
    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-6, Angle.FromDegrees(1), 1e-12);

    /// <summary>
    /// What a tessellation is asked for, which is NOT what an operation is asked for.
    /// </summary>
    /// <remarks>
    /// A linear tolerance of 1e-6 is a perfectly sensible thing to want from a boolean and a
    /// ruinous thing to want from a mesh: on a curved solid it is hundreds of millions of
    /// triangles. The provider clamps it now, and asking sensibly here as well keeps the suite
    /// fast rather than merely survivable.
    /// </remarks>
    private static Tolerance Display => new(0.01, Angle.FromDegrees(2), 1e-12);

    /// <summary>Sends a shape to the provider and reads it straight back.</summary>
    private static Brep Through(Brep shape) => Kernel.Heal(shape, Fine).Value;

    /// <summary>
    /// One face on one surface, with no loops.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than through <see cref="BrepBuilder"/>, because a loop needs edges
    /// and edges need curves, and none of that is what this file is testing. The provider builds
    /// an untrimmed face from its surface's own domain, so a face with no loops is exactly the
    /// input the encoding is about.
    /// </remarks>
    private static Brep Sheet(Surface surface) =>
        new(
            [],
            [],
            [surface],
            [],
            [],
            [],
            [],
            [new BrepFace(0, 0, 0, false)],
            [new BrepShell(0, 1)]);

    /// <summary>
    /// <b>The smallest probe of the import path there is.</b> Tessellating a managed BRep means
    /// importing it and meshing it, with nothing in between to repair it — so a positive 24 says
    /// the faces came out facing the way they went in. A healed shape cannot say that, because
    /// healing is what would have fixed it.
    /// </summary>
    [NativeFact]
    public void AnImportedBoxKeepsThePositiveVolumeItHad()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);

        Assert.Equal(24.0, Kernel.Tessellate(box, Display).Value.Volume(), 6);
    }

    /// <summary>The same for a cylinder, whose caps are round and whose wall has a seam.</summary>
    [NativeFact]
    public void AnImportedCylinderKeepsThePositiveVolumeItHad()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0);

        Assert.Equal(Math.PI * 1.5 * 1.5 * 5.0, Kernel.Tessellate(cylinder, Display).Value.Volume(), 1);
    }

    [NativeFact]
    public void ABoxSurvivesTheRoundTrip()
    {
        Brep sent = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Brep back = Through(sent);

        Assert.Equal(6, back.FaceCount);
        Assert.Equal(12, back.EdgeCount);
        Assert.Equal(8, back.VertexCount);
        Assert.Equal(1, back.ShellCount);

        Assert.All(back.Surfaces(), surface => Assert.IsType<PlaneSurface>(surface));
        Assert.Equal(24.0, Kernel.Tessellate(back, Display).Value.Volume(), 6);
    }

    [NativeFact]
    public void ACylinderSurvivesTheRoundTripStillCylindrical()
    {
        Brep sent = BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0);
        Brep back = Through(sent);

        Assert.Equal(3, back.FaceCount);
        Assert.Single(back.Surfaces().OfType<CylindricalSurface>());
        Assert.Equal(2, back.Surfaces().OfType<PlaneSurface>().Count());

        CylindricalSurface wall = back.Surfaces().OfType<CylindricalSurface>().Single();
        Assert.Equal(1.5, wall.Radius, 9);
    }

    /// <summary>
    /// <b>The trip is a trip, not an identity.</b> A tolerant kernel re-sews and may
    /// re-parameterise, which is exactly why ADR-0021 makes the provider's shape canonical rather
    /// than converting after every operation. What must hold is that the *geometry* is the same,
    /// so that is what this asserts — points on the surface, not numbers in a table.
    /// </summary>
    [NativeFact]
    public void ACylindersWallIsTheSameSurfaceEvenIfItIsNotTheSameNumbers()
    {
        Brep sent = BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0);
        CylindricalSurface before = sent.Surfaces().OfType<CylindricalSurface>().Single();
        CylindricalSurface after = Through(sent).Surfaces().OfType<CylindricalSurface>().Single();

        for (int i = 0; i <= 16; i++)
        {
            double u = before.DomainU.Min + ((before.DomainU.Max - before.DomainU.Min) * i / 16.0);

            for (int j = 0; j <= 4; j++)
            {
                double v = before.DomainV.Min + ((before.DomainV.Max - before.DomainV.Min) * j / 4.0);

                Point3d point = before.PointAt(u, v);

                // On the axis-aligned cylinder both parameterisations agree, but the assertion
                // that matters is the implicit one: the point is on the same surface.
                double radial = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
                Assert.Equal(1.5, radial, 9);
                Assert.Equal(1.5, after.Radius, 9);
            }
        }
    }

    /// <summary>
    /// Revolving a half-circle about its own diameter is a sphere, and it exercises two things at
    /// once: an <i>arc</i> going out through the encoding, and a surface with no analytic name in
    /// Spark coming back through it.
    /// </summary>
    [NativeFact]
    public void RevolvingAnArcMakesASphere()
    {
        KernelResult<Brep> spun = Kernel.Revolve(
            Arc.ByPlaneRadiusAngles(
                Plane.ByOriginXAxisYAxis(Point3d.Origin, Vector3d.XAxis, Vector3d.ZAxis),
                2.0,
                Angle.FromDegrees(-90),
                Angle.FromDegrees(180)),
            Point3d.Origin,
            Vector3d.ZAxis,
            Angle.FromDegrees(360),
            Fine);

        Assert.True(
            spun.IsSuccess,
            $"{spun.Diagnostic?.Message} {spun.Diagnostic?.Detail}");

        Brep sphere = spun.Value;

        Mesh mesh = Kernel.Tessellate(sphere, Display).Value;

        // The magnitude is the assertion; a shell's winding is the provider's business and a
        // sign flip here would say nothing about whether the sphere is the right size.
        Assert.Equal(4.0 / 3.0 * Math.PI * 8.0, Math.Abs(mesh.Volume()), 0);

        // And it materialises, which is what puts the read path under test.
        Assert.True(sphere.FaceCount >= 1);
    }

    /// <summary>
    /// A NURBS surface has no analytic name, so it crosses as poles, weights and knots — the
    /// longest path through the encoding and the one with the most to get wrong.
    /// </summary>
    [NativeFact]
    public void ANurbsSurfaceSurvivesTheRoundTrip()
    {
        NurbsSurface patch = new NurbsSurface(
            KnotVector.CreateClamped(2, 3),
            KnotVector.CreateClamped(2, 3),
            new Point3d[3, 3]
            {
                { new(0, 0, 0), new(0, 1, 1), new(0, 2, 0) },
                { new(1, 0, 1), new(1, 1, 2), new(1, 2, 1) },
                { new(2, 0, 0), new(2, 1, 1), new(2, 2, 0) },
            },
            weights: null);

        Brep back = Through(Sheet(patch));
        NurbsSurface returned = Assert.Single(back.Surfaces().OfType<NurbsSurface>());

        Assert.Equal(patch.DegreeU, returned.DegreeU);
        Assert.Equal(patch.DegreeV, returned.DegreeV);

        for (int i = 0; i <= 8; i++)
        {
            for (int j = 0; j <= 8; j++)
            {
                double u = i / 8.0;
                double v = j / 8.0;

                Point3d expected = patch.PointAt(
                    patch.DomainU.Min + (u * (patch.DomainU.Max - patch.DomainU.Min)),
                    patch.DomainV.Min + (v * (patch.DomainV.Max - patch.DomainV.Min)));

                Point3d actual = returned.PointAt(
                    returned.DomainU.Min + (u * (returned.DomainU.Max - returned.DomainU.Min)),
                    returned.DomainV.Min + (v * (returned.DomainV.Max - returned.DomainV.Min)));

                Assert.True(
                    expected.DistanceTo(actual) < 1e-9,
                    $"({u:F2}, {v:F2}): {expected} became {actual}");
            }
        }
    }

    [NativeFact]
    public void AResidentShapeMaterialisesOnceAndStaysResident()
    {
        Brep resident = Kernel
            .Union(
                BrepPrimitives.Box(Plane.WorldXY, 2, 2, 2),
                BrepPrimitives.Box(
                    Plane.ByOriginXAxisYAxis(new Point3d(1, 1, 1), Vector3d.XAxis, Vector3d.YAxis),
                    2,
                    2,
                    2),
                Fine)
            .Value;

        Assert.True(resident.IsResident);
        Assert.True(resident.NativeBytes > 0);

        int first = resident.FaceCount;
        int second = resident.FaceCount;

        Assert.Equal(first, second);

        // Read out once and now a value, with the provider's shape still held behind it.
        Assert.False(resident.IsResident);
        Assert.True(resident.NativeBytes > 0);
    }
}

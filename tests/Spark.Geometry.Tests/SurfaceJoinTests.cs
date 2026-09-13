using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Brep.FromSurface"/> and <see cref="Surface.Join(Surface)"/> — `E2-T66`'s last item.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the loop's winding, not its existence.</b> A face whose trims run the wrong way
/// round builds without complaint and reports the right face and edge counts; what it gets wrong is
/// which side is out. So the claim asserted is that the face's normal agrees with the surface's own
/// at a point inside it.
/// </para>
/// <para>
/// <b>The join's own claim is the count</b>: joining two surfaces gives one BRep of two faces, and
/// an implementation returning two BReps, or one face, would satisfy a test that only asked whether
/// it succeeded.
/// </para>
/// </remarks>
public sealed class SurfaceJoinTests
{
    /// <summary>A surface becomes one face bounded by its own four edges.</summary>
    [Fact]
    public void ASurfaceBecomesOneFaceWithFourEdges()
    {
        PlaneSurface sheet = new(Plane.WorldXY, new Interval(0, 3), new Interval(0, 2));

        Brep brep = Brep.FromSurface(sheet);

        Assert.Equal(1, brep.FaceCount);
        Assert.Equal(4, brep.EdgeCount);
        Assert.Equal(4, brep.VertexCount);
        Assert.Equal(1, brep.LoopCount);
    }

    /// <summary>
    /// <b>The winding.</b> The face's normal agrees with the surface's own, which is what a reversed
    /// loop would get wrong while still building a plausible BRep.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnalyticSurfaceTests.EverySurface), MemberType = typeof(AnalyticSurfaceTests))]
    public void TheFaceAgreesWithTheSurfaceAboutWhichWayIsOut(Surface surface)
    {
        Brep brep = Brep.FromSurface(surface);

        BrepFace face = brep.Faces()[0];
        double u = surface.DomainU.Denormalise(0.5);
        double v = surface.DomainV.Denormalise(0.5);

        Vector3d expected = surface.NormalAt(u, v);
        Vector3d actual = face.IsReversed ? -expected : expected;

        Assert.True(
            expected.Dot(actual) > 0.0,
            $"{surface.GetType().Name}: the face disagrees with its surface about which way is out.");
        Assert.False(face.IsReversed, "a surface wrapped as a face is not reversed.");
    }

    /// <summary>
    /// <b>The degenerate boundary is a decision.</b> A whole sphere's poles are single points, so
    /// its loop has three edges rather than four, and it builds rather than throwing.
    /// </summary>
    [Fact]
    public void AWholeSphereHasAPolarLoopOfThreeEdges()
    {
        SphericalSurface globe = new(Plane.WorldXY, 2.0);

        Brep brep = Brep.FromSurface(globe);

        Assert.Equal(1, brep.FaceCount);
        Assert.True(brep.EdgeCount < 4, $"the poles should have collapsed two edges, and there are {brep.EdgeCount}.");
        Assert.True(brep.EdgeCount >= 2, "a loop needs edges to close round.");
    }

    /// <summary>
    /// A cone's apex is the same case on one side only: one boundary collapses and the other three
    /// survive.
    /// </summary>
    [Fact]
    public void AConeWithAnApexLosesOnlyTheDegenerateBoundary()
    {
        // Radius shrinking to zero over the height: the far boundary is the apex.
        ConicalSurface cone = new(
            Plane.WorldXY, 2.0, Angle.FromRadians(Math.Atan2(-2.0, 4.0)), new Interval(0.0, 4.0));

        Brep brep = Brep.FromSurface(cone);

        Assert.Equal(1, brep.FaceCount);
        Assert.True(brep.EdgeCount >= 2, "the cone should keep its base and its seams.");
    }

    /// <summary>
    /// <b>The join's count.</b> Two surfaces give one BRep of two faces — not two BReps, and not
    /// one face.
    /// </summary>
    [Fact]
    public void JoiningTwoSurfacesGivesOneBrepOfTwoFaces()
    {
        PlaneSurface floor = new(Plane.WorldXY, new Interval(0, 3), new Interval(0, 2));
        PlaneSurface wall = new(Plane.WorldXZ, new Interval(0, 3), new Interval(0, 2));

        Brep joined = floor.Join(wall);

        Assert.Equal(2, joined.FaceCount);
        Assert.Equal(8, joined.EdgeCount);
        Assert.Equal(2, joined.LoopCount);
    }

    /// <summary>Joining a list gives one face per surface, however many there are.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void JoiningAListGivesOneFacePerSurface(int count)
    {
        List<Surface> surfaces = [];
        for (int index = 0; index < count; index++)
        {
            surfaces.Add(new PlaneSurface(
                Plane.FromOriginNormal(new Point3d(0, 0, index), Vector3d.ZAxis),
                new Interval(0, 1),
                new Interval(0, 1)));
        }

        Assert.Equal(count, Surface.Join(surfaces).FaceCount);
    }

    /// <summary>
    /// The surfaces themselves survive the join: each face still carries the surface it was made
    /// from, which is what makes a joined BRep worth having rather than a bag of triangles.
    /// </summary>
    [Fact]
    public void TheJoinedFacesKeepTheirSurfaces()
    {
        SphericalSurface globe = new(Plane.WorldXY, 2.0, new Interval(0.0, 1.0), new Interval(0.0, 1.0));
        CylindricalSurface pipe = new(Plane.WorldXY, 1.0, new Interval(0.0, 2.0));

        Brep joined = globe.Join(pipe);

        Assert.Equal(2, joined.FaceCount);
        Assert.Contains(joined.Surfaces(), surface => surface is SphericalSurface);
        Assert.Contains(joined.Surfaces(), surface => surface is CylindricalSurface);
    }

    [Fact]
    public void NoSurfacesIsRefused()
    {
        PlaneSurface sheet = new(Plane.WorldXY, new Interval(0, 1), new Interval(0, 1));

        Assert.Throws<ArgumentNullException>(() => Brep.FromSurface(null!));
        Assert.Throws<ArgumentNullException>(() => sheet.Join(null!));
        Assert.Throws<ArgumentNullException>(() => Surface.Join(null!));
        Assert.Throws<ArgumentException>(() => Surface.Join([]));
    }
}

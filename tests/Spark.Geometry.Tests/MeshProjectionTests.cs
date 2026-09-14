using System;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh.Project"/> — `E2-T69`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the rejection of hits behind the start.</b> A ray fired at a closed mesh meets
/// it twice, so an implementation that ignores the sign of the ray parameter — or takes the
/// smallest <i>magnitude</i> rather than the smallest non-negative value — lands on the far side,
/// or behind the caller, and both are perfectly good-looking surface points. The fixtures that
/// catch it aim <b>away</b> from the mesh and start <b>inside</b> it.
/// </para>
/// <para>
/// <b>A miss is <see langword="null"/>, and that is asserted rather than assumed</b>, because every
/// other answer a member of this shape could give — the query point, the nearest point, a sentinel
/// — is a wrong answer that looks like a right one.
/// </para>
/// </remarks>
public sealed class MeshProjectionTests
{
    /// <summary>A ray through a face's interior lands where arithmetic says.</summary>
    [Fact]
    public void ARayThroughAFaceLandsOnIt()
    {
        Mesh triangle = Triangle();

        Point3d? hit = triangle.Project(new Point3d(1, 1, 7), -Vector3d.ZAxis);

        Assert.NotNull(hit);
        Assert.True(
            hit!.Value.DistanceTo(new Point3d(1, 1, 0)) < 1e-12,
            $"the ray landed at {hit}, not at (1, 1, 0).");
    }

    /// <summary>A ray that passes beside the triangle misses, and says so.</summary>
    [Theory]
    [InlineData(5.0, 5.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(3.5, 3.5)]
    public void ARayBesideTheTriangleMisses(double x, double y)
    {
        Mesh triangle = Triangle();

        Assert.Null(triangle.Project(new Point3d(x, y, 7), -Vector3d.ZAxis));
    }

    /// <summary>
    /// <b>The branch.</b> The triangle is behind the start, so there is no hit — a projection along
    /// a direction means forwards.
    /// </summary>
    [Fact]
    public void AFaceBehindTheStartIsNotAHit()
    {
        Mesh triangle = Triangle();

        // Standing above the triangle and looking up: the triangle is behind.
        Assert.Null(triangle.Project(new Point3d(1, 1, 7), Vector3d.ZAxis));
    }

    /// <summary>
    /// <b>The branch, where it actually bites.</b> Inside a closed mesh the ray meets the surface
    /// twice — once in front and once behind — and the answer is the one in front.
    /// </summary>
    [Fact]
    public void FromInsideAClosedMeshTheHitIsTheOneInFront()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 5.0, 64, 32);

        Point3d? hit = sphere.Project(Point3d.Origin, Vector3d.XAxis);

        Assert.NotNull(hit);
        Assert.True(
            hit!.Value.X > 0.0,
            $"the ray started at the centre travelling towards +x and landed at {hit}, behind it.");
        Assert.True(
            Math.Abs(hit.Value.DistanceTo(Point3d.Origin) - 5.0) < 0.02,
            $"the hit is {hit.Value.DistanceTo(Point3d.Origin)} from the centre, not 5.");
    }

    /// <summary>
    /// And from outside, the hit is the <b>near</b> side — which is the same rule producing the
    /// opposite-looking answer.
    /// </summary>
    [Fact]
    public void FromOutsideAClosedMeshTheHitIsTheNearSide()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 5.0, 64, 32);

        Point3d? hit = sphere.Project(new Point3d(20, 0, 0), -Vector3d.XAxis);

        Assert.NotNull(hit);
        Assert.True(
            hit!.Value.X > 0.0,
            $"the ray came from +x and landed at {hit}, which is the far side.");
    }

    /// <summary>The nearest of several faces in the ray's path is the one hit.</summary>
    [Fact]
    public void TheNearestFaceInThePathWins()
    {
        Mesh stack = new(
            [
                new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0),
                new Point3d(0, 0, 3), new Point3d(4, 0, 3), new Point3d(0, 4, 3),
                new Point3d(0, 0, 6), new Point3d(4, 0, 6), new Point3d(0, 4, 6),
            ],
            [new MeshFace(0, 1, 2), new MeshFace(3, 4, 5), new MeshFace(6, 7, 8)],
            null,
            null,
            null);

        Point3d? hit = stack.Project(new Point3d(1, 1, 10), -Vector3d.ZAxis);

        Assert.NotNull(hit);
        Assert.True(
            Math.Abs(hit!.Value.Z - 6.0) < 1e-12,
            $"the ray landed at z = {hit.Value.Z}; the nearest face in its path is at z = 6.");
    }

    /// <summary>A ray in the triangle's own plane grazes it, and a graze is not a hit.</summary>
    [Fact]
    public void ARayLyingInTheFacesPlaneIsNotAHit()
    {
        Mesh triangle = Triangle();

        // Travelling along the x axis at z = 0, straight through the triangle's plane.
        Assert.Null(triangle.Project(new Point3d(-5, 1, 0), Vector3d.XAxis));
    }

    /// <summary>The direction need not be unit length, and its length does not move the answer.</summary>
    [Fact]
    public void TheDirectionsLengthDoesNotMatter()
    {
        Mesh triangle = Triangle();

        Point3d? once = triangle.Project(new Point3d(1, 1, 7), new Vector3d(0, 0, -1));
        Point3d? twenty = triangle.Project(new Point3d(1, 1, 7), new Vector3d(0, 0, -20));

        Assert.NotNull(once);
        Assert.NotNull(twenty);
        Assert.True(once!.Value.DistanceTo(twenty!.Value) < 1e-12);
    }

    /// <summary>A quad is hit as its two triangles, across both of them.</summary>
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(3.0, 3.0)]
    [InlineData(3.0, 1.0)]
    [InlineData(1.0, 3.0)]
    public void AQuadIsHitAcrossBothOfItsTriangles(double x, double y)
    {
        Mesh quad = new(
            [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(4, 4, 0), new Point3d(0, 4, 0)],
            [new MeshFace(0, 1, 2, 3)],
            null,
            null,
            null);

        Point3d? hit = quad.Project(new Point3d(x, y, 5), -Vector3d.ZAxis);

        Assert.NotNull(hit);
        Assert.True(hit!.Value.DistanceTo(new Point3d(x, y, 0)) < 1e-12);
    }

    /// <summary>An oblique ray, so the answer is not protected by an axis.</summary>
    [Fact]
    public void AnObliqueRayLandsWhereArithmeticSays()
    {
        Mesh triangle = Triangle();

        // From (0, 1, 3) travelling (1, 0, -1): it reaches z = 0 after three steps, at x = 3.
        Point3d? hit = triangle.Project(new Point3d(0, 1, 3), new Vector3d(1, 0, -1));

        Assert.NotNull(hit);
        Assert.True(
            hit!.Value.DistanceTo(new Point3d(3, 1, 0)) < 1e-12,
            $"the ray landed at {hit}, not at (3, 1, 0).");
    }

    [Fact]
    public void ItsDegeneraciesAreRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Triangle().Project(Point3d.Origin, Vector3d.Zero));

        Mesh empty = new([new Point3d(0, 0, 0)], [], null, null, null);

        Assert.Throws<InvalidOperationException>(() => empty.Project(Point3d.Origin, Vector3d.ZAxis));
    }

    /// <summary>The right triangle the hand-computed answers are measured against.</summary>
    /// <returns>A single triangle with corners at the origin, (4, 0, 0) and (0, 4, 0).</returns>
    private static Mesh Triangle() =>
        new(
            [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0)],
            [new MeshFace(0, 1, 2)],
            null,
            null,
            null);
}

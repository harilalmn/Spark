using System;
using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Tests;

/// <summary>
/// <see cref="Mesh.ClosestPoint"/> — `E2-T69`.
/// </summary>
/// <remarks>
/// <para>
/// <b>The branch is the edge and vertex regions.</b> A point above the interior of a face is
/// answered correctly by a plain projection onto the face's plane, so that case proves nothing. A
/// triangle divides space into seven regions, and the four that are not the face are where a naive
/// implementation is wrong — and they are the common case, because a point outside a mesh is
/// usually nearest an edge or a corner.
/// </para>
/// <para>
/// <b>Every expected answer here is computed by hand</b>, from geometry chosen so that it can be:
/// a right triangle in the world XY plane, and a query point placed beyond a named feature of it.
/// </para>
/// </remarks>
public sealed class MeshClosestPointTests
{
    /// <summary>A point over the face's interior projects onto it. Not the branch.</summary>
    [Fact]
    public void APointOverTheInteriorProjectsOntoTheFace()
    {
        Mesh triangle = Triangle();

        Point3d nearest = triangle.ClosestPoint(new Point3d(1, 1, 7));

        Assert.True(
            nearest.DistanceTo(new Point3d(1, 1, 0)) < 1e-12,
            $"the nearest point is {nearest}, not the projection (1, 1, 0).");
    }

    /// <summary>
    /// <b>The branch.</b> Beyond an edge, the answer is a point <i>on that edge</i> — which a plane
    /// projection gets wrong, because the projection falls outside the triangle.
    /// </summary>
    [Fact]
    public void BeyondAnEdgeTheAnswerIsOnThatEdge()
    {
        // The triangle is (0,0,0), (4,0,0), (0,4,0). The hypotenuse runs from (4,0) to (0,4).
        // The point (4, 4, 0) projects onto the plane at itself, which is outside the triangle;
        // the nearest point on the hypotenuse is its midpoint, (2, 2, 0).
        Mesh triangle = Triangle();

        Point3d nearest = triangle.ClosestPoint(new Point3d(4, 4, 0));

        Assert.True(
            nearest.DistanceTo(new Point3d(2, 2, 0)) < 1e-12,
            $"the nearest point is {nearest}, not the hypotenuse's midpoint (2, 2, 0).");
    }

    /// <summary>And the same beyond an edge while off the plane, so height does not rescue it.</summary>
    [Fact]
    public void BeyondAnEdgeAndOffThePlaneToo()
    {
        Mesh triangle = Triangle();

        Point3d nearest = triangle.ClosestPoint(new Point3d(4, 4, 5));

        Assert.True(
            nearest.DistanceTo(new Point3d(2, 2, 0)) < 1e-12,
            $"the nearest point is {nearest}, not (2, 2, 0).");
    }

    /// <summary>
    /// <b>The branch, second half.</b> Beyond a vertex, the answer is that vertex — not a point on
    /// either edge meeting it, and not a projection.
    /// </summary>
    /// <param name="x">The query point's x.</param>
    /// <param name="y">Its y.</param>
    /// <param name="z">Its z.</param>
    /// <param name="cornerX">The expected corner's x.</param>
    /// <param name="cornerY">Its y.</param>
    [Theory]
    [InlineData(-3.0, -3.0, 0.0, 0.0, 0.0)]
    [InlineData(-3.0, -3.0, 6.0, 0.0, 0.0)]
    [InlineData(9.0, -2.0, 0.0, 4.0, 0.0)]
    [InlineData(-2.0, 9.0, 0.0, 0.0, 4.0)]
    public void BeyondAVertexTheAnswerIsThatVertex(
        double x, double y, double z, double cornerX, double cornerY)
    {
        Mesh triangle = Triangle();

        Point3d nearest = triangle.ClosestPoint(new Point3d(x, y, z));
        Point3d corner = new(cornerX, cornerY, 0.0);

        Assert.True(
            nearest.DistanceTo(corner) < 1e-12,
            $"the nearest point is {nearest}, not the corner {corner}.");
    }

    /// <summary>A point already on the mesh is its own nearest point.</summary>
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(3.5, 0.0)]
    public void APointOnTheMeshIsItsOwnNearestPoint(double x, double y)
    {
        Mesh triangle = Triangle();
        Point3d on = new(x, y, 0.0);

        Assert.True(
            triangle.ClosestPoint(on).DistanceTo(on) < 1e-12,
            $"a point on the mesh moved to {triangle.ClosestPoint(on)}.");
    }

    /// <summary>
    /// On a closed mesh the answer is on the surface at the right distance — sampled all round, so
    /// that no one face is being flattered.
    /// </summary>
    [Fact]
    public void OnASphereTheAnswerIsOnTheSurface()
    {
        const double Radius = 5.0;
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, Radius, 48, 24);

        for (int index = 0; index < 40; index++)
        {
            double angle = 2.0 * Math.PI * index / 40.0;
            double height = -2.0 + (index * 0.1);
            Point3d outside = new(
                12.0 * Math.Cos(angle), 12.0 * Math.Sin(angle), height);

            Point3d nearest = sphere.ClosestPoint(outside);

            // The tessellation sits just inside the true sphere, so the answer is at the radius to
            // within the sagitta of one facet rather than exactly.
            Assert.True(
                Math.Abs(nearest.DistanceTo(Point3d.Origin) - Radius) < 0.02,
                $"the nearest point is {nearest.DistanceTo(Point3d.Origin)} from the centre, not {Radius}.");
        }
    }

    /// <summary>
    /// The nearest point on a mesh is generally <b>not</b> a vertex, which is the whole reason a
    /// k-d tree over the vertices does not answer this question.
    /// </summary>
    [Fact]
    public void TheAnswerIsGenerallyNotAVertex()
    {
        Mesh sphere = MeshPrimitives.Sphere(Plane.WorldXY, 5.0, 16, 8);
        Point3d outside = new(11.3, 2.7, 1.9);

        Point3d nearest = sphere.ClosestPoint(outside);

        double toNearestVertex = double.MaxValue;
        foreach (Point3d vertex in sphere.Vertices())
        {
            toNearestVertex = Math.Min(toNearestVertex, vertex.DistanceTo(outside));
        }

        Assert.True(
            nearest.DistanceTo(outside) < toNearestVertex - 1e-6,
            "the nearest point on the surface was no closer than the nearest vertex, so this "
            + "fixture does not distinguish the two.");
    }

    /// <summary>A quad is answered as its two triangles.</summary>
    [Fact]
    public void AQuadIsAnsweredAsItsTwoTriangles()
    {
        Mesh quad = new(
            [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(4, 4, 0), new Point3d(0, 4, 0)],
            [new MeshFace(0, 1, 2, 3)],
            null,
            null,
            null);

        Assert.True(quad.ClosestPoint(new Point3d(2, 2, 9)).DistanceTo(new Point3d(2, 2, 0)) < 1e-12);
        Assert.True(quad.ClosestPoint(new Point3d(9, 2, 0)).DistanceTo(new Point3d(4, 2, 0)) < 1e-12);
        Assert.True(quad.ClosestPoint(new Point3d(-3, -3, 0)).DistanceTo(Point3d.Origin) < 1e-12);
    }

    /// <summary>The nearest face wins, not the first one that is close.</summary>
    [Fact]
    public void TheNearestFaceWins()
    {
        // Two triangles, one at z = 0 and one at z = 10. A point at z = 9 belongs to the second.
        Mesh pair = new(
            [
                new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0),
                new Point3d(0, 0, 10), new Point3d(4, 0, 10), new Point3d(0, 4, 10),
            ],
            [new MeshFace(0, 1, 2), new MeshFace(3, 4, 5)],
            null,
            null,
            null);

        Point3d nearest = pair.ClosestPoint(new Point3d(1, 1, 9));

        Assert.True(
            nearest.DistanceTo(new Point3d(1, 1, 10)) < 1e-12,
            $"the nearest point is {nearest}, which is on the wrong triangle.");
    }

    /// <summary>A mesh with no faces has no surface to be near.</summary>
    [Fact]
    public void AMeshWithNoFacesIsRefused()
    {
        Mesh empty = new([new Point3d(0, 0, 0)], [], null, null, null);

        Assert.Throws<InvalidOperationException>(() => empty.ClosestPoint(Point3d.Origin));
    }

    /// <summary>The right triangle every hand-computed answer above is measured against.</summary>
    /// <returns>A single triangle with corners at the origin, (4, 0, 0) and (0, 4, 0).</returns>
    private static Mesh Triangle() =>
        new(
            [new Point3d(0, 0, 0), new Point3d(4, 0, 0), new Point3d(0, 4, 0)],
            [new MeshFace(0, 1, 2)],
            null,
            null,
            null);
}

using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// The solids a BRep can be built from surfaces this kernel already has.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are constructions, not operations, and the difference is where the seam is.</b> A box
/// is six planes and twelve edges written down; a *boolean* of two boxes is exact solid modelling
/// and lives behind <c>IBrepKernel</c> (`E2-T28`). Building the primitives here is what gives the
/// kernel seam something to be tested against, gives the viewport something to draw, and gives a
/// user something to subtract — and it needs no exact intersection at all, which is why it can
/// exist before the provider does.
/// </para>
/// <para>
/// <b>Every one of them is a closed, consistently-oriented solid</b>, so
/// <see cref="Brep.IsSolid"/> is true and every face's normal points out. That is asserted rather
/// than assumed, because a primitive whose normals point in is the single most confusing thing a
/// modelling kernel can hand somebody.
/// </para>
/// </remarks>
public static class BrepPrimitives
{
    /// <summary>A rectangular box, axis-aligned to a plane.</summary>
    /// <param name="plane">The frame: the box's minimum corner is at its origin.</param>
    /// <param name="length">Its extent along the plane's x-axis.</param>
    /// <param name="width">Its extent along the plane's y-axis.</param>
    /// <param name="height">Its extent along the plane's normal.</param>
    /// <returns>A closed solid of six planar faces.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A side is not finite and positive.</exception>
    /// <remarks>
    /// <b>Twelve edges, each shared by exactly two faces.</b> Writing six independent quads would
    /// be far shorter and would produce a model with 24 edges, no shared topology and
    /// <see cref="Brep.IsSolid"/> false — which is the difference between a BRep and a mesh with
    /// extra ceremony.
    /// </remarks>
    public static Brep Box(in Plane plane, double length = 1, double width = 1, double height = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        BrepBuilder builder = new();

        // The eight corners, bottom face first, anticlockwise seen from above.
        int[] corner = new int[8];

        for (int index = 0; index < 8; index++)
        {
            double x = (index & 1) == 0 ? 0.0 : length;
            double y = (index & 2) == 0 ? 0.0 : width;
            double z = (index & 4) == 0 ? 0.0 : height;

            // The bit pattern gives 0,1,3,2 as the anticlockwise circuit of the bottom, which is
            // why the loops below list them in that order rather than in index order.
            corner[index] = builder.AddVertex(
                plane.Origin + (plane.XAxis * x) + (plane.YAxis * y) + (plane.Normal * z));
        }

        // Twelve edges: four along the bottom, four along the top, four uprights.
        int bottomFront = builder.AddLineEdge(corner[0], corner[1]);
        int bottomRight = builder.AddLineEdge(corner[1], corner[3]);
        int bottomBack = builder.AddLineEdge(corner[3], corner[2]);
        int bottomLeft = builder.AddLineEdge(corner[2], corner[0]);

        int topFront = builder.AddLineEdge(corner[4], corner[5]);
        int topRight = builder.AddLineEdge(corner[5], corner[7]);
        int topBack = builder.AddLineEdge(corner[7], corner[6]);
        int topLeft = builder.AddLineEdge(corner[6], corner[4]);

        int frontLeft = builder.AddLineEdge(corner[0], corner[4]);
        int frontRight = builder.AddLineEdge(corner[1], corner[5]);
        int backRight = builder.AddLineEdge(corner[3], corner[7]);
        int backLeft = builder.AddLineEdge(corner[2], corner[6]);

        // Each face's loop is added immediately before the face, which is what keeps the loops
        // contiguous — see BrepBuilder.AddFace.
        //
        // **Every loop runs anticlockwise seen from outside the box**, which is the rule that makes
        // each edge appear once forwards and once backwards across the whole shell. The bottom face
        // is the one that catches people: seen from *below* its circuit is the reverse of the
        // obvious one seen from above, so all four of its trims are reversed.
        Face(
            builder,
            new PlaneSurface(plane, new Interval(0, length), new Interval(0, width)),
            [(bottomLeft, true), (bottomBack, true), (bottomRight, true), (bottomFront, true)],
            isReversed: true);

        Face(
            builder,
            new PlaneSurface(
                Plane.ByOriginXAxisYAxis(plane.Origin + (plane.Normal * height), plane.XAxis, plane.YAxis),
                new Interval(0, length),
                new Interval(0, width)),
            [(topFront, false), (topRight, false), (topBack, false), (topLeft, false)],
            isReversed: false);

        Face(
            builder,
            new PlaneSurface(
                Plane.ByOriginXAxisYAxis(plane.Origin, plane.XAxis, plane.Normal),
                new Interval(0, length),
                new Interval(0, height)),
            [(bottomFront, false), (frontRight, false), (topFront, true), (frontLeft, true)],
            isReversed: false);

        Face(
            builder,
            new PlaneSurface(
                Plane.ByOriginXAxisYAxis(plane.Origin + (plane.XAxis * length), plane.YAxis, plane.Normal),
                new Interval(0, width),
                new Interval(0, height)),
            [(bottomRight, false), (backRight, false), (topRight, true), (frontRight, true)],
            isReversed: false);

        Face(
            builder,
            new PlaneSurface(
                Plane.ByOriginXAxisYAxis(plane.Origin + (plane.YAxis * width) + (plane.XAxis * length), -plane.XAxis, plane.Normal),
                new Interval(0, length),
                new Interval(0, height)),
            [(bottomBack, false), (backLeft, false), (topBack, true), (backRight, true)],
            isReversed: false);

        Face(
            builder,
            new PlaneSurface(
                Plane.ByOriginXAxisYAxis(plane.Origin + (plane.YAxis * width), -plane.YAxis, plane.Normal),
                new Interval(0, width),
                new Interval(0, height)),
            [(bottomLeft, false), (frontLeft, false), (topLeft, true), (backLeft, true)],
            isReversed: false);

        builder.CloseShell();

        return builder.Build();
    }

    /// <summary>A solid cylinder: a curved wall and two planar caps.</summary>
    /// <param name="plane">The base: its origin is on the axis and its normal is the axis.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="height">How tall it is.</param>
    /// <returns>A closed solid of three faces.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The radius or height is not finite and positive.</exception>
    /// <remarks>
    /// <b>Three faces and two vertices, which is what a BRep is <i>for</i>.</b> The same shape as a
    /// mesh is hundreds of triangles and an approximation; here the wall is one exact cylindrical
    /// surface, the caps are exact planes, and the seam is a single edge shared between the wall and
    /// itself — which is the case that makes the trim's <c>IsReversed</c> flag earn its keep.
    /// </remarks>
    public static Brep Cylinder(in Plane plane, double radius = 1, double height = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        BrepBuilder builder = new();

        Point3d bottomCentre = plane.Origin;
        Point3d topCentre = plane.Origin + (plane.Normal * height);

        Plane bottomPlane = Plane.ByOriginXAxisYAxis(bottomCentre, plane.XAxis, plane.YAxis);
        Plane topPlane = Plane.ByOriginXAxisYAxis(topCentre, plane.XAxis, plane.YAxis);

        int bottomSeam = builder.AddVertex(bottomCentre + (plane.XAxis * radius));
        int topSeam = builder.AddVertex(topCentre + (plane.XAxis * radius));

        int bottomCircle = builder.AddEdge(bottomSeam, bottomSeam, Circle.ByPlaneRadius(bottomPlane, radius));
        int topCircle = builder.AddEdge(topSeam, topSeam, Circle.ByPlaneRadius(topPlane, radius));
        int seam = builder.AddLineEdge(bottomSeam, topSeam);

        // The bottom cap, wound so its outward normal points down: seen from below, the circle
        // runs the other way, so the trim is reversed.
        Face(builder, new PlaneSurface(bottomPlane, new Interval(-radius, radius), new Interval(-radius, radius)),
            [(bottomCircle, true)],
            isReversed: true);

        // The top cap.
        Face(builder, new PlaneSurface(topPlane, new Interval(-radius, radius), new Interval(-radius, radius)),
            [(topCircle, false)],
            isReversed: false);

        // The wall. Its loop runs up the seam, round the top, down the seam and round the bottom —
        // so the seam edge appears twice, once each way, which is exactly what makes the shell
        // closed. An implementation that used two seam edges would have a model that looks right
        // and reports IsSolid false.
        Face(
            builder,
            new CylindricalSurface(plane, radius, new Interval(0.0, height)),
            [(seam, false), (topCircle, true), (seam, true), (bottomCircle, false)],
            isReversed: false);

        builder.CloseShell();

        return builder.Build();
    }

    /// <summary>Adds a loop and the face over it, in the order the layout needs.</summary>
    private static void Face(
        BrepBuilder builder,
        Surface surface,
        IReadOnlyList<(int Edge, bool IsReversed)> edges,
        bool isReversed)
    {
        int loop = builder.AddLoop(edges);

        builder.AddFace(surface, [loop], isReversed);
    }
}

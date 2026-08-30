using System;
using System.Collections.Generic;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make and combine solids.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds of node live here and the difference is where the kernel seam is.</b> A box and a
/// cylinder are *constructions* — six planes and twelve edges written down — and they work with no
/// provider installed. A union, a fillet or a shell is an *operation*, needs exact solid modelling,
/// and goes through <see cref="BrepKernel.Current"/>; with no provider those refuse by name rather
/// than throwing.
/// </para>
/// <para>
/// <b>A refusal becomes an exception here, and only here.</b> <see cref="KernelResult{T}"/> exists
/// so that the kernel can decline without an exception — an exact kernel declines constantly and
/// legitimately. But a node is a plain method whose failure the engine already reports on the
/// canvas, so this is the layer that turns the one into the other, once, in a single helper rather
/// than at nine call sites.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.Solid)]
public static class Solid
{
    /// <summary>Makes a rectangular box standing on a plane.</summary>
    /// <param name="plane">The frame: the box's minimum corner is at its origin.</param>
    /// <param name="length">Its extent along the plane's x-axis.</param>
    /// <param name="width">Its extent along the plane's y-axis.</param>
    /// <param name="height">Its extent along the plane's normal.</param>
    /// <returns>The solid.</returns>
    [return: NodePort("solid")]
    public static Brep Box(Spark.Geometry.Plane plane, double length = 1, double width = 1, double height = 1) =>
        BrepPrimitives.Box(plane, length, width, height);

    /// <summary>Makes a solid cylinder standing on a plane.</summary>
    /// <param name="plane">The base: its origin is on the axis and its normal is the axis.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="height">How tall it is.</param>
    /// <returns>The solid.</returns>
    [return: NodePort("solid")]
    public static Brep Cylinder(Spark.Geometry.Plane plane, double radius = 1, double height = 1) =>
        BrepPrimitives.Cylinder(plane, radius, height);

    /// <summary>Everything in either solid.</summary>
    /// <param name="first">One solid.</param>
    /// <param name="second">The other.</param>
    /// <returns>The union.</returns>
    [return: NodePort("solid")]
    public static Brep Union(Brep first, Brep second) =>
        Unwrap(BrepKernel.Current.Union(first, second, Tolerance.Default));

    /// <summary>The first solid with the second taken out of it.</summary>
    /// <param name="solid">The solid to cut.</param>
    /// <param name="cutter">What to cut away.</param>
    /// <returns>The difference.</returns>
    [return: NodePort("solid")]
    public static Brep Difference(Brep solid, Brep cutter) =>
        Unwrap(BrepKernel.Current.Difference(solid, cutter, Tolerance.Default));

    /// <summary>Only what is in both solids.</summary>
    /// <param name="first">One solid.</param>
    /// <param name="second">The other.</param>
    /// <returns>The intersection.</returns>
    [return: NodePort("solid")]
    public static Brep Intersection(Brep first, Brep second) =>
        Unwrap(BrepKernel.Current.Intersection(first, second, Tolerance.Default));

    /// <summary>Sweeps a closed profile into a solid.</summary>
    /// <param name="profile">The closed curve to sweep.</param>
    /// <param name="direction">Which way and how far.</param>
    /// <returns>The solid.</returns>
    [return: NodePort("solid")]
    public static Brep Extrude(Spark.Geometry.Curve profile, Vector3d direction) =>
        Unwrap(BrepKernel.Current.Extrude(profile, direction, cap: true, Tolerance.Default));

    /// <summary>Rounds a solid's edges.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="radius">The fillet radius.</param>
    /// <returns>The filleted solid.</returns>
    /// <remarks>
    /// Every edge, because choosing a subset needs a way to *point at* an edge on the canvas — a
    /// selection mechanism that is `E8` work and not this row's.
    /// </remarks>
    [return: NodePort("solid")]
    public static Brep FilletAll(Brep solid, double radius = 0.1)
    {
        ArgumentNullException.ThrowIfNull(solid);

        List<int> edges = [];

        for (int index = 0; index < solid.EdgeCount; index++)
        {
            edges.Add(index);
        }

        return Unwrap(BrepKernel.Current.Fillet(solid, edges, radius, Tolerance.Default));
    }

    /// <summary>Hollows a solid.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="thickness">The wall thickness.</param>
    /// <returns>The shelled solid.</returns>
    [return: NodePort("solid")]
    public static Brep Hollow(Brep solid, double thickness = 0.1) =>
        Unwrap(BrepKernel.Current.Shell(solid, [], thickness, Tolerance.Default));

    /// <summary>Turns a solid into a mesh to a tolerance.</summary>
    /// <param name="solid">The solid.</param>
    /// <param name="tolerance">The largest distance the mesh may stray from the solid.</param>
    /// <returns>The mesh.</returns>
    [return: NodePort("mesh")]
    public static Mesh ToMesh(Brep solid, double tolerance = 0.01) =>
        Unwrap(BrepKernel.Current.Tessellate(
            solid, new Tolerance(tolerance, Angle.FromDegrees(1), 1e-12)));

    /// <summary>How much space a solid encloses.</summary>
    /// <param name="solid">The solid.</param>
    /// <returns>The volume.</returns>
    /// <remarks>
    /// <b>Measured on the tessellation, and that is honest rather than ideal.</b> The exact volume
    /// of a BRep is a surface integral over its faces, which is a kernel operation; what this gives
    /// is the volume of the mesh at the default tolerance, which approaches the true one from
    /// below. A node that reported an exact figure it had not computed would be worse than one that
    /// says which it is.
    /// </remarks>
    [return: NodePort("volume")]
    public static double Volume(Brep solid) => ToMesh(solid).Volume();

    /// <summary>How many faces a solid has.</summary>
    /// <param name="solid">The solid.</param>
    /// <returns>The count.</returns>
    [return: NodePort("count")]
    public static int FaceCount(Brep solid)
    {
        ArgumentNullException.ThrowIfNull(solid);

        return solid.FaceCount;
    }

    /// <summary>Whether a solid is closed and consistently oriented.</summary>
    /// <param name="solid">The solid.</param>
    /// <returns>True when every edge is used exactly twice, once each way.</returns>
    [return: NodePort("closed")]
    public static bool IsClosed(Brep solid)
    {
        ArgumentNullException.ThrowIfNull(solid);

        return solid.IsSolid;
    }

    /// <summary>Turns a kernel refusal into the exception the engine reports on the node.</summary>
    private static T Unwrap<T>(KernelResult<T> result) =>
        result.TryGetValue(out T value)
            ? value
            : throw new InvalidOperationException(
                result.Diagnostic is { } diagnostic
                    ? diagnostic.Detail is { Length: > 0 } detail
                        ? diagnostic.Message + " " + detail
                        : diagnostic.Message
                    : "The kernel operation did not succeed.");
}

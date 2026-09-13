using Spark.Api;
using Spark.Geometry;

namespace Spark.Nodes.Core;

/// <summary>
/// Nodes that make meshes from subdivision counts.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are not solids turned into meshes.</b> <c>Solid.ToMesh</c> takes a tolerance and gives
/// whatever density meets it, which is what you want when the mesh is on its way to a renderer or a
/// file. These take <b>how many divisions</b>, which is what you want when the mesh itself is the
/// thing you are going to work on — deform it, colour it per face, or count on its structure.
/// </para>
/// <para>
/// The type name shadows <see cref="Spark.Geometry.Mesh"/> inside this namespace, so the kernel
/// type is written out in full below.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.Solid)]
public static class Mesh
{
    /// <summary>Makes a flat rectangular grid of quads.</summary>
    /// <param name="plane">The plane it lies in; its origin is the centre.</param>
    /// <param name="width">The size along the plane's x axis.</param>
    /// <param name="length">The size along the plane's y axis.</param>
    /// <param name="xDivisions">How many quads across.</param>
    /// <param name="yDivisions">How many quads along.</param>
    /// <returns>The mesh.</returns>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("mesh")]
    [SparkNodeAlias("Mesh.Plane")]
    public static Spark.Geometry.Mesh PlaneGrid(
        Spark.Geometry.Plane plane, double width = 1, double length = 1, int xDivisions = 1, int yDivisions = 1) =>
        MeshPrimitives.Plane(plane, width, length, xDivisions, yDivisions);

    /// <summary>Makes a box, centerd on a plane's origin.</summary>
    /// <param name="plane">The plane; its origin is the centre of the box.</param>
    /// <param name="width">The size along the plane's x axis.</param>
    /// <param name="length">The size along the plane's y axis.</param>
    /// <param name="height">The size along the plane's normal.</param>
    /// <param name="xDivisions">Subdivisions along x.</param>
    /// <param name="yDivisions">Subdivisions along y.</param>
    /// <param name="zDivisions">Subdivisions along the normal.</param>
    /// <returns>The mesh.</returns>
    /// <remarks>
    /// <b>Its six sides do not share vertices</b>, because a box's edges are creases and sharing
    /// them would make a renderer round the corners off.
    /// </remarks>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("mesh")]
    public static Spark.Geometry.Mesh Cuboid(
        Spark.Geometry.Plane plane,
        double width = 1,
        double length = 1,
        double height = 1,
        int xDivisions = 1,
        int yDivisions = 1,
        int zDivisions = 1) =>
        MeshPrimitives.Cuboid(plane, width, length, height, xDivisions, yDivisions, zDivisions);

    /// <summary>Makes a sphere as a longitude and latitude grid.</summary>
    /// <param name="plane">The plane; its origin is the centre and its normal the polar axis.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="divisions">How many divisions around.</param>
    /// <param name="stacks">How many divisions from pole to pole.</param>
    /// <returns>The mesh: triangles at the poles, quads everywhere else.</returns>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("mesh")]
    public static Spark.Geometry.Mesh Sphere(
        Spark.Geometry.Plane plane, double radius = 1, int divisions = 16, int stacks = 8) =>
        MeshPrimitives.Sphere(plane, radius, divisions, stacks);

    /// <summary>Makes a cone, or a truncated one, standing on a plane.</summary>
    /// <param name="plane">The plane; its origin is the centre of the base and its normal the axis.</param>
    /// <param name="baseRadius">The radius at the base.</param>
    /// <param name="topRadius">The radius at the top. Zero gives a point.</param>
    /// <param name="height">How far it rises.</param>
    /// <param name="divisions">How many divisions around.</param>
    /// <param name="capped">Whether to close the ends.</param>
    /// <returns>The mesh.</returns>
    [SparkNode(Kind = NodeMemberKind.Create)]
    [return: NodePort("mesh")]
    public static Spark.Geometry.Mesh Cone(
        Spark.Geometry.Plane plane,
        double baseRadius = 1,
        double topRadius = 0,
        double height = 1,
        int divisions = 16,
        bool capped = true) =>
        MeshPrimitives.Cone(plane, baseRadius, topRadius, height, divisions, capped);
}

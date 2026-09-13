using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Meshes built directly from subdivision counts, rather than by tessellating a solid (`E2-T69`).
/// </summary>
/// <remarks>
/// <para>
/// <b>A mesh primitive is not a BRep primitive tessellated, and the argument is the difference.</b>
/// <c>Surface.ToMesh</c> and the kernel's tessellation take a <see cref="Tolerance"/> and
/// give whatever density meets it — the right answer when the mesh is a means to an end, and the
/// wrong one when the caller wants a grid. These take <b>subdivision counts</b>, because somebody
/// asking for a sphere of twelve divisions wants a predictable net they can deform, index into, or
/// hand to a shader, and a tolerance cannot express that.
/// </para>
/// <para>
/// <b>Every primitive is quad-dominant, and the exceptions are geometric rather than incidental.</b>
/// A sphere's poles and a cone's apex are single vertices, so the rings that touch them are
/// triangles and everything between them is a quad. That is the honest topology; splitting the
/// quads to make the mesh uniform would double the face count and lose the grid the caller asked
/// for.
/// </para>
/// <para>
/// <b>The seam is given once.</b> On a closed primitive the last column of vertices <i>is</i> the
/// first column, not a second copy at the same place. A duplicated seam renders identically, has
/// every vertex in the right position, and is two sheets meeting nowhere —
/// <see cref="MeshTopology.IsClosed"/> is the only thing that notices, and it is what the tests
/// assert.
/// </para>
/// <para>
/// <b>Each takes a <see cref="Plane"/> where Dynamo takes a bare point</b>, because Spark's own
/// primitives are orientable (<see cref="BrepPrimitives"/>) and a mesh that can only be built
/// axis-aligned would be the odd one out. Pass <see cref="Plane.WorldXY"/> for Dynamo's behaviour.
/// </para>
/// </remarks>
public static class MeshPrimitives
{
    /// <summary>A rectangular grid of quads lying in a plane.</summary>
    /// <param name="plane">The plane it lies in; its origin is the centre.</param>
    /// <param name="width">The extent along the plane's x axis.</param>
    /// <param name="length">The extent along the plane's y axis.</param>
    /// <param name="xDivisions">How many quads across. At least 1.</param>
    /// <param name="yDivisions">How many quads along. At least 1.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size is not finite and positive, or a division count is below 1.</exception>
    public static Mesh Plane(in Plane plane, double width = 1.0, double length = 1.0, int xDivisions = 1, int yDivisions = 1)
    {
        CheckSize(width, nameof(width));
        CheckSize(length, nameof(length));
        CheckDivisions(xDivisions, nameof(xDivisions));
        CheckDivisions(yDivisions, nameof(yDivisions));

        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        AddGrid(vertices, faces, plane, width, length, xDivisions, yDivisions, 0.0);

        return new Mesh(vertices, faces);
    }

    /// <summary>A box of six grids, centred on a plane's origin.</summary>
    /// <param name="plane">The plane; its origin is the centre of the box.</param>
    /// <param name="width">The extent along the plane's x axis.</param>
    /// <param name="length">The extent along the plane's y axis.</param>
    /// <param name="height">The extent along the plane's normal.</param>
    /// <param name="xDivisions">Subdivisions along x. At least 1.</param>
    /// <param name="yDivisions">Subdivisions along y. At least 1.</param>
    /// <param name="zDivisions">Subdivisions along the normal. At least 1.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size is not finite and positive, or a division count is below 1.</exception>
    /// <remarks>
    /// <b>The six faces do not share vertices, and that is deliberate.</b> A box's edges are creases:
    /// sharing a vertex along one would mean a single normal where the surface has two, and every
    /// renderer would round the edge off. The mesh is therefore six separate grids —
    /// <see cref="MeshTopology.IsClosed"/> is false for it, which is the truthful answer about this
    /// vertex set, and <see cref="Mesh.Welded"/> merges them for a caller who wants the other
    /// trade.
    /// </remarks>
    public static Mesh Cuboid(
        in Plane plane,
        double width = 1.0,
        double length = 1.0,
        double height = 1.0,
        int xDivisions = 1,
        int yDivisions = 1,
        int zDivisions = 1)
    {
        CheckSize(width, nameof(width));
        CheckSize(length, nameof(length));
        CheckSize(height, nameof(height));
        CheckDivisions(xDivisions, nameof(xDivisions));
        CheckDivisions(yDivisions, nameof(yDivisions));
        CheckDivisions(zDivisions, nameof(zDivisions));

        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        Vector3d x = plane.XAxis;
        Vector3d y = plane.YAxis;
        Vector3d z = plane.Normal;
        Point3d origin = plane.Origin;

        // Each face's own plane, chosen so that its normal - the cross product of the two axes
        // given - points OUT of the box. The offset is then always positive, along that normal,
        // which is what keeps the six calls uniform instead of three of them carrying a minus sign.
        AddGrid(vertices, faces, Face(origin, x, y), width, length, xDivisions, yDivisions, height * 0.5);
        AddGrid(vertices, faces, Face(origin, y, x), length, width, yDivisions, xDivisions, height * 0.5);
        AddGrid(vertices, faces, Face(origin, z, x), height, width, zDivisions, xDivisions, length * 0.5);
        AddGrid(vertices, faces, Face(origin, x, z), width, height, xDivisions, zDivisions, length * 0.5);
        AddGrid(vertices, faces, Face(origin, y, z), length, height, yDivisions, zDivisions, width * 0.5);
        AddGrid(vertices, faces, Face(origin, z, y), height, length, zDivisions, yDivisions, width * 0.5);

        return new Mesh(vertices, faces);

        static Plane Face(in Point3d origin, in Vector3d xAxis, in Vector3d yAxis) =>
            Spark.Geometry.Plane.FromOriginXAxisYAxis(origin, xAxis, yAxis);
    }

    /// <summary>A sphere as a longitude and latitude grid.</summary>
    /// <param name="plane">The plane; its origin is the centre and its normal the polar axis.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="divisions">How many divisions around. At least 3.</param>
    /// <param name="stacks">How many divisions from pole to pole. At least 2.</param>
    /// <returns>The mesh: triangles at the two poles, quads everywhere else.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The radius is not finite and positive, or a count is too low.</exception>
    /// <remarks>
    /// <b>The seam is given once and the poles are single vertices</b>, so the result is closed —
    /// see the remarks on this type for why that is the claim worth testing.
    /// </remarks>
    public static Mesh Sphere(in Plane plane, double radius = 1.0, int divisions = 16, int stacks = 8)
    {
        CheckSize(radius, nameof(radius));
        CheckDivisions(divisions, nameof(divisions), least: 3);
        CheckDivisions(stacks, nameof(stacks), least: 2);

        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        int south = vertices.Count;
        vertices.Add(plane.Origin - (plane.Normal * radius));

        // The rings between the poles. The seam is not repeated: column `divisions` is column 0.
        for (int stack = 1; stack < stacks; stack++)
        {
            double polar = Math.PI * stack / stacks;
            double ringRadius = radius * Math.Sin(polar);
            double along = -radius * Math.Cos(polar);

            for (int index = 0; index < divisions; index++)
            {
                double angle = 2.0 * Math.PI * index / divisions;

                vertices.Add(
                    plane.Origin
                    + (plane.XAxis * (ringRadius * Math.Cos(angle)))
                    + (plane.YAxis * (ringRadius * Math.Sin(angle)))
                    + (plane.Normal * along));
            }
        }

        int north = vertices.Count;
        vertices.Add(plane.Origin + (plane.Normal * radius));

        int FirstOfRing(int stack) => south + 1 + ((stack - 1) * divisions);

        for (int index = 0; index < divisions; index++)
        {
            int next = (index + 1) % divisions;

            faces.Add(new MeshFace(south, FirstOfRing(1) + next, FirstOfRing(1) + index));

            for (int stack = 1; stack < stacks - 1; stack++)
            {
                faces.Add(new MeshFace(
                    FirstOfRing(stack) + index,
                    FirstOfRing(stack) + next,
                    FirstOfRing(stack + 1) + next,
                    FirstOfRing(stack + 1) + index));
            }

            faces.Add(new MeshFace(north, FirstOfRing(stacks - 1) + index, FirstOfRing(stacks - 1) + next));
        }

        return new Mesh(vertices, faces);
    }

    /// <summary>A cone or a truncated cone, standing on a plane.</summary>
    /// <param name="plane">The plane; its origin is the centre of the base and its normal the axis.</param>
    /// <param name="baseRadius">The radius at the base.</param>
    /// <param name="topRadius">The radius at the top. Zero gives a point.</param>
    /// <param name="height">How far it rises along the axis.</param>
    /// <param name="divisions">How many divisions around. At least 3.</param>
    /// <param name="capped">Whether to close the base and, if it has one, the top.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The height is not finite and positive, a radius is negative or not finite, both radii are
    /// zero, or the division count is below 3.
    /// </exception>
    public static Mesh Cone(
        in Plane plane,
        double baseRadius = 1.0,
        double topRadius = 0.0,
        double height = 1.0,
        int divisions = 16,
        bool capped = true)
    {
        CheckSize(height, nameof(height));
        CheckDivisions(divisions, nameof(divisions), least: 3);

        if (!double.IsFinite(baseRadius) || baseRadius < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseRadius), baseRadius, "A radius must be finite and not negative.");
        }

        if (!double.IsFinite(topRadius) || topRadius < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(topRadius), topRadius, "A radius must be finite and not negative.");
        }

        if (baseRadius <= 0.0 && topRadius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseRadius), baseRadius, "A cone with no radius at either end is a line, not a mesh.");
        }

        List<Point3d> vertices = [];
        List<MeshFace> faces = [];

        int baseRing = vertices.Count;
        AddRing(vertices, plane, baseRadius, 0.0, divisions);

        int topRing = vertices.Count;
        AddRing(vertices, plane, topRadius, height, divisions);

        // A ring of radius zero collapses to one vertex, so the wall there is triangles. Both are
        // written the same way: the degenerate ring's `divisions` vertices are all the same point,
        // and the quad they would make has two coincident corners. Emitting a triangle instead
        // keeps the face list honest about how many corners the face has.
        bool apexAtTop = topRadius <= 0.0;
        bool apexAtBase = baseRadius <= 0.0;

        for (int index = 0; index < divisions; index++)
        {
            int next = (index + 1) % divisions;

            if (apexAtTop)
            {
                faces.Add(new MeshFace(baseRing + index, baseRing + next, topRing));
            }
            else if (apexAtBase)
            {
                faces.Add(new MeshFace(baseRing, topRing + next, topRing + index));
            }
            else
            {
                faces.Add(new MeshFace(baseRing + index, baseRing + next, topRing + next, topRing + index));
            }
        }

        if (capped)
        {
            if (!apexAtBase)
            {
                int centre = vertices.Count;
                vertices.Add(plane.Origin);

                for (int index = 0; index < divisions; index++)
                {
                    faces.Add(new MeshFace(centre, baseRing + ((index + 1) % divisions), baseRing + index));
                }
            }

            if (!apexAtTop)
            {
                int centre = vertices.Count;
                vertices.Add(plane.Origin + (plane.Normal * height));

                for (int index = 0; index < divisions; index++)
                {
                    faces.Add(new MeshFace(centre, topRing + index, topRing + ((index + 1) % divisions)));
                }
            }
        }

        return new Mesh(vertices, faces);
    }

    /// <summary>Adds a ring of points about a plane's normal, at a height along it.</summary>
    /// <remarks>A ring of radius zero is still <c>divisions</c> vertices, all of them the apex.</remarks>
    private static void AddRing(List<Point3d> vertices, in Plane plane, double radius, double along, int divisions)
    {
        if (radius <= 0.0)
        {
            vertices.Add(plane.Origin + (plane.Normal * along));

            return;
        }

        for (int index = 0; index < divisions; index++)
        {
            double angle = 2.0 * Math.PI * index / divisions;

            vertices.Add(
                plane.Origin
                + (plane.XAxis * (radius * Math.Cos(angle)))
                + (plane.YAxis * (radius * Math.Sin(angle)))
                + (plane.Normal * along));
        }
    }

    /// <summary>Adds a rectangular grid of quads, offset along its plane's normal.</summary>
    private static void AddGrid(
        List<Point3d> vertices,
        List<MeshFace> faces,
        in Plane plane,
        double width,
        double length,
        int xDivisions,
        int yDivisions,
        double offset)
    {
        int first = vertices.Count;
        Point3d centre = plane.Origin + (plane.Normal * offset);

        for (int i = 0; i <= xDivisions; i++)
        {
            double x = ((i / (double)xDivisions) - 0.5) * width;

            for (int j = 0; j <= yDivisions; j++)
            {
                double y = ((j / (double)yDivisions) - 0.5) * length;

                vertices.Add(centre + (plane.XAxis * x) + (plane.YAxis * y));
            }
        }

        int stride = yDivisions + 1;

        for (int i = 0; i < xDivisions; i++)
        {
            for (int j = 0; j < yDivisions; j++)
            {
                int corner = first + (i * stride) + j;

                faces.Add(new MeshFace(corner, corner + stride, corner + stride + 1, corner + 1));
            }
        }
    }

    private static void CheckSize(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A size must be finite and greater than zero.");
        }
    }

    private static void CheckDivisions(int value, string name, int least = 1)
    {
        if (value < least)
        {
            throw new ArgumentOutOfRangeException(
                name, value, $"A mesh primitive needs at least {least} division(s) here.");
        }
    }
}

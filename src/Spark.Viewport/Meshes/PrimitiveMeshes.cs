using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spark.Viewport.Meshes;

/// <summary>
/// Tessellated primitives the viewport can build without the kernel. They exist for two reasons
/// that are both real: they give the viewport something to draw before the graph engine can feed
/// it, and they are the fixtures the watertightness and winding tests assert against — a
/// renderer whose own primitives are not watertight cannot be trusted to report that incoming
/// geometry is not either.
/// </summary>
/// <remarks>
/// Winding is counter-clockwise seen from outside the solid and normals point away from the
/// material, exactly as <see cref="NamespaceDoc"/> states.
/// </remarks>
public static class PrimitiveMeshes
{
    /// <summary>
    /// An axis-aligned box with flat shading. Vertices are duplicated per face, because a box
    /// with shared corner vertices has one normal where it needs three and shades as a
    /// rounded lump.
    /// </summary>
    /// <param name="min">The lower corner.</param>
    /// <param name="max">The upper corner.</param>
    /// <returns>A mesh with 24 vertices, 12 triangles and 12 edge segments.</returns>
    public static Mesh Box(Vector3 min, Vector3 max)
    {
        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z),
            new(min.X, max.Y, max.Z),
        ];

        // Each face lists its four corners anti-clockwise seen from outside, then its outward
        // normal. Getting one of these backwards is exactly the defect two-sided shading exists
        // to keep visible, and exactly the defect the winding test exists to catch.
        (int A, int B, int C, int D, Vector3 Normal)[] faces =
        [
            (0, 3, 2, 1, -Vector3.UnitZ),   // bottom, seen from below
            (4, 5, 6, 7, Vector3.UnitZ),    // top
            (0, 1, 5, 4, -Vector3.UnitY),   // front, -Y
            (2, 3, 7, 6, Vector3.UnitY),    // back, +Y
            (1, 2, 6, 5, Vector3.UnitX),    // right, +X
            (3, 0, 4, 7, -Vector3.UnitX),   // left, -X
        ];

        float[] positions = new float[24 * 3];
        float[] normals = new float[24 * 3];
        int[] indices = new int[36];

        for (int face = 0; face < faces.Length; face++)
        {
            (int a, int b, int c, int d, Vector3 normal) = faces[face];
            int baseVertex = face * 4;

            WriteVertex(positions, normals, baseVertex + 0, corners[a], normal);
            WriteVertex(positions, normals, baseVertex + 1, corners[b], normal);
            WriteVertex(positions, normals, baseVertex + 2, corners[c], normal);
            WriteVertex(positions, normals, baseVertex + 3, corners[d], normal);

            int t = face * 6;
            indices[t + 0] = baseVertex + 0;
            indices[t + 1] = baseVertex + 1;
            indices[t + 2] = baseVertex + 2;
            indices[t + 3] = baseVertex + 0;
            indices[t + 4] = baseVertex + 2;
            indices[t + 5] = baseVertex + 3;
        }

        int[] edges = new int[24 * 2];
        int e = 0;
        for (int face = 0; face < faces.Length; face++)
        {
            int baseVertex = face * 4;
            for (int corner = 0; corner < 4; corner++)
            {
                edges[e++] = baseVertex + corner;
                edges[e++] = baseVertex + ((corner + 1) & 3);
            }
        }

        return new Mesh(positions, normals, indices, edges);
    }

    /// <summary>
    /// A latitude-longitude sphere with smooth shading, so the viewport has something whose
    /// lighting gradient is visible rather than faceted. Poles are degenerate triangles by
    /// construction, which is normal for this parameterisation and is why the watertightness
    /// assertion welds by position before counting edges.
    /// </summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radius">The radius. Must be positive.</param>
    /// <param name="segments">Divisions around the equator. Clamped to at least 3.</param>
    /// <param name="rings">Divisions from pole to pole. Clamped to at least 2.</param>
    /// <returns>The tessellated sphere.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is not positive.</exception>
    public static Mesh Sphere(Vector3 centre, float radius, int segments = 32, int rings = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        segments = Math.Max(3, segments);
        rings = Math.Max(2, rings);

        int vertexCount = (rings + 1) * (segments + 1);
        float[] positions = new float[vertexCount * 3];
        float[] normals = new float[vertexCount * 3];

        int v = 0;
        for (int ring = 0; ring <= rings; ring++)
        {
            // Polar angle measured from +Z, because +Z is up.
            float phi = MathF.PI * ring / rings;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            for (int segment = 0; segment <= segments; segment++)
            {
                float theta = MathF.Tau * segment / segments;
                Vector3 normal = new(sinPhi * MathF.Cos(theta), sinPhi * MathF.Sin(theta), cosPhi);
                WriteVertex(positions, normals, v++, centre + (normal * radius), normal);
            }
        }

        List<int> indices = new(rings * segments * 6);
        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int a = (ring * (segments + 1)) + segment;
                int b = a + segments + 1;

                // Ring 0 sits at +Z, rings descend, and segments advance anti-clockwise about
                // +Z. With that parameterisation the outward-facing winding is a -> b -> b+1,
                // which is the reverse of the order the indices are laid out in; getting it the
                // other way round produces a sphere lit from inside.
                indices.Add(a);
                indices.Add(b);
                indices.Add(b + 1);
                indices.Add(a);
                indices.Add(b + 1);
                indices.Add(a + 1);
            }
        }

        // Edges: every ring line, plus one meridian every eighth of a turn. Drawing all of them
        // turns the sphere into a solid white ball at any reasonable size.
        List<int> edges = new(rings * segments * 2);
        int meridianStride = Math.Max(1, segments / 8);
        for (int ring = 0; ring <= rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int a = (ring * (segments + 1)) + segment;
                if (ring is not 0 && ring != rings)
                {
                    edges.Add(a);
                    edges.Add(a + 1);
                }

                if (segment % meridianStride == 0 && ring < rings)
                {
                    edges.Add(a);
                    edges.Add(a + segments + 1);
                }
            }
        }

        return new Mesh(positions, normals, [.. indices], [.. edges]);
    }

    private static void WriteVertex(float[] positions, float[] normals, int vertex, Vector3 position, Vector3 normal)
    {
        int i = vertex * 3;
        positions[i] = position.X;
        positions[i + 1] = position.Y;
        positions[i + 2] = position.Z;
        normals[i] = normal.X;
        normals[i + 1] = normal.Y;
        normals[i + 2] = normal.Z;
    }
}

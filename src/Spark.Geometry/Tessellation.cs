using System;
using System.Collections.Generic;

namespace Spark.Geometry;

/// <summary>
/// Turns a <see cref="Surface"/> into triangles and quads to a tolerance (`E2-T26`).
/// </summary>
/// <remarks>
/// <para>
/// <b>Adaptive in each direction independently, on a tensor grid.</b> Each direction is subdivided
/// until the chord sag — how far the surface bulges away from the straight line between two
/// samples — is inside the tolerance, and the grid is the product of the two. It is what every
/// kernel does first, it produces regular quads that a renderer and a remesher both like, and its
/// limitation is stated rather than hidden: a surface with a small tight feature in the middle
/// gets refinement across the whole row and column that contains it, where a genuinely adaptive
/// scheme would refine only there. That is a later row, and this is what the viewport needs now.
/// </para>
/// <para>
/// <b>Sag is measured at several parameters in the other direction, not one.</b> Measuring a
/// cylinder's u-direction sag along a single v would be fine; measuring a cone's would sample the
/// narrow end and under-refine the wide one. Three probes cost three evaluations per test and
/// remove the whole class of error.
/// </para>
/// <para>
/// <b>Seams and poles are welded, and that is the decision that makes the output useful.</b> On a
/// closed direction the last column of samples *is* the first, and on a degenerate row every
/// sample is the same point — a sphere's pole, a cone's apex. Emitting them as distinct vertices
/// gives a mesh that looks perfect, has naked edges everywhere it should not, reports a nonsense
/// volume, and cannot be booleaned. So a closed direction reuses the first column's indices, and a
/// collapsed row becomes one vertex with a triangle fan around it. **The cost is the texture
/// seam**: a welded seam vertex has one texture coordinate where a renderer would want two, which
/// is a texturing concern and is the right thing to give up here.
/// </para>
/// </remarks>
public static class Tessellation
{
    /// <summary>
    /// The most samples either direction will take, whatever tolerance is asked for.
    /// </summary>
    /// <remarks>
    /// The same reasoning as <c>Curve</c>'s point cap: a tolerance far below a surface's size
    /// would otherwise ask for an unbounded grid, and a viewport that hangs is worse than a facet
    /// that is a micron out. 512 by 512 is a quarter of a million quads, which is already past what
    /// anything here should be producing from one surface.
    /// </remarks>
    public const int MaximumSamplesPerDirection = 512;

    /// <summary>How many probes in the other direction each sag test takes.</summary>
    private const int SagProbes = 3;

    /// <summary>Tessellates a surface into a sink.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="sink">Where the vertices and faces go.</param>
    /// <param name="tolerance">The largest chord sag to allow.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static void Tessellate(Surface surface, ITessellationSink sink, in Tolerance tolerance = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sink);

        double[] us = Parameters(surface, tolerance.Linear, alongU: true);
        double[] vs = Parameters(surface, tolerance.Linear, alongU: false);

        int[,] indices = new int[us.Length, vs.Length];

        for (int j = 0; j < vs.Length; j++)
        {
            // A row that has collapsed to a point is one vertex, shared by every face that reaches
            // it. A pole emitted as a row of coincident vertices is the classic cause of a "closed"
            // sphere with a ring of zero-area triangles and a hole underneath them.
            bool collapsed = IsCollapsedRow(surface, us, vs[j], tolerance.Linear);

            for (int i = 0; i < us.Length; i++)
            {
                if (collapsed && i > 0)
                {
                    indices[i, j] = indices[0, j];
                    continue;
                }

                // A closed direction's last sample is its first. Reusing the index is what keeps
                // the mesh closed across the seam.
                if (surface.IsClosedU && i == us.Length - 1 && Wraps(surface.DomainU, us))
                {
                    indices[i, j] = indices[0, j];
                    continue;
                }

                if (surface.IsClosedV && j == vs.Length - 1 && Wraps(surface.DomainV, vs))
                {
                    indices[i, j] = indices[i, 0];
                    continue;
                }

                indices[i, j] = sink.AddVertex(
                    surface.PointAt(us[i], vs[j]),
                    NormalNear(surface, us, vs, i, j),
                    new UV(
                        surface.DomainU.Normalise(us[i]),
                        surface.DomainV.Normalise(vs[j])));
            }
        }

        for (int i = 0; i + 1 < us.Length; i++)
        {
            for (int j = 0; j + 1 < vs.Length; j++)
            {
                Emit(
                    sink,
                    indices[i, j],
                    indices[i + 1, j],
                    indices[i + 1, j + 1],
                    indices[i, j + 1]);
            }
        }
    }

    /// <summary>Tessellates a surface into a new mesh.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="tolerance">The largest chord sag to allow.</param>
    /// <returns>A mesh with normals and texture coordinates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    public static Mesh ToMesh(this Surface surface, in Tolerance tolerance = default)
    {
        MeshBuilder builder = new();

        Tessellate(surface, builder, tolerance);

        return builder.Build();
    }

    /// <summary>
    /// Emits one cell of the grid, as a quad, a triangle, or nothing.
    /// </summary>
    /// <remarks>
    /// <b>A cell next to a welded pole has only three distinct corners</b> — two of its four are
    /// the same vertex — and a cell in a fully collapsed corner has fewer still. Emitting the quad
    /// regardless would give a face that names one vertex twice, which is degenerate, has no
    /// normal, and is exactly what a renderer draws as a black sliver.
    /// </remarks>
    private static void Emit(ITessellationSink sink, int a, int b, int c, int d)
    {
        // The four corners with consecutive duplicates removed, keeping the winding.
        Span<int> corners = stackalloc int[4];
        int count = 0;

        foreach (int corner in stackalloc[] { a, b, c, d })
        {
            if (count == 0 || corners[count - 1] != corner)
            {
                corners[count++] = corner;
            }
        }

        // The first and last can also coincide once the middle has been removed.
        if (count > 1 && corners[0] == corners[count - 1])
        {
            count--;
        }

        switch (count)
        {
            case 4:
                sink.AddQuad(corners[0], corners[1], corners[2], corners[3]);
                break;

            case 3:
                sink.AddTriangle(corners[0], corners[1], corners[2]);
                break;

            default:
                // Two distinct corners or fewer is a line or a point, and neither is a face.
                break;
        }
    }

    /// <summary>
    /// The sample parameters for one direction, refined until the chord sag is inside tolerance.
    /// </summary>
    private static double[] Parameters(Surface surface, double sag, bool alongU)
    {
        Interval domain = alongU ? surface.DomainU : surface.DomainV;
        Interval other = alongU ? surface.DomainV : surface.DomainU;

        List<double> parameters = [.. Seeds(surface, alongU)];

        // Bisect any span whose midpoint is further from its chord than the tolerance allows,
        // and keep going while any span still is. Bounded by the sample cap, because a tolerance
        // far below the surface's size would otherwise never be satisfied.
        bool refined = true;

        while (refined && parameters.Count < MaximumSamplesPerDirection)
        {
            refined = false;

            for (int index = parameters.Count - 1; index > 0; index--)
            {
                double low = parameters[index - 1];
                double high = parameters[index];
                double middle = (low + high) * 0.5;

                if (!Sags(surface, other, low, middle, high, sag, alongU))
                {
                    continue;
                }

                parameters.Insert(index, middle);
                refined = true;

                if (parameters.Count >= MaximumSamplesPerDirection)
                {
                    break;
                }
            }
        }

        return [.. parameters];
    }

    /// <summary>
    /// Whether the surface bulges further from the chord than the tolerance allows, anywhere along
    /// the other direction.
    /// </summary>
    private static bool Sags(
        Surface surface, in Interval other, double low, double middle, double high, double sag, bool alongU)
    {
        for (int probe = 0; probe <= SagProbes; probe++)
        {
            double across = other.Denormalise(probe / (double)SagProbes);

            Point3d a = Evaluate(surface, low, across, alongU);
            Point3d b = Evaluate(surface, middle, across, alongU);
            Point3d c = Evaluate(surface, high, across, alongU);

            if (b.DistanceTo(Point3d.Lerp(a, c, 0.5)) > sag)
            {
                return true;
            }
        }

        return false;
    }

    private static Point3d Evaluate(Surface surface, double along, double across, bool alongU) =>
        alongU ? surface.PointAt(along, across) : surface.PointAt(across, along);

    /// <summary>
    /// The parameters a direction starts from, before any refinement.
    /// </summary>
    /// <remarks>
    /// <b>A closed direction starts with four spans, not one.</b> A whole circle's first and last
    /// samples coincide, so a single span's chord has zero length and zero sag — the refinement
    /// would declare a circle flat and stop. Quarter turns are the smallest seed that cannot say
    /// that, and they are also where the analytic surfaces' own rational spans fall.
    /// </remarks>
    private static double[] Seeds(Surface surface, bool alongU)
    {
        Interval domain = alongU ? surface.DomainU : surface.DomainV;
        bool closed = alongU ? surface.IsClosedU : surface.IsClosedV;

        int spans = closed ? 4 : 1;
        double[] seeds = new double[spans + 1];

        for (int index = 0; index <= spans; index++)
        {
            seeds[index] = domain.Denormalise(index / (double)spans);
        }

        return seeds;
    }

    /// <summary>Whether a whole row of samples is the same point, within tolerance.</summary>
    private static bool IsCollapsedRow(Surface surface, double[] us, double v, double tolerance)
    {
        Point3d first = surface.PointAt(us[0], v);

        for (int index = 1; index < us.Length; index++)
        {
            if (surface.PointAt(us[index], v).DistanceTo(first) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a direction's samples run all the way round its domain.</summary>
    private static bool Wraps(in Interval domain, double[] parameters) =>
        Math.Abs(parameters[0] - domain.Min) <= double.Epsilon
        && Math.Abs(parameters[^1] - domain.Max) <= double.Epsilon;

    /// <summary>
    /// The normal at a grid point, stepped away from a degeneracy when there is one.
    /// </summary>
    /// <remarks>
    /// <b>A pole has no normal and every triangle that meets it needs one.</b> Refusing would
    /// leave a hole in the mesh where a sphere's cap should be; inventing the axis would be wrong
    /// on a cone whose apex is off-axis. Asking a short step *into* the surface gives the limit the
    /// surface is approaching, which is the answer a renderer wants and the one a subdivision
    /// scheme would converge to.
    /// </remarks>
    private static Vector3d NormalNear(Surface surface, double[] us, double[] vs, int i, int j)
    {
        try
        {
            return surface.NormalAt(us[i], vs[j]);
        }
        catch (InvalidOperationException)
        {
            double u = us[i];
            double v = vs[j];

            // A step towards the middle of the domain, small enough not to move the normal
            // measurably on anything that is not degenerate.
            if (j == 0 && vs.Length > 1)
            {
                v += (vs[1] - vs[0]) * 1e-3;
            }
            else if (j == vs.Length - 1 && vs.Length > 1)
            {
                v -= (vs[^1] - vs[^2]) * 1e-3;
            }

            if (i == 0 && us.Length > 1)
            {
                u += (us[1] - us[0]) * 1e-3;
            }
            else if (i == us.Length - 1 && us.Length > 1)
            {
                u -= (us[^1] - us[^2]) * 1e-3;
            }

            try
            {
                return surface.NormalAt(u, v);
            }
            catch (InvalidOperationException)
            {
                // Two degenerate directions at once, which is a surface with no tangent plane
                // anywhere near this parameter. Nothing better than a stated direction exists.
                return Vector3d.ZAxis;
            }
        }
    }
}

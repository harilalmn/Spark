using System;
using System.Collections.Generic;
using CsCheck;
using Spark.Geometry;

namespace Spark.Geometry.Properties;

/// <summary>
/// Invariants every surface has to satisfy, across nine decades of working scale in each
/// direction — `E2-T33`, `E11-T10`.
/// </summary>
/// <remarks>
/// <para>
/// Each sample builds one of every surface type at the drawn scale and asserts the same
/// invariants on all of them, so a defect in the shared base is found eight times and a defect in
/// one override is found once. Assertions are relative to the surface's own size, never absolute:
/// an absolute epsilon is a different test at a scale of 1e-9 than at 1e9, which is exactly the
/// failure [ADR-0018](../../docs/adr/0018-property-based-tests-on-the-kernel.md) exists to
/// prevent.
/// </para>
/// <para>
/// <b>Two traps were found by scouting before any of this was written, and both shape what is
/// below.</b>
/// </para>
/// <para>
/// <b>One.</b> <see cref="Surface.NormalAt"/> <i>throws</i> at a degenerate parameter rather than
/// returning anything — a sphere's poles and a cone's apex — and it is right to, because there is
/// no normal there. So every property that asks for a normal samples the <i>interior</i> of the
/// domain. Sampling the corners instead would have turned this file red on its first run for a
/// reason that is not a defect.
/// </para>
/// <para>
/// <b>Two, and this one would have passed silently.</b> <see cref="Tessellation.ToMesh"/> with the
/// default tolerance collapses a surface of size 1e-6 to two vertices and no faces at all —
/// correctly, because the default tolerance is absolute and the whole surface is inside it. A mesh
/// with no faces has no naked edges, so <c>Topology.IsClosed</c> answers <b>true</b>: an empty mesh
/// is vacuously watertight. A watertightness property that did not assert a face count first would
/// therefore have been strongest at exactly the scales where it was testing nothing.
/// </para>
/// </remarks>
public sealed class SurfaceProperties
{
    /// <summary>
    /// <b>Transforming a surface and evaluating it commute.</b> One line, and it catches a whole
    /// class of override bug across all eight types at once.
    /// </summary>
    /// <remarks>
    /// Every analytic surface overrides <see cref="Surface.TransformedBy"/> with a closed form
    /// that moves its own defining frame, radius or profile. That is eight opportunities to move
    /// one field and forget another, and the result still looks like a surface — it is simply not
    /// the one that was asked for. Comparing against the transformed points is the assertion that
    /// does not care how the override is written.
    /// </remarks>
    [Fact]
    public void TransformingASurfaceMovesEveryPointOnIt()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            Transform motion = scene.Motion;

            foreach (Surface surface in SurfacesAt(scene))
            {
                Surface moved = surface.TransformedBy(motion);
                double slack = Reach(surface) * 1e-9 * Math.Max(1.0, scene.Factor);

                foreach ((double a, double b) in Fractions())
                {
                    Point3d expected = motion * surface.PointAt(
                        surface.DomainU.Denormalise(a), surface.DomainV.Denormalise(b));
                    Point3d actual = moved.PointAt(
                        moved.DomainU.Denormalise(a), moved.DomainV.Denormalise(b));

                    Assert.True(
                        expected.DistanceTo(actual) <= slack,
                        $"{surface} does not move with its transform at ({a}, {b}).");
                }
            }
        });
    }

    /// <summary>
    /// <b>A point that is already on the surface is its own closest point.</b> The criterion
    /// `E2-T33` names, and the property that found a real defect the day it was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This one samples right up to the boundary, and it is the only one that does.</b> Every
    /// other property here keeps to the interior because <see cref="Surface.NormalAt"/> throws at a
    /// degeneracy; this property never asks for a normal, so it can go where the trouble is — and
    /// the trouble is entirely at the edges. Comparing <i>points</i> rather than parameters is what
    /// makes that safe: at a pole every <c>u</c> names the same place, so the parameters are not
    /// comparable and the point still is.
    /// </para>
    /// <para>
    /// <b>Written first with a uniform interior sample, where it passed against the defect it was
    /// meant to find.</b> `ClosestPoint` used to return a sphere's pole for a point a hair away
    /// from it, and the failure lives inside half a seed cell of the boundary — so a grid of
    /// evenly-spaced fractions steps straight over it. That is `E2-T33`'s own lesson arriving on
    /// schedule: <i>a property whose generator never produces a value near the boundary it is
    /// testing cannot fail, and it looks exactly like a passing test.</i> The fractions below
    /// therefore crowd towards both ends rather than spreading evenly, and the property now goes
    /// red against the old behaviour.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPointOnASurfaceIsItsOwnClosestPoint()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Surface surface in SurfacesAt(scene))
            {
                // A BILLIONTH OF THE SURFACE, AND THE NUMBER IS MEASURED RATHER THAN CHOSEN.
                //
                // Seven of the eight types answer to within 1e-12 of their own reach. The revolution
                // surface here folds: its profile runs tangent to the circle it sweeps at `v = 0`, so
                // the parameterisation is singular along that edge. Its worst is 1.3e-10, a point
                // exactly on the fold, where the distance is quartic in `v` and Newton converges
                // linearly - two thirds of the error per step - until its iterations run out. 1e-9
                // sits outside that, and everything else here is far inside it.
                //
                // It read 1e-3 until `E2-T62` and `E2-T63` (N152). Beside the fold, Newton converged
                // on a saddle 8.5e-5 of the reach away; on it, a line search comparing with the seed
                // refused the one step that mattered and a compass search crept to 1.8e-5. Each has
                // a named example in `SurfaceClosestPointNearDegeneraciesTests`, and at this bound
                // the property catches either of them coming back as well.
                double slack = Reach(surface) * 1e-9;

                foreach ((double a, double b) in FractionsToTheEdge())
                {
                    Point3d on = surface.PointAt(
                        surface.DomainU.Denormalise(a), surface.DomainV.Denormalise(b));

                    double miss = surface.ClosestPoint(on, out _, out _).DistanceTo(on);

                    Assert.True(
                        miss <= slack,
                        $"{surface} does not answer ({a}, {b}) as its own closest point: "
                        + $"out by {miss:e3}, which is {miss / Reach(surface):e3} of its reach.");
                }
            }
        });
    }

    /// <summary>
    /// <b>The answer is never worse than the grid the search seeds itself from.</b> This is the
    /// property that catches a search which stalls instead of converging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Against the seeder's own resolution, and deliberately not a finer one.</b>
    /// <see cref="Surface.ClosestPoint"/> says plainly that it does not promise the <i>global</i>
    /// closest point — it brackets with a coarse grid and refines with Newton, so on a surface with
    /// two comparable minima it can converge into the nearer-looking one. Written first against a
    /// 25×25 sampling, this property found exactly that: a revolution surface where the answer was
    /// 6.6e-5 worse, relatively, than a sample the seeder never took. That is the documented
    /// behaviour, not a defect, and a property asserting otherwise would be a standing demand to
    /// widen the grid.
    /// </para>
    /// <para>
    /// <b>What is left is not tautological, which is the point of keeping it.</b> The claim is that
    /// refinement never makes the seed worse — and until 2026-09-10 it did: beside a sphere's pole
    /// the Jacobian is singular, the first step was refused, and the search returned a grid node
    /// that was out by a sixtieth of the radius while a better one sat next to it. Never-worse is
    /// exactly the invariant that was broken.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheClosestPointIsNeverWorseThanItsSeed()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Surface surface in SurfacesAt(scene))
            {
                Point3d probe = surface.BoundingBox.Center
                    + (scene.Axis * (Reach(surface) * 0.7))
                    + (scene.First * (scene.Scale * 0.3));

                double answered = surface.ClosestPoint(probe, out _, out _).DistanceTo(probe);
                double sampled = double.MaxValue;

                // Sixteen spans in each direction: the grid `ClosestPoint` seeds itself from.
                for (int i = 0; i <= 16; i++)
                {
                    for (int j = 0; j <= 16; j++)
                    {
                        Point3d sample = surface.PointAt(
                            surface.DomainU.Denormalise(i / 16.0),
                            surface.DomainV.Denormalise(j / 16.0));

                        sampled = Math.Min(sampled, sample.DistanceTo(probe));
                    }
                }

                Assert.True(
                    answered <= sampled + (Reach(surface) * 1e-9),
                    $"{surface} answered {answered}, worse than its own seed grid's {sampled}.");
            }
        });
    }

    /// <summary>
    /// <b>Every normal is a unit vector, and it is perpendicular to the surface.</b>
    /// </summary>
    /// <remarks>
    /// Perpendicularity is checked against finite differences rather than against the analytic
    /// derivatives, because half the point is to catch an override whose closed-form normal
    /// disagrees with its own closed-form <c>PointAt</c>. The step is a millionth of the domain and
    /// the tolerance is loosened to match: what is being caught is a normal pointing somewhere
    /// else entirely, not one out in its twelfth digit.
    /// </remarks>
    [Fact]
    public void EveryNormalIsAUnitVectorPerpendicularToTheSurface()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Surface surface in SurfacesAt(scene))
            {
                foreach ((double a, double b) in Fractions())
                {
                    double u = surface.DomainU.Denormalise(a);
                    double v = surface.DomainV.Denormalise(b);

                    Vector3d normal = surface.NormalAt(u, v);
                    Assert.Equal(1.0, normal.Length, 1e-9);

                    Point3d origin = surface.PointAt(u, v);
                    double du = surface.DomainU.Length * 1e-6;
                    double dv = surface.DomainV.Length * 1e-6;

                    Vector3d alongU = surface.PointAt(Math.Min(u + du, surface.DomainU.Max), v) - origin;
                    Vector3d alongV = surface.PointAt(u, Math.Min(v + dv, surface.DomainV.Max)) - origin;

                    if (alongU.Length > 0.0)
                    {
                        Assert.Equal(0.0, normal.Dot(alongU.Normalised()), 1e-4);
                    }

                    if (alongV.Length > 0.0)
                    {
                        Assert.Equal(0.0, normal.Dot(alongV.Normalised()), 1e-4);
                    }
                }
            }
        });
    }

    /// <summary>Every bounding box contains the surface it bounds.</summary>
    [Fact]
    public void EveryBoundingBoxContainsTheSurfaceItBounds()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Surface surface in SurfacesAt(scene))
            {
                BoundingBox box = surface.BoundingBox;
                Tolerance slack = new(
                    Math.Max(Reach(surface) * 1e-9, 1e-300), Angle.FromDegrees(0.001), 1e-12);

                for (int i = 0; i <= 12; i++)
                {
                    for (int j = 0; j <= 12; j++)
                    {
                        Point3d point = surface.PointAt(
                            surface.DomainU.Denormalise(i / 12.0),
                            surface.DomainV.Denormalise(j / 12.0));

                        Assert.True(box.Contains(point, slack), $"{box} does not contain {point}.");
                    }
                }
            }
        });
    }

    /// <summary>
    /// <b>A surface that says it is closed really does meet itself</b>, and one that says it is
    /// not, does not.
    /// </summary>
    /// <remarks>
    /// <c>IsClosedU</c> and <c>IsClosedV</c> are declared by each type rather than derived, and the
    /// tessellator trusts them: a closed direction has its last row of vertices replaced by its
    /// first, which is what keeps a sphere watertight. A type that declared the wrong answer would
    /// produce a mesh with a seam welded shut across a gap, so the declaration is worth checking
    /// against the geometry.
    /// </remarks>
    [Fact]
    public void ClosednessMatchesTheSeam()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Surface surface in SurfacesAt(scene))
            {
                double slack = Reach(surface) * 1e-9;

                for (int step = 1; step < 4; step++)
                {
                    double v = surface.DomainV.Denormalise(step / 4.0);
                    double gapU = surface.PointAt(surface.DomainU.Min, v)
                        .DistanceTo(surface.PointAt(surface.DomainU.Max, v));

                    Assert.Equal(surface.IsClosedU, gapU <= slack);

                    double u = surface.DomainU.Denormalise(step / 4.0);
                    double gapV = surface.PointAt(u, surface.DomainV.Min)
                        .DistanceTo(surface.PointAt(u, surface.DomainV.Max));

                    Assert.Equal(surface.IsClosedV, gapV <= slack);
                }
            }
        });
    }

    /// <summary>
    /// <b>Every tessellation lands on the surface it came from</b>, at a tolerance that scales.
    /// </summary>
    /// <remarks>
    /// <b>The tolerance is a fraction of the surface, not a number.</b> The default is absolute,
    /// and at a scale of 1e-9 the entire surface is inside it — so the mesh comes back as two
    /// vertices and no faces, and every assertion about it becomes vacuous. This is the trap
    /// described on the class, and passing a scaled tolerance is what avoids it; the face count is
    /// asserted as well, so a mesh that collapses anyway is a failure rather than a free pass.
    /// </remarks>
    [Fact]
    public void EveryTessellationLandsOnItsSurface()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            foreach (Surface surface in SurfacesAt(scene))
            {
                double reach = Reach(surface);
                Tolerance sag = new(reach * 0.02, Angle.FromDegrees(1.0), 1e-9);
                Mesh mesh = surface.ToMesh(sag);

                Assert.True(mesh.FaceCount > 0, $"{surface} tessellated to nothing at all.");

                int stride = Math.Max(1, mesh.VertexCount / 12);
                for (int i = 0; i < mesh.VertexCount; i += stride)
                {
                    Point3d vertex = mesh.Vertex(i);

                    Assert.True(
                        surface.ClosestPoint(vertex, out _, out _).DistanceTo(vertex) <= reach * 1e-6,
                        $"{surface} has a mesh vertex that is not on it.");
                }
            }
        });
    }

    /// <summary>
    /// <b>A surface closed in both directions tessellates watertight</b> — no naked edge anywhere.
    /// </summary>
    /// <remarks>
    /// The torus is the one surface here that is closed in <i>both</i> directions, so it is the
    /// only one whose mesh can be watertight without a cap, and it is therefore the only one this
    /// applies to. A sphere is closed in <c>u</c> and bounded by two poles in <c>v</c>; the
    /// tessellator collapses those rows to a single vertex each, which is asserted here too because
    /// a pole emitted as a ring of coincident vertices is the classic way to produce a mesh that
    /// looks closed and has a hole under it.
    /// </remarks>
    [Fact]
    public void AToroidalSurfaceTessellatesWatertight()
    {
        GeometryGenerators.Scenes.Sample(scene =>
        {
            double radius = scene.Scale * 0.5;
            ToroidalSurface torus = new(scene.Plane, radius, radius * 0.25);
            SphericalSurface sphere = new(scene.Plane, radius);

            foreach (Surface surface in new Surface[] { torus, sphere })
            {
                Mesh mesh = surface.ToMesh(
                    new Tolerance(radius * 0.02, Angle.FromDegrees(1.0), 1e-9));

                Assert.True(mesh.FaceCount > 0, $"{surface} tessellated to nothing at all.");
                Assert.Equal(0, mesh.Topology.NonManifoldEdgeCount);
                Assert.Equal(0, mesh.Topology.NakedEdgeCount);
                Assert.True(mesh.Topology.IsClosed, $"{surface} tessellated with a hole in it.");
            }
        });
    }

    /// <summary>
    /// The fractions each property samples the domain at: strictly inside it, never on an edge.
    /// </summary>
    /// <remarks>
    /// <b>A tenth in from each end, because the ends are where the degeneracies are.</b> A sphere's
    /// poles sit exactly on <c>DomainV</c>'s bounds and a cone's apex can sit on either — and
    /// <see cref="Surface.NormalAt"/> throws there rather than answering. Sampling the corners
    /// would make this file red for a reason that is not a defect, and adding a try/catch would
    /// hide the day one of them starts throwing somewhere it should not.
    /// </remarks>
    private static IEnumerable<(double A, double B)> Fractions()
    {
        for (int i = 0; i <= 4; i++)
        {
            for (int j = 0; j <= 4; j++)
            {
                yield return (0.1 + (i * 0.2), 0.1 + (j * 0.2));
            }
        }
    }

    /// <summary>
    /// Fractions that crowd towards both ends of the domain, for the properties that may go there.
    /// </summary>
    /// <remarks>
    /// <b>Geometric rather than uniform, because the interesting region is not uniform.</b> A seed
    /// grid of sixteen spans has cells a sixteenth of the domain wide, and the closest-point defect
    /// this set exists to catch occupies the half-cell beside a degeneracy — about three per cent
    /// of the domain. Nine evenly-spaced fractions miss it; 0.001, 0.01 and 0.99 land squarely in
    /// it. The ends themselves are included, because a pole is a legitimate point on the surface
    /// and it is the one that always worked.
    /// </remarks>
    private static IEnumerable<(double A, double B)> FractionsToTheEdge()
    {
        double[] fractions = [0.0, 0.001, 0.01, 0.25, 0.5, 0.75, 0.99, 0.999, 1.0];

        foreach (double a in fractions)
        {
            foreach (double b in fractions)
            {
                yield return (a, b);
            }
        }
    }

    /// <summary>How big a surface is, for turning a relative tolerance into an absolute one.</summary>
    /// <remarks>
    /// The bounding box's diagonal rather than the scene's scale, because the two are not the same
    /// number: a torus of major radius <c>s/2</c> and a plane of side <c>s</c> are built at one
    /// scale and are different sizes, and a tolerance relative to the wrong one of them is a
    /// different test for each type.
    /// </remarks>
    private static double Reach(Surface surface)
    {
        BoundingBox box = surface.BoundingBox;

        return Math.Max(box.Min.DistanceTo(box.Max), double.Epsilon);
    }

    /// <summary>One of every surface type, built at the scene's working scale.</summary>
    /// <remarks>
    /// <b>Eight types, and the awkward ones are deliberately included rather than the easy ones
    /// repeated.</b> The cone is the only one with an apex it can reach, the torus the only one
    /// closed in both directions, and the revolution and extrusion surfaces the only two built
    /// from a curve rather than from numbers — so a defect in the curve-backed path is found by
    /// two of the eight and by none of the other six.
    /// </remarks>
    private static IEnumerable<Surface> SurfacesAt(Scene scene)
    {
        double scale = scene.Scale;
        double radius = scale * 0.5;
        Plane plane = scene.Plane;
        Point3d origin = plane.Origin;

        Curve profile = new Line(
            origin + (plane.XAxis * (scale * 0.2)),
            origin + (plane.XAxis * (scale * 0.2)) + (plane.YAxis * scale));

        Curve second = new Line(
            origin + (plane.XAxis * scale),
            origin + (plane.XAxis * scale) + (plane.YAxis * (scale * 0.7)));

        yield return new PlaneSurface(plane, scale, scale * 0.75);
        yield return new CylindricalSurface(plane, radius, new Interval(0.0, scale));
        yield return new ConicalSurface(plane, radius, Angle.FromDegrees(20.0), new Interval(0.0, scale));
        yield return new SphericalSurface(plane, radius);
        yield return new ToroidalSurface(plane, radius, radius * 0.25);
        yield return new RevolutionSurface(profile, origin, plane.Normal);
        yield return new RuledSurface(profile, second);
        yield return new ExtrusionSurface(profile, plane.Normal * scale);
    }
}

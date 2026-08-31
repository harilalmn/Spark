using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// The four <c>M1.6</c> criteria that ask what the kernel actually does, rather than whether it
/// works: what a materialisation costs, what it does under threads, what <c>ShapeFix</c> is allowed
/// to change, and whether its mesher is watertight.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these was written before the spike and says a finding either way passes.</b> The
/// only failure available is not asking. So each of these both measures and asserts: the assertion
/// is the loose bound that would catch a regression, and the measurement is printed and recorded in
/// [TASKS.md](../../docs/TASKS.md) where the criterion lives.
/// </para>
/// <para>
/// <b>The bounds are deliberately loose.</b> A timing assertion tight enough to be interesting is
/// tight enough to fail on a busy machine, and a flaky gate teaches people to ignore gates
/// ([N29](../../docs/NOTES.md) makes the same argument about benchmark budgets). What is tight here
/// is the *shape* of the claim — that a materialisation is paid once, that concurrent work on
/// distinct shapes does not corrupt anything — and those do not depend on a clock.
/// </para>
/// </remarks>
public sealed class KernelEnvelopeTests
{
    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    /// <summary>A shape with enough in it to be worth timing: a drilled, filleted plate.</summary>
    private static Brep Realistic(int seed)
    {
        Brep plate = BrepPrimitives.Box(
            Plane.ByOriginXAxisYAxis(new Point3d(0, 0, seed * 0.01), Vector3d.XAxis, Vector3d.YAxis),
            20,
            12,
            2);

        Brep running = plate;

        for (int i = 0; i < 6; i++)
        {
            Brep drill = BrepPrimitives.Cylinder(
                Plane.ByOriginXAxisYAxis(
                    new Point3d(2 + (i * 3), 6, (seed * 0.01) - 1), Vector3d.XAxis, Vector3d.YAxis),
                0.8,
                4);

            running = Kernel.Difference(running, drill, Fine).Value;
        }

        return running;
    }

    // ---------------------------------------------------------------------------------------------
    // M1.6-C4 — what a Materialise costs
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <b><c>M1.6-C4</c>.</b> <see cref="Brep.IsResident"/> makes materialisation lazy;
    /// [ADR-0021](../../docs/adr/0021-brep-kernel-residency.md)'s whole rule rests on it being paid
    /// <i>once</i>. This times the first structural question and then a thousand more.
    /// </summary>
    [NativeFact]
    public void AMaterialisationIsPaidOnceAndTheSecondQuestionIsFree()
    {
        Brep shape = Realistic(0);

        Assert.True(shape.IsResident, "the chain came back as a plain value");

        Stopwatch first = Stopwatch.StartNew();
        int faces = shape.FaceCount;
        first.Stop();

        Stopwatch again = Stopwatch.StartNew();

        for (int i = 0; i < 1000; i++)
        {
            _ = shape.FaceCount;
            _ = shape.EdgeCount;
        }

        again.Stop();

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"M1.6-C4: {faces} faces, {shape.EdgeCount} edges. "
            + $"first materialisation {first.Elapsed.TotalMilliseconds:F2} ms; "
            + $"2000 further questions {again.Elapsed.TotalMilliseconds:F2} ms."));

        // Twelve faces and thirty edges, not more: six drilled holes add six cylindrical faces and
        // put their circles in *inner loops* of the top and bottom planes rather than splitting
        // them. A face count is the wrong measure of how much work a materialisation is; the edge
        // count is closer, and the timing is the actual answer.
        Assert.True(shape.EdgeCount >= 24, $"the shape is not realistic enough to time: {shape.EdgeCount} edges");

        // The claim is *paid once*, and this is what that means in numbers: two thousand further
        // questions cost less than the first one did. A loose bound on the absolute time would say
        // nothing; a bound on the *ratio* is the claim itself.
        Assert.True(
            again.Elapsed < first.Elapsed,
            $"2000 questions took {again.Elapsed.TotalMilliseconds:F2} ms against a first "
            + $"materialisation of {first.Elapsed.TotalMilliseconds:F2} ms — it is not being paid once");
    }

    // ---------------------------------------------------------------------------------------------
    // M1.6-C5 and E13-T14 — the threading envelope
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <b><c>M1.6-C5</c> and <c>E13-T14</c>.</b> The parallel evaluator's real question is whether
    /// it may call the shim from several threads at once on <i>distinct</i> shapes. This runs the
    /// full width of the machine and checks every answer.
    /// </summary>
    /// <remarks>
    /// <b>Distinct shapes, deliberately.</b> The header already says a single handle must not be
    /// used from two threads at once, and that is the conservative half nobody needs evidence for.
    /// What the evaluator needs to know is whether *independent* work is independent, because that
    /// is exactly the shape replication produces: one node, a list of inputs, a value each.
    /// </remarks>
    [NativeFact]
    public void ConcurrentOperationsOnDistinctShapesAgreeWithSequentialOnes()
    {
        int width = Math.Max(4, Environment.ProcessorCount);
        const int Each = 25;

        ConcurrentBag<double> volumes = [];
        ConcurrentBag<string> failures = [];

        Stopwatch clock = Stopwatch.StartNew();

        Parallel.For(0, width, thread =>
        {
            for (int i = 0; i < Each; i++)
            {
                Brep first = BrepPrimitives.Box(
                    Plane.ByOriginXAxisYAxis(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis), 2, 3, 4);
                Brep second = BrepPrimitives.Box(
                    Plane.ByOriginXAxisYAxis(new Point3d(1, 1, 1), Vector3d.XAxis, Vector3d.YAxis), 2, 3, 4);

                KernelResult<Brep> fused = Kernel.Union(first, second, Fine);

                if (!fused.TryGetValue(out Brep? shape))
                {
                    failures.Add($"thread {thread} step {i}: {fused.Diagnostic?.Detail}");
                    continue;
                }

                KernelResult<Mesh> mesh = Kernel.Tessellate(shape, Fine);

                if (!mesh.TryGetValue(out Mesh? triangles))
                {
                    failures.Add($"thread {thread} step {i} mesh: {mesh.Diagnostic?.Detail}");
                    continue;
                }

                volumes.Add(triangles.Volume());
            }
        });

        clock.Stop();

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"M1.6-C5: {width} threads x {Each} union+tessellate = {volumes.Count} results in "
            + $"{clock.Elapsed.TotalSeconds:F2} s, {failures.Count} failure(s)."));

        Assert.Empty(failures);
        Assert.Equal(width * Each, volumes.Count);

        // Every one of them is the same shape, so every one of them is 42. A race that corrupted a
        // shared table would show up here as a wrong number, not only as a crash.
        Assert.All(volumes, volume => Assert.Equal(42.0, volume, 1));
    }

    /// <summary>
    /// The thread-local half of the error channel, checked rather than assumed: two threads failing
    /// at once must each read their own reason.
    /// </summary>
    [NativeFact]
    public void TwoThreadsFailingAtOnceEachReadTheirOwnReason()
    {
        ConcurrentBag<string> details = [];

        Parallel.For(0, Math.Max(4, Environment.ProcessorCount), _ =>
        {
            for (int i = 0; i < 20; i++)
            {
                KernelResult<Brep> refused = Kernel.Fillet(
                    BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1), [], 10.0, Fine);

                Assert.False(refused.IsSuccess);
                details.Add(refused.Diagnostic!.Detail ?? string.Empty);
            }
        });

        // If the error string were process-wide, some of these would be empty or would be some
        // other thread's message.
        Assert.All(details, detail => Assert.Contains("radius", detail, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------------------------
    // M1.6-C6 — what ShapeFix is allowed to change
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <b><c>M1.6-C6</c>.</b> ADR-0021 argues that a round trip through a tolerant kernel is not
    /// identity, and rests part of its case on <c>ShapeFix</c> being free to re-tolerance and
    /// re-parameterise. The question was whether it can be constrained to a policy we choose.
    /// </summary>
    /// <remarks>
    /// <b>What this measures is the part that can be measured: what it changes on a shape that
    /// needs nothing.</b> A healer that rewrites a healthy box is one that cannot be trusted on the
    /// import path, which is where it now sits.
    /// </remarks>
    [NativeFact]
    public void HealingAShapeThatNeedsNothingChangesNothingThatCanBeSeen()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Brep healed = Kernel.Heal(box, Fine).Value;

        Assert.Equal(6, healed.FaceCount);
        Assert.Equal(12, healed.EdgeCount);
        Assert.Equal(8, healed.VertexCount);
        Assert.Equal(1, healed.ShellCount);

        // Every surface is still a plane, at the same places.
        Assert.All(healed.Surfaces(), surface => Assert.IsType<PlaneSurface>(surface));
        Assert.Equal(24.0, Kernel.Tessellate(healed, Fine).Value.Volume(), 6);

        // And the eight corners are the eight corners, to the last bit anybody would check.
        Point3d[] corners = [.. healed.Points().OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z)];

        Assert.Equal(8, corners.Length);
        Assert.Equal(0.0, corners[0].DistanceTo(Point3d.Origin), 9);
        Assert.Equal(0.0, corners[7].DistanceTo(new Point3d(2, 3, 4)), 9);
    }

    /// <summary>And a healed shape is valid by the kernel's own checker, which is the point of it.</summary>
    [NativeFact]
    public void AHealedShapeIsValidByTheKernelsOwnChecker()
    {
        Brep healed = Kernel.Heal(BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0), Fine).Value;

        Assert.Equal(string.Empty, OcctBrepKernel.Check(healed));
    }

    // ---------------------------------------------------------------------------------------------
    // E13-T11 — NFR-8, answered rather than suppressed
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <b><c>E13-T11</c>'s NFR-8 question.</b> Spark's own tessellator was written to produce a
    /// watertight mesh and asserts it. OpenCascade's is a third party's and guarantees nothing at
    /// default deflection. The requirement says: either the property holds at a deflection we
    /// choose, or the requirement is restated to say what it actually guarantees.
    /// </summary>
    /// <remarks>
    /// <b>Whichever way this comes out, the answer belongs in the requirement rather than in a
    /// suppression.</b> A test that measured watertightness and then asserted nothing would be the
    /// worst of both.
    /// </remarks>
    [NativeFact]
    public void TheProvidersMeshIsClosedForTheShapesM6Ships()
    {
        (string Name, Brep Shape)[] shapes =
        [
            ("box", BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4)),
            ("cylinder", BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0)),
            ("union", Kernel.Union(
                BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4),
                BrepPrimitives.Box(
                    Plane.ByOriginXAxisYAxis(new Point3d(1, 1, 1), Vector3d.XAxis, Vector3d.YAxis),
                    2,
                    3,
                    4),
                Fine).Value),
            ("drilled plate", Realistic(0)),
        ];

        foreach ((string name, Brep shape) in shapes)
        {
            Mesh mesh = Kernel.Tessellate(shape, new Tolerance(0.01, Angle.FromDegrees(2), 1e-12)).Value;
            Mesh welded = mesh.Welded();

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"NFR-8: {name} — as meshed {mesh.VertexCount} vertices, "
                + $"{mesh.Topology.NakedEdges().Length} naked edge(s), closed = {mesh.Topology.IsClosed}; "
                + $"welded {welded.VertexCount} vertices, "
                + $"{welded.Topology.NakedEdges().Length} naked edge(s), closed = {welded.Topology.IsClosed}."));

            // AS MESHED IT IS NOT CLOSED, AND THAT IS THE ANSWER RATHER THAN THE FAILURE. Every
            // kernel tessellates a BRep face by face — ours and OpenCascade's alike — so a shared
            // edge gets two copies of every vertex, one per face. Nothing leaks: the copies are at
            // the same place. But `IsClosed` counts edges, so a sound box reports naked ones.
            Assert.False(
                mesh.Topology.IsClosed,
                $"the {name} meshed closed, which would mean the split stopped happening");

            // Welded, it closes. That is the property NFR-8 wanted, available where it is needed —
            // a volume, an STL, a printer — and not imposed on the shading, because a welded
            // corner has one normal and a box with one normal per corner shades like a ball.
            Assert.True(
                welded.Topology.IsClosed,
                $"the welded mesh of the {name} has {welded.Topology.NakedEdges().Length} naked edge(s)");
        }
    }
}

using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// A tessellation is made at the tolerance it was asked for (`E12-T20`, [N87](../../docs/NOTES.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect was a cache nobody wrote.</b> The shim meshed what it called a copy of the shape,
/// but assigning a <c>TopoDS_Shape</c> shares its faces, so the triangulation went into the caller's
/// shape — and the mesher keeps an existing triangulation finer than the one asked for. A shape's
/// first tessellation was therefore every later one: 1,099,460 triangles for a coarse request whose
/// fresh answer was 1,332.
/// </para>
/// <para>
/// <b>Every shape here is kernel-held, and each test says so.</b> A managed shape is imported afresh
/// on every call and so could never show the defect; a test built on one would pass either way.
/// </para>
/// </remarks>
public sealed class TessellationToleranceTests
{
    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Healing => new(1e-6, Angle.FromDegrees(1), 1e-12);

    private static Tolerance FineMesh => new(0.001, Angle.FromDegrees(0.5), 1e-12);

    private static Tolerance CoarseMesh => new(0.1, Angle.FromDegrees(30), 1e-12);

    /// <summary>
    /// <b>The row.</b> A coarse request after a fine one on the same shape is exactly as coarse as a
    /// coarse request on a shape that was never meshed.
    /// </summary>
    [NativeFact]
    public void ACoarseRequestAfterAFineOneIsAsCoarseAsOnAFreshShape()
    {
        Brep used = HeldCylinder();

        int fine = Kernel.Tessellate(used, FineMesh).Value.FaceCount;
        int coarseAfterFine = Kernel.Tessellate(used, CoarseMesh).Value.FaceCount;
        int coarseFresh = Kernel.Tessellate(HeldCylinder(), CoarseMesh).Value.FaceCount;

        Assert.True(coarseFresh < fine, $"the two tolerances did not differ: {coarseFresh} and {fine} triangles");
        Assert.Equal(coarseFresh, coarseAfterFine);
    }

    /// <summary>
    /// The other order, which the mesher always got right — it re-meshes a triangulation coarser
    /// than asked for — and which stays asserted so that a fix for one direction cannot break the
    /// other.
    /// </summary>
    [NativeFact]
    public void AFineRequestAfterACoarseOneIsAsFineAsOnAFreshShape()
    {
        Brep used = HeldCylinder();

        _ = Kernel.Tessellate(used, CoarseMesh);
        int fineAfterCoarse = Kernel.Tessellate(used, FineMesh).Value.FaceCount;
        int fineFresh = Kernel.Tessellate(HeldCylinder(), FineMesh).Value.FaceCount;

        Assert.Equal(fineFresh, fineAfterCoarse);
    }

    /// <summary>
    /// <b>The same request twice is served from the cache</b>, which is what keeps a correct answer
    /// from being a slow one: the viewport tessellates every solid on every scene rebuild.
    /// </summary>
    [NativeFact]
    public void TheSameRequestTwiceReturnsTheSameMesh()
    {
        Brep shape = HeldCylinder();

        Mesh first = Kernel.Tessellate(shape, CoarseMesh).Value;
        Mesh second = Kernel.Tessellate(shape, CoarseMesh).Value;

        Assert.Same(first, second);
    }

    /// <summary>
    /// <b>A shape with trimmed faces keeps its volume when it is meshed again at a new tolerance.</b>
    /// A later tolerance meshes a fresh import of the shape read back out, and a boolean's trimmed
    /// faces are the hardest thing that round trip carries — so the risk of the route is asserted
    /// rather than assumed.
    /// </summary>
    [NativeFact]
    public void AShapeWithTrimmedFacesKeepsItsVolumeWhenMeshedAgain()
    {
        Brep cut = Held(Kernel.Difference(
            BrepPrimitives.Box(Plane.WorldXY, 4, 4, 4),
            BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 8.0),
            Healing).Value);

        Mesh fine = Kernel.Tessellate(cut, FineMesh).Value;
        Mesh coarse = Kernel.Tessellate(cut, CoarseMesh).Value;

        Assert.True(coarse.FaceCount < fine.FaceCount, $"{coarse.FaceCount} coarse against {fine.FaceCount} fine");
        Assert.Equal(fine.Volume(), coarse.Volume(), fine.Volume() * 0.02);
    }

    /// <summary>A cylinder the kernel holds, which is the only kind of shape that ever showed the defect.</summary>
    private static Brep HeldCylinder() =>
        Held(Kernel.Heal(BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0), Healing).Value);

    private static Brep Held(Brep shape)
    {
        Assert.True(shape.IsResident, "the shape is not kernel-held, so this test would pass with the defect present");

        return shape;
    }
}

using System.Collections.Generic;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// The `Solid` node family against the real provider, and the face-index defect underneath it
/// (`E13-T22`).
/// </summary>
/// <remarks>
/// <para>
/// <b>This project gained a reference to <c>Spark.Nodes.Core</c> to have these.</b> The node library
/// is exercised everywhere else <i>without</i> a provider, where every kernel operation correctly
/// refuses — so a node that named the wrong face would have been invisible to the whole suite.
/// `ADR-0005`'s constraint is about what <c>Spark.Nodes.Core</c> may reference and is untouched by
/// who references it.
/// </para>
/// <para>
/// <b>Measured by volume, because volume is what separates a right answer from a plausible one.</b>
/// A face count says a shell was made; only the volume says <i>which</i> face was opened, and only
/// on a block that is not a cube.
/// </para>
/// </remarks>
public sealed class SolidNodeTests
{
    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    /// <summary>Opening a face perpendicular to z: the cavity runs the full height.</summary>
    private const double OpenedAlongZ = 54.0 - (2.2 * 2.2 * 5.6);

    /// <summary>Opening a face perpendicular to x or y: the cavity runs the full depth instead.</summary>
    private const double OpenedSideways = 54.0 - (2.2 * 2.6 * 5.2);

    private static Brep Block() =>
        Nodes.Core.Solid.Box(
            Plane.FromOriginXAxisYAxis(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis), 3, 3, 6);

    /// <summary>
    /// <b><see cref="Nodes.Core.Solid.Hollow"/> seals the void, which is faithful and invisible.</b>
    /// Dynamo's <c>ThinShell</c> takes no face list either, so a sealed shell is the right answer —
    /// but its outside is the outside of the original block, so nothing on screen can tell whether it
    /// ran. This is the assertion that can, and until 2026-09-12 the suite had none: both existing
    /// <c>Shell</c> call sites passed an empty face list and checked only that a wall appeared.
    /// </summary>
    [NativeFact]
    public void HollowingSealedLeavesTheOutsideAlone()
    {
        // 3 x 3 x 6, less a 2.2 x 2.2 x 5.2 cavity inset on all six sides.
        Assert.Equal(54.0 - (2.2 * 2.2 * 5.2), Volume(Nodes.Core.Solid.Hollow(Block(), -0.4)), 3);
    }

    /// <summary>
    /// <b>A FACE INDEX DOES NOT SURVIVE THE CROSSING INTO OCCT, AND THIS TEST PINS IT (`E13-T22`).</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The managed <see cref="Brep"/> numbers its faces in the order <see cref="BrepPrimitives.Box"/>
    /// wrote them; OCCT numbers them in its own explorer order, and
    /// <see cref="Spark.Api.IBrepKernel.Shell"/> passes the caller's indices straight through. **So
    /// asking to open face 1 — the top, normal +Z — opens a side.** The same applies to
    /// <c>Fillet</c>, <c>Chamfer</c> and <c>Draft</c>, which all take index lists.
    /// </para>
    /// <para>
    /// <b>Nothing shipped is wrong today</b>, which is why this went unnoticed: every caller in the
    /// product passes an <i>empty</i> list, meaning *all of them*, and a permutation of a whole set is
    /// that set. <c>FilletAll</c> rounds every edge; <c>Hollow</c> opens none. The defect is reachable
    /// only through <see cref="Spark.Api.IBrepKernel"/> directly.
    /// </para>
    /// <para>
    /// <b>It is pinned rather than fixed because the fix is blocked.</b> Matching the two orderings
    /// needs the shim to expose a stable face identity, and the shim cannot be rebuilt while the
    /// OpenCascade install is missing (`E13-T21`). <b>This test is expected to go RED when `E13-T22`
    /// is fixed</b> — that is its whole purpose, and the fixer should delete it and write the
    /// positive assertions in its place.
    /// </para>
    /// </remarks>
    [NativeFact]
    public void AFaceIndexDoesNotSurviveTheCrossingIntoOcct()
    {
        Brep block = Block();

        // What the managed model says: face 0 is the bottom, face 1 the top, the rest are sides.
        Assert.Equal(-1.0, NormalOf(block, 0).Z, 3);
        Assert.Equal(1.0, NormalOf(block, 1).Z, 3);

        // What the kernel does with those same indices. Face 1 is the top and opening it should
        // give OpenedAlongZ; it gives the sideways figure instead.
        Assert.Equal(OpenedAlongZ, ShellOpening(block, 0), 3);
        Assert.Equal(OpenedSideways, ShellOpening(block, 1), 3);

        // And face 2, which the managed model calls a side, behaves like a z-face.
        Assert.Equal(0.0, NormalOf(block, 2).Z, 3);
        Assert.Equal(OpenedAlongZ, ShellOpening(block, 2), 3);
    }

    /// <summary>
    /// <b>Exactly two of the six faces behave like z-faces, which says this is a permutation and not
    /// corruption.</b> The kernel is opening a real face of the real solid every time — just not the
    /// one it was asked for — so `E13-T22` is a mapping to be discovered, not a shape to be repaired.
    /// </summary>
    [NativeFact]
    public void TheMismatchIsAPermutationAndNotCorruption()
    {
        Brep block = Block();
        int alongZ = 0;

        for (int index = 0; index < block.FaceCount; index++)
        {
            double volume = ShellOpening(block, index);

            Assert.True(
                System.Math.Abs(volume - OpenedAlongZ) < 1e-3 || System.Math.Abs(volume - OpenedSideways) < 1e-3,
                $"face {index} shelled to {volume}, which is neither of the two volumes a box can give");

            if (System.Math.Abs(volume - OpenedAlongZ) < 1e-3)
            {
                alongZ++;
            }
        }

        Assert.Equal(2, alongZ);
    }

    private static Vector3d NormalOf(Brep solid, int face)
    {
        BrepFaceView view = solid.Face(face);

        return view.NormalAt(view.Surface.DomainU.Mid, view.Surface.DomainV.Mid);
    }

    private static double ShellOpening(Brep solid, int face) =>
        Volume(NativeProvider.Kernel.Shell(solid, [face], -0.4, Fine).Value);

    private static double Volume(Brep solid) =>
        NativeProvider.Kernel.Tessellate(solid, Fine).Value.Volume();
}

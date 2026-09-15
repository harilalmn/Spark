using System;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// What <see cref="Nodes.Core.Solid.Volume"/> costs by measuring the tessellation instead of the
/// faces, in numbers rather than in prose (<c>E2-T67</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The row wants the exact answer and cannot have it yet.</b> <c>BRepGProp</c> integrates over
/// the analytic faces and is what a kernel volume means; the <c>spark_occt</c> shim exports nothing
/// of the sort, and the shim cannot be rebuilt on this machine — the OpenCascade install is gone
/// (<c>E13-T21</c>) and <c>scripts/build-native.ps1</c> refuses, having searched all three vcpkg
/// roots. So the row is blocked on the same reinstall as <c>E13-T18</c>.
/// </para>
/// <para>
/// <b>What is not blocked is the claim the current implementation makes about itself.</b>
/// <c>Solid.Volume</c>'s remarks say the mesh volume <i>approaches the true one from below</i>.
/// That is a statement about the <b>sign</b> of an error, it was written without being measured,
/// and it is the kind of claim this project keeps finding stale. These tests state the closed form
/// — πr²h, and π(R² − r²)h for the tube — and assert the direction and the magnitude, so that the
/// defect is a number somebody can weigh rather than a sentence somebody can skim.
/// </para>
/// <para>
/// <b>The measured answer moved the row's own argument.</b> The remark's <i>from below</i> is
/// right, on every shape here. But the error at the node default is <b>1.3e-5</b> relative, which
/// is far smaller than the sentence *measured on the tessellation* leads a reader to expect — so
/// the case for <c>E2-T67</c> is <b>not</b> accuracy. It is that the answer is a function of a
/// meshing tolerance the caller never passed and cannot see, and no tightening makes it stop
/// moving:
/// <see cref="TheAnswerMovesWhenTheToleranceMovesAndTheCallerCannotSeeTheKnob"/> is that test, and
/// it replaced one asserting the opposite of what measurement showed.
/// </para>
/// </remarks>
public sealed class MassPropertyAccuracyTests
{
    private const double Radius = 2.0;

    private const double Height = 5.0;

    /// <summary>The tolerance <see cref="Nodes.Core.Solid.ToMesh"/> uses when nobody says.</summary>
    /// <remarks>
    /// Taken from the node's own default rather than chosen here, because the figure worth knowing
    /// is the error a user gets, and a user gets the default.
    /// </remarks>
    private static Tolerance NodeDefault => new(0.01, Angle.FromDegrees(1), 1e-12);

    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Plane Base => Plane.FromOriginXAxisYAxis(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis);

    /// <summary>
    /// <b>A cylinder is under-reported, and the documented direction holds.</b> The tessellation
    /// inscribes the curved face — every vertex sits on the surface and every chord cuts the corner
    /// — so the prism it builds is strictly inside the cylinder.
    /// </summary>
    [NativeFact]
    public void ACylinderIsUnderReportedByTheChordError()
    {
        double exact = Math.PI * Radius * Radius * Height;
        double measured = MeshVolume(BrepPrimitives.Cylinder(Base, Radius, Height));

        Assert.True(
            measured < exact,
            $"the mesh volume {measured} was not below the exact {exact}; Solid.Volume's remarks say it approaches from below.");

        // The bound is loose on purpose: it pins the ORDER of the error, which is the useful fact,
        // and does not pin the mesher's exact face count, which is OCCT's business and moves with
        // its version.
        Assert.InRange((exact - measured) / exact, 1e-5, 1e-2);
    }

    /// <summary>
    /// <b>A tube loses twice, because it has two curved faces.</b> The outer wall is inscribed and
    /// the inner wall is circumscribed — the bore's chords cut into the *material*, not into the
    /// void — so both errors take volume away and the relative error is worse than the solid
    /// cylinder's rather than cancelling against it.
    /// </summary>
    /// <remarks>
    /// <b>A sphere would be the better second case and there is no sphere to use.</b>
    /// <c>BrepPrimitives</c> has only a box and a cylinder (<c>E2-T66</c>), so a doubly curved
    /// solid cannot be built here without the kernel making one. A tube is the nearest available
    /// shape whose volume is still closed form: π(R² − r²)h, and no quadrature to be wrong about.
    /// </remarks>
    [NativeFact]
    public void ATubeLosesMoreThanASolidCylinderBecauseBothWallsAreCurved()
    {
        const double Bore = 1.2;

        double exactTube = Math.PI * ((Radius * Radius) - (Bore * Bore)) * Height;
        double exactCylinder = Math.PI * Radius * Radius * Height;

        Brep tube = Nodes.Core.Solid.Difference(
            BrepPrimitives.Cylinder(Base, Radius, Height),
            BrepPrimitives.Cylinder(Base, Bore, Height));

        double measuredTube = MeshVolume(tube);
        double measuredCylinder = MeshVolume(BrepPrimitives.Cylinder(Base, Radius, Height));

        Assert.True(
            measuredTube < exactTube,
            $"the mesh volume {measuredTube} was not below the exact {exactTube}; Solid.Volume's remarks say it approaches from below.");

        Assert.True(
            (exactTube - measuredTube) / exactTube > (exactCylinder - measuredCylinder) / exactCylinder,
            $"the tube's relative error {(exactTube - measuredTube) / exactTube} was not worse than the solid "
            + $"cylinder's {(exactCylinder - measuredCylinder) / exactCylinder}; two curved walls should lose more than one.");
    }

    /// <summary>
    /// <b>A box is exact, and that is why the defect goes unnoticed.</b> A planar-faced solid is
    /// represented by its tessellation without loss, so every test and every demo built on boxes
    /// agrees with the closed form to the last few bits — and none of them can see the error the
    /// two tests above measure.
    /// </summary>
    [NativeFact]
    public void ABoxIsExactWhichIsWhyTheDefectHides()
    {
        double measured = MeshVolume(BrepPrimitives.Box(Base, 3.0, 4.0, 5.0));

        Assert.Equal(60.0, measured, 9);
    }

    /// <summary>
    /// <b>THE DEFECT IS NOT THE SIZE OF THE ERROR — IT IS THAT THE ANSWER MOVES.</b> The same
    /// solid, asked for its volume twice, gives two different numbers, because what is actually
    /// measured is a mesh and the mesh is a function of a tolerance the caller never mentioned.
    /// <see cref="Nodes.Core.Solid.Volume"/> takes no tolerance parameter at all, so a user cannot
    /// even see the knob their answer depends on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test replaced one that asserted the opposite of what is true.</b> It was written
    /// expecting the volume error to be far <i>larger</i> than the tolerance asked for — the
    /// reasoning being that the tolerance bounds a distance and the volume error integrates that
    /// distance over the whole face. Measured, the cylinder's error at the node default is
    /// <b>0.000797</b> against a tolerance of <b>0.01</b>: an order of magnitude the other way, and
    /// a relative error of 1.3e-5. The reasoning ignored that OCCT also honours a one-degree
    /// angular deflection, which on a radius of two is the binding constraint and is far finer than
    /// the linear one.
    /// </para>
    /// <para>
    /// <b>So the case for <c>E2-T67</c> is not accuracy, and saying otherwise would oversell it.</b>
    /// 1.3e-5 is below what most models care about. The case is determinism: a quantity that a user
    /// reads as a property of their solid is a property of a *rendering* parameter, it changes when
    /// the viewport's tessellation quality changes, and no amount of tightening makes it stop
    /// moving. <c>BRepGProp</c> integrates the faces and does not have a tolerance to be a function
    /// of.
    /// </para>
    /// </remarks>
    [NativeFact]
    public void TheAnswerMovesWhenTheToleranceMovesAndTheCallerCannotSeeTheKnob()
    {
        double exact = Math.PI * Radius * Radius * Height;
        Brep cylinder = BrepPrimitives.Cylinder(Base, Radius, Height);

        double coarse = Volume(cylinder, new Tolerance(0.5, Angle.FromDegrees(30), 1e-12));
        double fine = Volume(BrepPrimitives.Cylinder(Base, Radius, Height), new Tolerance(1e-4, Angle.FromDegrees(0.1), 1e-12));

        // Both are wrong in the documented direction, and by different amounts. That difference is
        // the whole finding: it is not measurement noise, it is two different answers to the same
        // question.
        Assert.True(coarse < exact, $"the coarse volume {coarse} was not below the exact {exact}.");
        Assert.True(fine < exact, $"the fine volume {fine} was not below the exact {exact}.");

        // MEASURED 2026-09-15, r=2 h=5, exact 62.83185307:
        //   coarse (0.5, 30 deg)   62.11657082   1.14% low
        //   default (0.01, 1 deg)  62.83105559   1.27e-5 low
        //   fine (1e-4, 0.1 deg)   62.83184510   1.27e-7 low
        // The bound below is 0.01 against a measured gap of 0.715 - two orders of margin, because
        // the assertion is about the answer MOVING and not about how far.
        Assert.True(
            fine - coarse > 0.01,
            $"the two tessellations agreed to within {fine - coarse}; if they no longer disagree, the "
            + "mesher changed and this row's premise should be re-read rather than the bound relaxed.");
    }

    private static double MeshVolume(Brep solid) => Volume(solid, NodeDefault);

    /// <summary>The volume of the mesh a solid makes at a given tolerance.</summary>
    /// <remarks>
    /// <b>Pass a freshly built solid for each tolerance.</b> A kernel-held shape keeps the finer of
    /// the triangulations it has been asked for, which is `E12-T20`'s defect the right way round —
    /// re-measuring one shape at two tolerances would silently compare a mesh with itself.
    /// </remarks>
    private static double Volume(Brep solid, Tolerance tolerance) =>
        Kernel.Tessellate(solid, tolerance).Value.Volume();
}

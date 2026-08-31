using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// Sweep and patch, and the encoding change that made a polycurve exact.
/// </summary>
/// <remarks>
/// <b>A profile is a wire, not a curve.</b> The first version of the profile encoding made one
/// wire per curve, which forced a <see cref="PolyCurve"/> through an *interpolating* NURBS
/// conversion — an approximation, for a shape whose every piece was exactly representable. The
/// loop table already had a way to say "these edges are one circuit"; it was simply not being
/// read. These tests are what would have caught that, and what will catch it coming back.
/// </remarks>
public sealed class SweepAndPatchTests
{
    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    [NativeFact]
    public void TheProviderClaimsSweep()
    {
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Sweep));
    }

    /// <summary>
    /// A circle swept along a straight line is a cylinder, which is the one sweep whose answer is
    /// known in closed form — so it is the one worth asserting a volume on.
    /// </summary>
    [NativeFact]
    public void ACircleSweptAlongALineIsACylinder()
    {
        Circle profile = new(Plane.WorldXY, 1.5);
        Line rail = new(Point3d.Origin, new Point3d(0, 0, 6));

        KernelResult<Brep> swept = Kernel.Sweep(profile, rail, cap: true, Fine);

        Assert.True(swept.IsSuccess, swept.Diagnostic?.Detail);
        Assert.Equal(Math.PI * 1.5 * 1.5 * 6.0, Kernel.Tessellate(swept.Value, Fine).Value.Volume(), 1);
    }

    /// <summary>A sweep along a curved rail is a pipe, and it has more than three faces.</summary>
    [NativeFact]
    public void ACircleSweptAlongAnArcIsABend()
    {
        Circle profile = new(Plane.WorldXY, 0.5);

        // A quarter turn in the XZ plane, starting where the profile is.
        Arc rail = Arc.ByPlaneRadiusAngles(
            Plane.ByOriginXAxisYAxis(new Point3d(4, 0, 0), Vector3d.XAxis, Vector3d.ZAxis),
            4.0,
            Angle.FromDegrees(180),
            Angle.FromDegrees(90));

        KernelResult<Brep> bend = Kernel.Sweep(profile, rail, cap: true, Fine);

        Assert.True(bend.IsSuccess, bend.Diagnostic?.Detail);

        Mesh mesh = Kernel.Tessellate(bend.Value, Fine).Value;

        // A quarter of a torus: 2 pi^2 R r^2, for R = 4 and r = 0.5, divided by four.
        Assert.Equal(2.0 * Math.PI * Math.PI * 4.0 * 0.25 / 4.0, mesh.Volume(), 1);
    }

    /// <summary>
    /// <b>The reason the encoding changed.</b> A polycurve's segments go out as themselves; every
    /// one is an exact line or arc, and nothing is interpolated. A square drawn as four lines
    /// extrudes into a box whose volume is exactly its area times its height.
    /// </summary>
    [NativeFact]
    public void APolyCurveProfileIsExactRatherThanInterpolated()
    {
        PolyLine square = new(
        [
            new Point3d(0, 0, 0),
            new Point3d(3, 0, 0),
            new Point3d(3, 2, 0),
            new Point3d(0, 2, 0),
            new Point3d(0, 0, 0),
        ]);

        KernelResult<Brep> block = Kernel.Extrude(square, new Vector3d(0, 0, 5), cap: true, Fine);

        Assert.True(block.IsSuccess, block.Diagnostic?.Detail);

        // Six faces: an interpolating spline through the corners would have one curved wall and
        // several more faces, and would not measure 30 either.
        Assert.Equal(6, block.Value.FaceCount);
        Assert.Equal(30.0, Kernel.Tessellate(block.Value, Fine).Value.Volume(), 6);

        // And every surface is a plane, which a spline profile could not produce.
        Assert.All(block.Value.Surfaces(), surface => Assert.IsType<PlaneSurface>(surface));
    }

    /// <summary>The same, for a chain of mixed segment kinds rather than lines alone.</summary>
    [NativeFact]
    public void APolyCurveOfMixedSegmentsSweepsWithoutApproximation()
    {
        PolyCurve chain = PolyCurve.ByJoinedCurves(
        [
            new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)),
            Arc.ByPlaneRadiusAngles(
                Plane.ByOriginXAxisYAxis(new Point3d(4, 1, 0), Vector3d.XAxis, Vector3d.YAxis),
                1.0,
                Angle.FromDegrees(-90),
                Angle.FromDegrees(90)),
            new Line(new Point3d(5, 1, 0), new Point3d(5, 4, 0)),
        ]);

        KernelResult<Brep> wall = Kernel.Extrude(chain, new Vector3d(0, 0, 2), cap: false, Fine);

        Assert.True(wall.IsSuccess, wall.Diagnostic?.Detail);

        // Three segments, three faces: two planes and one cylinder. An interpolated profile would
        // have produced one NURBS face and no cylinder at all.
        Assert.Equal(3, wall.Value.FaceCount);
        Assert.Equal(2, wall.Value.Surfaces().OfType<PlaneSurface>().Count());
        Assert.Single(wall.Value.Surfaces().OfType<CylindricalSurface>());
    }

    // ---------------------------------------------------------------------------------------------
    // Patch
    // ---------------------------------------------------------------------------------------------

    /// <summary>A square boundary fills with a surface that spans it.</summary>
    [NativeFact]
    public void AClosedBoundaryFillsWithASurface()
    {
        List<Curve> boundary =
        [
            new Line(new Point3d(0, 0, 0), new Point3d(4, 0, 0)),
            new Line(new Point3d(4, 0, 0), new Point3d(4, 4, 1)),
            new Line(new Point3d(4, 4, 1), new Point3d(0, 4, 0)),
            new Line(new Point3d(0, 4, 0), new Point3d(0, 0, 0)),
        ];

        KernelResult<Brep> patch = Kernel.Patch(boundary, Fine);

        Assert.True(patch.IsSuccess, patch.Diagnostic?.Detail);
        Assert.Equal(1, patch.Value.FaceCount);

        // It is a sheet, not a solid: a patch encloses nothing.
        Assert.False(patch.Value.IsSolid);
    }

    /// <summary>An empty boundary is refused by name rather than producing an empty face.</summary>
    [NativeFact]
    public void AnEmptyBoundaryIsRefused()
    {
        KernelResult<Brep> patch = Kernel.Patch([], Fine);

        Assert.False(patch.IsSuccess);
        Assert.Equal(KernelDiagnostics.Refused, patch.Diagnostic!.Code);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// The provider, end to end. <c>M1.6-C2</c> lives in <see cref="TwoOverlappingBoxesFuse"/>.
/// </summary>
public sealed class OcctBrepKernelTests
{
    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    private static Brep Box(double x, double y, double z, double length, double width, double height) =>
        BrepPrimitives.Box(
            Plane.ByOriginXAxisYAxis(new Point3d(x, y, z), Vector3d.XAxis, Vector3d.YAxis),
            length,
            width,
            height);

    [NativeFact]
    public void TheProviderIsInstalledAndSaysWhatItCanDo()
    {
        Assert.Equal("opencascade", Kernel.Name);
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Boolean));
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Fillet));
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Tessellate));

        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Split));
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Offset));
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Step));

        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Sweep));

        // Not claimed, and the absence is the assertion: a capability flag is what the node
        // library greys operations out on, so claiming one the ABI cannot do is worse than
        // claiming nothing. `MeshBoolean` is E2's work and is deferred to 1.x.
        Assert.False(Kernel.Capabilities.HasFlag(BrepCapabilities.MeshBoolean));
    }

    /// <summary>
    /// <b><c>M1.6-C2</c>.</b> One boolean, end to end: two managed boxes in, one exact solid out,
    /// with the right volume and the right topology. If this test cannot be made to pass,
    /// ADR-0020 is the decision that has to be reopened.
    /// </summary>
    [NativeFact]
    public void TwoOverlappingBoxesFuse()
    {
        Brep first = Box(0, 0, 0, 2, 3, 4);
        Brep second = Box(1, 1, 1, 2, 3, 4);

        KernelResult<Brep> result = Kernel.Union(first, second, Fine);

        Assert.True(result.TryGetValue(out Brep? fused), result.Diagnostic?.Message);
        Assert.NotNull(fused);

        // 24 + 24 - the 1x2x3 overlap.
        Mesh mesh = Kernel.Tessellate(fused, Fine).Value;
        Assert.Equal(42.0, mesh.Volume(), 1);

        // More faces than either box had, because the union has a step in it.
        Assert.True(fused.FaceCount > 6, $"the union has {fused.FaceCount} faces");
    }

    [NativeFact]
    public void ADifferenceTakesTheSecondSolidOut()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);
        Brep bite = Box(1, 1, 3, 2, 2, 2);

        Brep cut = Kernel.Difference(block, bite, Fine).Value;
        Mesh mesh = Kernel.Tessellate(cut, Fine).Value;

        // 64 minus the 2x2x1 that was actually inside.
        Assert.Equal(60.0, mesh.Volume(), 1);
    }

    [NativeFact]
    public void AnIntersectionKeepsOnlyTheOverlap()
    {
        Brep first = Box(0, 0, 0, 2, 3, 4);
        Brep second = Box(1, 1, 1, 2, 3, 4);

        Brep common = Kernel.Intersection(first, second, Fine).Value;
        Mesh mesh = Kernel.Tessellate(common, Fine).Value;

        Assert.Equal(6.0, mesh.Volume(), 1);
        Assert.Equal(6, common.FaceCount);
    }

    /// <summary>
    /// <b>A refusal is a value.</b> Two solids that do not touch have no intersection, and that
    /// is the geometry declining rather than anything being broken — so it arrives as a
    /// diagnostic with a code and a help topic, not as an exception.
    /// </summary>
    [NativeFact]
    public void SolidsThatDoNotTouchRefuseToIntersect()
    {
        Brep here = Box(0, 0, 0, 1, 1, 1);
        Brep faraway = Box(100, 100, 100, 1, 1, 1);

        KernelResult<Brep> result = Kernel.Intersection(here, faraway, Fine);

        Assert.False(result.IsSuccess);
        Assert.Equal(KernelDiagnostics.Refused, result.Diagnostic!.Code);
        Assert.Equal(KernelDiagnostics.SolidsTopic, result.Diagnostic.HelpTopicId);
        Assert.Contains("intersection", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [NativeFact]
    public void AFilletRoundsEveryEdge()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);

        Brep rounded = Kernel.Fillet(block, [], 0.5, Fine).Value;

        // Twelve rounded edges and eight corners on top of the six flats.
        Assert.True(rounded.FaceCount >= 26, $"the filleted box has {rounded.FaceCount} faces");

        Mesh mesh = Kernel.Tessellate(rounded, Fine).Value;
        Assert.True(mesh.Volume() < 64.0, "rounding the edges removes material");
        Assert.True(mesh.Volume() > 60.0, "and does not remove very much of it");
    }

    /// <summary>A fillet that does not fit is a refusal, not a crash.</summary>
    [NativeFact]
    public void AFilletThatDoesNotFitIsRefused()
    {
        Brep block = Box(0, 0, 0, 1, 1, 1);

        KernelResult<Brep> result = Kernel.Fillet(block, [], 10.0, Fine);

        Assert.False(result.IsSuccess);
        Assert.Equal(KernelDiagnostics.Refused, result.Diagnostic!.Code);
    }

    [NativeFact]
    public void HollowingLeavesAWall()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);

        Brep hollow = Kernel.Shell(block, [], -0.5, Fine).Value;
        Mesh mesh = Kernel.Tessellate(hollow, Fine).Value;

        // 4^3 minus 3^3, to the tessellation's accuracy.
        Assert.Equal(37.0, mesh.Volume(), 1);
    }

    [NativeFact]
    public void ExtrudingAClosedProfileMakesASolid()
    {
        Circle profile = new(Plane.WorldXY, 2.0);

        Brep solid = Kernel.Extrude(profile, new Vector3d(0, 0, 5), cap: true, Fine).Value;
        Mesh mesh = Kernel.Tessellate(solid, Fine).Value;

        Assert.Equal(Math.PI * 4.0 * 5.0, mesh.Volume(), 0);
        Assert.Equal(3, solid.FaceCount);
    }

    [NativeFact]
    public void RevolvingAProfileMakesASolidOfRevolution()
    {
        // A unit square standing off the axis, spun a full turn: a square-section ring.
        Brep ring = Kernel.Revolve(
            new Line(new Point3d(2, 0, 0), new Point3d(3, 0, 0)),
            Point3d.Origin,
            Vector3d.ZAxis,
            Angle.FromDegrees(360),
            Fine).Value;

        Assert.True(ring.FaceCount >= 1);
    }

    [NativeFact]
    public void LoftingNeedsAtLeastTwoProfiles()
    {
        KernelResult<Brep> result = Kernel.Loft([new Circle(Plane.WorldXY, 1.0)], closed: false, Fine);

        Assert.False(result.IsSuccess);
    }

    [NativeFact]
    public void ATessellationCarriesSurfaceNormals()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1.0, 4.0);

        Mesh mesh = Kernel.Tessellate(cylinder, Fine).Value;
        Vector3d[] normals = mesh.Normals()!;

        Assert.NotEmpty(normals);
        Assert.Equal(mesh.VertexCount, normals.Length);

        // Somewhere on the wall the normal points outwards, horizontally. A per-facet normal
        // would still be horizontal, so the assertion that matters is the *count* of distinct
        // directions: a smooth wall has one per column of vertices, a faceted one has one per
        // triangle. This checks the cheap half; the shading is checked by eye in the viewport.
        Assert.Contains(normals, normal => Math.Abs(normal.Z) < 1e-6 && normal.Length > 0.5);
    }

    /// <summary>
    /// <b>A chain of operations converts twice, not ten times.</b> ADR-0021's whole claim, and the
    /// only way to see it from managed code is that the intermediate shapes were never read out.
    /// </summary>
    [NativeFact]
    public void AChainOfBooleansStaysResident()
    {
        Brep running = Box(0, 0, 0, 4, 4, 4);

        for (int i = 1; i <= 3; i++)
        {
            Brep cutter = Box(i, i, 3, 0.5, 0.5, 2);
            running = Kernel.Difference(running, cutter, Fine).Value;

            Assert.True(running.IsResident, $"step {i} came back as a plain value");
            Assert.True(running.NativeBytes > 0, $"step {i} reported no native memory");
        }

        // The first structural question is what materialises it, and only then. `IsResident`
        // means *not read out yet*, so asking is what makes it false — and the handle is still
        // held, which is what `NativeBytes` still being positive says. That is the distinction
        // that matters: the arrays now exist beside the shape rather than instead of it.
        Assert.True(running.FaceCount > 6);
        Assert.False(running.IsResident, "asking a structural question is what materialises it");
        Assert.True(running.NativeBytes > 0, "and the provider still holds the shape");
    }

    /// <summary>
    /// <b>Analytic stays analytic.</b> The reason for taking an exact kernel rather than a mesh
    /// one is that a cylinder comes back a cylinder — so a boolean that does not touch the wall
    /// must leave a cylindrical face behind, not a spline that happens to be round.
    /// </summary>
    [NativeFact]
    public void ACylinderSurvivesABooleanAsACylinder()
    {
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 2.0, 6.0);
        Brep notch = Box(-3, -3, 5, 6, 6, 2);

        Brep cut = Kernel.Difference(cylinder, notch, Fine).Value;

        Assert.Contains(cut.Surfaces(), surface => surface is CylindricalSurface);
        Assert.Contains(cut.Surfaces(), surface => surface is PlaneSurface);
    }

    /// <summary>
    /// <b>The solid demo graph's chain, as a test.</b> A demo that errors on screen is a demo that
    /// teaches the wrong thing, and the only way to keep one honest is to run what it runs. The
    /// numbers here are the numbers in <c>DemoGraphs.Solids</c>; if they stop working together,
    /// this goes red before anybody takes a screenshot.
    /// </summary>
    /// <remarks>
    /// <b>The fillet is on the plain box and not on the drilled one, and that is a measurement
    /// rather than a preference.</b> Filleting every edge of a box fused to a tangent cylinder and
    /// then drilled took <b>48 seconds</b> at a radius that fits, and refused outright at the
    /// radius that looked right — a vertex blend where three curved faces meet is the second
    /// known-hard case in this whole area (R18). A demo that takes a minute to open is not a demo.
    /// </remarks>
    [NativeFact]
    public void TheSolidDemosChainRuns()
    {
        Brep box = BrepPrimitives.Box(
            Plane.ByOriginXAxisYAxis(new Point3d(-4, -2, 0), Vector3d.XAxis, Vector3d.YAxis), 5, 4, 2);
        Brep post = BrepPrimitives.Cylinder(
            Plane.ByOriginXAxisYAxis(new Point3d(-1.5, 0, 0), Vector3d.XAxis, Vector3d.YAxis), 1.6, 4.5);
        Brep drill = BrepPrimitives.Cylinder(
            Plane.ByOriginXAxisYAxis(new Point3d(-1.5, 0, -1), Vector3d.XAxis, Vector3d.YAxis), 0.8, 7.0);

        KernelResult<Brep> fused = Kernel.Union(box, post, Fine);
        Assert.True(fused.IsSuccess, fused.Diagnostic?.Detail);

        KernelResult<Brep> drilled = Kernel.Difference(fused.Value, drill, Fine);
        Assert.True(drilled.IsSuccess, drilled.Diagnostic?.Detail);

        Mesh mesh = Kernel.Tessellate(drilled.Value, Fine).Value;
        Assert.True(mesh.Volume() > 0.0, "the demo's main solid has a positive volume");

        // The demo's third object: a box with every edge rounded, which is the thing no mesh
        // boolean could have produced.
        Brep plinth = BrepPrimitives.Box(
            Plane.ByOriginXAxisYAxis(new Point3d(12, -1.5, 0), Vector3d.XAxis, Vector3d.YAxis), 3, 3, 3);

        KernelResult<Brep> rounded = Kernel.Fillet(plinth, [], 0.4, Fine);
        Assert.True(rounded.IsSuccess, rounded.Diagnostic?.Detail);
        Assert.True(rounded.Value.FaceCount >= 26, $"{rounded.Value.FaceCount} faces");

        // And its second object: a hollowed box.
        Brep hollow = Kernel.Shell(
            BrepPrimitives.Box(
                Plane.ByOriginXAxisYAxis(new Point3d(6, -1.5, 0), Vector3d.XAxis, Vector3d.YAxis), 3, 3, 3),
            [],
            -0.4,
            Fine).Value;

        Assert.Equal(27.0 - (2.2 * 2.2 * 2.2), Kernel.Tessellate(hollow, Fine).Value.Volume(), 1);
    }

    /// <summary>
    /// <b>The difference between a split and a difference, measured.</b> A block cut by a plate
    /// comes back as pieces whose volumes <i>add up to the block's</i>; the same block differenced
    /// by the same plate comes back short by the plate. That arithmetic is the whole reason
    /// `Split` is not a fourth boolean opcode.
    /// </summary>
    [NativeFact]
    public void ASplitKeepsEveryPieceAndADifferenceDoesNot()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);
        Brep plate = Box(-1, -1, 1.9, 6, 6, 0.2);

        IReadOnlyList<Brep> pieces = Kernel.Split(block, [plate], Fine).Value;

        Assert.True(pieces.Count >= 2, $"the split produced {pieces.Count} piece(s)");

        double total = pieces.Sum(piece => Kernel.Tessellate(piece, Fine).Value.Volume());
        Assert.Equal(64.0, total, 1);

        // And the difference throws the middle away.
        Brep cut = Kernel.Difference(block, plate, Fine).Value;
        Assert.Equal(64.0 - (4 * 4 * 0.2), Kernel.Tessellate(cut, Fine).Value.Volume(), 1);
    }

    [NativeFact]
    public void ASplitThatCutsNothingReturnsTheShapeItself()
    {
        Brep block = Box(0, 0, 0, 2, 2, 2);
        Brep faraway = Box(50, 50, 50, 1, 1, 1);

        IReadOnlyList<Brep> pieces = Kernel.Split(block, [faraway], Fine).Value;

        Assert.Single(pieces);
        Assert.Equal(8.0, Kernel.Tessellate(pieces[0], Fine).Value.Volume(), 1);
    }

    /// <summary>Trimming keeps the piece the point is in.</summary>
    [NativeFact]
    public void TrimmingKeepsThePieceThePointIsIn()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);
        Brep plate = Box(-1, -1, 1.9, 6, 6, 0.2);

        Brep lower = Kernel.Trim(block, [plate], new Point3d(2, 2, 1), Fine).Value;
        Brep upper = Kernel.Trim(block, [plate], new Point3d(2, 2, 3), Fine).Value;

        Assert.Equal(4 * 4 * 1.9, Kernel.Tessellate(lower, Fine).Value.Volume(), 1);
        Assert.Equal(4 * 4 * 1.9, Kernel.Tessellate(upper, Fine).Value.Volume(), 1);
    }

    [NativeFact]
    public void TrimmingToAPointOutsideEveryPieceIsRefused()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);
        Brep plate = Box(-1, -1, 1.9, 6, 6, 0.2);

        KernelResult<Brep> result = Kernel.Trim(block, [plate], new Point3d(100, 100, 100), Fine);

        Assert.False(result.IsSuccess);
        Assert.Equal(KernelDiagnostics.Refused, result.Diagnostic!.Code);
        Assert.Contains("none of them", result.Diagnostic.Detail, StringComparison.Ordinal);
    }

    [NativeFact]
    public void OffsettingASolidGrowsIt()
    {
        Brep block = Box(0, 0, 0, 2, 2, 2);

        Brep bigger = Kernel.Offset(block, 0.25, Fine).Value;

        // A 2.5-cube with rounded corners: bigger than 8, smaller than 15.625.
        double volume = Kernel.Tessellate(bigger, Fine).Value.Volume();
        Assert.True(volume > 8.0, $"the offset shrank it to {volume}");
        Assert.True(volume < 15.7, $"the offset grew it to {volume}");
    }

    /// <summary>
    /// Thickening is the counterpart of hollowing: it adds material to something that encloses
    /// nothing yet, rather than taking it out of something that does.
    /// </summary>
    [NativeFact]
    public void ThickeningASheetMakesASolid()
    {
        // One face on one surface, no loops: the provider builds it from the surface's own domain,
        // which is exactly what a sheet is.
        Brep sheet = new(
            [],
            [],
            [PlaneSurface.ByPlaneSize(Plane.WorldXY, 4, 4)],
            [],
            [],
            [],
            [],
            [new BrepFace(0, 0, 0, false)],
            [new BrepShell(0, 1)]);

        Brep solid = Kernel.Thicken(sheet, 0.5, Fine).Value;

        Assert.Equal(16.0 * 0.5, Kernel.Tessellate(solid, Fine).Value.Volume(), 1);
    }

    /// <summary>
    /// Drafting tilts the walls so a moulded part can leave its mould. A box pulled upwards and
    /// pivoted about its base comes out bigger at the top and the same size at the bottom.
    /// </summary>
    [NativeFact]
    public void DraftingTiltsTheWallsAndLeavesTheNeutralPlaneAlone()
    {
        Brep block = Box(0, 0, 0, 4, 4, 4);

        KernelResult<Brep> drafted = Kernel.Draft(
            block,
            [],
            Vector3d.ZAxis,
            Angle.FromDegrees(5),
            Plane.ByOriginNormal(new Point3d(0, 0, 2), Vector3d.ZAxis),
            Fine);

        Assert.True(drafted.IsSuccess, drafted.Diagnostic?.Detail);

        // Six faces still: drafting tilts them, it does not add any.
        Assert.Equal(6, drafted.Value.FaceCount);

        // Tilting the four walls outwards adds material above the neutral plane and none below.
        double volume = Kernel.Tessellate(drafted.Value, Fine).Value.Volume();
        Assert.True(volume > 64.0, $"drafting outwards shrank it to {volume}");
        Assert.True(volume < 90.0, $"drafting five degrees grew it to {volume}");

        // And the height is untouched: drafting tilts the walls, it does not move the caps.
        BoundingBox bounds = drafted.Value.BoundingBox;
        Assert.Equal(0.0, bounds.Min.Z, 6);
        Assert.Equal(4.0, bounds.Max.Z, 6);

        // Narrower at the bottom than at the top, which is what a draft is.
        Assert.True(bounds.Min.X < 0.0, $"the bottom did not move outwards: {bounds.Min.X}");
        Assert.True(bounds.Max.X > 4.0, $"the top did not move outwards: {bounds.Max.X}");
    }

    /// <summary>A zero angle is an argument error, not a no-op that pretends to have worked.</summary>
    [NativeFact]
    public void AZeroDraftAngleIsRefused()
    {
        KernelResult<Brep> result = Kernel.Draft(
            Box(0, 0, 0, 1, 1, 1), [], Vector3d.ZAxis, Angle.FromDegrees(0), Plane.WorldXY, Fine);

        Assert.False(result.IsSuccess);
    }

    [NativeFact]
    public void SewingTwoHalvesMakesOneShape()
    {
        Brep left = Box(0, 0, 0, 1, 1, 1);
        Brep right = Box(1, 0, 0, 1, 1, 1);

        Brep sewn = Kernel.Sew([left, right], Fine).Value;

        Assert.True(sewn.FaceCount >= 6);
    }

    [NativeFact]
    public void HealingAGoodShapeLeavesItGood()
    {
        Brep block = Box(0, 0, 0, 2, 2, 2);

        Brep healed = Kernel.Heal(block, Fine).Value;
        Mesh mesh = Kernel.Tessellate(healed, Fine).Value;

        Assert.Equal(8.0, mesh.Volume(), 3);
    }
}

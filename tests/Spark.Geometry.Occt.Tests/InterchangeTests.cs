using System;
using System.IO;
using System.Linq;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// STEP and IGES, written and read back.
/// </summary>
/// <remarks>
/// <para>
/// <b>A round trip through a file is the only test available here, and it is a weak one on
/// purpose.</b> `E13-T12` says the validation that counts is a public corpus and a <i>third-party
/// viewer, never our own reader</i> — because OpenCascade wrote both ends of this trip and a
/// success proves the two ends agree, not that either is right. What these tests do prove is that
/// the plumbing works, the extension dispatch is real, and the exactness survives: a cylinder
/// written to STEP and read back is still a cylindrical surface and not a mesh.
/// </para>
/// <para>
/// <b>The stronger check is the file itself.</b> A STEP file naming `ADVANCED_FACE` and
/// `CYLINDRICAL_SURFACE` is a file a third party will read as a solid; one full of
/// `POLY_LOOP` would not be. Asserting on the text is not a substitute for a viewer and it is
/// evidence a round trip cannot give.
/// </para>
/// </remarks>
public sealed class InterchangeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "spark-occt-" + Guid.NewGuid().ToString("N"));

    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    public InterchangeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that will not delete is not a test failure.
        }
    }

    private string Path0(string name) => Path.Combine(_directory, name);

    [NativeFact]
    public void TheProviderClaimsStepAndIges()
    {
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Step));
        Assert.True(Kernel.Capabilities.HasFlag(BrepCapabilities.Iges));
    }

    [NativeFact]
    public void ABoxSurvivesAStepRoundTrip()
    {
        string file = Path0("box.step");
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);

        Assert.True(Kernel.WriteFile(box, file, Fine).IsSuccess);
        Assert.True(File.Exists(file));

        Brep back = Kernel.ReadFile(file, Fine).Value;

        Assert.Equal(6, back.FaceCount);
        Assert.Equal(12, back.EdgeCount);
        Assert.Equal(24.0, Kernel.Tessellate(back, Fine).Value.Volume(), 3);
    }

    /// <summary>
    /// <b>The exactness is the point, so the exactness is what is asserted.</b> A cylinder that
    /// came back as a fine mesh would pass a volume check and fail this one.
    /// </summary>
    [NativeFact]
    public void ACylinderSurvivesAStepRoundTripAsACylinder()
    {
        string file = Path0("cylinder.stp");
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1.5, 5.0);

        Assert.True(Kernel.WriteFile(cylinder, file, Fine).IsSuccess);

        Brep back = Kernel.ReadFile(file, Fine).Value;

        Assert.Equal(3, back.FaceCount);

        CylindricalSurface wall = Assert.Single(back.Surfaces().OfType<CylindricalSurface>());
        Assert.Equal(1.5, wall.Radius, 6);
    }

    /// <summary>
    /// The file is read as text and asked what it says. A third-party viewer is what `E13-T12`
    /// actually requires; this is the part of that check a test can do.
    /// </summary>
    [NativeFact]
    public void AStepFileNamesTheExactSurfacesRatherThanTriangles()
    {
        string file = Path0("named.step");
        Brep cylinder = BrepPrimitives.Cylinder(Plane.WorldXY, 1.0, 3.0);

        Assert.True(Kernel.WriteFile(cylinder, file, Fine).IsSuccess);

        string text = File.ReadAllText(file);

        Assert.Contains("ISO-10303-21", text, StringComparison.Ordinal);
        Assert.Contains("CYLINDRICAL_SURFACE", text, StringComparison.Ordinal);
        Assert.Contains("ADVANCED_FACE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("POLY_LOOP", text, StringComparison.Ordinal);
    }

    [NativeFact]
    public void ABooleanResultSurvivesAStepRoundTrip()
    {
        string file = Path0("fused.step");

        Brep first = BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4);
        Brep second = BrepPrimitives.Box(
            Plane.ByOriginXAxisYAxis(new Point3d(1, 1, 1), Vector3d.XAxis, Vector3d.YAxis), 2, 3, 4);

        Brep fused = Kernel.Union(first, second, Fine).Value;

        Assert.True(Kernel.WriteFile(fused, file, Fine).IsSuccess);

        Brep back = Kernel.ReadFile(file, Fine).Value;

        Assert.Equal(fused.FaceCount, back.FaceCount);
        Assert.Equal(42.0, Kernel.Tessellate(back, Fine).Value.Volume(), 2);
    }

    [NativeFact]
    public void ABoxSurvivesAnIgesRoundTrip()
    {
        string file = Path0("box.igs");
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 2, 2, 2);

        Assert.True(Kernel.WriteFile(box, file, Fine).IsSuccess);
        Assert.True(File.Exists(file));

        Brep back = Kernel.ReadFile(file, Fine).Value;

        Assert.Equal(6, back.FaceCount);
    }

    /// <summary>An extension the build does not know is a refusal that names what it does know.</summary>
    [NativeFact]
    public void AnUnknownExtensionIsRefusedByName()
    {
        KernelResult<bool> result =
            Kernel.WriteFile(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1), Path0("x.3dm"), Fine);

        Assert.False(result.IsSuccess);
        Assert.Equal(KernelDiagnostics.Refused, result.Diagnostic!.Code);
        Assert.Contains(".step", result.Diagnostic.Detail, StringComparison.Ordinal);
        Assert.Contains(".iges", result.Diagnostic.Detail, StringComparison.Ordinal);
    }

    [NativeFact]
    public void AMissingFileIsRefusedRatherThanThrown()
    {
        KernelResult<Brep> result = Kernel.ReadFile(Path0("nothing-here.step"), Fine);

        Assert.False(result.IsSuccess);
        Assert.Equal(KernelDiagnostics.Refused, result.Diagnostic!.Code);
    }
}

using System;
using System.IO;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Geometry.Occt.Tests;

/// <summary>
/// What a failure leaves behind for somebody who has to reproduce it.
/// </summary>
/// <remarks>
/// <b>R16 is the risk that a boolean returning a wrong-but-valid shape is diagnosable only inside
/// code we do not own.</b> Nothing here fixes that. What these assert is that the *evidence*
/// exists: the provider's own words in the diagnostic, OpenCascade's validity checker on demand,
/// and — when somebody sets <c>SPARK_OCCT_DUMP</c> — the exact inputs in the format upstream's own
/// test harness reads.
/// </remarks>
public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "spark-dump-" + Guid.NewGuid().ToString("N"));

    private static IBrepKernel Kernel => NativeProvider.Kernel;

    private static Tolerance Fine => new(1e-4, Angle.FromDegrees(1), 1e-12);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OcctBrepKernel.DumpVariable, null);

        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A valid solid has nothing to report, and the checker says so by saying nothing.</summary>
    [NativeFact]
    public void AGoodSolidCheeksOutClean()
    {
        Brep fused = Kernel.Union(
            BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4),
            BrepPrimitives.Box(
                Plane.ByOriginXAxisYAxis(new Point3d(1, 1, 1), Vector3d.XAxis, Vector3d.YAxis), 2, 3, 4),
            Fine).Value;

        Assert.Equal(string.Empty, OcctBrepKernel.Check(fused));
    }

    /// <summary>
    /// <b>Dumping is off unless asked for, and that is the assertion.</b> An exact kernel refuses
    /// constantly and correctly; a build that wrote a file on every refusal would fill a disk with
    /// evidence of things working as designed.
    /// </summary>
    [NativeFact]
    public void AFailureWritesNothingUnlessTheVariableIsSet()
    {
        Environment.SetEnvironmentVariable(OcctBrepKernel.DumpVariable, null);

        KernelResult<Brep> refused =
            Kernel.Fillet(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1), [], 10.0, Fine);

        Assert.False(refused.IsSuccess);
        Assert.False(Directory.Exists(_directory));
        Assert.DoesNotContain("reproduction", refused.Diagnostic!.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// With the variable set, the inputs land in `.brep` — the format OpenCascade's own Draw test
    /// harness reads — and the diagnostic names the files.
    /// </summary>
    [NativeFact]
    public void AFailureWritesItsInputsWhenAsked()
    {
        Environment.SetEnvironmentVariable(OcctBrepKernel.DumpVariable, _directory);

        try
        {
            KernelResult<Brep> refused =
                Kernel.Fillet(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1), [], 10.0, Fine);

            Assert.False(refused.IsSuccess);
            Assert.Contains("reproduction", refused.Diagnostic!.Detail, StringComparison.Ordinal);

            string[] files = Directory.GetFiles(_directory, "*.brep");
            Assert.NotEmpty(files);

            // A .brep file is text and its first line says what it is: `DBRep_DrawableShape` is
            // what Draw's `restore` expects to find. If that changes, upstream's harness will not
            // read the file and the whole point of writing it is gone.
            string first = File.ReadAllLines(files[0])[0];
            Assert.Equal("DBRep_DrawableShape", first.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OcctBrepKernel.DumpVariable, null);
        }
    }

    /// <summary>A refusal carries the provider's own words, not a generic sentence.</summary>
    [NativeFact]
    public void ARefusalCarriesTheProvidersOwnWords()
    {
        KernelResult<Brep> refused =
            Kernel.Fillet(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1), [], 10.0, Fine);

        Assert.False(refused.IsSuccess);
        Assert.Contains("radius", refused.Diagnostic!.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(KernelDiagnostics.SolidsTopic, refused.Diagnostic.HelpTopicId);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Engine.Tests;

/// <summary>
/// The kernel seam and residency — `E2-T28`, [ADR-0021].
/// </summary>
/// <remarks>
/// <para>
/// <b>These are tests of the seam, not of any provider.</b> They use a fake residency and the
/// no-provider kernel, which is exactly what makes them worth having: the properties asserted here
/// — laziness, one materialisation, a refusal being a value, capabilities being visible before an
/// operation is attempted — are Spark's, and they must hold whatever is on the other side.
/// </para>
/// <para>
/// <b>Nothing here asserts that a round trip is identity</b>, because ADR-0021 forbids it: a
/// tolerant kernel re-sews and re-tolerances, and a test insisting otherwise would be a test of a
/// provider's internals wearing Spark's name.
/// </para>
/// </remarks>
public sealed class BrepKernelSeamTests
{
    // -- Residency -------------------------------------------------------------------------------

    /// <summary>
    /// <b>Nothing is read out of the provider until something asks a structural question.</b> This
    /// is the whole of ADR-0021: a chain of ten operations performs zero imports and one
    /// materialisation, and it is a fidelity rule rather than a performance one.
    /// </summary>
    [Fact]
    public void AResidentShapeIsNotReadOutUntilItIsAsked()
    {
        CountingResidency residency = new(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1));
        Brep shape = new(residency);

        Assert.True(shape.IsResident);
        Assert.Equal(0, residency.Materialisations);

        Assert.Equal(6, shape.FaceCount);

        Assert.False(shape.IsResident);
        Assert.Equal(1, residency.Materialisations);
    }

    /// <summary>
    /// <b>And it is read out exactly once, however many questions follow.</b> Materialising per
    /// query would put a provider round trip inside every loop that walks a model.
    /// </summary>
    [Fact]
    public void AResidentShapeIsReadOutOnlyOnce()
    {
        CountingResidency residency = new(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1));
        Brep shape = new(residency);

        _ = shape.FaceCount;
        _ = shape.EdgeCount;
        _ = shape.BoundingBox;
        _ = shape.Validate();
        _ = shape.IsSolid;
        _ = shape.Face(0).Surface;

        Assert.Equal(1, residency.Materialisations);
    }

    /// <summary>A materialised resident shape is the shape the provider described.</summary>
    [Fact]
    public void AResidentShapeMaterialisesToWhatTheProviderHolds()
    {
        Brep original = BrepPrimitives.Cylinder(Plane.WorldXY, 2, 5);
        Brep resident = new(new CountingResidency(original));

        Assert.Equal(original.FaceCount, resident.FaceCount);
        Assert.Equal(original.EdgeCount, resident.EdgeCount);
        Assert.True(resident.IsSolid);
        Assert.Empty(resident.Validate());
    }

    /// <summary>
    /// The native size is visible before materialisation, which is what the evaluation cache needs:
    /// a cache seeing only managed bytes would hold gigabytes of a provider's heap while reporting
    /// megabytes.
    /// </summary>
    [Fact]
    public void NativeSizeIsVisibleWithoutMaterialising()
    {
        CountingResidency residency = new(BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1)) { Bytes = 4096 };
        Brep shape = new(residency);

        Assert.Equal(4096, shape.NativeBytes);
        Assert.Equal(0, residency.Materialisations);
    }

    /// <summary>A BRep built from arrays is never resident and holds no provider memory.</summary>
    [Fact]
    public void AValueBrepIsNotResident()
    {
        Brep box = BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1);

        Assert.False(box.IsResident);
        Assert.Equal(0, box.NativeBytes);
    }

    // -- The result type --------------------------------------------------------------------------

    /// <summary>A refusal is a value carrying a diagnostic, not an exception.</summary>
    [Fact]
    public void ARefusalIsAValue()
    {
        KernelResult<Brep> result = UnavailableBrepKernel.Instance.Union(
            BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1),
            BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1),
            Tolerance.Default);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal(KernelDiagnostics.Unavailable, result.Diagnostic!.Code);
        Assert.False(result.TryGetValue(out _));
    }

    /// <summary>
    /// Reading the value of a refusal is a mistake and says so, rather than handing back a null
    /// that fails somewhere else.
    /// </summary>
    [Fact]
    public void ReadingARefusedValueSaysWhatWentWrong()
    {
        KernelResult<Brep> result = UnavailableBrepKernel.Instance.Heal(
            BrepPrimitives.Box(Plane.WorldXY, 1, 1, 1), Tolerance.Default);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => result.Value);

        Assert.Contains("no solid-modelling kernel", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A success carries its value and no diagnostic.</summary>
    [Fact]
    public void ASuccessCarriesItsValue()
    {
        KernelResult<int> result = KernelResult<int>.Success(7);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Diagnostic);
        Assert.Equal(7, result.Value);
        Assert.True(result.TryGetValue(out int value));
        Assert.Equal(7, value);
    }

    // -- Capabilities ----------------------------------------------------------------------------

    /// <summary>
    /// <b>A session with no provider reports no capabilities</b>, which is what lets the node
    /// library grey the operations out rather than letting a user find out by pressing one.
    /// </summary>
    [Fact]
    public void NoProviderMeansNoCapabilities()
    {
        Assert.Equal(BrepCapabilities.None, UnavailableBrepKernel.Instance.Capabilities);
        Assert.False(UnavailableBrepKernel.Instance.Capabilities.HasFlag(BrepCapabilities.Boolean));
    }

    /// <summary>Every capability is a distinct flag, so a set can name several without collision.</summary>
    [Fact]
    public void EveryCapabilityIsADistinctFlag()
    {
        int[] values =
        [
            .. Enum.GetValues<BrepCapabilities>()
                .Where(value => value != BrepCapabilities.None)
                .Select(value => (int)value),
        ];

        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.All(values, value => Assert.Equal(0, value & (value - 1)));
    }

    // -- Tessellation across the seam --------------------------------------------------------------

    /// <summary>
    /// <b>An untrimmed shape tessellates with no provider, and that is not a special case sneaking
    /// through.</b> A face whose only loop is its surface's own boundary <i>is</i> a surface, and
    /// tessellating a surface is in front of the seam.
    /// </summary>
    [Fact]
    public void AnUntrimmedShapeTessellatesWithNoProvider()
    {
        KernelResult<Mesh> result = UnavailableBrepKernel.Instance.Tessellate(
            BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4), new Tolerance(0.05, Angle.FromDegrees(1), 1e-9));

        Assert.True(result.IsSuccess, result.Diagnostic?.Message);
        Assert.Equal(6, result.Value.FaceCount);

        // A 2 x 3 x 4 box: 2(2*3 + 2*4 + 3*4) = 52.
        Assert.Equal(52.0, result.Value.Area, 1e-9);
    }

    /// <summary>A trimmed shape is refused by name rather than tessellated wrongly.</summary>
    [Fact]
    public void ATrimmedShapeIsRefused()
    {
        Brep trimmed = new(
            [new Point3d(0, 0, 0)],
            [new Line(Point3d.Origin, new Point3d(1, 0, 0))],
            [new PlaneSurface(Plane.WorldXY, Interval.Unit, Interval.Unit)],
            [new BrepVertex(0)],
            [new BrepEdge(0, 0, 0)],
            [new BrepTrim(0, false), new BrepTrim(0, true)],
            [new BrepLoop(0, 1, BrepLoopKind.Outer), new BrepLoop(1, 1, BrepLoopKind.Inner)],
            [new BrepFace(0, 0, 2, false)],
            [new BrepShell(0, 1)]);

        KernelResult<Mesh> result = UnavailableBrepKernel.Instance.Tessellate(trimmed, Tolerance.Default);

        Assert.False(result.IsSuccess);
        Assert.Contains("trimmed", result.Diagnostic!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tessellated box has a positive volume, which says its faces came out wound the right way —
    /// the surface a reversed face sits on is wound inwards, and a mesher that ignored that would
    /// produce a solid whose volume is negative.
    /// </summary>
    [Fact]
    public void ATessellatedBoxHasAPositiveVolume()
    {
        Mesh mesh = UnavailableBrepKernel.Instance
            .Tessellate(BrepPrimitives.Box(Plane.WorldXY, 2, 3, 4), new Tolerance(0.05, Angle.FromDegrees(1), 1e-9))
            .Value;

        Assert.True(mesh.Volume() > 0.0, $"the volume came out {mesh.Volume()}");
    }

    /// <summary>A residency that hands back a model and counts how often it is asked.</summary>
    private sealed class CountingResidency(Brep model) : BrepResidency
    {
        internal int Materialisations { get; private set; }

        internal long Bytes { get; init; }

        public override long NativeBytes => Bytes;

        public override BrepData Materialise()
        {
            Materialisations++;

            return new BrepData(
                model.Points(),
                model.Curves(),
                model.Surfaces(),
                model.Vertices(),
                model.Edges(),
                model.Trims(),
                model.Loops(),
                model.Faces(),
                model.Shells());
        }

        public override void Dispose()
        {
        }
    }
}

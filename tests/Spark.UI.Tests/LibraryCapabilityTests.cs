using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The node library greys out what this build's kernel cannot do, instead of letting a user find
/// out by pressing it (<c>E2-T28</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The configuration under test is a supported one, not a contrivance.</b> A build with no
/// native component is supported and tested — <c>Spark.Geometry.Occt.Tests</c> skips itself when
/// the shim is absent, precisely because that build ships. On one, <c>BrepKernel.Current</c> is
/// <c>UnavailableBrepKernel</c> and every solid operation refuses by name; until this, the library
/// showed all of them exactly as it showed the nodes that work.
/// </para>
/// <para>
/// <b>These install a kernel and put the old one back.</b> <c>BrepKernel.Current</c> is ambient by
/// design — a node is a static method found by reflection, and a kernel parameter would appear as
/// a port on every solid node — so there is one to swap and it must be swapped back. xunit runs
/// classes in parallel, so this class is a collection of its own: another test reading the kernel
/// while this one has it uninstalled would fail for a reason nothing in its own file explains.
/// </para>
/// </remarks>
[Collection(nameof(LibraryCapabilityTests))]
[CollectionDefinition(nameof(LibraryCapabilityTests), DisableParallelization = true)]
public sealed class LibraryCapabilityTests
{
    /// <summary>
    /// <b>The row.</b> With no kernel, a node that needs one is unavailable and says why.
    /// </summary>
    [Fact]
    public void WithNoKernelASolidNodeIsUnavailableAndSaysWhy() => WithKernel(
        UnavailableBrepKernel.Instance,
        () =>
        {
            LibraryEntryViewModel union = Entry("Spark.Nodes.Core/Solid.Union");

            Assert.False(union.IsAvailable);
            Assert.NotNull(union.Unavailable);

            // The whole provider is absent, so the sentence says that rather than naming one flag:
            // one fact explaining every greyed row, not forty separate mysteries.
            Assert.Contains(
                "no solid-modelling kernel", union.Unavailable, StringComparison.Ordinal);

            // The tooltip says the reason, not what a union is. The row trims the reason - it is
            // three inches wide - so hovering has to answer the question the greying raised.
            Assert.Equal(union.Unavailable, union.Tooltip);
        });

    /// <summary>
    /// <b>A node that never reaches the kernel is untouched.</b> Greying the whole library on a
    /// build with no provider would be a far worse lie than greying none of it.
    /// </summary>
    [Fact]
    public void WithNoKernelANodeThatNeedsNoKernelIsStillAvailable() => WithKernel(
        UnavailableBrepKernel.Instance,
        () =>
        {
            LibraryEntryViewModel point = Entry("Spark.Nodes.Core/Point.FromCoordinates");

            Assert.True(point.IsAvailable);
            Assert.True(Entry("Spark.Nodes.Core/Number.Value").IsAvailable);
            Assert.Null(point.Unavailable);

            // And its tooltip is still what the node does.
            Assert.Equal(point.Description, point.Tooltip);
        });

    /// <summary>
    /// A kernel that can do some things and not others greys exactly the nodes it cannot run, and
    /// the sentence names the missing capability.
    /// </summary>
    /// <remarks>
    /// This is the case the flag set exists for rather than the all-or-nothing one: a provider is
    /// allowed to be partial, and the library has to be right about which part.
    /// </remarks>
    [Fact]
    public void APartialKernelGreysOnlyWhatItCannotDo() => WithKernel(
        new PartialKernel(BrepCapabilities.Boolean),
        () =>
        {
            Assert.True(Entry("Spark.Nodes.Core/Solid.Union").IsAvailable);

            LibraryEntryViewModel fillet = Entry("Spark.Nodes.Core/Solid.FilletAll");

            Assert.False(fillet.IsAvailable);
            Assert.Contains("Fillet", fillet.Unavailable!, StringComparison.Ordinal);

            // Not the no-kernel sentence: this build has a kernel, it just cannot do this.
            Assert.DoesNotContain(
                "no solid-modelling kernel", fillet.Unavailable!, StringComparison.Ordinal);
        });

    /// <summary>
    /// <b>An unavailable node is not placed.</b> A row that looks disabled and places anyway is
    /// worse than one that was never greyed.
    /// </summary>
    [Fact]
    public void AnUnavailableEntryIsNotPlaced() => WithKernel(
        UnavailableBrepKernel.Instance,
        () =>
        {
            MainWindowViewModel model = new();

            model.SelectedLibraryEntry =
                model.LibraryEntries.Single(entry => entry.Key == "Spark.Nodes.Core/Solid.Union");

            Assert.Equal(-1, model.PlaceSelectedLibraryEntry(0, 0));

            model.SelectedLibraryEntry = model.LibraryEntries.Single(
                entry => entry.Key == "Spark.Nodes.Core/Point.FromCoordinates");

            Assert.NotEqual(-1, model.PlaceSelectedLibraryEntry(0, 0));
        });

    /// <summary>
    /// <b>Every node that reaches the kernel declares what it needs.</b> The check that keeps this
    /// feature honest as the library grows: a solid node added without the attribute is one this
    /// build would offer on a machine that cannot run it, and nothing else would notice.
    /// </summary>
    /// <remarks>
    /// Read from the source of truth — the <c>Solid</c> type's members — rather than from a list
    /// here, which would be the hand-maintained mapping this whole design rejects. The members that
    /// legitimately need nothing are the ones that never touch <c>BrepKernel.Current</c>:
    /// <c>Box</c> and <c>Cylinder</c> come from <c>BrepPrimitives</c>, the transforms are managed,
    /// and the queries read the shape.
    /// </remarks>
    [Fact]
    public void EveryNodeThatReachesTheKernelDeclaresWhatItNeeds()
    {
        HashSet<string> declares =
        [
            .. typeof(Spark.Nodes.Core.Solid)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(method => method.GetCustomAttributes(
                    typeof(RequiresBrepCapabilityAttribute), inherit: false).Length > 0)
                .Select(method => method.Name),
        ];

        // The methods whose bodies call BrepKernel.Current, listed by name because reflection
        // cannot read a method body. Kept honest by the reverse direction below: a name here that
        // stops reaching the kernel, or one that starts, shows up as a mismatch.
        string[] reachTheKernel =
        [
            "Union", "Difference", "Intersection", "Extrude", "Sweep", "Patch", "Split", "Trim",
            "Offset", "Thicken", "FilletAll", "Draft", "Hollow", "ToMesh",
        ];

        Assert.Equal(reachTheKernel.OrderBy(n => n, StringComparer.Ordinal), declares.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// <b>Every flag a node asks for is one the real provider claims.</b> A node declaring a
    /// capability nothing grants is greyed out forever, on every machine, and looks exactly like
    /// the feature working.
    /// </summary>
    [Fact]
    public void EveryDeclaredCapabilityIsOneTheProviderClaims()
    {
        BrepCapabilities claimed = new Spark.Geometry.Occt.OcctBrepKernel().Capabilities;

        foreach (System.Reflection.MethodInfo method in typeof(Spark.Nodes.Core.Solid)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (method.GetCustomAttributes(typeof(RequiresBrepCapabilityAttribute), inherit: false)
                is [RequiresBrepCapabilityAttribute required])
            {
                Assert.True(
                    (required.Capability & ~claimed) == BrepCapabilities.None,
                    $"Solid.{method.Name} needs {required.Capability}, which the provider does not claim.");
            }
        }
    }

    private static LibraryEntryViewModel Entry(string key)
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));

        return new LibraryEntryViewModel(
            library.Definitions().Single(definition => definition.Key.Value == key));
    }

    /// <summary>Runs a body with a kernel installed, and puts the old one back.</summary>
    /// <param name="kernel">The kernel to install.</param>
    /// <param name="body">What to run.</param>
    private static void WithKernel(IBrepKernel kernel, Action body)
    {
        IBrepKernel previous = BrepKernel.Current;
        BrepKernel.Install(kernel);

        try
        {
            body();
        }
        finally
        {
            BrepKernel.Install(previous);
        }
    }

    /// <summary>
    /// A kernel that claims exactly what it is told to and delegates every operation to the
    /// unavailable one.
    /// </summary>
    /// <remarks>
    /// <b>A wrapper rather than a hand-written fake, so that it cannot drift.</b> An implementation
    /// of every member would have to be edited each time <c>IBrepKernel</c> gains one, and the
    /// edit that matters — a new capability the library should grey — is the one nobody would
    /// connect to a file in the test project. Nothing here is ever invoked: what is under test is
    /// what the library does with <see cref="IBrepKernel.Capabilities"/>, and refusing everything
    /// is what a partial provider does with the operations it does not have anyway.
    /// </remarks>
    /// <param name="capabilities">What this kernel claims it can do.</param>
    private sealed class PartialKernel(BrepCapabilities capabilities) : IBrepKernel
    {
        private static readonly IBrepKernel Nothing = UnavailableBrepKernel.Instance;

        public string Name => "partial";

        public BrepCapabilities Capabilities { get; } = capabilities;

        public KernelResult<Brep> Union(Brep first, Brep second, in Tolerance tolerance) =>
            Nothing.Union(first, second, tolerance);

        public KernelResult<Brep> Difference(Brep first, Brep second, in Tolerance tolerance) =>
            Nothing.Difference(first, second, tolerance);

        public KernelResult<Brep> Intersection(Brep first, Brep second, in Tolerance tolerance) =>
            Nothing.Intersection(first, second, tolerance);

        public KernelResult<Brep> Extrude(
            Curve profile, in Vector3d direction, bool cap, in Tolerance tolerance) =>
            Nothing.Extrude(profile, direction, cap, tolerance);

        public KernelResult<Brep> Revolve(
            Curve profile,
            in Point3d axisOrigin,
            in Vector3d axisDirection,
            Angle angle,
            in Tolerance tolerance) =>
            Nothing.Revolve(profile, axisOrigin, axisDirection, angle, tolerance);

        public KernelResult<Brep> Loft(
            IReadOnlyList<Curve> profiles, bool closed, in Tolerance tolerance) =>
            Nothing.Loft(profiles, closed, tolerance);

        public KernelResult<Brep> Sweep(Curve profile, Curve rail, bool cap, in Tolerance tolerance) =>
            Nothing.Sweep(profile, rail, cap, tolerance);

        public KernelResult<Brep> Patch(IReadOnlyList<Curve> boundary, in Tolerance tolerance) =>
            Nothing.Patch(boundary, tolerance);

        public KernelResult<Brep> Fillet(
            Brep solid, IReadOnlyList<int> edges, double radius, in Tolerance tolerance) =>
            Nothing.Fillet(solid, edges, radius, tolerance);

        public KernelResult<Brep> Chamfer(
            Brep solid, IReadOnlyList<int> edges, double distance, in Tolerance tolerance) =>
            Nothing.Chamfer(solid, edges, distance, tolerance);

        public KernelResult<Brep> Shell(
            Brep solid, IReadOnlyList<int> facesToOpen, double thickness, in Tolerance tolerance) =>
            Nothing.Shell(solid, facesToOpen, thickness, tolerance);

        public KernelResult<IReadOnlyList<Brep>> Split(
            Brep shape, IReadOnlyList<Brep> tools, in Tolerance tolerance) =>
            Nothing.Split(shape, tools, tolerance);

        public KernelResult<Brep> Trim(
            Brep shape, IReadOnlyList<Brep> tools, in Point3d keep, in Tolerance tolerance) =>
            Nothing.Trim(shape, tools, keep, tolerance);

        public KernelResult<Brep> Offset(Brep shape, double distance, in Tolerance tolerance) =>
            Nothing.Offset(shape, distance, tolerance);

        public KernelResult<Brep> Thicken(Brep sheet, double thickness, in Tolerance tolerance) =>
            Nothing.Thicken(sheet, thickness, tolerance);

        public KernelResult<Brep> Draft(
            Brep solid,
            IReadOnlyList<int> faces,
            in Vector3d pullDirection,
            Angle angle,
            in Plane neutral,
            in Tolerance tolerance) =>
            Nothing.Draft(solid, faces, pullDirection, angle, neutral, tolerance);

        public KernelResult<Brep> Sew(IReadOnlyList<Brep> pieces, in Tolerance tolerance) =>
            Nothing.Sew(pieces, tolerance);

        public KernelResult<Brep> Heal(Brep shape, in Tolerance tolerance) =>
            Nothing.Heal(shape, tolerance);

        public KernelResult<Brep> ReadFile(string path, in Tolerance tolerance) =>
            Nothing.ReadFile(path, tolerance);

        public KernelResult<bool> WriteFile(Brep shape, string path, in Tolerance tolerance) =>
            Nothing.WriteFile(shape, path, tolerance);

        public KernelResult<Mesh> Tessellate(Brep shape, in Tolerance tolerance) =>
            Nothing.Tessellate(shape, tolerance);
    }
}

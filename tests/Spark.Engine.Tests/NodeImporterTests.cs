using System;
using System.Linq;
using System.Reflection;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The importer's rules, exercised against the fixtures in <c>ImporterFixtures.cs</c> rather than
/// against the real library, because <c>Spark.Nodes.Core</c> is deliberately all static classes and
/// therefore never reaches the constructor, receiver, property, operator or dedup paths.
/// </summary>
public sealed class NodeImporterTests
{
    private const string Package = "Fixtures";

    /// <summary>ADR-0004: a constructor collapses into the factory that shadows it.</summary>
    [Fact]
    public void AConstructorIsSuppressedByAFactoryWithTheSameParameterTypes()
    {
        ImportReport report = Import(typeof(ImportedCircle));

        Assert.Contains(report.Nodes, node => node.Definition.DisplayName == "ImportedCircle.ByCentreRadius");
        Assert.DoesNotContain(report.Nodes, node => node.Member is ConstructorInfo);

        ExcludedMember suppressed = Assert.Single(
            report.Exclusions, exclusion => exclusion.Member is ConstructorInfo);

        Assert.Contains("ADR-0004", suppressed.Reason, StringComparison.Ordinal);
        Assert.Contains("ByCentreRadius", suppressed.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dedup matches parameter <b>types</b>, not names or counts. A factory whose shape differs
    /// leaves the constructor reachable, because ADR-0004's rule is that no member becomes
    /// unreachable.
    /// </summary>
    [Fact]
    public void AConstructorSurvivesWhenNoFactoryMatchesItsParameterTypes()
    {
        ImportReport report = Import(typeof(ImportedSegment));

        ImportedNode constructed = Assert.Single(report.Nodes, node => node.Member is ConstructorInfo);

        Assert.Equal("ImportedSegment.ByStartEnd", constructed.Definition.DisplayName);
        Assert.Equal(2, constructed.Definition.Inputs.Count);
        Assert.Equal("importedSegment", constructed.Definition.Outputs[0].Name);

        object?[] result = constructed.Definition.Invoke([2.0, 7.0]);
        Assert.Equal(new ImportedSegment(2, 7), result[0]);
    }

    /// <summary>An instance method takes its receiver as input port 0.</summary>
    [Fact]
    public void AnInstanceMethodTakesItsReceiverAsPortZero()
    {
        NodeDefinition reversed = Definition(Import(typeof(ImportedSegment)), "ImportedSegment.Reversed");

        Assert.Single(reversed.Inputs);
        Assert.Equal("importedSegment", reversed.Inputs[0].Name);
        Assert.Equal(typeof(ImportedSegment), reversed.Inputs[0].ValueType);
        Assert.Equal(new ImportedSegment(5, 1), reversed.Invoke([new ImportedSegment(1, 5)])[0]);
    }

    /// <summary>A property getter becomes a node; the accessor itself is accounted for separately.</summary>
    [Fact]
    public void APropertyGetterBecomesANode()
    {
        ImportReport report = Import(typeof(ImportedSegment));
        NodeDefinition length = Definition(report, "ImportedSegment.Length");

        Assert.Single(length.Inputs);
        Assert.Equal("length", length.Outputs[0].Name);
        Assert.Equal(4.0, length.Invoke([new ImportedSegment(1, 5)])[0]);

        Assert.Contains(
            report.Exclusions,
            exclusion => exclusion.Member is MethodInfo method && method.Name == "get_Length");
    }

    /// <summary>
    /// Everything the slice does not handle is excluded with a reason rather than skipped. A silent
    /// skip is exactly what lets a coverage test pass by accident.
    /// </summary>
    [Theory]
    [InlineData("Identity", "generic")]
    [InlineData("Mutate", "ref or in parameter")]
    [InlineData("DoNothing", "produces no value")]
    public void UnsupportedMembersAreExcludedWithAStatedReason(string memberName, string reasonFragment)
    {
        ImportReport report = Import(typeof(ImportedAwkward));

        ExcludedMember excluded = Assert.Single(
            report.Exclusions, exclusion => exclusion.Member.Name == memberName);

        Assert.Contains(reasonFragment, excluded.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(report.Nodes, node => node.Member.Name == memberName);
    }

    /// <summary>An operator is excluded rather than harvested, and says so.</summary>
    [Fact]
    public void OperatorsAreExcludedRatherThanHarvested()
    {
        ImportReport report = Import(typeof(ImportedAmount));

        ExcludedMember excluded = Assert.Single(
            report.Exclusions, exclusion => exclusion.Member.Name == "op_Addition");

        Assert.Contains("operator", excluded.Reason, StringComparison.OrdinalIgnoreCase);

        // The named form is the node, so the operation is still reachable.
        Assert.Contains(report.Nodes, node => node.Definition.DisplayName == "ImportedAmount.Add");
    }

    /// <summary><c>[NodeIgnore]</c> carries its author's reason through verbatim.</summary>
    [Fact]
    public void NodeIgnoreCarriesItsReasonThrough()
    {
        ImportReport report = Import(typeof(ImportedAwkward));

        ExcludedMember excluded = Assert.Single(
            report.Exclusions, exclusion => exclusion.Member.Name == "Internal");

        Assert.Equal("plumbing, not an operation", excluded.Reason);
    }

    /// <summary>
    /// Overloads stay one node each and are disambiguated by their parameter names, never by a
    /// numeric suffix: reflection does not guarantee member order, so a suffix would make the
    /// second overload's key differ between runs on the same source.
    /// </summary>
    [Fact]
    public void OverloadsAreDisambiguatedByParameterNames()
    {
        ImportReport report = Import(typeof(ImportedOverloads));

        string[] names = [.. report.Nodes
            .Select(node => node.Definition.DisplayName)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["ImportedOverloads.Combine(a, b)", "ImportedOverloads.Combine(a, b, c)"], names);
    }

    /// <summary>A type marked <c>[NodeIgnore]</c> contributes no nodes at all.</summary>
    [Fact]
    public void AnIgnoredTypeContributesNothing()
    {
        ImportReport report = Import(typeof(ImportedNothing));

        Assert.Empty(report.Nodes);
        ExcludedMember excluded = Assert.Single(report.Exclusions);
        Assert.Equal("a test fixture, not a library type", excluded.Reason);
    }

    /// <summary>
    /// The package half of the key is what keeps two libraries' <c>Curve.Offset</c> apart, so it has
    /// to be on every generated key.
    /// </summary>
    [Fact]
    public void EveryGeneratedKeyCarriesThePackage()
    {
        ImportReport report = Import(typeof(ImportedSegment));

        Assert.NotEmpty(report.Nodes);
        Assert.All(report.Nodes, node => Assert.Equal(Package, node.Definition.Key.Package));
    }

    /// <summary>
    /// A definition with no category at all still gets one, because the canvas has to colour it.
    /// </summary>
    [Fact]
    public void AnUncategorisedNodeFallsBackToCustom()
    {
        NodeDefinition keep = Definition(Import(typeof(ImportedAwkward)), "ImportedAwkward.Keep");

        Assert.Equal(Spark.Api.NodeCategories.Custom, keep.Category);
    }

    /// <summary>
    /// The library refuses a second definition under a key it already holds, rather than letting
    /// the later one win.
    /// </summary>
    /// <remarks>
    /// Last-one-wins would make the shadowed definition permanently unreachable, and a graph saved
    /// against it would bind to the other and produce geometry rather than an error — the worst
    /// shape a version conflict can take. <c>Spark.Nodes.Core</c> has no overloads today, so the
    /// coverage test's duplicate-key assertion cannot currently fail against the real library;
    /// this is the case that keeps the rule itself checked.
    /// </remarks>
    [Fact]
    public void TheLibraryRefusesADuplicateKey()
    {
        NodeLibrary library = new();
        library.Add(Definition(Import(typeof(ImportedOverloads)), "ImportedOverloads.Combine(a, b)"));

        Assert.Equal(1, library.Count);

        NodeDefinition clash = new(
            new NodeKey(Package, "ImportedOverloads.Combine(a, b)"),
            "A different node wearing the same key",
            [],
            [PortDefinition.Inferred("result", typeof(double))],
            _ => [0.0]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => library.Add(clash));

        Assert.Contains("already registered", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, library.Count);
    }


    /// <summary>
    /// The importer reads <see cref="NodeSideEffectAttribute"/> from the member.
    /// </summary>
    /// <remarks>
    /// The whole impurity mechanism was built and none of it was tested through the attribute:
    /// the one test that existed constructed a <see cref="NodeDefinition"/> by hand with
    /// <c>isSideEffect: true</c>, which exercises the engine and skips the only step a node
    /// author ever takes. Deleting the attribute check from the importer would have left every
    /// test green and made every impure node in every package silently pure — which is the
    /// worst failure available in a provenance cache, because it poisons nothing and therefore
    /// serves stale results forever without ever looking wrong.
    /// </remarks>
    [Fact]
    public void AnImpureMemberIsImportedAsImpure()
    {
        ImportReport report = Import(typeof(ImportedMixed));

        Assert.True(Definition(report, "ImportedMixed.Ticks").IsSideEffect);
        Assert.False(Definition(report, "ImportedMixed.Doubled").IsSideEffect);
    }

    /// <summary>
    /// The attribute is read from the declaring type as well, so that a package of nodes that
    /// all touch the same outside thing declares it once.
    /// </summary>
    [Fact]
    public void EveryMemberOfAnImpureTypeIsImportedAsImpure()
    {
        ImportReport report = Import(typeof(ImportedClock));

        Assert.True(Definition(report, "ImportedClock.Now").IsSideEffect);
        Assert.True(Definition(report, "ImportedClock.Later").IsSideEffect);
    }

    /// <summary>
    /// Nothing in the built-in library declares a side effect, and that is worth asserting
    /// rather than assuming.
    /// </summary>
    /// <remarks>
    /// Every node in <c>Spark.Nodes.Core</c> is a pure function of its inputs today. If one ever
    /// stops being one, this test fails and the author has to decide deliberately — which is the
    /// only moment at which the decision can be made correctly.
    /// </remarks>
    [Fact]
    public void NothingInTheBuiltInLibraryIsImpure()
    {
        ImportReport report = NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly);

        Assert.DoesNotContain(report.Nodes, node => node.Definition.IsSideEffect);
    }

    private static ImportReport Import(params Type[] types) => NodeImporter.Import(types, Package);

    private static NodeDefinition Definition(ImportReport report, string name) =>
        report.Nodes.Single(node => node.Definition.DisplayName == name).Definition;
}

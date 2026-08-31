using System;
using System.Linq;
using System.Reflection;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The Create / Action / Query axis the library panel files nodes on — <c>E8-T29</c>.
/// </summary>
/// <remarks>
/// <b>Dynamo reads this off the CLR member and Spark cannot.</b> A zero-touch node <i>is</i> the
/// member, so a constructor is a Create and a property getter is a Query; a Spark node is a static
/// method on a static facade, and every one of them looks identical to reflection. So the importer
/// infers from the ports and the naming convention, and <see cref="SparkNodeAttribute.Kind"/> is
/// how an author says otherwise. Both halves are asserted here, and the first-party library is
/// spot-checked against them — an inference nothing exercises is an inference that rots.
/// </remarks>
public sealed class NodeMemberKindTests
{
    /// <summary>A <c>By…</c> name is a Create, whatever its ports look like.</summary>
    [Fact]
    public void AFactoryNameIsCreate()
    {
        Assert.Equal(NodeMemberKind.Create, KindOf("Circle.ByCentreRadius"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Line.ByStartPointEndPoint"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Point.ByCoordinates"));
    }

    /// <summary>
    /// A node given nothing is making something, so it is a Create.
    /// </summary>
    /// <remarks>
    /// This is the rule that catches the constants — <c>Vector.ZAxis</c>, <c>Math.Pi</c>,
    /// <c>Plane.XY</c> — none of which is named like a factory and all of which plainly make a
    /// thing rather than doing something to one.
    /// </remarks>
    [Fact]
    public void ANodeWithNoInputsIsCreate()
    {
        Assert.Equal(NodeMemberKind.Create, KindOf("Vector.ZAxis"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Math.Pi"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Point.Origin"));
    }

    /// <summary>
    /// One input, and nothing coming back out of the same type: that is a report about the thing.
    /// </summary>
    /// <remarks>
    /// <b>The output type is what separates <c>Curve.Length</c> from <c>Curve.Reverse</c></b>, and
    /// it is the whole of the rule. Both take exactly one curve; one hands back a number and the
    /// other hands back a curve, and only the second is doing something <i>to</i> it.
    /// </remarks>
    [Fact]
    public void OneInputAndADifferentOutputIsQuery()
    {
        Assert.Equal(NodeMemberKind.Query, KindOf("Curve.Length"));
        Assert.Equal(NodeMemberKind.Query, KindOf("Curve.IsClosed"));
        Assert.Equal(NodeMemberKind.Query, KindOf("Solid.Volume"));

        Assert.Equal(NodeMemberKind.Action, KindOf("Curve.Reverse"));
    }

    /// <summary>Everything the first two rules did not claim is an Action.</summary>
    [Fact]
    public void EverythingElseIsAction()
    {
        Assert.Equal(NodeMemberKind.Action, KindOf("Point.Translate"));
        Assert.Equal(NodeMemberKind.Action, KindOf("Solid.Union"));
        Assert.Equal(NodeMemberKind.Action, KindOf("Math.Divide"));
    }

    /// <summary>
    /// The attribute overrides the inference, and the first-party library relies on it.
    /// </summary>
    /// <remarks>
    /// <c>Number.Value</c> takes a number and returns one, so every structural rule calls it an
    /// action; it is plainly a way of <i>making</i> a number and it is the node a user reaches for
    /// first. <c>Solid.Box</c> is the same shape as <c>Solid.Union</c> to reflection and the
    /// opposite thing to a person. This is what <see cref="SparkNodeAttribute.Kind"/> is for.
    /// </remarks>
    [Fact]
    public void TheAttributeOverridesTheInference()
    {
        Assert.Equal(NodeMemberKind.Create, KindOf("Number.Value"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Number.Range"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Solid.Box"));
        Assert.Equal(NodeMemberKind.Create, KindOf("Surface.Sphere"));
        Assert.Equal(NodeMemberKind.Query, KindOf("List.FirstItem"));
        Assert.Equal(NodeMemberKind.Action, KindOf("List.Flatten"));
    }

    /// <summary>
    /// No imported node carries the <see cref="NodeMemberKind.Auto"/> sentinel.
    /// </summary>
    /// <remarks>
    /// The same invariant <see cref="LacingMode.Auto"/> has: "not stated" is resolved before a
    /// definition exists, so nothing downstream has to decide what an unresolved value means. The
    /// constructor refuses it, and this asserts that the whole library gets past that refusal.
    /// </remarks>
    [Fact]
    public void NoDefinitionIsLeftOnAuto()
    {
        Assert.All(Library().Definitions(), d => Assert.NotEqual(NodeMemberKind.Auto, d.MemberKind));
    }

    /// <summary>A definition cannot store the sentinel, exactly as it cannot store Auto lacing.</summary>
    [Fact]
    public void AutoIsRefusedByTheConstructor()
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(() => new NodeDefinition(
            new NodeKey("Test", "Test.Node"),
            "Test.Node",
            [],
            [new PortDefinition("value", typeof(double), 0)],
            _ => [0.0],
            memberKind: NodeMemberKind.Auto));

        Assert.Contains("Auto", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every category holds at least one kind, and the three between them hold every node.
    /// </summary>
    /// <remarks>
    /// The panel builds its subgroups by filtering the category's entries three ways
    /// (<c>LibraryGroupViewModel</c>), so a node whose kind was none of the three would vanish from
    /// the library without any error being raised anywhere.
    /// </remarks>
    [Fact]
    public void TheThreeKindsPartitionTheLibrary()
    {
        NodeDefinition[] all = [.. Library().Definitions()];

        int create = all.Count(d => d.MemberKind == NodeMemberKind.Create);
        int action = all.Count(d => d.MemberKind == NodeMemberKind.Action);
        int query = all.Count(d => d.MemberKind == NodeMemberKind.Query);

        Assert.Equal(all.Length, create + action + query);
        Assert.True(create > 0);
        Assert.True(action > 0);
        Assert.True(query > 0);
    }

    private static NodeMemberKind KindOf(string displayName)
    {
        NodeDefinition definition = Library().Definitions()
            .Single(d => string.Equals(d.DisplayName, displayName, StringComparison.Ordinal));

        return definition.MemberKind;
    }

    private static NodeLibrary Library()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(Assembly.Load("Spark.Nodes.Core")));
        return library;
    }
}

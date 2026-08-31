using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;
using Spark.Geometry;
using Spark.Scripting;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Declaring the type of a code block's input port — `E6-T11`.
/// </summary>
/// <remarks>
/// <para>
/// <b>A wire is not the only way to learn a type, and before this it was the only one.</b> An
/// unwired port is <c>dynamic</c>, so a user typing <c>radius.</c> into the editor was offered the
/// members of <c>object</c> — <c>Equals</c>, <c>GetHashCode</c>, <c>ToString</c> — which is worse
/// than offering nothing, because it looks like an answer. That is what a person actually hit, and
/// it is what these are about.
/// </para>
/// <para>
/// <b>The precedence is the part with a decision in it.</b> A declaration beats the wire. The wire
/// is the better source whenever there is one, but a declaration is an instruction the user typed,
/// and offering a setting that is then quietly overruled is worse than not offering it.
/// </para>
/// </remarks>
public sealed class DeclaredInputTypeTests
{
    /// <summary>
    /// <b>The case the feature exists for: no wire, and a real type on the port anyway.</b>
    /// </summary>
    [Fact]
    public void ADeclaredTypeReachesAnUnwiredPort()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre;");

        Assert.Equal(typeof(object), PortType(graph, block));

        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(Point3d)));

        Assert.Equal(typeof(Point3d), PortType(graph, block));
    }

    /// <summary>
    /// The declaration reaches the compiler on a block that uses the type's members, and the block
    /// still has its output afterwards.
    /// </summary>
    /// <remarks>
    /// That the member resolves <i>at compile time</i> rather than through <c>dynamic</c> is
    /// asserted at the factory, by invoking — see
    /// <see cref="TypedScriptInputTests.ATypedInputResolvesItsMembersAtCompileTime"/>. What is new
    /// here is that the canvas gets the declaration that far.
    /// </remarks>
    [Fact]
    public void ADeclaredTypeReachesABlockThatUsesItsMembers()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre.X;");

        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(Point3d)));

        Assert.Equal(typeof(Point3d), PortType(graph, block));
        Assert.Single(graph.Engine.Node(block).Definition.Outputs);
    }

    /// <summary>
    /// <b>A declaration beats the wire.</b> The setting the user made wins over the one inferred
    /// for them, because a control that is silently overruled is worse than no control.
    /// </summary>
    [Fact]
    public void ADeclarationBeatsTheWire()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre;");
        NodeId point = AddPoint(graph);

        Assert.True(Connect(graph, point, 0, block, 0));
        Assert.Equal(typeof(Point3d), PortType(graph, block));

        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(object)));

        Assert.Equal(typeof(object), PortType(graph, block));
    }

    /// <summary>Clearing it hands the port back to whatever is wired in.</summary>
    [Fact]
    public void ClearingADeclarationGoesBackToTheWire()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre;");
        NodeId point = AddPoint(graph);

        Assert.True(Connect(graph, point, 0, block, 0));
        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(object)));
        Assert.Equal(typeof(object), PortType(graph, block));

        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", type: null));

        Assert.Equal(typeof(Point3d), PortType(graph, block));
    }

    /// <summary>
    /// <b>A declaration survives an edit to the script</b>, which is the thing that would make the
    /// feature useless if it did not. Rebuilding destroys the node instance the declaration lives
    /// on, so <c>ReplaceDefinition</c> carries it across by name, as it already does for the wires.
    /// </summary>
    [Fact]
    public void ADeclarationSurvivesAnEditToTheScript()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre;");

        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(Point3d)));

        CanvasNode node = graph.Nodes[graph.SlotOf(block)];
        const string Edited = "var offset = 1.0; return centre.X + offset;";

        Assert.True(graph.ReplaceDefinition(
            node,
            NodeDefinition.FromScript(
                Scripts.Create(Edited, graph.Engine.InputTypes(block)), Edited)));

        Assert.Equal(typeof(Point3d), PortType(graph, block));
    }

    /// <summary>
    /// <b>A declaration for a name that is not a port is kept, but does not reach the compiler.</b>
    /// Deleting a line and putting it back is ordinary while writing, so the declaration waits;
    /// but a type for a port that does not exist must not be handed to the script generator as if
    /// it did.
    /// </summary>
    [Fact]
    public void ADeclarationForAPortThatIsNotThereIsKeptButNotApplied()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre;");

        graph.Engine.SetDeclaredInputType(block, "radius", typeof(double));

        Assert.DoesNotContain("radius", graph.Engine.InputTypes(block).Keys);
        Assert.Equal(typeof(double), graph.Engine.DeclaredInputTypes(block)["radius"]);
    }

    /// <summary>Setting the same type twice does not churn the node.</summary>
    [Fact]
    public void DeclaringTheSameTypeTwiceRebuildsNothing()
    {
        (CanvasGraph graph, NodeId block) = Block("return centre;");

        Assert.True(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(Point3d)));
        Assert.False(graph.SetDeclaredInputType(graph.SlotOf(block), "centre", typeof(Point3d)));
    }

    /// <summary>Every token in the catalogue resolves, and round-trips back to itself.</summary>
    [Fact]
    public void TheCatalogueRoundTripsThroughItsTokens()
    {
        Assert.NotEmpty(ScriptInputTypes.Catalogue);

        foreach ((string token, Type type) in ScriptInputTypes.Catalogue)
        {
            Assert.Equal(type, ScriptInputTypes.Resolve(token));
            Assert.Equal(token, ScriptInputTypes.TokenFor(type));
        }
    }

    /// <summary>
    /// A token this build does not know loses the setting and nothing else. That is what a file
    /// written by a later version of Spark looks like, and it must not cost the document.
    /// </summary>
    [Fact]
    public void AnUnknownTokenResolvesToNothing()
    {
        Assert.Null(ScriptInputTypes.Resolve("quaternion-field"));
        Assert.Null(ScriptInputTypes.Resolve(null));
        Assert.Null(ScriptInputTypes.TokenFor(typeof(DeclaredInputTypeTests)));
    }

    private static readonly ScriptNodeFactory Scripts = MakeFactory();

    private static ScriptNodeFactory MakeFactory()
    {
        _ = typeof(Point3d).Assembly.Location;
        return new ScriptNodeFactory();
    }

    private static (CanvasGraph Graph, NodeId Block) Block(string script)
    {
        CanvasGraph graph = new() { Scripts = Scripts };
        int slot = graph.Add(NodeDefinition.FromScript(Scripts.Create(script), script), 0, 0);

        return (graph, graph.Nodes[slot].Id);
    }

    private static NodeId AddPoint(CanvasGraph graph)
    {
        int slot = graph.Add(TestGraphs.Library.ByName("Point.ByCoordinates"), 0, 200);

        return graph.Nodes[slot].Id;
    }

    /// <summary>Connects by identity, because re-typing moves slots underneath a test.</summary>
    private static bool Connect(
        CanvasGraph graph, NodeId source, int sourcePort, NodeId target, int targetPort) =>
        graph.TryConnect(
            new CanvasPort(graph.SlotOf(source), sourcePort, IsOutput: true),
            new CanvasPort(graph.SlotOf(target), targetPort, IsOutput: false));

    private static Type PortType(CanvasGraph graph, NodeId id) =>
        graph.Engine.Node(id).Definition.Inputs[0].ValueType;
}

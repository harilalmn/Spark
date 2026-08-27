using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// Graph construction, wire validation and dirty propagation.
/// </summary>
public sealed class GraphTests
{
    /// <summary>A compatible wire is created and reports which rule let it through.</summary>
    [Fact]
    public void ACompatibleWireIsAcceptedAndNamesTheRuleThatAllowedIt()
    {
        Graph graph = new();
        NodeInstance source = graph.AddNode(LacingNodes.Add);
        NodeInstance target = graph.AddNode(LacingNodes.Add);

        ConnectionResult result = graph.TryConnect(source.Id, 0, target.Id, 0);

        Assert.True(result.Accepted);
        Assert.Equal(PortCompatibility.Direct, result.Compatibility);
        Assert.Null(result.Diagnostic);
        Assert.Single(graph.Wires());
    }

    /// <summary>
    /// An incompatible wire is refused when it is drawn, not when the graph runs. That is what lets
    /// the canvas show a red wire under the cursor instead of a red node after a run.
    /// </summary>
    [Fact]
    public void AnIncompatibleWireIsRefusedAtCreationTimeAndNoWireIsAdded()
    {
        Graph graph = new();
        NodeInstance counter = graph.AddNode(LacingNodes.ListCount);
        NodeInstance circle = graph.AddNode(LacingNodes.CircleByCenterRadius);

        // int -> Point3d matches no rule in the order.
        ConnectionResult result = graph.TryConnect(counter.Id, 0, circle.Id, 0);

        Assert.False(result.Accepted);
        Assert.Equal(DiagnosticCodes.IncompatiblePortTypes, result.Diagnostic?.Code);
        Assert.Empty(graph.Wires());
    }

    /// <summary>A wire that would close a cycle is refused, and the graph is left exactly as it was.</summary>
    [Fact]
    public void AWireThatWouldCloseACycleIsRefused()
    {
        Graph graph = new();
        NodeInstance first = graph.AddNode(LacingNodes.Add);
        NodeInstance second = graph.AddNode(LacingNodes.Add);

        Assert.True(graph.TryConnect(first.Id, 0, second.Id, 0).Accepted);

        ConnectionResult closing = graph.TryConnect(second.Id, 0, first.Id, 0);

        Assert.False(closing.Accepted);
        Assert.Equal(DiagnosticCodes.WireWouldCloseCycle, closing.Diagnostic?.Code);
        Assert.Single(graph.Wires());
    }

    /// <summary>A node cannot be wired to itself either, and the cycle is what is reported.</summary>
    [Fact]
    public void ANodeCannotBeWiredToItself()
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(LacingNodes.Add);

        ConnectionResult result = graph.TryConnect(node.Id, 0, node.Id, 0);

        Assert.False(result.Accepted);
        Assert.Equal(DiagnosticCodes.WireWouldCloseCycle, result.Diagnostic?.Code);
    }

    /// <summary>
    /// An input port takes at most one wire. Dropping a second onto an occupied port replaces the
    /// first, which is what the gesture means to the person making it.
    /// </summary>
    [Fact]
    public void ASecondWireOntoOneInputPortReplacesTheFirst()
    {
        Graph graph = new();
        NodeInstance first = graph.AddNode(LacingNodes.Add);
        NodeInstance second = graph.AddNode(LacingNodes.Add);
        NodeInstance target = graph.AddNode(LacingNodes.Add);

        graph.TryConnect(first.Id, 0, target.Id, 0);
        graph.TryConnect(second.Id, 0, target.Id, 0);

        Wire wire = Assert.Single(graph.IncomingWires(target.Id));
        Assert.Equal(second.Id, wire.Source);
    }

    /// <summary>Removing a node takes its wires with it, in both directions.</summary>
    [Fact]
    public void RemovingANodeRemovesEveryWireTouchingIt()
    {
        Graph graph = new();
        NodeInstance upstream = graph.AddNode(LacingNodes.Add);
        NodeInstance middle = graph.AddNode(LacingNodes.Add);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);

        graph.TryConnect(upstream.Id, 0, middle.Id, 0);
        graph.TryConnect(middle.Id, 0, downstream.Id, 0);

        Assert.True(graph.RemoveNode(middle.Id));

        Assert.Empty(graph.Wires());
        Assert.Empty(graph.IncomingWires(downstream.Id));
        Assert.Empty(graph.OutgoingWires(upstream.Id));
    }

    /// <summary>
    /// Changing a literal marks the node and everything downstream of it dirty, and nothing upstream.
    /// </summary>
    [Fact]
    public void ChangingALiteralMarksTheNodeAndEverythingDownstreamDirty()
    {
        Graph graph = new();
        NodeInstance upstream = graph.AddNode(LacingNodes.Add);
        NodeInstance middle = graph.AddNode(LacingNodes.Add);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);

        graph.TryConnect(upstream.Id, 0, middle.Id, 0);
        graph.TryConnect(middle.Id, 0, downstream.Id, 0);
        graph.MarkAllClean();

        graph.SetLiteral(middle.Id, 1, 5.0);

        IReadOnlySet<NodeId> dirty = graph.DirtyNodes();
        Assert.Contains(middle.Id, dirty);
        Assert.Contains(downstream.Id, dirty);
        Assert.DoesNotContain(upstream.Id, dirty);
    }

    /// <summary>Changing lacing is a change to the result, so it dirties the same set.</summary>
    [Fact]
    public void ChangingLacingMarksTheNodeAndEverythingDownstreamDirty()
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(LacingNodes.Add);
        NodeInstance downstream = graph.AddNode(LacingNodes.Add);

        graph.TryConnect(node.Id, 0, downstream.Id, 0);
        graph.MarkAllClean();

        graph.SetLacing(node.Id, LacingMode.CrossProduct);

        Assert.Contains(node.Id, graph.DirtyNodes());
        Assert.Contains(downstream.Id, graph.DirtyNodes());
    }

    /// <summary>A freshly placed node carries Auto, which is how the graph records "not overridden".</summary>
    [Fact]
    public void AFreshlyPlacedNodeCarriesAutoAndResolvesToItsDefinitionsDefault()
    {
        Graph graph = new();

        NodeInstance add = graph.AddNode(LacingNodes.Add);
        NodeInstance grid = graph.AddNode(LacingNodes.GridByXY);

        Assert.Equal(LacingMode.Auto, add.Lacing);
        Assert.Equal(LacingMode.Auto, grid.Lacing);

        // Two nodes both reading "Auto" that resolve differently. That is the point, not a bug.
        Assert.Equal(LacingMode.Longest, add.EffectiveLacing);
        Assert.Equal(LacingMode.CrossProduct, grid.EffectiveLacing);
    }

    /// <summary>
    /// A definition whose declared default is itself <c>Auto</c> is refused: there is exactly one
    /// hop, never a chain to resolve.
    /// </summary>
    [Fact]
    public void ADefinitionCannotDeclareAutoAsItsDefaultLacing()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NodeDefinition(
            new NodeKey("Test", "Bad"),
            "Bad",
            [],
            [new PortDefinition("result", typeof(double), 0)],
            _ => [0.0],
            LacingMode.Auto));

        Assert.Contains("Auto", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A definition with no output ports has nothing a graph can carry, and is refused.</summary>
    [Fact]
    public void ADefinitionWithNoOutputPortsIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new NodeDefinition(
            new NodeKey("Test", "Bad"),
            "Bad",
            [],
            [],
            _ => []));
    }

    /// <summary>A node key carries package identity, so the same name from two packages is two keys.</summary>
    [Fact]
    public void ANodeKeyDistinguishesTheSameNameFromDifferentPackages()
    {
        NodeKey ours = new("Spark.Nodes.Core", "Curve.Offset");
        NodeKey theirs = new("Acme.Nodes", "Curve.Offset");

        Assert.NotEqual(ours, theirs);
        Assert.Equal("Spark.Nodes.Core/Curve.Offset", ours.Value);
        Assert.Equal(ours, NodeKey.Parse(ours.Value));
    }

    /// <summary>Node identities are minted fresh and never collide.</summary>
    [Fact]
    public void AddingANodeWithAnIdentityAlreadyInUseIsRefused()
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(LacingNodes.Add);

        Assert.Throws<ArgumentException>(() => graph.AddNode(LacingNodes.Add, node.Id));
    }

    /// <summary>The collections a graph hands out are snapshots; mutating one cannot reshape the graph.</summary>
    [Fact]
    public void TheCollectionsAGraphHandsOutAreSnapshots()
    {
        Graph graph = new();
        NodeInstance node = graph.AddNode(LacingNodes.Add);

        IReadOnlyList<NodeInstance> before = graph.Nodes();
        graph.AddNode(LacingNodes.Add);

        Assert.Single(before);
        Assert.Equal(2, graph.Nodes().Count);
        Assert.Equal(node.Id, before[0].Id);
    }
}

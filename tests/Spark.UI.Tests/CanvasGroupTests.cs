using System;
using System.Linq;
using Spark.Engine;
using Spark.UI.Canvas;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// Groups as the model holds them: membership by identity, a frame derived from it, and the
/// promise that removing the frame keeps the work.
/// </summary>
/// <remarks>
/// The load-bearing decision here is that a group stores <b>which nodes it contains</b> and derives
/// its rectangle, rather than storing a rectangle and deciding membership by containment. The
/// second is what most editors do and it is why a node can quietly join a group it was merely
/// dragged past. <see cref="AGroupsMembershipDoesNotChangeWhenANodeIsDraggedOutOfItsFrame"/> is the
/// test that pins the choice.
/// </remarks>
public sealed class CanvasGroupTests
{
    [Fact]
    public void AGroupFramesTheNodesItContains()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();

        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0, 1]));
        CanvasBounds frame = Assert.IsType<CanvasBounds>(graph.GroupBounds(group));

        foreach (CanvasNode node in graph.Nodes)
        {
            Assert.True(frame.MinX <= node.Bounds.MinX, "The frame must start left of every member.");
            Assert.True(frame.MaxX >= node.Bounds.MaxX, "The frame must end right of every member.");
            Assert.True(frame.MaxY >= node.Bounds.MaxY, "The frame must end below every member.");
        }

        // Room above the members for the title strip, which is also the only part that takes a
        // click - so it cannot be zero-height.
        Assert.True(frame.MinY <= graph.Nodes.Min(n => n.Bounds.MinY) - CanvasGroup.TitleHeight);
    }

    /// <summary>
    /// <b>The decision, asserted.</b> Dragging a node clean out of its group's frame does not
    /// remove it from the group — the frame follows the node instead. Membership changes only when
    /// somebody asks for it.
    /// </summary>
    [Fact]
    public void AGroupsMembershipDoesNotChangeWhenANodeIsDraggedOutOfItsFrame()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0, 1]));

        graph.Nodes[1].X += 5000;

        Assert.Equal(2, group.Members.Count);

        CanvasBounds frame = Assert.IsType<CanvasBounds>(graph.GroupBounds(group));
        Assert.True(frame.MaxX >= graph.Nodes[1].Bounds.MaxX, "The frame follows the member out.");
    }

    /// <summary>
    /// The other half of the same decision: a node dragged <i>into</i> a group's frame does not
    /// join it.
    /// </summary>
    [Fact]
    public void ANodeDraggedIntoAFrameDoesNotJoinTheGroup()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0]));

        graph.Nodes[1].X = graph.Nodes[0].X;
        graph.Nodes[1].Y = graph.Nodes[0].Y;

        Assert.Single(group.Members);
        Assert.False(group.Contains(graph.Nodes[1].Id));
    }

    /// <summary>
    /// <b>Ungrouping never deletes work.</b> The frame goes; every node it framed stays.
    /// </summary>
    [Fact]
    public void RemovingAGroupKeepsItsNodes()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        int nodes = graph.Nodes.Count;
        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0, 1]));

        Assert.True(graph.RemoveGroup(group));

        Assert.Empty(graph.Groups);
        Assert.Equal(nodes, graph.Nodes.Count);
    }

    /// <summary>
    /// A deleted node leaves the groups it was in, because a membership that cannot be found is a
    /// membership nothing can act on.
    /// </summary>
    [Fact]
    public void DeletingANodeTakesItOutOfItsGroups()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        NodeId doomed = graph.Nodes[1].Id;
        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0, 1]));

        graph.Remove(1);

        Assert.Single(group.Members);
        Assert.False(group.Contains(doomed));
    }

    /// <summary>
    /// A group whose last member is deleted goes with it. A frame around nothing is not a frame,
    /// and it would be invisible and undeletable — its own hit-test needs a rectangle.
    /// </summary>
    [Fact]
    public void AGroupWhoseLastMemberIsDeletedGoesToo()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        graph.AddGroup([1]);

        graph.Remove(1);

        Assert.Empty(graph.Groups);
    }

    [Fact]
    public void AGroupOverNoNodesIsNotCreated()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();

        Assert.Null(graph.AddGroup([]));
        Assert.Null(graph.AddGroup([99]));
        Assert.Empty(graph.Groups);
    }

    [Fact]
    public void AGroupRoundTripsThroughAFileWithItsMembershipIntact()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0, 1], "Radii"));

        CanvasGraph reopened = CanvasDocument.Open(CanvasDocument.Save(graph), TestGraphs.Library);

        CanvasGroup restored = Assert.Single(reopened.Groups);
        Assert.Equal(group.Id, restored.Id);
        Assert.Equal("Radii", restored.Title);
        Assert.Equal(2, restored.Members.Count);

        foreach (CanvasNode node in reopened.Nodes)
        {
            Assert.True(restored.Contains(node.Id), "Membership is by identity, which survives a file.");
        }
    }

    [Fact]
    public void AGraphWithGroupsReSavesByteForByte()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        graph.AddGroup([0, 1], "Both");

        string first = CanvasDocument.Save(graph);

        Assert.Equal(first, CanvasDocument.Save(CanvasDocument.Open(first, TestGraphs.Library)));
    }

    /// <summary>
    /// A group's frame reaches beyond its members by the padding and the title strip, so
    /// <i>Zoom to fit</i> has to fit the frame. Fitting the members exactly clips the title off
    /// the top of the window — which is where the group's name is, and the only part of it a
    /// pointer can grab.
    /// </summary>
    [Fact]
    public void ComputingBoundsIncludesTheGroupFrameAndNotJustItsMembers()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        CanvasBounds withoutGroup = graph.ComputeBounds();

        CanvasGroup group = Assert.IsType<CanvasGroup>(graph.AddGroup([0, 1], "Both"));
        CanvasBounds frame = Assert.IsType<CanvasBounds>(graph.GroupBounds(group));
        CanvasBounds withGroup = graph.ComputeBounds();

        Assert.True(withGroup.MinY < withoutGroup.MinY, "The title strip is above the members.");
        Assert.True(withGroup.MinY <= frame.MinY);
        Assert.True(withGroup.MinX <= frame.MinX);
    }

    /// <summary>
    /// Groups and notes share a format version. Inventing a version 3 for the second field to land
    /// in the same week would refuse a file to a reader that can in fact read it.
    /// </summary>
    [Fact]
    public void AGraphWithGroupsIsWrittenAsVersionTwo()
    {
        CanvasGraph graph = TestGraphs.SourceAndSink();
        graph.AddGroup([0], "One");

        Assert.Contains("\"formatVersion\": 2", CanvasDocument.Save(graph), StringComparison.Ordinal);
    }

}

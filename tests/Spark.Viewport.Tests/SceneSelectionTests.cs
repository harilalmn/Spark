using System;
using System.Linq;
using Spark.Viewport;

namespace Spark.Viewport.Tests;

/// <summary>
/// Selection reaching the viewport (<c>E9-T9</c>).
/// </summary>
/// <remarks>
/// The claim this row has made since M0 is that selection synchronisation <b>falls out of
/// node-keyed identity with no extra bookkeeping</b>. These tests are what turns that from an
/// argument into a fact: the canvas knows node ids, the scene is keyed by
/// <c>(NodeId, PortIndex)</c>, and nothing in between has to be maintained.
/// </remarks>
public sealed class SceneSelectionTests
{
    /// <summary>Selecting a node marks every package it produced, across all its output ports.</summary>
    [Fact]
    public void SelectingANodeMarksEveryPackageItProduced()
    {
        ViewportScene scene = Populated();

        Assert.True(scene.SetSelectedNodes(["alpha"]));

        Assert.All(
            scene.Snapshot().Where(p => p.Key.NodeId == "alpha"),
            package => Assert.True(package.Appearance.IsSelected));
        Assert.All(
            scene.Snapshot().Where(p => p.Key.NodeId != "alpha"),
            package => Assert.False(package.Appearance.IsSelected));
    }

    /// <summary>Selecting nothing clears it, rather than leaving the last selection outlined.</summary>
    [Fact]
    public void SelectingNothingClearsTheHighlight()
    {
        ViewportScene scene = Populated();
        scene.SetSelectedNodes(["alpha"]);

        Assert.True(scene.SetSelectedNodes([]));

        Assert.All(scene.Snapshot(), package => Assert.False(package.Appearance.IsSelected));
    }

    /// <summary>
    /// <b>The geometry is not rebuilt.</b> Appearance is a uniform, not a buffer, and the renderer
    /// recognises a package whose arrays are the same object. Selecting a node with a million
    /// triangles on it must not re-upload them.
    /// </summary>
    [Fact]
    public void SelectingSharesTheGeometryArraysRatherThanCopyingThem()
    {
        ViewportScene scene = Populated();
        RenderPackage before = scene.Snapshot().First(p => p.Key.NodeId == "alpha");

        scene.SetSelectedNodes(["alpha"]);
        RenderPackage after = scene.Snapshot().First(p => p.Key.NodeId == "alpha");

        Assert.NotSame(before, after);
        Assert.True(before.Positions.Overlaps(after.Positions), "the position buffer should be shared");
        Assert.True(before.Indices.Overlaps(after.Indices), "the index buffer should be shared");
    }

    /// <summary>
    /// Setting the same selection twice reports no change, so a caller can skip a repaint. Without
    /// this every mouse move over a selected node would redraw the viewport.
    /// </summary>
    [Fact]
    public void SettingTheSameSelectionTwiceReportsNoChange()
    {
        ViewportScene scene = Populated();

        Assert.True(scene.SetSelectedNodes(["alpha"]));
        Assert.False(scene.SetSelectedNodes(["alpha"]));
    }

    /// <summary>
    /// Selecting a node with no geometry changes nothing and is not an error. Most nodes on a
    /// canvas produce numbers.
    /// </summary>
    [Fact]
    public void SelectingANodeWithNoGeometryIsHarmless()
    {
        ViewportScene scene = Populated();

        Assert.False(scene.SetSelectedNodes(["a-node-that-drew-nothing"]));
    }

    /// <summary>A null selection is refused rather than treated as an empty one.</summary>
    [Fact]
    public void ANullSelectionThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new ViewportScene().SetSelectedNodes(null!));
    }

    /// <summary>The scene's version moves when the selection does, so a renderer knows to redraw.</summary>
    [Fact]
    public void TheSceneVersionMovesWhenTheSelectionChanges()
    {
        ViewportScene scene = Populated();
        long before = scene.Version;

        scene.SetSelectedNodes(["alpha"]);

        Assert.True(scene.Version > before);
    }

    /// <summary>Two output ports on one node, plus a second node, so both directions are covered.</summary>
    private static ViewportScene Populated()
    {
        ViewportScene scene = new();
        scene.Set(Triangle(new GeometryKey("alpha", 0)));
        scene.Set(Triangle(new GeometryKey("alpha", 1)));
        scene.Set(Triangle(new GeometryKey("beta", 0)));
        return scene;
    }

    private static RenderPackage Triangle(GeometryKey key) => new(
        key,
        "0",
        [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f],
        [0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f],
        [0, 1, 2],
        [0, 1, 1, 2, 2, 0],
        Appearance.Default);
}

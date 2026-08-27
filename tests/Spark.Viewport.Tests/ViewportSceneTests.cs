using System;
using System.Numerics;
using Spark.Viewport;
using Spark.Viewport.Meshes;

namespace Spark.Viewport.Tests;

/// <summary>
/// The scene's node-keyed identity, which is what makes re-evaluating one node re-upload one
/// buffer and what makes selection synchronisation fall out for free.
/// </summary>
public sealed class ViewportSceneTests
{
    [Fact]
    public void SettingTheSameKeyTwiceReplacesRatherThanAccumulates()
    {
        ViewportScene scene = new();
        GeometryKey key = new("node-1", 0);

        scene.Set(Package(key));
        scene.Set(Package(key));

        Assert.Equal(1, scene.Count);
    }

    [Fact]
    public void DifferentPortsOfOneNodeAreDifferentSlots()
    {
        ViewportScene scene = new();
        scene.Set(Package(new GeometryKey("node-1", 0)));
        scene.Set(Package(new GeometryKey("node-1", 1)));

        Assert.Equal(2, scene.Count);
    }

    [Fact]
    public void RemovingANodeTakesEveryPortWithIt()
    {
        ViewportScene scene = new();
        scene.Set(Package(new GeometryKey("doomed", 0)));
        scene.Set(Package(new GeometryKey("doomed", 1)));
        scene.Set(Package(new GeometryKey("kept", 0)));

        Assert.Equal(2, scene.RemoveNode("doomed"));
        Assert.Equal(1, scene.Count);
        Assert.Single(scene.Snapshot());
        Assert.Equal("kept", scene.Snapshot()[0].Key.NodeId);
    }

    [Fact]
    public void TheVersionRisesOnEveryMutationAndNotOtherwise()
    {
        ViewportScene scene = new();
        long start = scene.Version;

        scene.Set(Package(new GeometryKey("a", 0)));
        long afterSet = scene.Version;
        Assert.True(afterSet > start);

        // Removing something that is not there is not a mutation, and a renderer that skips work
        // on an unchanged version must not be woken by one.
        Assert.False(scene.Remove(new GeometryKey("missing", 0)));
        Assert.Equal(afterSet, scene.Version);

        scene.Clear();
        Assert.True(scene.Version > afterSet);

        long afterClear = scene.Version;
        scene.Clear();
        Assert.Equal(afterClear, scene.Version);
    }

    [Fact]
    public void TheSnapshotIsCachedUntilTheSceneChanges()
    {
        ViewportScene scene = new();
        scene.Set(Package(new GeometryKey("a", 0)));

        RenderPackage[] first = scene.Snapshot();
        Assert.Same(first, scene.Snapshot());

        scene.Set(Package(new GeometryKey("b", 0)));
        Assert.NotSame(first, scene.Snapshot());
    }

    [Fact]
    public void SceneBoundsAreTheUnionOfEveryPackage()
    {
        ViewportScene scene = new();
        scene.Set(PrimitiveMeshes.Box(new Vector3(-1, -1, -1), new Vector3(0, 0, 0))
            .ToRenderPackage(new GeometryKey("a", 0), "solid", Appearance.Default));
        scene.Set(PrimitiveMeshes.Box(new Vector3(2, 2, 2), new Vector3(4, 4, 4))
            .ToRenderPackage(new GeometryKey("b", 0), "solid", Appearance.Default));

        Bounds3 bounds = scene.ComputeBounds();

        Assert.Equal(new Vector3(-1, -1, -1), bounds.Min);
        Assert.Equal(new Vector3(4, 4, 4), bounds.Max);
    }

    [Fact]
    public void AnEmptySceneHasEmptyBounds() => Assert.True(new ViewportScene().ComputeBounds().IsEmpty);

    [Fact]
    public void APackageCopiesTheArraysItIsGiven()
    {
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        RenderPackage package = new(
            new GeometryKey("a", 0), "solid", positions, [], [0, 1, 2], [], Appearance.Default);

        positions[0] = 99;

        // The producer runs on a worker thread and the consumer on the render thread. Sharing the
        // arrays would make the package immutable only by convention, and the first bug it caused
        // would present as a flickering triangle rather than as anything that looks like a race.
        Assert.Equal(0, package.Positions[0]);
    }

    [Fact]
    public void ChangingOnlyTheAppearanceSharesTheGeometry()
    {
        RenderPackage original = Package(new GeometryKey("a", 0));
        RenderPackage selected = original.WithAppearance(Appearance.Default with { IsSelected = true });

        Assert.NotSame(original, selected);
        Assert.True(selected.Appearance.IsSelected);

        // Selecting an object must not re-tessellate it and must not re-upload its buffers.
        Assert.Same(original.PositionData, selected.PositionData);
        Assert.Same(original.IndexData, selected.IndexData);
    }

    [Fact]
    public void AnUnchangedAppearanceReturnsTheSameInstance()
    {
        RenderPackage original = Package(new GeometryKey("a", 0));

        Assert.Same(original, original.WithAppearance(original.Appearance));
    }

    [Fact]
    public void APackageRefusesAPositionArrayThatIsNotWholeTriples() =>
        Assert.Throws<ArgumentException>(() => new RenderPackage(
            new GeometryKey("a", 0), "solid", [0, 0], [], [], [], Appearance.Default));

    [Fact]
    public void APackageRefusesNormalsThatDoNotMatchThePositionCount() =>
        Assert.Throws<ArgumentException>(() => new RenderPackage(
            new GeometryKey("a", 0), "solid", [0, 0, 0], [1, 0, 0, 0, 1, 0], [], [], Appearance.Default));

    private static RenderPackage Package(GeometryKey key) =>
        PrimitiveMeshes.Box(Vector3.Zero, Vector3.One)
            .ToRenderPackage(key, "solid", Appearance.Default);
}

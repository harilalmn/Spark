using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Spark.UI.ViewModels;
using Spark.Viewport;

namespace Spark.UI.Tests;

/// <summary>
/// A pick in the viewport selects the node that drew the geometry (`E9-T8`).
/// </summary>
/// <remarks>
/// <b>The seam under test is the view model's.</b> The control turns a click into a
/// <see cref="GeometryKey"/> through <see cref="ViewportPicker"/>, which <c>ViewportPickerTests</c>
/// covers; the window may not name the engine's types, so turning that key back into a canvas slot is
/// <see cref="MainWindowViewModel.SlotDrawing(GeometryKey)"/>, and that is where a click on geometry
/// could quietly select nothing.
/// </remarks>
public sealed class ViewportPickSelectionTests
{
    private static int SlotThatDrew(MainWindowViewModel model, GeometryKey key) =>
        Enumerable.Range(0, model.Graph.Nodes.Count)
            .Single(slot => model.Graph.Nodes[slot].Id.ToString() == key.NodeId);

    /// <summary>The key of the demo's drawn points maps back to the node that drew them.</summary>
    [Fact]
    public async Task ADrawnPackagesKeyNamesTheNodeThatDrewIt()
    {
        using MainWindowViewModel model = new("demo");
        await model.EvaluateAsync();

        RenderPackage package = Assert.Single(model.Scene.Snapshot());

        Assert.Equal(SlotThatDrew(model, package.Key), model.SlotDrawing(package.Key));
    }

    /// <summary>
    /// <b>The whole chain without a window</b>: a camera aimed at one of the demo's point markers, the
    /// ray through the centre pixel, and the slot of the node that drew the marker.
    /// </summary>
    [Fact]
    public async Task APickThroughTheCameraFindsTheNodeThatDrewTheMarker()
    {
        using MainWindowViewModel model = new("demo");
        await model.EvaluateAsync();

        RenderPackage package = Assert.Single(model.Scene.Snapshot());
        float[] positions = package.Positions.ToArray();
        int[] indices = package.Indices.ToArray();

        Vector3 Corner(int n) => new(positions[indices[n] * 3], positions[(indices[n] * 3) + 1], positions[(indices[n] * 3) + 2]);

        Vector3 centroid = (Corner(0) + Corner(1) + Corner(2)) / 3f;

        Camera camera = new() { Target = centroid, Distance = 30f };
        camera.SetViewportSize(800, 600);

        ViewportHit? hit = ViewportPicker.Pick(camera, model.Scene.Snapshot(), 400, 300);

        Assert.NotNull(hit);
        Assert.Equal(SlotThatDrew(model, package.Key), model.SlotDrawing(hit.Value.Key));
    }

    /// <summary>A key no node on the canvas drew, or one that is not a node's at all, selects nothing.</summary>
    [Fact]
    public void AKeyNoNodeDrewSelectsNothing()
    {
        using MainWindowViewModel model = new("demo");

        Assert.Equal(-1, model.SlotDrawing(new GeometryKey(Guid.NewGuid().ToString(), 0)));
        Assert.Equal(-1, model.SlotDrawing(new GeometryKey("not a node", 0)));
    }
}

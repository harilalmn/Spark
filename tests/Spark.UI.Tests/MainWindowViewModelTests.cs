using System;
using System.Linq;
using System.Threading.Tasks;
using Spark.UI.Graph;
using Spark.UI.ViewModels;
using Spark.Viewport;

namespace Spark.UI.Tests;

/// <summary>
/// The walking skeleton end to end, without a window: library → graph → evaluation → geometry in
/// the viewport scene.
/// </summary>
/// <remarks>
/// These are the tests that would have caught a pipeline which compiles and puts nothing on screen.
/// Every one of them asserts on the <see cref="ViewportScene"/>'s contents rather than on the fact
/// that a run completed, because "the graph evaluated" and "something is drawn" are different
/// claims and only the second one is the point of this slice.
/// </remarks>
public sealed class MainWindowViewModelTests
{
    /// <summary>The built-in library is imported and reaches the library panel.</summary>
    [Fact]
    public void TheLibraryIsImportedAtStartup()
    {
        using MainWindowViewModel model = new();

        Assert.True(model.LibraryCount > 20, $"Only {model.LibraryCount} nodes were imported.");
        Assert.Contains(model.AllLibraryEntries, entry => entry.DisplayName == "Point.ByCoordinates");
        Assert.Contains(model.AllLibraryEntries, entry => entry.DisplayName == "Number.Range");

        // Descriptions come from the XML comments, which is what makes the tooltips real.
        LibraryEntryViewModel range = model.AllLibraryEntries.First(entry => entry.DisplayName == "Number.Range");
        Assert.DoesNotContain("No description", range.Description, StringComparison.Ordinal);
        Assert.Equal("(start, end, step) → numbers", range.Signature);
    }

    /// <summary>
    /// The demo graph evaluates and puts a hundred points into the viewport as one buffer set.
    /// </summary>
    [Fact]
    public async Task TheDemoGraphPutsAHundredPointsInTheViewport()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        RenderPackage package = Assert.Single(model.Scene.Snapshot());

        // One buffer set for one (NodeId, PortIndex); eight faces per point marker.
        Assert.Equal(100 * 8, package.TriangleCount);

        // And it is a grid rather than a line: nine units across in both x and y, plus a marker
        // radius at each end.
        Bounds3 bounds = package.ComputeBounds();
        Assert.InRange(bounds.Max.X - bounds.Min.X, 9f, 10f);
        Assert.InRange(bounds.Max.Y - bounds.Min.Y, 9f, 10f);
    }

    /// <summary>
    /// Editing a literal re-runs the graph and changes what the viewport is showing. This is the
    /// interaction the whole slice is judged on.
    /// </summary>
    [Fact]
    public async Task EditingALiteralChangesTheGeometry()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        Assert.Equal(100 * 8, model.Scene.Snapshot().Single().TriangleCount);

        int range = SlotOf(model, "Number.Range");
        model.ShowSelection([range]);

        PortLiteralViewModel end = model.Inspector.Single(port => port.Name == "end");
        Assert.True(end.IsEditable);

        end.Text = "2";
        end.Commit();

        await model.EvaluateAsync();

        // Three values crossed with ten: thirty points, not a hundred and not ten.
        Assert.Equal(30 * 8, model.Scene.Snapshot().Single().TriangleCount);
        Assert.Null(end.Error);
    }

    /// <summary>Text that is not a number commits nothing and says so.</summary>
    [Fact]
    public async Task InvalidLiteralTextIsRefusedRatherThanCoerced()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        model.ShowSelection([SlotOf(model, "Number.Range")]);
        PortLiteralViewModel end = model.Inspector.Single(port => port.Name == "end");

        end.Text = "nine";
        end.Commit();

        Assert.NotNull(end.Error);

        await model.EvaluateAsync();
        Assert.Equal(100 * 8, model.Scene.Snapshot().Single().TriangleCount);
    }

    /// <summary>A port fed by a wire is not editable, because the wire wins.</summary>
    [Fact]
    public void AWiredPortIsNotEditable()
    {
        using MainWindowViewModel model = new();

        model.ShowSelection([SlotOf(model, "Point.ByCoordinates")]);

        Assert.All(model.Inspector, port => Assert.True(port.IsWired));
        Assert.All(model.Inspector, port => Assert.False(port.IsEditable));
        Assert.All(model.Inspector, port => Assert.EndsWith("(wired)", port.Label, StringComparison.Ordinal));
    }

    /// <summary>
    /// Placing a node from the library and running produces a second buffer set, which is the
    /// shortest end-to-end path there is: a click in the library becomes a dot in the viewport.
    /// </summary>
    [Fact]
    public async Task PlacingANodeAddsGeometryToTheViewport()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        Assert.Equal(1, model.Scene.Count);

        model.SelectedLibraryEntry =
            model.AllLibraryEntries.First(entry => entry.DisplayName == "Point.Origin");

        int slot = model.PlaceSelectedLibraryEntry(0, 0);
        Assert.True(slot >= 0);

        await model.EvaluateAsync();

        Assert.Equal(2, model.Scene.Count);
        Assert.Equal("Point.Origin", model.Graph.Nodes[slot].Title);
    }

    /// <summary>
    /// A node that stops producing geometry stops showing it. Without the retire half of publishing
    /// the old points stay on screen, which reads as the graph not having run.
    /// </summary>
    [Fact]
    public async Task RemovingANodeRemovesItsGeometry()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();

        Assert.Equal(1, model.Scene.Count);

        model.Graph.Remove(SlotOf(model, "Display.ByGeometryColour"));
        await model.EvaluateAsync();

        // Nothing else in the demo is a terminal port that produces geometry — the point node still
        // feeds the translate branch — so the scene empties. Without the retire half of publishing
        // the display node's hundred points would simply stay there.
        Assert.Equal(0, model.Scene.Count);
    }

    /// <summary>Loading the synthetic graph replaces the document and clears the scene.</summary>
    [Fact]
    public async Task LoadingTheSyntheticGraphReplacesTheDocument()
    {
        using MainWindowViewModel model = new();
        await model.EvaluateAsync();
        Assert.Equal(1, model.Scene.Count);

        model.LoadSynthetic(200);

        Assert.Equal(200, model.Graph.Nodes.Count);
        Assert.Equal(0, model.Scene.Count);
    }

    /// <summary>The library search filters by name and by category.</summary>
    [Fact]
    public void TheLibrarySearchFilters()
    {
        using MainWindowViewModel model = new();
        int all = model.LibraryEntries.Count;

        model.LibrarySearch = "vector";
        Assert.True(model.LibraryEntries.Count < all);
        Assert.All(model.LibraryEntries, entry =>
            Assert.Contains("Vector", entry.DisplayName, StringComparison.OrdinalIgnoreCase));

        model.LibrarySearch = string.Empty;
        Assert.Equal(all, model.LibraryEntries.Count);
    }

    private static int SlotOf(MainWindowViewModel model, string title)
    {
        for (int slot = 0; slot < model.Graph.Nodes.Count; slot++)
        {
            if (string.Equals(model.Graph.Nodes[slot].Title, title, StringComparison.Ordinal))
            {
                return slot;
            }
        }

        Assert.Fail($"No node titled '{title}' in the demo graph.");
        return -1;
    }
}

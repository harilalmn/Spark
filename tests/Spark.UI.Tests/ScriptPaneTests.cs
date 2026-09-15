using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AvaloniaEdit;
using Spark.UI.Shell;
using Spark.UI.ViewModels;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// The docked script pane (<c>E6-T14</c>): the one pane that starts closed, and what it does to
/// the centre column when it is opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is NOT asserted here is the editor.</b> <c>CodeBlockEditor</c> is the same control the
/// Properties pane hosts and is covered by its own tests; the docked variant is a second
/// presentation of one pipeline, so what is worth guarding is the part that is genuinely new —
/// the pane exists, it is hidden by default, it can be opened by name, and opening it rescales the
/// centre column instead of redefining what <see cref="WorkspaceLayout.CanvasFraction"/> means.
/// </para>
/// <para>
/// That last one is the real risk. <c>CanvasFraction</c> is serialised into every saved layout and
/// written into all four presets as <i>the canvas's share of the graph views</i>. A third pane
/// added as a third term would silently reinterpret every number a user has already saved, and
/// nothing would fail — the shell would just come back subtly wrong.
/// </para>
/// </remarks>
public sealed class ScriptPaneTests
{
    /// <summary>
    /// <b>The default layout does not include it, and that is a decision rather than an
    /// oversight.</b> It edits a code block, a new document has none, and it sits in the centre
    /// column where it costs the canvas height.
    /// </summary>
    [Fact]
    public void TheScriptPaneIsHiddenInTheDefaultLayout()
    {
        WorkspaceLayout layout = WorkspaceLayout.Default;

        Assert.False(layout.IsVisible(WorkspacePane.Script));

        // The other five are showing, so this is the one pane that differs rather than a layout
        // that happens to be empty.
        Assert.True(layout.IsVisible(WorkspacePane.Library));
        Assert.True(layout.IsVisible(WorkspacePane.Canvas));
        Assert.True(layout.IsVisible(WorkspacePane.Viewport));
        Assert.True(layout.IsVisible(WorkspacePane.Inspector));
        Assert.True(layout.IsVisible(WorkspacePane.Console));
    }

    /// <summary>No preset opens it either, for the same reason the default does not.</summary>
    [Fact]
    public void NoPresetOpensTheScriptPane()
    {
        foreach ((string name, WorkspaceLayout layout) in WorkspaceLayout.Presets())
        {
            Assert.False(
                layout.IsVisible(WorkspacePane.Script),
                $"The '{name}' preset should not open the script pane.");
        }
    }

    /// <summary>
    /// It opens from <i>View → Script</i> by name, through the command that already existed.
    /// </summary>
    [Fact]
    public void TheScriptPaneTogglesByNameAndDrivesItsOwnTick()
    {
        using MainWindowViewModel model = new("demo");

        Assert.False(model.IsScriptVisible);

        model.TogglePane("Script");

        Assert.True(model.IsScriptVisible);

        // And it leaves the rest of the shell alone, which is the whole reason TogglePane exists
        // beside the presets.
        Assert.True(model.IsLibraryVisible);
        Assert.True(model.IsInspectorVisible);
        Assert.True(model.IsConsoleVisible);

        model.TogglePane("Script");

        Assert.False(model.IsScriptVisible);
    }

    /// <summary>A layout with the pane open survives a round trip through JSON.</summary>
    [Fact]
    public void AnOpenScriptPaneRoundTrips()
    {
        WorkspaceLayout layout = WorkspaceLayout.Default;
        layout.SetVisible(WorkspacePane.Script, true);

        WorkspaceLayout read = WorkspaceLayout.FromJson(layout.ToJson());

        Assert.True(read.IsVisible(WorkspacePane.Script));
    }

    /// <summary>
    /// <b>A layout saved before this pane existed reads back with it closed</b>, rather than with
    /// it open or with the file rejected as malformed. The JSON carries the panes it knew about
    /// and nothing more.
    /// </summary>
    [Fact]
    public void ALayoutWrittenBeforeTheScriptPaneExistedReadsBackWithItClosed()
    {
        const string Before =
            "{\"Library\":0.16,\"Inspector\":0.20,\"Canvas\":0.55,"
            + "\"Panes\":[\"Library\",\"Canvas\",\"Viewport\",\"Inspector\",\"Console\"]}";

        WorkspaceLayout read = WorkspaceLayout.FromJson(Before);

        Assert.False(read.IsVisible(WorkspacePane.Script));
        Assert.True(read.IsVisible(WorkspacePane.Canvas));
        Assert.True(read.IsVisible(WorkspacePane.Console));
    }

    /// <summary>
    /// The shell builds a dock for it, closed, so <i>View → Script</i> can open it without the
    /// rebuild that dragging a layout apart needs.
    /// </summary>
    [Fact]
    public void TheShellBuildsTheScriptDockAndLeavesItClosed() => HeadlessSession.Run(() =>
    {
        SparkDockFactory factory = BuildShell();

        factory.Apply(WorkspaceLayout.Default);

        Assert.NotNull(factory.DockFor(WorkspacePane.Script));
        Assert.False(factory.IsShowing(WorkspacePane.Script));
        Assert.Equal(0, Proportion(factory, WorkspacePane.Script));
    });

    /// <summary>
    /// <b>The regression guard for <c>CanvasFraction</c>'s meaning.</b> Opening the script pane
    /// scales the two graph views down together; it does not redistribute them. Their ratio to
    /// each other has to come out exactly as it was.
    /// </summary>
    [Fact]
    public void OpeningTheScriptPaneScalesTheGraphViewsAndKeepsTheirRatio() => HeadlessSession.Run(() =>
    {
        SparkDockFactory factory = BuildShell();

        factory.Apply(WorkspaceLayout.Default);

        double canvasBefore = Proportion(factory, WorkspacePane.Canvas);
        double viewportBefore = Proportion(factory, WorkspacePane.Viewport);

        WorkspaceLayout opened = WorkspaceLayout.Default;
        opened.SetVisible(WorkspacePane.Script, true);
        factory.Apply(opened);

        double canvasAfter = Proportion(factory, WorkspacePane.Canvas);
        double viewportAfter = Proportion(factory, WorkspacePane.Viewport);

        Assert.Equal(SparkDockFactory.ScriptFraction, Proportion(factory, WorkspacePane.Script), 6);

        // Scaled by exactly what the script pane took, and still summing to the column.
        double left = 1 - SparkDockFactory.ScriptFraction;
        Assert.Equal(canvasBefore * left, canvasAfter, 6);
        Assert.Equal(viewportBefore * left, viewportAfter, 6);
        Assert.Equal(1, canvasAfter + viewportAfter + SparkDockFactory.ScriptFraction, 6);

        // And the ratio between the two graph views is untouched, which is the claim that makes
        // every saved CanvasFraction still mean what it meant.
        Assert.Equal(canvasBefore / viewportBefore, canvasAfter / viewportAfter, 6);
    });

    /// <summary>
    /// Alone in the centre column it takes all of it, which is the rule the canvas and the
    /// viewport already follow rather than a special case for this pane.
    /// </summary>
    [Fact]
    public void TheScriptPaneAloneTakesTheWholeCentreColumn() => HeadlessSession.Run(() =>
    {
        SparkDockFactory factory = BuildShell();

        WorkspaceLayout only = WorkspaceLayout.Default;
        only.SetVisible(WorkspacePane.Canvas, false);
        only.SetVisible(WorkspacePane.Viewport, false);
        only.SetVisible(WorkspacePane.Script, true);

        factory.Apply(only);

        Assert.Equal(1, Proportion(factory, WorkspacePane.Script), 6);
        Assert.Equal(0, Proportion(factory, WorkspacePane.Canvas));
        Assert.Equal(0, Proportion(factory, WorkspacePane.Viewport));
    });

    /// <summary>
    /// <b>The default layout's centre column is exactly what it was before this pane existed.</b>
    /// A new pane that is hidden by default must cost nothing at all, and the way this could go
    /// wrong is arithmetic that rounds rather than short-circuits.
    /// </summary>
    [Fact]
    public void AClosedScriptPaneChangesNothingAboutTheDefaultCentreColumn() => HeadlessSession.Run(() =>
    {
        SparkDockFactory factory = BuildShell();

        factory.Apply(WorkspaceLayout.Default);

        Assert.Equal(WorkspaceLayout.Default.CanvasFraction, Proportion(factory, WorkspacePane.Canvas), 10);
        Assert.Equal(1 - WorkspaceLayout.Default.CanvasFraction, Proportion(factory, WorkspacePane.Viewport), 10);
    });

    /// <summary>
    /// <b>Both editors are views of one document, and the view model is the copy.</b> The pane's
    /// XML docs claim two can be open at once because neither owns the text; this is that claim,
    /// executable. A change to <see cref="MainWindowViewModel.ScriptText"/> reaches an already
    /// constructed pane through the property notification rather than through a DataContext
    /// change, which is the part that would silently stop working.
    /// </summary>
    [Fact]
    public void TheScriptPaneFollowsScriptTextRatherThanItsDataContext() => HeadlessSession.Run(() =>
    {
        using MainWindowViewModel model = new("demo");
        ScriptPane pane = new() { DataContext = model };

        TextEditor editor = pane.GetLogicalDescendants()
            .OfType<TextEditor>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("the script pane has no editor in it");

        // The selection moving is what raises ScriptText, and the pane is already built by then.
        model.ScriptText = "return 41 + 1;";

        Assert.Equal("return 41 + 1;", editor.Text);

        // And again, because the second change is the one that a subscription removed on the first
        // would break.
        model.ScriptText = "return 0;";

        Assert.Equal("return 0;", editor.Text);
    });

    /// <summary>
    /// The pane says what to do when there is nothing selected, rather than showing an empty box.
    /// </summary>
    [Fact]
    public void TheScriptPaneShowsAnEmptyStateWithNoCodeBlockSelected() => HeadlessSession.Run(() =>
    {
        using MainWindowViewModel model = new("demo");
        ScriptPane pane = new() { DataContext = model };

        Assert.Null(model.SelectedCodeBlock);

        TextBlock empty = pane.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(block => block.Text?.Contains("No code block selected", StringComparison.Ordinal) == true)
            ?? throw new InvalidOperationException("the script pane has no empty state");

        // Not a warning, so no warning glyph: nothing has gone wrong. It names the way out.
        Assert.Contains("select a code block", empty.Text, StringComparison.OrdinalIgnoreCase);
    });

    private static double Proportion(SparkDockFactory factory, WorkspacePane pane) =>
        factory.DockFor(pane)?.Proportion ?? 0;

    private static SparkDockFactory BuildShell()
    {
        SparkDockFactory factory = new();
        Dictionary<WorkspacePane, object?> content = [];

        foreach (WorkspacePane pane in Enum.GetValues<WorkspacePane>())
        {
            content[pane] = new UserControl();
        }

        factory.Build(content);
        return factory;
    }
}

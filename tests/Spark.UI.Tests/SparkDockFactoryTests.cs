using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Dock.Model.Core;
using Spark.UI.Shell;

namespace Spark.UI.Tests;

/// <summary>
/// The mapping between <see cref="WorkspaceLayout"/> — which is data — and the dock tree the shell
/// actually shows.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WorkspaceLayoutTests"/> already proves the model clamps, round-trips and has
/// presets. None of that was ever the risk: for the whole of step (a) the preset buttons updated a
/// correct model and the shell did not move, because nothing consumed it. What is asserted here is
/// the consumption — that applying a preset changes the tree, and that applying the default
/// changes it back.
/// </para>
/// <para>
/// The panes are stand-in <see cref="UserControl"/>s rather than the real four. What the factory
/// promises about content is that the view model reaches the control's <c>DataContext</c>, and a
/// bare control demonstrates that as well as a real pane would while costing nothing to build.
/// </para>
/// </remarks>
public sealed class SparkDockFactoryTests
{
    private static WorkspacePane[] AllPanes =>
        [WorkspacePane.Library, WorkspacePane.Canvas, WorkspacePane.Viewport, WorkspacePane.Inspector];

    /// <summary>Every pane is present and showing the moment the shell is built.</summary>
    [Fact]
    public void ABuiltShellShowsAllFourPanes() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _) = BuildShell();

        foreach (WorkspacePane pane in AllPanes)
        {
            Assert.True(factory.IsShowing(pane), $"{pane} should be showing in a freshly built shell.");
            Assert.NotNull(factory.DockFor(pane));
        }
    });

    /// <summary>
    /// <b>The regression guard for the empty panes.</b> Dock puts the dockable on the presented
    /// content's <c>DataContext</c>, so a pane that inherits it binds against a <c>Tool</c>; the
    /// compiled bindings then resolve to nothing without complaining, and the pane draws its
    /// heading over an empty list. Setting the context has to reach the control itself.
    /// </summary>
    [Fact]
    public void SettingTheContextReachesEachPaneControlAndNotOnlyItsTool() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Dictionary<WorkspacePane, UserControl> panes) = BuildShell();
        object model = new();

        factory.SetContext(model);

        foreach (WorkspacePane pane in AllPanes)
        {
            Assert.Same(model, panes[pane].DataContext);
        }

        // Clearing it has to clear the controls too, or a closed document leaves its view model
        // alive behind four panes that still draw it.
        factory.SetContext(null);

        foreach (WorkspacePane pane in AllPanes)
        {
            Assert.Null(panes[pane].DataContext);
        }
    });

    /// <summary>A preset that hides panes actually removes them from the tree.</summary>
    [Fact]
    public void PresentingHidesTheLibraryAndTheInspector() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _) = BuildShell();

        factory.Apply(WorkspaceLayout.Presets()["Presenting"]);

        Assert.False(factory.IsShowing(WorkspacePane.Library));
        Assert.False(factory.IsShowing(WorkspacePane.Inspector));
        Assert.True(factory.IsShowing(WorkspacePane.Canvas));
        Assert.True(factory.IsShowing(WorkspacePane.Viewport));
    });

    /// <summary>
    /// A hidden pane surrenders its share of the window rather than holding an empty column open.
    /// </summary>
    [Fact]
    public void AHiddenPaneKeepsNoWidthAndTheCentreTakesItAll() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _) = BuildShell();

        factory.Apply(WorkspaceLayout.Presets()["Presenting"]);

        Assert.Equal(0, Proportion(factory, WorkspacePane.Library));
        Assert.Equal(0, Proportion(factory, WorkspacePane.Inspector));

        // The centre is the parent of the canvas and the viewport, so it is read off either
        // child's owner rather than off a dock of its own.
        IDock? centre = factory.DockFor(WorkspacePane.Canvas)?.Owner as IDock;
        Assert.NotNull(centre);
        Assert.Equal(1, centre!.Proportion);
    });

    /// <summary>
    /// <i>Reset layout</i> is the escape hatch, so restoring the default has to put back panes an
    /// earlier preset removed — not merely resize what is left.
    /// </summary>
    [Fact]
    public void TheDefaultLayoutBringsBackWhatAPresetHid() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _) = BuildShell();

        factory.Apply(WorkspaceLayout.Presets()["Presenting"]);
        factory.Apply(WorkspaceLayout.Default);

        foreach (WorkspacePane pane in AllPanes)
        {
            Assert.True(factory.IsShowing(pane), $"{pane} should be back after a reset.");
        }

        Assert.Equal(0.16, Proportion(factory, WorkspacePane.Library), 3);
        Assert.Equal(0.20, Proportion(factory, WorkspacePane.Inspector), 3);
    });

    /// <summary>
    /// Presets arrive on every button press, including the one that is already applied. Hiding a
    /// pane that is already hidden and restoring one that was never hidden both have to be
    /// nothing, because both are asked for constantly.
    /// </summary>
    [Fact]
    public void ApplyingTheSameLayoutTwiceChangesNothing() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _) = BuildShell();
        WorkspaceLayout modelling = WorkspaceLayout.Presets()["Modelling"];

        factory.Apply(modelling);
        factory.Apply(modelling);
        factory.Apply(modelling);

        Assert.False(factory.IsShowing(WorkspacePane.Inspector));
        Assert.True(factory.IsShowing(WorkspacePane.Library));
        Assert.Single(factory.DockFor(WorkspacePane.Library)!.VisibleDockables!);
        Assert.Equal(0.14, Proportion(factory, WorkspacePane.Library), 3);
    });

    /// <summary>
    /// The centre column splits by <see cref="WorkspaceLayout.CanvasFraction"/>, and gives the
    /// canvas the whole column when the viewport is hidden rather than leaving a gap where it was.
    /// </summary>
    [Fact]
    public void TheCentreColumnFollowsTheCanvasFraction() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _) = BuildShell();

        // Modelling is the viewport-heavy preset: the canvas keeps under a third of the height.
        factory.Apply(WorkspaceLayout.Presets()["Modelling"]);

        Assert.Equal(0.32, Proportion(factory, WorkspacePane.Canvas), 3);
        Assert.Equal(0.68, Proportion(factory, WorkspacePane.Viewport), 3);

        WorkspaceLayout canvasOnly = WorkspaceLayout.Default;
        canvasOnly.SetVisible(WorkspacePane.Viewport, false);
        factory.Apply(canvasOnly);

        Assert.Equal(1, Proportion(factory, WorkspacePane.Canvas), 3);
    });

    /// <summary>
    /// Applying a workspace to a shell that was never built is a programming error, and it says so
    /// rather than quietly arranging nothing.
    /// </summary>
    [Fact]
    public void ApplyingBeforeBuildingThrows() => HeadlessSession.Run(() =>
    {
        SparkDockFactory factory = new();

        Assert.Throws<InvalidOperationException>(() => factory.Apply(WorkspaceLayout.Default));
    });

    private static double Proportion(SparkDockFactory factory, WorkspacePane pane) =>
        factory.DockFor(pane)!.Proportion;

    private static (SparkDockFactory Factory, Dictionary<WorkspacePane, UserControl> Panes) BuildShell()
    {
        SparkDockFactory factory = new();
        Dictionary<WorkspacePane, UserControl> panes = [];
        Dictionary<WorkspacePane, object?> content = [];

        foreach (WorkspacePane pane in AllPanes)
        {
            UserControl control = new();
            panes[pane] = control;
            content[pane] = control;
        }

        factory.Build(content);
        return (factory, panes);
    }
}

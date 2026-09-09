using System;
using Avalonia.Controls;
using Spark.Api;
using Spark.UI.Shell;
using Spark.UI.ViewModels;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// The console pane: where it sits, when it shows, and what it shows — `E8-T80`.
/// </summary>
/// <remarks>
/// <b>The interesting assertions are the two about <i>not</i> appearing.</b> A fifth pane is cheap
/// to add and expensive to add badly: it must be hidden until somebody asks for it, and it must not
/// change what the other four do when it is hidden — otherwise every layout, preset and saved
/// workspace shifts under a user who never wanted a console.
/// </remarks>
[Collection("SparkConsole")]
public sealed class ConsolePaneTests : IDisposable
{
    public ConsolePaneTests() => SparkConsole.Clear();

    public void Dispose() => SparkConsole.Clear();

    /// <summary>
    /// <b>Shown by default, in the right column under Properties</b> (`E8-T82`) — the arrangement
    /// the client made by hand and then asked for as the default.
    /// </summary>
    [Fact]
    public void TheConsoleIsShownByDefault()
    {
        WorkspaceLayout layout = WorkspaceLayout.Default;

        Assert.True(layout.IsVisible(WorkspacePane.Console));
        Assert.True(layout.IsVisible(WorkspacePane.Canvas));
    }

    /// <summary>
    /// <b><i>Reset layout</i> brings it back</b>, which is the second half of what was asked for:
    /// hidden by hand, or dragged somewhere else, the reset restores it.
    /// </summary>
    [Fact]
    public void ResettingTheLayoutRestoresTheConsole()
    {
        using MainWindowViewModel model = new();

        model.TogglePane("Console");
        Assert.False(model.IsConsoleVisible);

        model.ResetLayout();

        Assert.True(model.IsConsoleVisible);
    }

    /// <summary>The View menu's tick and the toggle behave as the other panes' do.</summary>
    [Fact]
    public void TheViewMenuTogglesIt()
    {
        using MainWindowViewModel model = new();

        Assert.True(model.IsConsoleVisible);

        model.TogglePane("Console");
        Assert.False(model.IsConsoleVisible);

        model.TogglePane("Console");
        Assert.True(model.IsConsoleVisible);
    }

    /// <summary>
    /// <b>Toggling the console leaves the other four alone</b>, which is the property that keeps
    /// every saved layout and every preset meaning what it meant.
    /// </summary>
    [Fact]
    public void ShowingItDisturbsNothingElse()
    {
        using MainWindowViewModel model = new();

        bool library = model.IsLibraryVisible;
        bool inspector = model.IsInspectorVisible;

        model.TogglePane("Console");

        Assert.Equal(library, model.IsLibraryVisible);
        Assert.Equal(inspector, model.IsInspectorVisible);
    }

    /// <summary>
    /// <b>A user who turned it off stays turned off across a restart</b>, which is what a saved
    /// layout is for — the default is a starting point, not something reapplied behind them.
    /// </summary>
    [Fact]
    public void AHiddenConsoleStaysHiddenAcrossARoundTrip()
    {
        WorkspaceLayout hidden = WorkspaceLayout.Default;
        hidden.SetVisible(WorkspacePane.Console, false);

        Assert.False(WorkspaceLayout.FromJson(hidden.ToJson()).IsVisible(WorkspacePane.Console));
    }

    /// <summary>And a layout that was showing it says so when it comes back.</summary>
    [Fact]
    public void AShownConsoleSurvivesARoundTrip()
    {
        WorkspaceLayout shown = WorkspaceLayout.Default;
        shown.SetVisible(WorkspacePane.Console, true);

        Assert.True(WorkspaceLayout.FromJson(shown.ToJson()).IsVisible(WorkspacePane.Console));
    }

    /// <summary>
    /// <b>The pane shows what was written, and stops listening when it goes away</b> (`E8-T80`).
    /// </summary>
    /// <remarks>
    /// <b>The second half is the one that was got wrong first.</b> The view model subscribed to
    /// <see cref="SparkConsole.Changed"/> in its constructor — a static event — so every model a
    /// test built stayed subscribed and went on posting to a dispatcher whose headless session had
    /// ended, turning unrelated tests red at random. The subscription now lives exactly as long as
    /// the control is attached.
    /// </remarks>
    [Fact]
    public void ThePaneShowsWhatWasWrittenAndStopsWhenItGoesAway() => HeadlessSession.Run(() =>
    {
        Window host = new();
        ConsolePane pane = new();
        host.Content = pane;
        host.Show();

        SparkConsole.WriteLine("hello");

        Assert.Contains("hello", pane.Text, StringComparison.Ordinal);

        host.Content = null;
        SparkConsole.WriteLine("after it went away");

        Assert.DoesNotContain("after it went away", pane.Text, StringComparison.Ordinal);

        host.Close();
    });

    /// <summary>
    /// <b>The summary is silent until something is lost, and then it is not.</b> A user reading
    /// from the top of a console that has quietly dropped nine thousand lines is reading the wrong
    /// thing without knowing it.
    /// </summary>
    [Fact]
    public void TheSummarySpeaksOnlyWhenLinesAreLost()
    {
        Assert.Equal(0, SparkConsole.Dropped);

        for (int line = 0; line < SparkConsole.Capacity + 3; line++)
        {
            SparkConsole.WriteLine(line);
        }

        Assert.Equal(3, SparkConsole.Dropped);
    }

    /// <summary>
    /// <b>The presets that are about the graph show it; the two that are not, do not</b>
    /// (`E8-T82`). <i>Modelling</i> is for watching geometry and <i>Presenting</i> is for an
    /// audience, and neither is a moment for reading printed output — so their leaving the console
    /// out is a decision, asserted here so it stays one.
    /// </summary>
    [Fact]
    public void ThePresetsThatShowItAreTheOnesAboutTheGraph()
    {
        var presets = WorkspaceLayout.Presets();

        Assert.True(presets["Default"].IsVisible(WorkspacePane.Console));
        Assert.True(presets["Authoring"].IsVisible(WorkspacePane.Console));
        Assert.False(presets["Modelling"].IsVisible(WorkspacePane.Console));
        Assert.False(presets["Presenting"].IsVisible(WorkspacePane.Console));
    }
}

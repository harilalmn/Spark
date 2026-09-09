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
    /// <b>Hidden by default.</b> A pane nobody asked for taking room from the canvas is
    /// <c>E8-T76</c>'s mistake again.
    /// </summary>
    [Fact]
    public void TheConsoleIsHiddenUntilItIsAskedFor()
    {
        WorkspaceLayout layout = WorkspaceLayout.Default;

        Assert.False(layout.IsVisible(WorkspacePane.Console));
        Assert.True(layout.IsVisible(WorkspacePane.Canvas));
    }

    /// <summary>The View menu's tick and the toggle behave as the other panes' do.</summary>
    [Fact]
    public void TheViewMenuTogglesIt()
    {
        using MainWindowViewModel model = new();

        Assert.False(model.IsConsoleVisible);

        model.TogglePane("Console");
        Assert.True(model.IsConsoleVisible);

        model.TogglePane("Console");
        Assert.False(model.IsConsoleVisible);
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
    /// A layout written before the console existed reads back with the console hidden, rather than
    /// with a pane the file never mentioned.
    /// </summary>
    [Fact]
    public void ALayoutWithoutAConsoleReadsBackWithoutOne()
    {
        WorkspaceLayout before = WorkspaceLayout.Default;
        WorkspaceLayout after = WorkspaceLayout.FromJson(before.ToJson());

        Assert.False(after.IsVisible(WorkspacePane.Console));
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
    /// <b>No preset turns the console on</b>, including <i>Authoring</i>, which is written as
    /// <i>all the panes</i> and so would have gained a fifth by definition. Switching it on there
    /// would contradict its being hidden by default and would take room from the canvas for
    /// somebody who chose a workspace rather than a console.
    /// </summary>
    [Fact]
    public void NoPresetTurnsTheConsoleOn()
    {
        Assert.All(
            WorkspaceLayout.Presets().Values,
            preset => Assert.False(preset.IsVisible(WorkspacePane.Console)));
    }
}

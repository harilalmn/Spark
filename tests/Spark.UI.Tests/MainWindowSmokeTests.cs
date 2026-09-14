using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Spark.UI.ViewModels;
using Spark.UI.Views;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// The application's real <see cref="MainWindow"/> opens, composes and draws — `E11-T15`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this suite opened the real window until now, and [N90](../../docs/NOTES.md) is
/// why.</b> A wrapping, data-bound <c>TextBlock</c> inside a <c>Grid</c> hung Avalonia's headless
/// <c>Window.Show()</c> before the first frame — it did not fail and did not time out, it sat
/// there until the harness killed it — and the inspector has one. `InspectorLayoutTests` was
/// deleted rather than left in the suite, and [N89](../../docs/NOTES.md)'s fix was verified by a
/// person's eyes and nothing else.
/// </para>
/// <para>
/// <b>It no longer reproduces.</b> Shown headlessly today the inspector opens in under a second and
/// the whole window composes: a title bar, a menu, a <see cref="DockControl"/> filling the body,
/// and a status bar, all laid out at real sizes. N90 is recorded as stale rather than deleted,
/// because a hang that came back would be read as a new fault otherwise.
/// </para>
/// <para>
/// <b>What this can and cannot prove.</b> <c>CaptureRenderedFrame</c> returns <see langword="null"/>
/// in this backend, so there is no bitmap to inspect and no assertion here claims one. Asking for
/// the frame still drives the draw path, which is what catches a control that throws while
/// rendering; the assertions are on <i>composition and layout</i> — that the parts exist, are
/// connected, and have been given real sizes. Claiming more would be the mistake N90's own note
/// warns about.
/// </para>
/// </remarks>
public sealed class MainWindowSmokeTests
{
    /// <summary>The window opens, lays out its shell, draws and closes.</summary>
    [Fact]
    public void TheRealMainWindowOpensComposesAndDraws() => HeadlessSession.Run(() =>
    {
        MainWindow window = new() { DataContext = new MainWindowViewModel() };

        window.Show();

        Assert.True(window.IsVisible, "the window did not become visible.");
        Assert.True(
            window.Bounds.Width > 0 && window.Bounds.Height > 0,
            $"the window laid out at {window.Bounds.Width} by {window.Bounds.Height}.");

        // THE DOCK IS THE TEST. The shell would lay out with an empty body; the DockControl is
        // where every pane lives, so a real size on it is what says the layout composed rather
        // than merely that a window appeared.
        DockControl dock = Assert.Single(window.GetVisualDescendants().OfType<DockControl>());

        Assert.True(
            dock.Bounds.Width > 0 && dock.Bounds.Height > 0,
            $"the dock laid out at {dock.Bounds.Width} by {dock.Bounds.Height}, so the panes have "
            + "no room and nothing inside them has been measured.");

        // Drives the draw path. There is no bitmap to look at - see the remarks.
        window.CaptureRenderedFrame();

        window.Close();
    });

    /// <summary>
    /// <b>The inspector shows on its own too</b>, which is the exact control N90 named.
    /// </summary>
    /// <remarks>
    /// Kept separate from the window test so that a return of the hang names the control rather
    /// than the application.
    /// </remarks>
    [Fact]
    public void TheInspectorPaneShowsHeadlessly() => HeadlessSession.Run(() =>
    {
        Window window = new()
        {
            Width = 400,
            Height = 600,
            Content = new InspectorPane { DataContext = new MainWindowViewModel() },
        };

        window.Show();

        Assert.Single(window.GetVisualDescendants().OfType<InspectorPane>());

        window.CaptureRenderedFrame();
        window.Close();
    });
}

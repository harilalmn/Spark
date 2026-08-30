using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Spark.UI.Controls;
using Spark.UI.Graph;

namespace Spark.UI.Tests;

/// <summary>
/// <i>Zoom to fit</i> asked for before the canvas has been laid out.
/// </summary>
/// <remarks>
/// <para>
/// This is a regression suite for one defect and it is worth naming plainly. When the shell became
/// a <c>DockControl</c>, Dock began laying its content out later than the <c>Grid</c> had, so the
/// startup fit started arriving before the canvas's first arrange. <c>ZoomToFit</c> opened with a
/// guard that returned when the bounds were still zero, so the request vanished and the
/// application opened at 100% showing a third of the graph.
/// </para>
/// <para>
/// Nothing failed. No exception, no warning, no failing test — the gate that noticed was a human
/// reading <c>zoom 100%, 7/18 nodes drawn</c> in a screenshot that was expected to look different
/// for an unrelated reason. A guard that returns silently is a bug waiting for a layout change.
/// </para>
/// </remarks>
public sealed class GraphCanvasZoomToFitTests
{
    /// <summary>
    /// <b>The regression.</b> A fit requested before layout is performed at the first arrange
    /// rather than dropped.
    /// </summary>
    [Fact]
    public void AFitAskedForBeforeLayoutHappensOnceThereIsALayout() => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = WideGraph() };

        // Never shown, never measured: exactly the state the canvas is in inside a DockControl
        // when the window's Opened handler runs.
        Assert.True(canvas.Bounds.Width < 1);

        canvas.ZoomToFit();

        // Nothing could have happened yet, and that is fine. What must not happen is the request
        // being forgotten.
        Assert.Equal(1, canvas.Transform.Zoom);

        Window window = new() { Width = 800, Height = 600, Content = canvas };
        window.Show();

        try
        {
            Assert.True(
                canvas.Transform.Zoom < 1,
                "The deferred fit must run once the canvas has a size, not be dropped.");
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// A fit asked for when the canvas already has a size still happens immediately. The deferral
    /// is a fallback, not a new schedule.
    /// </summary>
    [Fact]
    public void AFitAskedForAfterLayoutStillHappensAtOnce() => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = WideGraph() };
        Window window = new() { Width = 800, Height = 600, Content = canvas };
        window.Show();

        try
        {
            canvas.Transform.Zoom = 1;
            canvas.Transform.OffsetX = 0;

            canvas.ZoomToFit();

            Assert.True(canvas.Transform.Zoom < 1);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// The deferred fit fires once. A canvas that re-fitted on every arrange would fight the user
    /// for the viewport: every pane resize would throw away their pan and zoom.
    /// </summary>
    [Fact]
    public void TheDeferredFitDoesNotRepeatOnEveryLayout() => HeadlessSession.Run(() =>
    {
        GraphCanvas canvas = new() { Graph = WideGraph() };
        canvas.ZoomToFit();

        Window window = new() { Width = 800, Height = 600, Content = canvas };
        window.Show();

        try
        {
            // Where the user has panned to since.
            canvas.Transform.Zoom = 1;
            canvas.Transform.OffsetX = 1234;

            window.Width = 640;
            Dispatcher.Run();

            Assert.Equal(1234, canvas.Transform.OffsetX, 6);
            Assert.Equal(1, canvas.Transform.Zoom);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>A graph far wider than any window, so a real fit must zoom out and cannot not.</summary>
    private static CanvasGraph WideGraph()
    {
        CanvasGraph graph = new();
        graph.Add(TestGraphs.Library.ByName("Number.Value"), 0, 0);
        graph.Add(TestGraphs.Library.ByName("Math.Sin"), 6000, 0);
        return graph;
    }

    /// <summary>Drains the headless dispatcher so a pending layout pass actually runs.</summary>
    private static class Dispatcher
    {
        internal static void Run() =>
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(Avalonia.Threading.DispatcherPriority.Loaded);
    }
}

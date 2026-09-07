using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Spark.UI.Controls;
using Spark.UI.Shell;
using Spark.UI.Views.Panes;

namespace Spark.UI.Tests;

/// <summary>
/// Moving the viewport pane around the shell — `E9-T13`.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dragging the viewport out of its dock killed the application.</b> Re-parenting a control
/// detaches it from the visual tree and attaches it again a moment later;
/// <see cref="ViewportControl"/> disposed its CPU rasteriser on detach, and a disposed rasteriser
/// throws <see cref="System.ObjectDisposedException"/> from <c>Initialise</c>. The next frame threw
/// from inside <c>Render</c>, on the compositor's own dispatch, where nothing catches it — so the
/// process simply exited, with the stack ending in <c>Dispatcher.MainLoop</c>.
/// </para>
/// <para>
/// <b>The forced software backend is what makes this testable at all.</b> The headless session has
/// no OpenGL, so without <see cref="ViewportControl.ForceSoftwareRenderer"/> the control is still
/// waiting for a context when it draws and never reaches the rasteriser — the exact branch the
/// crash lives in. Setting it reproduces the real machine's state after a re-dock, where the GL
/// context has been de-initialised and the software path is drawing until a new one arrives.
/// </para>
/// </remarks>
public sealed class ViewportDockingTests
{
    /// <summary>
    /// <b>The crash, as a test.</b> Detach the viewport, attach it again, draw — which is what
    /// docking does, in that order.
    /// </summary>
    [Fact]
    public void TheViewportSurvivesBeingReparented() => HeadlessSession.Run(() =>
    {
        ViewportControl viewport = new() { ForceSoftwareRenderer = true };
        Window window = new() { Width = 400, Height = 300, Content = viewport };

        window.Show();
        window.UpdateLayout();

        Draw(viewport);

        // Exactly what re-docking a pane does to its content.
        window.Content = null;
        window.UpdateLayout();

        window.Content = viewport;
        window.UpdateLayout();

        Draw(viewport);
    });

    /// <summary>The viewport tool can be moved into another dock and is then in it.</summary>
    [Fact]
    public void TheViewportCanBeMovedIntoAnotherDock() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, _) = Shell();

        IDock source = factory.DockFor(WorkspacePane.Viewport)!;
        IDock target = factory.DockFor(WorkspacePane.Library)!;

        IDockable moved = source.VisibleDockables![0];
        IDockable beside = target.VisibleDockables![0];

        factory.MoveDockable(source, target, moved, beside);

        window.UpdateLayout();

        Assert.Contains(moved, target.VisibleDockables!);
    });

    /// <summary>The viewport tool can be floated out of the shell.</summary>
    [Fact]
    public void TheViewportCanBeFloated() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, _) = Shell();

        IDock source = factory.DockFor(WorkspacePane.Viewport)!;

        factory.FloatDockable(source.VisibleDockables![0]);

        window.UpdateLayout();
    });

    /// <summary>
    /// <b>A floated pane has to land in a window that can show it.</b> Dock ships no default host
    /// window: <c>FactoryBase.GetHostWindow</c> reads <c>HostWindowLocator</c> and
    /// <c>DefaultHostWindowLocator</c>, and with both unset it hands back null. The tool is still
    /// taken out of its dock and put in a <c>DockWindow</c> - so dragging any pane off the shell
    /// deleted it from the window and showed nothing anywhere, and only <i>Reset layout</i>
    /// brought it back.
    /// </summary>
    [Fact]
    public void AFloatedPaneGetsAWindowToLiveIn() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, IRootDock root) = Shell();

        IDock source = factory.DockFor(WorkspacePane.Viewport)!;
        IDockable floated = source.VisibleDockables![0];

        factory.FloatDockable(floated);
        window.UpdateLayout();

        IDockWindow dockWindow = Assert.Single(root.Windows!);
        Assert.NotNull(dockWindow.Host);
        Assert.NotNull(dockWindow.Layout);
    });

    /// <summary>
    /// <b>A floated pane is an ordinary window of this operating system.</b> Dock's theme binds
    /// <c>ToolChromeControlsWholeWindow</c> to <i>this window holds fewer than two dockables</i>,
    /// and when it is true the window is stripped to <c>BorderOnly</c> and the pane's own header
    /// is promoted to be the title bar — a title bar with maximise and close on it, nowhere to put
    /// minimise, no system menu and no double-click to maximise. A single floated pane was
    /// therefore the one window on the desktop that did not behave like a window (<c>N117</c>).
    /// </summary>
    /// <remarks>
    /// <b>What this can and cannot prove, stated rather than implied.</b> The headless session
    /// runs a bare <c>Application</c> with no Dock theme in it, so the setter being overridden is
    /// not loaded and the property would read <c>false</c> here whether or not anything set it —
    /// asserting the value alone is an assertion that passes on the defect. So it asserts the
    /// <i>local value is present</i>, which is the fix: a local value outranks a
    /// <c>ControlTheme</c> setter for the life of the window. That the chrome then keeps the
    /// native decorations is the theme's own behaviour, and the running application is what shows
    /// it.
    /// </remarks>
    [Fact]
    public void AFloatedPaneKeepsTheOperatingSystemsWindowButtons() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, IRootDock root) = Shell();

        factory.FloatDockable(factory.DockFor(WorkspacePane.Viewport)!.VisibleDockables![0]);
        window.UpdateLayout();

        HostWindow host = Assert.IsType<HostWindow>(Assert.Single(root.Windows!).Host);

        Assert.True(
            host.IsSet(HostWindow.ToolChromeControlsWholeWindowProperty),
            "The factory has to set it locally, or the theme's binding decides.");
        Assert.False(host.ToolChromeControlsWholeWindow);
    });

    /// <summary>Draws the control the way the compositor does, which is where the crash was.</summary>
    private static void Draw(Control control)
    {
        using RenderTargetBitmap target = new(new PixelSize(400, 300));
        using DrawingContext context = target.CreateDrawingContext();

        control.Render(context);
    }

    private static (SparkDockFactory Factory, Window Window, IRootDock Root) Shell()
    {
        SparkDockFactory factory = new();

        // The real viewport and stand-ins for the rest: the inspector cannot be shown in the
        // headless session at all ([N90](../../docs/NOTES.md)), and the pane under test is the one
        // that owns a rendering surface.
        IRootDock root = factory.Build(new Dictionary<WorkspacePane, object?>
        {
            [WorkspacePane.Library] = new UserControl(),
            [WorkspacePane.Canvas] = new UserControl(),
            [WorkspacePane.Viewport] = new ViewportPane(),
            [WorkspacePane.Inspector] = new UserControl(),
        });

        DockControl control = new() { Factory = factory, Layout = root };
        Window window = new() { Width = 1200, Height = 800, Content = control };

        window.Show();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        return (factory, window, root);
    }
}

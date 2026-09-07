using System.Collections.Generic;
using System.Linq;
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
    /// <b>The floating window's chrome is left entirely to Dock, and both ways of taking it over
    /// have now been tried and reverted.</b> <c>ToolChromeControlsWholeWindow</c> is not a
    /// decorations switch — <c>HostWindow.MoveDrag</c> opens with
    /// <c>if (!ToolChromeControlsWholeWindow) return;</c> and it is <c>MoveDrag</c> that starts
    /// both the window drag and the dock tracking, while the drag helper
    /// <c>ToolChromeControl</c> attaches to <c>PART_Grip</c> is gated on the same flag. Setting it
    /// false gave a pane that floated and could not be docked anywhere. Leaving it alone but
    /// forcing <c>WindowDecorations</c> gave two title bars stacked on each other, only the lower
    /// of which could dock. The window buttons live on Dock's own bar instead
    /// (<c>Theming/DockChrome.axaml</c>).
    /// </summary>
    /// <remarks>
    /// The headless session loads no Dock theme, so the values these properties report here mean
    /// nothing — what is asserted is that the factory sets <b>none</b> of them, which is the whole
    /// of the fix and is what a future tidy-up would undo.
    /// </remarks>
    [Fact]
    public void FloatingLeavesTheWindowChromeToDock() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, IRootDock root) = Shell();

        factory.FloatDockable(factory.DockFor(WorkspacePane.Viewport)!.VisibleDockables![0]);
        window.UpdateLayout();

        HostWindow host = Assert.IsType<HostWindow>(Assert.Single(root.Windows!).Host);

        Assert.False(
            host.IsSet(HostWindow.ToolChromeControlsWholeWindowProperty),
            "Dock decides this one. Setting it turns off dragging a floated pane back.");
        Assert.False(
            host.IsSet(Window.WindowDecorationsProperty),
            "Forcing the system's decorations puts a second title bar above Dock's.");
        Assert.False(
            host.IsSet(Window.ExtendClientAreaToDecorationsHintProperty),
            "Forcing the system's decorations puts a second title bar above Dock's.");
    });

    /// <summary>
    /// <b>Minimise is the one window button Dock's chrome cannot supply.</b>
    /// <c>ToolChromeControl</c> declares template parts for a close and a maximise/restore button
    /// and wires their clicks; there is no third, and a style cannot attach an event handler. So
    /// the button is bound to this method as a command, which is why it is public.
    /// </summary>
    [Fact]
    public void AFloatedPaneCanBeMinimised() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, IRootDock root) = Shell();

        IDockable viewport = factory.DockFor(WorkspacePane.Viewport)!.VisibleDockables![0];

        factory.FloatDockable(viewport);
        window.UpdateLayout();

        HostWindow host = Assert.IsType<HostWindow>(Assert.Single(root.Windows!).Host);

        factory.MinimiseFloatingWindow(viewport);

        Assert.Equal(WindowState.Minimized, host.WindowState);
    });

    /// <summary>A pane that is not floating has no window to minimise, and asking is not an error.</summary>
    [Fact]
    public void MinimisingADockedPaneDoesNothing() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, _, _) = Shell();

        factory.MinimiseFloatingWindow(factory.DockFor(WorkspacePane.Viewport)!.VisibleDockables![0]);
        factory.MinimiseFloatingWindow(null);
    });

    /// <summary>
    /// <b>Closing a floated pane's window puts the pane back rather than destroying it.</b> The
    /// close button is a consequence of the native decorations — while Dock drew the window there
    /// was none, because the chrome's own is bound to <c>CanClose</c> and these four panes cannot
    /// be closed. Dock's answer to a closing host window is to take everything in it away, which
    /// is <c>E8-T45</c>'s failure arriving through a door that did not exist before.
    /// </summary>
    [Fact]
    public void ClosingAFloatedPanesWindowReDocksItInsteadOfDeletingIt() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, IRootDock root) = Shell();

        IDockable viewport = factory.DockFor(WorkspacePane.Viewport)!.VisibleDockables![0];

        factory.FloatDockable(viewport);
        window.UpdateLayout();

        IDockWindow floated = Assert.Single(root.Windows!);
        Assert.False(Reaches(root, viewport), "It is in the window now, not the shell.");

        factory.OnWindowClosing(floated);

        Assert.True(Reaches(root, viewport), "Closing the window must not take the pane with it.");
    });

    /// <summary>
    /// And it lands beside its neighbour in the column it belongs to. <b>The dock a pane came from
    /// may have gone with it</b> — Dock floats the whole <c>ToolDock</c> when the pane was the only
    /// thing in it — so the landing place is chosen from what is still in the shell's tree. A
    /// viewport that came back beside the library would be docked and visibly wrong.
    /// </summary>
    [Fact]
    public void AReturningPaneLandsBesideItsColumnNeighbour() => HeadlessSession.Run(() =>
    {
        (SparkDockFactory factory, Window window, IRootDock root) = Shell();

        IDockable viewport = factory.DockFor(WorkspacePane.Viewport)!.VisibleDockables![0];

        factory.FloatDockable(viewport);
        window.UpdateLayout();
        factory.OnWindowClosing(Assert.Single(root.Windows!));

        Assert.Same(factory.DockFor(WorkspacePane.Canvas), viewport.Owner);
    });

    /// <summary>Whether the shell's tree still reaches a dockable.</summary>
    private static bool Reaches(IDockable node, IDockable target) =>
        ReferenceEquals(node, target)
        || ((node as IDock)?.VisibleDockables ?? []).Any(child => Reaches(child, target));

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

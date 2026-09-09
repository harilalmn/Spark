using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Settings;

namespace Spark.UI.Shell;

/// <summary>
/// Builds the shell's dock layout and keeps it in step with a <see cref="WorkspaceLayout"/>.
/// </summary>
/// <remarks>
/// <para>
/// The layout is built <b>once</b> and then adjusted in place. Rebuilding it on every preset would
/// mean re-parenting the panes, and one of them owns an OpenGL surface: a viewport that is torn
/// out of the visual tree and put back has to re-acquire its context, and <i>Modelling</i> would
/// cost a black frame for no reason. Proportions and visibility are both things the existing tree
/// can be asked for.
/// </para>
/// <para>
/// The tree is deliberately shallow — a row of three, with the middle one a column of two —
/// because that is the arrangement <see cref="WorkspaceLayout"/> can describe. A layout model that
/// cannot express what the shell is showing is worse than no layout model, since it silently stops
/// being true the first time somebody drags a pane somewhere it cannot represent.
/// </para>
/// </remarks>
public sealed class SparkDockFactory : Factory
{
    /// <summary>
    /// A floating pane must not outlive the shell it came from.
    /// </summary>
    /// <remarks>
    /// Dock's default is to leave floating windows open, which for a document-shaped application is
    /// arguable and here is not: closing the main window left the pane on screen with the graph it
    /// was showing already gone, and the process alive behind it because a window was still up.
    /// It is a static on <c>DockSettings</c> rather than a property of a window, so it is set once,
    /// here, beside the only code that makes floating windows at all.
    /// </remarks>
    static SparkDockFactory() => DockSettings.CloseFloatingWindowsOnMainWindowClose = true;

    private readonly Dictionary<WorkspacePane, Tool> _tools = [];
    private readonly Dictionary<WorkspacePane, ToolDock> _docks = [];
    private ProportionalDock? _columns;
    private ProportionalDock? _center;
    private RootDock? _root;

    /// <summary>
    /// Builds the four-pane shell.
    /// </summary>
    /// <param name="content">
    /// What to put in each pane, by pane. A pane with no entry gets an empty tool, which is what
    /// makes this callable from a test that has no Avalonia controls to hand.
    /// </param>
    /// <returns>The root dock, ready to assign to a <c>DockControl</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    public IRootDock Build(IReadOnlyDictionary<WorkspacePane, object?> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A REBUILD IS THE ONLY WAY BACK FROM A LAYOUT A USER HAS DRAGGED APART.
        //
        // Dock removes a ToolDock from the tree once its last tool is dragged out of it, and
        // makes new ones where a tool is dropped. So the objects this factory recorded when it
        // first built the shell can all be orphans - which is why `Apply` had no visible effect
        // for a user who had rearranged everything and then asked for the layout back (`E8-T33`):
        // it was setting proportions on docks that were no longer in the tree.
        //
        // Whatever floating windows the dragging produced are closed first. They hold panes, and
        // a rebuilt shell that left them open would be a second copy of the same controls.
        foreach (IDockWindow window in (_root?.Windows ?? []).ToArray())
        {
            window.Exit();
        }

        _tools.Clear();
        _docks.Clear();

        _center = Column(
            Pane(WorkspacePane.Canvas, "Canvas", content),
            Pane(WorkspacePane.Viewport, "Viewport", content),
            Pane(WorkspacePane.Console, "Console", content));

        _columns = Row(
            Pane(WorkspacePane.Library, "Library", content),
            _center,
            Pane(WorkspacePane.Inspector, "Properties", content));

        _root = new RootDock
        {
            Id = "Shell",
            Title = "Shell",
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(_columns),
            ActiveDockable = _columns,
            DefaultDockable = _columns,
        };

        InitLayout(_root);
        return _root;
    }

    /// <summary>
    /// Prepares the layout, and tells Dock what a floating window is made of.
    /// </summary>
    /// <param name="layout">The layout being initialised.</param>
    /// <remarks>
    /// <b>Dock ships no default host window, and a pane dragged out of the shell without one is
    /// simply deleted.</b> <c>FactoryBase.GetHostWindow</c> reads these two locators and hands
    /// back null when neither is set; floating carries on regardless — the tool is taken out of
    /// its dock and given to a <c>DockWindow</c> that has nothing to present it in. What a user
    /// sees is a pane that vanishes the moment they drag it off the window, with <i>Reset
    /// layout</i> as the only way back, because a rebuild is the only thing that puts the tool
    /// into a dock again (<c>N116</c>).
    /// <para>
    /// Both are set. The keyed locator is what Dock asks for first; the default is the fallback
    /// for a window Dock creates under some other key, and leaving that one null would reopen
    /// the same hole for any path that does not use <c>nameof(IDockWindow)</c>.
    /// </para>
    /// </remarks>
    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator ??= new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = FloatingWindow,
        };

        DefaultHostWindowLocator ??= FloatingWindow;

        base.InitLayout(layout);
    }

    /// <summary>
    /// The window a pane dragged off the shell lands in.
    /// </summary>
    /// <returns>The host window.</returns>
    /// <remarks>
    /// <para>
    /// <b>Its chrome is deliberately Dock's and not the operating system's, and that is the second
    /// thing this was got wrong.</b> Giving the window <c>WindowDecorations="Full"</c> produced two
    /// title bars stacked on each other — the system's, and the pane's own underneath it — and only
    /// the lower one could be dragged back into the shell. Handing the whole job to the system
    /// instead is not available: <c>ToolChromeControlsWholeWindow</c> is not a decorations switch,
    /// it is the switch on the drag-and-dock path. <c>HostWindow.MoveDrag</c> opens with
    /// <c>if (!ToolChromeControlsWholeWindow) return;</c> and it is <c>MoveDrag</c> that starts both
    /// the window drag and the dock tracking, while the drag helper <c>ToolChromeControl</c>
    /// attaches to <c>PART_Grip</c> is gated on the same flag. A pane whose window wore the system's
    /// title bar could not be docked anywhere at all (<c>N117</c>).
    /// </para>
    /// <para>
    /// So there is <b>one</b> title bar, Dock's, and <c>Theming/DockChrome.axaml</c> puts minimise,
    /// maximise/restore and close on it — the buttons the system's would have carried — leaving the
    /// gesture that re-docks exactly where Dock expects to find it.
    /// </para>
    /// </remarks>
    private static HostWindow FloatingWindow() => new();

    /// <summary>
    /// How much of the centre column the console takes when it is showing (<c>E8-T80</c>).
    /// </summary>
    /// <remarks>
    /// <b>A constant rather than a fourth number in <see cref="WorkspaceLayout"/>.</b> That type
    /// serialises, so a new field is a format question — a saved layout written before it exists
    /// would read back a zero-height console and look broken. A user who wants it taller drags the
    /// splitter, which Dock already honours until the next <c>Apply</c>.
    /// </remarks>
    public const double ConsoleFraction = 0.25;

    /// <summary>
    /// Brings the built layout into line with a workspace: the pane proportions, and which panes
    /// are showing at all.
    /// </summary>
    /// <param name="layout">The workspace to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> has not been called.</exception>
    /// <remarks>
    /// Visibility is applied before the proportions, because hiding the last tool in a dock
    /// collapses the dock, and the proportions that matter are the ones across whatever is left.
    /// </remarks>
    public void Apply(WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (_root is null || _center is null)
        {
            throw new InvalidOperationException("Build the layout before applying a workspace to it.");
        }

        foreach ((WorkspacePane pane, Tool tool) in _tools)
        {
            SetPaneVisible(_docks[pane], tool, layout.IsVisible(pane));
        }

        // The center takes whatever the two side panes are not using. Asking for 0.16 and 0.20 of
        // a window that is only showing the canvas would leave the canvas at 0.64 of it and two
        // thirds of the shell empty.
        double library = layout.IsVisible(WorkspacePane.Library) ? layout.LibraryFraction : 0;
        double inspector = layout.IsVisible(WorkspacePane.Inspector) ? layout.InspectorFraction : 0;

        SetProportion(WorkspacePane.Library, library);
        SetProportion(WorkspacePane.Inspector, inspector);
        _center.Proportion = Math.Max(0, 1 - library - inspector);

        // Same again down the middle: with the viewport hidden, a canvas still asking for 0.55
        // would leave the bottom half of the column empty rather than give the canvas the room.
        bool canvas = layout.IsVisible(WorkspacePane.Canvas);
        bool viewport = layout.IsVisible(WorkspacePane.Viewport);

        // `E8-T80`: the console takes a fixed slice off the bottom of the column and the other two
        // split what is left in the proportion they already had. **When it is hidden this is
        // arithmetically identical to what was here before** - `console` is zero and `rest` is one -
        // which is what keeps the existing layout tests meaningful rather than merely passing.
        double console = layout.IsVisible(WorkspacePane.Console) ? ConsoleFraction : 0;
        double rest = 1 - console;

        SetProportion(WorkspacePane.Canvas, canvas ? (viewport ? layout.CanvasFraction * rest : rest) : 0);
        SetProportion(WorkspacePane.Viewport, viewport ? (canvas ? (1 - layout.CanvasFraction) * rest : rest) : 0);
        SetProportion(WorkspacePane.Console, console);
    }

    /// <summary>
    /// Gives every pane the object its bindings are written against.
    /// </summary>
    /// <param name="context">The view model, or null to clear.</param>
    /// <remarks>
    /// <b>A tool with a null <c>Context</c> shows nothing at all</b>, and a pane that inherits its
    /// <c>DataContext</c> shows almost nothing — which is worse, because it looks like it worked.
    /// Dock puts the <i>dockable</i> on the presented content's <c>DataContext</c>, so a pane whose
    /// bindings are compiled against <c>MainWindowViewModel</c> resolves them against a
    /// <c>Tool</c> instead. Compiled bindings do not throw on a mismatched type; they bind to
    /// nothing. The pane then draws its static markup — its title, its buttons — and every bound
    /// row inside it is simply absent: an empty library list under a correct heading
    /// (N35). So the context is set on both, and the explicit
    /// <c>DataContext</c> is the half that makes the bindings work.
    /// </remarks>
    public void SetContext(object? context)
    {
        foreach (Tool tool in _tools.Values)
        {
            tool.Context = context;

            if (tool.Content is StyledElement pane)
            {
                pane.DataContext = context;
            }
        }
    }

    /// <summary>
    /// A floating pane's window is being closed: the panes in it go back into the shell rather
    /// than out of existence.
    /// </summary>
    /// <param name="window">The window closing, or null.</param>
    /// <returns>True — the close always proceeds, on an emptied window.</returns>
    /// <remarks>
    /// <para>
    /// <b>The close button is new, and it deleted panes.</b> While a floated pane's window was
    /// drawn by Dock it had no close button at all — the chrome's own is bound to <c>CanClose</c>,
    /// which is false on all four panes. Giving the window the operating system's decorations gave
    /// it the operating system's <b>X</b>, and Dock's answer to that is to take the window and
    /// everything in it away: the pane was gone until <i>Reset layout</i>, which is exactly the
    /// failure <c>E8-T45</c> had just fixed, arriving through a door that did not exist before.
    /// </para>
    /// <para>
    /// So closing a floated pane <b>re-docks it</b>. That is what the button means here: the pane
    /// is not a document and cannot be closed, so the only sense the gesture can carry is <i>stop
    /// floating</i>. It lands in <see cref="LandingDock"/> — its own dock when that is still in the
    /// shell, and a neighbour's when a drag has taken it out — and Dock closes the emptied window
    /// itself. To stop <i>showing</i> a pane there is <b>View → Workspace</b>, which is where a
    /// gesture that hides things belongs.
    /// </para>
    /// </remarks>
    public override bool OnWindowClosing(IDockWindow? window)
    {
        if (_root is not null && window?.Layout is { } layout)
        {
            // Materialised first. Moving a dockable edits the collections being walked, and the
            // window's tree is small enough that the copy costs nothing worth measuring.
            foreach (Tool tool in ToolsIn(layout).ToArray())
            {
                ReturnToShell(tool);
            }
        }

        return base.OnWindowClosing(window);
    }

    /// <summary>
    /// Minimises the floating window a pane is in. Bound from the pane's title bar.
    /// </summary>
    /// <param name="dockable">The pane whose window to minimise.</param>
    /// <remarks>
    /// <b>Minimise is the one window button Dock's chrome cannot supply on its own.</b>
    /// <c>ToolChromeControl</c> declares template parts for a close button and a maximise/restore
    /// button and wires their clicks; there is no third. So the button in
    /// <c>Theming/DockChrome.axaml</c> is bound to this as a command instead — the same mechanism
    /// Dock's own template uses for <c>Owner.Factory.FloatDockable</c> — which is why it is public
    /// and takes an <see cref="IDockable"/> rather than being a private helper.
    /// </remarks>
    public void MinimiseFloatingWindow(IDockable? dockable)
    {
        if (WindowOf(dockable)?.Host is Window window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    /// <summary>
    /// Sends a floating pane back into the shell. Bound from the pane's title bar, and the answer
    /// to closing its window by any other means.
    /// </summary>
    /// <param name="dockable">The pane to bring back.</param>
    /// <remarks>
    /// <b>Close cannot mean close here.</b> These four panes are the shell and <c>CanClose</c> is
    /// false on all of them, so the only sense the gesture can carry is <i>stop floating</i> — and
    /// letting it mean what Dock means by it would take the window away with the pane inside, which
    /// is the defect <c>E8-T45</c> fixed. The pane lands in <see cref="LandingDock"/>, and Dock
    /// closes the emptied window itself. To stop <i>showing</i> a pane there is
    /// <b>View → Workspace</b>, which is where a gesture that hides things belongs.
    /// </remarks>
    public void ReturnToShell(IDockable? dockable)
    {
        if (dockable is not Tool tool
            || PaneOf(tool) is not { } pane
            || tool.Owner is not IDock source
            || LandingDock(pane) is not { } target
            || ReferenceEquals(source, target))
        {
            return;
        }

        MoveDockable(source, target, tool, null);
    }

    /// <summary>The floating window a dockable is in, if it is in one.</summary>
    /// <param name="dockable">The dockable.</param>
    /// <returns>The window, or null.</returns>
    private IDockWindow? WindowOf(IDockable? dockable) =>
        dockable is null
            ? null
            : (_root?.Windows ?? [])
                .FirstOrDefault(window => window.Layout is { } layout && Reaches(layout, dockable));

    /// <summary>Every one of this shell's panes inside a dockable tree.</summary>
    /// <param name="dockable">The root to walk.</param>
    /// <returns>The tools found, depth first.</returns>
    private IEnumerable<Tool> ToolsIn(IDockable dockable)
    {
        if (dockable is Tool tool && _tools.ContainsValue(tool))
        {
            yield return tool;
        }

        foreach (IDockable child in (dockable as IDock)?.VisibleDockables ?? [])
        {
            foreach (Tool found in ToolsIn(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>Where a pane coming back from a floating window should land.</summary>
    /// <param name="pane">The pane.</param>
    /// <returns>A dock that is still in the shell, or null when none is.</returns>
    /// <remarks>
    /// Its own dock first, and a neighbour after — because <b>the dock a pane came from may have
    /// gone with it</b>. Dock floats the whole <c>ToolDock</c> when the pane was the only thing in
    /// it, so <c>DockFor</c> can hand back an object that is no longer anywhere in the shell's
    /// tree; a pane docked into that would be re-attached to an orphan and would not appear
    /// (`E8-T33`, from the other direction). Membership is therefore asked of the tree rather than
    /// of this factory's bookkeeping, which a drag can leave stale.
    /// </remarks>
    private ToolDock? LandingDock(WorkspacePane pane)
    {
        // Its own dock first, then its neighbour in the column it belongs to, then the rest. The
        // neighbour matters: a viewport that came back beside the library rather than under the
        // canvas is technically docked and visibly wrong, and that is what plain enum order gave.
        WorkspacePane[] order = pane switch
        {
            WorkspacePane.Canvas =>
                [WorkspacePane.Canvas, WorkspacePane.Viewport, WorkspacePane.Library, WorkspacePane.Inspector],
            WorkspacePane.Viewport =>
                [WorkspacePane.Viewport, WorkspacePane.Canvas, WorkspacePane.Library, WorkspacePane.Inspector],
            WorkspacePane.Library =>
                [WorkspacePane.Library, WorkspacePane.Canvas, WorkspacePane.Viewport, WorkspacePane.Inspector],
            _ =>
                [WorkspacePane.Inspector, WorkspacePane.Canvas, WorkspacePane.Viewport, WorkspacePane.Library],
        };

        return order
            .Select(candidate => _docks.TryGetValue(candidate, out ToolDock? dock) ? dock : null)
            .FirstOrDefault(dock => dock is not null && IsInShell(dock));
    }

    /// <summary>Whether a dockable is still somewhere in the shell's own tree.</summary>
    /// <param name="dockable">The dockable to look for.</param>
    /// <returns>True when the root reaches it.</returns>
    private bool IsInShell(IDockable dockable) => _root is not null && Reaches(_root, dockable);

    private static bool Reaches(IDockable node, IDockable target) =>
        ReferenceEquals(node, target)
        || ((node as IDock)?.VisibleDockables ?? []).Any(child => Reaches(child, target));

    /// <summary>Which pane a dockable is, if it is one of the four.</summary>
    /// <param name="dockable">The dockable.</param>
    /// <returns>The pane, or null.</returns>
    private WorkspacePane? PaneOf(IDockable dockable)
    {
        foreach ((WorkspacePane pane, Tool tool) in _tools)
        {
            if (ReferenceEquals(tool, dockable))
            {
                return pane;
            }
        }

        return null;
    }

    /// <summary>The dock holding a pane, for a test that wants to read a proportion back.</summary>
    /// <param name="pane">The pane.</param>
    /// <returns>Its dock, or null when <see cref="Build"/> has not run.</returns>
    public IDock? DockFor(WorkspacePane pane) =>
        _docks.TryGetValue(pane, out ToolDock? dock) ? dock : null;

    /// <summary>Whether a pane is currently in the tree rather than hidden.</summary>
    /// <param name="pane">The pane.</param>
    /// <returns>True when it is showing.</returns>
    /// <remarks>
    /// Asked as <i>is the tool still among its dock's visible children</i>, and deliberately not
    /// as <c>Owner is not null</c>. <c>HideDockable</c> leaves <c>Owner</c> set — it has to, since
    /// that is where <c>RestoreDockable</c> puts the tool back — so an owner-based answer says
    /// every pane is showing, always. It is a predicate that is wrong only in the direction that
    /// looks like success, which is how the first version of this survived a screenshot.
    /// </remarks>
    public bool IsShowing(WorkspacePane pane) =>
        _tools.TryGetValue(pane, out Tool? tool)
        && _docks.TryGetValue(pane, out ToolDock? dock)
        && dock.VisibleDockables?.Contains(tool) == true;

    private void SetPaneVisible(ToolDock dock, Tool tool, bool visible)
    {
        // Hiding a dockable that is already hidden re-registers it, and restoring one that was
        // never hidden has no owner to go back to. Both are asked for on every preset, so both
        // have to be no-ops rather than accidents.
        bool showing = dock.VisibleDockables?.Contains(tool) == true;

        if (visible && !showing)
        {
            RestoreDockable(tool);
        }
        else if (!visible && showing)
        {
            HideDockable(tool);
        }
    }

    private void SetProportion(WorkspacePane pane, double proportion)
    {
        if (_docks.TryGetValue(pane, out ToolDock? dock))
        {
            dock.Proportion = proportion;
        }
    }

    private ToolDock Pane(
        WorkspacePane pane, string title, IReadOnlyDictionary<WorkspacePane, object?> content)
    {
        // WHAT A PANE'S TITLE BAR IS ALLOWED TO OFFER, AND WHY IT IS ALMOST NOTHING.
        //
        // Dock draws a button per capability, so this is the chrome. `CanPin` false takes the pin
        // off the title bar and the auto-hide entries out of the menu: Dock's pin is auto-hide,
        // and auto-hide collapses a pane into an edge strip that then shows nothing at all when
        // clicked (`N117`). `CanDockAsDocument` false because there is no DocumentDock in this
        // shell to dock into. `CanClose` was already false - these four panes are the shell, and
        // View > Workspace is how you stop showing one.
        //
        // What is left is `CanFloat`, which is the one gesture that behaves the way a Windows
        // user expects it to: drag the pane off, get a window.
        Tool tool = new()
        {
            Id = pane.ToString(),
            Title = title,
            CanClose = false,
            CanFloat = true,
            CanPin = false,
            CanDockAsDocument = false,
        };

        if (content.TryGetValue(pane, out object? body) && body is not null)
        {
            tool.Content = body;
        }

        // Alignment is deliberately left Unset. A ToolDock defaults to AutoHide with
        // IsExpanded false, so giving it an alignment turns it into an auto-hiding strip that
        // draws its title bar and nothing else - a pane that looks correctly placed and
        // permanently empty. These four panes are the shell, not drawers pinned to its edges.
        ToolDock dock = new()
        {
            Id = pane + "Dock",
            Title = title,
            VisibleDockables = CreateList<IDockable>(tool),
            ActiveDockable = tool,
        };

        _tools[pane] = tool;
        _docks[pane] = dock;
        return dock;
    }

    private ProportionalDock Row(params IDockable[] children) =>
        Stack(Orientation.Horizontal, children);

    private ProportionalDock Column(params IDockable[] children) =>
        Stack(Orientation.Vertical, children);

    private ProportionalDock Stack(Orientation orientation, IDockable[] children)
    {
        // A splitter between each pair and nowhere else. Dock treats splitters as ordinary
        // children of the stack, so a trailing one would claim a strip of the window that
        // separates a pane from the edge.
        List<IDockable> visible = [];
        foreach (IDockable child in children)
        {
            if (visible.Count > 0)
            {
                visible.Add(new ProportionalDockSplitter());
            }

            visible.Add(child);
        }

        return new ProportionalDock
        {
            Orientation = orientation,
            VisibleDockables = CreateList(visible.ToArray()),
            ActiveDockable = children.FirstOrDefault(),
        };
    }
}

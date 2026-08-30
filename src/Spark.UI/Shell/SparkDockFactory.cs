using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

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
    private readonly Dictionary<WorkspacePane, Tool> _tools = [];
    private readonly Dictionary<WorkspacePane, ToolDock> _docks = [];
    private ProportionalDock? _columns;
    private ProportionalDock? _centre;
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

        _centre = Column(
            Pane(WorkspacePane.Canvas, "Canvas", content),
            Pane(WorkspacePane.Viewport, "Viewport", content));

        _columns = Row(
            Pane(WorkspacePane.Library, "Library", content),
            _centre,
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

        if (_root is null || _centre is null)
        {
            throw new InvalidOperationException("Build the layout before applying a workspace to it.");
        }

        foreach ((WorkspacePane pane, Tool tool) in _tools)
        {
            SetPaneVisible(_docks[pane], tool, layout.IsVisible(pane));
        }

        // The centre takes whatever the two side panes are not using. Asking for 0.16 and 0.20 of
        // a window that is only showing the canvas would leave the canvas at 0.64 of it and two
        // thirds of the shell empty.
        double library = layout.IsVisible(WorkspacePane.Library) ? layout.LibraryFraction : 0;
        double inspector = layout.IsVisible(WorkspacePane.Inspector) ? layout.InspectorFraction : 0;

        SetProportion(WorkspacePane.Library, library);
        SetProportion(WorkspacePane.Inspector, inspector);
        _centre.Proportion = Math.Max(0, 1 - library - inspector);

        // Same again down the middle: with the viewport hidden, a canvas still asking for 0.55
        // would leave the bottom half of the column empty rather than give the canvas the room.
        bool canvas = layout.IsVisible(WorkspacePane.Canvas);
        bool viewport = layout.IsVisible(WorkspacePane.Viewport);

        SetProportion(WorkspacePane.Canvas, canvas ? (viewport ? layout.CanvasFraction : 1) : 0);
        SetProportion(WorkspacePane.Viewport, viewport ? (canvas ? 1 - layout.CanvasFraction : 1) : 0);
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
        Tool tool = new()
        {
            Id = pane.ToString(),
            Title = title,
            CanClose = false,
            CanFloat = true,
            CanPin = true,
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

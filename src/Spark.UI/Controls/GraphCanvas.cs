using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Spark.UI.Canvas;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Controls;

/// <summary>
/// What one gesture did to the graph: a phrase for the undo menu, and whether the change is one
/// the evaluator has to see.
/// </summary>
/// <remarks>
/// The two are genuinely independent, and moving a node is the case that proves it. A move changes
/// the document — it is saved, and a user expects to be able to undo it — but it cannot change any
/// value, because a position is not in a node's provenance and never enters a cache key. Reporting
/// both facts in one event is what lets the shell record a step without also starting a run that
/// could only produce the answer it already has.
/// </remarks>
/// <param name="label">What the edit did, in the words the menu shows: <c>Move node</c>.</param>
/// <param name="affectsEvaluation">Whether the graph has to be run again.</param>
public sealed class GraphEditedEventArgs(string label, bool affectsEvaluation) : EventArgs
{
    /// <summary>What the edit did, phrased for an undo menu.</summary>
    public string Label { get; } = label;

    /// <summary>Whether the change requires the graph to be evaluated again.</summary>
    public bool AffectsEvaluation { get; } = affectsEvaluation;
}

/// <summary>
/// A request to create a node at a point on the canvas, raised by double-clicking empty space.
/// </summary>
/// <remarks>
/// The canvas reports where and never what. It has no library, cannot construct a node instance
/// without naming an engine type, and would break the seam ADR-0005 draws if it tried — so it says
/// "here", and the shell decides what "here" gets.
/// </remarks>
/// <param name="worldX">Where the node's left edge goes, in world coordinates.</param>
/// <param name="worldY">Where the node's top edge goes, in world coordinates.</param>
/// <param name="screenX">The same point in control coordinates, for placing a popup over it.</param>
/// <param name="screenY">The same point in control coordinates.</param>
public sealed class CanvasCreateRequestedEventArgs(
    double worldX, double worldY, double screenX, double screenY) : EventArgs
{
    /// <summary>Where the node's left edge goes, in world coordinates.</summary>
    public double WorldX { get; } = worldX;

    /// <summary>Where the node's top edge goes, in world coordinates.</summary>
    public double WorldY { get; } = worldY;

    /// <summary>The same point in control coordinates.</summary>
    public double ScreenX { get; } = screenX;

    /// <summary>The same point in control coordinates.</summary>
    public double ScreenY { get; } = screenY;
}

/// <summary>
/// The node canvas: <b>one</b> Avalonia control that draws the entire graph in immediate mode
/// over a retained <see cref="SceneIndex"/> (ADR-0013). Nodes, ports and wires are drawn, not
/// instantiated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is one control and not one control per node.</b> The obvious implementation — an
/// <c>ItemsControl</c> over a <c>Canvas</c> with a template per node — collapses somewhere between
/// 500 and 2,000 controls because layout and hit-test costs are per-visual and the framework pays
/// them whether or not a node is visible. Real graphs exceed that, so the collapse is the expected
/// steady state for a serious user rather than a corner case. Drawing a few thousand rounded
/// rectangles and Béziers through Skia is trivial; the framework machinery per node is not.
/// </para>
/// <para>
/// <b>Everything is drawn in world coordinates under one pushed transform.</b> Pan and zoom are
/// that transform and nothing else — never per-node layout. Strokes that must hold a screen-space
/// width (the wire casing and core, the 2 px state rings, the focus sandwich) are drawn with a
/// thickness of <c>screenWidth / zoom</c>, which is what lets a 2 px error ring stay 2 px at 15%
/// zoom. That ring is the only element on the canvas that refuses to scale, and it refuses because
/// "where is the broken node?" is the question a user zooms out to answer.
/// </para>
/// <para>
/// <b>What is not here yet.</b> The hybrid overlay — a real Avalonia control positioned over the
/// node currently being edited — is not implemented; nothing on the canvas is editable in place
/// yet. Keyboard navigation between nodes is the M8 accessibility pass. Groups, notes, the
/// evaluating animation and the frozen and not-evaluated states are specified in the design
/// language and are not drawn.
/// </para>
/// </remarks>
public sealed class GraphCanvas : Control
{
    private const double CornerRadius = 6;
    private const double PortRadius = 2.5;
    private const double PortHoverRadius = 3.5;
    private const double PortHitScreenSize = 14;
    private const double PortMinimumHitScreenSize = 10;
    private const double WireHitScreenSize = 6;
    private const int WireHitSamples = 16;
    private const double GlyphFontSize = 12;
    private const double HeaderFontSize = 12;

    /// <summary>The size a note's own text is drawn at.</summary>
    private const double NoteFontSize = 12;

    /// <summary>The inset between a note's edge and its text.</summary>
    private const double NotePadding = 10;

    /// <summary>The gap between a node's bottom edge and its preview bubble.</summary>
    private const double PreviewGap = 6;

    /// <summary>The inset between a preview bubble's edge and its text.</summary>
    private const double PreviewPadding = 8;

    /// <summary>The gap between the rank line and the value line inside a bubble.</summary>
    private const double PreviewLineGap = 3;

    /// <summary>How wide a preview bubble's value line may grow before it wraps.</summary>
    private const double PreviewMaximumWidth = 320;
    private const double PortFontSize = 11;
    private const double TypeFontSize = 10;
    private const double TypeGap = 6;
    private const double PortLabelInset = 9;
    private const double MinimumRowGap = 8;
    private const int MaximumCachedTextRuns = 4096;

    private static readonly Typeface HeaderTypeface =
        new("Inter", FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);

    private static readonly Typeface LabelTypeface =
        new("Inter", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    private readonly SceneIndex _index = new();
    private readonly CanvasTransform _transform = new();
    private readonly Dictionary<string, FormattedText> _headerText = [];
    private readonly Dictionary<string, FormattedText> _labelText = [];
    private readonly Dictionary<string, FormattedText> _glyphText = [];
    private readonly Dictionary<string, FormattedText> _typeText = [];
    private readonly List<WireVisual> _wireVisuals = [];
    private readonly HashSet<int> _selection = [];

    /// <summary>Which nodes already got a bubble this frame, so none is drawn twice.</summary>
    private readonly HashSet<int> _previewsDrawn = [];
    private readonly HashSet<CanvasPort> _connectedPorts = [];

    private CanvasGraph _graph = new();
    private bool _indexDirty = true;
    private bool _fitPending;
    private (double Zoom, double OffsetX, double OffsetY) _fitDeferredFrom;

    private InteractionMode _mode;
    private Point _pointerAnchor;
    private Point _dragStartWorld;
    private int _hoverNode = -1;
    private int _focusNode = -1;
    private CanvasPort? _hoverPort;
    private CanvasPort? _dragSourcePort;
    private CanvasWire? _selectedWire;
    private CanvasNote? _selectedNote;
    private CanvasNote? _hoverNote;
    private CanvasGroup? _selectedGroup;
    private Point _dragWireWorldEnd;
    private WireOutcome _dragOutcome = WireOutcome.Refused;
    private Point _marqueeStartWorld;
    private Point _marqueeEndWorld;
    private double _dragTotalX;
    private double _dragTotalY;

    /// <summary>Creates an empty canvas.</summary>
    public GraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Background = SparkPalette.CanvasBackgroundBrush;

        // The focus sandwich is drawn by this control rather than by the framework (ADR-0013), so
        // the control has to repaint when focus arrives or leaves. Nothing else notices.
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>
    /// Raised whenever a gesture changed the document — a wire drawn or removed, a node deleted, a
    /// selection moved. The shell listens for this, records an undo step, and starts an evaluation
    /// when <see cref="GraphEditedEventArgs.AffectsEvaluation"/> says one is needed.
    /// </summary>
    /// <remarks>
    /// The canvas reports intent and never evaluates anything itself. Evaluation is off the UI
    /// thread and belongs to the view model; a control that started a run would be doing it on the
    /// thread it is drawing on.
    /// </remarks>
    public event EventHandler<GraphEditedEventArgs>? GraphChanged;

    /// <summary>Raised when the selected nodes change, so the inspector can follow.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised when empty canvas is double-clicked, which is a request to create a node there.
    /// </summary>
    /// <remarks>
    /// Dynamo's gesture, and the reason to copy it is that it is the one interaction a graph
    /// author repeats more than any other. Hunting a tree for a node you can already name is the
    /// slowest part of building a graph, and it gets slower with every package installed.
    /// </remarks>
    public event EventHandler<CanvasCreateRequestedEventArgs>? CreateRequested;

    private enum InteractionMode
    {
        None,
        Panning,
        DraggingNodes,
        Marquee,
        DraggingWire,
        DraggingNote,
        DraggingGroup,
    }

    /// <summary>The canvas background fill. Exposed so the shell can paint the same colour behind it.</summary>
    public IBrush? Background { get; set; }

    /// <summary>Frame timings for the on-screen readout and for the benchmark.</summary>
    public FrameTimer Frames { get; } = new();

    /// <summary>Whether the frame-time readout is drawn in the corner.</summary>
    public bool ShowFrameStatistics { get; set; }

    /// <summary>The pan and zoom transform. Mutating it requires an explicit invalidate.</summary>
    public CanvasTransform Transform => _transform;

    /// <summary>
    /// The slots currently selected, as indices into <see cref="CanvasGraph.Nodes"/>.
    /// </summary>
    /// <remarks>
    /// Exposed as a read-only view rather than raised as a change notification. Two thousand nodes
    /// pushed through <c>INotifyPropertyChanged</c> is the cost ADR-0013 exists to avoid, and the
    /// inspector reads this once per selection change rather than binding to it.
    /// </remarks>
    public IReadOnlySet<int> Selection => _selection;

    /// <summary>The slot the keyboard acts from, or −1 when nothing is focused.</summary>
    public int FocusedSlot => _focusNode;

    /// <summary>The port under the pointer, or null.</summary>
    public CanvasPort? HoveredPort => _hoverPort;

    /// <summary>The wire the last click selected, or null.</summary>
    public CanvasWire? SelectedWire => _selectedWire;

    /// <summary>The selected note, or null.</summary>
    /// <remarks>
    /// A third kind of selection beside nodes and wires, and deliberately not folded into the node
    /// selection. That set holds <i>slots</i>, which index <c>Graph.Nodes</c>; a note has no slot,
    /// and giving it a fake one would make every existing loop over the selection wrong in a way
    /// the compiler could not see.
    /// </remarks>
    public CanvasNote? SelectedNote => _selectedNote;

    /// <summary>The selected group, or null.</summary>
    public CanvasGroup? SelectedGroup => _selectedGroup;

    /// <summary>
    /// Rebuilds the spatial index and the wire geometry after the graph was edited from outside the
    /// canvas — the library panel placing a node, the inspector changing a literal.
    /// </summary>
    /// <remarks>
    /// The canvas never places a node itself. A view that constructed a node instance would have to
    /// reach into <c>Spark.Engine</c>, and the whole point of the seam is that it does not.
    /// </remarks>
    public void RefreshStructure()
    {
        _wireVisuals.Clear();
        _indexDirty = true;
        InvalidateVisual();
    }

    /// <summary>Adds a slot to the selection, leaving whatever else is selected alone.</summary>
    /// <param name="slot">The slot. Out-of-range values are ignored.</param>
    /// <remarks>
    /// The programmatic half of shift-clicking. It exists so a multi-node selection can be made
    /// without a pointer, which is what lets the collapse gesture be exercised from a command line
    /// and therefore photographed.
    /// </remarks>
    public void SelectAlso(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        _selection.Add(slot);
        _focusNode = slot;
        _selectedWire = null;

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects exactly one slot and gives it keyboard focus.</summary>
    /// <param name="slot">The slot, or −1 to select nothing.</param>
    public void SelectOnly(int slot)
    {
        _selection.Clear();
        _selectedWire = null;

        if (slot >= 0 && slot < _graph.Nodes.Count)
        {
            _selection.Add(slot);
            _focusNode = slot;
        }
        else
        {
            _focusNode = -1;
        }

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A world position near the centre of the visible canvas, offset so repeated placements do
    /// not land on top of each other.
    /// </summary>
    /// <param name="ordinal">How many nodes have already been placed this way.</param>
    /// <param name="x">The left edge to place at.</param>
    /// <param name="y">The top edge to place at.</param>
    public void SuggestPlacement(int ordinal, out double x, out double y)
    {
        CanvasBounds visible = _transform.VisibleWorld(
            Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));

        x = visible.MinX + (visible.Width * 0.30) + ((ordinal % 6) * 28);
        y = visible.MinY + (visible.Height * 0.25) + ((ordinal % 6) * 34);
    }

    /// <summary>The number of nodes the last frame's cull found visible.</summary>
    public int LastVisibleNodeCount { get; private set; }

    /// <summary>The number of nodes the last frame's cull had to test.</summary>
    public int LastConsideredNodeCount { get; private set; }

    /// <summary>The graph being drawn.</summary>
    /// <remarks>
    /// Setting this rebuilds the spatial index on the next frame rather than immediately, so that
    /// loading a graph costs one rebuild however many times the property is touched.
    /// </remarks>
    public CanvasGraph Graph
    {
        get => _graph;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _graph = value;
            _selection.Clear();
            _selectedWire = null;
            _hoverNode = -1;
            _focusNode = -1;
            _hoverPort = null;
            _wireVisuals.Clear();
            _indexDirty = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Puts a world region in the middle of the control without changing the zoom.</summary>
    /// <param name="bounds">The region to centre on.</param>
    public void CentreOn(CanvasBounds bounds)
    {
        double zoom = _transform.Zoom;
        _transform.OffsetX = ((bounds.MinX + bounds.MaxX) / 2) - (Bounds.Width / (2 * zoom));
        _transform.OffsetY = ((bounds.MinY + bounds.MaxY) / 2) - (Bounds.Height / (2 * zoom));
        InvalidateVisual();
    }

    /// <summary>Frames the whole graph in the control, with a margin.</summary>
    /// <remarks>
    /// <para>
    /// <b>A fit asked for before the control has been laid out is remembered, not dropped.</b> It
    /// cannot be performed — the fit needs a width and a height, and there are none yet — but
    /// returning quietly makes the caller's request disappear, and the caller has no way to know.
    /// That is exactly what happened when the shell became a <c>DockControl</c>: Dock lays its
    /// content out later than the <c>Grid</c> did, so the startup fit began arriving before the
    /// first arrange, and the application opened at 100% showing a third of the graph. Nothing
    /// failed; a guard returned.
    /// </para>
    /// <para>
    /// Honoured by the <i>canvas</i> rather than re-timed by the window on purpose. Asking the
    /// shell to call this later would put the container's layout schedule into the window's head,
    /// and the next container change would break it again in the same silent way.
    /// </para>
    /// </remarks>
    public void ZoomToFit()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            _fitPending = true;

            // Where the view was when the fit was deferred. If anything moves it before the first
            // arrival of a real size, that is a more recent instruction than this one and the
            // deferred fit stands down - otherwise a fit requested at startup would silently
            // overwrite a zoom set deliberately a moment later, which is exactly what --zoom
            // found.
            _fitDeferredFrom = (_transform.Zoom, _transform.OffsetX, _transform.OffsetY);
            return;
        }

        _fitPending = false;
        _transform.FitTo(_graph.ComputeBounds(), Bounds.Width, Bounds.Height);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);

        // The first arrange that produces a real size is where a deferred fit belongs. Checked
        // against `finalSize` rather than `Bounds`, because Bounds is not updated until after
        // this returns and a fit measured against the previous size would be a frame late.
        if (_fitPending && finalSize.Width >= 1 && finalSize.Height >= 1)
        {
            _fitPending = false;

            bool untouched = _transform.Zoom == _fitDeferredFrom.Zoom
                && _transform.OffsetX == _fitDeferredFrom.OffsetX
                && _transform.OffsetY == _fitDeferredFrom.OffsetY;

            if (untouched)
            {
                _transform.FitTo(_graph.ComputeBounds(), finalSize.Width, finalSize.Height);
                InvalidateVisual();
            }
        }

        return arranged;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsMiddleButtonPressed)
        {
            _mode = InteractionMode.Panning;
            _pointerAnchor = screen;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        CanvasPort? port = HitTestPort(world);
        if (port is not null)
        {
            _mode = InteractionMode.DraggingWire;
            _dragSourcePort = port;
            _dragWireWorldEnd = world;
            _dragOutcome = WireOutcome.Refused;
            _selectedWire = null;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        int node = HitTestNode(world);
        if (node >= 0)
        {
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                || e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (additive)
            {
                if (!_selection.Add(node))
                {
                    _selection.Remove(node);
                }
            }
            else if (!_selection.Contains(node))
            {
                _selection.Clear();
                _selection.Add(node);
            }

            _selectedWire = null;
            _selectedNote = null;
            _selectedGroup = null;
            _focusNode = node;
            _mode = InteractionMode.DraggingNodes;
            _dragTotalX = 0;
            _dragTotalY = 0;
            _dragStartWorld = world;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // A wire is only reachable on empty canvas, so it is tested after nodes and ports. Testing
        // it first would make a wire crossing a node steal that node's clicks.
        if (HitTestWire(world) is { } wire)
        {
            _selection.Clear();
            _selectedNote = null;
            _selectedGroup = null;
            _selectedWire = wire;
            _mode = InteractionMode.None;
            e.Handled = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Notes are tested last of all the things that can be hit, which is the same ordering as
        // their draw order: a note is behind everything, so everything on top of it wins its
        // clicks. Selecting one clears the node selection, because the two cannot be dragged or
        // deleted together and a selection that spans both would have to answer what Delete means.
        if (HitTestNote(world) is { } note)
        {
            _selection.Clear();
            _selectedGroup = null;
            _selectedNote = note;
            _mode = InteractionMode.DraggingNote;
            _dragTotalX = 0;
            _dragTotalY = 0;
            _dragStartWorld = world;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // A group is behind even the notes, and it is grabbed by its title strip rather than by
        // its whole rectangle. Its rectangle is mostly the gap between its own nodes, and a group
        // that swallowed every click in that gap would make marquee-selecting inside one
        // impossible - which is the gesture a user reaches for most often once nodes are grouped.
        if (HitTestGroupTitle(world) is { } group)
        {
            _selection.Clear();
            _selectedWire = null;
            _selectedNote = null;
            _selectedGroup = group;
            _mode = InteractionMode.DraggingGroup;
            _dragTotalX = 0;
            _dragTotalY = 0;
            _dragStartWorld = world;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _selectedWire = null;
        _selectedNote = null;
        _selectedGroup = null;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _selection.Clear();
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);

        _mode = InteractionMode.Marquee;
        _marqueeStartWorld = world;
        _marqueeEndWorld = world;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);

        switch (_mode)
        {
            case InteractionMode.Panning:
                _transform.PanByScreen(screen.X - _pointerAnchor.X, screen.Y - _pointerAnchor.Y);
                _pointerAnchor = screen;
                InvalidateVisual();
                return;

            case InteractionMode.DraggingNodes:
                MoveSelection(world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                _dragStartWorld = world;
                InvalidateVisual();
                return;

            case InteractionMode.DraggingNote when _selectedNote is { } dragged:
                MoveNote(dragged, world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                _dragStartWorld = world;
                InvalidateVisual();
                return;

            case InteractionMode.DraggingGroup when _selectedGroup is { } group:
                MoveGroup(group, world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                _dragStartWorld = world;
                InvalidateVisual();
                return;

            case InteractionMode.Marquee:
                _marqueeEndWorld = world;
                InvalidateVisual();
                return;

            case InteractionMode.DraggingWire:
                _dragWireWorldEnd = world;
                _hoverPort = HitTestPort(world);
                _dragOutcome = EvaluateDrag(_dragSourcePort, _hoverPort);
                InvalidateVisual();
                return;

            default:
                break;
        }

        int node = HitTestNode(world);
        CanvasPort? port = HitTestPort(world);
        CanvasNote? note = node >= 0 ? null : HitTestNote(world);

        if (node != _hoverNode || !NullablePortEquals(port, _hoverPort) || !ReferenceEquals(note, _hoverNote))
        {
            _hoverNode = node;
            _hoverPort = port;
            _hoverNote = note;
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        switch (_mode)
        {
            case InteractionMode.Marquee:
                CommitMarquee();
                break;

            case InteractionMode.DraggingWire when _dragSourcePort is { } source && _hoverPort is { } target:
                TryConnect(source, target);
                break;

            // A move is reported as an edit but not as a reason to run: a position is not in a
            // node's provenance, so nothing it feeds can produce a different answer.
            case InteractionMode.DraggingNodes when _dragTotalX != 0 || _dragTotalY != 0:
                _dragTotalX = 0;
                _dragTotalY = 0;
                GraphChanged?.Invoke(
                    this, new GraphEditedEventArgs(Plural("Move", _selection.Count), affectsEvaluation: false));
                break;

            // Net displacement again, for the reason the node drag learned it: a note dragged out
            // and back records nothing, because an undo step that moves nothing reads as broken.
            case InteractionMode.DraggingNote when _dragTotalX != 0 || _dragTotalY != 0:
                _dragTotalX = 0;
                _dragTotalY = 0;
                GraphChanged?.Invoke(
                    this, new GraphEditedEventArgs("Move note", affectsEvaluation: false));
                break;

            case InteractionMode.DraggingGroup when _dragTotalX != 0 || _dragTotalY != 0:
                _dragTotalX = 0;
                _dragTotalY = 0;
                GraphChanged?.Invoke(
                    this, new GraphEditedEventArgs("Move group", affectsEvaluation: false));
                break;

            default:
                break;
        }

        _mode = InteractionMode.None;
        _dragSourcePort = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Double-clicking empty canvas asks the shell to create a node there. On a node, a port or a
    /// wire it does nothing yet — those are the in-place editors the hybrid overlay will carry
    /// (`E8-T5`), and doing something arbitrary in the meantime would teach a gesture that has to
    /// be untaught.
    /// </remarks>
    protected override void OnDoubleTapped(Avalonia.Input.TappedEventArgs e)
    {
        base.OnDoubleTapped(e);

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);

        if (HitTestPort(world) is not null || HitTestNode(world) >= 0 || HitTestWire(world) is not null)
        {
            return;
        }

        // Nothing is stood down here, and that is worth saying rather than leaving to be
        // rediscovered. The first click of the double starts a marquee and the second ends it:
        // OnPointerReleased runs for both, and it clears the mode and the capture unconditionally.
        // A defensive reset here looked prudent and was unreachable — no input sequence could
        // arrive with a mode still set — so it went, along with the test that could not fail
        // ([N27](../../../docs/NOTES.md)).
        e.Handled = true;
        CreateRequested?.Invoke(this, new CanvasCreateRequestedEventArgs(world.X, world.Y, screen.X, screen.Y));
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Point screen = e.GetPosition(this);
        double factor = Math.Pow(1.15, e.Delta.Y);
        _transform.ZoomAbout(factor, screen.X, screen.Y);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_mode is InteractionMode.None && (_hoverNode >= 0 || _hoverPort is not null))
        {
            _hoverNode = -1;
            _hoverPort = null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Home:
                ZoomToFit();
                e.Handled = true;
                break;

            case Key.Escape:
                _selection.Clear();
                _selectedWire = null;
                InvalidateVisual();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Delete:
            case Key.Back:
                e.Handled = DeleteSelection();
                break;

            default:
                break;
        }
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        long started = Stopwatch.GetTimestamp();

        Rect bounds = new(Bounds.Size);
        if (Background is not null)
        {
            context.FillRectangle(Background, bounds);
        }

        if (bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        EnsureIndex();

        double zoom = _transform.Zoom;
        CanvasDetail detail = CanvasLevelOfDetail.For(zoom);
        CanvasBounds visible = _transform.VisibleWorld(bounds.Width, bounds.Height);

        _index.Query(visible.MinX, visible.MinY, visible.MaxX, visible.MaxY);
        LastVisibleNodeCount = _index.VisibleCount;
        LastConsideredNodeCount = _index.ConsideredCount;

        FramePens pens = FramePens.ForZoom(zoom);

        using (context.PushTransform(
            Matrix.CreateScale(zoom, zoom) *
            Matrix.CreateTranslation(-_transform.OffsetX * zoom, -_transform.OffsetY * zoom)))
        {
            // Notes first, and therefore behind. A note is a background that a region of the
            // graph sits on — a label for it — so a note drawn over its own nodes would be
            // annotating them by hiding them.
            DrawGroups(context, pens, detail);
            DrawNotes(context, pens, detail);
            DrawWires(context, pens, visible, detail);
            DrawNodes(context, pens, detail, zoom);
            DrawPreviews(context, pens, detail);
            DrawDragWire(context, pens);
            DrawMarquee(context, pens);
        }

        if (ShowFrameStatistics)
        {
            DrawFrameStatistics(context, bounds);
        }

        Frames.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private void EnsureIndex()
    {
        if (!_indexDirty && !_index.NeedsRebuild)
        {
            return;
        }

        List<CanvasBounds> bounds = new(_graph.Nodes.Count);
        foreach (CanvasNode node in _graph.Nodes)
        {
            bounds.Add(node.Bounds);
        }

        _index.Rebuild(bounds);
        _indexDirty = false;
    }

    /// <summary>
    /// Draws the groups, behind everything including the notes.
    /// </summary>
    /// <remarks>
    /// The frame is derived from the members every frame rather than stored, so a group cannot
    /// drift from what it contains — drag a member and the frame follows on the next paint with
    /// nothing to keep in step. A group whose members have all been deleted measures to nothing
    /// and is skipped, which is the same answer as not drawing a frame around nothing.
    /// </remarks>
    private void DrawGroups(DrawingContext context, in FramePens pens, CanvasDetail detail)
    {
        if (_graph.Groups.Count == 0)
        {
            return;
        }

        bool drawsTitle = CanvasLevelOfDetail.DrawsTitle(detail);

        foreach (CanvasGroup group in _graph.Groups)
        {
            if (_graph.GroupBounds(group) is not { } bounds)
            {
                continue;
            }

            Rect rect = new(bounds.MinX, bounds.MinY, bounds.Width, bounds.Height);
            RoundedRect rounded = new(rect, CornerRadius);

            context.DrawRectangle(SparkPalette.CanvasGroupBrush, null, rounded);
            context.DrawRectangle(
                null,
                ReferenceEquals(group, _selectedGroup) ? pens.SelectionRing : pens.NodeOutline,
                rounded);

            if (!drawsTitle || group.Title.Length == 0)
            {
                continue;
            }

            // In the title strip, which is also the only part of the frame that takes a click.
            // The rest of a group's rectangle is the gap between its own nodes, and swallowing
            // clicks there would make marquee-selecting inside a group impossible.
            FormattedText title = new(
                group.Title,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                NoteFontSize,
                SparkPalette.TextSecondaryBrush);

            Rect strip = new(rect.X, rect.Y, rect.Width, CanvasGroup.TitleHeight);
            using (context.PushClip(strip))
            {
                context.DrawText(
                    title,
                    new Point(rect.X + 8, rect.Y + ((CanvasGroup.TitleHeight - title.Height) / 2)));
            }
        }
    }

    /// <summary>
    /// Draws the notes, behind everything else on the canvas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A note is painted on <c>canvas.group</c>, the design language's surface for canvas
    /// annotation. It is reused rather than duplicated because a note and a group are the same
    /// kind of thing to a reader — a labelled region rather than a participant — and because its
    /// text contrast is already verified at 14.58:1 by <c>PaletteContrastTests</c>. A new colour
    /// would be a new row in that table for no gain.
    /// </para>
    /// <para>
    /// Below the title threshold the text is dropped and the rectangle is kept. That is the same
    /// rule the nodes follow, and it is the right one: zoomed out, a note's job is to show that a
    /// region is annotated at all, and unreadable text costs layout time to communicate nothing.
    /// </para>
    /// </remarks>
    private void DrawNotes(DrawingContext context, in FramePens pens, CanvasDetail detail)
    {
        if (_graph.Notes.Count == 0)
        {
            return;
        }

        bool drawsText = CanvasLevelOfDetail.DrawsTitle(detail);

        foreach (CanvasNote note in _graph.Notes)
        {
            Rect rect = new(note.X, note.Y, note.Width, note.Height);
            RoundedRect rounded = new(rect, CornerRadius);

            context.DrawRectangle(
                ReferenceEquals(note, _hoverNote)
                    ? SparkPalette.SurfaceBaseBrush
                    : SparkPalette.CanvasGroupBrush,
                null,
                rounded);

            // The selection ring is the same one a node gets. A user who has learned what the
            // accent outline means should not have to learn it twice.
            context.DrawRectangle(
                null,
                ReferenceEquals(note, _selectedNote) ? pens.SelectionRing : pens.NodeOutline,
                rounded);

            if (!drawsText || note.Text.Length == 0)
            {
                continue;
            }

            // Not cached by string, unlike node titles and port labels. Those repeat across
            // thousands of nodes drawn from a library of a couple of hundred names; a note's text
            // is unique to it and is being edited, so caching it would fill the cache with entries
            // that are each used once and are stale a keystroke later.
            FormattedText run = new(
                note.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                NoteFontSize,
                SparkPalette.TextPrimaryBrush)
            {
                MaxTextWidth = Math.Max(1, note.Width - (2 * NotePadding)),
                MaxTextHeight = Math.Max(1, note.Height - (2 * NotePadding)),
            };

            using (context.PushClip(rect))
            {
                context.DrawText(run, new Point(note.X + NotePadding, note.Y + NotePadding));
            }
        }
    }

    private void DrawNodes(DrawingContext context, in FramePens pens, CanvasDetail detail, double zoom)
    {
        bool drawsTitle = CanvasLevelOfDetail.DrawsTitle(detail);
        bool drawsPortLabels = CanvasLevelOfDetail.DrawsPortLabels(detail);
        bool drawsOutline = CanvasLevelOfDetail.DrawsOutline(detail);
        bool drawsShadow = CanvasLevelOfDetail.DrawsShadow(detail);
        bool drawsLip = CanvasLevelOfDetail.DrawsLip(detail);
        double categoryBlend = CanvasLevelOfDetail.CategoryFillBlend(zoom);

        foreach (int slot in _index.Visible)
        {
            CanvasNode node = _graph.Nodes[slot];
            bool selected = _selection.Contains(slot);
            bool hovered = slot == _hoverNode;

            bool notEvaluated = node.State.HasFlag(CanvasNodeState.NotEvaluated);

            Rect nodeRect = new(node.X, node.Y, node.Width, node.Height);
            RoundedRect rounded = new(nodeRect, CornerRadius);
            Color categoryColour = hovered
                ? NodeCategoryColours.HoverColourOf(node.Category)
                : NodeCategoryColours.ColourOf(node.Category);

            if (notEvaluated)
            {
                // §7.7. The desaturation is luminance-preserving, so header text contrast is
                // unchanged to within a hundredth: the state is carried by the loss of HUE, which
                // costs no contrast at all, plus a dashed outline and a ○ glyph. A user must still
                // be able to read a node that did not run, because reading it is how they work out
                // what should have run.
                categoryColour = SparkPalette.Desaturate(categoryColour);
            }

            if (detail == CanvasDetail.Silhouette)
            {
                // Below 40% the category fill is the only thing carrying identity, and it clears
                // 3:1 against the canvas on its own — 5.39:1 at worst, for cat.script.
                context.DrawRectangle(new ImmutableSolidColorBrush(categoryColour), null, rounded);
                DrawStateRings(context, pens, node, nodeRect, selected);
                continue;
            }

            // Depth. The shadow goes first because it is drawn behind the body, and the highlight
            // half is dropped below 100% where its 6 px blur falls under five device pixels.
            if (drawsShadow)
            {
                BoxShadow shadow = new()
                {
                    OffsetX = hovered ? 4 : 3,
                    OffsetY = hovered ? 6 : 4,
                    Blur = hovered ? 16 : 12,
                    Color = Color.FromArgb(0xBF, 0x0C, 0x0E, 0x13),
                };

                context.DrawRectangle(Brushes.Transparent, null, rounded, new BoxShadows(shadow));
            }

            // Body. Hover and selection step the fill DOWN the ladder, never up: the text on it is
            // light, so darkening is the direction that raises contrast (§5.1).
            Color bodyColour = notEvaluated
                ? SparkPalette.NodeBodySelected
                : selected
                    ? SparkPalette.NodeBodySelected
                    : hovered
                        ? SparkPalette.NodeBodyHover
                        : SparkPalette.NodeBody;

            if (categoryBlend > 0)
            {
                // Between 67% and 40% the body lerps towards the category colour so the
                // level-of-detail transition is a fade rather than a jump. Body text has already
                // been dropped by this point, which is what makes brightening the fill legal.
                bodyColour = SparkPalette.Mix(bodyColour, categoryColour, categoryBlend);
            }

            context.DrawRectangle(new ImmutableSolidColorBrush(bodyColour), null, rounded);

            // Header: full-strength category colour with dark text (Decision V2). Clipped rather
            // than drawn as a separately-rounded rectangle so the top corners match the body's
            // radius exactly.
            Rect headerRect = new(node.X, node.Y, node.Width, CanvasNode.HeaderHeight);
            using (context.PushClip(headerRect))
            {
                context.DrawRectangle(new ImmutableSolidColorBrush(categoryColour), null, rounded);
            }

            if (drawsLip)
            {
                // The lit side of the depth pair is spent on a 1 px lip along the top and left
                // edges rather than a wide highlight blur, because a broad light-on-dark highlight
                // reads as a glow — and a glow is the vocabulary reserved for focus (Decision V6).
                IPen lipPen = hovered ? pens.LipHover : pens.LipRest;
                double inset = 0.5 / _transform.Zoom;
                context.DrawLine(
                    lipPen,
                    new Point(node.X + CornerRadius, node.Y + inset),
                    new Point(node.X + node.Width - CornerRadius, node.Y + inset));
                context.DrawLine(
                    lipPen,
                    new Point(node.X + inset, node.Y + CornerRadius),
                    new Point(node.X + inset, node.Y + node.Height - CornerRadius));
            }

            // Decision V5: every node carries a 1 px border.control outline, because node.body
            // sits at 1.21:1 against the canvas and a node's extent is what you aim at.
            if (drawsOutline)
            {
                context.DrawRectangle(null, notEvaluated ? pens.NodeOutlineDashed : pens.NodeOutline, rounded);
            }

            DrawPorts(context, pens, node, slot, detail);

            if (drawsTitle)
            {
                FormattedText title = HeaderRun(node.Title);
                using (context.PushClip(headerRect))
                {
                    context.DrawText(
                        title,
                        new Point(node.X + 8, node.Y + ((CanvasNode.HeaderHeight - title.Height) / 2)));

                    // The state glyph is right-aligned in the header (§7.4) and is what makes the
                    // state survive colour blindness and a monochrome screenshot. Colour is never
                    // the only carrier of meaning.
                    if (StateGlyph(node.State) is { } glyph)
                    {
                        FormattedText run = GlyphRun(glyph);
                        context.DrawText(
                            run,
                            new Point(
                                node.X + node.Width - 8 - run.Width,
                                node.Y + ((CanvasNode.HeaderHeight - run.Height) / 2)));
                    }
                }
            }

            if (drawsPortLabels)
            {
                DrawPortLabels(context, node, CanvasLevelOfDetail.DrawsPortTypes(detail));
            }

            DrawStateRings(context, pens, node, nodeRect, selected);

            if (slot == _focusNode && IsFocused)
            {
                DrawFocusSandwich(context, pens, nodeRect);
            }
        }
    }

    private void DrawPorts(DrawingContext context, in FramePens pens, CanvasNode node, int slot, CanvasDetail detail)
    {
        // Below 67% ports are 2 px screen-space dots; above it they are drawn at their design size
        // and grow on hover. The hit target never shrinks below 10 px of screen space regardless
        // (§7.6) — ports are the smallest thing anyone has to aim at in the product.
        double zoom = _transform.Zoom;
        double radius = detail <= CanvasDetail.Fill ? 1.0 / zoom : PortRadius;

        bool shaped = detail >= CanvasDetail.Lip;

        for (int i = 0; i < node.Inputs.Count; i++)
        {
            node.InputPortCentre(i, out double x, out double y);
            CanvasPort port = new(slot, i, IsOutput: false);
            DrawPort(context, pens, port, x, y, radius, shaped ? node.Inputs[i].DeclaredRank : 0);
        }

        for (int i = 0; i < node.Outputs.Count; i++)
        {
            node.OutputPortCentre(i, out double x, out double y);
            CanvasPort port = new(slot, i, IsOutput: true);
            DrawPort(context, pens, port, x, y, radius, shaped ? node.Outputs[i].DeclaredRank : 0);
        }
    }

    private void DrawPort(
        DrawingContext context, in FramePens pens, CanvasPort port, double x, double y, double radius, int declaredRank)
    {
        bool hovered = _hoverPort == port;
        bool connected = _connectedPorts.Contains(port);
        double drawn = hovered ? Math.Max(radius, PortHoverRadius) : radius;

        IBrush fill = connected ? SparkPalette.PortConnectedBrush : SparkPalette.PortRestBrush;
        context.DrawEllipse(fill, null, new Point(x, y), drawn, drawn);

        // §7.6: port geometry encodes declared rank, so a user can see why a node replicated
        // without opening anything. A rank-1 output feeding a rank-0 input is a lacing waiting to
        // happen, and this is the only place on the canvas that says so before the run.
        if (declaredRank >= 1)
        {
            double ring = drawn + (1.5 / _transform.Zoom);
            context.DrawEllipse(null, pens.PortRankRing, new Point(x, y), ring, ring);
        }

        if (hovered)
        {
            context.DrawEllipse(null, pens.AccentThin, new Point(x, y), drawn + (2 / _transform.Zoom), drawn + (2 / _transform.Zoom));
        }
    }

    /// <summary>
    /// Draws the port names, and beside each one the type it wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type is what turns a port from a word into an instruction: <c>centre</c> says where the
    /// value goes and <c>Point</c> says what to go and find. It is drawn in <c>text.muted</c> at
    /// 10 px so the name still wins the row, and it is dropped a level of detail earlier than the
    /// name for the reason every threshold in §7.3 exists — 10 px below 82% zoom is under eight
    /// device pixels, and the design language drops text there rather than clamping it.
    /// </para>
    /// <para>
    /// Nothing here says whether the port wants a list. The ring around the port disc already does
    /// (§7.6), and saying it twice would cost width on every node in the graph.
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="node">The node being drawn.</param>
    /// <param name="types">Whether the zoom is high enough for the type labels.</param>
    private void DrawPortLabels(DrawingContext context, CanvasNode node, bool types)
    {
        int rows = Math.Max(node.Inputs.Count, node.Outputs.Count);

        for (int row = 0; row < rows; row++)
        {
            double y = node.Y + CanvasNode.HeaderHeight + (CanvasNode.PortPitch * (row + 0.5));
            double leftEnd = node.X + PortLabelInset;
            double rightStart = node.X + node.Width - PortLabelInset;

            if (row < node.Inputs.Count)
            {
                FormattedText name = LabelRun(node.Inputs[row].Name);
                context.DrawText(name, new Point(leftEnd, y - (name.Height / 2)));
                leftEnd += name.Width;
            }

            if (row < node.Outputs.Count)
            {
                FormattedText name = LabelRun(node.Outputs[row].Name);
                rightStart -= name.Width;
                context.DrawText(name, new Point(rightStart, y - (name.Height / 2)));
            }

            if (!types)
            {
                continue;
            }

            // The two type labels compete for the space between the two names, and the node was
            // sized from an estimate rather than from measured text (N24). So each one is drawn
            // only if it fits with a gap to spare — which makes an overlap impossible whatever the
            // font turns out to measure, rather than merely unlikely.
            double free = rightStart - leftEnd;

            if (row < node.Inputs.Count && node.Inputs[row].TypeName is { } inputType)
            {
                FormattedText run = TypeRun(inputType);
                if (free >= TypeGap + run.Width + MinimumRowGap)
                {
                    context.DrawText(run, new Point(leftEnd + TypeGap, y - (run.Height / 2)));
                    free -= TypeGap + run.Width;
                }
            }

            // An output name is right-aligned, so its type goes to its left. The input's type wins
            // a contested row: the question a port label answers is what to plug in.
            if (row < node.Outputs.Count && node.Outputs[row].TypeName is { } outputType)
            {
                FormattedText run = TypeRun(outputType);
                if (free >= TypeGap + run.Width + MinimumRowGap)
                {
                    context.DrawText(
                        run, new Point(rightStart - TypeGap - run.Width, y - (run.Height / 2)));
                }
            }
        }
    }

    /// <summary>
    /// Draws a preview bubble under the hovered node and under every selected node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Rank first, on its own line.</b> `E8-T10` asks for a node's output <i>and its rank</i>,
    /// and says why: rank is what users get wrong. A node that quietly produced a list of lists
    /// where a list was expected is the commonest way a graph goes wrong without ever erroring, and
    /// it is invisible in the value — <c>[[1], [2]]</c> and <c>[1, 2]</c> look alike at a glance and
    /// are not alike at all. So rank gets a line rather than a clause.
    /// </para>
    /// <para>
    /// <b>Only the hovered and selected nodes get one, and that is a budget decision as much as a
    /// design one.</b> Laying out text for two thousand nodes would spend `E8-T15`'s whole 16.7 ms
    /// frame on strings nobody is reading. It is also the better design: a bubble answers <i>what
    /// is this one doing</i>, which is a question about the node under the pointer, and a permanent
    /// readout is what a <c>Watch</c> node is for.
    /// </para>
    /// </remarks>
    private void DrawPreviews(DrawingContext context, in FramePens pens, CanvasDetail detail)
    {
        // Below the title threshold the text would be unreadable, and a bubble with unreadable
        // text in it is a smudge that hides the graph behind it.
        if (!CanvasLevelOfDetail.DrawsTitle(detail))
        {
            return;
        }


        // The selection first, because a selected node may be off screen after a pan and still
        // deserves its bubble, and the cull would have dropped it.
        _previewsDrawn.Clear();

        foreach (int slot in _selection)
        {
            if (slot >= 0 && slot < _graph.Nodes.Count && _previewsDrawn.Add(slot))
            {
                DrawPreview(context, pens, _graph.Nodes[slot]);
            }
        }

        // Then whatever the cull kept, so an off-screen watch costs nothing. ShowsPreview owns
        // the rule; this loop owns the pixels, which is what makes the rule testable without a
        // frame.
        foreach (int slot in _index.Visible)
        {
            if (ShowsPreview(slot) && _previewsDrawn.Add(slot))
            {
                DrawPreview(context, pens, _graph.Nodes[slot]);
            }
        }
    }

    /// <summary>
    /// Whether a node's value is on show: it is a watch, or it is selected, or the pointer is over
    /// it.
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <returns>True when a preview bubble belongs under it.</returns>
    /// <remarks>
    /// The rule, separated from the drawing, because the rule is the part with a decision in it
    /// and the drawing is the part that needs a frame. A <b>watch</b> is permanent — that is what
    /// distinguishes it from a bubble, which answers <i>what is this one doing</i> about whatever
    /// is under the pointer right now.
    /// </remarks>
    public bool ShowsPreview(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return false;
        }

        return _graph.Nodes[slot].ShowsValue || _selection.Contains(slot) || slot == _hoverNode;
    }

    private static void DrawPreview(DrawingContext context, in FramePens pens, CanvasNode node)
    {
        if (node.ResultSummary is not { } summary || summary.Length == 0)
        {
            return;
        }

        FormattedText rank = new(
            CanvasGraph.RankLine(node),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            TypeFontSize,
            SparkPalette.TextMutedBrush);

        FormattedText value = new(
            summary,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            PortFontSize,
            SparkPalette.TextPrimaryBrush)
        {
            MaxTextWidth = PreviewMaximumWidth,
        };

        double width = Math.Max(rank.Width, value.Width) + (2 * PreviewPadding);
        double height = rank.Height + value.Height + (2 * PreviewPadding) + PreviewLineGap;

        // Under the node and left-aligned with it, so a column of nodes produces a column of
        // bubbles rather than a staircase.
        Rect box = new(node.X, node.Y + node.Height + PreviewGap, width, height);
        RoundedRect rounded = new(box, CornerRadius);

        context.DrawRectangle(SparkPalette.SurfaceFloatBrush, null, rounded);
        context.DrawRectangle(null, pens.NodeOutline, rounded);

        using (context.PushClip(box))
        {
            context.DrawText(rank, new Point(box.X + PreviewPadding, box.Y + PreviewPadding));
            context.DrawText(
                value,
                new Point(box.X + PreviewPadding, box.Y + PreviewPadding + rank.Height + PreviewLineGap));
        }
    }

    private void DrawStateRings(DrawingContext context, in FramePens pens, CanvasNode node, Rect nodeRect, bool selected)
    {
        // State strokes are drawn at screen width and never scale, which is what makes an error
        // findable in a zoomed-out graph. Error and warning rings go around the node's outer edge
        // against the canvas, never on the header: an amber ring on a gold cat.input header would
        // be invisible, while against canvas.bg it reads 8.41:1.
        double zoom = _transform.Zoom;

        if (selected)
        {
            RoundedRect ring = new(nodeRect.Inflate(1.5 / zoom), CornerRadius + (1.5 / zoom));
            context.DrawRectangle(null, pens.SelectionRing, ring);
        }

        if (node.State.HasFlag(CanvasNodeState.Error))
        {
            RoundedRect ring = new(nodeRect.Inflate(4 / zoom), CornerRadius + (4 / zoom));
            context.DrawRectangle(null, pens.ErrorRing, ring);
        }
        else if (node.State.HasFlag(CanvasNodeState.Warning))
        {
            RoundedRect ring = new(nodeRect.Inflate(4 / zoom), CornerRadius + (4 / zoom));
            context.DrawRectangle(null, pens.WarningRing, ring);
        }

        if (node.State.HasFlag(CanvasNodeState.Anchor))
        {
            DrawAnchorTicks(context, pens, nodeRect);
        }
    }

    private void DrawAnchorTicks(DrawingContext context, in FramePens pens, Rect rect)
    {
        // Corner ticks rather than a brighter ring, because a shape difference survives monochrome
        // rendering, colour blindness and a bad monitor, and a brightness difference does not.
        double length = 6 / _transform.Zoom;
        (Point Corner, double DirectionX, double DirectionY)[] corners =
        [
            (rect.TopLeft, 1, 1),
            (new Point(rect.Right, rect.Top), -1, 1),
            (new Point(rect.Right, rect.Bottom), -1, -1),
            (new Point(rect.Left, rect.Bottom), 1, -1),
        ];

        foreach ((Point corner, double dx, double dy) in corners)
        {
            context.DrawLine(pens.SelectionRing, corner, new Point(corner.X + (length * dx), corner.Y));
            context.DrawLine(pens.SelectionRing, corner, new Point(corner.X, corner.Y + (length * dy)));
        }
    }

    private void DrawFocusSandwich(DrawingContext context, in FramePens pens, Rect nodeRect)
    {
        // Dark, light, dark — 4 px total, drawn outside the control's bounds with a 2 px gap. Never
        // a glow, never an elevation change, and never suppressed by hover (Decision V7). The two
        // dark separators exist so the ring's 3:1 requirement holds against whatever it lands on.
        double zoom = _transform.Zoom;
        Rect inner = nodeRect.Inflate(2.5 / zoom);
        Rect middle = nodeRect.Inflate(4 / zoom);
        Rect outer = nodeRect.Inflate(5.5 / zoom);

        context.DrawRectangle(null, pens.FocusContour, new RoundedRect(inner, CornerRadius + (2.5 / zoom)));
        context.DrawRectangle(null, pens.FocusRing, new RoundedRect(middle, CornerRadius + (4 / zoom)));
        context.DrawRectangle(null, pens.FocusContour, new RoundedRect(outer, CornerRadius + (5.5 / zoom)));
    }

    private void DrawWires(DrawingContext context, in FramePens pens, CanvasBounds visible, CanvasDetail detail)
    {
        EnsureWireVisuals();

        for (int i = 0; i < _wireVisuals.Count; i++)
        {
            WireVisual visual = _wireVisuals[i];
            if (!visual.Bounds.Intersects(visible))
            {
                continue;
            }

            // Casing then core. Exactly one of the two always clears 3:1 against whatever is behind
            // the wire — the core against the canvas and node bodies, the casing against every
            // bright node header — and the casing is retained at every zoom including LOD, because
            // at LOD every node is a bright rectangle (Decision V9).
            context.DrawGeometry(null, pens.WireCasing, visual.Geometry);

            IPen core = _selectedWire == visual.Wire
                ? pens.WireSelected
                : detail == CanvasDetail.Silhouette ? pens.WireCoreThin : pens.WireCore;

            context.DrawGeometry(null, core, visual.Geometry);
        }
    }

    private void DrawDragWire(DrawingContext context, in FramePens pens)
    {
        if (_mode is not InteractionMode.DraggingWire || _dragSourcePort is not { } source)
        {
            return;
        }

        PortCentre(source, out double x, out double y);
        StreamGeometry geometry = BuildWireGeometry(x, y, _dragWireWorldEnd.X, _dragWireWorldEnd.Y);

        IPen core = _dragOutcome switch
        {
            WireOutcome.Accepted => pens.WireSuccess,
            WireOutcome.Lossy => pens.WireWarning,
            _ => pens.WireError,
        };

        context.DrawGeometry(null, pens.WireCasing, geometry);
        context.DrawGeometry(null, core, geometry);

        // The cursor glyph is what carries the outcome for a user who cannot separate the three
        // hues, and it is why the colour reuse in Decision V1 is safe.
        string glyph = _dragOutcome switch
        {
            WireOutcome.Accepted => "✓",
            WireOutcome.Lossy => "≈",
            _ => "✕",
        };

        FormattedText run = new(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            13,
            core.Brush);

        context.DrawText(run, new Point(_dragWireWorldEnd.X + (10 / _transform.Zoom), _dragWireWorldEnd.Y));
    }

    private void DrawMarquee(DrawingContext context, in FramePens pens)
    {
        if (_mode is not InteractionMode.Marquee)
        {
            return;
        }

        Rect rect = new(_marqueeStartWorld, _marqueeEndWorld);

        // An accent tint is permitted here and almost nowhere else, because a marquee lands only on
        // empty canvas and there is no text over it to lose contrast against (§5.4).
        context.DrawRectangle(SparkPalette.MarqueeFillBrush, pens.AccentThin, rect);
    }

    private void DrawFrameStatistics(DrawingContext context, Rect bounds)
    {
        string text = string.Create(
            CultureInfo.InvariantCulture,
            $"{Frames.Summary()}   {LastVisibleNodeCount}/{_graph.Nodes.Count} nodes drawn, " +
            $"{LastConsideredNodeCount} considered   zoom {_transform.Zoom * 100:F0}%");

        FormattedText run = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            11,
            SparkPalette.TextMutedBrush);

        // On an E3 floating surface with its own fill, never as bare text over the canvas: an
        // overlay is UI and is fully inside the contrast rules (§8.5).
        Rect plate = new(8, bounds.Height - run.Height - 16, run.Width + 16, run.Height + 8);
        context.DrawRectangle(
            SparkPalette.Frozen(SparkPalette.SurfaceFloat),
            new ImmutablePen(new ImmutableSolidColorBrush(SparkPalette.BorderHairline), 1),
            new RoundedRect(plate, 4));

        context.DrawText(run, new Point(plate.X + 8, plate.Y + 4));
    }

    private void EnsureWireVisuals()
    {
        IReadOnlyList<CanvasWire> wires = _graph.Wires;

        // Whether a port is connected decides its fill, and it is asked once per port per frame.
        // Answering it by walking the wire list would be quadratic in graph size, which is
        // invisible on a demo graph and fatal on a real one.
        _connectedPorts.Clear();
        foreach (CanvasWire wire in wires)
        {
            _connectedPorts.Add(wire.From);
            _connectedPorts.Add(wire.To);
        }

        while (_wireVisuals.Count > wires.Count)
        {
            _wireVisuals.RemoveAt(_wireVisuals.Count - 1);
        }

        for (int i = 0; i < wires.Count; i++)
        {
            CanvasWire wire = wires[i];
            PortCentre(wire.From, out double x0, out double y0);
            PortCentre(wire.To, out double x1, out double y1);

            if (i < _wireVisuals.Count && _wireVisuals[i].Matches(wire, x0, y0, x1, y1))
            {
                continue;
            }

            // Bézier geometry is cached and invalidated only when an endpoint actually moves.
            // Rebuilding every wire every frame is the single most expensive thing this control
            // could do, and it is invisible in a profile until the graph gets large.
            WireVisual visual = new(wire, x0, y0, x1, y1, BuildWireGeometry(x0, y0, x1, y1));

            if (i < _wireVisuals.Count)
            {
                _wireVisuals[i] = visual;
            }
            else
            {
                _wireVisuals.Add(visual);
            }
        }
    }

    private static StreamGeometry BuildWireGeometry(double x0, double y0, double x1, double y1)
    {
        double reach = Math.Max(40, Math.Abs(x1 - x0) * 0.5);
        StreamGeometry geometry = new();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x0, y0), isFilled: false);
            ctx.CubicBezierTo(
                new Point(x0 + reach, y0),
                new Point(x1 - reach, y1),
                new Point(x1, y1));
            ctx.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private void MoveSelection(double dx, double dy)
    {
        // The net displacement, not whether the pointer moved. A drag that goes out and comes back
        // to where it started leaves every node where it was, and recording it would put a step on
        // the undo stack whose undo moves nothing — which reads as undo being broken.
        _dragTotalX += dx;
        _dragTotalY += dy;

        foreach (int slot in _selection)
        {
            if (slot < 0 || slot >= _graph.Nodes.Count)
            {
                continue;
            }

            CanvasNode node = _graph.Nodes[slot];
            node.X += dx;
            node.Y += dy;
            _index.Update(slot, node.Bounds);
        }
    }

    private void CommitMarquee()
    {
        EnsureIndex();

        CanvasBounds rect = new(
            Math.Min(_marqueeStartWorld.X, _marqueeEndWorld.X),
            Math.Min(_marqueeStartWorld.Y, _marqueeEndWorld.Y),
            Math.Max(_marqueeStartWorld.X, _marqueeEndWorld.X),
            Math.Max(_marqueeStartWorld.Y, _marqueeEndWorld.Y));

        _index.Query(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY);
        foreach (int slot in _index.Visible)
        {
            _selection.Add(slot);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryConnect(CanvasPort source, CanvasPort target)
    {
        if (!_graph.TryConnect(source, target))
        {
            return;
        }

        _wireVisuals.Clear();
        _selectedWire = null;
        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Connect", affectsEvaluation: true));
    }

    /// <summary>An edit label naming how many nodes it touched: <c>Move node</c>, <c>Move 3 nodes</c>.</summary>
    /// <param name="verb">The verb the label starts with.</param>
    /// <param name="count">How many nodes the edit touched.</param>
    /// <returns>The label.</returns>
    private static string Plural(string verb, int count) => count == 1
        ? verb + " node"
        : string.Create(CultureInfo.InvariantCulture, $"{verb} {count} nodes");

    private WireOutcome EvaluateDrag(CanvasPort? source, CanvasPort? target)
    {
        // The answer is the engine's own type check, reached through the canvas graph — never a
        // guess made here. That is what makes the amber "accepted with a lossy conversion" stroke
        // mean something rather than being a colour the canvas can draw but never shows.
        if (source is not { } from || target is not { } to)
        {
            return WireOutcome.Refused;
        }

        return _graph.Preview(from, to);
    }

    /// <summary>
    /// Puts a new, empty note on the canvas and selects it.
    /// </summary>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <returns>The note, so the caller can put the caret in it.</returns>
    /// <remarks>
    /// Created empty rather than with placeholder text. Placeholder text has to be deleted before
    /// the note can be written, and a user who forgets is left with a note that says
    /// <i>New note</i> in the middle of their graph.
    /// </remarks>
    public CanvasNote AddNote(double x, double y)
    {
        CanvasNote note = _graph.AddNote(x, y);

        _selection.Clear();
        _selectedWire = null;
        _selectedNote = note;

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Add note", affectsEvaluation: false));
        return note;
    }

    /// <summary>
    /// Puts a frame around the selected nodes and selects it.
    /// </summary>
    /// <param name="title">What to call it, or null for the default.</param>
    /// <returns>The group, or null when nothing was selected.</returns>
    public CanvasGroup? GroupSelection(string? title = null)
    {
        if (_selection.Count == 0)
        {
            return null;
        }

        if (_graph.AddGroup([.. _selection], title) is not { } group)
        {
            return null;
        }

        _selection.Clear();
        _selectedWire = null;
        _selectedNote = null;
        _selectedGroup = group;

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Group nodes", affectsEvaluation: false));
        return group;
    }

    /// <summary>Whether there is a selection a group could be made from.</summary>
    /// <returns>True when at least one node is selected.</returns>
    public bool CanGroupSelection() => _selection.Count > 0;

    /// <summary>
    /// Reports that the selection has been collapsed into one node elsewhere, and selects it.
    /// </summary>
    /// <param name="slot">The new node's slot, or −1 when there is none.</param>
    /// <remarks>
    /// <b>The canvas does the selection bookkeeping and nothing else.</b> Working out the new
    /// node's interface and building it are engine work, and a view that reached into
    /// <c>Spark.Engine</c> to do them would break the layering rule <c>Spark.Architecture.Tests</c>
    /// enforces — which is how this method came to exist rather than the obvious one. The gesture
    /// itself lives on the view model.
    /// </remarks>
    public void CollapsedInto(int slot)
    {
        _selection.Clear();
        _selectedWire = null;
        _selectedNote = null;
        _selectedGroup = null;

        if (slot >= 0 && slot < _graph.Nodes.Count)
        {
            _selection.Add(slot);
            _focusNode = slot;
        }

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Collapse to custom node", affectsEvaluation: true));
    }

    /// <summary>Whether the selection could become a custom node.</summary>
    /// <returns>True when at least one node is selected.</returns>
    /// <remarks>
    /// Deliberately the same cheap test as grouping. Whether the selection <i>would</i> produce a
    /// usable node needs the full plan, and running that on every selection change to decide
    /// whether a button is enabled would be work done to answer a question the user has not asked.
    /// The refusal, when it comes, names the reason.
    /// </remarks>
    public bool CanCollapseSelection() => _selection.Count > 0;

    /// <summary>Reports that the selected group's title has been edited elsewhere.</summary>
    public void GroupTitleEdited()
    {
        InvalidateVisual();
        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Rename group", affectsEvaluation: false));
    }

    /// <summary>Reports that the selected note's text has been edited elsewhere.</summary>
    /// <remarks>
    /// The canvas hosts no controls — it is one immediate-mode surface — so a note is typed into
    /// in the properties pane and the canvas is told. Redrawing is the canvas's job; recording the
    /// undo step is the shell's, which is why this raises the edit rather than performing it.
    /// </remarks>
    public void NoteTextEdited()
    {
        InvalidateVisual();
        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Edit note", affectsEvaluation: false));
    }

    /// <summary>
    /// Whether an alignment would be meaningful over the current selection.
    /// </summary>
    /// <param name="align">The operation.</param>
    /// <returns>True when there are enough nodes selected for it to mean something.</returns>
    public bool CanAlignSelection(CanvasAlign align) =>
        CanvasAlignment.IsApplicable(align, _selection.Count);

    /// <summary>
    /// Lines up or spreads out the selected nodes.
    /// </summary>
    /// <param name="align">Which operation to apply.</param>
    /// <returns>True when at least one node actually moved.</returns>
    /// <remarks>
    /// <para>
    /// Reported as an edit that does <b>not</b> require a run, for the same reason a drag is: a
    /// position is not in a node's provenance, so nothing downstream of it can evaluate
    /// differently afterwards.
    /// </para>
    /// <para>
    /// <b>An alignment that moves nothing records nothing.</b> Aligning an already-aligned column
    /// is a thing users do constantly — it is how you check — and putting a step on the undo stack
    /// whose undo moves nothing reads as undo being broken. That is N19 in the shape the drag
    /// gesture already had to learn.
    /// </para>
    /// </remarks>
    public bool AlignSelection(CanvasAlign align)
    {
        if (!CanAlignSelection(align))
        {
            return false;
        }

        // The spatial index is rebuilt inside Render, so a canvas that has never painted has a
        // stale one - and an alignment can be invoked from a menu before any frame is drawn.
        EnsureIndex();

        // Sorted so that the operation is a function of the geometry and not of the order the
        // user happened to click in. Distribute is the case that would notice.
        List<int> slots = [.. _selection];
        slots.Sort();

        List<CanvasBounds> boxes = new(slots.Count);
        foreach (int slot in slots)
        {
            if (slot < 0 || slot >= _graph.Nodes.Count)
            {
                return false;
            }

            boxes.Add(_graph.Nodes[slot].Bounds);
        }

        IReadOnlyList<(double X, double Y)> placed = CanvasAlignment.Apply(align, boxes);
        bool moved = false;

        for (int i = 0; i < slots.Count; i++)
        {
            CanvasNode node = _graph.Nodes[slots[i]];
            (double x, double y) = placed[i];

            if (node.X == x && node.Y == y)
            {
                continue;
            }

            node.X = x;
            node.Y = y;
            _index.Update(slots[i], node.Bounds);
            moved = true;
        }

        if (!moved)
        {
            return false;
        }

        _wireVisuals.Clear();
        InvalidateVisual();
        // Labelled without a node count, unlike Move and Delete. Those name an amount of work;
        // an alignment names an arrangement, and "Undo Align left" says everything "Undo Align
        // left 3 nodes" would while reading like English.
        GraphChanged?.Invoke(
            this, new GraphEditedEventArgs(CanvasAlignment.Describe(align), affectsEvaluation: false));

        return true;
    }

    /// <summary>
    /// Deletes the selected wire if there is one, otherwise every selected node.
    /// </summary>
    /// <remarks>
    /// Wire first. A user who has just clicked a wire and pressed Delete means the wire, and
    /// deleting their whole selection instead is the kind of surprise that costs trust in an editor
    /// permanently.
    /// </remarks>
    /// <returns>True when something was removed.</returns>
    public bool DeleteSelection()
    {
        if (_selectedWire is { } wire)
        {
            _selectedWire = null;
            if (!_graph.Disconnect(wire))
            {
                return false;
            }

            _wireVisuals.Clear();
            InvalidateVisual();
            GraphChanged?.Invoke(this, new GraphEditedEventArgs("Delete wire", affectsEvaluation: true));
            return true;
        }

        // Ungrouping never deletes work. The frame goes; every node it framed stays exactly
        // where it was. An editor that takes the contents with the container is the single most
        // expensive surprise it can spring on somebody.
        if (_selectedGroup is { } group)
        {
            _selectedGroup = null;
            if (!_graph.RemoveGroup(group))
            {
                return false;
            }

            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            GraphChanged?.Invoke(this, new GraphEditedEventArgs("Ungroup", affectsEvaluation: false));
            return true;
        }

        if (_selectedNote is { } note)
        {
            _selectedNote = null;
            if (!_graph.RemoveNote(note))
            {
                return false;
            }

            _hoverNote = null;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            GraphChanged?.Invoke(this, new GraphEditedEventArgs("Delete note", affectsEvaluation: false));
            return true;
        }

        if (_selection.Count == 0)
        {
            return false;
        }

        // Highest slot first: removing a node renumbers every slot after it, so removing in
        // ascending order would delete the wrong nodes from the second one onwards.
        List<int> doomed = [.. _selection];
        doomed.Sort();

        for (int index = doomed.Count - 1; index >= 0; index--)
        {
            _graph.Remove(doomed[index]);
        }

        _selection.Clear();
        _hoverNode = -1;
        _focusNode = -1;
        _hoverPort = null;
        _wireVisuals.Clear();
        _indexDirty = true;

        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        GraphChanged?.Invoke(this, new GraphEditedEventArgs(Plural("Delete", doomed.Count), affectsEvaluation: true));
        return true;
    }

    /// <summary>
    /// The wire whose curve passes closest to a world point, within a screen-space reach.
    /// </summary>
    /// <remarks>
    /// Sampled rather than solved. The exact closest point on a cubic Bézier is a quintic root
    /// find, and sixteen samples is well inside the tolerance of a fourteen-pixel target while
    /// being cheap enough to run on every click without thinking about it.
    /// </remarks>
    private CanvasWire? HitTestWire(Point world)
    {
        EnsureWireVisuals();

        double reach = WireHitScreenSize / _transform.Zoom;
        double best = reach * reach;
        CanvasWire? found = null;

        foreach (WireVisual visual in _wireVisuals)
        {
            if (!visual.Bounds.Contains(world.X, world.Y))
            {
                continue;
            }

            for (int step = 0; step <= WireHitSamples; step++)
            {
                double t = step / (double)WireHitSamples;
                visual.Sample(t, out double x, out double y);

                double dx = x - world.X;
                double dy = y - world.Y;
                double distance = (dx * dx) + (dy * dy);

                if (distance < best)
                {
                    best = distance;
                    found = visual.Wire;
                }
            }
        }

        return found;
    }

    /// <summary>The topmost note under a world point, or null.</summary>
    /// <remarks>
    /// A linear scan, back to front, and not an entry in <c>SceneIndex</c>. The index earns itself
    /// over thousands of nodes; a graph with thousands of <i>notes</i> is not a thing anybody has,
    /// and a second index would be a second thing to keep in step for a loop that is currently
    /// shorter than the call that would replace it. Revisit it when a real graph has hundreds.
    /// </remarks>
    private CanvasNote? HitTestNote(Point world)
    {
        IReadOnlyList<CanvasNote> notes = _graph.Notes;
        for (int index = notes.Count - 1; index >= 0; index--)
        {
            if (notes[index].Bounds.Contains(world.X, world.Y))
            {
                return notes[index];
            }
        }

        return null;
    }

    /// <summary>The topmost group whose <i>title strip</i> is under a world point, or null.</summary>
    private CanvasGroup? HitTestGroupTitle(Point world)
    {
        IReadOnlyList<CanvasGroup> groups = _graph.Groups;
        for (int index = groups.Count - 1; index >= 0; index--)
        {
            if (_graph.GroupBounds(groups[index]) is not { } bounds)
            {
                continue;
            }

            if (world.X >= bounds.MinX && world.X <= bounds.MaxX
                && world.Y >= bounds.MinY && world.Y <= bounds.MinY + CanvasGroup.TitleHeight)
            {
                return groups[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Moves a group by moving its members. The frame follows because it is derived from them.
    /// </summary>
    private void MoveGroup(CanvasGroup group, double dx, double dy)
    {
        _dragTotalX += dx;
        _dragTotalY += dy;

        foreach (int slot in _graph.SlotsIn(group))
        {
            CanvasNode node = _graph.Nodes[slot];
            node.X += dx;
            node.Y += dy;
            _index.Update(slot, node.Bounds);
        }

        _wireVisuals.Clear();
    }

    private void MoveNote(CanvasNote note, double dx, double dy)
    {
        _dragTotalX += dx;
        _dragTotalY += dy;
        note.X += dx;
        note.Y += dy;
    }

    private int HitTestNode(Point world)
    {
        // Hit-testing must not depend on a frame having been painted first. It does not in the
        // running application — a paint always precedes a click — but a canvas that is clicked
        // before its first render answers "nothing here", and that failure is invisible until
        // something automates the click.
        EnsureIndex();
        return _index.HitTest(world.X, world.Y);
    }

    private CanvasPort? HitTestPort(Point world)
    {
        EnsureIndex();

        double reach = Math.Max(PortHitScreenSize, PortMinimumHitScreenSize) / _transform.Zoom / 2;

        _index.Query(world.X - reach, world.Y - reach, world.X + reach, world.Y + reach);

        foreach (int slot in _index.VisibleTopDown)
        {
            CanvasNode node = _graph.Nodes[slot];

            for (int i = 0; i < node.Inputs.Count; i++)
            {
                node.InputPortCentre(i, out double x, out double y);
                if (Math.Abs(x - world.X) <= reach && Math.Abs(y - world.Y) <= reach)
                {
                    return new CanvasPort(slot, i, IsOutput: false);
                }
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                node.OutputPortCentre(i, out double x, out double y);
                if (Math.Abs(x - world.X) <= reach && Math.Abs(y - world.Y) <= reach)
                {
                    return new CanvasPort(slot, i, IsOutput: true);
                }
            }
        }

        return null;
    }

    private void PortCentre(CanvasPort port, out double x, out double y)
    {
        if (port.NodeIndex < 0 || port.NodeIndex >= _graph.Nodes.Count)
        {
            x = 0;
            y = 0;
            return;
        }

        CanvasNode node = _graph.Nodes[port.NodeIndex];
        if (port.IsOutput)
        {
            node.OutputPortCentre(port.PortIndex, out x, out y);
        }
        else
        {
            node.InputPortCentre(port.PortIndex, out x, out y);
        }
    }

    private Point ToWorld(Point screen) =>
        new(_transform.ToWorldX(screen.X), _transform.ToWorldY(screen.Y));

    private static bool NullablePortEquals(CanvasPort? left, CanvasPort? right) =>
        left is null ? right is null : right is not null && left.Value == right.Value;

    /// <summary>
    /// The header glyph for a state, from §7.4. Error wins over warning, and both win over
    /// not-evaluated, because a node that errored is the one the user is looking for.
    /// </summary>
    private static string? StateGlyph(CanvasNodeState state)
    {
        if (state.HasFlag(CanvasNodeState.Error))
        {
            return "✕";
        }

        if (state.HasFlag(CanvasNodeState.Warning))
        {
            return "⚠";
        }

        return state.HasFlag(CanvasNodeState.NotEvaluated) ? "○" : null;
    }

    private FormattedText GlyphRun(string text) =>
        Run(_glyphText, text, HeaderTypeface, GlyphFontSize, SparkPalette.TextInverseBrush);

    private FormattedText HeaderRun(string text) =>
        Run(_headerText, text, HeaderTypeface, HeaderFontSize, SparkPalette.TextInverseBrush);

    private FormattedText LabelRun(string text) =>
        Run(_labelText, text, LabelTypeface, PortFontSize, SparkPalette.TextSecondaryBrush);

    private FormattedText TypeRun(string text) =>
        Run(_typeText, text, LabelTypeface, TypeFontSize, SparkPalette.TextMutedBrush);

    private static FormattedText Run(
        Dictionary<string, FormattedText> cache, string text, Typeface typeface, double size, IBrush brush)
    {
        if (cache.TryGetValue(text, out FormattedText? existing))
        {
            return existing;
        }

        // Text layout dominates at scale, which is the reason the design language drops labels
        // below 8 px rather than clamping them. Caching by string means a graph of two thousand
        // nodes drawn from a library of two hundred names lays out two hundred runs, once.
        if (cache.Count >= MaximumCachedTextRuns)
        {
            cache.Clear();
        }

        FormattedText run = new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        cache[text] = run;
        return run;
    }

    private sealed class WireVisual
    {
        internal WireVisual(CanvasWire wire, double x0, double y0, double x1, double y1, StreamGeometry geometry)
        {
            Wire = wire;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Geometry = geometry;
            Bounds = new CanvasBounds(
                Math.Min(x0, x1) - 64, Math.Min(y0, y1) - 8, Math.Max(x0, x1) + 64, Math.Max(y0, y1) + 8);
        }

        internal CanvasWire Wire { get; }

        internal double X0 { get; }

        internal double Y0 { get; }

        internal double X1 { get; }

        internal double Y1 { get; }

        internal StreamGeometry Geometry { get; }

        internal CanvasBounds Bounds { get; }

        internal bool Matches(CanvasWire wire, double x0, double y0, double x1, double y1) =>
            Wire == wire && X0 == x0 && Y0 == y0 && X1 == x1 && Y1 == y1;

        /// <summary>The point on the wire's cubic Bézier at parameter <paramref name="t"/>.</summary>
        internal void Sample(double t, out double x, out double y)
        {
            double reach = Math.Max(40, Math.Abs(X1 - X0) * 0.5);
            double u = 1 - t;
            double a = u * u * u;
            double b = 3 * u * u * t;
            double c = 3 * u * t * t;
            double d = t * t * t;

            x = (a * X0) + (b * (X0 + reach)) + (c * (X1 - reach)) + (d * X1);
            y = (a * Y0) + (b * Y0) + (c * Y1) + (d * Y1);
        }
    }

    /// <summary>
    /// The pens for one frame. Every screen-space width is divided by the zoom here, once, rather
    /// than at each of the several thousand places that would otherwise need it.
    /// </summary>
    private readonly struct FramePens
    {
        private FramePens(double zoom)
        {
            double screen = 1 / zoom;

            NodeOutline = Pen(SparkPalette.BorderControl, screen);

            // Dashes are given in multiples of the stroke width, so a screen-space stroke gives a
            // screen-space dash for free and the pattern does not dissolve when zoomed out.
            NodeOutlineDashed = new ImmutablePen(
                new ImmutableSolidColorBrush(SparkPalette.BorderControl),
                screen,
                new ImmutableDashStyle([3, 2], 0),
                PenLineCap.Flat,
                PenLineJoin.Round);

            PortRankRing = Pen(SparkPalette.PortRest, screen);
            WireSelected = Pen(SparkPalette.Accent, Math.Max(2.25, screen));
            LipRest = Pen(Color.FromArgb(0xB3, 0x3E, 0x46, 0x54), screen);
            LipHover = Pen(Color.FromArgb(0xB3, 0x86, 0x74, 0xD6), screen);
            SelectionRing = Pen(SparkPalette.Accent, 2 * screen);
            ErrorRing = Pen(SparkPalette.StateError, 2 * screen);
            WarningRing = Pen(SparkPalette.StateWarning, 2 * screen);
            FocusRing = Pen(SparkPalette.FocusRing, 2 * screen);
            FocusContour = Pen(SparkPalette.FocusContour, screen);
            AccentThin = Pen(SparkPalette.Accent, screen);

            // The casing never falls below 2 px of screen space and the core never below 1 px,
            // because a sub-pixel stroke is antialiased into invisibility exactly when the graph is
            // zoomed out far enough that the wires are the only structure left to read.
            WireCasing = Pen(SparkPalette.WireCasing, Math.Max(3.75, 2 * screen));
            WireCore = Pen(SparkPalette.WireCore, Math.Max(1.75, screen));
            WireCoreThin = Pen(SparkPalette.WireCore, screen);
            WireSuccess = Pen(SparkPalette.StateSuccess, Math.Max(2.25, screen));
            WireWarning = Pen(SparkPalette.StateWarning, Math.Max(2.25, screen));
            WireError = Pen(SparkPalette.StateError, Math.Max(2.25, screen));
        }

        internal IPen NodeOutline { get; }

        internal IPen NodeOutlineDashed { get; }

        internal IPen PortRankRing { get; }

        internal IPen WireSelected { get; }

        internal IPen LipRest { get; }

        internal IPen LipHover { get; }

        internal IPen SelectionRing { get; }

        internal IPen ErrorRing { get; }

        internal IPen WarningRing { get; }

        internal IPen FocusRing { get; }

        internal IPen FocusContour { get; }

        internal IPen AccentThin { get; }

        internal IPen WireCasing { get; }

        internal IPen WireCore { get; }

        internal IPen WireCoreThin { get; }

        internal IPen WireSuccess { get; }

        internal IPen WireWarning { get; }

        internal IPen WireError { get; }

        internal static FramePens ForZoom(double zoom) => new(zoom);

        private static IPen Pen(Color colour, double thickness) =>
            new ImmutablePen(new ImmutableSolidColorBrush(colour), thickness, null, PenLineCap.Round, PenLineJoin.Round);
    }
}

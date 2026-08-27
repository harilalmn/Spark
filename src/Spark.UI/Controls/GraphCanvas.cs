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
    private const double HeaderFontSize = 12;
    private const double PortFontSize = 11;
    private const int MaximumCachedTextRuns = 4096;

    private static readonly Typeface HeaderTypeface =
        new("Inter", FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);

    private static readonly Typeface LabelTypeface =
        new("Inter", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    private readonly SceneIndex _index = new();
    private readonly CanvasTransform _transform = new();
    private readonly Dictionary<string, FormattedText> _headerText = [];
    private readonly Dictionary<string, FormattedText> _labelText = [];
    private readonly List<WireVisual> _wireVisuals = [];
    private readonly HashSet<int> _selection = [];
    private readonly HashSet<PlaceholderPort> _connectedPorts = [];

    private PlaceholderGraph _graph = new();
    private bool _indexDirty = true;

    private InteractionMode _mode;
    private Point _pointerAnchor;
    private Point _dragStartWorld;
    private int _hoverNode = -1;
    private int _focusNode = -1;
    private PlaceholderPort? _hoverPort;
    private PlaceholderPort? _dragSourcePort;
    private Point _dragWireWorldEnd;
    private WireOutcome _dragOutcome = WireOutcome.Refused;
    private Point _marqueeStartWorld;
    private Point _marqueeEndWorld;

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

    /// <summary>How a wire being dragged is reported back to the user while the button is down.</summary>
    /// <remarks>
    /// The three outcomes are drawn in <c>state.success</c>, <c>state.warning</c> and
    /// <c>state.error</c> — the same three hexes used by node error badges (Decision V1). The
    /// reuse is safe because semantic colours appear only on strokes, rings and glyphs and never
    /// as a fill, and because each is accompanied by a glyph so the meaning survives colour
    /// blindness.
    /// </remarks>
    public enum WireOutcome
    {
        /// <summary>The connection is accepted as-is. Drawn in <c>state.success</c> with a <c>✓</c>.</summary>
        Accepted,

        /// <summary>Accepted with a lossy conversion. Drawn in <c>state.warning</c> with a <c>≈</c>.</summary>
        Lossy,

        /// <summary>Refused. Drawn in <c>state.error</c> with a <c>✕</c>.</summary>
        Refused,
    }

    private enum InteractionMode
    {
        None,
        Panning,
        DraggingNodes,
        Marquee,
        DraggingWire,
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
    /// The slots currently selected, as indices into <see cref="PlaceholderGraph.Nodes"/>.
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
    public PlaceholderPort? HoveredPort => _hoverPort;

    /// <summary>The number of nodes the last frame's cull found visible.</summary>
    public int LastVisibleNodeCount { get; private set; }

    /// <summary>The number of nodes the last frame's cull had to test.</summary>
    public int LastConsideredNodeCount { get; private set; }

    /// <summary>The graph being drawn.</summary>
    /// <remarks>
    /// Setting this rebuilds the spatial index on the next frame rather than immediately, so that
    /// loading a graph costs one rebuild however many times the property is touched.
    /// </remarks>
    public PlaceholderGraph Graph
    {
        get => _graph;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _graph = value;
            _selection.Clear();
            _hoverNode = -1;
            _focusNode = -1;
            _wireVisuals.Clear();
            _indexDirty = true;
            InvalidateVisual();
        }
    }

    /// <summary>Frames the whole graph in the control, with a margin.</summary>
    public void ZoomToFit()
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            return;
        }

        _transform.FitTo(_graph.ComputeBounds(), Bounds.Width, Bounds.Height);
        InvalidateVisual();
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

        PlaceholderPort? port = HitTestPort(world);
        if (port is not null)
        {
            _mode = InteractionMode.DraggingWire;
            _dragSourcePort = port;
            _dragWireWorldEnd = world;
            _dragOutcome = WireOutcome.Refused;
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

            _focusNode = node;
            _mode = InteractionMode.DraggingNodes;
            _dragStartWorld = world;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _selection.Clear();
        }

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
        PlaceholderPort? port = HitTestPort(world);

        if (node != _hoverNode || !NullablePortEquals(port, _hoverPort))
        {
            _hoverNode = node;
            _hoverPort = port;
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

            default:
                break;
        }

        _mode = InteractionMode.None;
        _dragSourcePort = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
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
                InvalidateVisual();
                e.Handled = true;
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
            DrawWires(context, pens, visible, detail);
            DrawNodes(context, pens, detail, zoom);
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
        foreach (PlaceholderNode node in _graph.Nodes)
        {
            bounds.Add(node.Bounds);
        }

        _index.Rebuild(bounds);
        _indexDirty = false;
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
            PlaceholderNode node = _graph.Nodes[slot];
            bool selected = _selection.Contains(slot);
            bool hovered = slot == _hoverNode;

            Rect nodeRect = new(node.X, node.Y, node.Width, node.Height);
            RoundedRect rounded = new(nodeRect, CornerRadius);
            Color categoryColour = hovered
                ? NodeCategoryColours.HoverColourOf(node.Category)
                : NodeCategoryColours.ColourOf(node.Category);

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
            Color bodyColour = selected
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
            Rect headerRect = new(node.X, node.Y, node.Width, PlaceholderNode.HeaderHeight);
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
                context.DrawRectangle(null, pens.NodeOutline, rounded);
            }

            DrawPorts(context, pens, node, slot, detail);

            if (drawsTitle)
            {
                FormattedText title = HeaderRun(node.Title);
                using (context.PushClip(headerRect))
                {
                    context.DrawText(
                        title,
                        new Point(node.X + 8, node.Y + ((PlaceholderNode.HeaderHeight - title.Height) / 2)));
                }
            }

            if (drawsPortLabels)
            {
                DrawPortLabels(context, node);
            }

            DrawStateRings(context, pens, node, nodeRect, selected);

            if (slot == _focusNode && IsFocused)
            {
                DrawFocusSandwich(context, pens, nodeRect);
            }
        }
    }

    private void DrawPorts(DrawingContext context, in FramePens pens, PlaceholderNode node, int slot, CanvasDetail detail)
    {
        // Below 67% ports are 2 px screen-space dots; above it they are drawn at their design size
        // and grow on hover. The hit target never shrinks below 10 px of screen space regardless
        // (§7.6) — ports are the smallest thing anyone has to aim at in the product.
        double zoom = _transform.Zoom;
        double radius = detail <= CanvasDetail.Fill ? 1.0 / zoom : PortRadius;

        for (int i = 0; i < node.Inputs.Count; i++)
        {
            node.InputPortCentre(i, out double x, out double y);
            PlaceholderPort port = new(slot, i, IsOutput: false);
            DrawPort(context, pens, port, x, y, radius);
        }

        for (int i = 0; i < node.Outputs.Count; i++)
        {
            node.OutputPortCentre(i, out double x, out double y);
            PlaceholderPort port = new(slot, i, IsOutput: true);
            DrawPort(context, pens, port, x, y, radius);
        }
    }

    private void DrawPort(DrawingContext context, in FramePens pens, PlaceholderPort port, double x, double y, double radius)
    {
        bool hovered = _hoverPort == port;
        bool connected = _connectedPorts.Contains(port);
        double drawn = hovered ? Math.Max(radius, PortHoverRadius) : radius;

        IBrush fill = connected ? SparkPalette.PortConnectedBrush : SparkPalette.PortRestBrush;
        context.DrawEllipse(fill, null, new Point(x, y), drawn, drawn);

        if (hovered)
        {
            context.DrawEllipse(null, pens.AccentThin, new Point(x, y), drawn + (2 / _transform.Zoom), drawn + (2 / _transform.Zoom));
        }
    }

    private void DrawPortLabels(DrawingContext context, PlaceholderNode node)
    {
        for (int i = 0; i < node.Inputs.Count; i++)
        {
            node.InputPortCentre(i, out _, out double y);
            FormattedText label = LabelRun(node.Inputs[i]);
            context.DrawText(label, new Point(node.X + 9, y - (label.Height / 2)));
        }

        for (int i = 0; i < node.Outputs.Count; i++)
        {
            node.OutputPortCentre(i, out _, out double y);
            FormattedText label = LabelRun(node.Outputs[i]);
            context.DrawText(label, new Point(node.X + node.Width - 9 - label.Width, y - (label.Height / 2)));
        }
    }

    private void DrawStateRings(DrawingContext context, in FramePens pens, PlaceholderNode node, Rect nodeRect, bool selected)
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

        if (node.State.HasFlag(PlaceholderNodeState.Error))
        {
            RoundedRect ring = new(nodeRect.Inflate(4 / zoom), CornerRadius + (4 / zoom));
            context.DrawRectangle(null, pens.ErrorRing, ring);
        }
        else if (node.State.HasFlag(PlaceholderNodeState.Warning))
        {
            RoundedRect ring = new(nodeRect.Inflate(4 / zoom), CornerRadius + (4 / zoom));
            context.DrawRectangle(null, pens.WarningRing, ring);
        }

        if (node.State.HasFlag(PlaceholderNodeState.Anchor))
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
            context.DrawGeometry(null, detail == CanvasDetail.Silhouette ? pens.WireCoreThin : pens.WireCore, visual.Geometry);
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
        IReadOnlyList<PlaceholderWire> wires = _graph.Wires;

        // Whether a port is connected decides its fill, and it is asked once per port per frame.
        // Answering it by walking the wire list would be quadratic in graph size, which is
        // invisible on a demo graph and fatal on a real one.
        _connectedPorts.Clear();
        foreach (PlaceholderWire wire in wires)
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
            PlaceholderWire wire = wires[i];
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
        foreach (int slot in _selection)
        {
            if (slot < 0 || slot >= _graph.Nodes.Count)
            {
                continue;
            }

            PlaceholderNode node = _graph.Nodes[slot];
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
    }

    private void TryConnect(PlaceholderPort source, PlaceholderPort target)
    {
        (PlaceholderPort from, PlaceholderPort to) = source.IsOutput ? (source, target) : (target, source);
        if (_graph.AddWire(new PlaceholderWire(from, to)))
        {
            _wireVisuals.Clear();
        }
    }

    private static WireOutcome EvaluateDrag(PlaceholderPort? source, PlaceholderPort? target)
    {
        if (source is not { } from || target is not { } to)
        {
            return WireOutcome.Refused;
        }

        if (from.IsOutput == to.IsOutput || from.NodeIndex == to.NodeIndex)
        {
            return WireOutcome.Refused;
        }

        // The placeholder accepts every input-to-output pair. The real answer comes from the graph
        // engine's type check, which also reports the lossy case this canvas already draws.
        return WireOutcome.Accepted;
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

    private PlaceholderPort? HitTestPort(Point world)
    {
        EnsureIndex();

        double reach = Math.Max(PortHitScreenSize, PortMinimumHitScreenSize) / _transform.Zoom / 2;

        _index.Query(world.X - reach, world.Y - reach, world.X + reach, world.Y + reach);

        foreach (int slot in _index.VisibleTopDown)
        {
            PlaceholderNode node = _graph.Nodes[slot];

            for (int i = 0; i < node.Inputs.Count; i++)
            {
                node.InputPortCentre(i, out double x, out double y);
                if (Math.Abs(x - world.X) <= reach && Math.Abs(y - world.Y) <= reach)
                {
                    return new PlaceholderPort(slot, i, IsOutput: false);
                }
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                node.OutputPortCentre(i, out double x, out double y);
                if (Math.Abs(x - world.X) <= reach && Math.Abs(y - world.Y) <= reach)
                {
                    return new PlaceholderPort(slot, i, IsOutput: true);
                }
            }
        }

        return null;
    }

    private void PortCentre(PlaceholderPort port, out double x, out double y)
    {
        if (port.NodeIndex < 0 || port.NodeIndex >= _graph.Nodes.Count)
        {
            x = 0;
            y = 0;
            return;
        }

        PlaceholderNode node = _graph.Nodes[port.NodeIndex];
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

    private static bool NullablePortEquals(PlaceholderPort? left, PlaceholderPort? right) =>
        left is null ? right is null : right is not null && left.Value == right.Value;

    private FormattedText HeaderRun(string text) =>
        Run(_headerText, text, HeaderTypeface, HeaderFontSize, SparkPalette.TextInverseBrush);

    private FormattedText LabelRun(string text) =>
        Run(_labelText, text, LabelTypeface, PortFontSize, SparkPalette.TextSecondaryBrush);

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
        internal WireVisual(PlaceholderWire wire, double x0, double y0, double x1, double y1, StreamGeometry geometry)
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

        internal PlaceholderWire Wire { get; }

        internal double X0 { get; }

        internal double Y0 { get; }

        internal double X1 { get; }

        internal double Y1 { get; }

        internal StreamGeometry Geometry { get; }

        internal CanvasBounds Bounds { get; }

        internal bool Matches(PlaceholderWire wire, double x0, double y0, double x1, double y1) =>
            Wire == wire && X0 == x0 && Y0 == y0 && X1 == x1 && Y1 == y1;
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

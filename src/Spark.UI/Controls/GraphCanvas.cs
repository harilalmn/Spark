using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Spark.UI.Canvas;
using Spark.UI.Graph;
using Spark.UI.Theming;

namespace Spark.UI.Controls;

/// <summary>
/// Which part of a preview bubble a pointer landed on (<c>E8-T72</c>).
/// </summary>
/// <remarks>
/// <b>Three targets in one strip, so a press has to say which.</b> The toggle and the pin do
/// something; the body does nothing and still has to be reported, because a press inside a bubble
/// must not fall through and start a marquee across the graph behind it.
/// </remarks>
public enum CanvasPreviewPart
{
    /// <summary>The point is not in any bubble.</summary>
    None,

    /// <summary>Inside a bubble, but on neither of its two controls.</summary>
    Body,

    /// <summary>On the triangle that opens and closes it.</summary>
    Toggle,

    /// <summary>On the pin that keeps it open after the node stops being selected.</summary>
    Pin,
}

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
/// <param name="recordsUndo">
/// Whether this is a step on the undo stack. False only while a continuous gesture is still in
/// progress, which records once when it ends.
/// </param>
public sealed class GraphEditedEventArgs(
    string label, bool affectsEvaluation, bool recordsUndo = true) : EventArgs
{
    /// <summary>What the edit did, phrased for an undo menu.</summary>
    public string Label { get; } = label;

    /// <summary>Whether the change requires the graph to be evaluated again.</summary>
    public bool AffectsEvaluation { get; } = affectsEvaluation;

    /// <summary>
    /// Whether the change is a step on the undo stack (<c>E8-T25</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>True for every edit except the middle of a continuous gesture.</b> Dragging a slider
    /// changes the graph on every pointer move and must re-run it on every pointer move, or the
    /// slider is not a slider - but recording each of those would put a hundred entries on the
    /// undo stack for one gesture, and each entry serialises the whole document to a string.
    /// </para>
    /// <para>
    /// The gesture records once, on release, so undo steps back over the whole drag. That is what
    /// a user means by "undo that": not "undo the last pixel of that".
    /// </para>
    /// </remarks>
    public bool RecordsUndo { get; } = recordsUndo;
}

/// <summary>
/// A request to edit a node's value in place, raised by clicking the field drawn on it
/// (<c>E8-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The canvas says where and what, and hosts nothing.</b> It is an immediate-mode surface
/// ([ADR-0013](../../docs/adr/0013-immediate-mode-canvas.md)) and a <c>Control</c> rather than a
/// <c>Panel</c>, so it cannot hold a child even if it wanted one — which is the right shape
/// anyway. A caret, a selection, an input method and a clipboard are not things to re-implement in
/// a draw loop, so the pane over the canvas puts a real <c>TextBox</c> at the rectangle named here.
/// </para>
/// <para>
/// The rectangle is in <b>control</b> coordinates, already through the pan and zoom, because that
/// is the space the overlay is positioned in.
/// </para>
/// </remarks>
/// <param name="slot">The node whose first input is being edited.</param>
/// <param name="text">What the field currently holds, rendered invariantly.</param>
/// <param name="screenX">The field's left edge in control coordinates.</param>
/// <param name="screenY">Its top edge.</param>
/// <param name="screenWidth">Its width, already scaled by the zoom.</param>
/// <param name="screenHeight">Its height.</param>
public sealed class CanvasFieldEditEventArgs(
    int slot,
    string text,
    double screenX,
    double screenY,
    double screenWidth,
    double screenHeight) : EventArgs
{
    /// <summary>The node whose first input is being edited.</summary>
    public int Slot { get; } = slot;

    /// <summary>What the field currently holds.</summary>
    public string Text { get; } = text;

    /// <summary>The field's left edge in control coordinates.</summary>
    public double ScreenX { get; } = screenX;

    /// <summary>Its top edge in control coordinates.</summary>
    public double ScreenY { get; } = screenY;

    /// <summary>Its width in control coordinates.</summary>
    public double ScreenWidth { get; } = screenWidth;

    /// <summary>Its height in control coordinates.</summary>
    public double ScreenHeight { get; } = screenHeight;
}

/// <summary>
/// A request to create something at a point on the canvas: a searched-for node when empty space is
/// right-clicked, a code block when it is double-clicked.
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
/// yet. Keyboard navigation between nodes is the M8 accessibility pass. Groups, notes, the frozen
/// and not-evaluated states and the evaluating animation (<c>E3-T14</c>), once listed here as
/// specified and not drawn, are all drawn now.
/// </para>
/// </remarks>
public sealed class GraphCanvas : Control
{
    private const double CornerRadius = 6;
    /// <summary>The radius of a port disc, in world units — a 7 px disc at 100%.</summary>
    /// <remarks>
    /// <b>Grown from 5 px on a user's report that the dots were hard to pick.</b> The design
    /// language's §7.4 row moves with it: 7 px at rest, 9 px hovered, over an 18 px hit target.
    /// A port is the smallest thing on the canvas anybody has to aim at, and it is the one that
    /// starts every wire.
    /// </remarks>
    private const double PortRadius = 3.5;
    private const double PortHoverRadius = 4.5;
    private const double PortHitScreenSize = 18;
    private const double PortMinimumHitScreenSize = 14;
    /// <summary>How far a press may travel, in screen pixels, and still count as a click.</summary>
    /// <remarks>
    /// A hand on a mouse moves a pixel or two between press and release, and a wire that refused to
    /// arm because of it would be a feature that works for some people and not others.
    /// </remarks>
    /// <summary>The inset from a port tab's outer end to its name.</summary>
    private const double PortTabTextInset = 8;

    private const double ClickSlopScreen = 3;

    private const double WireHitScreenSize = 6;
    private const int WireHitSamples = 16;
    private const double GlyphFontSize = 12;
    private const double HeaderFontSize = 12;

    /// <summary>The size a note's own text is drawn at.</summary>
    private const double NoteFontSize = 12;

    /// <summary>The inset between a note's edge and its text.</summary>
    private const double NotePadding = 10;

    // `E8-T72`: THE BUBBLE'S FOUR NUMBERS MOVED TO `CanvasNode`, AND THAT IS THE POINT.
    //
    // They were private constants here, so the bubble's geometry existed only inside the draw
    // loop - which is fine for a rectangle nobody clicks and useless for one carrying a toggle and
    // a pin. `CanvasNode.PreviewGap`, `PreviewPadding`, `PreviewRowHeight` and `PreviewLineHeight`
    // are the same numbers where `PortTab` and `FieldBox` already live, and a test can ask for
    // them with no window.
    private const double PortFontSize = 11;
    private const double TypeFontSize = 10;
    private const double TypeGap = 6;
    private const double MinimumRowGap = 8;
    private const int MaximumCachedTextRuns = 4096;

    /// <summary>How much of a node's outline the travelling evaluating stroke covers (<c>E3-T14</c>).</summary>
    private const double EvaluatingStrokeShare = 0.25;

    /// <summary>
    /// One lap of the evaluating stroke: <c>motion.ambient</c>, the only repeating animation in the
    /// product (design language §7.4, <c>E3-T14</c>).
    /// </summary>
    internal static readonly TimeSpan EvaluatingLap = TimeSpan.FromMilliseconds(900);

    // Where every stroke's lap is measured from, so two nodes evaluating at once travel together.
    private static readonly long AnimationEpoch = Stopwatch.GetTimestamp();

    private static readonly Typeface HeaderTypeface =
        new("Inter", FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);

    private static readonly Typeface LabelTypeface =
        new("Inter", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    /// <summary>
    /// The face a code block's source is drawn in on the canvas (<c>E8-T39</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same family the editor uses</b>, because the drawn text and the editor that opens
    /// over it are the same lines in the same place, and a node whose text reflowed the instant
    /// it was clicked into would read as the node moving.
    /// </para>
    /// <para>
    /// <b>Source Code Pro, shipped rather than asked for by name</b> (`E8-T57`). It is the face
    /// Dynamo draws its Code Block in, and the client asked for that face. What was here before —
    /// <c>Cascadia Mono, Consolas, Menlo, monospace</c> — is a wish list that resolves to whatever
    /// a machine happens to have, so the answer differed per machine and matched Dynamo on none of
    /// them by design.
    /// </para>
    /// <para>
    /// <b>No layout constant moved, and that was measured rather than hoped.</b> Source Code Pro
    /// advances <c>0.6 em</c> for every glyph, which is 6.6 px at
    /// <see cref="PortFontSize"/> — exactly the <c>ScriptCharWidth</c> that Cascadia Mono was
    /// estimated against, because that is a monospaced convention rather than a coincidence.
    /// <c>CodeBlockFontTests</c> asserts it against the shipped file, so the constant is a checked
    /// fact rather than an estimate that happens to hold.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>A property rather than a field, because the face is a setting now</b> (`E8-T59`). A
    /// <c>static readonly</c> Typeface captured the family at class load, so changing the font
    /// redrew every block in the face it started with until the application was restarted.
    /// </remarks>
    private static Typeface ScriptTypeface =>
        new(CodeFont.FontFamily, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    private readonly SceneIndex _index = new();
    private readonly CanvasTransform _transform = new();
    private readonly Dictionary<string, FormattedText> _headerText = [];
    private readonly Dictionary<string, FormattedText> _labelText = [];
    private readonly Dictionary<string, FormattedText> _glyphText = [];
    private readonly Dictionary<string, FormattedText> _typeText = [];
    private readonly Dictionary<string, FormattedText> _scriptText = [];
    private readonly Dictionary<string, FormattedText> _numberText = [];
    private readonly Dictionary<string, FormattedText> _hintText = [];
    private readonly Dictionary<string, FormattedText[]> _colouredScript = [];
    private readonly List<WireVisual> _wireVisuals = [];
    private readonly HashSet<int> _selection = [];

    /// <summary>Which nodes already got a bubble this frame, so none is drawn twice.</summary>
    private readonly HashSet<int> _previewsDrawn = [];
    private readonly HashSet<CanvasPort> _connectedPorts = [];

    private CanvasGraph _graph = new();
    private bool _indexDirty = true;
    private bool _fitPending;

    // `E3-T14`: whether the frame being drawn is on screen - an export is a still - and whether a
    // stroke travelled in it, which is what asks for the next frame.
    private bool _liveFrame;
    private bool _evaluatingTravelled;
    private (double Zoom, double OffsetX, double OffsetY) _fitDeferredFrom;

    private InteractionMode _mode;
    private Point _pointerAnchor;
    private Point _dragStartWorld;
    private int _sliderSlot = -1;
    private bool _sliderMoved;
    private int _hoverNode = -1;
    private int _focusNode = -1;
    private CanvasPort? _hoverPort;
    private CanvasPort? _dragSourcePort;
    private bool _wireDragMoved;

    // PULLING A WIRE OFF AN INPUT PORT, IN TWO STAGES, AND THE TWO STAGES ARE THE POINT.
    //
    // `_detachCandidate` is the wire found under a press on a wired input. `_detachedWire` is that
    // wire once the pointer has actually travelled far enough to be a drag. Detaching on the press
    // instead would make a plain CLICK on a wired input delete the wire - and a click on a port is
    // not a mistake, it is `E8-T34`'s gesture for arming a wire, which has to keep working exactly
    // as it did.
    private CanvasWire? _detachCandidate;
    private CanvasWire? _detachedWire;
    private bool _duplicateOnDrag;
    private bool _deselectOnRelease;
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

    /// <summary>
    /// Whether the node drag in progress has travelled far enough to be a drag (<c>E8-T53</c>).
    /// </summary>
    /// <remarks>
    /// <b>Slop, and not <see cref="_dragTotalX"/> being zero.</b> The net displacement is the right
    /// question for <i>did this edit anything</i> — a node dragged out and back is not a move — and
    /// the wrong one for <i>was this a click</i>: a hand that trembles one pixel has moved the node
    /// one pixel, and a click-to-edit gesture that a tremor swallows is a gesture users learn not
    /// to trust. <c>_wireDragMoved</c> already draws the line in the right place and this is the
    /// same line.
    /// </remarks>
    private bool _nodeDragMoved;

    /// <summary>Where a node drag's press landed, which is not where the drag is measured from.</summary>
    /// <remarks>
    /// <b><see cref="_dragStartWorld"/> cannot answer this and it is worth saying why.</b> A node
    /// drag advances it on every pointer move, because the move applies a <i>delta</i> — so it
    /// holds the previous event's position, and the distance from it is one mouse-move's worth of
    /// travel rather than the gesture's. Measuring the slop against it would call every drag a
    /// click. The wire drag reuses it safely only because it never advances it.
    /// </remarks>
    private Point _nodeDragAnchorWorld;

    /// <summary>Whether the node press in progress was made with no modifier held (<c>E8-T53</c>).</summary>
    /// <remarks>
    /// <b>Recorded at the press, not read at the release</b>, for the reason
    /// <see cref="_duplicateOnDrag"/> and <c>_deselectOnRelease</c> are: the gesture is the one the
    /// user started, and a key let go part way through a drag must not change what the gesture was.
    /// Reading <c>KeyModifiers</c> off the release event instead is what the first version did, and
    /// a Control+click opened an editor.
    /// </remarks>
    private bool _nodePressPlain;

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

    /// <inheritdoc/>
    /// <remarks>
    /// <b>The code font is listened for from here rather than from the constructor</b> (`E8-T59`).
    /// `CodeFont` is static, so a subscription taken in the constructor outlives any canvas that
    /// is never attached - and the handler is then invoked on whichever thread changed the font,
    /// against a control owned by the thread that built it. That is not hypothetical: it turned
    /// three font tests red in the full suite and green in isolation, because other tests build a
    /// canvas off the UI thread and never show it.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        CodeFont.Changed += OnCodeFontChanged;
        OnCodeFontChanged(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        CodeFont.Changed -= OnCodeFontChanged;
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
    /// Raised when empty canvas is <b>right</b>-clicked, which is a request to search for a node
    /// and create it there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hunting a tree for a node you can already name is the slowest part of building a graph, and
    /// it gets slower with every package installed — so the search box earns a gesture of its own.
    /// </para>
    /// <para>
    /// <b>It was the double-click, and Dynamo does not agree.</b> There, a double-click on empty
    /// canvas drops a code block; a user arriving from Dynamo double-clicks expecting one and gets
    /// a search dialog instead. The search moved here rather than being deleted, because the two
    /// gestures are both worth having and only one of them was contested.
    /// </para>
    /// </remarks>
    public event EventHandler<CanvasCreateRequestedEventArgs>? CreateRequested;

    /// <summary>
    /// Raised when empty canvas is double-clicked, which is a request for a code block there.
    /// </summary>
    /// <remarks>
    /// Dynamo's gesture, and the reason to copy it is muscle memory: a code block is how a Dynamo
    /// user writes a number, a formula or a list without hunting for the node that does it, and
    /// double-click-then-type is the whole of that workflow.
    /// </remarks>
    public event EventHandler<CanvasCreateRequestedEventArgs>? CodeBlockRequested;

    /// <summary>Raised when a node's in-place value field is clicked (<c>E8-T5</c>).</summary>
    public event EventHandler<CanvasFieldEditEventArgs>? FieldEditRequested;

    /// <summary>
    /// Asks the pane above to put a real code editor over a code block's source (<c>E8-T39</c>).
    /// </summary>
    /// <remarks>
    /// Shaped exactly like <see cref="FieldEditRequested"/> and for exactly the same reason: the
    /// canvas says <i>which node, what it holds, and where on screen</i>, and hosts nothing.
    /// </remarks>
    public event EventHandler<CanvasFieldEditEventArgs>? ScriptEditRequested;

    /// <summary>
    /// Asks the pane above to put a text box over a node's title (<c>E8-T68</c>).
    /// </summary>
    /// <remarks>
    /// The third member of the same family, shaped like the other two: the canvas says
    /// <i>which node, what it holds, and where on screen</i>, and hosts nothing.
    /// </remarks>
    public event EventHandler<CanvasFieldEditEventArgs>? TitleEditRequested;

    /// <summary>Something moved a node's rectangle on screen (<c>E8-T43</c>, <c>E8-T52</c>).</summary>
    /// <remarks>
    /// <para>
    /// <b>For the overlay above, which positions real controls in screen coordinates over a
    /// surface that moves.</b> Everything the canvas draws is in world units and follows the view
    /// for free; the one control the hybrid overlay is holding does not, and before this the
    /// collision was resolved by closing it — one notch of the wheel and the editor a user was
    /// typing in snapped shut.
    /// </para>
    /// <para>
    /// <b>It is named for what its consumer needs to know, and that is not "the view moved".</b>
    /// The pan and the zoom move every node's rectangle at once; dragging a node moves one. Both
    /// leave a control positioned in screen pixels pointing at where the block used to be, and the
    /// overlay cannot tell the difference — so the event is <i>a node is somewhere else now</i>.
    /// It was called <c>ViewChanged</c> and raised only by the pan and the zoom, which is exactly
    /// the shape of the defect `E8-T52` fixes: the two node drags each remembered to redraw and
    /// neither remembered to announce.
    /// </para>
    /// </remarks>
    public event EventHandler? ContentMoved;

    private enum InteractionMode
    {
        None,
        Panning,
        DraggingNodes,
        Marquee,
        DraggingWire,

        /// <summary>
        /// A port has been <i>clicked</i>, and the wire is following the pointer with no button
        /// held until a second click lands (`E8-T34`).
        /// </summary>
        PendingWire,
        DraggingNote,
        DraggingGroup,
        DraggingSlider,
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

    /// <summary>
    /// Re-measures and redraws every block when the code font changes (`E8-T59`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three things have to happen and none of them are optional.</b> The cached
    /// <see cref="FormattedText"/> runs hold the old face, so they go; a node's width came from the
    /// old face's character width, so every node measures again; and the spatial index was built
    /// from the widths that just changed, so it is rebuilt before anything hit-tests against it.
    /// </para>
    /// <para>
    /// <b>It announces as well as redrawing</b>, because a block that has just changed width has
    /// moved its source rectangle — and an open editor is positioned over that rectangle in screen
    /// pixels. This is the fourth caller of that funnel and the first that is not a position
    /// change, which is why <c>ContentMoved</c> is named for the *consequence* rather than the
    /// cause.
    /// </para>
    /// </remarks>
    private void OnCodeFontChanged(object? sender, EventArgs e)
    {
        // The same guard `CodeBlockEditor.ApplyCodeFont` carries, for the same reason: a canvas
        // this thread does not own is dead or will re-measure when it is next attached.
        if (!CheckAccess())
        {
            return;
        }

        CanvasNode.ScriptCharWidth = ScriptFontSize * CodeFont.CurrentAdvanceRatio;

        _scriptText.Clear();
        _graph.RemeasureNodes();
        _wireVisuals.Clear();
        RefreshStructure();
        AnnounceMove();
    }

    /// <summary>The size a block's source is drawn at, which the character width is measured at.</summary>
    private const double ScriptFontSize = PortFontSize;

    /// <summary>
    /// Redraws, and tells the overlay that a node is somewhere else now (<c>E8-T43</c>,
    /// <c>E8-T52</c>).
    /// </summary>
    /// <remarks>
    /// <b>Every site that moves the pan, the zoom or a node calls this instead of
    /// <c>InvalidateVisual</c>.</b> They all had to invalidate anyway, so routing them through one
    /// place costs nothing and makes "did anything move?" answerable — the alternative is a call
    /// site that has to remember a second thing, which is an opportunity to forget. **It was
    /// forgotten**: the pan and the wheel came through here and the two node drags did not, so an
    /// open editor followed a zoom and was left behind by a drag.
    /// </remarks>
    private void AnnounceMove()
    {
        InvalidateVisual();
        ContentMoved?.Invoke(this, EventArgs.Empty);
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
    /// A world position near the center of the visible canvas, offset so repeated placements do
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

    /// <summary>
    /// Whether the last frame drew an evaluating stroke travelling, and so asked for the next frame
    /// (<c>E3-T14</c>).
    /// </summary>
    public bool LastFrameAnimatedEvaluation { get; private set; }

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
    /// <param name="bounds">The region to center on.</param>
    public void CenterOn(CanvasBounds bounds)
    {
        double zoom = _transform.Zoom;
        _transform.OffsetX = ((bounds.MinX + bounds.MaxX) / 2) - (Bounds.Width / (2 * zoom));
        _transform.OffsetY = ((bounds.MinY + bounds.MaxY) / 2) - (Bounds.Height / (2 * zoom));
        AnnounceMove();
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
        AnnounceMove();
    }

    /// <summary>
    /// Writes the whole graph to a PNG at a chosen resolution (<c>E8-T69</c>).
    /// </summary>
    /// <param name="path">Where to write the file.</param>
    /// <param name="pixelWidth">The image width, clamped by <see cref="CanvasExport"/>.</param>
    /// <param name="pixelHeight">The image height, clamped the same way.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// <b>Asked for by the client</b>, and the resolution is the point of it: a canvas-sized
    /// picture is a screenshot anybody could have taken, and a graph put in a document or printed
    /// needs more pixels than a window has.
    /// </para>
    /// <para>
    /// <b>The whole graph, not the visible part.</b> The view is fitted to
    /// <c>ComputeBounds()</c> for the duration of the render, so what comes out is the graph and
    /// not the scroll position it happened to be at. That is also why the view is put back
    /// afterwards, exactly: an export is not an edit, and a user who exports and finds their
    /// canvas somewhere else has been charged for a file.
    /// </para>
    /// <para>
    /// <b>No frame counter in the file.</b> That overlay belongs on a screen and not in a picture
    /// somebody is going to hand to a client, which is why <see cref="RenderScene"/> takes it as a
    /// parameter rather than reading the field.
    /// </para>
    /// <para>
    /// <b>Past four times magnification the image gets larger and the graph does not</b>, because
    /// the fit runs through <see cref="CanvasTransform"/> and that is its ceiling. A small graph
    /// asked for at 8,000 pixels comes out centred with margin around it rather than at eight
    /// times — which is the honest answer, and the alternative was a second zoom rule that only
    /// files obey.
    /// </para>
    /// </remarks>
    public void ExportImage(string path, int pixelWidth, int pixelHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int width = CanvasExport.Clamp(pixelWidth);
        int height = CanvasExport.Clamp(pixelHeight);

        // Kept by value, not by reference: `_transform` is mutated below and a saved reference
        // would be a saved view of the change rather than of what came before it.
        (double Zoom, double OffsetX, double OffsetY) view =
            (_transform.Zoom, _transform.OffsetX, _transform.OffsetY);

        try
        {
            _transform.FitTo(_graph.ComputeBounds(), width, height);

            using RenderTargetBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96));

            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                RenderScene(context, new Rect(0, 0, width, height), statistics: false);
            }

            bitmap.Save(path, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            _transform.Zoom = view.Zoom;
            _transform.OffsetX = view.OffsetX;
            _transform.OffsetY = view.OffsetY;
        }

        // The index was queried against the export's viewport, so the next frame on screen would
        // otherwise cull against a rectangle nobody is looking through.
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
                AnnounceMove();
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

        // Right-click on empty canvas opens the node search (`E8-T27`). On anything else it does
        // nothing yet rather than something arbitrary: a context menu on a node is a real feature
        // with a real menu behind it, and half of one taught now would have to be untaught.
        if (properties.IsRightButtonPressed)
        {
            if (HitTestPort(world) is null && HitTestNode(world) < 0 && HitTestWire(world) is null)
            {
                e.Handled = true;
                CreateRequested?.Invoke(
                    this, new CanvasCreateRequestedEventArgs(world.X, world.Y, screen.X, screen.Y));
            }

            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        // `E8-T72`: THE BUBBLES ARE HIT FIRST, BECAUSE THEY ARE DRAWN LAST.
        //
        // A bubble hangs below its node and over whatever is behind it, so a press inside one must
        // belong to it rather than to the node it happens to overlap. A press on the body is
        // swallowed rather than ignored: it must not start a marquee across the graph, and it must
        // not clear the selection the bubble is attached to.
        if (HitTestPreview(world, out int previewSlot) is var part && part is not CanvasPreviewPart.None)
        {
            switch (part)
            {
                case CanvasPreviewPart.Toggle:
                    TogglePreview(previewSlot);
                    break;

                case CanvasPreviewPart.Pin:
                    PinPreview(previewSlot);
                    break;

                default:
                    break;
            }

            e.Handled = true;
            return;
        }

        CanvasPort? port = HitTestPort(world);

        // THE SECOND CLICK OF A TWO-CLICK CONNECTION (`E8-T34`).
        //
        // Asked for directly: dragging a wire from one port to another is precise work with the
        // button held, and on a trackpad it is worse than that. So a click on a port arms it, the
        // wire follows the pointer, and a click on a second port finishes the connection. The drag
        // is untouched and still works - this is an addition, not a replacement, because a drag is
        // what everybody who has used a node editor before will try first.
        if (_mode == InteractionMode.PendingWire && _dragSourcePort is { } armed)
        {
            // Taken before standing down, because standing down is what puts a lifted wire back
            // and this one is about to be dropped rather than abandoned.
            CanvasWire? lifted = _detachedWire;
            _detachedWire = null;

            StandDownPendingWire();

            if (lifted is { } detached)
            {
                // The second click of a LIFT lands the wire exactly where the release of a drag
                // does: on another port it moves, on the port it came from it goes back, and
                // anywhere else it goes.
                DropDetachedWire(detached, port);
                InvalidateVisual();

                if (port is not null)
                {
                    e.Handled = true;
                    return;
                }
            }
            else if (port is { } second && !PortEquals(second, armed))
            {
                TryConnect(armed, second);
                e.Handled = true;
                InvalidateVisual();

                return;
            }
            else
            {
                // A click on the armed port itself cancels and stops there; a click anywhere else
                // cancels and then does whatever that click would ordinarily have done, because a
                // pending wire must never swallow a selection.
                InvalidateVisual();

                if (port is not null)
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        if (port is not null)
        {
            _mode = InteractionMode.DraggingWire;
            _dragSourcePort = port;
            _dragWireWorldEnd = world;
            _dragStartWorld = world;
            _wireDragMoved = false;
            _dragOutcome = WireOutcome.Refused;
            _selectedWire = null;

            // Only remembered. Whether this press becomes a detach is decided by the pointer
            // moving, in OnPointerMoved, and nothing has changed in the graph yet.
            _detachCandidate = _graph.WireInto(port.Value);
            _detachedWire = null;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        // Before the node test, for the reason the slider is: the field lives inside the node's
        // bounds, so testing the node first would make clicking a value box drag the node.
        int field = HitTestField(world);
        if (field >= 0)
        {
            _selection.Clear();
            _selection.Add(field);
            _selectedWire = null;
            _selectedNote = null;
            _selectedGroup = null;
            _focusNode = field;

            RequestFieldEdit(field);

            e.Handled = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // BEFORE the node test, and that order is the whole of the interaction. A slider lives
        // inside its node's bounds, so testing the node first would mean every drag of a thumb
        // moved the node instead - which is the same class of mistake as testing a wire before a
        // node, and is why that one is tested last.
        int slider = HitTestSlider(world);
        if (slider >= 0)
        {
            _mode = InteractionMode.DraggingSlider;
            _sliderSlot = slider;
            _sliderMoved = false;

            // Selecting it too: a slider being dragged is the thing the user is working on, and
            // the properties panel showing its range while they drag is the point of having the
            // range on ports at all.
            _selection.Clear();
            _selection.Add(slider);
            _selectedWire = null;
            _selectedNote = null;
            _selectedGroup = null;
            _focusNode = slider;

            if (DragSlider(slider, world))
            {
                _sliderMoved = true;

                // Evaluated while dragging, not recorded while dragging. RecordEdit serialises the
                // whole document, so one undo entry per pointer move would be both a flooded undo
                // stack and a document written to a string sixty times a second.
                GraphChanged?.Invoke(this, new GraphEditedEventArgs(
                    "Set slider", affectsEvaluation: true, recordsUndo: false));
            }

            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        int node = HitTestNode(world);
        if (node >= 0)
        {
            bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || control;
            bool alreadySelected = _selection.Contains(node);

            // CONTROL ON AN ALREADY-SELECTED NODE DEFERS ITS DESELECTION UNTIL THE RELEASE
            // (`E8-T37`), AND THAT ONE LINE IS WHAT MAKES COPIES CHAIN.
            //
            // Control+click has always toggled a node out of the selection, and a copy made by
            // Control+drag lands *selected* — so pressing Control on the copy to drag another one
            // out of it deselected it, and the drag then had nothing to copy. The user had to click
            // away and click back between every copy, which is an extra click per node in exactly
            // the gesture that exists to avoid extra clicks.
            //
            // So the toggle waits: on a click it happens on release, on a drag it never happens.
            // Shift keeps toggling immediately, because Shift is only ever about the selection.
            if (additive)
            {
                if (!alreadySelected)
                {
                    _selection.Add(node);
                }
                else if (!control)
                {
                    _selection.Remove(node);
                }
            }
            else if (!alreadySelected)
            {
                _selection.Clear();
                _selection.Add(node);
            }

            // CONTROL ARMS A COPY; IT DOES NOT MAKE ONE.
            //
            // Armed rather than done, so the copy happens on the first movement: a Control+click
            // that never becomes a drag selects or deselects exactly as it always has, and a
            // Control+drag leaves the original behind and takes a copy with the pointer — which is
            // what Dynamo, Grasshopper and every drawing application do.
            _duplicateOnDrag = control;
            _deselectOnRelease = control && alreadySelected;

            _selectedWire = null;
            _selectedNote = null;
            _selectedGroup = null;
            _focusNode = node;
            _mode = InteractionMode.DraggingNodes;
            _dragTotalX = 0;
            _dragTotalY = 0;
            _nodeDragMoved = false;
            _nodeDragAnchorWorld = world;
            _nodePressPlain = !additive;
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
                AnnounceMove();
                return;

            case InteractionMode.DraggingNodes:
                // Measured against where the press landed, not against the last pointer move -
                // and in screen pixels, so the slop is the same physical distance at every zoom
                // (`E8-T53`).
                _nodeDragMoved = _nodeDragMoved
                    || (Math.Abs(world.X - _nodeDragAnchorWorld.X) * _transform.Zoom) > ClickSlopScreen
                    || (Math.Abs(world.Y - _nodeDragAnchorWorld.Y) * _transform.Zoom) > ClickSlopScreen;

                if (_duplicateOnDrag)
                {
                    DuplicateDraggedSelection();
                }

                MoveSelection(world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                _dragStartWorld = world;

                // `E8-T52`: announced and not merely redrawn. The block being dragged may be the
                // one an open editor is sitting on, and the editor is positioned in screen pixels.
                AnnounceMove();
                return;

            case InteractionMode.DraggingSlider when _sliderSlot >= 0:
                if (DragSlider(_sliderSlot, world))
                {
                    _sliderMoved = true;

                    // Runs the graph, records nothing. See GraphEditedEventArgs.RecordsUndo.
                    GraphChanged?.Invoke(this, new GraphEditedEventArgs(
                        "Set slider", affectsEvaluation: true, recordsUndo: false));
                    InvalidateVisual();
                }

                return;

            case InteractionMode.DraggingNote when _selectedNote is { } dragged:
                MoveNote(dragged, world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                _dragStartWorld = world;
                InvalidateVisual();
                return;

            case InteractionMode.DraggingGroup when _selectedGroup is { } group:
                MoveGroup(group, world.X - _dragStartWorld.X, world.Y - _dragStartWorld.Y);
                _dragStartWorld = world;

                // A group carries its member nodes, so this moves blocks too (`E8-T52`).
                AnnounceMove();
                return;

            case InteractionMode.Marquee:
                _marqueeEndWorld = world;
                InvalidateVisual();
                return;

            case InteractionMode.DraggingWire:
            case InteractionMode.PendingWire:
                // A press that has travelled further than a hand shakes is a drag, and a drag
                // ends where it is released rather than arming a second click.
                _wireDragMoved = _wireDragMoved
                    || (Math.Abs(world.X - _dragStartWorld.X) * _transform.Zoom) > ClickSlopScreen
                    || (Math.Abs(world.Y - _dragStartWorld.Y) * _transform.Zoom) > ClickSlopScreen;

                // THE MOMENT A PRESS ON A WIRED INPUT BECOMES A DETACH.
                //
                // The wire is lifted off the port and the drag continues from its SOURCE, so the
                // rubber band trails from the output the wire came from - which is what makes the
                // gesture read as picking a wire up rather than starting a new one. The graph is
                // still untouched: nothing is committed until the pointer is released, so a drag
                // that ends back where it started costs no edit and no undo entry.
                if (_wireDragMoved && _detachedWire is null && _detachCandidate is { } lifted)
                {
                    _detachedWire = lifted;
                    _dragSourcePort = lifted.From;

                    // The wire has to stop being drawn, or it stays pinned to the port the user
                    // is pulling it off. Clearing the visuals rebuilds them without it.
                    _wireVisuals.Clear();
                }

                _dragWireWorldEnd = world;
                _hoverPort = HitTestPort(world);
                _dragOutcome = EvaluateDrag(_dragSourcePort, _hoverPort);
                InvalidateVisual();
                return;

            default:
                break;
        }

        int node = HitTestNode(world);

        // `E8-T72`, CORRECTED BY `E8-T73`: THE HOVER SURVIVES THE JOURNEY TO THE BUBBLE.
        //
        // A bubble is drawn `PreviewGap` *below* its node and is not part of it, so moving down
        // onto the strip to press its toggle used to drop the hover - which took the bubble away
        // before the pointer arrived. `E8-T72` covered the bubble and not the gap between the two,
        // which is six world units of neither, and the client hit it on the first try.
        // `IsInPreviewReach` is the node, the gap and the bubble as one region.
        if (node < 0)
        {
            node = HitTestPreviewReach(world);
        }

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

            // Before the ordinary connect, because a detached wire is being MOVED and the two
            // halves of that - off the old port, onto the new one - have to land as one edit.
            case InteractionMode.DraggingWire when _detachedWire is { } detached:
                DropDetachedWire(detached, _hoverPort);
                _wireDragMoved = true;
                break;

            case InteractionMode.DraggingWire
                when _dragSourcePort is { } source && _hoverPort is { } target && !PortEquals(source, target):
                TryConnect(source, target);
                _wireDragMoved = true;
                break;

            // The whole drag becomes one undo step here, having already run the graph on every
            // move. Nothing is recorded when the value never actually changed - a click on the
            // thumb that lands back where it started is not an edit, for the same reason a node
            // dragged out and back is not.
            case InteractionMode.DraggingSlider when _sliderMoved:
                _sliderMoved = false;
                _sliderSlot = -1;
                GraphChanged?.Invoke(
                    this, new GraphEditedEventArgs("Set slider", affectsEvaluation: false));
                break;

            case InteractionMode.DraggingSlider:
                _sliderSlot = -1;
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

        // A CLICK ON A CODE BLOCK OPENS ITS EDITOR (`E8-T53`).
        //
        // Here rather than in OnDoubleTapped, and on the *release* rather than the press, because
        // a press is the start of a drag and a block has to stay draggable. A gesture that never
        // travelled past the slop is a click; one that did is a move, and it opens nothing.
        //
        // No modifier, and that is not caution: Control arms a copy (`E8-T37`) and Shift toggles
        // the selection, so both are gestures about *which nodes*, and neither is a request to
        // type into one.
        //
        // NARROWED BY `E8-T68`: NOT ON THE HEADER. The header is now the rename target, and the
        // first click of a double-click lands there - so a header that opened the source editor
        // would make the rename gesture unreachable on the one node kind whose body is text. The
        // block is still opened by a click anywhere else on it, which is all of it but 22 px.
        if (_mode is InteractionMode.DraggingNodes && !_nodeDragMoved && _nodePressPlain
            && _focusNode >= 0 && _focusNode < _graph.Nodes.Count
            && _graph.Nodes[_focusNode].Script is not null
            && !_graph.Nodes[_focusNode].IsInHeader(_nodeDragAnchorWorld.X, _nodeDragAnchorWorld.Y))
        {
            RequestScriptEdit(_focusNode);
        }

        // The deselection a Control+press on a selected node deferred. It happens only if the press
        // never became a drag — a drag was a copy, and a copy that deselected what it copied would
        // leave nothing to copy next time.
        if (_deselectOnRelease)
        {
            _deselectOnRelease = false;

            if (_mode is InteractionMode.DraggingNodes && _dragTotalX == 0 && _dragTotalY == 0
                && _focusNode >= 0 && _selection.Remove(_focusNode))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        _duplicateOnDrag = false;

        // A press and release on one port, with no travel in between, is a *click*: the wire stays
        // armed and waits for a second one. Every other release ends the interaction.
        bool armed = _mode is InteractionMode.DraggingWire && !_wireDragMoved && _dragSourcePort is not null;

        _mode = armed ? InteractionMode.PendingWire : InteractionMode.None;

        // A CLICK ON A WIRED INPUT LIFTS THE WIRE, EXACTLY AS A DRAG DOES.
        //
        // `E8-T49` lifted only on movement, and gave a reason: a click that removed a wire would be
        // an accident Escape could not undo. That reason belonged to a design where lifting *was*
        // removing. Nothing is committed until the gesture ends, so this commits nothing either -
        // the wire is off the port, following the pointer, and `StandDownPendingWire` puts it back
        // if the user presses Escape. Leaving the two gestures different was caution outliving its
        // cause, and the client found it immediately (`E8-T50`).
        if (armed && _detachCandidate is { } lifted && _detachedWire is null)
        {
            _detachedWire = lifted;
            _dragSourcePort = lifted.From;
            _wireVisuals.Clear();
        }

        if (!armed)
        {
            _dragSourcePort = null;
            _detachedWire = null;
        }

        // Cleared whatever happened: a candidate that outlived its press would lift a wire on the
        // NEXT gesture over a different port.
        _detachCandidate = null;

        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    /// <summary>
    /// Leaves the dragged nodes where they were and drags copies of them instead (`E8-T37`).
    /// </summary>
    /// <remarks>
    /// Called on the first movement rather than on the press, so a Control+click that never becomes
    /// a drag copies nothing. The copies land exactly on top of the originals and are then moved by
    /// the same delta the drag would have applied, which is what makes the gesture read as *peeling
    /// one off* rather than as *a copy appeared somewhere*.
    /// </remarks>
    private void DuplicateDraggedSelection()
    {
        _duplicateOnDrag = false;

        IReadOnlyList<int> copies = _graph.Duplicate([.. _selection], 0, 0);

        if (copies.Count == 0)
        {
            return;
        }

        _selection.Clear();

        foreach (int slot in copies)
        {
            _selection.Add(slot);
        }

        _focusNode = copies[0];
        _wireVisuals.Clear();
        _indexDirty = true;

        GraphChanged?.Invoke(
            this, new GraphEditedEventArgs(Plural("Duplicate", copies.Count), affectsEvaluation: true));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether two ports are the same port.</summary>
    private static bool PortEquals(CanvasPort left, CanvasPort right) =>
        left.NodeIndex == right.NodeIndex
        && left.PortIndex == right.PortIndex
        && left.IsOutput == right.IsOutput;

    /// <summary>Cancels a wire that a click armed, leaving everything else alone.</summary>
    private void StandDownPendingWire()
    {
        _mode = InteractionMode.None;
        _dragSourcePort = null;
        _wireDragMoved = false;
        _dragOutcome = WireOutcome.Refused;

        // A WIRE LIFTED OFF A PORT AND THEN ABANDONED GOES BACK ON IT.
        //
        // Nothing was committed when it was lifted, so there is nothing to undo and nothing to
        // announce - the wire simply starts being drawn again. This is what makes Escape a real
        // cancel, and it is why a *click* is allowed to lift a wire at all: the caution that kept
        // the click from lifting one was written for a design that removed the wire immediately.
        if (_detachedWire is not null)
        {
            _detachedWire = null;
            _wireVisuals.Clear();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Double-clicking empty canvas asks the shell for a code block there (`E8-T27`), and
    /// double-clicking a code block opens the editor over it (`E8-T39`) — the same gesture, in and
    /// out. On any other node, a port or a wire it still does nothing.
    ///
    /// <para>
    /// <b>A single click opens it too (`E8-T53`), and this stays.</b> The second click of a double
    /// lands on the editor the first one opened, so the double-click path is usually dead — but
    /// *usually* is not *always*, and a gesture that has worked since `E8-T39` should not stop
    /// working because a faster one arrived. Opening an editor that is already open over the same
    /// block is idempotent.
    /// </para>
    /// </remarks>
    protected override void OnDoubleTapped(Avalonia.Input.TappedEventArgs e)
    {
        base.OnDoubleTapped(e);

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);

        if (HitTestPort(world) is not null)
        {
            return;
        }

        if (HitTestNode(world) is int node && node >= 0)
        {
            // `E8-T68`. THE HEADER IS CHECKED FIRST, AND IT WINS ON A CODE BLOCK TOO.
            //
            // The header is where the title is drawn, so it is where a double-click meaning
            // *rename this* has to land; a rule with an exception for one node kind is a rule
            // nobody can learn. What it costs is the top 22 px of a block, which is the one band
            // of a block that is not source.
            if (_graph.Nodes[node].IsInHeader(world.X, world.Y))
            {
                e.Handled = true;
                RequestTitleEdit(node);

                return;
            }

            // `E8-T39`. Anywhere on a code block, not only inside the source rectangle: the
            // node is almost entirely source, and a double-click that lands two pixels into the
            // padding and does nothing reads as the gesture not working.
            if (_graph.Nodes[node].Script is not null)
            {
                e.Handled = true;
                RequestScriptEdit(node);
            }

            return;
        }

        if (HitTestWire(world) is not null)
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
        CodeBlockRequested?.Invoke(this, new CanvasCreateRequestedEventArgs(world.X, world.Y, screen.X, screen.Y));
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Point screen = e.GetPosition(this);
        double factor = Math.Pow(1.15, e.Delta.Y);
        _transform.ZoomAbout(factor, screen.X, screen.Y);
        e.Handled = true;
        AnnounceMove();
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

            // Before the selection, because a pending wire is the more recent thing the user
            // started and is the one they mean to abandon.
            case Key.Escape when _mode is InteractionMode.PendingWire:
                StandDownPendingWire();
                InvalidateVisual();
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

        _liveFrame = true;
        _evaluatingTravelled = false;
        RenderScene(context, new Rect(Bounds.Size), ShowFrameStatistics);
        _liveFrame = false;

        // `E3-T14`: THE ONLY REPEATING ANIMATION, AND IT ASKS FOR FRAMES ONLY WHILE IT RUNS. A canvas
        // with nothing evaluating, or zoomed out past the animation budget, draws once and rests.
        LastFrameAnimatedEvaluation = _evaluatingTravelled;

        if (_evaluatingTravelled)
        {
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => InvalidateVisual());
        }

        Frames.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>
    /// Draws the graph into a rectangle of a given size (<c>E8-T69</c>).
    /// </summary>
    /// <param name="context">Where to draw.</param>
    /// <param name="bounds">The rectangle to draw into, at its origin.</param>
    /// <param name="statistics">Whether to draw the frame-rate overlay.</param>
    /// <remarks>
    /// <b>Extracted from <see cref="Render"/> so an export can ask for a size the control does not
    /// have.</b> Everything here used to read <c>Bounds</c> directly, which made the on-screen size
    /// the only size the graph could be drawn at — and the whole of an image export is drawing the
    /// same scene into a different rectangle. The overlay is a parameter rather than a field read
    /// for the same reason: a frame counter belongs on a screen and not in a file somebody is
    /// going to put in a document.
    /// </remarks>
    private void RenderScene(DrawingContext context, Rect bounds, bool statistics)
    {
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

        if (statistics)
        {
            DrawFrameStatistics(context, bounds);
        }
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
            // The node's own category, unless the user chose another one's colour for it
            // (`E8-T35`). Hover, desaturation and every level of detail follow from this one
            // value, so a recoloured node behaves exactly like a node of that category.
            Color categoryColour = hovered
                ? NodeCategoryColours.HoverColourOf(node.DisplayCategory)
                : NodeCategoryColours.ColourOf(node.DisplayCategory);

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
                // Below 40% the header fill is the whole node. It used to carry the library
                // category and now carries only what a user chose to mark it with (`E8-T38`), so
                // most of a graph is grey at this zoom - which is the trade §7.2 records. Every
                // fill it can be clears 3:1 against the canvas on its own.
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

            if (drawsTitle)
            {
                DrawBodyDivide(context, pens, node, rounded);
            }

            // Header: full-strength category colour with dark text (Decision V2). Clipped rather
            // than drawn as a separately-rounded rectangle so the top corners match the body's
            // radius exactly.
            // The same rectangle the rename gesture hit-tests against and the title editor is
            // placed by (`E8-T68`), so the target cannot drift away from where the title is drawn.
            node.HeaderBox(out double headerX, out double headerY, out double headerWidth, out double headerHeight);
            Rect headerRect = new(headerX, headerY, headerWidth, headerHeight);
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
                FormattedText title = HeaderRun(node.DisplayTitle);
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

            if (node.HasSlider)
            {
                DrawSlider(context, node, slot, drawsPortLabels);
            }

            if (node.HasField)
            {
                DrawField(context, pens, node, slot, drawsPortLabels);
            }

            if (node.Script is not null)
            {
                DrawScript(context, pens, node, drawsPortLabels);
            }

            DrawStateRings(context, pens, node, nodeRect, selected);

            if (slot == _focusNode && IsFocused)
            {
                DrawFocusSandwich(context, pens, nodeRect);
            }
        }
    }

    /// <summary>
    /// Splits a node's body into its input and output halves (<c>E8-T67</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked for by the client</b>: a faint line down the middle of a node, and a very small
    /// difference in tint either side of it. What it says is that the two columns of a node mean
    /// different things — the left edge is where wires arrive and the right edge is where they
    /// leave — which is the first thing somebody has to learn about a node and the one thing the
    /// drawing never said.
    /// </para>
    /// <para>
    /// <b>Drawn immediately after the body and before everything else on the node.</b> The header,
    /// the port tabs, the slider, the value field and a code block's source all go over the top of
    /// it, which is what makes this a wash on the body rather than a thing competing with what is
    /// on the body. On a code block the source covers nearly all of it, and that is correct: a
    /// block's body is one column of text, not two columns of ports.
    /// </para>
    /// <para>
    /// <b>Clipped to the rounded body rather than drawn as a plain rectangle</b>, so the
    /// bottom-right corner stays round. The wash is the same shape as the node, seen through the
    /// half of it that is being tinted.
    /// </para>
    /// <para>
    /// <b>Above 67% zoom only</b>, which is the level at which the header title appears and, not
    /// coincidentally, the level at and below which the body lerps towards the category colour
    /// (§7.3). Below it there are no port labels to separate, the tint would land on a brightened
    /// fill, and a hairline is texture rather than structure.
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="pens">The frame's pens, for the hairline.</param>
    /// <param name="node">The node.</param>
    /// <param name="rounded">The node's rounded body, so the tint keeps its corners.</param>
    private static void DrawBodyDivide(
        DrawingContext context, in FramePens pens, CanvasNode node, in RoundedRect rounded)
    {
        node.BodyDivide(out double middle, out double top, out double bottom);

        if (bottom <= top)
        {
            return;
        }

        double right = node.X + node.Width;

        if (right > middle)
        {
            using (context.PushClip(new Rect(middle, top, right - middle, bottom - top)))
            {
                context.DrawRectangle(SparkPalette.NodeOutputTintBrush, null, rounded);
            }
        }

        context.DrawLine(pens.BodyDivide, new Point(middle, top), new Point(middle, bottom));
    }

    /// <summary>
    /// Draws a code block's source on the node itself (<c>E8-T39</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Drawn, not hosted.</b> The canvas is immediate-mode and holds no children
    /// ([ADR-0013](../../docs/adr/0013-immediate-mode-node-canvas.md)) — so what is on screen for
    /// every block but one is a picture of the source, and the block being typed into gets a real
    /// editor put over this exact rectangle by the pane above. That is what keeps a graph of a
    /// hundred code blocks the same cost as a graph of a hundred anything else.
    /// </para>
    /// <para>
    /// <b>No syntax colouring here, deliberately.</b> Highlighting is a lexer's answer and the
    /// editor already has one; running a second one in the draw loop would be a second opinion
    /// about what a token is, on the surface where a hundred nodes are competing for the frame.
    /// The text is drawn in one colour, and the editor supplies the colours the moment anyone
    /// looks closely enough to type.
    /// </para>
    /// <para>
    /// <b>It goes below the same threshold as every other 10-to-11 px label</b> (§7.3). Source at
    /// 30% zoom is a grey smear that costs a text layout per line, and a block zoomed that far out
    /// is a shape in a graph rather than something being read.
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="pens">The frame's pens, for the source area's own outline.</param>
    /// <param name="node">The node.</param>
    /// <param name="drawsLabels">Whether the zoom is high enough to draw text at all.</param>
    private void DrawScript(DrawingContext context, in FramePens pens, CanvasNode node, bool drawsLabels)
    {
        node.ScriptBox(out double x, out double y, out double width, out double height);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        Rect box = new(x, y, width, height);

        // The sunken ground is drawn at every zoom, labels or not: it is what says *this node is
        // written in* at the size where the words themselves are gone.
        context.DrawRectangle(
            SparkPalette.Frozen(SparkPalette.SurfaceSunken), pens.NodeOutline, box, 3, 3);

        if (!drawsLabels || node.Script is not { Length: > 0 } source)
        {
            return;
        }

        // THE NOTE ABOVE THE SOURCE (`E8-T65`).
        //
        // The client wrote seven `new Circle(...)` lines, got one output port, and said the rule
        // was fine but that the block should say so itself. It is drawn outside the sunken ground,
        // in the band `ScriptHintBox` reserves, so the editor laid over the source cannot cover it.
        node.ScriptHintBox(out double hintX, out double hintY, out double hintWidth, out double hintHeight);

        if (hintWidth > 0 && hintHeight > 0)
        {
            using (context.PushClip(new Rect(hintX, hintY, hintWidth, hintHeight)))
            {
                context.DrawText(HintRun(ScriptHint), new Point(hintX + CanvasNode.ScriptGap, hintY));
            }
        }

        using (context.PushClip(box))
        {
            double line = y + CanvasNode.ScriptPadding;
            int drawn = 0;

            double gutter = node.ScriptGutterWidth;
            double codeLeft = x + CanvasNode.ScriptGap + gutter;
            double numberRight = codeLeft - CanvasNode.ScriptGap;

            IReadOnlyList<FormattedText> coloured = ColouredScript(source);

            foreach (string text in source.ReplaceLineEndings("\n").Split('\n'))
            {
                if (drawn == node.ScriptLineCount)
                {
                    break;
                }

                drawn++;

                // Right-aligned, the way an editor's gutter is: the ones column lines up, so a
                // ten-line block does not step its numbers sideways at line 10.
                FormattedText number = NumberRun(drawn.ToString(CultureInfo.InvariantCulture));
                context.DrawText(number, new Point(numberRight - number.Width, line));

                if (text.Length > 0)
                {
                    context.DrawText(
                        drawn <= coloured.Count ? coloured[drawn - 1] : ScriptRun(text),
                        new Point(codeLeft, line));
                }

                line += CanvasNode.ScriptLineHeight;
            }
        }
    }

    /// <summary>
    /// Draws a slider node's track, its thumb and its current value (<c>E8-T25</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole point of a slider is sweeping a value and watching the geometry answer</b>,
    /// which is the one thing a text box in a side panel cannot do however convenient it is. That
    /// is why this node kind earns a widget when no other does.
    /// </para>
    /// <para>
    /// <b>The filled part of the track carries the node's category colour and the rest does not.</b>
    /// Principle 4 of the design language says a category fill must never read as a state, so the
    /// unfilled remainder is drawn in a surface colour rather than a dimmed category one - a
    /// half-lit category colour is exactly the thing that reads as "disabled".
    /// </para>
    /// <para>
    /// <b>The number is drawn only when the port labels are.</b> It is 10 px text and it is
    /// governed by the same level-of-detail threshold as everything else that small (§7.3); a
    /// slider zoomed out far enough to lose its labels is a shape you drag, not a value you read.
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="node">The node.</param>
    /// <param name="slot">Its slot, for reading the literals.</param>
    /// <param name="drawsLabels">Whether the zoom is high enough for the value text.</param>
    private void DrawSlider(DrawingContext context, CanvasNode node, int slot, bool drawsLabels)
    {
        node.SliderTrack(out double left, out double right, out double y);

        bool live = _graph.SliderRange(
            slot, out double value, out double minimum, out double maximum, out double step);

        double half = CanvasNode.SliderTrackHeight / 2;

        // The whole track first, then the filled part over it. Drawn as two rectangles rather than
        // as a line and a line, so the ends stay square against the node's own geometry.
        context.FillRectangle(
            SparkPalette.Frozen(SparkPalette.SurfaceSunken),
            new Rect(left, y - half, Math.Max(right - left, 0), CanvasNode.SliderTrackHeight),
            (float)half);

        if (!live)
        {
            // An impossible range - inverted, empty, or a literal that is not a number - is drawn
            // as a dead track with no thumb. Dragging it does nothing, which is what an impossible
            // range should feel like, and it is visibly different from a slider at zero.
            return;
        }

        double fraction = Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        double thumbX = left + (fraction * (right - left));

        context.FillRectangle(
            SparkPalette.Frozen(NodeCategoryColours.ColourOf(node.Category)),
            new Rect(left, y - half, Math.Max(thumbX - left, 0), CanvasNode.SliderTrackHeight),
            (float)half);

        context.DrawEllipse(
            SparkPalette.Frozen(SparkPalette.TextPrimary),
            pen: null,
            new Point(thumbX, y),
            CanvasNode.SliderThumbRadius,
            CanvasNode.SliderThumbRadius);

        if (!drawsLabels)
        {
            return;
        }

        // Rendered to as many decimals as the step justifies and no more. A step of 1 showing
        // "40.00000" is noise, and a step of 0.01 showing "40" is a lie about where the thumb is.
        FormattedText text = TypeRun(FormatSliderValue(value, step));

        context.DrawText(
            text,
            new Point(
                Math.Clamp(thumbX - (text.Width / 2), node.X + 4, node.X + node.Width - 4 - text.Width),
                y + CanvasNode.SliderThumbRadius + 1));
    }

    /// <summary>
    /// Draws a node's in-place value field: a sunken box with the literal in it (<c>E8-T5</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An input node whose value lives in a side panel is a node you cannot read.</b> Six
    /// numbers in a graph are six identical boxes labelled <c>Number.Value</c>, and finding which
    /// one is the wall height means clicking each in turn.
    /// </para>
    /// <para>
    /// <b>A wired input shows no field.</b> The wire wins over the literal everywhere else in
    /// Spark and does here: a box offering a value that would then be ignored is worse than no box.
    /// The port row above still says the port is there and wired.
    /// </para>
    /// <para>
    /// It is drawn on <c>surface.sunken</c>, the token the design language names for *inset wells:
    /// text fields* — the same ground the code editor uses, so a place you can type into looks the
    /// same everywhere in the application.
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="pens">The frame's pens.</param>
    /// <param name="node">The node.</param>
    /// <param name="slot">Its slot, for reading the literal.</param>
    /// <param name="drawsLabels">Whether the zoom is high enough for the value text.</param>
    private void DrawField(
        DrawingContext context, in FramePens pens, CanvasNode node, int slot, bool drawsLabels)
    {
        if (_graph.IsInputWired(slot, 0))
        {
            return;
        }

        node.FieldBox(out double x, out double y, out double width, out double height);

        Rect box = new(x, y, width, height);

        context.DrawRectangle(
            SparkPalette.Frozen(SparkPalette.SurfaceSunken), pens.NodeOutline, box, 3, 3);

        if (!drawsLabels || _graph.FieldText(slot) is not { } text || text.Length == 0)
        {
            return;
        }

        FormattedText run = LabelRun(text);

        // Clipped to the box rather than allowed to run over the node's edge. A long string is
        // commoner here than anywhere else on a node, because this is the one place a user types
        // arbitrary text onto the canvas.
        using (context.PushClip(box))
        {
            context.DrawText(run, new Point(x + 5, y + ((height - run.Height) / 2)));
        }
    }

    /// <summary>The slot whose in-place field is under a world point, or -1 (<c>E8-T5</c>).</summary>
    /// <param name="world">The point, in world coordinates.</param>
    /// <returns>The slot, or -1.</returns>
    private int HitTestField(Point world)
    {
        for (int slot = _graph.Nodes.Count - 1; slot >= 0; slot--)
        {
            CanvasNode node = _graph.Nodes[slot];

            if (!node.HasField || _graph.IsInputWired(slot, 0))
            {
                continue;
            }

            node.FieldBox(out double x, out double y, out double width, out double height);

            if (world.X >= x && world.X <= x + width && world.Y >= y && world.Y <= y + height)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>Asks the shell to put a real text box over a node's field.</summary>
    /// <param name="slot">The node's slot.</param>
    private void RequestFieldEdit(int slot)
    {
        if (_graph.FieldText(slot) is not { } text)
        {
            return;
        }

        _graph.Nodes[slot].FieldBox(out double x, out double y, out double width, out double height);

        Point topLeft = new(_transform.ToScreenX(x), _transform.ToScreenY(y));
        Point bottomRight = new(
            _transform.ToScreenX(x + width), _transform.ToScreenY(y + height));

        FieldEditRequested?.Invoke(this, new CanvasFieldEditEventArgs(
            slot,
            text,
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y));
    }

    /// <summary>
    /// Asks the pane to put a text box over a node's title (<c>E8-T68</c>).
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <remarks>
    /// <b>The rectangle is <see cref="CanvasNode.HeaderBox"/></b>, which is the same rectangle the
    /// title was drawn in — so the editor opens over the word rather than near it, and committing
    /// puts the new word back in the same place.
    /// </remarks>
    public void RequestTitleEdit(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        CanvasNode node = _graph.Nodes[slot];
        node.HeaderBox(out double x, out double y, out double width, out double height);

        Point topLeft = new(_transform.ToScreenX(x), _transform.ToScreenY(y));
        Point bottomRight = new(_transform.ToScreenX(x + width), _transform.ToScreenY(y + height));

        TitleEditRequested?.Invoke(this, new CanvasFieldEditEventArgs(
            slot,
            node.DisplayTitle,
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y));
    }

    /// <summary>
    /// Renames a node from the in-place editor, as one undo step (<c>E8-T68</c>).
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <param name="text">What the user typed. Blank, or the definition's own name, clears it.</param>
    /// <remarks>
    /// <para>
    /// <b>Typing the definition's own name back is a reset, not a custom title.</b> A node called
    /// <c>Math.Sin</c> whose <see cref="CanvasNode.CustomTitle"/> is the string
    /// <c>"Math.Sin"</c> looks identical and behaves differently: it is a renamed node, so it is
    /// written into the document and survives the definition being renamed underneath it. The
    /// user who typed it meant <i>put it back</i>.
    /// </para>
    /// <para>
    /// <b>The structure is refreshed as well as the frame</b>, because a longer name is a wider
    /// node (<c>E8-T35</c>) and the spatial index was built from the old width.
    /// </para>
    /// </remarks>
    public void CommitNodeTitle(int slot, string? text)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        CanvasNode node = _graph.Nodes[slot];

        string? renamed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        if (string.Equals(renamed, node.Title, StringComparison.Ordinal))
        {
            renamed = null;
        }

        if (string.Equals(renamed, node.CustomTitle, StringComparison.Ordinal))
        {
            return;
        }

        node.CustomTitle = renamed;

        RefreshStructure();
        GraphChanged?.Invoke(
            this,
            new GraphEditedEventArgs(
                renamed is null ? "Reset node name" : "Rename node", affectsEvaluation: false));
        AnnounceMove();
    }

    /// <summary>
    /// Asks the pane to put a real code editor over a code block's source (<c>E8-T39</c>).
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <remarks>
    /// <b>The rectangle is the one the source was drawn in</b>, through the same
    /// <see cref="CanvasNode.ScriptBox"/> the renderer used — so the editor opens over the words
    /// rather than near them, and closing it puts the same words back in the same place.
    /// </remarks>
    public void RequestScriptEdit(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count || _graph.Nodes[slot].Script is not { } source)
        {
            return;
        }

        _graph.Nodes[slot].ScriptBox(out double x, out double y, out double width, out double height);

        Point topLeft = new(_transform.ToScreenX(x), _transform.ToScreenY(y));
        Point bottomRight = new(_transform.ToScreenX(x + width), _transform.ToScreenY(y + height));

        ScriptEditRequested?.Invoke(this, new CanvasFieldEditEventArgs(
            slot,
            source,
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y));
    }

    /// <summary>
    /// Makes room on a code block for an editor of a given screen size, and says where it goes
    /// (<c>E8-T40</c>).
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <param name="screenWidth">The width the editor needs, in screen pixels.</param>
    /// <param name="screenHeight">The height it needs.</param>
    /// <param name="x">The left edge of the rectangle to place it at, in control coordinates.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="width">Its width, which is at least <paramref name="screenWidth"/>.</param>
    /// <param name="height">Its height, which is at least <paramref name="screenHeight"/>.</param>
    /// <returns>False when the slot is not a code block, in which case nothing was reserved.</returns>
    /// <remarks>
    /// <para>
    /// <b>The caller says what the editor needs and this decides where it goes</b>, which is the
    /// only division of labour that works here: the editor's metrics belong to the pane that
    /// hosts it, and the node's geometry belongs to the canvas that draws it. The pane asking for
    /// a rectangle and then quietly making it bigger is what covered the port tabs.
    /// </para>
    /// <para>
    /// <b>Screen pixels go in and the reservation is in world units</b>, divided by the zoom —
    /// which is what makes the request mean <i>this many pixels once it is drawn</i> rather than
    /// this many world units, and is why zooming out grows the block rather than shrinking the
    /// editor into it.
    /// </para>
    /// </remarks>
    public bool ScriptEditorSpace(
        int slot,
        double screenWidth,
        double screenHeight,
        out double x,
        out double y,
        out double width,
        out double height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;

        if (slot < 0 || slot >= _graph.Nodes.Count || _graph.Nodes[slot].Script is null)
        {
            return false;
        }

        CanvasNode node = _graph.Nodes[slot];

        node.ReserveScriptSpace(screenWidth / _transform.Zoom, screenHeight / _transform.Zoom);

        // The node's bounds changed, so the spatial index was built from a shape that no longer
        // exists - and hit-testing reads the index, not the nodes.
        RefreshStructure();

        node.ScriptBox(out double worldX, out double worldY, out double worldWidth, out double worldHeight);

        x = _transform.ToScreenX(worldX);
        y = _transform.ToScreenY(worldY);
        width = _transform.ToScreenX(worldX + worldWidth) - x;
        height = _transform.ToScreenY(worldY + worldHeight) - y;

        return true;
    }

    /// <summary>Gives back the room an editor reserved on a code block (<c>E8-T40</c>).</summary>
    /// <param name="slot">The node's slot.</param>
    /// <remarks>
    /// <b>Call this before committing, not after.</b> Committing an edit replaces the node's
    /// definition, which removes the node and puts a new one back — so a release aimed at it
    /// afterwards either does nothing or, if the slots have moved, shrinks somebody else.
    /// </remarks>
    public void EndScriptEdit(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        _graph.Nodes[slot].ReserveScriptSpace(0, 0);
        RefreshStructure();
    }

    /// <summary>
    /// Commits text typed into an in-place field, and reports it if it changed (<c>E8-T5</c>).
    /// </summary>
    /// <param name="slot">The node's slot.</param>
    /// <param name="text">What was typed.</param>
    /// <remarks>
    /// Public because the control that hosted the editing is the pane, not the canvas — the canvas
    /// named the rectangle and has heard nothing since.
    /// </remarks>
    public void CommitFieldText(int slot, string? text)
    {
        if (!_graph.SetFieldText(slot, text))
        {
            return;
        }

        GraphChanged?.Invoke(this, new GraphEditedEventArgs("Set value", affectsEvaluation: true));
        InvalidateVisual();
    }

    /// <summary>Renders a slider's value to as many decimals as its step justifies.</summary>
    /// <param name="value">The value.</param>
    /// <param name="step">The step, or zero for a continuous slider.</param>
    /// <returns>The text.</returns>
    private static string FormatSliderValue(double value, double step)
    {
        int decimals = 2;

        if (step > 0 && double.IsFinite(step))
        {
            decimals = 0;
            double scaled = step;

            while (decimals < 6 && Math.Abs(scaled - Math.Round(scaled)) > 1e-9)
            {
                scaled *= 10;
                decimals++;
            }
        }

        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The slot whose slider thumb or track is under a world point, or -1 (<c>E8-T25</c>).
    /// </summary>
    /// <remarks>
    /// <b>The whole track is a target, not only the thumb.</b> A six-pixel disc is a hard thing to
    /// hit with a mouse and an unreasonable one with a trackpad, and clicking a track to jump the
    /// value there is what every slider does. The band is deliberately taller than the track it is
    /// drawn as, for the reason ports have a screen-space hit size larger than their disc.
    /// </remarks>
    /// <param name="world">The point, in world coordinates.</param>
    /// <returns>The slot, or -1.</returns>
    private int HitTestSlider(Point world)
    {
        // Front to back, so the node drawn on top wins - the same order HitTestNode uses.
        for (int slot = _graph.Nodes.Count - 1; slot >= 0; slot--)
        {
            CanvasNode node = _graph.Nodes[slot];

            if (!node.HasSlider)
            {
                continue;
            }

            node.SliderTrack(out double left, out double right, out double y);

            double reach = CanvasNode.SliderThumbRadius + 3;

            if (world.X >= left - reach
                && world.X <= right + reach
                && world.Y >= y - reach
                && world.Y <= y + reach)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>
    /// Moves a slider's value to wherever the pointer is along its track (<c>E8-T25</c>).
    /// </summary>
    /// <param name="slot">The slider's slot.</param>
    /// <param name="world">The pointer, in world coordinates.</param>
    /// <returns>True when the value actually changed.</returns>
    /// <remarks>
    /// <b>Snapping is done here as well as in the node.</b> The node clamps and snaps because its
    /// value port can also be wired or typed into; this snaps so that the thumb lands where the
    /// value will actually be, rather than sliding smoothly and jumping on release.
    /// </remarks>
    private bool DragSlider(int slot, Point world)
    {
        if (!_graph.SliderRange(slot, out _, out double minimum, out double maximum, out double step))
        {
            return false;
        }

        _graph.Nodes[slot].SliderTrack(out double left, out double right, out _);

        double span = right - left;
        double fraction = span > 0 ? Math.Clamp((world.X - left) / span, 0, 1) : 0;
        double value = minimum + (fraction * (maximum - minimum));

        if (step > 0 && double.IsFinite(step))
        {
            value = minimum + (Math.Round((value - minimum) / step) * step);
        }

        return _graph.SetSliderValue(slot, Math.Clamp(value, minimum, maximum));
    }

    private void DrawPorts(DrawingContext context, in FramePens pens, CanvasNode node, int slot, CanvasDetail detail)
    {
        // Below 67% ports are 2 px screen-space dots; above it they are drawn at their design size
        // and grow on hover. The hit target never shrinks below 10 px of screen space regardless
        // (§7.6) — ports are the smallest thing anyone has to aim at in the product.
        double zoom = _transform.Zoom;
        double radius = detail <= CanvasDetail.Fill ? 1.0 / zoom : PortRadius;

        bool shaped = detail >= CanvasDetail.Lip;

        // The tabs are drawn at any detail that draws port names at all. Below that a node is a
        // silhouette and its ports are the dots §7.6 asks for, because a lozenge with no room for
        // a word in it is a rectangle that means nothing.
        bool tabs = CanvasLevelOfDetail.DrawsPortLabels(detail);

        for (int i = 0; i < node.Inputs.Count; i++)
        {
            // `E8-T25`: a port that takes no wire has no disc and no tab, because both of them are
            // an invitation to aim a wire at it.
            if (node.InputRow(i) < 0)
            {
                continue;
            }

            node.InputPortCenter(i, out double x, out double y);
            CanvasPort port = new(slot, i, IsOutput: false);

            if (tabs)
            {
                DrawPortTab(context, pens, node, port, i);
            }

            DrawPort(context, pens, port, x, y, radius, shaped ? node.Inputs[i].DeclaredRank : 0);
        }

        for (int i = 0; i < node.Outputs.Count; i++)
        {
            node.OutputPortCenter(i, out double x, out double y);
            CanvasPort port = new(slot, i, IsOutput: true);

            if (tabs)
            {
                DrawPortTab(context, pens, node, port, i);
            }

            DrawPort(context, pens, port, x, y, radius, shaped ? node.Outputs[i].DeclaredRank : 0);
        }
    }

    /// <summary>
    /// Draws a port as the lozenge that carries its name (`E8-T36`).
    /// </summary>
    /// <remarks>
    /// <b>The tab is the target, and it is drawn so that it looks like one.</b> A connected port
    /// fills in <c>port.connected</c>, an unconnected one sits on a raised surface, and the hovered
    /// one takes the accent outline every other hoverable thing on this canvas takes — so the thing
    /// a user is about to click is the thing that lit up, which is the whole reason to draw a port
    /// bigger than a dot.
    /// </remarks>
    private void DrawPortTab(
        DrawingContext context, in FramePens pens, CanvasNode node, CanvasPort port, int index)
    {
        node.PortTab(index, port.IsOutput, out double left, out double top, out double right, out double bottom);

        Rect rect = new(left, top, right - left, bottom - top);
        RoundedRect rounded = new(rect, (bottom - top) / 2);

        bool hovered = _hoverPort == port;
        bool connected = _connectedPorts.Contains(port);

        // An INSET WELL, not a raised chip. A port is a socket - something a wire goes into - and
        // `surface.sunken` is the token the design language names for exactly that (§7.1), so a
        // port reads as a hole in the node rather than as a button on it. It is also the ground
        // `text.primary` is measured against, which is what keeps the name inside it legible.
        IBrush fill = connected
            ? SparkPalette.Frozen(SparkPalette.Mix(SparkPalette.SurfaceSunken, SparkPalette.PortConnected, 0.45))
            : SparkPalette.Frozen(SparkPalette.SurfaceSunken);

        context.DrawRectangle(fill, hovered ? pens.AccentThin : null, rounded);

        string name = port.IsOutput ? node.Outputs[index].Name : node.Inputs[index].Name;

        if (name.Length == 0)
        {
            return;
        }

        FormattedText run = LabelRun(name);

        // Clipped to the tab, so a name too long for the lozenge is cut by it rather than running
        // out across the node's own body.
        using (context.PushClip(rect))
        {
            double x = port.IsOutput
                ? right - PortTabTextInset - run.Width
                : left + PortTabTextInset;

            context.DrawText(run, new Point(x, ((top + bottom) / 2) - (run.Height / 2)));
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
    /// The type is what turns a port from a word into an instruction: <c>center</c> says where the
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
        int rows = Math.Max(node.VisibleInputCount, node.Outputs.Count);

        for (int row = 0; row < rows; row++)
        {
            double y = node.Y + CanvasNode.HeaderHeight + (CanvasNode.PortPitch * (row + 0.5));

            // BOTH NAMES ARE DRAWN INSIDE THEIR TABS (`E8-T36`), so nothing here draws a name —
            // this method places the types in what the tabs leave behind.
            //
            // The output branch used to draw the name a SECOND time and take its right-hand edge
            // from that text's width. The tab holding the name reaches `PortTabPadding` further
            // left again, so the type was placed clear of the word and painted over the lozenge:
            // on `Math.Divide` the type ended 4 px inside the `result` tab. Asking the node for the
            // row's free span puts both edges on the same geometry the tabs are drawn with.
            node.PortLabelRow(row, out double leftEnd, out double rightStart);

            if (!types)
            {
                continue;
            }

            // A CODE BLOCK DRAWS NO TYPE LABELS, AND THE SPACE IS THE REASON (`E6-T29`).
            //
            // On an ordinary node the span between the two tabs is empty and a type label is the
            // best thing that could be in it. On a block that span *is the source*, so a label
            // there is drawn over the user's code - which is what the client saw the moment
            // `E6-T29` gave these ports real types to report.
            //
            // It costs nothing, which is the other half of the argument: since `E6-T29` the port's
            // NAME is its kind, so a port called `integer` was being labelled `integer` and one
            // called `string` was being labelled `text`. The rank pip on the port still says what
            // the type could not, and the properties pane still says the rest.
            if (node.Script is not null)
            {
                continue;
            }

            // `E8-T70`: EACH SIDE HAS ITS OWN ROOM, BOUNDED BY THE DIVIDE.
            //
            // The two labels used to share one span between the two tabs, so a long input type
            // spent the output's budget as well as its own and was drawn across the middle of the
            // node. Since `E8-T67` that middle is a line saying *inputs here, outputs there*, and a
            // label crossing it says the opposite. `WidestRow` sizes each half to hold its own
            // label; this is the guard that holds when the font measures wider than the estimate
            // the node was sized from (N24), and it drops a label rather than crossing the line.
            node.PortTypeRoom(row, out double inputRoom, out double outputRoom);

            if (node.InputAtRow(row) is int input and >= 0
                && node.Inputs[input].TypeName is { } inputType)
            {
                FormattedText run = TypeRun(inputType);
                if (inputRoom >= TypeGap + run.Width + MinimumRowGap)
                {
                    context.DrawText(run, new Point(leftEnd + TypeGap, y - (run.Height / 2)));
                }
            }

            // An output name is right-aligned, so its type goes to its left.
            if (row < node.Outputs.Count && node.Outputs[row].TypeName is { } outputType)
            {
                FormattedText run = TypeRun(outputType);
                if (outputRoom >= TypeGap + run.Width + MinimumRowGap)
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

        CanvasNode node = _graph.Nodes[slot];

        // `E8-T72`: A PIN IS A FOURTH REASON, AND IT IS THE ONE THE CLIENT ASKED FOR.
        //
        // The other three are all about *now* - it is a watch, it is selected, the pointer is on
        // it - so there was no way to keep one value on screen while working somewhere else short
        // of wiring a `Watch` node into the graph. Pinning is that, without changing the graph.
        return node.ShowsValue || node.PreviewPinned || _selection.Contains(slot) || slot == _hoverNode;
    }

    /// <summary>
    /// The part of a preview bubble a world point lands on, and whose (<c>E8-T72</c>).
    /// </summary>
    /// <param name="world">The point, in world coordinates.</param>
    /// <param name="slot">The node whose bubble was hit, or -1.</param>
    /// <returns>Which part of it, or <see cref="CanvasPreviewPart.None"/>.</returns>
    /// <remarks>
    /// <b>Only bubbles that are on screen are hit, which is the same set that is drawn.</b> A
    /// bubble belongs to a node and is drawn under it, so the spatial index — which is built from
    /// node bounds — does not cover it; this walks the nodes instead. That is affordable because
    /// <see cref="ShowsPreview"/> is false for nearly all of them, and it is the honest version:
    /// the alternative is remembering where the last frame drew things, which is the defect this
    /// canvas already fixed once.
    /// </remarks>
    public CanvasPreviewPart HitTestPreview(Point world, out int slot)
    {
        for (int candidate = _graph.Nodes.Count - 1; candidate >= 0; candidate--)
        {
            CanvasNode node = _graph.Nodes[candidate];

            if (!node.HasPreview || !ShowsPreview(candidate) || !node.IsInPreview(world.X, world.Y))
            {
                continue;
            }

            slot = candidate;

            if (node.PreviewExpanded && Inside(node, world, pin: true))
            {
                return CanvasPreviewPart.Pin;
            }

            return Inside(node, world, pin: false)
                ? CanvasPreviewPart.Toggle
                : CanvasPreviewPart.Body;
        }

        slot = -1;
        return CanvasPreviewPart.None;

        static bool Inside(CanvasNode node, Point world, bool pin)
        {
            if (pin)
            {
                node.PreviewPinBox(out double x, out double y, out double width, out double height);

                return world.X >= x && world.X <= x + width && world.Y >= y && world.Y <= y + height;
            }

            node.PreviewToggleBox(out double tx, out double ty, out double tw, out double th);

            return world.X >= tx && world.X <= tx + tw && world.Y >= ty && world.Y <= ty + th;
        }
    }

    /// <summary>
    /// The node whose bubble — or the gap above it — is under a world point (<c>E8-T73</c>).
    /// </summary>
    /// <param name="world">The point, in world coordinates.</param>
    /// <returns>The node's slot, or -1.</returns>
    /// <remarks>
    /// <b>Only bubbles that are already on screen, which is what stops this being sticky.</b>
    /// <see cref="ShowsPreview"/> is true for the hovered node, so a node whose bubble the pointer
    /// is travelling towards keeps itself alive; a node with no bubble showing cannot be reached
    /// into, and the moment the pointer leaves the region the hover clears like any other.
    /// </remarks>
    private int HitTestPreviewReach(Point world)
    {
        for (int slot = _graph.Nodes.Count - 1; slot >= 0; slot--)
        {
            CanvasNode node = _graph.Nodes[slot];

            if (node.HasPreview && ShowsPreview(slot) && node.IsInPreviewReach(world.X, world.Y))
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>Opens or closes a node's preview bubble (<c>E8-T72</c>).</summary>
    /// <param name="slot">The node's slot.</param>
    /// <remarks>
    /// <b>Closing also unpins.</b> A pin keeps an <i>open</i> bubble, so a pinned bubble that was
    /// closed would be an invisible piece of state that made the node behave differently from its
    /// neighbours for no visible reason.
    /// </remarks>
    public void TogglePreview(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        CanvasNode node = _graph.Nodes[slot];
        node.PreviewExpanded = !node.PreviewExpanded;

        if (!node.PreviewExpanded)
        {
            node.PreviewPinned = false;
        }

        InvalidateVisual();
    }

    /// <summary>Pins or unpins a node's preview bubble (<c>E8-T72</c>).</summary>
    /// <param name="slot">The node's slot.</param>
    /// <remarks>
    /// <b>Pinning opens it, because a pin on a closed bubble would keep a word on screen.</b> The
    /// thing worth keeping is the value.
    /// </remarks>
    public void PinPreview(int slot)
    {
        if (slot < 0 || slot >= _graph.Nodes.Count)
        {
            return;
        }

        CanvasNode node = _graph.Nodes[slot];
        node.PreviewPinned = !node.PreviewPinned;

        if (node.PreviewPinned)
        {
            node.PreviewExpanded = true;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Draws a node's preview bubble: a strip, and the value underneath when it is open
    /// (<c>E8-T72</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dynamo's shape, asked for by the client with two screenshots of it.</b> The bubble used
    /// to be one thing that appeared whole and vanished whole; it is now a collapsed strip naming
    /// the type, a toggle that opens it onto the rank and the value, and a pin that keeps it open
    /// after the node stops being selected.
    /// </para>
    /// <para>
    /// <b>Every rectangle here comes from <see cref="CanvasNode"/> rather than from measured
    /// text</b>, which is what makes the toggle and the pin things a test can find. The bubble is
    /// the node's own width and the value wraps inside it; a value too long for
    /// <see cref="CanvasNode.PreviewMaximumLines"/> is clipped, and the properties pane is where
    /// the whole of it lives.
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="pens">The frame's pens.</param>
    /// <param name="node">The node.</param>
    private static void DrawPreview(DrawingContext context, in FramePens pens, CanvasNode node)
    {
        if (node.ResultSummary is not { Length: > 0 } summary)
        {
            return;
        }

        node.PreviewBox(out double boxX, out double boxY, out double boxWidth, out double boxHeight);

        Rect box = new(boxX, boxY, boxWidth, boxHeight);
        RoundedRect rounded = new(box, CornerRadius);

        context.DrawRectangle(SparkPalette.SurfaceFloatBrush, null, rounded);
        context.DrawRectangle(null, pens.NodeOutline, rounded);

        using (context.PushClip(box))
        {
            FormattedText label = new(
                node.PreviewLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                PortFontSize,
                SparkPalette.TextSecondaryBrush);

            context.DrawText(
                label,
                new Point(
                    box.X + CanvasNode.PreviewPadding,
                    box.Y + ((CanvasNode.PreviewRowHeight - label.Height) / 2)));

            if (node.PreviewExpanded)
            {
                node.PreviewPinBox(out double pinX, out double pinY, out double pinSize, out _);

                DrawGlyph(
                    context,
                    PreviewGlyphs.Pin,
                    pinX,
                    pinY,
                    pinSize,
                    node.PreviewPinned ? SparkPalette.AccentBrush : SparkPalette.TextMutedBrush);
            }

            node.PreviewToggleBox(out double toggleX, out double toggleY, out double toggleSize, out _);

            DrawGlyph(
                context,
                node.PreviewExpanded ? PreviewGlyphs.Collapse : PreviewGlyphs.Expand,
                toggleX,
                toggleY,
                toggleSize,
                SparkPalette.TextMutedBrush);

            if (!node.PreviewExpanded)
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
                MaxTextWidth = Math.Max(1, box.Width - (2 * CanvasNode.PreviewPadding)),
            };

            double line = box.Y + CanvasNode.PreviewRowHeight;

            context.DrawText(rank, new Point(box.X + CanvasNode.PreviewPadding, line));
            context.DrawText(
                value,
                new Point(box.X + CanvasNode.PreviewPadding, line + CanvasNode.PreviewLineHeight));
        }
    }

    /// <summary>Draws one 16-unit glyph path scaled into a box (<c>E8-T72</c>).</summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="glyph">The path, authored in <see cref="PreviewGlyphs.DesignSize"/> units.</param>
    /// <param name="x">The box's left edge, in world coordinates.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="size">Its side length.</param>
    /// <param name="brush">What to fill it with.</param>
    private static void DrawGlyph(
        DrawingContext context, Avalonia.Media.Geometry glyph, double x, double y, double size, IBrush brush)
    {
        double scale = size / PreviewGlyphs.DesignSize;

        using (context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
        {
            context.DrawGeometry(brush, null, glyph);
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
            // The halo first, so the crisp rings above draw over it rather than under it. It is
            // inflated less than the error and warning rings so it hugs the node: a halo that sat
            // outside them would separate a node from its own state stroke.
            RoundedRect halo = new(nodeRect.Inflate(2.5 / zoom), CornerRadius + (2.5 / zoom));
            context.DrawRectangle(null, pens.SelectionHalo, halo);

            RoundedRect ring = new(nodeRect.Inflate(1.5 / zoom), CornerRadius + (1.5 / zoom));
            context.DrawRectangle(null, pens.SelectionRing, ring);
        }

        if (node.State.HasFlag(CanvasNodeState.Evaluating))
        {
            // `E3-T14`: first, because a node being worked on has no settled state yet - an error
            // ring left by the run before would be news about a result this run is replacing.
            RoundedRect ring = new(nodeRect.Inflate(4 / zoom), CornerRadius + (4 / zoom));

            // Travelling on screen and inside §10.2's animation budget; everywhere else - an export,
            // a zoomed-out survey, a crowded view - the static ring §7.4 gives for exactly those cases.
            if (_liveFrame && CanvasLevelOfDetail.AllowsAnimation(zoom, _index.VisibleCount))
            {
                double phase = EvaluatingPhase(Stopwatch.GetElapsedTime(AnimationEpoch));
                context.DrawRectangle(null, TravellingStroke(ring.Rect, CornerRadius + (4 / zoom), zoom, phase), ring);
                _evaluatingTravelled = true;
            }
            else
            {
                context.DrawRectangle(null, pens.EvaluatingRing, ring);
            }
        }
        else if (node.State.HasFlag(CanvasNodeState.Error))
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
        if (_mode is not (InteractionMode.DraggingWire or InteractionMode.PendingWire)
            || _dragSourcePort is not { } source)
        {
            return;
        }

        PortCenter(source, out double x, out double y);
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

    /// <summary>
    /// Draws the box being dragged, in the style of the direction it is being dragged in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rectangle is normalised here, and it was not.</b> Avalonia's
    /// <c>Rect(Point, Point)</c> subtracts rather than ordering: given an end point above or to the
    /// left of the start it produces a negative width, and a rectangle with a negative width draws
    /// nothing at all. Every right-to-left drag — which is to say every crossing selection — was
    /// therefore invisible while still selecting nodes on release, which is exactly the report that
    /// sent us here.
    /// </para>
    /// <para>
    /// An accent tint is permitted here and almost nowhere else, because a marquee lands only on
    /// empty canvas and there is no text over it to lose contrast against (§5.4).
    /// </para>
    /// </remarks>
    /// <param name="context">The drawing context.</param>
    /// <param name="pens">The frame's pens.</param>
    private void DrawMarquee(DrawingContext context, in FramePens pens)
    {
        if (_mode is not InteractionMode.Marquee)
        {
            return;
        }

        context.DrawRectangle(
            MarqueeIsCrossing ? SparkPalette.MarqueeCrossingFillBrush : SparkPalette.MarqueeWindowFillBrush,
            MarqueeIsCrossing ? pens.MarqueeCrossing : pens.MarqueeWindow,
            MarqueeRectangle);
    }

    /// <summary>
    /// The box currently being dragged, in world coordinates, with its corners ordered.
    /// </summary>
    /// <remarks>
    /// <b>Ordered corners are the whole point of this being a property.</b> The width and height
    /// are never negative, whichever way the drag went, and a test can say so — which is the one
    /// assertion that would have caught a marquee that selected nodes and drew nothing. Empty when
    /// no marquee is in progress.
    /// </remarks>
    public Rect MarqueeRectangle => _mode is InteractionMode.Marquee
        ? Normalise(_marqueeStartWorld, _marqueeEndWorld)
        : default;

    /// <summary>
    /// Whether the box being dragged is a <i>crossing</i> box rather than a <i>window</i> box.
    /// </summary>
    /// <remarks>
    /// <b>Direction, as every CAD application has meant it for forty years.</b> Dragging to the
    /// right selects only what the box wholly contains; dragging to the left selects everything the
    /// box touches. Users arrive already knowing this, which is the only reason to spend a gesture
    /// on it — and the pair is worth having because "select that node and not the one behind it" is
    /// otherwise a click-by-click job.
    /// </remarks>
    public bool MarqueeIsCrossing => _marqueeEndWorld.X < _marqueeStartWorld.X;

    private static Rect Normalise(Point a, Point b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X),
        Math.Abs(a.Y - b.Y));

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

        // A wire being pulled off its port is not drawn where it used to be - the rubber band under
        // the pointer is standing in for it. Filtering it out here rather than skipping it in the
        // loop below keeps the positional cache honest: `_wireVisuals[i]` is matched against
        // `wires[i]`, so a hole in the middle would shift every wire after it and rebuild them all.
        //
        // This is the one place that allocates per frame, and only while a drag is in flight -
        // never in the steady state, which is what the cache below exists to protect.
        if (_detachedWire is { } detached)
        {
            List<CanvasWire> remaining = new(wires.Count);
            foreach (CanvasWire wire in wires)
            {
                if (wire != detached)
                {
                    remaining.Add(wire);
                }
            }

            wires = remaining;
        }

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
            PortCenter(wire.From, out double x0, out double y0);
            PortCenter(wire.To, out double x1, out double y1);

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

    /// <summary>
    /// Adds what the box caught to the selection, by the rule its direction chose.
    /// </summary>
    /// <remarks>
    /// The spatial index answers <i>intersects</i>, which is the crossing rule already. A window
    /// selection is that answer filtered down to the nodes the box wholly contains — the index
    /// stays the same shape, and the narrower rule costs one containment test per candidate rather
    /// than a second query structure.
    /// </remarks>
    private void CommitMarquee()
    {
        EnsureIndex();

        CanvasBounds rect = new(
            Math.Min(_marqueeStartWorld.X, _marqueeEndWorld.X),
            Math.Min(_marqueeStartWorld.Y, _marqueeEndWorld.Y),
            Math.Max(_marqueeStartWorld.X, _marqueeEndWorld.X),
            Math.Max(_marqueeStartWorld.Y, _marqueeEndWorld.Y));

        bool crossing = MarqueeIsCrossing;

        _index.Query(rect.MinX, rect.MinY, rect.MaxX, rect.MaxY);
        foreach (int slot in _index.Visible)
        {
            // `SelectionBounds` for both halves of the pair, so a block whose editor is open is
            // caught by the box that looks like it catches it rather than by the one that would
            // have to enclose a rectangle nobody can see.
            CanvasBounds bounds = _graph.Nodes[slot].SelectionBounds;

            if (crossing ? rect.Intersects(bounds) : rect.Contains(bounds))
            {
                _selection.Add(slot);
            }
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finishes a wire that was pulled off its input port: onto the port under the pointer, or off
    /// the graph entirely.
    /// </summary>
    /// <param name="detached">The wire that was lifted.</param>
    /// <param name="target">The port under the pointer, if any.</param>
    /// <remarks>
    /// <para>
    /// <b>One <see cref="GraphChanged"/> for the whole gesture, and that is what makes it one undo
    /// step.</b> Undo is a snapshot of the document taken when this event is raised, so a
    /// disconnect and a reconnect between two raises would be two entries and two presses of
    /// Control+Z to put a wire back where it started. The disconnect and the connect below happen
    /// with nothing raised between them.
    /// </para>
    /// <para>
    /// <b>Dropped back on the port it came from, nothing happens at all</b> — not a disconnect
    /// followed by an identical reconnect, which would be an undo entry for a gesture that changed
    /// nothing. Anything that is not a port the wire can reach removes it, which is the gesture the
    /// client asked for: pull it off and let go.
    /// </para>
    /// </remarks>
    private void DropDetachedWire(CanvasWire detached, CanvasPort? target)
    {
        if (target is { } port && PortEquals(port, detached.To))
        {
            _detachedWire = null;
            _wireVisuals.Clear();
            InvalidateVisual();
            return;
        }

        // Asked before anything is removed, because the answer decides whether this is a move or a
        // deletion, and the engine would answer differently once the wire is gone.
        bool lands = target is { } landing
            && _graph.Preview(detached.From, landing) is not WireOutcome.Refused;

        if (!_graph.Disconnect(detached))
        {
            // It could not be removed, so it is still there and still correct. Nothing to report.
            _detachedWire = null;
            _wireVisuals.Clear();
            InvalidateVisual();
            return;
        }

        bool reconnected = lands && _graph.TryConnect(detached.From, target!.Value);

        _detachedWire = null;
        _wireVisuals.Clear();
        _selectedWire = null;
        InvalidateVisual();

        GraphChanged?.Invoke(
            this,
            new GraphEditedEventArgs(reconnected ? "Move wire" : "Disconnect wire", affectsEvaluation: true));
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

        // `E8-T52`: nodes moved, so this announces rather than merely redrawing, for the same
        // reason the drags do. Unreachable with an editor open today - the editor holds the
        // keyboard, and reaching a menu commits it - but the funnel is the point: the rule is
        // "moved a node, say so", not "moved a node in a way somebody has checked matters".
        AnnounceMove();

        // Labelled without a node count, unlike Move and Delete. Those name an amount of work;
        // an alignment names an arrangement, and "Undo Align left" says everything "Undo Align
        // left 3 nodes" would while reading like English.
        GraphChanged?.Invoke(
            this, new GraphEditedEventArgs(CanvasAlignment.Describe(align), affectsEvaluation: false));

        return true;
    }

    /// <summary>
    /// Whether a clean-up would be meaningful over what it would act on.
    /// </summary>
    /// <returns>True when there are at least two nodes to arrange.</returns>
    public bool CanCleanUpLayout() => CanvasLayout.IsApplicable(LayoutSlots().Count);

    /// <summary>
    /// Arranges nodes into columns that follow the wires: every node to the right of everything
    /// that feeds it.
    /// </summary>
    /// <returns>True when at least one node actually moved.</returns>
    /// <remarks>
    /// <para>
    /// <b>The selection decides the scope, and one node is not a scope.</b> Two or more selected
    /// nodes are tidied on their own and everything else is left alone; anything less — nothing
    /// selected, or a single node — tidies the whole graph. That is the rule the user already knows
    /// from every other editor's clean-up, and the single-node case matters: clicking a node to
    /// look at it and then pressing the key means <i>tidy this graph</i>, never <i>move this one
    /// node nowhere</i>.
    /// </para>
    /// <para>
    /// Reported as an edit that does <b>not</b> require a run, for the same reason an alignment is:
    /// a position is not in a node's provenance, so nothing downstream of it can evaluate
    /// differently afterwards.
    /// </para>
    /// <para>
    /// <b>A clean-up that moves nothing records nothing.</b> Pressing the key on an already-tidy
    /// graph is how a user checks it is tidy, and an undo step whose undo moves nothing reads as
    /// undo being broken — N19, in the shape the drag gesture and then the alignments already had
    /// to learn.
    /// </para>
    /// </remarks>
    public bool CleanUpLayout()
    {
        List<int> slots = LayoutSlots();
        if (!CanvasLayout.IsApplicable(slots.Count))
        {
            return false;
        }

        // The spatial index is rebuilt inside Render, so a canvas that has never painted has a
        // stale one - and a clean-up can be invoked from a menu before any frame is drawn.
        EnsureIndex();

        List<CanvasBounds> boxes = new(slots.Count);
        foreach (int slot in slots)
        {
            boxes.Add(_graph.Nodes[slot].Bounds);
        }

        // The wires arrive in slot terms and the layout works in positions within the set being
        // laid out, so they are mapped through the same ordering the boxes were built from. A wire
        // with one end outside the selection maps to nothing and is dropped: it is not a link
        // between two nodes that are moving.
        Dictionary<int, int> position = [];
        for (int i = 0; i < slots.Count; i++)
        {
            position[slots[i]] = i;
        }

        List<(int From, int To)> links = [];
        foreach (CanvasWire wire in _graph.Wires)
        {
            if (position.TryGetValue(wire.From.NodeIndex, out int from) &&
                position.TryGetValue(wire.To.NodeIndex, out int to))
            {
                links.Add((from, to));
            }
        }

        IReadOnlyList<(double X, double Y)> placed = CanvasLayout.Apply(boxes, links);
        bool moved = false;

        for (int i = 0; i < slots.Count; i++)
        {
            CanvasNode node = _graph.Nodes[slots[i]];
            (double x, double y) = placed[i];

            // Not an exact comparison, and CanvasLayout.Negligible says why: a width recovered from
            // two corners is not bit-stable under moving the box, so a second pass over an
            // already-tidy graph lands every node a fraction of a millionth of a unit from where
            // the first one put it.
            if (!CanvasLayout.Moves((node.X, node.Y), (x, y)))
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
        AnnounceMove();
        GraphChanged?.Invoke(
            this, new GraphEditedEventArgs(CanvasLayout.Description, affectsEvaluation: false));

        return true;
    }

    /// <summary>
    /// Which slots a clean-up would arrange: the selection when it holds more than one node, and
    /// the whole graph otherwise.
    /// </summary>
    /// <returns>The slots, ascending, and every one of them a node that exists.</returns>
    /// <remarks>
    /// Sorted so that the arrangement is a function of the graph and not of the order the user
    /// happened to click in — the same reason <see cref="AlignSelection"/> sorts, and it matters
    /// more here because a column's ordering falls back to the list order when two nodes want the
    /// same height.
    /// </remarks>
    private List<int> LayoutSlots()
    {
        List<int> slots = [];

        if (_selection.Count > 1)
        {
            foreach (int slot in _selection)
            {
                if (slot >= 0 && slot < _graph.Nodes.Count)
                {
                    slots.Add(slot);
                }
            }

            slots.Sort();
            return slots;
        }

        for (int slot = 0; slot < _graph.Nodes.Count; slot++)
        {
            slots.Add(slot);
        }

        return slots;
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

        // `E8-T54`: DELETING IS A MOVE, AND IT IS THE MOVE THAT MATTERS MOST.
        //
        // Every slot after a deleted one shifts down, so this changes where nodes are on screen
        // just as surely as a drag does - and it is the one case where the open editor's block may
        // not be there at all. Without the announcement the editor stayed on the canvas over
        // nothing, and cleared itself only on the next pan or zoom, because that was the only
        // thing that reached the handler that hides it. `E8-T52` routed the two drags into the
        // funnel and did not look for the other sites; this is one.
        AnnounceMove();
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

        // NOT `_index.HitTest`, AND THE INDEX IS NOT THE THING THAT IS WRONG.
        //
        // The index is built from `Bounds`, which is what a node is drawn as — and a code block
        // hosting an open editor is drawn much larger than it will be a moment later. Its clicks
        // belong to the editor, which is a control on top of this one, so the canvas must not
        // claim them. The index still narrows the candidates; `SelectionBounds` decides.
        _index.Query(world.X, world.Y, world.X, world.Y);

        foreach (int slot in _index.VisibleTopDown)
        {
            if (_graph.Nodes[slot].SelectionBounds.Contains(world.X, world.Y))
            {
                return slot;
            }
        }

        return -1;
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
                // Nothing is drawn for it, so nothing may be clicked on it. Without this the
                // header would answer for a hidden port whose centre `InputPortCenter` puts there.
                if (node.InputRow(i) < 0)
                {
                    continue;
                }

                node.InputPortCenter(i, out double x, out double y);

                if ((Math.Abs(x - world.X) <= reach && Math.Abs(y - world.Y) <= reach)
                    || InPortTab(node, i, isOutput: false, world))
                {
                    return new CanvasPort(slot, i, IsOutput: false);
                }
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                node.OutputPortCenter(i, out double x, out double y);

                if ((Math.Abs(x - world.X) <= reach && Math.Abs(y - world.Y) <= reach)
                    || InPortTab(node, i, isOutput: true, world))
                {
                    return new CanvasPort(slot, i, IsOutput: true);
                }
            }
        }

        return null;
    }

    /// <summary>Whether a point is inside a port's tab, which is the whole of the target.</summary>
    /// <remarks>
    /// <b>This is the reason the tabs exist</b> (`E8-T36`): the port's name is part of the port, so
    /// clicking the word <c>radius</c> starts the wire that <c>radius</c> wants. The disc's own
    /// screen-space reach is still tested as well, because it extends *outside* the node where the
    /// tab does not, and that is where a wire is aimed from.
    /// </remarks>
    private static bool InPortTab(CanvasNode node, int index, bool isOutput, Point world)
    {
        node.PortTab(index, isOutput, out double left, out double top, out double right, out double bottom);

        return world.X >= left && world.X <= right && world.Y >= top && world.Y <= bottom;
    }

    private void PortCenter(CanvasPort port, out double x, out double y)
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
            node.OutputPortCenter(port.PortIndex, out x, out y);
        }
        else
        {
            node.InputPortCenter(port.PortIndex, out x, out y);
        }
    }

    private Point ToWorld(Point screen) =>
        new(_transform.ToWorldX(screen.X), _transform.ToWorldY(screen.Y));

    private static bool NullablePortEquals(CanvasPort? left, CanvasPort? right) =>
        left is null ? right is null : right is not null && left.Value == right.Value;

    /// <summary>Where the evaluating stroke is on its lap, from 0 to 1, after a time (<c>E3-T14</c>).</summary>
    /// <param name="elapsed">Time since the animation epoch.</param>
    /// <returns>The fraction of a lap travelled.</returns>
    internal static double EvaluatingPhase(TimeSpan elapsed) =>
        (elapsed.Ticks % EvaluatingLap.Ticks) / (double)EvaluatingLap.Ticks;

    /// <summary>
    /// The travelling stroke for one outline: a single 2 px <c>accent</c> dash a quarter of the way
    /// round, started where the phase says (<c>E3-T14</c>).
    /// </summary>
    /// <remarks>
    /// Dash lengths are multiples of the stroke width, so the outline's perimeter is measured in
    /// widths: the straight runs, less the corners, plus the corners' arcs.
    /// </remarks>
    private static ImmutablePen TravellingStroke(Rect outline, double radius, double zoom, double phase)
    {
        double width = 2 / zoom;
        double perimeter = (2 * (outline.Width + outline.Height)) - ((8 - (2 * Math.PI)) * radius);
        double lap = perimeter / width;
        double dash = lap * EvaluatingStrokeShare;

        return new ImmutablePen(
            new ImmutableSolidColorBrush(SparkPalette.Accent),
            width,
            new ImmutableDashStyle([dash, lap - dash], -phase * lap),
            PenLineCap.Flat,
            PenLineJoin.Round);
    }

    /// <summary>
    /// The header glyph for a state, from §7.4. Error wins over warning, and both win over
    /// not-evaluated, because a node that errored is the one the user is looking for.
    /// </summary>
    private static string? StateGlyph(CanvasNodeState state)
    {
        // `E3-T14`: evaluating first, for the reason its ring is drawn first.
        if (state.HasFlag(CanvasNodeState.Evaluating))
        {
            return "…";
        }

        if (state.HasFlag(CanvasNodeState.Error))
        {
            return "✕";
        }

        if (state.HasFlag(CanvasNodeState.Warning))
        {
            return "⚠";
        }

        // Frozen before not-evaluated: a frozen node carries both, and the one worth showing is
        // the one the user chose. Nothing else distinguishes them once both are desaturated.
        if (state.HasFlag(CanvasNodeState.Frozen))
        {
            return "‖";
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

    private FormattedText ScriptRun(string text) =>
        Run(_scriptText, text, ScriptTypeface, PortFontSize, SparkPalette.TextPrimaryBrush);

    private FormattedText NumberRun(string text) =>
        Run(_numberText, text, ScriptTypeface, ScriptFontSize, SparkPalette.TextMutedBrush);

    private FormattedText HintRun(string text) =>
        Run(_hintText, text, LabelTypeface, TypeFontSize, SparkPalette.TextMutedBrush);

    /// <summary>The note drawn above every code block's source (`E8-T65`).</summary>
    /// <remarks>
    /// <b>It states the case that surprises rather than the whole rule</b>, because it has to fit
    /// the narrowest block anybody draws. The full account is `CodeBlock.md` §4, and a node is
    /// not the place to reproduce it.
    /// </remarks>
    private const string ScriptHint = "a call or new makes no port — assign it with var";

    /// <summary>How many whole scripts' coloured lines are kept (`E8-T65`).</summary>
    /// <remarks>
    /// Far smaller than <see cref="MaximumCachedTextRuns"/> because an entry is a whole block
    /// rather than one string, and a canvas holds tens of blocks rather than thousands.
    /// </remarks>
    private const int MaximumCachedScripts = 256;

    /// <summary>A block's source, one <see cref="FormattedText"/> per line, syntax coloured.</summary>
    /// <remarks>
    /// <b>Cached by the whole source rather than line by line</b>, because colour is a
    /// whole-document property: the same characters are a comment or are code depending on what
    /// opened above them, so a per-line cache would hand one block another block's colours.
    /// </remarks>
    private IReadOnlyList<FormattedText> ColouredScript(string source)
    {
        if (_colouredScript.TryGetValue(source, out FormattedText[]? existing))
        {
            return existing;
        }

        if (_colouredScript.Count >= MaximumCachedScripts)
        {
            _colouredScript.Clear();
        }

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        IReadOnlyList<IReadOnlyList<Theming.ScriptColouring.Section>> sections =
            Theming.ScriptColouring.Of(source);

        FormattedText[] runs = new FormattedText[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            FormattedText run = new(
                lines[i],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                ScriptTypeface,
                ScriptFontSize,
                SparkPalette.TextPrimaryBrush);

            if (i < sections.Count)
            {
                foreach (Theming.ScriptColouring.Section section in sections[i])
                {
                    run.SetForegroundBrush(section.Brush, section.Start, section.Length);
                }
            }

            runs[i] = run;
        }

        _colouredScript[source] = runs;

        return runs;
    }

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

            // `E8-T67`. border.hairline is the palette's quietest line, which is what a divider
            // inside a surface is for: it separates two halves of one node without reading as an
            // edge between two things.
            BodyDivide = Pen(SparkPalette.BorderHairline, screen);
            WireSelected = Pen(SparkPalette.Accent, Math.Max(2.25, screen));
            LipRest = Pen(Color.FromArgb(0xB3, 0x3E, 0x46, 0x54), screen);
            LipHover = Pen(Color.FromArgb(0xB3, 0x86, 0x74, 0xD6), screen);
            SelectionRing = Pen(SparkPalette.Accent, 2 * screen);
            ErrorRing = Pen(SparkPalette.StateError, 2 * screen);
            WarningRing = Pen(SparkPalette.StateWarning, 2 * screen);
            EvaluatingRing = Pen(SparkPalette.Accent, 2 * screen);
            FocusRing = Pen(SparkPalette.FocusRing, 2 * screen);
            FocusContour = Pen(SparkPalette.FocusContour, screen);
            AccentThin = Pen(SparkPalette.Accent, screen);

            // The halo is wide and translucent where every other ring is narrow and opaque, which
            // is what keeps it from being read as a state. It never falls below 6 px of screen
            // space, because a halo that thins with the zoom stops being a halo.
            SelectionHalo = new ImmutablePen(
                new ImmutableSolidColorBrush(SparkPalette.SelectionHalo, 0.45),
                6 * screen,
                null,
                PenLineCap.Round,
                PenLineJoin.Round);

            // 1.5 px rather than 1: a hairline marquee over a busy graph is a line the eye loses
            // among the wires, and this one has to be followed while it is being dragged.
            MarqueeWindow = Pen(SparkPalette.Accent, 1.5 * screen);

            // Dashed, and that is the whole of how a crossing box is told from a window box.
            MarqueeCrossing = new ImmutablePen(
                new ImmutableSolidColorBrush(SparkPalette.Accent),
                1.5 * screen,
                new ImmutableDashStyle([4, 3], 0),
                PenLineCap.Flat,
                PenLineJoin.Round);

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

        internal IPen BodyDivide { get; }

        internal IPen WireSelected { get; }

        internal IPen LipRest { get; }

        internal IPen LipHover { get; }

        internal IPen SelectionRing { get; }

        internal IPen ErrorRing { get; }

        internal IPen WarningRing { get; }

        internal IPen EvaluatingRing { get; }

        internal IPen FocusRing { get; }

        internal IPen FocusContour { get; }

        internal IPen AccentThin { get; }

        internal IPen SelectionHalo { get; }

        internal IPen MarqueeWindow { get; }

        internal IPen MarqueeCrossing { get; }

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

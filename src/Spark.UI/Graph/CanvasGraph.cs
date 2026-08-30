using System;
using System.Collections.Generic;
using System.Globalization;
using Spark.Api;
using Spark.Engine;
using Spark.UI.Canvas;
using Spark.UI.Theming;

namespace Spark.UI.Graph;

/// <summary>
/// The visual state a node is in, from <c>docs/help/concepts/design-language.md</c> §7.4. States
/// stack — a node can be selected and errored at once — which is why this is a flags enum rather
/// than an ordered one.
/// </summary>
[Flags]
public enum CanvasNodeState
{
    /// <summary>Nothing in particular.</summary>
    None = 0,

    /// <summary>Selected: body steps down the ladder and a 2 px accent ring is drawn.</summary>
    Selected = 1,

    /// <summary>The anchor of a multi-selection: as selected, plus four accent corner ticks.</summary>
    Anchor = 2,

    /// <summary>Warning: a 2 px <c>state.warning</c> ring outside the node, and a <c>⚠</c> glyph.</summary>
    Warning = 4,

    /// <summary>Error: a 2 px <c>state.error</c> ring outside the node, and a <c>✕</c> glyph.</summary>
    Error = 8,

    /// <summary>
    /// Not evaluated: something upstream errored, so this node never ran. Desaturated header, body
    /// at L−2, dashed outline, <c>○</c> glyph — and <b>no error of its own</b>, because there is
    /// nothing wrong with it.
    /// </summary>
    NotEvaluated = 16,
}

/// <summary>Identifies one port on one node, by the node's slot on the canvas.</summary>
/// <param name="NodeIndex">The node's index in <see cref="CanvasGraph.Nodes"/>.</param>
/// <param name="PortIndex">The zero-based port index within its side.</param>
/// <param name="IsOutput">True for an output port on the node's right edge.</param>
public readonly record struct CanvasPort(int NodeIndex, int PortIndex, bool IsOutput);

/// <summary>A connection between an output port and an input port, in canvas slot terms.</summary>
/// <param name="From">The output end.</param>
/// <param name="To">The input end.</param>
public readonly record struct CanvasWire(CanvasPort From, CanvasPort To);

/// <summary>
/// One port as the renderer needs it: a name, and the declared rank its shape encodes.
/// </summary>
/// <param name="Name">The port's display name.</param>
/// <param name="DeclaredRank">
/// How deeply nested a value the port wants. Drawn as a plain disc at rank 0 and a disc with a
/// concentric ring at rank 1 or more (§7.6), so a user can see <i>why</i> a node replicated without
/// opening anything.
/// </param>
/// <param name="Description">One line describing the port, or null.</param>
/// <param name="TypeName">
/// What the port wants, in the words a user types it in — <c>number</c>, <c>degrees</c>,
/// <c>Point</c>. Null when the port's own name already says it, which is why an output called
/// <c>circle</c> is not drawn as "circle Circle" (<see cref="PortTypeName.Beside"/>).
/// </param>
public readonly record struct CanvasPortInfo(
    string Name, int DeclaredRank, string? Description, string? TypeName);

/// <summary>
/// One node as the canvas draws it: a position, a size derived from its ports, a category colour
/// and a state.
/// </summary>
/// <remarks>
/// The node carries a <see cref="NodeId"/> so the view model can find the engine instance behind
/// it, and nothing else from the engine. That is the whole of the seam ADR-0005 asks for: the
/// renderer reads positions, names, ranks and states, and never calls an engine API.
/// </remarks>
public sealed class CanvasNode
{
    /// <summary>The narrowest a node is drawn, in world units — device-independent pixels at 100%.</summary>
    public const double MinimumWidth = 168;

    /// <summary>The header height at 100% zoom, from §7.1.</summary>
    public const double HeaderHeight = 22;

    /// <summary>The vertical pitch of one port row.</summary>
    public const double PortPitch = 18;

    /// <summary>Padding below the last port row.</summary>
    public const double BodyPadding = 10;

    /// <summary>The horizontal inset a port label starts at, on either side.</summary>
    private const double PortInset = 9;

    /// <summary>The gap between a port name and its type, and between the two sides of a row.</summary>
    private const double PortGap = 6;

    /// <summary>The least space left between the two sides of a row, so they never read as one.</summary>
    private const double RowGutter = 18;

    /// <summary>Approximate width of one character of a port name at 11 px Inter.</summary>
    private const double PortCharWidth = 6.2;

    /// <summary>Approximate width of one character of a type label at 10 px Inter.</summary>
    private const double TypeCharWidth = 5.6;

    internal CanvasNode(
        NodeId id,
        string title,
        NodeCategory category,
        double x,
        double y,
        IReadOnlyList<CanvasPortInfo> inputs,
        IReadOnlyList<CanvasPortInfo> outputs,
        string? description,
        bool showsValue = false)
    {
        Id = id;
        Title = title;
        Category = category;
        X = x;
        Y = y;
        Inputs = inputs;
        Outputs = outputs;
        Description = description;
        ShowsValue = showsValue;

        // Roughly 6.6 px per character at 12 px semibold, plus the two 8 px header insets and room
        // for a state glyph. A title that overflows is clipped by the header, which reads as a bug.
        // The port rows are measured too, because a row carries a name and the type beside it and
        // the two sides of a row must not meet in the middle.
        Width = System.Math.Max(
            System.Math.Max(MinimumWidth, 34 + (title.Length * 6.8)),
            WidestRow(inputs, outputs));
    }

    /// <summary>The engine identity of the node instance this draws.</summary>
    public NodeId Id { get; }

    /// <summary>The name drawn in the header.</summary>
    public string Title { get; }

    /// <summary>One paragraph describing the node, or null.</summary>
    public string? Description { get; }

    /// <summary>The library category, which decides the header colour.</summary>
    public NodeCategory Category { get; }

    /// <summary>
    /// Whether this node's value is shown permanently rather than only on hover or selection — a
    /// watch node.
    /// </summary>
    /// <remarks>
    /// Copied from the definition, which declares it. The canvas asks a canvas question and never
    /// names an engine type, which is the rule
    /// ([ADR-0005](../../docs/adr/0005-api-engine-host-layering.md)) that also decides where the
    /// double-click search box gets its answers.
    /// </remarks>
    public bool ShowsValue { get; }

    /// <summary>The left edge in world coordinates.</summary>
    public double X { get; set; }

    /// <summary>The top edge in world coordinates.</summary>
    public double Y { get; set; }

    /// <summary>The node's width in world units.</summary>
    public double Width { get; set; }

    /// <summary>Input ports, top to bottom.</summary>
    public IReadOnlyList<CanvasPortInfo> Inputs { get; }

    /// <summary>Output ports, top to bottom.</summary>
    public IReadOnlyList<CanvasPortInfo> Outputs { get; }

    /// <summary>The node's visual state.</summary>
    public CanvasNodeState State { get; set; }

    /// <summary>The first diagnostic message the last run produced for this node, or null.</summary>
    public string? Message { get; set; }

    /// <summary>A one-line summary of the value on output port 0, or null.</summary>
    public string? ResultSummary { get; set; }

    /// <summary>
    /// The rank of the value the node last produced: 0 for a single value, 1 for a list, 2 for a
    /// list of lists.
    /// </summary>
    /// <remarks>
    /// Kept as a number rather than left inside <see cref="ResultSummary"/>'s text. Rank is the
    /// thing users get wrong — a node that quietly produced a list of lists where they expected a
    /// list is the commonest way a graph goes wrong without erroring — so it has to be something
    /// the canvas can lay out on its own line, not a substring somebody has to read.
    /// </remarks>
    public int ResultRank { get; set; }

    /// <summary>How many items the value holds, or 0 when it is not a list.</summary>
    public int ResultCount { get; set; }

    /// <summary>
    /// The node's height, derived from its port count. A node is as tall as its content and no
    /// taller, which is what makes a dense graph readable.
    /// </summary>
    public double Height =>
        HeaderHeight + (System.Math.Max(Inputs.Count, Outputs.Count) * PortPitch) + BodyPadding;

    /// <summary>The node's bounds in world coordinates.</summary>
    public CanvasBounds Bounds => CanvasBounds.FromSize(X, Y, Width, Height);

    /// <summary>
    /// How wide the widest port row wants to be: two names, up to two types, and a gutter.
    /// </summary>
    /// <remarks>
    /// Estimated from character counts rather than measured, for the same reason the title is:
    /// this runs when a node is created, off the render thread, with no drawing context and no
    /// typeface to measure against. It errs generous — an over-wide node is untidy, an under-wide
    /// one puts an input's type on top of an output's name.
    /// </remarks>
    /// <param name="inputs">The input ports.</param>
    /// <param name="outputs">The output ports.</param>
    /// <returns>The width in world units, or zero when the node has no ports.</returns>
    private static double WidestRow(
        IReadOnlyList<CanvasPortInfo> inputs, IReadOnlyList<CanvasPortInfo> outputs)
    {
        int rows = System.Math.Max(inputs.Count, outputs.Count);
        if (rows == 0)
        {
            return 0;
        }

        double widest = 0;
        for (int row = 0; row < rows; row++)
        {
            double width = 0;

            if (row < inputs.Count)
            {
                width += SideWidth(inputs[row]);
            }

            if (row < outputs.Count)
            {
                width += SideWidth(outputs[row]);
            }

            widest = System.Math.Max(widest, width);
        }

        return widest + (2 * PortInset) + RowGutter;
    }

    private static double SideWidth(in CanvasPortInfo port) =>
        (port.Name.Length * PortCharWidth)
        + (port.TypeName is { } type ? PortGap + (type.Length * TypeCharWidth) : 0);

    /// <summary>The world position of an input port's centre.</summary>
    /// <param name="index">The zero-based port index.</param>
    /// <param name="x">The x coordinate: the node's left edge.</param>
    /// <param name="y">The y coordinate.</param>
    public void InputPortCentre(int index, out double x, out double y)
    {
        x = X;
        y = Y + HeaderHeight + (PortPitch * (index + 0.5));
    }

    /// <summary>The world position of an output port's centre.</summary>
    /// <param name="index">The zero-based port index.</param>
    /// <param name="x">The x coordinate: the node's right edge.</param>
    /// <param name="y">The y coordinate.</param>
    public void OutputPortCentre(int index, out double x, out double y)
    {
        x = X + Width;
        y = Y + HeaderHeight + (PortPitch * (index + 0.5));
    }
}

/// <summary>How a proposed connection is reported back while the wire is being dragged.</summary>
public enum WireOutcome
{
    /// <summary>The connection is accepted as-is. Drawn in <c>state.success</c> with a <c>✓</c>.</summary>
    Accepted,

    /// <summary>Accepted with a lossy conversion. Drawn in <c>state.warning</c> with a <c>≈</c>.</summary>
    Lossy,

    /// <summary>Refused. Drawn in <c>state.error</c> with a <c>✕</c>.</summary>
    Refused,
}

/// <summary>
/// The canvas's view of a real <see cref="Spark.Engine.Graph"/>: node positions and visual state on
/// this side, node instances and wires on the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only type in <c>Spark.UI</c> that talks to the engine, apart from the view
/// model.</b> <see cref="Controls.GraphCanvas"/> reads slots, names, ranks and states from here and
/// reports gestures back; it never calls an engine API. That keeps ADR-0005's "views never touch
/// <c>Spark.Engine</c>" true in source as well as in the project graph, and
/// <c>Spark.Architecture.Tests</c> asserts it by scanning the view files.
/// </para>
/// <para>
/// <b>Slots, not identities, for the renderer.</b> The spatial index is an array of bounds and its
/// hit test answers with an array index, so the canvas speaks in slots. Removing a node therefore
/// renumbers everything after it, which is why <see cref="Remove"/> rebuilds and why the caller
/// must drop its selection afterwards.
/// </para>
/// </remarks>
public sealed class CanvasGraph
{
    private readonly List<CanvasNode> _nodes = [];
    private readonly Dictionary<NodeId, int> _slots = [];
    private readonly List<CanvasWire> _wires = [];
    private readonly List<CanvasNote> _notes = [];
    private readonly List<CanvasGroup> _groups = [];
    private readonly TypeCompatibility _compatibility = TypeCompatibility.Default;
    private bool _wiresDirty = true;

    /// <summary>Creates a canvas view over an empty graph.</summary>
    public CanvasGraph() : this(new Spark.Engine.Graph())
    {
    }

    /// <summary>Creates a canvas view over an existing graph.</summary>
    /// <param name="graph">The engine graph. The view takes ownership of its layout, not its life.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is null.</exception>
    public CanvasGraph(Spark.Engine.Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Engine = graph;
    }

    /// <summary>The engine graph behind this view.</summary>
    public Spark.Engine.Graph Engine { get; }

    /// <summary>
    /// A scope every mutation is run inside, or null to mutate directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Evaluation reads the whole graph on a worker thread. An edit applied while that is in
    /// progress produces a <c>KeyNotFoundException</c> deep inside the evaluator that looks nothing
    /// like the edit that caused it, so the shell sets this to the session's mutation gate, which
    /// cancels the run in flight and then takes the lock.
    /// </para>
    /// <para>
    /// It is a hook rather than a session reference because the canvas has to be constructible in a
    /// headless test with no session at all, and because a graph that could only be edited through a
    /// session would make the test set up the wrong thing to check the right one.
    /// </para>
    /// </remarks>
    public Action<Action>? EditScope { get; set; }

    /// <summary>The nodes, in draw order — index 0 is at the bottom.</summary>
    public IReadOnlyList<CanvasNode> Nodes => _nodes;

    /// <summary>
    /// The notes on the canvas, in draw order.
    /// </summary>
    /// <remarks>
    /// Kept beside the nodes rather than among them. A note is not a node — nothing wires to it and
    /// it never evaluates — and putting the two in one list would mean every loop over nodes had to
    /// remember to skip some of them, which is the sort of thing that is remembered nine times out
    /// of ten.
    /// </remarks>
    public IReadOnlyList<CanvasNote> Notes => _notes;

    /// <summary>The groups on the canvas, in draw order.</summary>
    /// <remarks>
    /// Beside the nodes for the reason the notes are: a group is not a node, and a single list
    /// would put a skip in every loop that walks the nodes.
    /// </remarks>
    public IReadOnlyList<CanvasGroup> Groups => _groups;

    /// <summary>The wires, projected into slot terms.</summary>
    public IReadOnlyList<CanvasWire> Wires
    {
        get
        {
            RebuildWires();
            return _wires;
        }
    }

    /// <summary>Creates a group around a set of nodes.</summary>
    /// <param name="slots">The slots to enclose.</param>
    /// <param name="title">What to call it, or null for the default.</param>
    /// <returns>The group, or null when no slot named a node that exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="slots"/> is null.</exception>
    /// <remarks>
    /// Membership is recorded by identity rather than by slot, because slots renumber when a node
    /// is deleted and a group holding stale slots would frame whichever nodes happened to move
    /// into them.
    /// </remarks>
    public CanvasGroup? AddGroup(IReadOnlyCollection<int> slots, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(slots);

        CanvasGroup group = new();
        foreach (int slot in slots)
        {
            if (slot >= 0 && slot < _nodes.Count)
            {
                group.Add(_nodes[slot].Id);
            }
        }

        if (group.Members.Count == 0)
        {
            return null;
        }

        if (title is not null)
        {
            group.Title = title;
        }

        _groups.Add(group);
        return group;
    }

    /// <summary>Adopts a group that already has an identity, which is what opening a file does.</summary>
    /// <param name="group">The group.</param>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
    public void AdoptGroup(CanvasGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _groups.Add(group);
    }

    /// <summary>
    /// Removes a group. <b>Its nodes stay exactly where they are.</b>
    /// </summary>
    /// <param name="group">The group to remove.</param>
    /// <returns>True when it was there to remove.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
    /// <remarks>
    /// Ungrouping must never delete work. A frame around some nodes is an annotation, and deleting
    /// an annotation that takes the annotated thing with it is the single most expensive surprise
    /// an editor can spring on somebody.
    /// </remarks>
    public bool RemoveGroup(CanvasGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return _groups.Remove(group);
    }

    /// <summary>
    /// The rectangle a group draws, derived from its members and never stored.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <returns>Its frame, or null when none of its members is still in the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
    public CanvasBounds? GroupBounds(CanvasGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        bool any = false;

        foreach (CanvasNode node in _nodes)
        {
            if (!group.Contains(node.Id))
            {
                continue;
            }

            any = true;
            CanvasBounds bounds = node.Bounds;
            minX = Math.Min(minX, bounds.MinX);
            minY = Math.Min(minY, bounds.MinY);
            maxX = Math.Max(maxX, bounds.MaxX);
            maxY = Math.Max(maxY, bounds.MaxY);
        }

        if (!any)
        {
            return null;
        }

        return new CanvasBounds(
            minX - CanvasGroup.Padding,
            minY - CanvasGroup.Padding - CanvasGroup.TitleHeight,
            maxX + CanvasGroup.Padding,
            maxY + CanvasGroup.Padding);
    }

    /// <summary>The slots of a group's members that are still in the graph.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The slots, in ascending order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is null.</exception>
    public IReadOnlyList<int> SlotsIn(CanvasGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        List<int> slots = [];
        for (int slot = 0; slot < _nodes.Count; slot++)
        {
            if (group.Contains(_nodes[slot].Id))
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    /// <summary>Adds a note at a position and returns it.</summary>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <param name="text">What it says, or null for an empty note.</param>
    /// <returns>The note, so the caller can select it or begin editing it.</returns>
    public CanvasNote AddNote(double x, double y, string? text = null)
    {
        CanvasNote note = new() { X = x, Y = y, Text = text ?? string.Empty };
        _notes.Add(note);
        return note;
    }

    /// <summary>Adopts a note that already has an identity, which is what opening a file does.</summary>
    /// <param name="note">The note.</param>
    /// <exception cref="ArgumentNullException"><paramref name="note"/> is null.</exception>
    public void AdoptNote(CanvasNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _notes.Add(note);
    }

    /// <summary>Removes a note.</summary>
    /// <param name="note">The note to remove.</param>
    /// <returns>True when it was there to remove.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="note"/> is null.</exception>
    public bool RemoveNote(CanvasNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return _notes.Remove(note);
    }

    /// <summary>Places a node instance and gives it a position.</summary>
    /// <param name="definition">The definition to instantiate.</param>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <returns>The new node's slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    public int Add(NodeDefinition definition, double x, double y) =>
        Add(definition, x, y, null);

    /// <summary>Places a node instance with a chosen identity, and gives it a position.</summary>
    /// <remarks>
    /// The identity overload exists for graphs that have to be reproducible — a seeded demo, a
    /// checked-in example, a test fixture. A graph whose node identities are freshly generated
    /// every time cannot be committed to a repository usefully: regenerating it rewrites every id
    /// and every wire, so a one-literal change arrives as a diff of the whole file, which is
    /// exactly what [ADR-0017](../../../docs/adr/0017-spark-file-is-plain-json.md) chose text to
    /// avoid.
    /// </remarks>
    /// <param name="definition">The definition to instantiate.</param>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <param name="id">The identity to give it, or null for a fresh one.</param>
    /// <returns>The new node's slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    public int Add(NodeDefinition definition, double x, double y, NodeId? id)
    {
        ArgumentNullException.ThrowIfNull(definition);

        NodeInstance instance = null!;
        Edit(() => instance = Engine.AddNode(definition, id));
        return Adopt(instance, x, y);
    }

    /// <summary>
    /// Brings an instance that is already in the engine graph onto the canvas at a position.
    /// </summary>
    /// <param name="instance">The instance.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <returns>The new node's slot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instance"/> is null.</exception>
    public int Adopt(NodeInstance instance, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(instance);

        CanvasNode node = new(
            instance.Id,
            instance.Definition.DisplayName,
            NodeCategoryNames.Parse(instance.Definition.Category),
            x,
            y,
            Describe(instance.Definition.Inputs),
            Describe(instance.Definition.Outputs),
            instance.Definition.Description,
            instance.Definition.ShowsValue);

        _nodes.Add(node);
        _slots[instance.Id] = _nodes.Count - 1;
        _wiresDirty = true;

        return _nodes.Count - 1;
    }

    /// <summary>Removes a node and every wire touching it, renumbering the slots after it.</summary>
    /// <param name="slot">The node's slot.</param>
    /// <returns>The identity of the removed node, or null when the slot was out of range.</returns>
    public NodeId? Remove(int slot)
    {
        if (slot < 0 || slot >= _nodes.Count)
        {
            return null;
        }

        NodeId id = _nodes[slot].Id;
        Edit(() => Engine.RemoveNode(id));
        _nodes.RemoveAt(slot);

        _slots.Clear();
        for (int index = 0; index < _nodes.Count; index++)
        {
            _slots[_nodes[index].Id] = index;
        }

        // A deleted node leaves every group it was in. Left behind, the identity would be a
        // member that cannot be found, and a group whose last member was deleted would go on
        // claiming to contain something. Groups that empty out are removed with it: a frame
        // around nothing is not a frame.
        for (int index = _groups.Count - 1; index >= 0; index--)
        {
            if (_groups[index].Remove(id) && _groups[index].Members.Count == 0)
            {
                _groups.RemoveAt(index);
            }
        }

        _wiresDirty = true;
        return id;
    }

    /// <summary>
    /// Reports what would happen if a wire were dropped here, without drawing it.
    /// </summary>
    /// <remarks>
    /// The three outcomes are the design language's <c>state.success</c>, <c>state.warning</c> and
    /// <c>state.error</c>, and they are the engine's own answer rather than a UI guess — which is
    /// the whole reason the placeholder that always said "accepted" had to go.
    /// </remarks>
    /// <param name="from">One end of the proposed wire.</param>
    /// <param name="to">The other end.</param>
    /// <returns>Accepted, lossy, or refused.</returns>
    public WireOutcome Preview(CanvasPort from, CanvasPort to)
    {
        if (!TryOrder(from, to, out CanvasPort output, out CanvasPort input))
        {
            return WireOutcome.Refused;
        }

        CanvasNode source = _nodes[output.NodeIndex];
        CanvasNode target = _nodes[input.NodeIndex];

        if (Engine.WouldCloseCycle(source.Id, target.Id))
        {
            return WireOutcome.Refused;
        }

        NodeInstance sourceInstance = Engine.Node(source.Id);
        NodeInstance targetInstance = Engine.Node(target.Id);

        if (output.PortIndex >= sourceInstance.Definition.Outputs.Count
            || input.PortIndex >= targetInstance.Definition.Inputs.Count)
        {
            return WireOutcome.Refused;
        }

        CompatibilityResult result = _compatibility.Check(
            sourceInstance.Definition.Outputs[output.PortIndex],
            targetInstance.Definition.Inputs[input.PortIndex]);

        if (!result.IsAccepted)
        {
            return WireOutcome.Refused;
        }

        return result.IsLossy ? WireOutcome.Lossy : WireOutcome.Accepted;
    }

    /// <summary>Draws a wire, if the engine accepts it.</summary>
    /// <param name="from">One end.</param>
    /// <param name="to">The other end.</param>
    /// <returns>True when a wire was created.</returns>
    public bool TryConnect(CanvasPort from, CanvasPort to)
    {
        if (!TryOrder(from, to, out CanvasPort output, out CanvasPort input))
        {
            return false;
        }

        ConnectionResult result = default;
        Edit(() => result = Engine.TryConnect(
            _nodes[output.NodeIndex].Id,
            output.PortIndex,
            _nodes[input.NodeIndex].Id,
            input.PortIndex));

        if (result.Accepted)
        {
            _wiresDirty = true;
        }

        return result.Accepted;
    }

    /// <summary>Removes a wire.</summary>
    /// <param name="wire">The wire, in slot terms.</param>
    /// <returns>True when a wire was removed.</returns>
    public bool Disconnect(CanvasWire wire)
    {
        if (!InRange(wire.From) || !InRange(wire.To))
        {
            return false;
        }

        bool removed = false;
        Edit(() => removed = Engine.Disconnect(new Wire(
            _nodes[wire.From.NodeIndex].Id,
            wire.From.PortIndex,
            _nodes[wire.To.NodeIndex].Id,
            wire.To.PortIndex)));

        if (removed)
        {
            _wiresDirty = true;
        }

        return removed;
    }

    /// <summary>Sets the literal on an unwired input port.</summary>
    /// <param name="slot">The node's slot.</param>
    /// <param name="portIndex">The input port index.</param>
    /// <param name="value">The literal.</param>
    public void SetLiteral(int slot, int portIndex, object? value)
    {
        if (slot < 0 || slot >= _nodes.Count)
        {
            return;
        }

        Edit(() => Engine.SetLiteral(_nodes[slot].Id, portIndex, value));
    }

    /// <summary>Whether an input port already has a wire into it.</summary>
    /// <param name="slot">The node's slot.</param>
    /// <param name="portIndex">The input port index.</param>
    /// <returns>True when a wire terminates there.</returns>
    public bool IsInputWired(int slot, int portIndex)
    {
        if (slot < 0 || slot >= _nodes.Count)
        {
            return false;
        }

        foreach (Wire wire in Engine.IncomingWires(_nodes[slot].Id))
        {
            if (wire.TargetPort == portIndex)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The output ports whose values the viewport previews: the ones nothing downstream consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Previewing <i>every</i> port would draw the same hundred points twice — once from the point
    /// node and once from the display node wired to it — coincident and z-fighting, which reads as
    /// a renderer defect rather than as a preview policy.
    /// </para>
    /// <para>
    /// Terminal-only is this slice's rule and it is a placeholder for the real one: the per-node
    /// preview toggle in §7.7, which is not built yet. When it lands, this becomes "every port whose
    /// node has preview on".
    /// </para>
    /// </remarks>
    /// <returns>The (slot, port index) pairs to preview.</returns>
    public IReadOnlyList<(int Slot, int PortIndex)> PreviewPorts()
    {
        List<(int, int)> ports = [];

        for (int slot = 0; slot < _nodes.Count; slot++)
        {
            NodeInstance instance = Engine.Node(_nodes[slot].Id);
            IReadOnlyList<Wire> outgoing = Engine.OutgoingWires(instance.Id);

            for (int port = 0; port < instance.Definition.Outputs.Count; port++)
            {
                bool consumed = false;
                foreach (Wire wire in outgoing)
                {
                    if (wire.SourcePort == port)
                    {
                        consumed = true;
                        break;
                    }
                }

                if (!consumed)
                {
                    ports.Add((slot, port));
                }
            }
        }

        return ports;
    }

    /// <summary>The slot a node identity occupies, or −1.</summary>
    /// <param name="id">The identity.</param>
    /// <returns>The slot.</returns>
    public int SlotOf(NodeId id) => _slots.TryGetValue(id, out int slot) ? slot : -1;

    /// <summary>
    /// Applies a run's states and diagnostics to the nodes, preserving selection flags.
    /// </summary>
    /// <remarks>
    /// The mapping is the whole of the design language's error story. An errored node gets the
    /// error ring; every node the evaluator marked <c>NotEvaluated</c> gets the grey state and
    /// <b>no diagnostic at all</b>, because cascading one broken node into fifty errors is what
    /// buries the cause.
    /// </remarks>
    /// <param name="result">The run, or null to clear every evaluated state.</param>
    public void ApplyResult(EvaluationResult? result)
    {
        foreach (CanvasNode node in _nodes)
        {
            CanvasNodeState kept = node.State & (CanvasNodeState.Selected | CanvasNodeState.Anchor);

            if (result is null)
            {
                node.State = kept;
                node.Message = null;
                node.ResultSummary = null;
                node.ResultRank = 0;
                node.ResultCount = 0;
                continue;
            }

            node.State = kept | result.StateOf(node.Id) switch
            {
                NodeState.Error or NodeState.Cycle => CanvasNodeState.Error,
                NodeState.Warning => CanvasNodeState.Warning,
                NodeState.NotEvaluated => CanvasNodeState.NotEvaluated,
                _ => CanvasNodeState.None,
            };

            IReadOnlyList<SparkDiagnostic> diagnostics = result.DiagnosticsFor(node.Id);
            node.Message = diagnostics.Count > 0 ? diagnostics[0].Message : null;
            object? value = result.Value(node.Id);
            node.ResultSummary = Summarise(value);
            node.ResultRank = SparkList.RankOf(value);
            node.ResultCount = value is SparkList produced ? produced.Count : 0;
        }
    }

    /// <summary>The bounds of every node in the graph.</summary>
    /// <returns>The union, or a unit rectangle at the origin when the graph is empty.</returns>
    public CanvasBounds ComputeBounds()
    {
        if (_nodes.Count == 0 && _notes.Count == 0)
        {
            return new CanvasBounds(0, 0, 1, 1);
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (CanvasNode node in _nodes)
        {
            CanvasBounds bounds = node.Bounds;
            minX = System.Math.Min(minX, bounds.MinX);
            minY = System.Math.Min(minY, bounds.MinY);
            maxX = System.Math.Max(maxX, bounds.MaxX);
            maxY = System.Math.Max(maxY, bounds.MaxY);
        }

        // Notes count. *Zoom to fit* has to fit the document, not the part of it that evaluates:
        // a note placed beside a graph would otherwise sit just off the edge of the one gesture
        // whose entire promise is that nothing is off the edge any more.
        foreach (CanvasNote note in _notes)
        {
            CanvasBounds bounds = note.Bounds;
            minX = System.Math.Min(minX, bounds.MinX);
            minY = System.Math.Min(minY, bounds.MinY);
            maxX = System.Math.Max(maxX, bounds.MaxX);
            maxY = System.Math.Max(maxY, bounds.MaxY);
        }

        // And so do group frames, which reach beyond their own members by the padding and the
        // title strip. Fitting the members exactly clips the title off the top of the window,
        // which is where the group's name is and the only part of it a pointer can grab.
        foreach (CanvasGroup group in _groups)
        {
            if (GroupBounds(group) is not { } bounds)
            {
                continue;
            }

            minX = System.Math.Min(minX, bounds.MinX);
            minY = System.Math.Min(minY, bounds.MinY);
            maxX = System.Math.Max(maxX, bounds.MaxX);
            maxY = System.Math.Max(maxY, bounds.MaxY);
        }

        return new CanvasBounds(minX, minY, maxX, maxY);
    }

    /// <summary>Marks the wire projection stale, after the engine graph was edited elsewhere.</summary>
    public void InvalidateWires() => _wiresDirty = true;

    /// <summary>
    /// The rank line a preview bubble and the watch panel both show above a value.
    /// </summary>
    /// <param name="node">The node whose last result is being described.</param>
    /// <returns>Text such as <c>rank 0 · one value</c> or <c>rank 2 · 4 items</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    /// <remarks>
    /// <b>Rank 0 says <i>one value</i>, never <i>0 items</i>.</b> A scalar and an empty list are
    /// precisely the two things this line exists to tell apart, and wording them alike would defeat
    /// it at the one moment it matters. `E8-T10` asks for rank because rank is what users get
    /// wrong: <c>[[1], [2]]</c> and <c>[1, 2]</c> read alike at a glance and behave completely
    /// differently under lacing.
    /// </remarks>
    public static string RankLine(CanvasNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.ResultRank == 0)
        {
            return "rank 0 · one value";
        }

        string items = node.ResultCount == 1 ? "1 item" : string.Create(
            CultureInfo.InvariantCulture, $"{node.ResultCount} items");

        return string.Create(
            CultureInfo.InvariantCulture, $"rank {node.ResultRank} · {items}");
    }

    /// <summary>
    /// The most characters the watch panel renders. Beyond this the value is cut and the cut is
    /// announced.
    /// </summary>
    /// <remarks>
    /// A cap rather than no cap, because a list of a hundred thousand points renders to several
    /// megabytes of text and a <c>TextBox</c> handed that stops being a user interface. Generous
    /// enough that anything a person is actually reading arrives whole, and the cut says how much
    /// was left out rather than trailing off, so nobody mistakes a truncation for the end of their
    /// data.
    /// </remarks>
    public const int WatchCharacterLimit = 20_000;

    /// <summary>
    /// The full rendering of a value for the watch panel, capped rather than summarised.
    /// </summary>
    /// <param name="value">The value, which may be a list.</param>
    /// <returns>The rendering, or an empty string when there is nothing to show.</returns>
    public static string Expand(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        string text = value.ToString() ?? string.Empty;
        if (text.Length <= WatchCharacterLimit)
        {
            return text;
        }

        int hidden = text.Length - WatchCharacterLimit;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{text[..WatchCharacterLimit]}{Environment.NewLine}{Environment.NewLine}… {hidden} more characters not shown.");
    }

    /// <summary>
    /// A one-line rendering of a value for the properties panel and the node's own readout.
    /// </summary>
    /// <param name="value">The value, which may be a list.</param>
    /// <returns>The rendering, or null when there is nothing to say.</returns>
    public static string? Summarise(object? value)
    {
        if (value is null)
        {
            return null;
        }

        // The value alone. Rank and length used to be prefixed here, and once RankLine existed
        // that made a preview bubble read "rank 1 · 8 items" above "8 items, rank 1  [...]" —
        // the same fact twice, in two wordings, in adjacent lines. One rendering of a value, one
        // rendering of its shape, and callers compose the two.
        string text = value.ToString() ?? string.Empty;
        return text.Length > 60 ? text[..57] + "…" : text;
    }

    private static IReadOnlyList<CanvasPortInfo> Describe(IReadOnlyList<PortDefinition> ports)
    {
        CanvasPortInfo[] described = new CanvasPortInfo[ports.Count];
        for (int index = 0; index < ports.Count; index++)
        {
            described[index] = new CanvasPortInfo(
                ports[index].Name,
                ports[index].KeepStructure ? -1 : ports[index].DeclaredRank,
                ports[index].Description,
                PortTypeName.Beside(ports[index].Name, ports[index].ValueType));
        }

        return described;
    }

    private void Edit(Action edit)
    {
        if (EditScope is { } scope)
        {
            scope(edit);
            return;
        }

        edit();
    }

    private bool InRange(CanvasPort port) =>
        port.NodeIndex >= 0 && port.NodeIndex < _nodes.Count;

    private bool TryOrder(CanvasPort from, CanvasPort to, out CanvasPort output, out CanvasPort input)
    {
        (output, input) = from.IsOutput ? (from, to) : (to, from);

        return from.IsOutput != to.IsOutput
            && from.NodeIndex != to.NodeIndex
            && InRange(output)
            && InRange(input);
    }

    private void RebuildWires()
    {
        if (!_wiresDirty)
        {
            return;
        }

        _wires.Clear();
        foreach (Wire wire in Engine.Wires())
        {
            if (!_slots.TryGetValue(wire.Source, out int source) || !_slots.TryGetValue(wire.Target, out int target))
            {
                continue;
            }

            _wires.Add(new CanvasWire(
                new CanvasPort(source, wire.SourcePort, IsOutput: true),
                new CanvasPort(target, wire.TargetPort, IsOutput: false)));
        }

        _wiresDirty = false;
    }
}

using System;
using System.Collections.Generic;
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
public readonly record struct CanvasPortInfo(string Name, int DeclaredRank, string? Description);

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

    internal CanvasNode(
        NodeId id,
        string title,
        NodeCategory category,
        double x,
        double y,
        IReadOnlyList<CanvasPortInfo> inputs,
        IReadOnlyList<CanvasPortInfo> outputs,
        string? description)
    {
        Id = id;
        Title = title;
        Category = category;
        X = x;
        Y = y;
        Inputs = inputs;
        Outputs = outputs;
        Description = description;

        // Roughly 6.6 px per character at 12 px semibold, plus the two 8 px header insets and room
        // for a state glyph. A title that overflows is clipped by the header, which reads as a bug.
        Width = System.Math.Max(MinimumWidth, 34 + (title.Length * 6.8));
    }

    /// <summary>The engine identity of the node instance this draws.</summary>
    public NodeId Id { get; }

    /// <summary>The name drawn in the header.</summary>
    public string Title { get; }

    /// <summary>One paragraph describing the node, or null.</summary>
    public string? Description { get; }

    /// <summary>The library category, which decides the header colour.</summary>
    public NodeCategory Category { get; }

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
    /// The node's height, derived from its port count. A node is as tall as its content and no
    /// taller, which is what makes a dense graph readable.
    /// </summary>
    public double Height =>
        HeaderHeight + (System.Math.Max(Inputs.Count, Outputs.Count) * PortPitch) + BodyPadding;

    /// <summary>The node's bounds in world coordinates.</summary>
    public CanvasBounds Bounds => CanvasBounds.FromSize(X, Y, Width, Height);

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

    /// <summary>The wires, projected into slot terms.</summary>
    public IReadOnlyList<CanvasWire> Wires
    {
        get
        {
            RebuildWires();
            return _wires;
        }
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
            instance.Definition.Description);

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
            node.ResultSummary = Summarise(result.Value(node.Id));
        }
    }

    /// <summary>The bounds of every node in the graph.</summary>
    /// <returns>The union, or a unit rectangle at the origin when the graph is empty.</returns>
    public CanvasBounds ComputeBounds()
    {
        if (_nodes.Count == 0)
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

        return new CanvasBounds(minX, minY, maxX, maxY);
    }

    /// <summary>Marks the wire projection stale, after the engine graph was edited elsewhere.</summary>
    public void InvalidateWires() => _wiresDirty = true;

    /// <summary>
    /// A one-line rendering of a value for the properties panel and the node's own readout.
    /// </summary>
    /// <param name="value">The value, which may be a list.</param>
    /// <returns>The rendering, or null when there is nothing to say.</returns>
    public static string? Summarise(object? value)
    {
        switch (value)
        {
            case null:
                return null;

            case SparkList list:
                string text = list.ToString();
                string head = text.Length > 60 ? text[..57] + "…" : text;
                return $"{list.Count} items, rank {list.Rank}  {head}";

            default:
                string plain = value.ToString() ?? string.Empty;
                return plain.Length > 60 ? plain[..57] + "…" : plain;
        }
    }

    private static IReadOnlyList<CanvasPortInfo> Describe(IReadOnlyList<PortDefinition> ports)
    {
        CanvasPortInfo[] described = new CanvasPortInfo[ports.Count];
        for (int index = 0; index < ports.Count; index++)
        {
            described[index] = new CanvasPortInfo(
                ports[index].Name,
                ports[index].KeepStructure ? -1 : ports[index].DeclaredRank,
                ports[index].Description);
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

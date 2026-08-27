using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// The outcome of trying to draw a wire.
/// </summary>
/// <param name="Wire">The wire, when it was accepted.</param>
/// <param name="Compatibility">Which compatibility rule let it through, if any.</param>
/// <param name="Diagnostic">
/// The refusal, or the warning that the connection is lossy. <see langword="null"/> when the wire
/// was accepted cleanly.
/// </param>
public readonly record struct ConnectionResult(Wire? Wire, PortCompatibility Compatibility, SparkDiagnostic? Diagnostic)
{
    /// <summary>Whether the wire was created.</summary>
    public bool Accepted => Wire.HasValue;
}

/// <summary>
/// A node graph: instances, the wires between them, and the record of which of them still need to
/// be evaluated.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation goes through this type rather than through the nodes, because every mutation has
/// to mark the affected subgraph dirty. Collections handed out are snapshots; nothing mutable
/// escapes.
/// </para>
/// <para>
/// <b>Wires are validated when they are drawn, not when the graph runs.</b> A type mismatch and a
/// cycle are both refused by <see cref="TryConnect"/>, which is what lets the canvas show a red
/// wire under the cursor rather than a red node after a run. The one path that does not validate is
/// <see cref="LoadWire"/>, which exists because a file may contain a cycle a hand edit or an older
/// version put there, and the answer to that is to load it and report it, never to refuse to open
/// the document.
/// </para>
/// </remarks>
public sealed class Graph
{
    private readonly Dictionary<NodeId, NodeInstance> _nodes = [];
    private readonly HashSet<Wire> _wires = [];
    private readonly Dictionary<NodeId, List<Wire>> _incoming = [];
    private readonly Dictionary<NodeId, List<Wire>> _outgoing = [];
    private readonly HashSet<NodeId> _dirty = [];
    private readonly TypeCompatibility _compatibility;

    /// <summary>Creates an empty graph.</summary>
    /// <param name="compatibility">
    /// The wire rules this graph uses. Defaults to the rules with no registered converters.
    /// </param>
    public Graph(TypeCompatibility? compatibility = null) =>
        _compatibility = compatibility ?? TypeCompatibility.Default;

    /// <summary>Every node, in no particular order.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<NodeInstance> Nodes() => [.. _nodes.Values];

    /// <summary>Every wire, in no particular order.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<Wire> Wires() => [.. _wires];

    /// <summary>The nodes whose cached results are known to be out of date.</summary>
    /// <returns>A snapshot.</returns>
    public IReadOnlySet<NodeId> DirtyNodes() => new HashSet<NodeId>(_dirty);

    /// <summary>Looks up a node.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns>The node.</returns>
    /// <exception cref="KeyNotFoundException">No node has that identity.</exception>
    public NodeInstance Node(NodeId id) => _nodes[id];

    /// <summary>Whether the graph contains a node.</summary>
    /// <param name="id">The node identity.</param>
    /// <param name="node">The node, when it exists.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    public bool TryGetNode(NodeId id, out NodeInstance? node) => _nodes.TryGetValue(id, out node);

    /// <summary>Adds a node instance.</summary>
    /// <param name="definition">The definition to instantiate.</param>
    /// <param name="id">
    /// The identity to use, for deserialisation. Omit it to mint a fresh one. Identities are never
    /// reused, so passing one that already exists is an error rather than a replace.
    /// </param>
    /// <returns>The new instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A node with that identity already exists.</exception>
    public NodeInstance AddNode(NodeDefinition definition, NodeId? id = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        NodeId nodeId = id ?? NodeId.New();
        if (_nodes.ContainsKey(nodeId))
        {
            throw new ArgumentException($"A node with identity {nodeId} already exists.", nameof(id));
        }

        NodeInstance instance = new(nodeId, definition);
        _nodes[nodeId] = instance;
        _incoming[nodeId] = [];
        _outgoing[nodeId] = [];
        _dirty.Add(nodeId);

        return instance;
    }

    /// <summary>Removes a node and every wire touching it.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns><see langword="true"/> when a node was removed.</returns>
    public bool RemoveNode(NodeId id)
    {
        if (!_nodes.Remove(id))
        {
            return false;
        }

        foreach (Wire wire in _incoming[id].Concat(_outgoing[id]).ToArray())
        {
            Disconnect(wire);
        }

        _incoming.Remove(id);
        _outgoing.Remove(id);
        _dirty.Remove(id);

        return true;
    }

    /// <summary>
    /// Tries to draw a wire, applying the type compatibility rules and refusing anything that would
    /// close a cycle.
    /// </summary>
    /// <param name="source">The node the value comes from.</param>
    /// <param name="sourcePort">Its output port index.</param>
    /// <param name="target">The node the value goes to.</param>
    /// <param name="targetPort">Its input port index.</param>
    /// <returns>The wire, or the reason it was refused.</returns>
    /// <exception cref="KeyNotFoundException">Either node is not in this graph.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either port index does not exist.</exception>
    public ConnectionResult TryConnect(NodeId source, int sourcePort, NodeId target, int targetPort)
    {
        NodeInstance sourceNode = _nodes[source];
        NodeInstance targetNode = _nodes[target];

        ArgumentOutOfRangeException.ThrowIfNegative(sourcePort);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourcePort, sourceNode.Definition.Outputs.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(targetPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(targetPort, targetNode.Definition.Inputs.Count);

        // Refuse the cycle before checking types: a self-referential wire whose types happen not to
        // match should report the cycle, which is the more fundamental problem.
        if (source == target || Reaches(target, source))
        {
            return new ConnectionResult(null, PortCompatibility.Incompatible, DiagnosticCodes.Create(
                DiagnosticSeverity.Error,
                DiagnosticCodes.WireWouldCloseCycle,
                $"Connecting '{sourceNode.Definition.DisplayName}' to '{targetNode.Definition.DisplayName}' would close a cycle. A dataflow graph has no way to evaluate one, so the wire is refused rather than the graph hanging later.",
                portIndex: targetPort));
        }

        CompatibilityResult compatibility = _compatibility.Check(
            sourceNode.Definition.Outputs[sourcePort], targetNode.Definition.Inputs[targetPort]);

        if (!compatibility.IsAccepted)
        {
            string code = string.Equals(
                sourceNode.Definition.Outputs[sourcePort].ValueType.FullName,
                targetNode.Definition.Inputs[targetPort].ValueType.FullName,
                StringComparison.Ordinal)
                ? DiagnosticCodes.SameNameDifferentAssembly
                : DiagnosticCodes.IncompatiblePortTypes;

            return new ConnectionResult(null, PortCompatibility.Incompatible, DiagnosticCodes.Create(
                DiagnosticSeverity.Error, code, compatibility.Explanation, portIndex: targetPort));
        }

        // An input port takes at most one wire. Replacing rather than refusing is what a user
        // expects when they drop a second wire onto an occupied port.
        foreach (Wire existing in _incoming[target].Where(wire => wire.TargetPort == targetPort).ToArray())
        {
            Disconnect(existing);
        }

        Wire created = new(source, sourcePort, target, targetPort);
        AddWire(created);

        SparkDiagnostic? warning = compatibility.IsLossy
            ? DiagnosticCodes.Create(
                DiagnosticSeverity.Warning,
                DiagnosticCodes.LossyConversion,
                compatibility.Explanation,
                portIndex: targetPort)
            : null;

        return new ConnectionResult(created, compatibility.Kind, warning);
    }

    /// <summary>
    /// Adds a wire without validating it. This is the deserialisation path.
    /// </summary>
    /// <remarks>
    /// A file can contain a cycle — an older format version, a hand edit, a merge that went wrong —
    /// and refusing to open the document is the wrong answer. The graph loads, the evaluator finds
    /// the cycle, every node in it errors, and the rest of the graph still evaluates.
    /// </remarks>
    /// <param name="source">The node the value comes from.</param>
    /// <param name="sourcePort">Its output port index.</param>
    /// <param name="target">The node the value goes to.</param>
    /// <param name="targetPort">Its input port index.</param>
    /// <returns>The wire.</returns>
    /// <exception cref="KeyNotFoundException">Either node is not in this graph.</exception>
    public Wire LoadWire(NodeId source, int sourcePort, NodeId target, int targetPort)
    {
        _ = _nodes[source];
        _ = _nodes[target];

        Wire wire = new(source, sourcePort, target, targetPort);
        AddWire(wire);
        return wire;
    }

    /// <summary>Removes a wire.</summary>
    /// <param name="wire">The wire.</param>
    /// <returns><see langword="true"/> when a wire was removed.</returns>
    public bool Disconnect(Wire wire)
    {
        if (!_wires.Remove(wire))
        {
            return false;
        }

        if (_incoming.TryGetValue(wire.Target, out List<Wire>? incoming))
        {
            incoming.Remove(wire);
        }

        if (_outgoing.TryGetValue(wire.Source, out List<Wire>? outgoing))
        {
            outgoing.Remove(wire);
        }

        // The target may already be gone: RemoveNode drops the node and then unwires it, and a
        // removed node has nothing to mark dirty.
        if (_nodes.ContainsKey(wire.Target))
        {
            MarkDirty(wire.Target);
        }

        return true;
    }

    /// <summary>The wires feeding a node's input ports.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns>A snapshot.</returns>
    /// <exception cref="KeyNotFoundException">No node has that identity.</exception>
    public IReadOnlyList<Wire> IncomingWires(NodeId id) => [.. _incoming[id]];

    /// <summary>The wires leaving a node's output ports.</summary>
    /// <param name="id">The node identity.</param>
    /// <returns>A snapshot.</returns>
    /// <exception cref="KeyNotFoundException">No node has that identity.</exception>
    public IReadOnlyList<Wire> OutgoingWires(NodeId id) => [.. _outgoing[id]];

    /// <summary>Sets a node's lacing, marking it and everything downstream dirty.</summary>
    /// <param name="id">The node identity.</param>
    /// <param name="lacing">The new lacing.</param>
    /// <exception cref="KeyNotFoundException">No node has that identity.</exception>
    public void SetLacing(NodeId id, LacingMode lacing)
    {
        _nodes[id].Lacing = lacing;
        MarkDirty(id);
    }

    /// <summary>
    /// Sets the literal value on an unwired input port, marking the node and everything downstream
    /// dirty.
    /// </summary>
    /// <param name="id">The node identity.</param>
    /// <param name="portIndex">The input port index.</param>
    /// <param name="value">The literal.</param>
    /// <exception cref="KeyNotFoundException">No node has that identity.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="portIndex"/> is not an input port.</exception>
    public void SetLiteral(NodeId id, int portIndex, object? value)
    {
        _nodes[id].SetLiteral(portIndex, value);
        MarkDirty(id);
    }

    /// <summary>
    /// Marks a node and every node reachable from it as needing re-evaluation.
    /// </summary>
    /// <param name="id">The node identity.</param>
    /// <exception cref="KeyNotFoundException">No node has that identity.</exception>
    public void MarkDirty(NodeId id)
    {
        _ = _nodes[id];

        Stack<NodeId> pending = new();
        pending.Push(id);

        while (pending.Count > 0)
        {
            NodeId current = pending.Pop();
            if (!_dirty.Add(current))
            {
                continue;
            }

            if (!_outgoing.TryGetValue(current, out List<Wire>? outgoing))
            {
                continue;
            }

            foreach (Wire wire in outgoing)
            {
                pending.Push(wire.Target);
            }
        }
    }

    /// <summary>Marks every node clean, as the evaluator does once a run completes.</summary>
    public void MarkAllClean() => _dirty.Clear();

    /// <summary>Marks one node clean.</summary>
    /// <param name="id">The node identity.</param>
    public void MarkClean(NodeId id) => _dirty.Remove(id);

    private void AddWire(Wire wire)
    {
        if (!_wires.Add(wire))
        {
            return;
        }

        _incoming[wire.Target].Add(wire);
        _outgoing[wire.Source].Add(wire);
        MarkDirty(wire.Target);
    }

    private bool Reaches(NodeId from, NodeId to)
    {
        HashSet<NodeId> seen = [];
        Stack<NodeId> pending = new();
        pending.Push(from);

        while (pending.Count > 0)
        {
            NodeId current = pending.Pop();
            if (current == to)
            {
                return true;
            }

            if (!seen.Add(current) || !_outgoing.TryGetValue(current, out List<Wire>? outgoing))
            {
                continue;
            }

            foreach (Wire wire in outgoing)
            {
                pending.Push(wire.Target);
            }
        }

        return false;
    }
}

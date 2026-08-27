using System;
using System.Collections.Generic;
using Spark.UI.Canvas;
using Spark.UI.Theming;

namespace Spark.UI.Graph;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// TEMPORARY. Everything in this file is a placeholder for the graph engine's model and is
// deleted when that model lands. It exists so the canvas can be built, measured and reviewed
// before the engine exists, and it deliberately assumes nothing about the engine's API.
//
// The seam is narrow on purpose: GraphCanvas reads exactly PlaceholderNode's position, size,
// title, category, port counts and state, and PlaceholderWire's two endpoints. Replacing this
// file means implementing those six things over the real model.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The visual state a node is in, from <c>docs/help/concepts/design-language.md</c> §7.4. States
/// stack — a node can be selected and errored at once — which is why this is a flags enum rather
/// than an ordered one.
/// </summary>
[Flags]
public enum PlaceholderNodeState
{
    /// <summary>Nothing in particular.</summary>
    None = 0,

    /// <summary>Selected: body steps down the ladder and a 2 px accent ring is drawn.</summary>
    Selected = 1,

    /// <summary>The anchor of a multi-selection: as selected, plus four accent corner ticks.</summary>
    Anchor = 2,

    /// <summary>Warning: a 2 px <c>state.warning</c> ring outside the node, and a glyph.</summary>
    Warning = 4,

    /// <summary>Error: a 2 px <c>state.error</c> ring outside the node, and a glyph.</summary>
    Error = 8,
}

/// <summary>
/// <b>Temporary.</b> A node on the canvas, holding only what the renderer draws. Replaced by the
/// graph engine's model.
/// </summary>
public sealed class PlaceholderNode
{
    /// <summary>The default node width in world units, which are device-independent pixels at 100% zoom.</summary>
    public const double DefaultWidth = 168;

    /// <summary>The header height at 100% zoom, from §7.1.</summary>
    public const double HeaderHeight = 22;

    /// <summary>The vertical pitch of one port row.</summary>
    public const double PortPitch = 18;

    /// <summary>Padding below the last port row.</summary>
    public const double BodyPadding = 10;

    /// <summary>Creates a node.</summary>
    /// <param name="id">A stable identifier, used as the viewport's <c>NodeId</c>.</param>
    /// <param name="title">The name drawn in the header.</param>
    /// <param name="category">The library category, which decides the header colour.</param>
    /// <param name="x">The left edge in world coordinates.</param>
    /// <param name="y">The top edge in world coordinates.</param>
    /// <param name="inputs">Input port names, top to bottom on the left edge.</param>
    /// <param name="outputs">Output port names, top to bottom on the right edge.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    public PlaceholderNode(
        string id,
        string title,
        NodeCategory category,
        double x,
        double y,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);

        Id = id;
        Title = title;
        Category = category;
        X = x;
        Y = y;
        Inputs = inputs;
        Outputs = outputs;
    }

    /// <summary>A stable identifier.</summary>
    public string Id { get; }

    /// <summary>The name drawn in the header.</summary>
    public string Title { get; }

    /// <summary>The library category.</summary>
    public NodeCategory Category { get; }

    /// <summary>The left edge in world coordinates.</summary>
    public double X { get; set; }

    /// <summary>The top edge in world coordinates.</summary>
    public double Y { get; set; }

    /// <summary>The node's width in world units.</summary>
    public double Width { get; set; } = DefaultWidth;

    /// <summary>Input port names, top to bottom.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>Output port names, top to bottom.</summary>
    public IReadOnlyList<string> Outputs { get; }

    /// <summary>The node's visual state.</summary>
    public PlaceholderNodeState State { get; set; }

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

/// <summary>
/// <b>Temporary.</b> Identifies one port on one node. Replaced by the engine's own port identity.
/// </summary>
/// <param name="NodeIndex">The node's index in <see cref="PlaceholderGraph.Nodes"/>.</param>
/// <param name="PortIndex">The zero-based port index within its side.</param>
/// <param name="IsOutput">True for an output port on the node's right edge.</param>
public readonly record struct PlaceholderPort(int NodeIndex, int PortIndex, bool IsOutput);

/// <summary>
/// <b>Temporary.</b> A connection between an output port and an input port.
/// </summary>
/// <param name="From">The output end.</param>
/// <param name="To">The input end.</param>
public readonly record struct PlaceholderWire(PlaceholderPort From, PlaceholderPort To);

/// <summary>
/// <b>Temporary.</b> A whole graph: nodes in draw order, plus wires. Replaced by the engine's
/// document model.
/// </summary>
public sealed class PlaceholderGraph
{
    private readonly List<PlaceholderNode> _nodes = [];
    private readonly List<PlaceholderWire> _wires = [];

    /// <summary>The nodes, in draw order — index 0 is at the bottom.</summary>
    public IReadOnlyList<PlaceholderNode> Nodes => _nodes;

    /// <summary>The wires.</summary>
    public IReadOnlyList<PlaceholderWire> Wires => _wires;

    /// <summary>Appends a node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>Its index, which is its draw order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    public int Add(PlaceholderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    /// <summary>
    /// Adds a wire, refusing one that would connect a port to itself or run output-to-output.
    /// </summary>
    /// <param name="wire">The wire.</param>
    /// <returns>True when the wire was accepted.</returns>
    /// <remarks>
    /// This is the placeholder's stand-in for the engine's type check. The real one reports
    /// accepted, accepted-with-a-lossy-conversion or refused, and the canvas already draws those
    /// three cases — see <c>GraphCanvas</c>'s drag feedback.
    /// </remarks>
    public bool AddWire(PlaceholderWire wire)
    {
        if (!wire.From.IsOutput || wire.To.IsOutput || wire.From.NodeIndex == wire.To.NodeIndex)
        {
            return false;
        }

        for (int i = 0; i < _wires.Count; i++)
        {
            if (_wires[i].To == wire.To)
            {
                // An input takes one wire. Replacing rather than refusing matches what every node
                // editor does and is what a user reconnecting an input expects.
                _wires[i] = wire;
                return true;
            }
        }

        _wires.Add(wire);
        return true;
    }

    /// <summary>Whether an input port already has a wire into it.</summary>
    /// <param name="port">The port to test.</param>
    /// <returns>True when a wire terminates at the port.</returns>
    public bool IsConnected(PlaceholderPort port)
    {
        foreach (PlaceholderWire wire in _wires)
        {
            if (wire.From == port || wire.To == port)
            {
                return true;
            }
        }

        return false;
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

        foreach (PlaceholderNode node in _nodes)
        {
            CanvasBounds bounds = node.Bounds;
            minX = System.Math.Min(minX, bounds.MinX);
            minY = System.Math.Min(minY, bounds.MinY);
            maxX = System.Math.Max(maxX, bounds.MaxX);
            maxY = System.Math.Max(maxY, bounds.MaxY);
        }

        return new CanvasBounds(minX, minY, maxX, maxY);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spark.Api;
using Spark.Engine;

namespace Spark.UI.Graph;

/// <summary>One end of a wire in node-identity terms, which survives slot renumbering.</summary>
/// <param name="Node">The node.</param>
/// <param name="Port">The port index within its side.</param>
public readonly record struct GraphPort(NodeId Node, int Port);

/// <summary>
/// What collapsing a selection would produce, worked out before anything is changed.
/// </summary>
/// <param name="Definition">The custom node, ready to write as a <c>.sparkcustom</c> file.</param>
/// <param name="InputSources">
/// For each input port, in order, the <b>outer</b> output port that feeds it.
/// </param>
/// <param name="OutputTargets">
/// For each output port, in order, the <b>outer</b> input ports it feeds. A list, because one
/// output may have fanned out to several nodes.
/// </param>
/// <param name="Absorbed">The nodes that move inside the definition and leave the outer graph.</param>
/// <param name="X">Where the new node goes: the centre of what it replaced.</param>
/// <param name="Y">Where the new node goes.</param>
public sealed record CollapsePlan(
    CustomNodeDocument Definition,
    IReadOnlyList<GraphPort> InputSources,
    IReadOnlyList<IReadOnlyList<GraphPort>> OutputTargets,
    IReadOnlyList<NodeId> Absorbed,
    double X,
    double Y);

/// <summary>
/// Turns a selection into a custom node (<c>E7-T12</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The interface is inferred from the wires that crossed the boundary</b>, which is the whole
/// of the feature. A user selects a working piece of a graph and asks for it to become a node;
/// what that node's ports are is not a question they should have to answer, because the graph
/// already answered it. Anything wired in from outside is an input; anything read from outside is
/// an output; anything wired entirely within the selection is now private.
/// </para>
/// <para>
/// <b>One input port per distinct external source, not per crossing wire.</b> One node feeding
/// three ports inside the selection is one value arriving, and giving it three ports would make
/// the user wire the same thing three times. One output port per distinct inner source, for the
/// mirror reason.
/// </para>
/// <para>
/// <b>Split into planning and applying on purpose.</b> The inference is the part with the
/// judgement in it and the part worth testing; applying is bookkeeping. Planning changes nothing,
/// so a caller can show a user what they are about to get.
/// </para>
/// <para>
/// <b>Everything is expressed in <see cref="NodeId"/>s rather than canvas slots</b>, because
/// removing the absorbed nodes renumbers every slot after them and a plan holding slot indices
/// would rewire whichever nodes happened to move into them.
/// </para>
/// </remarks>
public static class CanvasCollapse
{
    /// <summary>How far to the side of the definition graph the Input and Output nodes sit.</summary>
    private const double PortColumnOffset = 220.0;

    /// <summary>
    /// Works out what collapsing a selection would produce, changing nothing.
    /// </summary>
    /// <param name="graph">The canvas graph.</param>
    /// <param name="slots">The selected canvas slots.</param>
    /// <param name="identity">What the new node should be called.</param>
    /// <returns>
    /// The plan, or <see langword="null"/> when the selection names no node that exists or would
    /// produce a node with no outputs at all.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <b>A selection nothing reads produces no output port, and that is refused rather than
    /// papered over.</b> A node with no outputs cannot be wired to anything, so it could never be
    /// used and its creation would look like a bug. The caller is expected to say so.
    /// </remarks>
    public static CollapsePlan? Plan(
        CanvasGraph graph, IReadOnlyCollection<int> slots, CustomNodeInterface identity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(identity);

        HashSet<NodeId> selected = [];
        List<CanvasNode> chosen = [];

        foreach (int slot in slots)
        {
            if (slot >= 0 && slot < graph.Nodes.Count && selected.Add(graph.Nodes[slot].Id))
            {
                chosen.Add(graph.Nodes[slot]);
            }
        }

        if (chosen.Count == 0)
        {
            return null;
        }

        List<Wire> internalWires = [];
        List<Wire> incoming = [];
        List<Wire> outgoing = [];

        foreach (Wire wire in graph.Engine.Wires())
        {
            bool sourceIn = selected.Contains(wire.Source);
            bool targetIn = selected.Contains(wire.Target);

            if (sourceIn && targetIn)
            {
                internalWires.Add(wire);
            }
            else if (targetIn)
            {
                incoming.Add(wire);
            }
            else if (sourceIn)
            {
                outgoing.Add(wire);
            }
        }

        // One port per distinct crossing *source*, in a stable order, so that collapsing the same
        // selection twice produces the same interface.
        List<GraphPort> inputSources = [];
        Dictionary<GraphPort, int> inputOf = [];
        foreach (Wire wire in incoming.OrderBy(Key))
        {
            GraphPort source = new(wire.Source, wire.SourcePort);
            if (!inputOf.ContainsKey(source))
            {
                inputOf[source] = inputSources.Count;
                inputSources.Add(source);
            }
        }

        List<GraphPort> outputSources = [];
        Dictionary<GraphPort, int> outputOf = [];
        List<List<GraphPort>> outputTargets = [];
        foreach (Wire wire in outgoing.OrderBy(Key))
        {
            GraphPort source = new(wire.Source, wire.SourcePort);
            if (!outputOf.TryGetValue(source, out int index))
            {
                index = outputSources.Count;
                outputOf[source] = index;
                outputSources.Add(source);
                outputTargets.Add([]);
            }

            outputTargets[index].Add(new GraphPort(wire.Target, wire.TargetPort));
        }

        if (outputSources.Count == 0)
        {
            return null;
        }

        CustomNodeDocument definition = BuildDefinition(
            graph, chosen, internalWires, incoming, inputOf, outputSources, identity);

        return new CollapsePlan(
            definition,
            inputSources,
            [.. outputTargets],
            [.. chosen.Select(node => node.Id)],
            chosen.Average(node => node.X),
            chosen.Average(node => node.Y));
    }

    /// <summary>
    /// Applies a plan: adds the new node, reconnects the wires that crossed the boundary, and
    /// removes the nodes that moved inside.
    /// </summary>
    /// <param name="graph">The canvas graph.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="definition">
    /// The compiled custom node, from <see cref="CustomNodeLibrary"/>. Passed in rather than built
    /// here because compiling it is what discovers recursion, and a caller has to be able to
    /// report that before anything is removed.
    /// </param>
    /// <returns>The new node's canvas slot.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <b>The new node is added and wired before anything is removed.</b> Removing first would
    /// renumber the slots the plan's remaining work refers to, and would leave the graph briefly
    /// missing both the old nodes and the new one — which is the state an interrupted edit would
    /// be caught in.
    /// </remarks>
    public static int Apply(CanvasGraph graph, CollapsePlan plan, NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(definition);

        int slot = graph.Add(definition, plan.X, plan.Y);
        NodeId created = graph.Nodes[slot].Id;

        for (int port = 0; port < plan.InputSources.Count; port++)
        {
            GraphPort source = plan.InputSources[port];
            graph.Engine.LoadWire(source.Node, source.Port, created, port);
        }

        for (int port = 0; port < plan.OutputTargets.Count; port++)
        {
            foreach (GraphPort target in plan.OutputTargets[port])
            {
                graph.Engine.LoadWire(created, port, target.Node, target.Port);
            }
        }

        foreach (NodeId absorbed in plan.Absorbed)
        {
            int at = graph.SlotOf(absorbed);
            if (at >= 0)
            {
                graph.Remove(at);
            }
        }

        graph.InvalidateWires();
        return graph.SlotOf(created);
    }

    /// <summary>
    /// Builds the definition graph: the selected nodes at their own identities, the wires between
    /// them, and an Input or Output node for every crossing.
    /// </summary>
    /// <remarks>
    /// A real <see cref="Spark.Engine.Graph"/> is constructed and then captured, rather than
    /// <see cref="GraphDocumentNode"/>s being written by hand. <see cref="GraphDocument.Capture"/>
    /// already knows to suppress a literal that equals its port's default and to refuse one a file
    /// cannot represent; reimplementing either here would be a second copy of a rule that is only
    /// correct once.
    /// </remarks>
    private static CustomNodeDocument BuildDefinition(
        CanvasGraph graph,
        IReadOnlyList<CanvasNode> chosen,
        IReadOnlyList<Wire> internalWires,
        IReadOnlyList<Wire> incoming,
        IReadOnlyDictionary<GraphPort, int> inputOf,
        IReadOnlyList<GraphPort> outputSources,
        CustomNodeInterface identity)
    {
        Spark.Engine.Graph body = new();
        Dictionary<NodeId, (double X, double Y)> positions = [];

        foreach (CanvasNode node in chosen)
        {
            NodeInstance instance = graph.Engine.Node(node.Id);
            body.AddNode(instance.Definition, node.Id);
            body.SetLacing(node.Id, instance.Lacing);

            IReadOnlyList<object?> literals = instance.Literals();
            for (int port = 0; port < literals.Count; port++)
            {
                if (literals[port] is { } value)
                {
                    body.SetLiteral(node.Id, port, value);
                }
            }

            positions[node.Id] = (node.X, node.Y);
        }

        foreach (Wire wire in internalWires)
        {
            body.LoadWire(wire.Source, wire.SourcePort, wire.Target, wire.TargetPort);
        }

        double left = chosen.Min(node => node.X) - PortColumnOffset;
        double right = chosen.Max(node => node.X) + PortColumnOffset;

        // The port *order* is the canvas order of these nodes -- CustomNodePorts.Collect sorts by Y
        // then X -- so their positions are the interface, not decoration. Each one is placed at the
        // height of what it connects to, which is also where a reader would expect to find it.
        Dictionary<int, NodeId> inputNodes = [];
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        foreach (Wire wire in incoming)
        {
            int port = inputOf[new GraphPort(wire.Source, wire.SourcePort)];
            if (!inputNodes.TryGetValue(port, out NodeId inputId))
            {
                inputId = body.AddNode(CustomNodePorts.Input).Id;
                inputNodes[port] = inputId;

                string name = Unique(
                    PortName(graph, wire.Target, wire.TargetPort, isInput: true), used);

                body.SetLiteral(inputId, CustomNodePorts.NamePort, name);
                positions[inputId] = (left, positions[wire.Target].Y + wire.TargetPort);
            }

            body.LoadWire(inputId, 0, wire.Target, wire.TargetPort);
        }

        used.Clear();
        for (int port = 0; port < outputSources.Count; port++)
        {
            GraphPort source = outputSources[port];
            NodeId outputId = body.AddNode(CustomNodePorts.Output).Id;

            string name = Unique(PortName(graph, source.Node, source.Port, isInput: false), used);

            body.SetLiteral(outputId, CustomNodePorts.NamePort, name);
            body.LoadWire(source.Node, source.Port, outputId, CustomNodePorts.ValuePort);
            positions[outputId] = (right, positions[source.Node].Y + source.Port);
        }

        return new CustomNodeDocument(
            identity,
            GraphDocument.Capture(body, id => positions.TryGetValue(id, out (double X, double Y) at) ? at : (0.0, 0.0)));
    }

    /// <summary>
    /// The name of the inner port a crossing wire attaches to, which is what the new port is
    /// called.
    /// </summary>
    /// <remarks>
    /// A user who selected a <c>Circle.ByCentreRadius</c> and wired a number into its
    /// <c>radius</c> expects the resulting node to have a port called <c>radius</c>. Inventing
    /// <c>in0</c> would be correct and unhelpful.
    /// </remarks>
    private static string PortName(CanvasGraph graph, NodeId node, int port, bool isInput)
    {
        if (!graph.Engine.TryGetNode(node, out NodeInstance? instance) || instance is null)
        {
            return isInput ? "input" : "output";
        }

        IReadOnlyList<PortDefinition> ports = isInput ? instance.Definition.Inputs : instance.Definition.Outputs;
        return port >= 0 && port < ports.Count ? ports[port].Name : (isInput ? "input" : "output");
    }

    /// <summary>
    /// Makes a port name unique within its side by appending a number.
    /// </summary>
    /// <remarks>
    /// Two selected nodes may both have a <c>radius</c>, and two ports called the same thing is
    /// not an error the definition format refuses — it is simply confusing to use. Numbering the
    /// second is better than renaming both, because the first one keeps the name a user recognises.
    /// </remarks>
    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name))
        {
            return name;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = name + suffix.ToString(CultureInfo.InvariantCulture);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>A stable sort key, so the same selection always produces the same interface.</summary>
    private static string Key(Wire wire) => string.Create(
        CultureInfo.InvariantCulture,
        $"{wire.Source.Value}:{wire.SourcePort:D4}:{wire.Target.Value}:{wire.TargetPort:D4}");
}

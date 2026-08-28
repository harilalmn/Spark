using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Engine;

/// <summary>
/// One node as it appears in a `.spark` file: its identity, which definition it is, how it laces,
/// where it sits on the canvas, and the values typed into its unwired inputs.
/// </summary>
/// <param name="Id">The node's stable identity, which survives save and load.</param>
/// <param name="Key">
/// The package-qualified definition key. This is what binds the node back to a definition on load,
/// and it is why <see cref="NodeKey"/> carries package identity rather than a bare name.
/// </param>
/// <param name="Lacing">
/// The lacing the user chose, which may be <see cref="LacingMode.Auto"/>. The *effective* lacing is
/// deliberately not stored: it is derived from the definition, so persisting it would let a file
/// disagree with the node library it is opened against.
/// </param>
/// <param name="X">The node's left edge on the canvas.</param>
/// <param name="Y">The node's top edge on the canvas.</param>
/// <param name="Literals">The values typed into unwired input ports, sparsely — only ports that have one.</param>
public sealed record GraphDocumentNode(
    NodeId Id,
    NodeKey Key,
    LacingMode Lacing,
    double X,
    double Y,
    IReadOnlyList<GraphLiteral> Literals);

/// <summary>One wire in a `.spark` file.</summary>
/// <param name="Source">The node the wire leaves.</param>
/// <param name="SourcePort">The output port index.</param>
/// <param name="Target">The node the wire reaches.</param>
/// <param name="TargetPort">The input port index.</param>
public readonly record struct GraphDocumentWire(
    NodeId Source, int SourcePort, NodeId Target, int TargetPort);

/// <summary>One value typed into an unwired input port.</summary>
/// <param name="PortIndex">Which input port it belongs to.</param>
/// <param name="Value">
/// The value. Only the kinds a port literal can actually hold are supported —
/// <see cref="bool"/>, <see cref="long"/>, <see cref="int"/>, <see cref="double"/>,
/// <see cref="string"/> and <see cref="Angle"/> — and anything else is refused at save time rather
/// than written as something that will not come back the same.
/// </param>
public readonly record struct GraphLiteral(int PortIndex, object? Value);

/// <summary>
/// A whole graph in the shape a `.spark` file holds it: the data model, with no evaluation state,
/// no results and no geometry.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the seam between the graph and the file.</b> <see cref="SparkFile"/> turns it
/// into canonical JSON and back, and knows nothing about <see cref="Graph"/>; this type moves
/// between the document and a live graph, and knows nothing about text. Keeping them apart is what
/// makes the JSON-to-JSON migrations [ADR-0017](../../docs/adr/0017-spark-file-is-plain-json.md)
/// requires possible — a migration rewrites text, and never has to construct a typed model from a
/// version whose types no longer exist.
/// </para>
/// <para>
/// <b>Canvas positions live here rather than on <see cref="NodeInstance"/>.</b> The engine has no
/// notion of a screen and should not acquire one, but a graph file plainly has to remember where
/// the user put things. The document carries the coordinates through without the engine ever
/// reading them, which is also why <c>spark run</c> will be able to load the same file with no UI
/// present at all.
/// </para>
/// </remarks>
public sealed class GraphDocument
{
    /// <summary>
    /// The format version this build writes, and the highest it can read.
    /// </summary>
    /// <remarks>
    /// A single monotonic integer, decoupled from the product version, per
    /// [ADR-0017](../../docs/adr/0017-spark-file-is-plain-json.md): a format change is a format
    /// change and a release is a release, and tying them together makes every release a format
    /// question.
    /// </remarks>
    public const int CurrentFormatVersion = 1;

    private readonly GraphDocumentNode[] _nodes;
    private readonly GraphDocumentWire[] _wires;

    /// <summary>Creates a document.</summary>
    /// <param name="formatVersion">The format version. Must be positive.</param>
    /// <param name="nodes">The nodes.</param>
    /// <param name="wires">The wires.</param>
    /// <exception cref="ArgumentNullException">Either collection is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="formatVersion"/> is not positive.</exception>
    public GraphDocument(
        int formatVersion,
        IEnumerable<GraphDocumentNode> nodes,
        IEnumerable<GraphDocumentWire> wires)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(wires);

        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(formatVersion), formatVersion, "A format version must be positive.");
        }

        FormatVersion = formatVersion;

        // Sorted here rather than at write time, so that two documents holding the same graph are
        // the same document however they were assembled. Node order in memory is a dictionary's
        // business and is not something a file should inherit.
        _nodes = [.. nodes.OrderBy(node => node.Id.Value.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)];
        _wires =
        [
            .. wires
                .OrderBy(wire => wire.Source.Value.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ThenBy(wire => wire.SourcePort)
                .ThenBy(wire => wire.Target.Value.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .ThenBy(wire => wire.TargetPort),
        ];
    }

    /// <summary>The format version this document was read from, or is to be written as.</summary>
    public int FormatVersion { get; }

    /// <summary>The nodes, ordered by identity so that the file's order never depends on memory order.</summary>
    public IReadOnlyList<GraphDocumentNode> Nodes => _nodes;

    /// <summary>The wires, ordered by their endpoints for the same reason.</summary>
    public IReadOnlyList<GraphDocumentWire> Wires => _wires;

    /// <summary>
    /// Captures a live graph as a document, taking canvas positions from a lookup the caller owns.
    /// </summary>
    /// <param name="graph">The graph to capture.</param>
    /// <param name="positions">
    /// Where each node sits, or <see langword="null"/> to write every node at the origin — which is
    /// what a headless caller with no canvas does.
    /// </param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">
    /// A literal holds a value no `.spark` file can represent. The message names the node and the
    /// port, because on a graph of two hundred nodes that is the only part a caller can act on.
    /// </exception>
    public static GraphDocument Capture(
        Graph graph, Func<NodeId, (double X, double Y)>? positions = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        List<GraphDocumentNode> nodes = [];
        foreach (NodeInstance instance in graph.Nodes())
        {
            (double x, double y) = positions?.Invoke(instance.Id) ?? (0.0, 0.0);

            List<GraphLiteral> literals = [];
            IReadOnlyList<object?> values = instance.Literals();
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] is not { } value)
                {
                    continue;
                }

                // A port that still holds its declared default is not written. The default is
                // reproducible from the definition on load, so writing it would be noise in the
                // diff and would couple the file to a value it does not need — and it would drag
                // in types that are not literals at all, because a port declared `Plane` is seeded
                // with one whether or not anybody typed anything.
                if (Equals(value, instance.Definition.Inputs[index].DefaultValue))
                {
                    continue;
                }

                if (!SparkFile.IsWritableLiteral(value))
                {
                    throw new SparkFileException(new SparkDiagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.UnwritableLiteral,
                        $"'{instance.Definition.DisplayName}' has a value on input {index} of type "
                        + $"{value.GetType().Name}, which a .spark file cannot represent.",
                        detail: "Only numbers, integers, true/false, text and angles can be typed "
                            + "into a port and saved.",
                        nodeId: instance.Id.Value,
                        portIndex: index,
                        helpTopicId: DiagnosticCodes.FileTopic));
                }

                literals.Add(new GraphLiteral(index, value));
            }

            nodes.Add(new GraphDocumentNode(
                instance.Id,
                instance.Definition.Key,
                instance.Lacing,
                x,
                y,
                literals));
        }

        List<GraphDocumentWire> wires =
        [
            .. graph.Wires().Select(wire =>
                new GraphDocumentWire(wire.Source, wire.SourcePort, wire.Target, wire.TargetPort)),
        ];

        return new GraphDocument(CurrentFormatVersion, nodes, wires);
    }

    /// <summary>
    /// Rebuilds a live graph from this document, binding each node to a definition in a library.
    /// </summary>
    /// <remarks>
    /// Wires are restored through <see cref="Graph.LoadWire"/> rather than
    /// <see cref="Graph.TryConnect"/>, because a file is not a gesture: a wire that a current
    /// library would refuse still has to load so that the user can see it and fix it. A file
    /// containing a cycle therefore opens, and every node on the cycle errors — which is the
    /// behaviour `E3-T7` asks for and the reason <c>LoadWire</c> exists.
    /// </remarks>
    /// <param name="library">The library to bind definitions from.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">
    /// A node names a definition the library does not have. Missing-package placeholders are M7
    /// (`E7`); until they exist, refusing loudly beats opening a graph with holes in it that look
    /// like the user's own doing.
    /// </exception>
    public Graph Restore(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        Graph graph = new();
        foreach (GraphDocumentNode node in _nodes)
        {
            if (!library.TryGet(node.Key, out NodeDefinition? definition) || definition is null)
            {
                throw new SparkFileException(new SparkDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.UnknownNodeDefinition,
                    $"No node called '{node.Key}' is loaded.",
                    detail: "The package that defines it is not installed, or the node has been "
                        + "renamed since this graph was saved.",
                    nodeId: node.Id.Value,
                    helpTopicId: DiagnosticCodes.FileTopic));
            }

            graph.AddNode(definition, node.Id);
            graph.SetLacing(node.Id, node.Lacing);

            foreach (GraphLiteral literal in node.Literals)
            {
                graph.SetLiteral(node.Id, literal.PortIndex, literal.Value);
            }
        }

        foreach (GraphDocumentWire wire in _wires)
        {
            graph.LoadWire(wire.Source, wire.SourcePort, wire.Target, wire.TargetPort);
        }

        return graph;
    }
}

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
/// <param name="Script">
/// The source of a code block, or <see langword="null"/> for every other kind of node. A code
/// block's ports depend on what the user typed, so its definition cannot be looked up in a library
/// and has to be rebuilt from this when the file is opened.
/// </param>
/// <param name="Frozen">
/// Whether the node is frozen (<c>E7-T14</c>). Written only when true, so a file containing no
/// frozen nodes is byte-for-byte what it was before freezing existed.
/// </param>
/// <param name="InputTypes">
/// The types the user declared for a code block's input ports (<c>E6-T11</c>), sparsely — only
/// ports that have one, and empty for every other kind of node. Written only when non-empty, for
/// the reason <paramref name="Frozen"/> is.
/// </param>
public sealed record GraphDocumentNode(
    NodeId Id,
    NodeKey Key,
    LacingMode Lacing,
    double X,
    double Y,
    IReadOnlyList<GraphLiteral> Literals,
    string? Script = null,
    bool Frozen = false,
    IReadOnlyList<GraphInputType>? InputTypes = null)
{
    /// <summary>
    /// The declared input types, never null.
    /// </summary>
    /// <remarks>
    /// The constructor parameter is nullable so that every existing call site keeps compiling and
    /// keeps meaning what it meant. Callers read this.
    /// </remarks>
    public IReadOnlyList<GraphInputType> DeclaredInputTypes => InputTypes ?? [];
}

/// <summary>One type declared for a code block's input port (<c>E6-T11</c>).</summary>
/// <param name="Name">
/// The port's name. Named rather than indexed because a code block's port indices move when its
/// source gains an identifier, so an index would silently come to mean a different port.
/// </param>
/// <param name="Token">
/// The type, as one of <see cref="ScriptInputTypes"/>'s short tokens. A token rather than an
/// assembly-qualified name: the latter would bind a saved graph to an assembly version, and this
/// survives a rename, a move between assemblies and a framework bump.
/// </param>
public readonly record struct GraphInputType(string Name, string Token);

/// <summary>One wire in a `.spark` file.</summary>
/// <param name="Source">The node the wire leaves.</param>
/// <param name="SourcePort">The output port index.</param>
/// <param name="Target">The node the wire reaches.</param>
/// <param name="TargetPort">The input port index.</param>
public readonly record struct GraphDocumentWire(
    NodeId Source, int SourcePort, NodeId Target, int TargetPort);

/// <summary>
/// One note in a `.spark` file: a rectangle of text the user put on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// A note is a <b>canvas annotation, not a document object</b>. It has no <see cref="NodeId"/>, no
/// ports and no provenance, it is never evaluated, and nothing can be wired to it. Giving it a
/// place in <see cref="Graph"/> would put something that cannot evaluate into the evaluator's
/// model, so it lives here instead — beside the node coordinates, which is already this type's
/// precedent for data the file must remember and the engine must never read.
/// </para>
/// <para>
/// It carries a <see cref="Guid"/> of its own all the same, for the reason the nodes do: the file
/// is sorted by identity so that two files holding the same graph are the same bytes however they
/// were assembled. Sorting notes by their position or their text instead would make moving one, or
/// fixing a typo in it, reorder the file.
/// </para>
/// </remarks>
/// <param name="Id">The note's identity, which survives save and load.</param>
/// <param name="X">Its left edge on the canvas.</param>
/// <param name="Y">Its top edge on the canvas.</param>
/// <param name="Width">How wide it is.</param>
/// <param name="Height">How tall it is.</param>
/// <param name="Text">What it says. Never null; an empty note is a note the user has not typed in yet.</param>
public sealed record GraphDocumentNote(
    Guid Id, double X, double Y, double Width, double Height, string Text);

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
/// One group in a `.spark` file: a titled frame around a set of nodes.
/// </summary>
/// <remarks>
/// <para>
/// A group is the same kind of thing as a <see cref="GraphDocumentNote"/> — a canvas annotation
/// with no <see cref="NodeId"/> of its own, no ports and no provenance — and it is carried the same
/// way, beside the coordinates rather than inside <see cref="Graph"/>.
/// </para>
/// <para>
/// <b>It stores which nodes it contains, and not the rectangle it draws.</b> The rectangle is
/// derived from the members every time it is needed, so it can never drift from them. The
/// alternative — storing a rectangle and deciding membership by containment — makes a group
/// silently gain a node the moment somebody drags one across its edge, and silently lose one when
/// they drag it out. Membership that changes without being asked for is the thing users get burned
/// by in other editors, and it is not recoverable by looking at the file afterwards.
/// </para>
/// </remarks>
/// <param name="Id">The group's identity, which survives save and load.</param>
/// <param name="Title">What the group is called. Never null; an untitled group is a real state.</param>
/// <param name="Members">The nodes inside it, ordered by identity so the file is stable.</param>
public sealed record GraphDocumentGroup(Guid Id, string Title, IReadOnlyList<NodeId> Members);

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
    public const int CurrentFormatVersion = 3;

    /// <summary>
    /// The version a document writes when it contains nothing that needs a newer reader.
    /// </summary>
    /// <remarks>
    /// <b>The version written is the minimum version that can read the file, not a stamp of the
    /// build that wrote it.</b> That is forced rather than chosen:
    /// [ADR-0016](../../docs/adr/0016-no-dynamo-interoperability.md) requires a graph referencing a missing
    /// package to re-save <i>byte-identically</i>, and stamping every save with the current version
    /// would rewrite the first line of every version-1 graph in existence the first time it was
    /// opened. A file is therefore version 2 only if it actually contains something a version-1
    /// reader would silently drop.
    /// </remarks>
    public const int BaselineFormatVersion = 1;

    /// <summary>The first version whose reader understands notes.</summary>
    public const int NotesFormatVersion = 2;

    /// <summary>
    /// The first version whose reader understands a node carrying its own source.
    /// </summary>
    /// <remarks>
    /// Three rather than two, and the rule is the same one notes established: a version-2 reader
    /// does not know the <c>script</c> field exists, so it would open the file, show a code block
    /// with no code in it, and write the code away on the next save. Sharing a version with notes
    /// and groups would be convenient and wrong — they shipped, and a reader that shipped is a
    /// reader that exists.
    /// </remarks>
    public const int ScriptsFormatVersion = 3;

    /// <summary>
    /// The first version whose reader understands groups. The same as
    /// <see cref="NotesFormatVersion"/>, deliberately: groups and notes landed in the same week,
    /// and inventing a version 3 for the second of them would refuse a file to a reader that can
    /// in fact read it.
    /// </summary>
    public const int GroupsFormatVersion = 2;

    private readonly GraphDocumentNode[] _nodes;
    private readonly GraphDocumentWire[] _wires;
    private readonly GraphDocumentNote[] _notes;
    private readonly GraphDocumentGroup[] _groups;

    /// <summary>Creates a document.</summary>
    /// <param name="formatVersion">The format version. Must be positive.</param>
    /// <param name="nodes">The nodes.</param>
    /// <param name="wires">The wires.</param>
    /// <param name="notes">The canvas notes, if any.</param>
    /// <param name="groups">The canvas groups, if any.</param>
    /// <exception cref="ArgumentNullException">A collection is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="formatVersion"/> is not positive.</exception>
    public GraphDocument(
        int formatVersion,
        IEnumerable<GraphDocumentNode> nodes,
        IEnumerable<GraphDocumentWire> wires,
        IEnumerable<GraphDocumentNote>? notes = null,
        IEnumerable<GraphDocumentGroup>? groups = null)
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

        _notes =
        [
            .. (notes ?? []).OrderBy(
                note => note.Id.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal),
        ];

        // Members are sorted here too, not only the groups. A group whose members are listed in
        // the order the user happened to select them would make the same selection produce two
        // different files, which is exactly what canonical formatting exists to prevent.
        _groups =
        [
            .. (groups ?? [])
                .Select(group => group with
                {
                    Members =
                    [
                        .. group.Members.OrderBy(
                            id => id.Value.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal),
                    ],
                })
                .OrderBy(group => group.Id.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal),
        ];
    }

    /// <summary>The format version this document was read from, or is to be written as.</summary>
    public int FormatVersion { get; }

    /// <summary>The nodes, ordered by identity so that the file's order never depends on memory order.</summary>
    public IReadOnlyList<GraphDocumentNode> Nodes => _nodes;

    /// <summary>The wires, ordered by their endpoints for the same reason.</summary>
    public IReadOnlyList<GraphDocumentWire> Wires => _wires;

    /// <summary>The source of every code block in this document, in document order.</summary>
    /// <remarks>
    /// <b>What the trust decision is made about</b> (`E6-T16`). A Spark graph is executable code,
    /// and the honest way to say so is to show the user what would run before it runs — so this is
    /// read before <see cref="Restore"/> is called, not discovered inside it. It is also how a
    /// caller decides whether to touch Roslyn at all: a document with no scripts in it never asks
    /// for a factory, which is `E6-T14`.
    /// </remarks>
    public IReadOnlyList<string> Scripts()
    {
        List<string> sources = [];

        foreach (GraphDocumentNode node in _nodes)
        {
            if (node.Script is { } source)
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    /// <summary>Whether this document contains anything that would execute code.</summary>
    public bool HasScripts => Scripts().Count > 0;

    /// <summary>The canvas notes, ordered by identity. Empty for a graph that has none.</summary>
    public IReadOnlyList<GraphDocumentNote> Notes => _notes;

    /// <summary>The canvas groups, ordered by identity, for the same reason.</summary>
    public IReadOnlyList<GraphDocumentGroup> Groups => _groups;

    /// <summary>
    /// The lowest format version whose reader could load this document without losing anything.
    /// </summary>
    /// <param name="notes">How many notes the document carries.</param>
    /// <param name="groups">How many groups it carries.</param>
    /// <param name="scripts">How many of its nodes carry their own source.</param>
    /// <returns>
    /// <see cref="NotesFormatVersion"/> when there is anything a version-1 reader would drop,
    /// otherwise <see cref="BaselineFormatVersion"/>.
    /// </returns>
    /// <remarks>
    /// A version-1 reader does not know the <c>notes</c> array exists; it would open the file, show
    /// the graph, and drop every note the next time the user saved. Refusing to open is the honest
    /// outcome, and asking for it costs exactly this: writing 2 when, and only when, there is
    /// something a version-1 reader would throw away.
    /// </remarks>
    public static int MinimumReaderVersion(int notes, int groups = 0, int scripts = 0)
    {
        if (scripts > 0)
        {
            return ScriptsFormatVersion;
        }

        return notes > 0 || groups > 0 ? NotesFormatVersion : BaselineFormatVersion;
    }

    /// <summary>
    /// Captures a live graph as a document, taking canvas positions from a lookup the caller owns.
    /// </summary>
    /// <param name="graph">The graph to capture.</param>
    /// <param name="positions">
    /// Where each node sits, or <see langword="null"/> to write every node at the origin — which is
    /// what a headless caller with no canvas does.
    /// </param>
    /// <param name="notes">
    /// The canvas notes, or <see langword="null"/> for none. A headless caller has none, and a
    /// document with neither notes nor groups is written at <see cref="BaselineFormatVersion"/> so
    /// that it stays byte-identical to what earlier builds wrote.
    /// </param>
    /// <param name="groups">The canvas groups, or <see langword="null"/> for none.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">
    /// A literal holds a value no `.spark` file can represent. The message names the node and the
    /// port, because on a graph of two hundred nodes that is the only part a caller can act on.
    /// </exception>
    public static GraphDocument Capture(
        Graph graph,
        Func<NodeId, (double X, double Y)>? positions = null,
        IReadOnlyList<GraphDocumentNote>? notes = null,
        IReadOnlyList<GraphDocumentGroup>? groups = null)
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

            // A declaration for a type outside the catalogue is dropped rather than invented a
            // spelling for. Nothing can put one there through the panel; this is the guard for a
            // caller that reached the graph directly, and losing a setting beats writing a file
            // that will not read back the same.
            List<GraphInputType> declared = [];
            foreach ((string name, Type type) in instance.DeclaredInputTypes)
            {
                if (ScriptInputTypes.TokenFor(type) is { } token)
                {
                    declared.Add(new GraphInputType(name, token));
                }
            }

            // Sorted, so that two graphs holding the same declarations are the same bytes however
            // the dictionary happened to enumerate. This is the same reason the file sorts its
            // nodes by identity.
            declared.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            nodes.Add(new GraphDocumentNode(
                instance.Id,
                instance.Definition.Key,
                instance.Lacing,
                x,
                y,
                literals,
                instance.Definition.Script,
                instance.IsFrozen,
                declared.Count > 0 ? declared : null));
        }

        List<GraphDocumentWire> wires =
        [
            .. graph.Wires().Select(wire =>
                new GraphDocumentWire(wire.Source, wire.SourcePort, wire.Target, wire.TargetPort)),
        ];

        int scripts = 0;
        foreach (GraphDocumentNode node in nodes)
        {
            if (node.Script is not null)
            {
                scripts++;
            }
        }

        return new GraphDocument(
            MinimumReaderVersion(notes?.Count ?? 0, groups?.Count ?? 0, scripts), nodes, wires, notes, groups);
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
    /// <param name="scripts">
    /// How to turn a code block's source into a definition, or <see langword="null"/> when
    /// scripting is not available. A document containing a code block is then refused rather than
    /// opened with the node missing.
    /// </param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is <see langword="null"/>.</exception>
    /// <param name="missing">
    /// What to do about a node whose definition the library does not have.
    /// <see cref="MissingNodePolicy.Placeholder"/> by default, because the promise is that nobody's
    /// graph is ever damaged by opening it on a machine without a package (<c>E7-T6</c>).
    /// </param>
    /// <exception cref="SparkFileException">
    /// Under <see cref="MissingNodePolicy.Refuse"/>, a node names a definition the library does not
    /// have. Also, in either policy, the document contains a code block and no
    /// <paramref name="scripts"/> factory was supplied — that case is <b>not</b> placeholdered,
    /// because a code block's source is the node, and standing in for it would mean pretending to
    /// hold executable code that nothing can execute.
    /// </exception>
    public Graph Restore(
        NodeLibrary library,
        IScriptNodeFactory? scripts = null,
        MissingNodePolicy missing = MissingNodePolicy.Placeholder)
    {
        ArgumentNullException.ThrowIfNull(library);

        Graph graph = new();
        foreach (GraphDocumentNode node in _nodes)
        {
            NodeDefinition? definition;

            // A node carrying its own source is a code block, and its definition is built rather
            // than looked up: its ports are whatever the user's identifiers imply, so no library
            // could hold it.
            if (node.Script is { } source)
            {
                if (scripts is null)
                {
                    throw new SparkFileException(new SparkDiagnostic(
                        DiagnosticSeverity.Error,
                        DiagnosticCodes.UnknownNodeDefinition,
                        "This graph contains a code block and scripting is not available.",
                        detail: "A Spark graph is executable code. Opening one with scripting "
                            + "disabled refuses rather than dropping the executable parts, because "
                            + "a graph silently missing a node is worse than one that will not open.",
                        nodeId: node.Id.Value,
                        helpTopicId: DiagnosticCodes.FileTopic));
                }

                // THE DECLARATIONS ARE RESOLVED BEFORE THE BLOCK IS COMPILED, NOT AFTER.
                //
                // Compiling with `dynamic` and re-typing afterwards would work, but it compiles
                // the same script twice on every open of every graph that declares anything - and
                // the second compile is the one whose result is kept. Passing them in means the
                // definition is right the first time.
                //
                // An unrecognised token resolves to null and is skipped: a file written by a later
                // version of Spark costs the user that setting, not the graph.
                Dictionary<string, Type> declared = new(StringComparer.Ordinal);
                foreach (GraphInputType entry in node.DeclaredInputTypes)
                {
                    if (ScriptInputTypes.Resolve(entry.Token) is { } declaredType)
                    {
                        declared[entry.Name] = declaredType;
                    }
                }

                definition = NodeDefinition.FromScript(scripts.Create(source, declared), source);
                graph.AddNode(definition, node.Id);
                graph.SetLacing(node.Id, node.Lacing);
                _ = graph.SetFrozen(node.Id, node.Frozen);

                // Recorded on the instance as well, so that the declaration survives the next
                // rebuild - which happens as soon as a wire lands on the block.
                foreach ((string name, Type declaredType) in declared)
                {
                    graph.SetDeclaredInputType(node.Id, name, declaredType);
                }

                foreach (GraphLiteral literal in node.Literals)
                {
                    graph.SetLiteral(node.Id, literal.PortIndex, literal.Value);
                }

                continue;
            }

            if (!library.TryGet(node.Key, out definition) || definition is null)
            {
                if (missing == MissingNodePolicy.Refuse)
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

                definition = PlaceholderNode.For(node.Key, InputsUsedBy(node), OutputsUsedBy(node));
            }

            graph.AddNode(definition, node.Id);
            graph.SetLacing(node.Id, node.Lacing);
            _ = graph.SetFrozen(node.Id, node.Frozen);

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

    /// <summary>
    /// How many input ports the file actually uses on one node: one past the highest literal
    /// index and the highest incoming wire's target port.
    /// </summary>
    /// <remarks>
    /// This is the only evidence of a missing node's shape there is. The definition is absent —
    /// that is the situation — so the graph's own usage has to stand in for it, and a placeholder
    /// exactly wide enough to hold what is there is the precise condition for a byte-identical
    /// re-save (<c>E7-T7</c>).
    /// </remarks>
    private int InputsUsedBy(GraphDocumentNode node)
    {
        int count = 0;
        foreach (GraphLiteral literal in node.Literals)
        {
            count = Math.Max(count, literal.PortIndex + 1);
        }

        foreach (GraphDocumentWire wire in _wires)
        {
            if (wire.Target == node.Id)
            {
                count = Math.Max(count, wire.TargetPort + 1);
            }
        }

        return count;
    }

    /// <summary>How many output ports the file uses on one node: one past the highest wire source port.</summary>
    private int OutputsUsedBy(GraphDocumentNode node)
    {
        int count = 0;
        foreach (GraphDocumentWire wire in _wires)
        {
            if (wire.Source == node.Id)
            {
                count = Math.Max(count, wire.SourcePort + 1);
            }
        }

        return count;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// The two node types that give a custom node its ports (<c>E7-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A custom node's interface is not declared, it is drawn.</b> Placing an
/// <see cref="InputKey">Input</see> node inside a definition graph adds an input port; placing an
/// <see cref="OutputKey">Output</see> node adds an output. There is no separate list of ports to
/// keep in step with the graph, because a list that can disagree with the graph eventually will.
/// </para>
/// <para>
/// <b>Port order is the order they appear on the canvas</b>, top to bottom and then left to
/// right. Any rule would do as long as it is stable; this one is the only rule a user can predict
/// without being told it, because it is what they already see.
/// </para>
/// </remarks>
public static class CustomNodePorts
{
    /// <summary>The package every custom-node port type belongs to.</summary>
    public const string Package = "Spark.Custom";

    /// <summary>The index of the port carrying a port's name on both Input and Output nodes.</summary>
    public const int NamePort = 0;

    /// <summary>The index of the port carrying the value on an Output node.</summary>
    public const int ValuePort = 1;

    /// <summary>The key of the Input node.</summary>
    public static NodeKey InputKey { get; } = new(Package, "Input");

    /// <summary>The key of the Output node.</summary>
    public static NodeKey OutputKey { get; } = new(Package, "Output");

    /// <summary>
    /// The Input node's definition. Its <c>value</c> port is filled in by the custom node at
    /// invocation time and is never wired by the user, which is why it is last: a port a user
    /// cannot use should not be the first thing they see.
    /// </summary>
    public static NodeDefinition Input { get; } = new(
        InputKey,
        "Input",
        [
            new PortDefinition("name", typeof(string), 0, "What this port is called on the custom node.", defaultValue: "input"),
            new PortDefinition("value", typeof(object), 0, "Supplied by the custom node when it runs.", keepStructure: true),
        ],
        [new PortDefinition("value", typeof(object), 0, "The value passed into this port.", keepStructure: true)],
        arguments => [arguments.Length > ValuePort ? arguments[ValuePort] : null],
        description: "Adds an input port to the custom node this graph defines.",
        category: NodeCategories.Input);

    /// <summary>
    /// The Output node's definition. It passes its value through as well as reporting it, so an
    /// output can be inspected on the canvas like anything else while it is being built.
    /// </summary>
    public static NodeDefinition Output { get; } = new(
        OutputKey,
        "Output",
        [
            new PortDefinition("name", typeof(string), 0, "What this port is called on the custom node.", defaultValue: "output"),
            new PortDefinition("value", typeof(object), 0, "The value this port returns.", keepStructure: true),
        ],
        [new PortDefinition("value", typeof(object), 0, "The same value, so it can be previewed here.", keepStructure: true)],
        arguments => [arguments.Length > ValuePort ? arguments[ValuePort] : null],
        description: "Adds an output port to the custom node this graph defines.",
        category: NodeCategories.Display);

    /// <summary>Adds both port types to a library.</summary>
    /// <param name="library">The library to add them to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    /// <remarks>
    /// They belong in every library that will open a <c>.sparkcustom</c> file — which, because
    /// graph-in-graph is the same mechanism, is every library that will open a graph containing a
    /// custom node.
    /// </remarks>
    public static void AddTo(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        if (!library.TryGet(InputKey, out _))
        {
            library.Add(Input);
        }

        if (!library.TryGet(OutputKey, out _))
        {
            library.Add(Output);
        }
    }

    /// <summary>
    /// Finds the Input or Output nodes in a definition graph, in canvas order, with the names the
    /// user typed.
    /// </summary>
    /// <param name="document">The definition graph.</param>
    /// <param name="key">Which port type to collect.</param>
    /// <returns>The node ids and port names, ordered top to bottom then left to right.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <remarks>
    /// A port whose name literal is blank or missing falls back to <c>in0</c>, <c>out1</c> and so
    /// on. <b>Duplicate names are left alone rather than rejected</b>: two ports called the same
    /// thing are confusing, but refusing to open a graph over it would be worse, and the user can
    /// see both of them and fix it.
    /// </remarks>
    public static IReadOnlyList<(NodeId Id, string Name)> Collect(GraphDocument document, NodeKey key)
    {
        ArgumentNullException.ThrowIfNull(document);

        string prefix = key == InputKey ? "in" : "out";
        List<(NodeId Id, string Name, double X, double Y)> found = [];

        foreach (GraphDocumentNode node in document.Nodes)
        {
            if (node.Key != key)
            {
                continue;
            }

            string? name = node.Literals
                .FirstOrDefault(literal => literal.PortIndex == NamePort).Value as string;

            found.Add((node.Id, name ?? string.Empty, node.X, node.Y));
        }

        List<(NodeId, string)> ordered = [];
        int index = 0;
        foreach ((NodeId id, string name, _, _) in found.OrderBy(f => f.Y).ThenBy(f => f.X))
        {
            ordered.Add((
                id,
                string.IsNullOrWhiteSpace(name)
                    ? prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : name));
            index++;
        }

        return ordered;
    }
}

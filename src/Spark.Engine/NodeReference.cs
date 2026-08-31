using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Spark.Api;
using Spark.Api.Help;

namespace Spark.Engine;

/// <summary>
/// Builds a help page for every node, from the live node library (<c>E10-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Generated at runtime from the library, not written by hand and not committed as files.</b>
/// That is the strongest available form of the rule this epic exists to enforce: a page produced
/// from the definition it describes cannot drift from it, because there is no second copy to
/// drift. Add a node and it has a page; rename a port and the page renames with it; delete a node
/// and its page is gone. No build step, no stale file, nothing to forget.
/// </para>
/// <para>
/// <b>The content is already there and this only arranges it.</b> Every node's summary is its XML
/// doc comment and every port's description is its parameter's, because CS1591 is an error on
/// <c>Spark.Nodes.Core</c> — the build refuses to produce an assembly with an undocumented public
/// member. So the reference is complete on the day it is switched on, which is exactly what
/// <c>DocGenerator</c>'s 1,478 hand-maintained entries were not.
/// </para>
/// <para>
/// <b>What this is not.</b> A reference page tells a user what a node takes and returns. It does
/// not tell them why they would want it, which is what the hand-written concept topics in
/// <c>docs/help/</c> are for. Generating one does not remove the need for the other, and reading
/// this class as "the documentation is done" would be the mistake it is easiest to make.
/// </para>
/// </remarks>
public static class NodeReference
{
    /// <summary>The topic id prefix every generated node page carries.</summary>
    public const string TopicPrefix = "nodes.";

    /// <summary>The topic id for a node key.</summary>
    /// <param name="key">The node key.</param>
    /// <returns>A stable id such as <c>nodes.Spark.Core/Point.ByCoordinates</c>.</returns>
    public static string TopicIdFor(NodeKey key) => TopicPrefix + key.Value;

    /// <summary>Builds the page for one node.</summary>
    /// <param name="definition">The node.</param>
    /// <returns>The topic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
    public static HelpDocument For(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<HelpBlock> blocks =
        [
            new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines(definition.DisplayName), 1),
        ];

        blocks.Add(new HelpBlock(
            HelpBlockKind.Paragraph,
            HelpMarkdown.ParseInlines(
                string.IsNullOrWhiteSpace(definition.Description)
                    ? "_No description. This node's summary comes from its XML doc comment; if this "
                        + "page is blank, that comment is._"
                    : definition.Description)));

        blocks.Add(new HelpBlock(HelpBlockKind.Table, rows: Facts(definition)));

        blocks.Add(new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines("Inputs"), 2));
        blocks.Add(definition.Inputs.Count == 0
            ? new HelpBlock(HelpBlockKind.Paragraph, HelpMarkdown.ParseInlines("This node takes nothing."))
            : new HelpBlock(HelpBlockKind.Table, rows: Ports(definition.Inputs, showDefaults: true)));

        blocks.Add(new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines("Outputs"), 2));
        blocks.Add(new HelpBlock(HelpBlockKind.Table, rows: Ports(definition.Outputs, showDefaults: false)));

        blocks.Add(new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines("Lacing"), 2));
        blocks.Add(new HelpBlock(HelpBlockKind.Paragraph, HelpMarkdown.ParseInlines(LacingSentence(definition))));

        return new HelpDocument(
            TopicIdFor(definition.Key),
            definition.DisplayName,
            blocks,
            nodes: [definition.Key.Value],
            related: ["concepts.lacing"]);
    }

    /// <summary>Builds a page for every node in a library, ordered by key.</summary>
    /// <param name="library">The library.</param>
    /// <returns>One topic per node, in a stable order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public static IReadOnlyList<HelpDocument> ForAll(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        return
        [
            .. library.Definitions()
                .OrderBy(d => d.Key.Value, StringComparer.Ordinal)
                .Select(For),
        ];
    }

    /// <summary>
    /// Builds the index page: every node, grouped by category, each one a link to its page.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <returns>The index topic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public static HelpDocument Index(NodeLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);

        IReadOnlyList<NodeDefinition> definitions = library.Definitions();

        List<HelpBlock> blocks =
        [
            new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines("Node reference"), 1),
            new HelpBlock(HelpBlockKind.Paragraph, HelpMarkdown.ParseInlines(
                "Every one of the "
                + definitions.Count.ToString(CultureInfo.InvariantCulture)
                + " nodes currently loaded, grouped by category. These pages are generated from the "
                + "nodes themselves, so they describe what is actually installed rather than what "
                + "was written down at some point.")),
        ];

        foreach (IGrouping<string, NodeDefinition> group in definitions
            .GroupBy(d => d.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            blocks.Add(new HelpBlock(HelpBlockKind.Heading, HelpMarkdown.ParseInlines(group.Key), 2));

            foreach (NodeDefinition definition in group.OrderBy(d => d.DisplayName, StringComparer.Ordinal))
            {
                List<HelpInline> line =
                [
                    new HelpInline(
                        HelpInlineKind.Link, definition.DisplayName, TopicIdFor(definition.Key)),
                ];

                if (!string.IsNullOrWhiteSpace(definition.Description))
                {
                    line.Add(new HelpInline(HelpInlineKind.Text, " — " + Summarise(definition.Description)));
                }

                blocks.Add(new HelpBlock(HelpBlockKind.ListItem, line));
            }
        }

        return new HelpDocument("nodes.index", "Node reference", blocks);
    }

    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpInline>>> Facts(NodeDefinition definition)
    {
        List<IReadOnlyList<IReadOnlyList<HelpInline>>> rows =
        [
            Row("", ""),
            Row("Key", "`" + definition.Key.Value + "`"),
            Row("Category", definition.Category),
        ];

        if (definition.IsSideEffect)
        {
            rows.Add(Row("Side effect", "Yes — this node acts on the world, so it runs even when nothing reads it."));
        }

        return rows;
    }

    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpInline>>> Ports(
        IReadOnlyList<PortDefinition> ports, bool showDefaults)
    {
        List<IReadOnlyList<IReadOnlyList<HelpInline>>> rows =
        [
            showDefaults ? Row("Port", "Type", "Default", "Description") : Row("Port", "Type", "Description"),
        ];

        foreach (PortDefinition port in ports)
        {
            string type = FriendlyTypeName(port.ValueType) + (port.KeepStructure ? " (any depth)" : string.Empty);
            string description = port.Description ?? string.Empty;

            rows.Add(showDefaults
                ? Row("`" + port.Name + "`", type, DescribeDefault(port), description)
                : Row("`" + port.Name + "`", type, description));
        }

        return rows;
    }

    private static string DescribeDefault(PortDefinition port) => port.DefaultValue switch
    {
        null => "—",
        string text => "\"" + text + "\"",
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        object value => value.ToString() ?? "—",
    };

    /// <summary>
    /// A readable type name. <c>Double</c> rather than <c>System.Double</c>, and <c>list of
    /// Point3d</c> rather than a generic arity — a reader looking at a port wants to know what to
    /// wire into it, not what the CLR calls it.
    /// </summary>
    private static string FriendlyTypeName(Type type)
    {
        if (type == typeof(double))
        {
            return "number";
        }

        if (type == typeof(int))
        {
            return "integer";
        }

        if (type == typeof(bool))
        {
            return "true/false";
        }

        if (type == typeof(string))
        {
            return "text";
        }

        if (type == typeof(object))
        {
            return "anything";
        }

        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            string name = type.Name;
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick > 0)
            {
                name = name[..tick];
            }

            return name + " of " + string.Join(", ", arguments.Select(FriendlyTypeName));
        }

        return type.Name;
    }

    private static string LacingSentence(NodeDefinition definition) => definition.DefaultLacing switch
    {
        LacingMode.Shortest =>
            "By default this node laces **Shortest**: given lists of different lengths it stops at "
            + "the end of the shortest one. See [Lists, ranks and lacing](concepts.lacing).",
        LacingMode.Longest =>
            "By default this node laces **Longest**: given lists of different lengths it repeats "
            + "the last item of the shorter ones. See [Lists, ranks and lacing](concepts.lacing).",
        LacingMode.CrossProduct =>
            "By default this node laces **Cross Product**: it produces every combination of the "
            + "inputs. See [Lists, ranks and lacing](concepts.lacing).",
        LacingMode.Disabled =>
            "This node does not replicate. It takes its lists whole. See "
            + "[Lists, ranks and lacing](concepts.lacing).",
        _ => "See [Lists, ranks and lacing](concepts.lacing).",
    };

    /// <summary>The first sentence of a summary, for a one-line index entry.</summary>
    private static string Summarise(string description)
    {
        int stop = description.IndexOf(". ", StringComparison.Ordinal);
        string first = stop > 0 ? description[..(stop + 1)] : description;
        return first.Length > 120 ? first[..117] + "…" : first;
    }

    private static IReadOnlyList<IReadOnlyList<HelpInline>> Row(params string[] cells)
    {
        List<IReadOnlyList<HelpInline>> row = new(cells.Length);
        foreach (string cell in cells)
        {
            row.Add(HelpMarkdown.ParseInlines(cell));
        }

        return row;
    }
}

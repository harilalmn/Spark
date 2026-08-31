using System;
using System.Text.Json;
using Spark.Api;

namespace Spark.Engine;

/// <summary>
/// What a custom node calls itself: the part of a <c>.sparkcustom</c> file that is not the graph.
/// </summary>
/// <param name="Package">
/// The package half of the node's key. For a node the user made themselves this is their own
/// namespace; for one that arrives inside a NuGet package it is the package's id.
/// </param>
/// <param name="Name">The name half of the key, and what appears on the canvas.</param>
/// <param name="Description">One sentence for the library and the tooltip, or null.</param>
/// <param name="Category">Which library category it files under, or null for Custom.</param>
/// <param name="ViewKey">
/// <b>Reserved, and deliberately unused (<c>E7-T15</c>).</b> Names the custom user interface a node
/// draws in place of its default body — a slider, a colour well, a small chart. Nothing reads it
/// yet, and nothing sets it.
/// <para>
/// It is in the format <i>now</i> because adding a property to a file format after people have
/// files is a migration, and adding one before they do is a line of code. A reader that already
/// carries the field through will open tomorrow's files today, and a writer that already
/// round-trips it will not quietly strip one written by a newer version.
/// </para>
/// </param>
public sealed record CustomNodeInterface(
    string Package,
    string Name,
    string? Description = null,
    string? Category = null,
    string? ViewKey = null)
{
    /// <summary>The node's key.</summary>
    public NodeKey Key => new(Package, Name);
}

/// <summary>A custom node: an interface block and the graph that implements it.</summary>
/// <param name="Interface">What the node calls itself and how it presents.</param>
/// <param name="Body">
/// The definition graph. An ordinary <see cref="GraphDocument"/> — <b>the same schema, not a
/// parallel one</b> — whose Input and Output nodes give the custom node its ports.
/// </param>
public sealed record CustomNodeDocument(CustomNodeInterface Interface, GraphDocument Body);

/// <summary>
/// Reads and writes <c>.sparkcustom</c> files (<c>E7-T11</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The format is the graph format plus one object.</b> A <c>.sparkcustom</c> file is exactly
/// what <see cref="SparkFile.Write"/> produces with an <c>"interface"</c> property added, and it
/// is read by the same reader. That is not a saving of effort, it is the point:
/// <i>graph-in-graph is the same mechanism, not a separate feature</i>, so a custom node's body
/// has to be an ordinary graph in an ordinary format or the two will drift and every tool will
/// need to know about both.
/// </para>
/// <para>
/// <b>A consequence worth stating:</b> <see cref="SparkFile.Read"/> ignores the extra property, so
/// a <c>.sparkcustom</c> file opens as a plain graph and the user sees the definition they wrote,
/// with its Input and Output nodes in place. That is a feature — it is how you edit one — and it
/// is only true because the two formats are one format.
/// </para>
/// </remarks>
public static class CustomNodeFile
{
    /// <summary>The file extension custom node definitions are saved under.</summary>
    public static string Extension => ".sparkcustom";

    /// <summary>Serialises a custom node.</summary>
    /// <param name="document">The interface and its body graph.</param>
    /// <returns>The file text, with a trailing newline, exactly as <see cref="SparkFile.Write"/> writes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static string Write(CustomNodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string graph = SparkFile.Write(document.Body);

        // Splice the interface in after the format version rather than re-serialising the graph
        // through a second writer. Two writers is two sets of formatting decisions, and the
        // byte-for-byte round trip this format promises would then depend on them agreeing.
        const string Anchor = "\"nodes\": [";
        int at = graph.IndexOf(Anchor, StringComparison.Ordinal);
        if (at < 0)
        {
            throw new SparkFileException("The graph writer produced a file with no nodes array.");
        }

        string block = BuildInterfaceBlock(document.Interface);
        return graph[..at] + block + graph[at..];
    }

    /// <summary>Reads a custom node.</summary>
    /// <param name="json">The file text.</param>
    /// <returns>The interface and its body graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="SparkFileException">
    /// The file is not valid JSON, has no <c>interface</c> block, or the block has no package or
    /// name. A custom node without a key cannot be put in a library or referenced from a graph, so
    /// there is nothing useful to do with a file missing one.
    /// </exception>
    public static CustomNodeDocument Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        GraphDocument body = SparkFile.Read(json);

        using JsonDocument parsed = Parse(json);
        JsonElement root = parsed.RootElement;

        if (!root.TryGetProperty("interface", out JsonElement block) || block.ValueKind != JsonValueKind.Object)
        {
            throw new SparkFileException(new SparkDiagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCodes.MalformedGraphFile,
                "This is a graph, not a custom node: it has no 'interface' block.",
                detail: "A .sparkcustom file is a .spark file plus an interface block naming the "
                    + "node. Without one there is no key to file it under.",
                helpTopicId: DiagnosticCodes.FileTopic));
        }

        string package = Text(block, "package")
            ?? throw Malformed("The custom node's interface has no 'package'.");
        string name = Text(block, "name")
            ?? throw Malformed("The custom node's interface has no 'name'.");

        return new CustomNodeDocument(
            new CustomNodeInterface(
                package,
                name,
                Text(block, "description"),
                Text(block, "category"),
                Text(block, "viewKey")),
            body);
    }

    private static string BuildInterfaceBlock(CustomNodeInterface node)
    {
        System.Text.StringBuilder text = new();
        text.Append("\"interface\": {\n");
        text.Append("    \"package\": ").Append(JsonSerializer.Serialize(node.Package)).Append(",\n");
        text.Append("    \"name\": ").Append(JsonSerializer.Serialize(node.Name));

        if (node.Description is not null)
        {
            text.Append(",\n    \"description\": ").Append(JsonSerializer.Serialize(node.Description));
        }

        if (node.Category is not null)
        {
            text.Append(",\n    \"category\": ").Append(JsonSerializer.Serialize(node.Category));
        }

        if (node.ViewKey is not null)
        {
            text.Append(",\n    \"viewKey\": ").Append(JsonSerializer.Serialize(node.ViewKey));
        }

        text.Append("\n  },\n  ");
        return text.ToString();
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            throw Malformed($"The file is not valid JSON: {error.Message}");
        }
    }

    private static string? Text(JsonElement block, string name) =>
        block.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static SparkFileException Malformed(string message) =>
        new(new SparkDiagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCodes.MalformedGraphFile,
            message,
            helpTopicId: DiagnosticCodes.FileTopic));
}

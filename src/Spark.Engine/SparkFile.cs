using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Engine;

/// <summary>
/// Raised when a `.spark` file cannot be written or cannot be read, carrying the diagnostic that
/// says why.
/// </summary>
public sealed class SparkFileException : Exception
{
    /// <summary>Creates the exception from a diagnostic.</summary>
    /// <param name="diagnostic">The diagnostic, whose message becomes the exception's.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diagnostic"/> is <see langword="null"/>.</exception>
    public SparkFileException(SparkDiagnostic diagnostic)
        : base(diagnostic?.Message ?? throw new ArgumentNullException(nameof(diagnostic))) =>
        Diagnostic = diagnostic;

    /// <summary>Creates the exception with a message. Provided for the framework's benefit.</summary>
    /// <param name="message">The message.</param>
    public SparkFileException(string message)
        : base(message) =>
        Diagnostic = new SparkDiagnostic(
            DiagnosticSeverity.Error, DiagnosticCodes.MalformedGraphFile, message);

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public SparkFileException(string message, Exception innerException)
        : base(message, innerException) =>
        Diagnostic = new SparkDiagnostic(
            DiagnosticSeverity.Error, DiagnosticCodes.MalformedGraphFile, message);

    /// <summary>Creates the exception with no message. Provided for the framework's benefit.</summary>
    public SparkFileException()
        : base("The graph file could not be read.") =>
        Diagnostic = new SparkDiagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCodes.MalformedGraphFile,
            "The graph file could not be read.");

    /// <summary>What went wrong, in the form the diagnostics panel shows.</summary>
    public SparkDiagnostic Diagnostic { get; }
}

/// <summary>
/// Reads and writes `.spark` files: plain JSON, canonically formatted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Canonical formatting is the feature, not the tidiness.</b>
/// [ADR-0017](../../docs/adr/0017-spark-file-is-plain-json.md) chose text over a container so that
/// graphs review like code — so that a pull request shows which node moved and which literal
/// changed. That benefit evaporates silently without stable ordering and invariant numbers:
/// opening an untouched graph and saving it would produce a diff of reordered keys and re-rendered
/// floats, and a diff that is noisy every time is a diff nobody reads. Every ordering decision here
/// exists to make <b>read-then-write byte-identical</b>, which is asserted by a test rather than
/// assumed.
/// </para>
/// <para>
/// <b>Keys are written in a fixed order by hand rather than by a serialiser.</b> Reflection-based
/// serialisation orders members by declaration, which means reordering two properties in a source
/// file silently changes every file this build writes. Writing the keys explicitly costs a few
/// lines and makes that impossible.
/// </para>
/// <para>
/// <b>A literal's kind is written next to it.</b> JSON cannot tell 1 from 1.0, and Spark's ports
/// are typed — a node expecting an integer and a node expecting a number are different bindings.
/// Storing the kind is what makes a round trip return the value the user typed rather than one that
/// merely prints the same.
/// </para>
/// </remarks>
public static class SparkFile
{
    private const string KindBoolean = "boolean";
    private const string KindInteger = "integer";
    private const string KindNumber = "number";
    private const string KindText = "text";
    private const string KindAngle = "angle";

    /// <summary>The extension a Spark graph file carries, including the dot.</summary>
    public static string Extension => ".spark";

    /// <summary>Whether a value is one a `.spark` file can hold.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it can be written and read back unchanged.</returns>
    public static bool IsWritableLiteral(object? value) =>
        value is null or bool or int or long or double or string or Angle;

    /// <summary>Writes a document as canonical JSON.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The file's text, ending in a single newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">A literal holds a value the format cannot represent.</exception>
    public static string Write(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using MemoryStream stream = new();
        JsonWriterOptions options = new()
        {
            Indented = true,
            IndentCharacter = ' ',
            IndentSize = 2,

            // A line feed, explicitly, because the default is Environment.NewLine — and a format
            // whose whole premise is a quiet diff cannot write CRLF on Windows and LF on Linux.
            // Left at the default, a graph saved on Windows and re-saved on Linux would produce a
            // diff of every line in the file while nothing about the graph had changed. Found by
            // git warning that the first committed example would be normalised on checkout.
            NewLine = "\n",
        };

        using (Utf8JsonWriter writer = new(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", document.FormatVersion);

            writer.WriteStartArray("nodes");
            foreach (GraphDocumentNode node in document.Nodes)
            {
                WriteNode(writer, node);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("wires");
            foreach (GraphDocumentWire wire in document.Wires)
            {
                writer.WriteStartObject();
                writer.WriteString("source", Format(wire.Source));
                writer.WriteNumber("sourcePort", wire.SourcePort);
                writer.WriteString("target", Format(wire.Target));
                writer.WriteNumber("targetPort", wire.TargetPort);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            // Omitted entirely when there are none, not written as an empty array. Every version-1
            // graph on disk has to re-save byte-identically (ADR-0016), and "notes": [] would put
            // two new lines into the diff of every graph that has never had a note in it.
            if (document.Notes.Count > 0)
            {
                writer.WriteStartArray("notes");
                foreach (GraphDocumentNote note in document.Notes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", note.Id.ToString("D", CultureInfo.InvariantCulture));
                    writer.WriteNumber("x", note.X);
                    writer.WriteNumber("y", note.Y);
                    writer.WriteNumber("width", note.Width);
                    writer.WriteNumber("height", note.Height);
                    writer.WriteString("text", note.Text);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            // Groups after notes, and omitted the same way when there are none. Both arrive at
            // version 2, so a file carrying either needs the same reader.
            if (document.Groups.Count > 0)
            {
                writer.WriteStartArray("groups");
                foreach (GraphDocumentGroup group in document.Groups)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", group.Id.ToString("D", CultureInfo.InvariantCulture));
                    writer.WriteString("title", group.Title);

                    writer.WriteStartArray("members");
                    foreach (NodeId member in group.Members)
                    {
                        writer.WriteStringValue(Format(member));
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        // A trailing newline, because every other text file in the repository has one and a file
        // without one produces a "\ No newline at end of file" marker in every diff that touches
        // its last line.
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Reads canonical JSON into a document.</summary>
    /// <param name="json">The file's text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="SparkFileException">
    /// The text is not JSON, is not a graph, or names a format version this build cannot read.
    /// </exception>
    public static GraphDocument Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            throw Malformed($"The file is not valid JSON: {error.Message}");
        }

        using (parsed)
        {
            JsonElement root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Malformed("The file's top level is not an object.");
            }

            if (!root.TryGetProperty("formatVersion", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int formatVersion))
            {
                throw Malformed("The file has no formatVersion.");
            }

            if (formatVersion > GraphDocument.CurrentFormatVersion)
            {
                throw new SparkFileException(new SparkDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.UnreadableFormatVersion,
                    $"This graph is saved in format version {formatVersion}, and this build reads "
                    + $"up to {GraphDocument.CurrentFormatVersion}.",
                    detail: "A newer version of Spark saved it. Nothing here can be recovered "
                        + "safely by guessing, so the file is refused rather than partly read.",
                    helpTopicId: DiagnosticCodes.FileTopic));
            }

            List<GraphDocumentNode> nodes = [];
            if (root.TryGetProperty("nodes", out JsonElement nodeArray)
                && nodeArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in nodeArray.EnumerateArray())
                {
                    nodes.Add(ReadNode(element));
                }
            }

            List<GraphDocumentWire> wires = [];
            if (root.TryGetProperty("wires", out JsonElement wireArray)
                && wireArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in wireArray.EnumerateArray())
                {
                    wires.Add(new GraphDocumentWire(
                        ReadId(element, "source"),
                        ReadInt(element, "sourcePort"),
                        ReadId(element, "target"),
                        ReadInt(element, "targetPort")));
                }
            }

            List<GraphDocumentNote> notes = [];
            if (root.TryGetProperty("notes", out JsonElement noteArray)
                && noteArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in noteArray.EnumerateArray())
                {
                    notes.Add(ReadNote(element));
                }
            }

            List<GraphDocumentGroup> groups = [];
            if (root.TryGetProperty("groups", out JsonElement groupArray)
                && groupArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in groupArray.EnumerateArray())
                {
                    groups.Add(ReadGroup(element));
                }
            }

            return new GraphDocument(formatVersion, nodes, wires, notes, groups);
        }
    }

    private static void WriteNode(Utf8JsonWriter writer, GraphDocumentNode node)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Format(node.Id));
        writer.WriteString("key", node.Key.Value);
        writer.WriteString("lacing", node.Lacing.ToString());

        // Written only when true. A graph nobody has frozen anything in therefore saves exactly as
        // it did before freezing existed, which is what keeps E7-T7's byte-for-byte round trip an
        // assertion about every file rather than about files written by this build.
        if (node.Frozen)
        {
            writer.WriteBoolean("frozen", value: true);
        }
        writer.WriteNumber("x", node.X);
        writer.WriteNumber("y", node.Y);

        // Written before the literals so the node's shape reads top-down: what it is, then what
        // was typed into it. A node with no script omits the field entirely, which keeps every
        // graph that has never held a code block byte-identical to what earlier builds wrote.
        if (node.Script is { } script)
        {
            writer.WriteString("script", script);
        }

        if (node.Literals.Count > 0)
        {
            writer.WriteStartArray("literals");
            foreach (GraphLiteral literal in node.Literals)
            {
                writer.WriteStartObject();
                writer.WriteNumber("port", literal.PortIndex);
                WriteLiteralValue(writer, literal, node);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteLiteralValue(
        Utf8JsonWriter writer, GraphLiteral literal, GraphDocumentNode node)
    {
        switch (literal.Value)
        {
            case bool flag:
                writer.WriteString("kind", KindBoolean);
                writer.WriteBoolean("value", flag);
                return;

            case int number:
                writer.WriteString("kind", KindInteger);
                writer.WriteNumber("value", number);
                return;

            case long number:
                writer.WriteString("kind", KindInteger);
                writer.WriteNumber("value", number);
                return;

            case double number:
                writer.WriteString("kind", KindNumber);
                writer.WriteNumber("value", number);
                return;

            case string text:
                writer.WriteString("kind", KindText);
                writer.WriteString("value", text);
                return;

            case Angle angle:
                // Written in degrees, which is the unit the port is edited in. Radians would be a
                // more faithful record of the field and a worse record of what the user typed.
                writer.WriteString("kind", KindAngle);
                writer.WriteNumber("value", angle.Degrees);
                return;

            default:
                throw new SparkFileException(new SparkDiagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCodes.UnwritableLiteral,
                    $"The value on input {literal.PortIndex} of node {node.Key} is a "
                    + $"{literal.Value?.GetType().Name ?? "null"}, which a .spark file cannot hold.",
                    helpTopicId: DiagnosticCodes.FileTopic,
                    nodeId: node.Id.Value,
                    portIndex: literal.PortIndex));
        }
    }

    private static GraphDocumentNode ReadNode(JsonElement element)
    {
        NodeId id = ReadId(element, "id");

        if (!element.TryGetProperty("key", out JsonElement key)
            || key.ValueKind != JsonValueKind.String)
        {
            throw Malformed("A node has no key.");
        }

        NodeKey nodeKey;
        try
        {
            nodeKey = NodeKey.Parse(key.GetString()!);
        }
        catch (FormatException error)
        {
            throw Malformed($"A node's key is malformed: {error.Message}");
        }
        catch (ArgumentException error)
        {
            throw Malformed($"A node's key is malformed: {error.Message}");
        }

        LacingMode lacing = LacingMode.Auto;
        if (element.TryGetProperty("lacing", out JsonElement lacingElement)
            && lacingElement.ValueKind == JsonValueKind.String
            && Enum.TryParse(lacingElement.GetString(), ignoreCase: false, out LacingMode parsed))
        {
            lacing = parsed;
        }

        double x = ReadDouble(element, "x");
        double y = ReadDouble(element, "y");

        List<GraphLiteral> literals = [];
        string? script = element.TryGetProperty("script", out JsonElement scriptElement)
            && scriptElement.ValueKind == JsonValueKind.String
                ? scriptElement.GetString()
                : null;

        if (element.TryGetProperty("literals", out JsonElement literalArray)
            && literalArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in literalArray.EnumerateArray())
            {
                literals.Add(ReadLiteral(entry));
            }
        }

        bool frozen = element.TryGetProperty("frozen", out JsonElement frozenElement)
            && frozenElement.ValueKind == JsonValueKind.True;

        return new GraphDocumentNode(id, nodeKey, lacing, x, y, literals, script, frozen);
    }

    private static GraphLiteral ReadLiteral(JsonElement element)
    {
        int port = ReadInt(element, "port");

        if (!element.TryGetProperty("kind", out JsonElement kind)
            || kind.ValueKind != JsonValueKind.String)
        {
            throw Malformed($"The literal on port {port} has no kind.");
        }

        if (!element.TryGetProperty("value", out JsonElement value))
        {
            throw Malformed($"The literal on port {port} has no value.");
        }

        return kind.GetString() switch
        {
            KindBoolean => new GraphLiteral(port, value.GetBoolean()),
            KindInteger => new GraphLiteral(port, (int)value.GetInt64()),
            KindNumber => new GraphLiteral(port, value.GetDouble()),
            KindText => new GraphLiteral(port, value.GetString()),
            KindAngle => new GraphLiteral(port, Angle.FromDegrees(value.GetDouble())),
            _ => throw Malformed(
                $"The literal on port {port} has an unknown kind '{kind.GetString()}'."),
        };
    }

    /// <summary>
    /// Reads one note. Its text is required to be present but is allowed to be empty, because a
    /// note the user has created and not yet typed into is a real state and saving is not modal.
    /// </summary>
    private static GraphDocumentNote ReadNote(JsonElement element)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(idElement.GetString(), "D", out Guid id))
        {
            throw Malformed("A note has no identity.");
        }

        if (!element.TryGetProperty("text", out JsonElement textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            throw Malformed("A note has no text.");
        }

        return new GraphDocumentNote(
            id,
            ReadDouble(element, "x"),
            ReadDouble(element, "y"),
            ReadDouble(element, "width"),
            ReadDouble(element, "height"),
            textElement.GetString() ?? string.Empty);
    }

    /// <summary>
    /// Reads one group. A group with no members is malformed rather than empty: a group's whole
    /// content is what it contains, so one containing nothing is a frame around nothing and could
    /// only have arrived by an editing mistake.
    /// </summary>
    private static GraphDocumentGroup ReadGroup(JsonElement element)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(idElement.GetString(), "D", out Guid id))
        {
            throw Malformed("A group has no identity.");
        }

        if (!element.TryGetProperty("title", out JsonElement titleElement)
            || titleElement.ValueKind != JsonValueKind.String)
        {
            throw Malformed("A group has no title.");
        }

        if (!element.TryGetProperty("members", out JsonElement memberArray)
            || memberArray.ValueKind != JsonValueKind.Array)
        {
            throw Malformed("A group has no members.");
        }

        List<NodeId> members = [];
        foreach (JsonElement member in memberArray.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.String
                || !Guid.TryParseExact(member.GetString(), "D", out Guid memberId))
            {
                throw Malformed("A group names a member that is not a node identity.");
            }

            members.Add(new NodeId(memberId));
        }

        if (members.Count == 0)
        {
            throw Malformed("A group has no members.");
        }

        return new GraphDocumentGroup(id, titleElement.GetString() ?? string.Empty, members);
    }

    private static NodeId ReadId(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(value.GetString(), "D", out Guid id))
        {
            throw Malformed($"'{name}' is missing or is not a node identity.");
        }

        return new NodeId(id);
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number))
        {
            throw Malformed($"'{name}' is missing or is not a whole number.");
        }

        return number;
    }

    private static double ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out double number))
        {
            throw Malformed($"'{name}' is missing or is not a number.");
        }

        return number;
    }

    private static SparkFileException Malformed(string message) =>
        new(new SparkDiagnostic(
            DiagnosticSeverity.Error,
            DiagnosticCodes.MalformedGraphFile,
            message,
            helpTopicId: DiagnosticCodes.FileTopic));

    private static string Format(NodeId id) =>
        id.Value.ToString("D", CultureInfo.InvariantCulture);
}

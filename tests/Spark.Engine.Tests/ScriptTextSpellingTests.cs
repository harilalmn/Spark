using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// A <c>.spark</c> file spells text the way it was typed (`E3-T24`,
/// [ADR-0026](../../docs/adr/0026-spark-file-text-is-written-as-typed.md)).
/// </summary>
/// <remarks>
/// <para>
/// <b>The format exists to review like code</b> (ADR-0017), and code-block text is the text most
/// likely to be read in a diff — and where <c>+</c> and quotation marks are commonest. The default
/// JSON encoder wrote them as six-character escaped code points — a plus sign as backslash, <c>u</c>,
/// <c>002B</c> — which is right for JSON embedded in a
/// web page and wrong for a file a person reads.
/// </para>
/// <para>
/// <b>The one-time diff is asserted rather than described.</b> A file written by an earlier build
/// reads back to the same document, and saving it produces today's spelling — once. Every file
/// written since re-saves byte for byte, which is `E7-T7`'s promise and is unchanged.
/// </para>
/// </remarks>
public sealed class ScriptTextSpellingTests
{
    private static readonly NodeId Block = new(new Guid("5f0c6a3e-0000-4000-8000-000000000001"));
    private static readonly NodeId Titled = new(new Guid("5f0c6a3e-0000-4000-8000-000000000002"));
    private static readonly Guid NoteId = new("5f0c6a3e-0000-4000-8000-000000000003");

    /// <summary>
    /// <b>The row's own example</b>: <c>+</c> and a quoted string in a code block are written as typed.
    /// A quotation mark inside a JSON string is still escaped — JSON requires that — but as
    /// <c>\"</c>, which a reader recognises, rather than <c>"</c>.
    /// </summary>
    [Fact]
    public void ScriptTextIsWrittenAsTyped()
    {
        string text = SparkFile.Write(Document("return \"Spark\" + 1 + '<' + \"&\";"));

        Assert.Contains("return \\\"Spark\\\" + 1 + '<' + \\\"&\\\";", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-ASCII in a node's title and in a note is written as typed, for the same reason: a diff of
    /// <c>Façade → north</c> is a diff nobody reads.
    /// </summary>
    [Fact]
    public void NonAsciiInATitleAndANoteIsWrittenAsTyped()
    {
        string text = SparkFile.Write(Document("return 1;"));

        Assert.Contains("Façade → north", text, StringComparison.Ordinal);
        Assert.Contains("Höhe ≥ 3 m", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The one-time diff.</b> A file spelled the old way — every <c>+</c>, quotation mark and
    /// non-ASCII character escaped — reads back to the same document, and saving it writes today's
    /// spelling, which then re-saves byte for byte.
    /// </summary>
    [Fact]
    public void AFileSpelledTheOldWayReadsBackTheSameAndReSavesTheNewWay()
    {
        string today = SparkFile.Write(Document("return \"Spark\" + 1;"));

        // The spelling an earlier build wrote: System.Text.Json's default encoder, which is what
        // `SparkFile` used until `E3-T24`. Asserted, so that this fixture cannot quietly stop being
        // an old file and leave the test proving nothing.
        string earlier = JsonNode.Parse(today)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        Assert.Contains("\\u002B", earlier, StringComparison.Ordinal);
        Assert.Contains("\\u0022", earlier, StringComparison.Ordinal);
        Assert.Contains("\\u00E7", earlier, StringComparison.Ordinal);

        GraphDocument read = SparkFile.Read(earlier);

        Assert.Equal("return \"Spark\" + 1;", read.Nodes[0].Script);
        Assert.Equal(today, SparkFile.Write(read));
        Assert.Equal(today, SparkFile.Write(SparkFile.Read(today)));
    }

    /// <summary>
    /// A character outside the Basic Multilingual Plane still comes back exactly, whatever the
    /// encoder does with it on the way out — the relaxed encoder writes it as an escaped surrogate
    /// pair, and a file holding one is still byte-stable across saves.
    /// </summary>
    [Fact]
    public void ACharacterBeyondTheBasicPlaneRoundTripsAndIsByteStable()
    {
        const string script = "return \"🔥\";";
        string text = SparkFile.Write(Document(script));

        // Pinned, because N150 says so and a document that says what the encoder does had better be
        // checked by something that fails when it stops being true.
        Assert.Contains("\\uD83D\\uDD25", text, StringComparison.Ordinal);

        Assert.Equal(script, SparkFile.Read(text).Nodes[0].Script);
        Assert.Equal(text, SparkFile.Write(SparkFile.Read(text)));
    }

    /// <summary>A code block, a retitled node and a note, as a file would hold them.</summary>
    private static GraphDocument Document(string script) => new(
        GraphDocument.CurrentFormatVersion,
        [
            new GraphDocumentNode(
                Block, new NodeKey("Spark.Script", "CodeBlock"), default, 0.0, 0.0, [], Script: script),
            new GraphDocumentNode(
                Titled, new NodeKey("Spark.Nodes.Core", "Number.Value"), default, 0.0, 0.0, [], Title: "Façade → north"),
        ],
        [],
        notes: [new GraphDocumentNote(NoteId, 0.0, 0.0, 100.0, 50.0, "Höhe ≥ 3 m")]);
}

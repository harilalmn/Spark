using System;
using System.Collections.Generic;

namespace Spark.Api.Help;

/// <summary>What kind of inline run a piece of help text is.</summary>
public enum HelpInlineKind
{
    /// <summary>Ordinary prose.</summary>
    Text,

    /// <summary>Emphasised prose, from <c>**bold**</c>.</summary>
    Strong,

    /// <summary>Italic prose, from <c>*italic*</c>.</summary>
    Emphasis,

    /// <summary>Inline code, from backticks. Rendered in the monospace face.</summary>
    Code,

    /// <summary>A link. <see cref="HelpInline.Target"/> carries where it goes.</summary>
    Link,
}

/// <summary>A run of text inside a help block.</summary>
/// <param name="Kind">How the run is drawn.</param>
/// <param name="Text">The text itself, with markup already removed.</param>
/// <param name="Target">
/// For <see cref="HelpInlineKind.Link"/>, where it points: another topic id, a node key, or a URL.
/// Null otherwise.
/// </param>
public readonly record struct HelpInline(HelpInlineKind Kind, string Text, string? Target = null);

/// <summary>What kind of block a piece of a help topic is.</summary>
public enum HelpBlockKind
{
    /// <summary>A heading. <see cref="HelpBlock.Level"/> is 1 to 6.</summary>
    Heading,

    /// <summary>A paragraph of prose.</summary>
    Paragraph,

    /// <summary>A fenced code block. <see cref="HelpBlock.Language"/> names the language.</summary>
    Code,

    /// <summary>One item of a bulleted list.</summary>
    ListItem,

    /// <summary>A block quotation.</summary>
    Quote,

    /// <summary>A table. <see cref="HelpBlock.Rows"/> holds the cells, header row first.</summary>
    Table,

    /// <summary>A horizontal rule.</summary>
    Rule,
}

/// <summary>One block of a help topic.</summary>
/// <remarks>
/// A deliberately small set. Every construct here appears in the topics that already exist, and
/// none is here in anticipation: a renderer with a case nothing produces is a case nothing tests.
/// </remarks>
public sealed class HelpBlock
{
    /// <summary>Creates a block.</summary>
    /// <param name="kind">What kind of block it is.</param>
    /// <param name="inlines">Its text runs. Empty for <see cref="HelpBlockKind.Rule"/> and tables.</param>
    /// <param name="level">Heading level, 1 to 6, or 0.</param>
    /// <param name="language">The fence language for a code block, or null.</param>
    /// <param name="text">The raw text of a code block, or null.</param>
    /// <param name="rows">Table rows, header first, or null.</param>
    public HelpBlock(
        HelpBlockKind kind,
        IReadOnlyList<HelpInline>? inlines = null,
        int level = 0,
        string? language = null,
        string? text = null,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpInline>>>? rows = null)
    {
        Kind = kind;
        Inlines = inlines ?? [];
        Level = level;
        Language = language;
        Text = text;
        Rows = rows ?? [];
    }

    /// <summary>What kind of block this is.</summary>
    public HelpBlockKind Kind { get; }

    /// <summary>The text runs making up this block.</summary>
    public IReadOnlyList<HelpInline> Inlines { get; }

    /// <summary>Heading level, 1 to 6, or 0 for a block that is not a heading.</summary>
    public int Level { get; }

    /// <summary>The language named on a code fence, or null.</summary>
    public string? Language { get; }

    /// <summary>The raw text of a code block, newlines intact, or null.</summary>
    public string? Text { get; }

    /// <summary>Table rows, header row first; each cell is its own run of inlines.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<HelpInline>>> Rows { get; }

    /// <summary>The block's text with all markup removed, for search and for accessible names.</summary>
    public string PlainText
    {
        get
        {
            if (Kind == HelpBlockKind.Code)
            {
                return Text ?? string.Empty;
            }

            System.Text.StringBuilder plain = new();
            foreach (HelpInline inline in Inlines)
            {
                plain.Append(inline.Text);
            }

            // A table's text lives in its cells, not in Inlines, and leaving it out would make
            // every table invisible to search -- including the lacing case table, which is the
            // single most searched thing in the help.
            foreach (IReadOnlyList<IReadOnlyList<HelpInline>> row in Rows)
            {
                foreach (IReadOnlyList<HelpInline> cell in row)
                {
                    foreach (HelpInline inline in cell)
                    {
                        plain.Append(inline.Text).Append(' ');
                    }
                }
            }

            return plain.ToString();
        }
    }
}

/// <summary>
/// A help topic: its front matter, and its body as blocks a renderer can draw without knowing
/// anything about Markdown.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type takes no user-interface dependency, and that is the requirement rather than a
/// preference (<c>E10-T13</c>).</b> The same document is drawn by the desktop shell, walked by the
/// documentation harness, and printed by the command line. A model that could only be rendered by
/// one of them would need the other two to reimplement Markdown, and three parsers is three sets
/// of bugs about the same asterisk.
/// </para>
/// <para>
/// <b>It is also why the API reference can be generated at runtime.</b> A topic is a value, not a
/// file, so a page describing a node can be produced from the live node library on demand and is
/// incapable of drifting from the code it describes.
/// </para>
/// </remarks>
public sealed class HelpDocument
{
    /// <summary>Creates a topic.</summary>
    /// <param name="id">The stable topic id, such as <c>concepts.lacing</c>.</param>
    /// <param name="title">The title shown to a reader.</param>
    /// <param name="blocks">The body.</param>
    /// <param name="nodes">Node keys this topic documents, or empty.</param>
    /// <param name="related">Ids of related topics, or empty.</param>
    /// <param name="since">The version this topic first applied to, or null.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="title"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> is null.</exception>
    public HelpDocument(
        string id,
        string title,
        IReadOnlyList<HelpBlock> blocks,
        IReadOnlyList<string>? nodes = null,
        IReadOnlyList<string>? related = null,
        string? since = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(blocks);

        Id = id;
        Title = title;
        Blocks = blocks;
        Nodes = nodes ?? [];
        Related = related ?? [];
        Since = since;
    }

    /// <summary>The stable topic id.</summary>
    public string Id { get; }

    /// <summary>The title shown to a reader.</summary>
    public string Title { get; }

    /// <summary>The body, in order.</summary>
    public IReadOnlyList<HelpBlock> Blocks { get; }

    /// <summary>The node keys this topic documents.</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>Ids of related topics.</summary>
    public IReadOnlyList<string> Related { get; }

    /// <summary>The version this topic first applied to, or null.</summary>
    public string? Since { get; }

    /// <summary>
    /// Whether this topic contains a worked example: a fenced code block, or a table showing a
    /// case and its result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The documentation harness fails a topic without one. The rule is older than this type and
    /// is the reason the help topics that exist are usable: a description of what a node does,
    /// with no example of it doing anything, is a restatement of its signature.
    /// </para>
    /// <para>
    /// <b>Three shapes count, and the third is not a loophole.</b> A fenced code block and a table
    /// are obvious. A section headed <i>example</i> counts because <b>this is a node-graph tool</b>
    /// and its most useful worked examples are walkthroughs — *place this node, wire it here, watch
    /// the viewport* — which contain no code at all. Requiring a fence would have forced a snippet
    /// into topics where a snippet is the wrong illustration.
    /// </para>
    /// <para>
    /// <b>What deliberately does not count</b> is prose that merely mentions an example, or the
    /// string <c>.spark</c> appearing anywhere in the text. The harness used to accept the latter,
    /// which meant any topic saying the words ".spark file" passed — a check that could hardly
    /// fail. `Spark.Docs.Verify` now applies the same three rules at the file level. <b>The two
    /// implementations are separate and have to be kept in step by hand</b>, because that harness
    /// deliberately references no Spark project so that it cannot constrain what it observes.
    /// </para>
    /// </remarks>
    public bool HasWorkedExample
    {
        get
        {
            foreach (HelpBlock block in Blocks)
            {
                if (block.Kind is HelpBlockKind.Code or HelpBlockKind.Table)
                {
                    return true;
                }

                if (block.Kind == HelpBlockKind.Heading
                    && block.PlainText.Contains("example", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The whole topic as plain text, for searching.</summary>
    /// <returns>Every block's text, one per line.</returns>
    public string PlainText()
    {
        System.Text.StringBuilder plain = new();
        plain.AppendLine(Title);
        foreach (HelpBlock block in Blocks)
        {
            plain.AppendLine(block.PlainText);
        }

        return plain.ToString();
    }
}

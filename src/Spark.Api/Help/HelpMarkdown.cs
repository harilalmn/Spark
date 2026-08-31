using System;
using System.Collections.Generic;
using System.Text;

namespace Spark.Api.Help;

/// <summary>
/// Reads the subset of Markdown Spark's help topics are written in, plus their YAML front matter
/// (<c>E10-T13</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A subset, deliberately, and the subset is defined by what the topics already use</b> rather
/// than by what Markdown offers: front matter, ATX headings, paragraphs, fenced code, bullet
/// lists, block quotes, pipe tables, horizontal rules, and inline bold, italic, code and links.
/// Nothing else. A parser with a case nothing produces is a case nothing tests, and this one is
/// checked against the corpus in <c>docs/help/</c> rather than against a specification.
/// </para>
/// <para>
/// <b>Why not a Markdown package.</b> `Spark.Api` is a contract assembly: every dependency it
/// takes is one every package author inherits and one that can never be side-by-sided
/// (<c>ADR-0019</c>). Two hundred lines that handle the constructs our own documents contain is a
/// smaller liability than a general parser and its transitive graph, and the ceiling on its
/// ambition is set by a corpus we control.
/// </para>
/// <para>
/// <b>It never throws on bad input.</b> A malformed topic renders as best it can, because the
/// alternative is a help panel that shows an exception where a page should be — and a reader
/// looking at help is usually already stuck.
/// </para>
/// </remarks>
public static class HelpMarkdown
{
    /// <summary>
    /// Parses a help topic: optional YAML front matter, then Markdown.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="fallbackId">The id to use when the front matter has none, such as a file name.</param>
    /// <returns>The parsed topic.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static HelpDocument Parse(string text, string fallbackId = "topic")
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int start = 0;

        Dictionary<string, string> front = [];
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            int end = 1;
            while (end < lines.Length && lines[end].Trim() != "---")
            {
                end++;
            }

            // Unterminated front matter is treated as no front matter at all. The alternative --
            // consuming to the end of the file looking for a closing marker -- silently swallows
            // the whole topic, which looks exactly like an empty page and gives a reader nothing
            // to go on.
            if (end < lines.Length)
            {
                for (int line = 1; line < end; line++)
                {
                    ReadFrontMatterLine(lines[line], front);
                }

                start = Math.Min(end + 1, lines.Length);
            }
        }

        List<HelpBlock> blocks = ParseBlocks(lines, start);

        string id = front.GetValueOrDefault("id", fallbackId);
        string title = front.GetValueOrDefault("title", FirstHeading(blocks) ?? id);

        return new HelpDocument(
            string.IsNullOrWhiteSpace(id) ? fallbackId : id,
            string.IsNullOrWhiteSpace(title) ? fallbackId : title,
            blocks,
            SplitList(front.GetValueOrDefault("nodes")),
            SplitList(front.GetValueOrDefault("related")),
            front.GetValueOrDefault("since"));
    }

    /// <summary>Parses body Markdown with no front matter, for generated pages.</summary>
    /// <param name="markdown">The Markdown body.</param>
    /// <returns>The blocks.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is null.</exception>
    public static IReadOnlyList<HelpBlock> ParseBody(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return ParseBlocks(lines, 0);
    }

    /// <summary>
    /// Splits a line into inline runs. Public because the node reference builds inlines directly
    /// from XML doc summaries without going through a whole document.
    /// </summary>
    /// <param name="line">One line of Markdown, without block markers.</param>
    /// <returns>The runs, in order. Never null; an empty line gives an empty list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is null.</exception>
    public static IReadOnlyList<HelpInline> ParseInlines(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        List<HelpInline> runs = [];
        StringBuilder plain = new();

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                runs.Add(new HelpInline(HelpInlineKind.Text, plain.ToString()));
                plain.Clear();
            }
        }

        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];

            if (c == '`')
            {
                int close = line.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushPlain();
                    runs.Add(new HelpInline(HelpInlineKind.Code, line[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }
            else if (c == '*' && i + 1 < line.Length && line[i + 1] == '*')
            {
                int close = line.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i)
                {
                    FlushPlain();
                    runs.Add(new HelpInline(HelpInlineKind.Strong, line[(i + 2)..close]));
                    i = close + 2;
                    continue;
                }
            }
            else if (c == '*')
            {
                int close = line.IndexOf('*', i + 1);
                if (close > i + 1)
                {
                    FlushPlain();
                    runs.Add(new HelpInline(HelpInlineKind.Emphasis, line[(i + 1)..close]));
                    i = close + 1;
                    continue;
                }
            }
            else if (c == '[')
            {
                int label = line.IndexOf(']', i + 1);
                if (label > i && label + 1 < line.Length && line[label + 1] == '(')
                {
                    int target = line.IndexOf(')', label + 2);
                    if (target > label)
                    {
                        FlushPlain();
                        runs.Add(new HelpInline(
                            HelpInlineKind.Link, line[(i + 1)..label], line[(label + 2)..target]));
                        i = target + 1;
                        continue;
                    }
                }
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();
        return runs;
    }

    private static List<HelpBlock> ParseBlocks(string[] lines, int start)
    {
        List<HelpBlock> blocks = [];
        List<string> paragraph = [];

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            blocks.Add(new HelpBlock(
                HelpBlockKind.Paragraph, ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                string language = trimmed[3..].Trim();
                StringBuilder code = new();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    // A line feed explicitly, never AppendLine: that writes Environment.NewLine,
                    // so the same topic would yield different code text on Windows and on Linux.
                    // The .spark writer made this exact decision for the same reason.
                    code.Append(lines[i]).Append('\n');
                    i++;
                }

                blocks.Add(new HelpBlock(
                    HelpBlockKind.Code,
                    language: language.Length == 0 ? null : language,
                    text: code.ToString().TrimEnd('\n')));
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                {
                    level++;
                }

                if (level is >= 1 and <= 6)
                {
                    FlushParagraph();
                    blocks.Add(new HelpBlock(
                        HelpBlockKind.Heading, ParseInlines(trimmed[level..].Trim()), level));
                    continue;
                }
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new HelpBlock(HelpBlockKind.Rule));
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal)
                || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new HelpBlock(HelpBlockKind.ListItem, ParseInlines(trimmed[2..].Trim())));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                FlushParagraph();
                blocks.Add(new HelpBlock(
                    HelpBlockKind.Quote, ParseInlines(trimmed.Length > 1 ? trimmed[2..] : string.Empty)));
                continue;
            }

            if (trimmed.StartsWith('|') && trimmed.EndsWith('|'))
            {
                FlushParagraph();
                i = ReadTable(lines, i, blocks);
                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>
    /// Reads a pipe table. The alignment row of dashes is dropped rather than interpreted:
    /// alignment is a rendering decision and the renderer has better information than the file.
    /// </summary>
    private static int ReadTable(string[] lines, int at, List<HelpBlock> blocks)
    {
        List<IReadOnlyList<IReadOnlyList<HelpInline>>> rows = [];

        while (at < lines.Length)
        {
            string row = lines[at].Trim();
            if (!row.StartsWith('|') || !row.EndsWith('|'))
            {
                break;
            }

            string inner = row[1..^1];
            string[] cells = inner.Split('|');

            bool isAlignmentRow = true;
            foreach (string cell in cells)
            {
                string t = cell.Trim();
                if (t.Length == 0 || t.Trim('-', ':').Length != 0)
                {
                    isAlignmentRow = false;
                    break;
                }
            }

            if (!isAlignmentRow)
            {
                List<IReadOnlyList<HelpInline>> parsed = [];
                foreach (string cell in cells)
                {
                    parsed.Add(ParseInlines(cell.Trim()));
                }

                rows.Add(parsed);
            }

            at++;
        }

        blocks.Add(new HelpBlock(HelpBlockKind.Table, rows: rows));
        return at - 1;
    }

    private static void ReadFrontMatterLine(string line, Dictionary<string, string> front)
    {
        int colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return;
        }

        string key = line[..colon].Trim();
        string value = line[(colon + 1)..].Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (key.Length > 0)
        {
            front[key] = value;
        }
    }

    private static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string inner = value.Trim();
        if (inner.Length >= 2 && inner[0] == '[' && inner[^1] == ']')
        {
            inner = inner[1..^1];
        }

        List<string> items = [];
        foreach (string part in inner.Split(','))
        {
            string item = part.Trim().Trim('"', '\'');
            if (item.Length > 0)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static string? FirstHeading(List<HelpBlock> blocks)
    {
        foreach (HelpBlock block in blocks)
        {
            if (block.Kind == HelpBlockKind.Heading)
            {
                return block.PlainText;
            }
        }

        return null;
    }
}

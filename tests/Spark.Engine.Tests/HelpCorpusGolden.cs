using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Spark.Api.Help;

namespace Spark.Engine.Tests;

/// <summary>
/// A stable text rendering of a parsed help topic, and the golden files it is compared against
/// (<c>E11-T7</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Parity against what.</b> <c>HelpMarkdown</c> is a deliberate subset of Markdown, not an
/// implementation of it — <c>ADR-0019</c> is why <c>Spark.Api</c> takes no parser dependency, and
/// the subset is defined by what the topics already use. So checking it against a general Markdown
/// library would be checking it against a specification it never claimed to meet, and would go red
/// on the first run for reasons that are all correct. <b>The reference it must agree with is
/// itself, yesterday, over the documents that actually exist</b>, which is what a golden corpus is
/// for.
/// </para>
/// <para>
/// <b>What the rendering records is what a reader would see change.</b> Every block in order with
/// its kind, a heading's level, a fence's language, and — the part unit tests over synthetic
/// snippets cannot reach — <b>every inline run with its kind and a link's target</b>. A parser
/// change that merges two runs, drops an emphasis, or resolves a link somewhere else alters this
/// file and alters nothing a per-construct test asserts.
/// </para>
/// <para>
/// <b>The format is the house one</b>, shared with <c>tests/corpus/dynamo-parity.tsv</c> and
/// <c>GeometryGolden</c>: a <c>#</c> provenance block, then a header row asserted <b>literally</b>
/// on read, so that a column reordered by hand refuses to parse rather than being silently
/// reinterpreted.
/// </para>
/// </remarks>
internal static class HelpCorpusGolden
{
    /// <summary>Set to <c>1</c> to rewrite goldens instead of asserting against them.</summary>
    internal const string UpdateVariable = "SPARK_UPDATE_GOLDEN";

    /// <summary>The header line every golden carries, asserted on read.</summary>
    private const string Header = "Part\tDetail";

    /// <summary>Where the help topics live, relative to the repository root.</summary>
    private static readonly string[] HelpDirectory = ["docs", "help", "concepts"];

    /// <summary>Every topic in the corpus, by the id its file name gives it.</summary>
    /// <returns>The topic names, sorted, so the theory's cases are stable.</returns>
    internal static IReadOnlyList<string> Names()
    {
        List<string> names = [];

        foreach (string file in Directory.EnumerateFiles(TopicDirectory(), "*.md"))
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>The Markdown of one topic, as committed.</summary>
    /// <param name="name">The topic's file name without its extension.</param>
    /// <returns>The file's text.</returns>
    internal static string Source(string name) =>
        File.ReadAllText(Path.Combine(TopicDirectory(), name + ".md"));

    /// <summary>Where a topic's golden lives.</summary>
    /// <param name="name">The topic's name.</param>
    /// <returns>The absolute path.</returns>
    internal static string PathFor(string name) =>
        Path.Combine(RepositoryRoot(), "tests", "corpus", "help", name + ".tsv");

    /// <summary>Where the rendering of a failing run is written, beside its golden.</summary>
    /// <param name="goldenPath">The golden's path.</param>
    /// <returns>The sidecar's path.</returns>
    /// <remarks>
    /// Written on failure and **deleted on success**, so the presence of one always means this
    /// topic is red right now — [N177](../../docs/NOTES.md), which is the same trap the geometry
    /// goldens fell into.
    /// </remarks>
    internal static string SidecarFor(string goldenPath) =>
        Path.ChangeExtension(goldenPath, ".actual.tsv");

    /// <summary>The parsed topic as the lines a golden holds.</summary>
    /// <param name="topic">The parsed topic.</param>
    /// <returns>Part-and-detail pairs, in document order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="topic"/> is null.</exception>
    internal static IReadOnlyList<(string Part, string Detail)> Render(HelpDocument topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        List<(string, string)> lines =
        [
            ("id", topic.Id),
            ("title", topic.Title),
            ("since", topic.Since ?? "(none)"),
            ("nodes", topic.Nodes.Count == 0 ? "(none)" : string.Join(", ", topic.Nodes)),
            ("related", topic.Related.Count == 0 ? "(none)" : string.Join(", ", topic.Related)),
            ("blocks", topic.Blocks.Count.ToString(CultureInfo.InvariantCulture)),
            ("example", topic.HasWorkedExample ? "yes" : "no"),
        ];

        for (int index = 0; index < topic.Blocks.Count; index++)
        {
            HelpBlock block = topic.Blocks[index];
            string part = string.Create(
                CultureInfo.InvariantCulture,
                $"block[{index}].{block.Kind.ToString().ToLowerInvariant()}");

            lines.Add((part, Describe(block)));
        }

        return lines;
    }

    /// <summary>The golden's text: provenance, header, then the rendering.</summary>
    /// <param name="name">The topic's name, for the comment block.</param>
    /// <param name="lines">The rendering.</param>
    /// <returns>The file's text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    internal static string ToText(string name, IReadOnlyList<(string Part, string Detail)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        StringBuilder text = new();
        text.Append("# Help-renderer golden for '").Append(name).Append("' - E11-T7. Generated; do not hand-edit.\n");
        text.Append($"# Rewrite it with {UpdateVariable}=1, then READ THE DIFF before committing it.\n");
        text.Append("#\n");
        text.Append("# This is what HelpMarkdown makes of docs/help/concepts/").Append(name).Append(".md: every\n");
        text.Append("# block in order, and every inline run inside it with its kind and a link's target.\n");
        text.Append("# HelpMarkdown is a deliberate SUBSET of Markdown (ADR-0019), so the reference it has\n");
        text.Append("# to agree with is itself over the documents that exist - not a specification.\n");
        text.Append("#\n");
        text.Append("# A change here is a change to what a reader sees in the help panel. If it is\n");
        text.Append("# intended, the topic or the parser changed and this file follows; if it is not, the\n");
        text.Append("# parser regressed on real input.\n");
        text.Append(Header).Append('\n');

        foreach ((string part, string detail) in lines)
        {
            text.Append(part).Append('\t').Append(detail).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads a golden back.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The rendering it holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="InvalidDataException">The header is missing or has moved.</exception>
    internal static IReadOnlyList<(string Part, string Detail)> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<(string, string)> lines = [];
        bool seenHeader = false;

        foreach (string raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.Length == 0 || raw[0] == '#')
            {
                continue;
            }

            if (!seenHeader)
            {
                if (raw != Header)
                {
                    throw new InvalidDataException(
                        $"The golden's header is '{raw}' and must be 'Part<tab>Detail'. A column "
                        + "reordered by hand and read as though it had not moved is worse than a "
                        + "file that refuses to load.");
                }

                seenHeader = true;
                continue;
            }

            int tab = raw.IndexOf('\t', StringComparison.Ordinal);
            lines.Add(tab < 0 ? (raw, string.Empty) : (raw[..tab], raw[(tab + 1)..]));
        }

        return seenHeader
            ? lines
            : throw new InvalidDataException(
                "The golden has no header row. Every fixture in this repository opens with one, "
                + "and a reader that tolerates its absence cannot tell a file from a fragment.");
    }

    /// <summary>The diff table two renderings fail with, or null when they match.</summary>
    /// <param name="golden">What was committed.</param>
    /// <param name="actual">What this run produced.</param>
    /// <returns>The report, or null.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// <b>Aligned by part name rather than by row number</b>, because a single inserted paragraph
    /// shifts every block index after it and a positional diff would report the whole rest of the
    /// document as changed. The report names the first differing part and shows both sides.
    /// </remarks>
    internal static string? Compare(
        IReadOnlyList<(string Part, string Detail)> golden,
        IReadOnlyList<(string Part, string Detail)> actual)
    {
        ArgumentNullException.ThrowIfNull(golden);
        ArgumentNullException.ThrowIfNull(actual);

        List<string> rows = [];
        int limit = Math.Max(golden.Count, actual.Count);

        for (int index = 0; index < limit; index++)
        {
            string goldenPart = index < golden.Count ? golden[index].Part : "(absent)";
            string actualPart = index < actual.Count ? actual[index].Part : "(absent)";
            string goldenDetail = index < golden.Count ? golden[index].Detail : "(absent)";
            string actualDetail = index < actual.Count ? actual[index].Detail : "(absent)";

            if (goldenPart == actualPart && goldenDetail == actualDetail)
            {
                continue;
            }

            rows.Add($"  {goldenPart} -> {actualPart}");
            rows.Add($"    golden: {goldenDetail}");
            rows.Add($"    actual: {actualDetail}");
        }

        if (rows.Count == 0)
        {
            return null;
        }

        int moved = rows.Count / 3;
        return $"{moved} of {limit} parts differ.\n\n" + string.Join("\n", rows);
    }

    /// <summary>Compares one topic against its committed golden.</summary>
    /// <param name="name">The topic's name.</param>
    /// <returns>Null when it matches, or the report to fail with.</returns>
    internal static string? Check(string name)
    {
        string path = PathFor(name);
        IReadOnlyList<(string Part, string Detail)> actual = Render(HelpMarkdown.Parse(Source(name), name));

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ToText(name, actual));
            return $"Golden rewritten at {path}. Unset {UpdateVariable} and re-run; read the file before committing it.";
        }

        if (!File.Exists(path))
        {
            return $"No golden at {path}. Run with {UpdateVariable}=1 to create one, then read it.";
        }

        string? report = Compare(Parse(File.ReadAllText(path)), actual);
        string sidecar = SidecarFor(path);

        if (report is null)
        {
            File.Delete(sidecar);
            return null;
        }

        File.WriteAllText(sidecar, ToText(name, actual));

        return $"{name} does not render as its golden says.\n\n{report}\n\n"
            + $"The rendering this run produced was written to {sidecar}.\n"
            + $"If the change is intended, re-run with {UpdateVariable}=1 and commit the new golden.";
    }

    /// <summary>One block as its golden line.</summary>
    private static string Describe(HelpBlock block)
    {
        StringBuilder detail = new();

        if (block.Level > 0)
        {
            detail.Append(CultureInfo.InvariantCulture, $"level={block.Level} ");
        }

        if (block.Language is { Length: > 0 } language)
        {
            detail.Append(CultureInfo.InvariantCulture, $"lang={language} ");
        }

        switch (block.Kind)
        {
            case HelpBlockKind.Code:
                // A fence's body is recorded by shape rather than verbatim: a golden that repeats
                // the code would be a second copy of the topic to keep in step, and what a parser
                // regression changes here is the line count or the language, never the characters
                // between the fences.
                detail.Append(CultureInfo.InvariantCulture, $"lines={CountLines(block.Text)}");
                break;

            case HelpBlockKind.Table:
                detail.Append(CultureInfo.InvariantCulture, $"rows={block.Rows.Count}");
                for (int row = 0; row < block.Rows.Count; row++)
                {
                    detail.Append(" | ");
                    for (int cell = 0; cell < block.Rows[row].Count; cell++)
                    {
                        if (cell > 0)
                        {
                            detail.Append(" ; ");
                        }

                        Append(detail, block.Rows[row][cell]);
                    }
                }

                break;

            case HelpBlockKind.Rule:
                detail.Append("---");
                break;

            default:
                Append(detail, block.Inlines);
                break;
        }

        return detail.ToString();
    }

    /// <summary>A run of inlines, each with its kind and a link's target.</summary>
    private static void Append(StringBuilder detail, IReadOnlyList<HelpInline> inlines)
    {
        // NO SEPARATOR BETWEEN RUNS. The `[kind]` bracket already delimits them, and a space here
        // would make a run's own leading or trailing whitespace unreadable in the golden - which
        // matters, because whitespace between an inline and the prose around it is exactly the
        // kind of thing a parser change moves and a reader notices.
        foreach (HelpInline inline in inlines)
        {
            string kind = inline.Kind.ToString().ToLowerInvariant();
            string text = inline.Text.Replace("\t", "\\t", StringComparison.Ordinal);

            detail.Append(inline.Target is { Length: > 0 } target
                ? $"[{kind}->{target}]{text}"
                : $"[{kind}]{text}");
        }
    }

    private static int CountLines(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;

    private static string TopicDirectory() =>
        Path.Combine([RepositoryRoot(), .. HelpDirectory]);

    /// <summary>Walks up from the test binaries until the repository root is found.</summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"No Spark.slnx above {AppContext.BaseDirectory}; the corpus cannot be located.");
    }
}

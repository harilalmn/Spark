using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Api.Help;

namespace Spark.Engine.Tests;

/// <summary>
/// <c>HelpMarkdown.Write</c>, the inverse of the reader, and the round trip that is its contract
/// (<c>E12-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The round trip is asserted over the shipped corpus and over the generated pages, not over a
/// fixture.</b> A fixture covers what its author thought of; the topics that exist are the ones
/// that have to survive, and the generated node reference is three hundred more documents nobody
/// wrote by hand. <c>HelpCorpusGoldenTests</c> makes the same argument for the reader and it is the
/// same argument here.
/// </para>
/// <para>
/// <b>Equality is asserted on the parsed structure, never on the bytes.</b> Writing is not
/// byte-preserving and is not meant to be: a wrapped paragraph comes back as one line, because the
/// reader joins wrapped lines with a space and the information about where the breaks were is gone
/// before a topic reaches the writer. What must hold is that the <i>document</i> survives, which is
/// what a reader of the page actually sees. <see cref="WritingIsIdempotent"/> pins the byte-level
/// half of that: the second pass changes nothing, so a generated tree is stable under
/// regeneration.
/// </para>
/// </remarks>
public sealed class HelpMarkdownWriterTests
{
    /// <summary>The shipped topics, by file name.</summary>
    public static TheoryData<string> Corpus => [.. Names()];

    /// <summary>
    /// <b>The contract.</b> Parsing what the writer produced gives back the topic it was handed,
    /// for every topic that ships.
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void AShippedTopicSurvivesTheRoundTrip(string name)
    {
        HelpDocument original = HelpMarkdown.Parse(
            File.ReadAllText(Path.Combine(TopicDirectory(), name + ".md")), name);

        HelpDocument again = HelpMarkdown.Parse(HelpMarkdown.Write(original), name);

        AssertSame(original, again);
    }

    /// <summary>
    /// Every generated node page survives it too — and there are far more of those than there are
    /// hand-written topics, over far more shapes of summary text.
    /// </summary>
    /// <remarks>
    /// A node summary is an XML doc comment written by somebody who was not thinking about
    /// Markdown, so this is where a stray backtick, bracket or asterisk turns up. That is exactly
    /// the input a writer gets wrong, and exactly the input no fixture contains.
    /// </remarks>
    [Fact]
    public void EveryGeneratedNodePageSurvivesTheRoundTrip()
    {
        NodeLibrary library = new();
        library.Add(NodeImporter.Import(typeof(Spark.Nodes.Core.Point).Assembly));

        IReadOnlyList<HelpDocument> pages = NodeReference.ForAll(library);

        Assert.NotEmpty(pages);

        foreach (HelpDocument page in pages)
        {
            AssertSame(page, HelpMarkdown.Parse(HelpMarkdown.Write(page), page.Id));
        }

        HelpDocument index = NodeReference.Index(library);
        AssertSame(index, HelpMarkdown.Parse(HelpMarkdown.Write(index), index.Id));
    }

    /// <summary>
    /// Writing twice changes nothing the second time, so regenerating a tree of pages produces an
    /// empty diff rather than churn.
    /// </summary>
    [Fact]
    public void WritingIsIdempotent()
    {
        foreach (string name in Names())
        {
            HelpDocument original = HelpMarkdown.Parse(
                File.ReadAllText(Path.Combine(TopicDirectory(), name + ".md")), name);

            string once = HelpMarkdown.Write(original);
            string twice = HelpMarkdown.Write(HelpMarkdown.Parse(once, name));

            Assert.Equal(once, twice);
        }
    }

    /// <summary>
    /// <b>Newlines are <c>\n</c>, never the platform's.</b> A topic written on Windows and one
    /// written on Linux have to be the same bytes, or every generated page reads as changed on the
    /// other machine.
    /// </summary>
    [Fact]
    public void NothingWrittenCarriesACarriageReturn()
    {
        HelpDocument topic = HelpMarkdown.Parse(
            "---\nid: t\ntitle: T\n---\n\n# One\n\nTwo.\n\n- a\n- b\n", "t");

        Assert.DoesNotContain('\r', HelpMarkdown.Write(topic));
    }

    /// <summary>
    /// Every block kind and every inline kind round-trips, so a construct cannot be added to the
    /// reader and forgotten in the writer.
    /// </summary>
    /// <remarks>
    /// The reader's own summary says its set is deliberately small and nothing is there in
    /// anticipation. This is the check that keeps the pair in step: a new kind with no branch here
    /// throws <see cref="ArgumentOutOfRangeException"/> rather than writing something unreadable.
    /// </remarks>
    [Fact]
    public void EveryBlockAndInlineKindSurvivesTheRoundTrip()
    {
        const string source = """
            ---
            id: every.kind
            title: Every kind
            nodes: [Spark.Nodes.Core/Point.FromCoordinates]
            related: [concepts.lacing]
            since: "2026.9"
            ---

            # A heading

            Plain, **strong**, *emphasis*, `code` and a [link](concepts.files.md).

            > A quote.
            > Its second line.

            - First item
            - Second item, with `code`

            ```csharp
            var x = 1;
            ```

            | Port | Type |
            |---|---|
            | `point` | Point3d |

            ---

            ## A second heading
            """;

        HelpDocument original = HelpMarkdown.Parse(source, "every.kind");

        // Proof the fixture reaches every branch rather than merely looking as though it does.
        Assert.Equal(
            Enum.GetValues<HelpBlockKind>().ToHashSet(),
            original.Blocks.Select(block => block.Kind).ToHashSet());

        Assert.Equal(
            Enum.GetValues<HelpInlineKind>().ToHashSet(),
            original.Blocks.SelectMany(block => block.Inlines).Select(run => run.Kind).ToHashSet());

        AssertSame(original, HelpMarkdown.Parse(HelpMarkdown.Write(original), "every.kind"));
    }

    /// <summary>
    /// <c>since: 2026.9</c> is a YAML float, so a bare version would come back as a number in any
    /// other reader. It is quoted, exactly as the shipped topics write it by hand.
    /// </summary>
    [Fact]
    public void AVersionLikeScalarIsQuoted()
    {
        HelpDocument topic = HelpMarkdown.Parse("---\nid: t\ntitle: T\nsince: \"2026.9\"\n---\n\nBody.\n", "t");

        Assert.Contains("since: \"2026.9\"", HelpMarkdown.Write(topic), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>WriteBody</c> writes no front matter, which is what a generated page needs: it was never
    /// a file and has no file's metadata to write.
    /// </summary>
    [Fact]
    public void WriteBodyOmitsTheFrontMatter()
    {
        HelpDocument topic = HelpMarkdown.Parse("---\nid: t\ntitle: T\n---\n\n# H\n\nBody.\n", "t");

        string body = HelpMarkdown.WriteBody(topic.Blocks);

        Assert.DoesNotContain("id: t", body, StringComparison.Ordinal);
        Assert.StartsWith("# H", body, StringComparison.Ordinal);
    }

    private static void AssertSame(HelpDocument expected, HelpDocument actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Since, actual.Since);
        Assert.Equal(expected.Nodes, actual.Nodes);
        Assert.Equal(expected.Related, actual.Related);

        Assert.Equal(expected.Blocks.Count, actual.Blocks.Count);

        for (int i = 0; i < expected.Blocks.Count; i++)
        {
            HelpBlock one = expected.Blocks[i];
            HelpBlock two = actual.Blocks[i];

            Assert.Equal(one.Kind, two.Kind);
            Assert.Equal(one.Level, two.Level);
            Assert.Equal(one.Language, two.Language);
            Assert.Equal(one.Text, two.Text);
            Assert.Equal(one.Inlines, two.Inlines);

            Assert.Equal(one.Rows.Count, two.Rows.Count);

            for (int row = 0; row < one.Rows.Count; row++)
            {
                Assert.Equal(one.Rows[row].Count, two.Rows[row].Count);

                for (int cell = 0; cell < one.Rows[row].Count; cell++)
                {
                    Assert.Equal(one.Rows[row][cell], two.Rows[row][cell]);
                }
            }
        }
    }

    private static IReadOnlyList<string> Names()
    {
        List<string> names = [];

        foreach (string file in Directory.EnumerateFiles(TopicDirectory(), "*.md"))
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string TopicDirectory() =>
        Path.Combine(RepositoryRoot(), "docs", "help", "concepts");

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

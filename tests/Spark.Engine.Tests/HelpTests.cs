using System;
using System.IO;
using System.Linq;
using Spark.Api;
using Spark.Api.Help;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The help model, the Markdown reader and the generated node reference
/// (<c>E10-T5</c>, <c>E10-T13</c>).
/// </summary>
/// <remarks>
/// <b>The parser is checked against the corpus rather than against a specification.</b> It handles
/// the subset of Markdown Spark's own topics are written in, so the topics that exist are the test
/// data. A parser that passed a specification and choked on `docs/help/concepts/lacing.md` would be
/// correct and useless.
/// </remarks>
public sealed class HelpTests
{
    /// <summary>Front matter is read, and the body starts after it rather than including it.</summary>
    [Fact]
    public void FrontMatterIsReadAndDoesNotLeakIntoTheBody()
    {
        HelpDocument topic = HelpMarkdown.Parse(
            "---\nid: concepts.demo\ntitle: A demo topic\nrelated: [concepts.lacing, concepts.files]\nsince: \"0.1\"\n---\n\nSome prose.\n");

        Assert.Equal("concepts.demo", topic.Id);
        Assert.Equal("A demo topic", topic.Title);
        Assert.Equal(["concepts.lacing", "concepts.files"], topic.Related);
        Assert.Equal("0.1", topic.Since);
        Assert.DoesNotContain(topic.Blocks, b => b.PlainText.Contains("title:", StringComparison.Ordinal));
    }

    /// <summary>Headings, paragraphs, lists, quotes, rules and fences each become their own block.</summary>
    [Fact]
    public void EachBlockConstructIsRecognised()
    {
        HelpDocument topic = HelpMarkdown.Parse(
            "# Title\n\nA paragraph.\n\n- an item\n\n> a quote\n\n---\n\n```csharp\nvar x = 1;\n```\n");

        Assert.Contains(topic.Blocks, b => b.Kind == HelpBlockKind.Heading && b.Level == 1);
        Assert.Contains(topic.Blocks, b => b.Kind == HelpBlockKind.Paragraph);
        Assert.Contains(topic.Blocks, b => b.Kind == HelpBlockKind.ListItem);
        Assert.Contains(topic.Blocks, b => b.Kind == HelpBlockKind.Quote);
        Assert.Contains(topic.Blocks, b => b.Kind == HelpBlockKind.Rule);

        HelpBlock code = Assert.Single(topic.Blocks, b => b.Kind == HelpBlockKind.Code);
        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = 1;", code.Text);
    }

    /// <summary>Inline bold, italic, code and links are separated from the prose around them.</summary>
    [Fact]
    public void InlineMarkupIsSeparatedFromProse()
    {
        var runs = HelpMarkdown.ParseInlines("Plain **bold** and `code` and [a link](concepts.lacing) and *italic*.");

        Assert.Contains(runs, r => r.Kind == HelpInlineKind.Strong && r.Text == "bold");
        Assert.Contains(runs, r => r.Kind == HelpInlineKind.Code && r.Text == "code");
        Assert.Contains(runs, r => r.Kind == HelpInlineKind.Emphasis && r.Text == "italic");

        HelpInline link = Assert.Single(runs, r => r.Kind == HelpInlineKind.Link);
        Assert.Equal("a link", link.Text);
        Assert.Equal("concepts.lacing", link.Target);
    }

    /// <summary>
    /// A pipe table becomes rows of cells, and the row of dashes is dropped rather than read as
    /// data — otherwise every table in the help would have a row of hyphens in the middle of it.
    /// </summary>
    [Fact]
    public void ATableBecomesRowsAndTheAlignmentRowIsDropped()
    {
        HelpDocument topic = HelpMarkdown.Parse("| A | B |\n|---|---|\n| one | two |\n");

        HelpBlock table = Assert.Single(topic.Blocks, b => b.Kind == HelpBlockKind.Table);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("one", table.Rows[1][0][0].Text);
    }

    /// <summary>
    /// <b>The real corpus parses.</b> Every committed help topic is read, and each produces a
    /// title, an id and a body. This is what stops the parser from being correct against a
    /// specification and wrong about the documents it exists for.
    /// </summary>
    [Fact]
    public void EveryCommittedHelpTopicParses()
    {
        string directory = Path.Combine(RepositoryRoot(), "docs", "help");
        HelpLibrary library = new();

        int loaded = library.LoadDirectory(directory);

        Assert.True(loaded >= 9, $"expected the nine committed topics or more, loaded {loaded}");
        Assert.Empty(library.Problems);

        foreach (HelpDocument topic in library.Topics)
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Id), "a topic parsed with no id");
            Assert.False(string.IsNullOrWhiteSpace(topic.Title), $"'{topic.Id}' parsed with no title");
            Assert.NotEmpty(topic.Blocks);
        }
    }

    /// <summary>
    /// Every committed topic contains a worked example. The rule predates this code and is the
    /// reason the topics that exist are usable rather than restatements of signatures.
    /// </summary>
    [Fact]
    public void EveryCommittedTopicContainsAWorkedExample()
    {
        HelpLibrary library = new();
        library.LoadDirectory(Path.Combine(RepositoryRoot(), "docs", "help"));

        string[] without =
        [
            .. library.Topics.Where(t => !t.HasWorkedExample).Select(t => t.Id),
        ];

        Assert.True(
            without.Length == 0,
            "These topics have no code fence, no table and no section headed 'example', so they "
            + "contain no worked example: " + string.Join(", ", without));
    }

    /// <summary>A generated node page names the node, its ports and their descriptions.</summary>
    [Fact]
    public void AGeneratedNodePageDescribesItsPorts()
    {
        HelpDocument page = NodeReference.For(Sample());

        Assert.Equal("nodes.Test/Number.Add", page.Id);
        Assert.Equal("Number.Add", page.Title);
        Assert.Contains("Adds two numbers", page.PlainText(), StringComparison.Ordinal);
        Assert.Contains("the first addend", page.PlainText(), StringComparison.Ordinal);
        Assert.Contains("Lacing", page.PlainText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Generated pages exist for every node with no exceptions, which is the property that makes
    /// the reference complete by construction rather than by diligence.
    /// </summary>
    [Fact]
    public void EveryNodeInALibraryGetsAPage()
    {
        NodeLibrary library = new();
        library.Add(Sample());
        library.Add(Other());

        Assert.Equal(2, NodeReference.ForAll(library).Count);
        Assert.All(NodeReference.ForAll(library), page => Assert.True(page.HasWorkedExample));
    }

    /// <summary>The index links to every node and groups them by category.</summary>
    [Fact]
    public void TheIndexLinksToEveryNode()
    {
        NodeLibrary library = new();
        library.Add(Sample());
        library.Add(Other());

        HelpDocument index = NodeReference.Index(library);

        var links = index.Blocks
            .SelectMany(b => b.Inlines)
            .Where(i => i.Kind == HelpInlineKind.Link)
            .Select(i => i.Target)
            .ToArray();

        Assert.Contains("nodes.Test/Number.Add", links);
        Assert.Contains("nodes.Test/Number.Halve", links);
    }

    /// <summary>
    /// A hand-written topic naming a node wins over the generated page for it. The generated page
    /// says what a node takes; a topic somebody wrote says why you would want it.
    /// </summary>
    [Fact]
    public void AHandWrittenTopicWinsOverTheGeneratedPageForTheSameNode()
    {
        HelpLibrary library = new();
        library.Add(NodeReference.For(Sample()));
        library.Add(HelpMarkdown.Parse(
            "---\nid: concepts.adding\ntitle: Adding things up\nnodes: [Test/Number.Add]\n---\n\nWhy you would add.\n"));

        HelpDocument? found = library.ForNode("Test/Number.Add");

        Assert.NotNull(found);
        Assert.Equal("concepts.adding", found!.Id);
    }

    /// <summary>With no hand-written topic, the generated page is what F1 lands on.</summary>
    [Fact]
    public void WithNoHandWrittenTopicTheGeneratedPageIsUsed()
    {
        HelpLibrary library = new();
        library.Add(NodeReference.For(Sample()));

        Assert.Equal("nodes.Test/Number.Add", library.ForNode("Test/Number.Add")?.Id);
    }

    /// <summary>Search ranks a title match above a body mention, which is what a reader expects.</summary>
    [Fact]
    public void SearchRanksTitleMatchesAboveBodyMentions()
    {
        HelpLibrary library = new();
        library.Add(HelpMarkdown.Parse("---\nid: a\ntitle: Fillet\n---\n\nnothing here.\n"));
        library.Add(HelpMarkdown.Parse("---\nid: b\ntitle: Something else\n---\n\nmentions fillet in passing.\n"));

        var results = library.Search("Fillet");

        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].Id);
    }

    /// <summary>
    /// A malformed topic is skipped and named rather than taking the help system down. A reader
    /// consulting help is usually already stuck.
    /// </summary>
    [Fact]
    public void AMalformedTopicDoesNotThrow()
    {
        HelpDocument topic = HelpMarkdown.Parse("---\nnot: closed\n\n# Still readable\n\n`unclosed code", "fallback");

        Assert.Equal("fallback", topic.Id);
        Assert.NotEmpty(topic.Blocks);
    }

    private static NodeDefinition Sample() => new(
        NodeKey.Parse("Test/Number.Add"),
        "Number.Add",
        [
            new PortDefinition("a", typeof(double), 0, "the first addend"),
            new PortDefinition("b", typeof(double), 0, "the second addend", defaultValue: 0.0),
        ],
        [new PortDefinition("sum", typeof(double), 0, "the total")],
        args => [Convert.ToDouble(args[0]) + Convert.ToDouble(args[1])],
        description: "Adds two numbers together.",
        category: NodeCategories.Math);

    private static NodeDefinition Other() => new(
        NodeKey.Parse("Test/Number.Halve"),
        "Number.Halve",
        [new PortDefinition("value", typeof(double), 0, "the number to halve")],
        [new PortDefinition("half", typeof(double), 0, "half of it")],
        args => [Convert.ToDouble(args[0]) / 2.0],
        description: "Halves a number.",
        category: NodeCategories.Math);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Spark.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}

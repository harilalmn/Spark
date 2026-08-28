using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// Checks the documentation against the repository on every build.
/// </summary>
/// <remarks>
/// <para>
/// The client's standing instruction is that documentation is updated after every change.
/// A rule that is only written down decays; this is the part that makes it true, by turning
/// documentation rot into a red build.
/// </para>
/// <para>
/// The design follows CADScript's <c>verify/</c> and RCS's <c>DocumentationChecks.cs</c>,
/// both of which compile every fenced sample in their help files. Spark inherits that and
/// adds coverage checks in both directions once there is an API to check against, which is
/// the failure mode DoodleSharp discovered the hard way: its help had entries pointing at
/// members that no longer existed, and members with no entry at all, in both directions at
/// once, and neither was visible until a reflection diff was written.
/// </para>
/// <para>
/// Checks that need compiled Spark assemblies — sample compilation, node-to-topic coverage,
/// diagnostic-code coverage — arrive with the milestones that create the things they check.
/// They are listed in TASKS.md rather than stubbed here, so that this file never contains a
/// test that passes by doing nothing.
/// </para>
/// </remarks>
public sealed class DocumentationChecks
{
    private static readonly string Root = RepositoryRoot();

    /// <summary>
    /// Every help topic carries the front matter the help panel and the search index read.
    /// A topic missing <c>id</c> or <c>title</c> is invisible in the UI rather than broken,
    /// which is exactly the kind of fault nobody reports.
    /// </summary>
    [Fact]
    public void EveryHelpTopicHasCompleteFrontMatter()
    {
        List<string> problems = [];

        foreach (string topic in HelpTopics())
        {
            string text = File.ReadAllText(topic);
            string name = Relative(topic);

            if (!text.StartsWith("---", StringComparison.Ordinal))
            {
                problems.Add($"{name}: no YAML front matter.");
                continue;
            }

            int end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0)
            {
                problems.Add($"{name}: front matter is not terminated.");
                continue;
            }

            string frontMatter = text[3..end];

            foreach (string required in (string[])["id:", "title:"])
            {
                if (!frontMatter.Contains(required, StringComparison.Ordinal))
                {
                    problems.Add($"{name}: front matter has no '{required}'.");
                }
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// Every help topic contains a worked example. This is the client's standing instruction
    /// stated as a check: a topic that explains a concept without showing it is half a topic,
    /// and "add an example later" never happens on its own.
    /// </summary>
    [Fact]
    public void EveryHelpTopicContainsAWorkedExample()
    {
        List<string> withoutExample = HelpTopics()
            .Where(topic => !ContainsExample(File.ReadAllText(topic)))
            .Select(Relative)
            .ToList();

        Assert.Empty(withoutExample);
    }

    /// <summary>
    /// Relative links between documents resolve to files that exist. Renaming a document and
    /// missing one inbound link is the single most common way documentation breaks, and it is
    /// invisible until a reader follows the link.
    /// </summary>
    [Fact]
    public void EveryRelativeLinkResolves()
    {
        Regex link = new(@"\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
        List<string> broken = [];

        foreach (string document in AllMarkdown())
        {
            string directory = Path.GetDirectoryName(document)!;

            foreach (Match match in link.Matches(WithoutFencedCode(File.ReadAllText(document))))
            {
                string target = match.Groups[1].Value;

                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith('#')
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Strip any anchor; we are checking that the file exists, not the heading.
                string path = target.Split('#')[0];
                if (path.Length == 0)
                {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(directory, path));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    broken.Add($"{Relative(document)} -> {target}");
                }
            }
        }

        Assert.Empty(broken);
    }

    /// <summary>
    /// Every architecture decision referenced anywhere in the repository actually exists.
    /// ADR numbers are cited constantly in comments, build files and help text, and a citation
    /// pointing at nothing is worse than no citation: it implies a rationale was recorded when
    /// it was not.
    /// </summary>
    /// <remarks>
    /// This deliberately scans build files and source as well as documentation. An earlier
    /// version looked only at Markdown, and two citations in <c>Directory.Packages.props</c>
    /// and <c>.gitattributes</c> went unchecked as a result.
    /// <para>
    /// Note what this cannot catch: a citation pointing at a record that exists but is about
    /// something else entirely. Both of those two citations were of exactly that kind — they
    /// named real ADRs on unrelated subjects, so an existence check passed them. Semantic
    /// correctness of a citation is a review matter, not a testable one.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCitedArchitectureDecisionExists()
    {
        string decisionDirectory = Path.Combine(Root, "docs", "adr");
        if (!Directory.Exists(decisionDirectory))
        {
            return;
        }

        HashSet<string> existing = Directory
            .EnumerateFiles(decisionDirectory, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && name.Length >= 4)
            .Select(name => name![..4])
            .ToHashSet(StringComparer.Ordinal);

        Regex citation = new(@"ADR-(\d{4})", RegexOptions.Compiled);
        List<string> dangling = [];

        foreach (string file in AllMarkdown().Concat(AllCitableSource()))
        {
            foreach (Match match in citation.Matches(File.ReadAllText(file)))
            {
                string number = match.Groups[1].Value;
                if (!existing.Contains(number))
                {
                    dangling.Add($"{Relative(file)} cites ADR-{number}, which does not exist.");
                }
            }
        }

        Assert.Empty(dangling.Distinct());
    }

    /// <summary>
    /// Every help topic id named in the source resolves to a topic that exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This check exists because it was missing.</b> Five diagnostic codes resolved to
    /// <c>concepts.evaluation</c> from M0 onwards and <c>docs/help/concepts/evaluation.md</c>
    /// did not exist, so a user following an <c>SPK101x</c> code landed nowhere. Nothing was
    /// broken in any way a test could see: the id was a well-formed string, the codes were
    /// registered, the topics that did exist all passed their front-matter check. The gap was
    /// between two things nobody was comparing.
    /// </para>
    /// <para>
    /// It reads the source as text rather than referencing <c>Spark.Engine</c>, which is this
    /// harness's whole charter (see the remarks on the class). The cost is that a topic id
    /// assembled at run time from pieces would not be seen — and that is worth saying out loud,
    /// because it is the way this check could be defeated. Topic ids are constants and should
    /// stay constants.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryHelpTopicIdInTheSourceNamesATopicThatExists()
    {
        HashSet<string> topics = TopicIds();
        Regex reference = new(@"""(concepts\.[a-z][a-z0-9-]*)""", RegexOptions.Compiled);
        List<string> dangling = [];

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in reference.Matches(File.ReadAllText(file)))
            {
                string id = match.Groups[1].Value;

                if (!topics.Contains(id))
                {
                    dangling.Add($"{Relative(file)} names '{id}', which no topic declares.");
                }
            }
        }

        Assert.True(
            dangling.Count == 0,
            "Help topic ids in the source with no topic behind them:\n" + string.Join("\n", dangling));
    }

    /// <summary>
    /// Every <c>related</c> id in a topic's front matter resolves to a topic that exists.
    /// </summary>
    /// <remarks>
    /// The same fault in the other direction, and it had also happened: <c>concepts.lacing</c>
    /// listed <c>concepts.lists</c> as related, and there is no such topic — lacing is the
    /// topic that covers lists. A dangling cross-reference is worse than a missing one, because
    /// it reads as a promise that something more is written down somewhere.
    /// </remarks>
    [Fact]
    public void EveryRelatedTopicIdResolves()
    {
        HashSet<string> topics = TopicIds();
        List<string> dangling = [];

        foreach (string topic in HelpTopics())
        {
            Match related = Regex.Match(File.ReadAllText(topic), @"^related:\s*\[(.*?)\]", RegexOptions.Multiline);

            if (!related.Success)
            {
                continue;
            }

            foreach (string raw in related.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string id = raw.Trim();

                if (id.Length > 0 && !topics.Contains(id))
                {
                    dangling.Add($"{Relative(topic)} relates to '{id}', which no topic declares.");
                }
            }
        }

        Assert.True(
            dangling.Count == 0,
            "Related ids with no topic behind them:\n" + string.Join("\n", dangling));
    }

    /// <summary>
    /// Every topic's <c>id</c> is unique, and matches nothing else's.
    /// </summary>
    /// <remarks>
    /// Two topics sharing an id is a help panel that shows one of them and never the other,
    /// with nothing anywhere reporting a problem. It is also what would quietly make the two
    /// checks above pass while the wrong page was served.
    /// </remarks>
    [Fact]
    public void NoTwoTopicsShareAnId()
    {
        Dictionary<string, string> seen = [];
        List<string> clashes = [];

        foreach (string topic in HelpTopics())
        {
            Match id = Regex.Match(File.ReadAllText(topic), @"^id:\s*(\S+)", RegexOptions.Multiline);

            if (!id.Success)
            {
                clashes.Add($"{Relative(topic)} declares no id.");
                continue;
            }

            if (seen.TryGetValue(id.Groups[1].Value, out string? other))
            {
                clashes.Add($"{Relative(topic)} and {other} both declare '{id.Groups[1].Value}'.");
                continue;
            }

            seen[id.Groups[1].Value] = Relative(topic);
        }

        Assert.True(clashes.Count == 0, string.Join("\n", clashes));
    }

    /// <summary>
    /// The core project documents each carry a <c>Last updated</c> date. It is how a reader
    /// judges whether to trust a document, and the one piece of metadata that reliably reveals
    /// a document nobody has revisited.
    /// </summary>
    [Fact]
    public void EveryCoreDocumentCarriesALastUpdatedDate()
    {
        string[] core =
        [
            Path.Combine(Root, "docs", "PRD.md"),
            Path.Combine(Root, "docs", "EPICS.md"),
            Path.Combine(Root, "docs", "TASKS.md"),
            Path.Combine(Root, "docs", "TODO.md"),
            Path.Combine(Root, "AGENTS.md"),
        ];

        List<string> problems = [];

        foreach (string document in core)
        {
            if (!File.Exists(document))
            {
                problems.Add($"{Relative(document)} is missing.");
                continue;
            }

            if (!File.ReadAllText(document).Contains("Last updated", StringComparison.Ordinal))
            {
                problems.Add($"{Relative(document)} has no 'Last updated' line.");
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// Blanks out fenced code blocks, so that a sample's contents are not mistaken for the
    /// document's own markup.
    /// </summary>
    /// <remarks>
    /// A fenced block routinely contains links that are illustrative rather than navigable —
    /// template text quoting a file we have not written yet, or markup copied verbatim from
    /// another project. Treating those as broken links makes the check cry wolf, and a check
    /// that cries wolf gets suppressed. Line structure is preserved so any future line-numbered
    /// diagnostic still points at the right place.
    /// </remarks>
    private static string WithoutFencedCode(string text)
    {
        string[] lines = text.Split('\n');
        bool inFence = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                lines[i] = string.Empty;
            }
            else if (inFence)
            {
                lines[i] = string.Empty;
            }
        }

        return string.Join('\n', lines);
    }

    private static bool ContainsExample(string text)
    {
        // Either a fenced code sample, or a reference to a worked example graph. Both count:
        // for a node-graph tool, an example graph is often the better illustration.
        return text.Contains("```", StringComparison.Ordinal)
            || text.Contains(".spark", StringComparison.Ordinal);
    }

    private static HashSet<string> TopicIds()
    {
        HashSet<string> ids = [];

        foreach (string topic in HelpTopics())
        {
            Match id = Regex.Match(File.ReadAllText(topic), @"^id:\s*(\S+)", RegexOptions.Multiline);

            if (id.Success)
            {
                ids.Add(id.Groups[1].Value);
            }
        }

        return ids;
    }

    private static IEnumerable<string> HelpTopics()
    {
        string help = Path.Combine(Root, "docs", "help");

        return Directory.Exists(help)
            ? Directory.EnumerateFiles(help, "*.md", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    /// Source and build files that may carry an ADR citation in a comment.
    /// </summary>
    private static IEnumerable<string> AllCitableSource()
    {
        string[] patterns = ["*.cs", "*.csproj", "*.props", "*.targets", "*.yml", ".gitattributes"];

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(Root, pattern, SearchOption.AllDirectories))
            .Where(NotGenerated);
    }

    private static IEnumerable<string> AllMarkdown()
    {
        return Directory
            .EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            .Where(NotGenerated);
    }

    private static bool NotGenerated(string path)
    {
        char separator = Path.DirectorySeparatorChar;

        return !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}.git{separator}", StringComparison.Ordinal);
    }

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

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

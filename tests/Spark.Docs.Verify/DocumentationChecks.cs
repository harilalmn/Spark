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

            foreach (Match match in link.Matches(File.ReadAllText(document)))
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
    /// Every architecture decision referenced anywhere in the documentation actually exists.
    /// ADR numbers are cited constantly in comments and help text, and a citation pointing at
    /// nothing is worse than no citation: it implies a rationale was recorded when it was not.
    /// </summary>
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

        foreach (string document in AllMarkdown())
        {
            foreach (Match match in citation.Matches(File.ReadAllText(document)))
            {
                string number = match.Groups[1].Value;
                if (!existing.Contains(number))
                {
                    dangling.Add($"{Relative(document)} cites ADR-{number}, which does not exist.");
                }
            }
        }

        Assert.Empty(dangling.Distinct());
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

    private static bool ContainsExample(string text)
    {
        // Either a fenced code sample, or a reference to a worked example graph. Both count:
        // for a node-graph tool, an example graph is often the better illustration.
        return text.Contains("```", StringComparison.Ordinal)
            || text.Contains(".spark", StringComparison.Ordinal);
    }

    private static IEnumerable<string> HelpTopics()
    {
        string help = Path.Combine(Root, "docs", "help");

        return Directory.Exists(help)
            ? Directory.EnumerateFiles(help, "*.md", SearchOption.AllDirectories)
            : [];
    }

    private static IEnumerable<string> AllMarkdown()
    {
        return Directory
            .EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Spark.Docs.Verify;

/// <summary>
/// Checks that every Markdown link pointing at a heading actually lands on one (`E11-T7`).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="DocumentationChecks.EveryRelativeLinkResolves"/> checks the file and says so in
/// its own code</b> — <i>we are checking that the file exists, not the heading</i>. This is the
/// other half, and it guards the failure that is silent: a link whose file is fine and whose
/// anchor is stale still <i>works</i>, it just lands at the top of the page instead of at the
/// section it named. Nobody reports that, because nothing looks broken.
/// </para>
/// <para>
/// <b>It matters here more than it would in most repositories</b>, because these documents cite
/// sections whose titles carry <i>counts</i> —
/// <c>DYNAMO-COVERAGE.md#32-curves--11-types-187-members-141-reachable</c> — so every time a count
/// changes, every citation of it goes stale at once.
/// </para>
/// <para>
/// <b>A gate that passes by checking nothing is the failure this repository already has a note
/// about</b> ([N167](../../docs/NOTES.md)). So the check over the real documents is not the only
/// test here: <see cref="TheCheckCatchesABrokenAnchor"/> runs the same code over a synthetic
/// document whose anchor is deliberately wrong, and fails if it is not reported.
/// </para>
/// </remarks>
public sealed class LinkAnchorChecks
{
    private static readonly Regex Link =
        new(@"\[[^\]]*\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);

    private static readonly Regex Heading =
        new(@"^(#{1,6})\s+(.*?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly string Root = RepositoryRoot();

    /// <summary>Every link that names a heading lands on a heading that exists.</summary>
    [Fact]
    public void EveryAnchorResolves()
    {
        List<string> broken = Broken(
            Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories).Where(NotGenerated),
            Root);

        Assert.Empty(broken);
    }

    /// <summary>
    /// <b>The check is not vacuous.</b> Run over a document whose anchor names no heading, it
    /// reports it; run over the same link with the anchor corrected, it does not.
    /// </summary>
    /// <remarks>
    /// Without this, a checker with an inverted condition, an empty loop or a regular expression
    /// that matches nothing passes every real document perfectly and guards nothing at all.
    /// </remarks>
    [Fact]
    public void TheCheckCatchesABrokenAnchor()
    {
        string folder = Path.Combine(Path.GetTempPath(), "spark-anchor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "target.md"),
                "# The Title\n\n## A Real Section\n\nSome words.\n");

            string linking = Path.Combine(folder, "source.md");

            File.WriteAllText(linking, "See [it](target.md#a-section-that-is-not-there).\n");
            Assert.Single(Broken([linking], folder));

            File.WriteAllText(linking, "See [it](target.md#a-real-section).\n");
            Assert.Empty(Broken([linking], folder));

            // And the same for a link within one document.
            File.WriteAllText(linking, "# Top\n\nSee [it](#no-such-heading).\n");
            Assert.Single(Broken([linking], folder));

            File.WriteAllText(linking, "# Top\n\nSee [it](#top).\n");
            Assert.Empty(Broken([linking], folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The slug rules are GitHub's, and the awkward cases are taken from anchors that really
    /// appear in these documents rather than invented.
    /// </summary>
    /// <param name="heading">The heading text, without its hashes.</param>
    /// <param name="anchor">The anchor it must produce.</param>
    [Theory]
    // The one this repository cites constantly: a dotted number loses its dot, and an em dash
    // leaves the spaces either side of it behind, so the result carries a DOUBLE hyphen.
    [InlineData("3.2 Curves — 11 types, 187 members, 141 reachable", "32-curves--11-types-187-members-141-reachable")]
    [InlineData("Before you commit", "before-you-commit")]
    [InlineData("The protocol", "the-protocol")]
    [InlineData("13. Decision log", "13-decision-log")]
    [InlineData("`Curve2d`: lines and NURBS", "curve2d-lines-and-nurbs")]
    [InlineData("E2 — Geometry kernel", "e2--geometry-kernel")]
    [InlineData("What we could not interpret confidently", "what-we-could-not-interpret-confidently")]
    public void TheSlugRulesAreGitHubs(string heading, string anchor) =>
        Assert.Equal(anchor, Slug(heading));

    /// <summary>
    /// <b>A heading containing a code span keeps the words inside it.</b> GitHub drops the marks
    /// and keeps the text, so brackets inside backticks contribute nothing and no hyphen.
    /// </summary>
    /// <remarks>
    /// This is a regression test for a bug this check had for one build. Defusing link punctuation
    /// inside code spans is right when <i>scanning for links</i> and wrong when <i>slugging a
    /// heading</i>: applied to both, it turned a real heading in NOTES.md into an anchor with two
    /// invented hyphens in it, and reported a citation that had always been correct as broken. The
    /// two strippers are separate for that reason.
    /// </remarks>
    [Fact]
    public void AHeadingWithACodeSpanKeepsTheWordsInsideIt()
    {
        // The real heading that caught this, shortened.
        HashSet<string> anchors = AnchorsOf("## N34 — Dock's `Tool.Content` is `[TemplateContent]`\n");

        Assert.Contains("n34--docks-toolcontent-is-templatecontent", anchors);
    }

    /// <summary>A heading repeated in one document gets a numeric suffix, as GitHub does.</summary>
    [Fact]
    public void ARepeatedHeadingGetsANumericSuffix()
    {
        HashSet<string> anchors = AnchorsOf("## Notes\n\n## Notes\n\n## Notes\n");

        Assert.Contains("notes", anchors);
        Assert.Contains("notes-1", anchors);
        Assert.Contains("notes-2", anchors);
    }

    /// <summary>The links whose anchors name no heading.</summary>
    /// <param name="documents">The documents to check.</param>
    /// <param name="root">What paths are reported relative to.</param>
    /// <returns>One line per broken anchor.</returns>
    private static List<string> Broken(IEnumerable<string> documents, string root)
    {
        Dictionary<string, HashSet<string>> anchorsOf = new(StringComparer.OrdinalIgnoreCase);
        List<string> broken = [];

        foreach (string document in documents)
        {
            string directory = Path.GetDirectoryName(document)!;
            string text = WithoutFencedCode(File.ReadAllText(document));

            foreach (Match match in Link.Matches(text))
            {
                string target = match.Groups[1].Value;

                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int hash = target.IndexOf('#', StringComparison.Ordinal);
                if (hash < 0)
                {
                    continue;
                }

                string anchor = target[(hash + 1)..];
                if (anchor.Length == 0)
                {
                    continue;
                }

                string path = hash == 0
                    ? document
                    : Path.GetFullPath(Path.Combine(directory, target[..hash]));

                // A file that does not exist is EveryRelativeLinkResolves's complaint, not this
                // one. Reporting it twice would make one fault look like two.
                if (!File.Exists(path))
                {
                    continue;
                }

                if (!anchorsOf.TryGetValue(path, out HashSet<string>? anchors))
                {
                    anchors = AnchorsOf(File.ReadAllText(path));
                    anchorsOf[path] = anchors;
                }

                if (!anchors.Contains(anchor))
                {
                    broken.Add(
                        $"{Path.GetRelativePath(root, document).Replace('\\', '/')} -> {target}");
                }
            }
        }

        return broken;
    }

    /// <summary>Every anchor a document offers, from its headings.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The anchors, compared without regard to case.</returns>
    private static HashSet<string> AnchorsOf(string text)
    {
        HashSet<string> anchors = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> seen = new(StringComparer.Ordinal);

        // FENCES ONLY. A heading's slug is made from the heading as written, backticks and all -
        // GitHub keeps the words inside a code span and drops only the marks. Running the
        // link-neutralising stripper here instead turns `[TemplateContent]` into spaces and
        // invents two hyphens that the real anchor does not have, which is a bug this check had
        // for exactly one build.
        foreach (Match match in Heading.Matches(WithoutFences(text)))
        {
            string slug = Slug(match.Groups[2].Value);

            if (slug.Length == 0)
            {
                continue;
            }

            if (seen.TryGetValue(slug, out int already))
            {
                seen[slug] = already + 1;
                anchors.Add($"{slug}-{already + 1}");
            }
            else
            {
                seen[slug] = 0;
                anchors.Add(slug);
            }
        }

        return anchors;
    }

    /// <summary>The anchor a heading produces, by GitHub's rules.</summary>
    /// <param name="heading">The heading text.</param>
    /// <returns>The anchor.</returns>
    /// <remarks>
    /// <b>Lowercase, drop everything that is not a letter, a digit, a space or a hyphen, then turn
    /// the spaces into hyphens.</b> The order matters: dropping punctuation <i>before</i> the
    /// spaces are converted is what leaves a double hyphen where an em dash stood between two
    /// spaces, and that is not a quirk to work around — it is what the anchors in these documents
    /// actually are.
    /// </remarks>
    private static string Slug(string heading)
    {
        StringBuilder kept = new(heading.Length);

        foreach (char character in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character == ' ' || character == '-')
            {
                kept.Append(character);
            }
        }

        return kept.ToString().Trim().Replace(' ', '-');
    }

    /// <summary>The document with its fenced code blocks removed.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The prose.</returns>
    /// <remarks>
    /// A fence can hold a line that looks like a heading or a link, and neither is one. This is
    /// the same treatment <see cref="DocumentationChecks"/> gives its own scans.
    /// </remarks>
    private static string WithoutFencedCode(string text) => Strip(text, links: true);

    /// <summary>The document with its fenced code blocks removed and nothing else touched.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The prose, code spans intact.</returns>
    private static string WithoutFences(string text) => Strip(text, links: false);

    /// <summary>The document with its fences gone and, optionally, its code-span links defused.</summary>
    /// <param name="text">The document.</param>
    /// <param name="links">Whether to blank link punctuation inside code spans.</param>
    /// <returns>The stripped document.</returns>
    private static string Strip(string text, bool links)
    {
        StringBuilder prose = new(text.Length);
        bool inside = false;

        foreach (string line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inside = !inside;
                continue;
            }

            if (!inside)
            {
                prose.Append(links ? WithoutLinksInCode(line) : line).Append('\n');
            }
        }

        return prose.ToString();
    }

    /// <summary>The line with link punctuation inside backticks made harmless.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The line, with brackets and parentheses inside code spans blanked.</returns>
    /// <remarks>
    /// <para>
    /// <b>A link inside a code span is not a link, and this check learned that from its own
    /// documentation.</b> Writing a literal image link in a note <i>about</i> link checking made
    /// the checker report the path as a missing file: the stripper only ever handled fenced
    /// blocks, so an inline span went through as prose.
    /// </para>
    /// <para>
    /// <b>It blanks the punctuation rather than the span, and the first version got that
    /// wrong.</b> Removing a code span's <i>contents</i> breaks two things at once: a heading like
    /// <c>N34 — Dock's ToolContent</c> loses the word that its anchor is made of, and an ADR
    /// citation written in backticks stops being seen by the check that verifies ADRs exist.
    /// Blanking only the four characters that make a link keeps every word where it was.
    /// </para>
    /// </remarks>
    private static string WithoutLinksInCode(string line)
    {
        if (!line.Contains('`', StringComparison.Ordinal))
        {
            return line;
        }

        StringBuilder prose = new(line.Length);
        bool inside = false;

        foreach (char character in line)
        {
            if (character == '`')
            {
                inside = !inside;
                prose.Append(character);
                continue;
            }

            prose.Append(
                inside && character is '[' or ']' or '(' or ')' ? ' ' : character);
        }

        return prose.ToString();
    }


    /// <summary>Whether a document is one somebody wrote rather than one a build produced.</summary>
    /// <param name="path">The path.</param>
    /// <returns>Whether to check it.</returns>
    private static bool NotGenerated(string path)
    {
        char separator = Path.DirectorySeparatorChar;

        return !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}artifacts{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}.git{separator}", StringComparison.Ordinal);
    }

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    /// <returns>The root.</returns>
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

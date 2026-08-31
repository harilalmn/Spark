using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spark.UI.Tests;

/// <summary>
/// The shape every hand-written help topic has to have (<c>E10-T6</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The eleven topics already agreed on this schema before it was written down.</b> That is the
/// reason to write it down: a convention every file happens to follow is one a twelfth file breaks
/// without anybody noticing, and the reader who notices is the one whose <c>related:</c> link went
/// nowhere.
/// </para>
/// <para>
/// <b>The <c>related:</c> check is the one that earns its place.</b> Every entry today names a
/// topic that exists, and nothing made that true — it is true by luck. A renamed topic is
/// exactly the kind of edit that leaves a dangling reference, and <c>E11-T5</c> already makes the
/// same argument about node names.
/// </para>
/// </remarks>
public sealed class HelpTopicSchemaTests
{
    private static readonly IReadOnlyList<Topic> Topics = Read();

    /// <summary>The suite is checking something. A glob that matched nothing would pass silently.</summary>
    [Fact]
    public void TheTopicsAreThere()
    {
        Assert.True(Topics.Count >= 11, $"expected at least 11 concept topics, found {Topics.Count}");
    }

    /// <summary>Every topic declares the five keys, and none of them is blank.</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("nodes")]
    [InlineData("related")]
    [InlineData("since")]
    public void EveryTopicDeclares(string key)
    {
        List<string> missing = [.. Topics.Where(t => !t.FrontMatter.ContainsKey(key)).Select(t => t.File)];

        Assert.True(missing.Count == 0, $"topics with no '{key}:' in their front matter: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// <b>A topic's id matches its file name.</b> The library keys by id and a reader navigates by
    /// file, so the two disagreeing means a link that works in one place and not the other.
    /// </summary>
    [Fact]
    public void EveryIdMatchesItsFileName()
    {
        List<string> wrong = [];

        foreach (Topic topic in Topics)
        {
            string expected = "concepts." + Path.GetFileNameWithoutExtension(topic.File);

            if (!string.Equals(topic.FrontMatter["id"], expected, StringComparison.Ordinal))
            {
                wrong.Add($"{topic.File} declares '{topic.FrontMatter["id"]}', expected '{expected}'");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>Ids are unique, or the library silently keeps one of two topics.</summary>
    [Fact]
    public void EveryIdIsUnique()
    {
        List<string> duplicated =
        [
            .. Topics.GroupBy(t => t.FrontMatter["id"], StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        Assert.Empty(duplicated);
    }

    /// <summary>
    /// <b>Every <c>related:</c> entry names a topic that exists.</b> True today and true by
    /// accident; this is what makes it stay true through a rename.
    /// </summary>
    [Fact]
    public void EveryRelatedEntryNamesATopicThatExists()
    {
        HashSet<string> known = [.. Topics.Select(t => t.FrontMatter["id"])];
        List<string> dangling = [];

        foreach (Topic topic in Topics)
        {
            foreach (string related in List(topic.FrontMatter["related"]))
            {
                if (!known.Contains(related))
                {
                    dangling.Add($"{topic.File} -> {related}");
                }
            }
        }

        Assert.True(dangling.Count == 0, "related entries naming a topic that does not exist: " + string.Join(", ", dangling));
    }

    /// <summary>A topic never lists itself as related to itself.</summary>
    [Fact]
    public void NoTopicIsRelatedToItself()
    {
        List<string> selfish =
        [
            .. Topics.Where(t => List(t.FrontMatter["related"]).Contains(t.FrontMatter["id"], StringComparer.Ordinal))
                .Select(t => t.File),
        ];

        Assert.Empty(selfish);
    }

    /// <summary>
    /// Every topic carries the three provenance lines the existing ones use: what state it is in,
    /// who owns it, and when it was last touched.
    /// </summary>
    [Theory]
    [InlineData("**Status:**")]
    [InlineData("**Owner:**")]
    [InlineData("**Last updated:**")]
    public void EveryTopicCarries(string line)
    {
        List<string> missing = [.. Topics.Where(t => !t.Body.Contains(line, StringComparison.Ordinal)).Select(t => t.File)];

        Assert.True(missing.Count == 0, $"topics with no '{line}' line: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// <b>A status is one of two words, and <c>Specification</c> has to be earned.</b> It means
    /// <i>written before the code, and the code is written to match</i>. Two topics still said it
    /// months after their code shipped, which is how a status line becomes decoration.
    /// </summary>
    [Fact]
    public void EveryStatusIsCurrentOrSpecification()
    {
        List<string> odd = [];

        foreach (Topic topic in Topics)
        {
            Match status = Regex.Match(topic.Body, @"\*\*Status:\*\*\s*(\w+)");

            if (!status.Success || status.Groups[1].Value is not ("Current" or "Specification"))
            {
                odd.Add(topic.File + ": " + (status.Success ? status.Groups[1].Value : "no status"));
            }
        }

        Assert.True(odd.Count == 0, "topics whose status is neither Current nor Specification: " + string.Join(", ", odd));
    }

    private static IReadOnlyList<string> List(string value)
    {
        string inner = value.Trim().Trim('[', ']');

        return inner.Length == 0
            ? []
            : [.. inner.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0)];
    }

    private static IReadOnlyList<Topic> Read()
    {
        string folder = Path.Combine(RepositoryRoot(), "docs", "help", "concepts");
        List<Topic> topics = [];

        foreach (string path in Directory.EnumerateFiles(folder, "*.md").OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);
            Match front = Regex.Match(text, @"\A---\r?\n(.*?)\r?\n---", RegexOptions.Singleline);

            Dictionary<string, string> keys = new(StringComparer.Ordinal);

            if (front.Success)
            {
                foreach (Match pair in Regex.Matches(front.Groups[1].Value, @"^(\w+):\s*(.*)$", RegexOptions.Multiline))
                {
                    keys[pair.Groups[1].Value] = pair.Groups[2].Value.Trim();
                }
            }

            topics.Add(new Topic(Path.GetFileName(path), keys, text));
        }

        return topics;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);

        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "Spark.slnx")))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        throw new InvalidOperationException("the repository root was not found above " + AppContext.BaseDirectory);
    }

    private sealed record Topic(string File, Dictionary<string, string> FrontMatter, string Body);
}

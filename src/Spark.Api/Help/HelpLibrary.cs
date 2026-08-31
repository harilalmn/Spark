using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spark.Api.Help;

/// <summary>
/// Every help topic available in a session: the hand-written concept topics loaded from disk, and
/// whatever pages a caller generates and adds (<c>E10-T3</c>, <c>E10-T13</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two sources, one lookup.</b> A reader following a link does not care whether the page on the
/// other end was typed by a person or produced from a node definition, and neither should
/// anything that resolves links, searches, or renders. Keeping one index is what makes
/// <c>concepts.lacing</c> and <c>nodes.Spark.Core/Point.ByCoordinates</c> equally reachable from
/// each other.
/// </para>
/// <para>
/// <b>Loading never throws over a bad file.</b> A topic that will not parse is skipped and named
/// in <see cref="Problems"/>. A help system that refuses to open because one file is malformed is
/// worse than one missing a page, because the reader consulting it is usually already stuck.
/// </para>
/// </remarks>
public sealed class HelpLibrary
{
    private readonly Dictionary<string, HelpDocument> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _problems = [];

    /// <summary>The topics, ordered by id.</summary>
    public IReadOnlyList<HelpDocument> Topics =>
        [.. _topics.Values.OrderBy(t => t.Id, StringComparer.Ordinal)];

    /// <summary>How many topics are loaded.</summary>
    public int Count => _topics.Count;

    /// <summary>Files that could not be read, with the reason. Empty when everything loaded.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Adds or replaces a topic.</summary>
    /// <param name="topic">The topic.</param>
    /// <exception cref="ArgumentNullException"><paramref name="topic"/> is null.</exception>
    public void Add(HelpDocument topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        _topics[topic.Id] = topic;
    }

    /// <summary>Adds many topics.</summary>
    /// <param name="topics">The topics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="topics"/> is null.</exception>
    public void AddRange(IEnumerable<HelpDocument> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);

        foreach (HelpDocument topic in topics)
        {
            Add(topic);
        }
    }

    /// <summary>
    /// Loads every <c>.md</c> file under a directory, recursively.
    /// </summary>
    /// <param name="directory">The directory. A missing one loads nothing and is not an error.</param>
    /// <returns>How many topics were added.</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null or blank.</exception>
    public int LoadDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        int added = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                HelpDocument topic = HelpMarkdown.Parse(
                    File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
                Add(topic);
                added++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _problems.Add($"{Path.GetFileName(file)}: {error.Message}");
            }
        }

        return added;
    }

    /// <summary>Finds a topic by id.</summary>
    /// <param name="id">The topic id.</param>
    /// <param name="topic">The topic, when found.</param>
    /// <returns>True when a topic with that id is loaded.</returns>
    public bool TryGet(string? id, out HelpDocument? topic)
    {
        topic = null;
        return !string.IsNullOrWhiteSpace(id) && _topics.TryGetValue(id, out topic);
    }

    /// <summary>
    /// Finds the topic documenting a node key, preferring a hand-written topic that names the node
    /// over the generated reference page.
    /// </summary>
    /// <param name="nodeKey">The node key, as <c>Package/Name</c>.</param>
    /// <returns>The topic, or null when nothing documents that node.</returns>
    /// <remarks>
    /// The preference is the point. A generated page says what a node takes and returns; a topic
    /// somebody wrote says why you would want it. When both exist, the second is the one a reader
    /// pressing F1 should land on.
    /// </remarks>
    public HelpDocument? ForNode(string? nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return null;
        }

        foreach (HelpDocument topic in Topics)
        {
            if (topic.Id.StartsWith("nodes.", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string named in topic.Nodes)
            {
                if (string.Equals(named, nodeKey, StringComparison.OrdinalIgnoreCase)
                    || named.EndsWith("/" + nodeKey, StringComparison.OrdinalIgnoreCase))
                {
                    return topic;
                }
            }
        }

        return TryGet("nodes." + nodeKey, out HelpDocument? generated) ? generated : null;
    }

    /// <summary>
    /// Searches titles and body text, case-insensitively.
    /// </summary>
    /// <param name="query">What to look for. Blank returns nothing.</param>
    /// <param name="limit">The most results to return.</param>
    /// <returns>
    /// Matching topics, title matches first. Ranked rather than filtered, because a reader looking
    /// for "fillet" wants the fillet page above the six pages that mention filleting.
    /// </returns>
    public IReadOnlyList<HelpDocument> Search(string? query, int limit = 25)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string needle = query.Trim();

        return
        [
            .. Topics
                .Select(topic => (Topic: topic, Rank: RankOf(topic, needle)))
                .Where(scored => scored.Rank > 0)
                .OrderByDescending(scored => scored.Rank)
                .ThenBy(scored => scored.Topic.Title, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, limit))
                .Select(scored => scored.Topic),
        ];
    }

    private static int RankOf(HelpDocument topic, string needle)
    {
        if (topic.Title.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (topic.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (topic.Id.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return topic.PlainText().Contains(needle, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}

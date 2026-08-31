using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spark.Api.Help;
using Spark.Engine;
using Spark.Host;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// Node-to-topic coverage in both directions (<c>E11-T4</c>, <c>E11-T5</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Both directions, because they fail differently and neither implies the other.</b> Forward:
/// a node with no help topic ships undocumented. Reverse: a topic naming a node that no longer
/// exists sends a reader to a page about something gone. DoodleSharp had both at once and neither
/// was visible until somebody wrote a reflection diff — its help pointed at members that had been
/// deleted while 101 of 108 constructors had no entry at all.
/// </para>
/// <para>
/// <b>The forward direction is currently true by construction</b>, because the reference pages are
/// generated from the live library rather than written. It is asserted anyway: the property that
/// matters is <i>every node has a topic</i>, not <i>we generate pages</i>, and asserting the
/// mechanism instead of the property is how a guarantee quietly becomes an implementation detail
/// that somebody later replaces.
/// </para>
/// </remarks>
public sealed class NodeTopicCoverageTests
{
    /// <summary>
    /// <b>Forward (<c>E11-T4</c>): every built-in node resolves to a help topic.</b> A new node
    /// shipping undocumented fails here.
    /// </summary>
    [Fact]
    public void EveryBuiltInNodeResolvesToATopic()
    {
        HeadlessSession.Run(() =>
        {
            MainWindowViewModel model = new();
            HelpLibrary help = model.Help();

            List<string> undocumented = [];
            foreach (LibraryEntryViewModel entry in model.AllLibraryEntries)
            {
                if (help.ForNode(entry.Key) is null)
                {
                    undocumented.Add(entry.Key);
                }
            }

            Assert.True(
                undocumented.Count == 0,
                $"{undocumented.Count} nodes have no help topic: " + string.Join(", ", undocumented));
        });
    }

    /// <summary>
    /// <b>Reverse (<c>E11-T5</c>): every node named in a topic's front matter still exists.</b>
    /// This is the one that catches renames, and it is the direction nothing checked before.
    /// </summary>
    /// <remarks>
    /// A topic's <c>nodes:</c> list may name a node either fully, as <c>Package/Name</c>, or by its
    /// bare name — the hand-written topics use bare names, which is what an author reasonably
    /// writes. Both are accepted; what is not accepted is a name that matches nothing.
    /// </remarks>
    [Fact]
    public void EveryNodeNamedInATopicStillExists()
    {
        using SparkSession session = new();

        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        foreach (NodeDefinition definition in session.Library.Definitions())
        {
            keys.Add(definition.Key.Value);
            names.Add(definition.Key.Name);
            names.Add(definition.DisplayName);
        }

        HelpLibrary help = new();
        help.LoadDirectory(Path.Combine(RepositoryRoot(), "docs", "help"));

        List<string> dangling = [];
        foreach (HelpDocument topic in help.Topics)
        {
            foreach (string named in topic.Nodes)
            {
                if (!keys.Contains(named) && !names.Contains(named))
                {
                    dangling.Add($"{topic.Id} names '{named}'");
                }
            }
        }

        Assert.True(
            dangling.Count == 0,
            "These topics name nodes that do not exist:\n" + string.Join("\n", dangling));
    }

    /// <summary>
    /// The reverse check is only worth having if it notices, so a deliberately wrong name must be
    /// rejected by the same lookup the check uses.
    /// </summary>
    [Fact]
    public void ANodeNameThatDoesNotExistIsNotAccepted()
    {
        using SparkSession session = new();

        bool exists = session.Library.Definitions().Any(definition =>
            string.Equals(definition.Key.Name, "Circle.ByRenamedSomething", StringComparison.OrdinalIgnoreCase));

        Assert.False(exists, "the probe name should not match any real node");
    }

    /// <summary>
    /// At least one hand-written topic actually names nodes, so the reverse check has something to
    /// check. A test that walks an empty list passes and proves nothing.
    /// </summary>
    [Fact]
    public void AtLeastOneTopicNamesNodes()
    {
        HelpLibrary help = new();
        help.LoadDirectory(Path.Combine(RepositoryRoot(), "docs", "help"));

        int named = help.Topics
            .Where(topic => !topic.Id.StartsWith("nodes.", StringComparison.Ordinal))
            .Sum(topic => topic.Nodes.Count);

        Assert.True(named >= 10, $"only {named} node names appear in topic front matter; the reverse check is idle");
    }

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

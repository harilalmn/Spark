using System;
using System.Linq;
using Spark.Api;
using Spark.UI.ViewModels;

namespace Spark.UI.Tests;

/// <summary>
/// The library panel grouped by category — `E8-T24`.
/// </summary>
/// <remarks>
/// <b>A flat list of a hundred and eight nodes is a list nobody reads.</b> It gets scrolled past on
/// the way to the search box, which means the panel only ever answers a question the user already
/// knew how to ask — and somebody who does not yet know that <c>Surface.ByLoft</c> exists cannot
/// search for it. Asked for directly, against Dynamo's library.
/// </remarks>
public sealed class LibraryGroupingTests
{
    /// <summary>Every entry lands in exactly one group, and none is lost.</summary>
    [Fact]
    public void EveryEntryIsInExactlyOneGroup()
    {
        MainWindowViewModel model = new();

        Assert.NotEmpty(model.LibraryGroups);

        Assert.Equal(
            model.LibraryEntries.Count,
            model.LibraryGroups.Sum(group => group.Entries.Count));

        Assert.Equal(
            model.LibraryEntries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key),
            model.LibraryGroups
                .SelectMany(group => group.Entries)
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => e.Key));
    }

    /// <summary>A group holds only the entries whose category it is named for.</summary>
    [Fact]
    public void AGroupHoldsOnlyItsOwnCategory()
    {
        MainWindowViewModel model = new();

        foreach (LibraryGroupViewModel group in model.LibraryGroups)
        {
            Assert.All(group.Entries, entry => Assert.Equal(group.Category, entry.Category));
        }
    }

    /// <summary>
    /// <b>Alphabetical, with <c>Custom</c> last.</b> Alphabetical is the order somebody can predict
    /// without being told it; <c>Custom</c> is the catch-all, and sorting "everything else" into
    /// the middle of the specific things reads as an accident.
    /// </summary>
    [Fact]
    public void CategoriesAreAlphabeticalWithCustomLast()
    {
        MainWindowViewModel model = new();

        string[] names = [.. model.LibraryGroups.Select(group => group.Category)];
        string[] named = [.. names.Where(n => n != NodeCategories.Custom)];

        Assert.Equal(named.OrderBy(n => n, StringComparer.Ordinal), named);

        if (Array.IndexOf(names, NodeCategories.Custom) is int custom and >= 0)
        {
            Assert.Equal(names.Length - 1, custom);
        }
    }

    /// <summary>
    /// <b>With no query the groups are closed</b>, which is the whole point: ten headings fit on a
    /// screen where a hundred and eight entries do not.
    /// </summary>
    [Fact]
    public void GroupsStartClosed()
    {
        MainWindowViewModel model = new();

        Assert.All(model.LibraryGroups, group => Assert.False(group.IsExpanded));
    }

    /// <summary>
    /// <b>Searching narrows the tree and opens it.</b> A user who has typed has already said what
    /// they want; charging them a click to see it would be charging for nothing.
    /// </summary>
    [Fact]
    public void SearchingNarrowsAndOpensTheTree()
    {
        MainWindowViewModel model = new();

        int before = model.LibraryGroups.Count;

        model.LibrarySearch = "circle";

        Assert.NotEmpty(model.LibraryGroups);
        Assert.True(
            model.LibraryGroups.Count <= before,
            "searching did not narrow the tree");
        Assert.All(model.LibraryGroups, group => Assert.True(group.IsExpanded));
        Assert.All(model.LibraryGroups, group => Assert.NotEmpty(group.Entries));
    }

    /// <summary>And clearing the box puts it back, closed.</summary>
    [Fact]
    public void ClearingTheSearchRestoresTheWholeTree()
    {
        MainWindowViewModel model = new();

        int before = model.LibraryGroups.Count;

        model.LibrarySearch = "circle";
        model.LibrarySearch = string.Empty;

        Assert.Equal(before, model.LibraryGroups.Count);
        Assert.All(model.LibraryGroups, group => Assert.False(group.IsExpanded));
    }

    /// <summary>
    /// The count beside a heading is what makes a collapsed group legible, so it has to be the
    /// number of things actually in it.
    /// </summary>
    [Fact]
    public void TheCountMatchesTheEntries()
    {
        MainWindowViewModel model = new();

        Assert.All(model.LibraryGroups, group => Assert.Equal(group.Entries.Count, group.Count));
    }

    /// <summary>
    /// <b>The categories are the ones the canvas colours by</b>, so a node's colour and its place
    /// in the panel cannot disagree.
    /// </summary>
    [Fact]
    public void EveryGroupNameIsAKnownCategory()
    {
        MainWindowViewModel model = new();

        string[] known =
        [
            NodeCategories.Input, NodeCategories.Logic, NodeCategories.Display,
            NodeCategories.Solid, NodeCategories.Curve, NodeCategories.Point,
            NodeCategories.Script, NodeCategories.List, NodeCategories.Math,
            NodeCategories.Custom,
        ];

        Assert.All(
            model.LibraryGroups,
            group => Assert.Contains(group.Category, known, StringComparer.Ordinal));
    }

    /// <summary>A node built by the user reaches the tree, not only the flat list.</summary>
    [Fact]
    public void APublishedCustomNodeReachesTheTree()
    {
        MainWindowViewModel model = new();

        int before = model.LibraryGroups.Sum(group => group.Entries.Count);

        model.PublishCustomNode(TestGraphs.Library.ByName("Point.ByCoordinates"));

        Assert.Equal(before + 1, model.LibraryGroups.Sum(group => group.Entries.Count));
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Api;

namespace Spark.UI.ViewModels;

/// <summary>
/// One category in the library panel, and the entries filed under it (<c>E8-T24</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A flat list of a hundred and eight nodes is a list nobody reads.</b> It is scrolled past on
/// the way to the search box, which means the panel only ever answers a question the user already
/// knew how to ask — and a user who does not yet know that <c>Surface.ByLoft</c> exists cannot
/// search for it. Categories are how somebody finds out what a tool can do, which is why every
/// comparable editor has them.
/// </para>
/// <para>
/// <b>The category is already on every node</b>, as <c>SparkNode(Category = …)</c>, and it is
/// already what the canvas colours a node by. This groups by the same string, so a node's colour
/// on the canvas and its place in the panel can never disagree.
/// </para>
/// </remarks>
public sealed partial class LibraryGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    internal LibraryGroupViewModel(string category, IEnumerable<LibraryEntryViewModel> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Category = category;
        Entries = [.. entries];

        // Built here rather than by the panel, so the two collections cannot fall out of step: a
        // subgroup is a view of `Entries`, not a second list that has to be kept level with it.
        // Empty kinds are omitted rather than shown at zero — a `Create` heading over nothing is a
        // row that costs a line and answers no question.
        foreach (NodeMemberKind kind in Kinds)
        {
            List<LibraryEntryViewModel> matching = [];
            foreach (LibraryEntryViewModel entry in Entries)
            {
                if (entry.Kind == kind)
                {
                    matching.Add(entry);
                }
            }

            if (matching.Count > 0)
            {
                Subgroups.Add(new LibraryKindGroupViewModel(kind, matching));
            }
        }
    }

    /// <summary>
    /// The three kinds, in the order the panel shows them.
    /// </summary>
    /// <remarks>
    /// <b>Create, then Action, then Query</b> — the order a graph is built in rather than
    /// alphabetical. You make a thing before you change it and change it before you measure it, and
    /// that is the order Dynamo shows them in for the same reason.
    /// </remarks>
    private static readonly NodeMemberKind[] Kinds =
        [NodeMemberKind.Create, NodeMemberKind.Action, NodeMemberKind.Query];

    /// <summary>The category's name, as it is written on the node's attribute.</summary>
    public string Category { get; }

    /// <summary>The entries filed under it, in library order.</summary>
    public ObservableCollection<LibraryEntryViewModel> Entries { get; }

    /// <summary>
    /// The same entries split into <b>Create</b>, <b>Action</b> and <b>Query</b> blocks
    /// (<c>E8-T29</c>), skipping any block that would be empty.
    /// </summary>
    public ObservableCollection<LibraryKindGroupViewModel> Subgroups { get; } = [];

    /// <summary>
    /// How many entries the group holds, shown beside its name.
    /// </summary>
    /// <remarks>
    /// A count on a collapsed group is the difference between "there is something in here" and
    /// "there might be". It also makes a search result legible without expanding anything: three
    /// categories reading 1, 4 and 12 says where the answer probably is.
    /// </remarks>
    public int Count => Entries.Count;

    /// <inheritdoc/>
    public override string ToString() => Category + " (" + Count.ToString(
        System.Globalization.CultureInfo.InvariantCulture) + ")";
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

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
    }

    /// <summary>The category's name, as it is written on the node's attribute.</summary>
    public string Category { get; }

    /// <summary>The entries filed under it, in library order.</summary>
    public ObservableCollection<LibraryEntryViewModel> Entries { get; }

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

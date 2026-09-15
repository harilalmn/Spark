using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Spark.Api;
using Spark.UI.Theming;

namespace Spark.UI.ViewModels;

/// <summary>
/// One <b>Create</b> / <b>Action</b> / <b>Query</b> block inside a library category, and the
/// entries filed under it (<c>E8-T29</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A category is still a wall.</b> Grouping by category (<c>E8-T24</c>) turned a hundred and
/// thirty-six names into ten headings, and then <c>Solid</c> alone had thirty-eight entries under
/// it. Split three ways, somebody who wants to <i>make</i> a solid never reads the eleven nodes
/// that change one or the four that measure one. Dynamo does exactly this, the client asked for it
/// by name, and users arrive already able to read the three marks.
/// </para>
/// <para>
/// The rail colour and the glyph live in <see cref="NodeKindGlyphs"/> rather than here, so that a
/// view model carries no drawing decisions and the panel and any future consumer cannot disagree
/// about what a Query looks like.
/// </para>
/// </remarks>
public sealed partial class LibraryKindGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    internal LibraryKindGroupViewModel(NodeMemberKind kind, IEnumerable<LibraryEntryViewModel> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Kind = kind;
        Entries = [.. entries];
        Label = NodeKindGlyphs.LabelOf(kind);
        Description = NodeKindGlyphs.DescriptionOf(kind);
        Rail = NodeKindGlyphs.BrushOf(kind);
        Rows = BuildRows(Entries);
    }

    /// <summary>
    /// Folds the overloads of one member into a single row, leaving everything else alone
    /// (<c>E5-T4</c>).
    /// </summary>
    /// <param name="entries">The block's entries, in library order.</param>
    /// <returns>The rows to draw: entries as themselves, overload families as one item each.</returns>
    /// <remarks>
    /// <para>
    /// <b>The bracket is the signal.</b> The importer appends a parameter list to a display name
    /// only when two candidates collide on it, so a name containing <c>(</c> is by construction one
    /// of at least two and the text before the bracket is the member they share. Nothing has to be
    /// recorded on the definition and nothing is inferred from the member.
    /// </para>
    /// <para>
    /// <b>Library order is preserved, and a family takes the place of its first variant.</b> The
    /// entries arrive sorted, so a family's members are already adjacent — but building the rows by
    /// remembering where the first one sat, rather than by appending families at the end, is what
    /// keeps the block alphabetical whether or not that stays true.
    /// </para>
    /// </remarks>
    private static ObservableCollection<object> BuildRows(IReadOnlyList<LibraryEntryViewModel> entries)
    {
        Dictionary<string, List<LibraryEntryViewModel>> families = [];

        foreach (LibraryEntryViewModel entry in entries)
        {
            if (BaseNameOf(entry.DisplayName) is { } member)
            {
                if (!families.TryGetValue(member, out List<LibraryEntryViewModel>? kin))
                {
                    families[member] = kin = [];
                }

                kin.Add(entry);
            }
        }

        ObservableCollection<object> rows = [];
        HashSet<string> placed = [];

        foreach (LibraryEntryViewModel entry in entries)
        {
            if (BaseNameOf(entry.DisplayName) is not { } member)
            {
                rows.Add(entry);
                continue;
            }

            // A collision can be split across two kinds - one Create overload and one Query - which
            // leaves a family of one in this block. One variant is not a family, and a row that
            // hid a single node behind a flyout would be strictly worse than the node.
            if (families[member].Count == 1)
            {
                rows.Add(entry);
                continue;
            }

            if (placed.Add(member))
            {
                rows.Add(new LibraryOverloadViewModel(member, families[member]));
            }
        }

        return rows;
    }

    /// <summary>The member name an overloaded display name belongs to, or null when it is not one.</summary>
    /// <param name="displayName">The entry's display name.</param>
    /// <returns>The text before the parameter list, or null.</returns>
    /// <remarks>
    /// The name must also <i>end</i> in a bracket, so that a member whose own name contains one -
    /// which no C# identifier can, but a <c>SparkNode(Name = ...)</c> could - is not silently
    /// folded into a family with something it has nothing to do with.
    /// </remarks>
    private static string? BaseNameOf(string displayName)
    {
        if (!displayName.EndsWith(')'))
        {
            return null;
        }

        int bracket = displayName.IndexOf('(', StringComparison.Ordinal);

        return bracket > 0 ? displayName[..bracket] : null;
    }

    /// <summary>Which of the three this block is.</summary>
    public NodeMemberKind Kind { get; }

    /// <summary>The entries filed under it, in library order.</summary>
    /// <remarks>
    /// <b>Flat, and deliberately left flat when <see cref="Rows"/> was added.</b> This is what
    /// <see cref="Count"/> counts and what a test asking <i>is this node in the library</i> reads;
    /// folding overloads into it would have made both answer a different question for a reason that
    /// is entirely about drawing.
    /// </remarks>
    public ObservableCollection<LibraryEntryViewModel> Entries { get; }

    /// <summary>
    /// The same entries as the panel draws them: a <see cref="LibraryEntryViewModel"/> for an
    /// ordinary node, and a <see cref="LibraryOverloadViewModel"/> standing for a member's
    /// overloads (<c>E5-T4</c>).
    /// </summary>
    /// <remarks>
    /// <b>Typed as <c>object</c> because the two rows are unrelated and a common base would be a
    /// fiction.</b> A node and a family of nodes share a rail colour and nothing else: one is
    /// placeable and the other opens a flyout. The panel picks a template by type, which is what
    /// <c>DataTemplate</c> does, and inventing an abstract row to avoid the word <c>object</c>
    /// would push the same switch into the view models.
    /// </remarks>
    public ObservableCollection<object> Rows { get; }

    /// <summary>The word beside the glyph — <c>Create</c>, <c>Action</c> or <c>Query</c>.</summary>
    public string Label { get; }

    /// <summary>One sentence saying what the kind means, shown as the block's tooltip.</summary>
    public string Description { get; }

    /// <summary>The colour of the vertical rail drawn down the left of the block.</summary>
    public IBrush Rail { get; }

    /// <summary>The glyph drawn at the head of the block, in a sixteen-by-sixteen box.</summary>
    /// <remarks>
    /// <b>Resolved on access rather than in the constructor, and that is not a micro-optimisation.</b>
    /// Building a <c>Geometry</c> needs Avalonia's render interface, and this view model is built
    /// while the library is loaded - which happens in tests that have no rendering platform at all.
    /// Constructing it eagerly made <c>MainWindowViewModel</c>'s constructor throw in those, and it
    /// passed only when some other test class had happened to initialise the platform first, which
    /// is a flake waiting for a machine with different scheduling.
    /// </remarks>
    public Avalonia.Media.Geometry Glyph => NodeKindGlyphs.GeometryOf(Kind);

    /// <summary>How many entries the block holds, shown beside its label.</summary>
    public int Count => Entries.Count;

    /// <inheritdoc/>
    public override string ToString() => Label + " (" + Count.ToString(
        System.Globalization.CultureInfo.InvariantCulture) + ")";
}
